using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed record IterationWindow(DateTime Start, DateTime End);

public sealed partial class DailyPlanService
{
    public const string ExternalPrefix = "ado:";

    public async Task<IterationWindow> GetWindowAsync(DateTime date, CancellationToken cancellationToken)
    {
        var window = WindowFor(date, (await LoadAsync(cancellationToken)).Iterations);
        return new IterationWindow(window.Start, window.End);
    }

    public async Task<IReadOnlyList<string>> GetSyncedIdsAsync(string prefix, CancellationToken cancellationToken)
    {
        var store = await LoadAsync(cancellationToken);

        return store.Iterations
            .SelectMany(iteration => iteration.Targets)
            .Where(target => target.ExternalId.StartsWith(prefix, StringComparison.Ordinal))
            .Select(target => target.ExternalId[prefix.Length..])
            .Distinct()
            .ToList();
    }

    // Work items assigned to the user become targets once; later runs only react to a state change upstream, so manual edits made here stay.
    public async Task<WorkItemSyncResult> ApplyWorkItemsAsync(
        IReadOnlyList<ExternalWorkItem> assigned,
        IReadOnlyList<ExternalWorkItem> tracked,
        DateTime today,
        IStateMapping options,
        CancellationToken cancellationToken)
    {
        var result = new WorkItemSyncResult { Configured = true };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);
            var existing = store.Iterations
                .SelectMany(iteration => iteration.Targets.Select(target => (Plan: iteration, Target: target)))
                .Where(entry => entry.Target.ExternalId.Length > 0)
                .GroupBy(entry => entry.Target.ExternalId)
                .ToDictionary(group => group.Key, group => group.First());

            var dismissed = store.DismissedExternalIds.ToHashSet();
            var assignedIds = assigned.Select(item => options.Prefix + item.Id).ToHashSet();
            var changed = false;

            // Targets imported before the Cancelled status, iteration alignment and ACF existed are re-read from their source once.
            if (store.SyncSchema < CurrentSyncSchema)
            {
                var firstRead = store.SyncSchema < 2;
                existing.Where(entry => firstRead || entry.Key.StartsWith("sre:", StringComparison.Ordinal))
                    .ToList().ForEach(entry => entry.Value.Target.ExternalState = string.Empty);
                store.SyncSchema = CurrentSyncSchema;
                changed = true;
            }

            foreach (var item in assigned.DistinctBy(item => item.Id))
            {
                var key = options.Prefix + item.Id;
                var category = Classify(item.State, options);

                if (dismissed.Contains(key) || category == WorkItemCategory.Removed)
                {
                    continue;
                }

                if (existing.TryGetValue(key, out var entry))
                {
                    var (dirty, statusChanged) = Refresh(entry.Target, item, category, today);
                    changed |= dirty;
                    result.Updated += statusChanged ? 1 : 0;

                    // Links built by the sync (they carry ?StartItem=) follow the source's current address; a link typed by hand is left alone.
                    if (item.Url.Length > 0 && entry.Target.Url != item.Url && entry.Target.Url.Contains("?StartItem=", StringComparison.Ordinal))
                    {
                        entry.Target.Url = item.Url;
                        changed = true;
                    }

                    if (Align(store, existing, key, item))
                    {
                        changed = true;
                    }

                    continue;
                }

                var plan = PlanFor(store, item.IterationStart ?? (category is WorkItemCategory.Closed or WorkItemCategory.Cancelled ? item.ClosedOn ?? today : today));

                if (plan.Targets.Count >= MaxTargets)
                {
                    result.Skipped++;
                    continue;
                }

                var target = NewTargetFrom(item, options.Prefix);
                Refresh(target, item, category, today);
                plan.Targets.Add(target);
                existing[key] = (plan, target);
                result.Added++;
                changed = true;
            }

            var upstream = tracked.ToDictionary(item => options.Prefix + item.Id);

            foreach (var (key, entry) in existing.Where(pair => pair.Key.StartsWith(options.Prefix, StringComparison.Ordinal) && !assignedIds.Contains(pair.Key)).ToList())
            {
                var found = upstream.TryGetValue(key, out var item);
                var category = found ? Classify(item!.State, options) : WorkItemCategory.Open;

                if (found && category is WorkItemCategory.Closed or WorkItemCategory.Cancelled)
                {
                    var (dirty, statusChanged) = Refresh(entry.Target, item!, category, today);
                    changed |= dirty;
                    result.Updated += statusChanged ? 1 : 0;
                    changed |= Align(store, existing, key, item!);
                }
                else if (IsUntouched(entry.Target))
                {
                    entry.Plan.Targets.Remove(entry.Target);
                    result.Removed++;
                    changed = true;
                }
            }

            if (changed)
            {
                await PersistAsync(store, cancellationToken);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private const int CurrentSyncSchema = 3;

    private enum WorkItemCategory { Open, Active, Acf, Closed, Cancelled, Removed }

    private static WorkItemCategory Classify(string state, IStateMapping options) =>
        options.Removed.Contains(state, StringComparer.OrdinalIgnoreCase) ? WorkItemCategory.Removed
        : options.Cancelled.Contains(state, StringComparer.OrdinalIgnoreCase) ? WorkItemCategory.Cancelled
        : options.Closed.Contains(state, StringComparer.OrdinalIgnoreCase) ? WorkItemCategory.Closed
        : options.Acf.Contains(state, StringComparer.OrdinalIgnoreCase) ? WorkItemCategory.Acf
        : options.Active.Contains(state, StringComparer.OrdinalIgnoreCase) ? WorkItemCategory.Active
        : WorkItemCategory.Open;

    private static IterationPlan PlanFor(DailyPlanStore store, DateTime date)
    {
        var window = WindowFor(date, store.Iterations);
        var plan = store.Iterations.FirstOrDefault(iteration => iteration.Start == Format(window.Start));

        if (plan is null)
        {
            plan = new IterationPlan { Start = Format(window.Start), End = Format(window.End) };
            store.Iterations.Add(plan);
        }

        return plan;
    }

    // A target follows its Azure DevOps iteration; one carried over on purpose stays where it was carried to.
    private static bool Align(DailyPlanStore store, Dictionary<string, (IterationPlan Plan, IterationTarget Target)> existing, string key, ExternalWorkItem item)
    {
        var (plan, target) = existing[key];
        var labelChanged = item.IterationPath.Length > 0 && target.ExternalIteration != item.IterationPath;

        if (labelChanged)
        {
            target.ExternalIteration = item.IterationPath;
        }

        if (item.IterationStart is not { } start || target.CarriedFrom.Length > 0 || target.Status == "Done")
        {
            return labelChanged;
        }

        var desired = PlanFor(store, start);

        if (desired == plan || desired.Targets.Count >= MaxTargets)
        {
            return labelChanged;
        }

        plan.Targets.Remove(target);
        desired.Targets.Add(target);
        existing[key] = (desired, target);
        return true;
    }

    private static bool IsUntouched(IterationTarget target) =>
        target.Status == "Todo" && target.SubTasks.Count == 0 && target.Outcome.Length == 0;

    private static IterationTarget NewTargetFrom(ExternalWorkItem item, string prefix)
    {
        var title = $"{(item.Id.Length > 0 && char.IsDigit(item.Id[0]) ? "#" : string.Empty)}{item.Id} {item.Title}".Trim();

        return new IterationTarget
        {
            Id = NewId(),
            Title = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title,
            Priority = item.Priority switch { 1 => "P1", 3 or 4 => "P3", _ => "P2" },
            Url = item.Url,
            ExternalId = prefix + item.Id,
            ExternalIteration = item.IterationPath
        };
    }

    private static (bool Dirty, bool StatusChanged) Refresh(IterationTarget target, ExternalWorkItem item, WorkItemCategory category, DateTime today)
    {
        if (target.ExternalState == item.State)
        {
            return (false, false);
        }

        var hadState = target.ExternalState.Length > 0;
        target.ExternalState = item.State;
        var before = target.Status;

        if (category == WorkItemCategory.Closed)
        {
            if (target.Status != "Done")
            {
                target.Status = "Done";
                target.SubTasks.ForEach(subTask => subTask.Done = true);
                target.CompletedOn = Format(item.ClosedOn ?? today);
            }
        }
        else if (category == WorkItemCategory.Cancelled)
        {
            if (target.Status is not ("Done" or "Cancelled"))
            {
                target.Status = "Cancelled";
            }
        }
        else if (category == WorkItemCategory.Acf)
        {
            // Waiting on the customer: not a target, so any planned days are cleared.
            var reopen = target.Status == "Done" && hadState;

            if (reopen)
            {
                target.CompletedOn = string.Empty;
                target.SubTasks.ForEach(subTask => subTask.Done = false);
            }

            if (reopen || target.Status is "Todo" or "InProgress" or "Cancelled")
            {
                target.Status = "ACF";
                target.PlannedDays = null;
            }
        }
        else if (target.Status == "Cancelled")
        {
            target.Status = category == WorkItemCategory.Active ? "InProgress" : "Todo";
        }
        else if (target.Status == "ACF" && hadState)
        {
            target.Status = category == WorkItemCategory.Active ? "InProgress" : "Todo";
        }
        else if (target.Status == "Done" && hadState)
        {
            target.Status = "InProgress";
            target.CompletedOn = string.Empty;
            target.SubTasks.ForEach(subTask => subTask.Done = false);
        }
        else if (category == WorkItemCategory.Active && target.Status == "Todo")
        {
            target.Status = "InProgress";
        }

        return (true, target.Status != before);
    }
}
