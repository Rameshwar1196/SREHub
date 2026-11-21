using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed class WorkItemSyncService(DailyPlanService plan, IWorkItemSource source, TimeProvider clock, ILogger<WorkItemSyncService> logger)
{
    private readonly SemaphoreSlim _running = new(1, 1);
    private DateTime? _syncedIteration;
    private DateTime? _syncedOn;

    public WorkItemSyncResult? Last { get; private set; }

    public IWorkItemSource Source => source;

    public DateTime Today => clock.GetLocalNow().Date;

    // The first sync of an iteration imports its work and the one on its last day closes out what finished; the Sync button works any day.
    public async Task<bool> IsDueAsync(CancellationToken cancellationToken)
    {
        if (!source.IsConfigured)
        {
            return false;
        }

        var window = await plan.GetWindowAsync(Today, cancellationToken);
        return _syncedIteration != window.Start || (Today == window.End && _syncedOn != Today);
    }

    public async Task<WorkItemSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        if (!source.IsConfigured)
        {
            return new WorkItemSyncResult { Error = $"{source.Name} sync is not set up. {source.SetupHint}" };
        }

        await _running.WaitAsync(cancellationToken);
        try
        {
            var today = Today;
            var window = await plan.GetWindowAsync(today, cancellationToken);
            var assigned = await source.GetAssignedAsync(window.Start, cancellationToken);

            var assignedIds = assigned.Select(item => item.Id).ToHashSet();
            var missing = (await plan.GetSyncedIdsAsync(source.States.Prefix, cancellationToken)).Where(id => !assignedIds.Contains(id)).ToList();
            var tracked = missing.Count > 0 ? await source.GetByIdsAsync(missing, cancellationToken) : [];

            Last = await plan.ApplyWorkItemsAsync(assigned, tracked, today, source.States, cancellationToken);
            _syncedIteration = window.Start;
            _syncedOn = today;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "{Source} sync failed", source.Name);
            Last = new WorkItemSyncResult { Configured = true, Error = $"{source.Name}: {exception.Message}" };
        }
        finally
        {
            _running.Release();
        }

        Last.RanAt = clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm");
        return Last;
    }
}

// Runs every set-up source and reports one combined result, so a failing source never blocks the others.
public sealed class WorkItemSyncHub
{
    private readonly IReadOnlyList<WorkItemSyncService> _services;

    public WorkItemSyncHub(IEnumerable<IWorkItemSource> sources, DailyPlanService plan, TimeProvider clock, ILoggerFactory loggers) =>
        _services = sources.Select(source => new WorkItemSyncService(plan, source, clock, loggers.CreateLogger<WorkItemSyncService>())).ToList();

    public bool IsConfigured => _services.Any(service => service.Source.IsConfigured);

    public int IntervalMinutes =>
        _services.Where(service => service.Source.IsConfigured && service.Source.SyncMinutes > 0).Select(service => service.Source.SyncMinutes).DefaultIfEmpty(0).Min();

    public WorkItemSyncResult Last => Combine(_services.Where(service => service.Last is not null).Select(service => service.Last!).ToList());

    public async Task<WorkItemSyncResult> SyncAsync(bool onlyDue, CancellationToken cancellationToken, string? only = null)
    {
        var results = new List<WorkItemSyncResult>();

        if (!string.IsNullOrWhiteSpace(only))
        {
            var chosen = _services.FirstOrDefault(service => service.Source.States.Prefix.TrimEnd(':').Equals(only, StringComparison.OrdinalIgnoreCase));
            return chosen is null
                ? new WorkItemSyncResult { Error = $"Unknown work item source '{only}'." }
                : await chosen.SyncAsync(cancellationToken);
        }

        foreach (var service in _services.Where(service => service.Source.IsConfigured))
        {
            if (!onlyDue || await service.IsDueAsync(cancellationToken))
            {
                results.Add(await service.SyncAsync(cancellationToken));
            }
        }

        if (results.Count == 0 && !onlyDue)
        {
            return new WorkItemSyncResult { Error = string.Join(" ", _services.Select(service => $"{service.Source.Name}: {service.Source.SetupHint}")) };
        }

        return Combine(results);
    }

    public static WorkItemSyncResult Combine(IReadOnlyList<WorkItemSyncResult> results) => new()
    {
        Configured = results.Count > 0,
        RanAt = results.Select(result => result.RanAt).DefaultIfEmpty(string.Empty).Max() ?? string.Empty,
        Added = results.Sum(result => result.Added),
        Updated = results.Sum(result => result.Updated),
        Removed = results.Sum(result => result.Removed),
        Skipped = results.Sum(result => result.Skipped),
        Error = string.Join(" | ", results.Select(result => result.Error).Where(error => error.Length > 0))
    };
}

public sealed class WorkItemSyncWorker(WorkItemSyncHub hub, ILogger<WorkItemSyncWorker> logger) : BackgroundService
{
    private const int MinimumMinutes = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (hub.IntervalMinutes <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(hub.IntervalMinutes, MinimumMinutes));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await hub.SyncAsync(onlyDue: true, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Checking whether a work item sync is due failed");
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
