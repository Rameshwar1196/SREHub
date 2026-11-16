using System.Globalization;
using System.Text;
using System.Text.Json;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed class DailyPlanService
{
    public static readonly string[] Priorities = ["P1", "P2", "P3"];
    public static readonly string[] Statuses = ["Todo", "InProgress", "Done", "Blocked", "Carried"];
    public static readonly double[] PlannedDayValues = [0.5, 1, 2, 3, 4, 5];

    private const int IterationDays = 14;
    private const int CarryOverWindowDays = 60;
    private const int MaxTargets = 50;
    private const int MaxSubTasks = 20;
    private const int MaxTitleLength = 200;
    private const int MaxTextLength = 500;
    private const int MaxReflectionLength = 1000;
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storePath;
    private readonly WorkLogService _workLog;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DailyPlanService(IWebHostEnvironment environment, WorkLogService workLog)
    {
        _storePath = Path.Combine(environment.ContentRootPath, "App_Data", "dailyplan.json");
        _workLog = workLog;
    }

    public async Task<DailyPlanView> GetAsync(string date, CancellationToken cancellationToken)
    {
        var parsed = ParseDate(date);
        return BuildView(await LoadAsync(cancellationToken), parsed);
    }

    public async Task<DailyPlanView> SaveAsync(string date, DailyPlanSave incoming, CancellationToken cancellationToken)
    {
        var parsed = ParseDate(date);
        var day = NormalizeDay(Format(parsed), incoming.Day ?? new DailyPlanDay());
        var targets = NormalizeTargets(incoming.Targets ?? [], day.Date);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);
            var window = WindowFor(parsed, store.Iterations);

            store.Days.RemoveAll(existing => existing.Date == day.Date);
            if (!day.IsEmpty)
            {
                store.Days.Add(day);
            }

            var stored = store.Iterations.FirstOrDefault(existing => existing.Start == Format(window.Start));
            if (stored is not null)
            {
                stored.Targets = targets;
            }
            else if (targets.Count > 0)
            {
                store.Iterations.Add(new IterationPlan { Start = Format(window.Start), End = Format(window.End), Targets = targets });
            }

            await PersistAsync(store, cancellationToken);
            return BuildView(store, parsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DailyPlanView> SetIterationStartAsync(string date, string start, CancellationToken cancellationToken)
    {
        var parsed = ParseDate(date);
        var newStart = ParseDate(start);
        var newEnd = newStart.AddDays(IterationDays - 1);

        if (newStart > parsed || newEnd < parsed)
        {
            throw new ArgumentException("The iteration must still contain the day you are viewing.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);
            var window = WindowFor(parsed, store.Iterations);
            var current = store.Iterations.FirstOrDefault(existing => existing.Start == Format(window.Start));

            var clash = store.Iterations.FirstOrDefault(other =>
                other != current && ParseDate(other.Start) <= newEnd && ParseDate(other.End) >= newStart);

            if (clash is not null)
            {
                throw new ArgumentException($"That start date would overlap the iteration starting {clash.Start}.");
            }

            if (current is null)
            {
                current = new IterationPlan();
                store.Iterations.Add(current);
            }

            current.Start = Format(newStart);
            current.End = Format(newEnd);

            await PersistAsync(store, cancellationToken);
            return BuildView(store, parsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Open targets from earlier iterations are copied here and marked Carried there, so a second call finds nothing left to carry.
    public async Task<DailyPlanView> CarryOverAsync(string date, CancellationToken cancellationToken)
    {
        var parsed = ParseDate(date);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);
            var window = WindowFor(parsed, store.Iterations);
            var start = Format(window.Start);
            var target = store.Iterations.FirstOrDefault(existing => existing.Start == start)
                ?? new IterationPlan { Start = start, End = Format(window.End) };

            foreach (var source in EarlierIterations(store, window.Start))
            {
                foreach (var item in source.Targets.Where(IsOpen).ToList())
                {
                    if (target.Targets.Count >= MaxTargets)
                    {
                        throw new ArgumentException($"An iteration is limited to {MaxTargets} targets.");
                    }

                    target.Targets.Add(new IterationTarget
                    {
                        Id = NewId(),
                        Title = item.Title,
                        Priority = item.Priority,
                        Status = item.Status,
                        PlannedDays = item.PlannedDays,
                        Url = item.Url,
                        CarriedFrom = item.CarriedFrom.Length > 0 ? item.CarriedFrom : source.Start,
                        SubTasks = item.SubTasks.Select(subTask => new SubTask { Id = NewId(), Title = subTask.Title, Done = subTask.Done }).ToList()
                    });
                    item.Status = "Carried";
                }
            }

            if (target.Targets.Count > 0 && !store.Iterations.Contains(target))
            {
                store.Iterations.Add(target);
            }

            await PersistAsync(store, cancellationToken);
            return BuildView(store, parsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ExportCsvAsync(string date, CancellationToken cancellationToken)
    {
        var view = BuildView(await LoadAsync(cancellationToken), ParseDate(date));

        var builder = new StringBuilder();
        builder.AppendLine("Iteration Start,Priority,Target,Status,Percent,Planned Days,Sub Task,Sub Task Done,Outcome,Completed On,Carried From,Link");

        foreach (var target in view.Iteration.Targets)
        {
            AppendRow(builder, view.Iteration.Start, target, target.Priority, string.Empty, string.Empty);

            foreach (var subTask in target.SubTasks)
            {
                AppendRow(builder, view.Iteration.Start, target, string.Empty, subTask.Title, subTask.Done ? "Yes" : "No");
            }
        }

        return builder.ToString();
    }

    // Replaces only the summary; attendance and other fields already logged for the day are kept.
    public async Task<WorkLogEntry> PushToWorkLogAsync(string date, CancellationToken cancellationToken)
    {
        var parsed = ParseDate(date);
        var key = Format(parsed);
        var day = (await LoadAsync(cancellationToken)).Days.FirstOrDefault(existing => existing.Date == key);

        var lines = (day?.EndOfDay ?? string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart('-', '*', '\u2022', ' '))
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            throw new ArgumentException("Fill in \"What I achieved\" (or mark a target Done) before pushing to the work log.");
        }

        var summary = string.Join("; ", lines);

        if (summary.Length > MaxTextLength)
        {
            summary = summary[..(MaxTextLength - 3)] + "...";
        }

        var month = await _workLog.GetMonthAsync(parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture), cancellationToken);
        var existing = month.Entries.FirstOrDefault(entry => entry.Date == key);

        var entry = new WorkLogEntry
        {
            Date = key,
            Attendance = existing?.Attendance.Length > 0 ? existing.Attendance : "Yes",
            EmergencyLeave = existing?.EmergencyLeave.Length > 0 ? existing.EmergencyLeave : "No",
            NetworkPower = existing?.NetworkPower.Length > 0 ? existing.NetworkPower : "No",
            Summary = summary,
            Escalation = existing?.Escalation ?? string.Empty,
            Note = existing?.Note ?? string.Empty
        };

        return await _workLog.SaveAsync(key, entry, cancellationToken);
    }

    private static void AppendRow(StringBuilder builder, string start, IterationTarget target, string priority, string subTask, string subTaskDone)
    {
        builder.AppendLine(string.Join(',', new[]
        {
            start, priority, target.Title, target.Status, target.Percent.ToString(CultureInfo.InvariantCulture),
            target.PlannedDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            subTask, subTaskDone, target.Outcome, target.CompletedOn, target.CarriedFrom, target.Url
        }.Select(WorkLogService.CsvCell)));
    }

    private static DailyPlanView BuildView(DailyPlanStore store, DateTime date)
    {
        var window = WindowFor(date, store.Iterations);
        var start = Format(window.Start);

        return new DailyPlanView
        {
            Day = store.Days.FirstOrDefault(day => day.Date == Format(date)) ?? new DailyPlanDay { Date = Format(date) },
            Iteration = store.Iterations.FirstOrDefault(iteration => iteration.Start == start)
                ?? new IterationPlan { Start = start, End = Format(window.End) },
            CarryOverCount = EarlierIterations(store, window.Start).SelectMany(iteration => iteration.Targets).Count(IsOpen)
        };
    }

    // Windows are 14-day steps from the nearest stored iteration; with none stored the first one starts on that week's Monday.
    private static (DateTime Start, DateTime End) WindowFor(DateTime date, List<IterationPlan> iterations)
    {
        var starts = iterations.Select(iteration => ParseDate(iteration.Start)).OrderBy(start => start).ToList();
        var anchor = starts.Where(start => start <= date).Cast<DateTime?>().LastOrDefault() ?? starts.Cast<DateTime?>().FirstOrDefault();

        var startOfWindow = anchor is { } known
            ? known.AddDays((int)Math.Floor((date - known).TotalDays / IterationDays) * IterationDays)
            : date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

        var end = startOfWindow.AddDays(IterationDays - 1);
        var next = starts.Where(start => start > startOfWindow).Cast<DateTime?>().FirstOrDefault();

        return (startOfWindow, next is { } upcoming && upcoming <= end ? upcoming.AddDays(-1) : end);
    }

    private static IEnumerable<IterationPlan> EarlierIterations(DailyPlanStore store, DateTime start)
    {
        var earliest = start.AddDays(-CarryOverWindowDays);

        return store.Iterations
            .Where(iteration => ParseDate(iteration.End) < start && ParseDate(iteration.Start) >= earliest)
            .OrderBy(iteration => iteration.Start, StringComparer.Ordinal);
    }

    private static bool IsOpen(IterationTarget target) => target.Status != "Done" && target.Status != "Carried";

    private static DateTime ParseDate(string date)
    {
        if (!DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new ArgumentException("Date must look like 2026-10-12.");
        }

        return parsed;
    }

    private static string Format(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static DailyPlanDay NormalizeDay(string date, DailyPlanDay incoming) => new()
    {
        Date = date,
        Focus = Text(incoming.Focus, MaxTitleLength, "Focus"),
        EndOfDay = Text(incoming.EndOfDay, MaxReflectionLength, "What I achieved"),
        Blockers = Text(incoming.Blockers, MaxReflectionLength, "Blockers"),
        Tomorrow = Text(incoming.Tomorrow, MaxReflectionLength, "Tomorrow")
    };

    private static List<IterationTarget> NormalizeTargets(List<IterationTarget> incoming, string date)
    {
        var targets = incoming
            .Where(target => !string.IsNullOrWhiteSpace(target.Title))
            .Select(target => NormalizeTarget(target, date))
            .ToList();

        if (targets.Count > MaxTargets)
        {
            throw new ArgumentException($"An iteration is limited to {MaxTargets} targets.");
        }

        return targets;
    }

    private static IterationTarget NormalizeTarget(IterationTarget target, string date)
    {
        if (target.PlannedDays is { } days && !PlannedDayValues.Contains(days))
        {
            throw new ArgumentException($"Planned days must be one of: {string.Join(", ", PlannedDayValues)}.");
        }

        var url = Text(target.Url, MaxTextLength, "Link");

        if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("Link must start with http:// or https://.");
        }

        var subTasks = (target.SubTasks ?? [])
            .Where(subTask => !string.IsNullOrWhiteSpace(subTask.Title))
            .Select(subTask => new SubTask { Id = ValidId(subTask.Id), Title = Text(subTask.Title, MaxTitleLength, "Sub task"), Done = subTask.Done })
            .ToList();

        if (subTasks.Count > MaxSubTasks)
        {
            throw new ArgumentException($"A target is limited to {MaxSubTasks} sub tasks.");
        }

        var status = Choice(target.Status, Statuses, "Todo", "Status");

        if (status == "Done")
        {
            subTasks.ForEach(subTask => subTask.Done = true);
        }
        else if (status != "Carried" && subTasks.Count > 0 && subTasks.All(subTask => subTask.Done))
        {
            status = "Done";
        }
        else if (status == "Todo" && subTasks.Any(subTask => subTask.Done))
        {
            status = "InProgress";
        }

        var completedOn = status != "Done" ? string.Empty
            : DateTime.TryParseExact(target.CompletedOn, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? target.CompletedOn : date;

        return new IterationTarget
        {
            Id = ValidId(target.Id),
            Title = Text(target.Title, MaxTitleLength, "Target"),
            Priority = Choice(target.Priority, Priorities, "P2", "Priority"),
            Status = status,
            PlannedDays = target.PlannedDays,
            Url = url,
            Outcome = Text(target.Outcome, MaxTextLength, "Outcome"),
            CompletedOn = completedOn,
            CarriedFrom = DateTime.TryParseExact(target.CarriedFrom, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? target.CarriedFrom : string.Empty,
            SubTasks = subTasks
        };
    }

    private static string ValidId(string? id)
    {
        var trimmed = (id ?? string.Empty).Trim();
        return trimmed.Length is > 0 and <= 64 && trimmed.All(char.IsAsciiLetterOrDigit) ? trimmed : NewId();
    }

    private static string Choice(string? value, string[] allowed, string fallback, string label)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return fallback;
        }

        if (!allowed.Contains(trimmed))
        {
            throw new ArgumentException($"{label} must be one of: {string.Join(", ", allowed)}.");
        }

        return trimmed;
    }

    private static string Text(string? value, int maxLength, string label)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{label} is limited to {maxLength} characters.");
        }

        return trimmed;
    }

    private async Task<DailyPlanStore> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return new DailyPlanStore();
        }

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<DailyPlanStore>(stream, SerializerOptions, cancellationToken) ?? new DailyPlanStore();
    }

    private async Task PersistAsync(DailyPlanStore store, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);

        var temporary = _storePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(store, SerializerOptions), cancellationToken);
        File.Move(temporary, _storePath, overwrite: true);
    }
}
