using System.Globalization;
using System.Text;
using System.Text.Json;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed class WorkLogService
{
    private const int MaxTextLength = 500;

    private static readonly string[] AttendanceValues = ["Yes", "Late", "No", "Leave", "Comp-off", "Holiday", "Weekend", "DO-wkd", "DO-wk"];
    private static readonly string[] EmergencyLeaveValues = ["No", "Yes", "Weekend", "N/A"];
    private static readonly string[] NetworkPowerValues = ["No", "Network issue", "Power outage", "Both", "N/A"];
    private static readonly string[] WorkingValues = ["Yes", "Late", "DO-wkd", "DO-wk"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorkLogService(IWebHostEnvironment environment)
    {
        _storePath = Path.Combine(environment.ContentRootPath, "App_Data", "worklog.json");
    }

    public async Task<WorkLogMonth> GetMonthAsync(string month, CancellationToken cancellationToken)
    {
        var start = ParseMonth(month);
        var prefix = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var entries = (await LoadAsync(cancellationToken))
            .Where(entry => entry.Date.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(entry => entry.Date, StringComparer.Ordinal)
            .ToList();

        return new WorkLogMonth { Month = prefix, Entries = entries, Summary = Summarize(entries) };
    }

    public async Task<WorkLogYear> GetYearAsync(int year, CancellationToken cancellationToken)
    {
        if (year < 2000 || year > 2100)
        {
            throw new ArgumentException("Year must be between 2000 and 2100.");
        }

        var prefix = year.ToString("0000", CultureInfo.InvariantCulture) + "-";
        var entries = (await LoadAsync(cancellationToken))
            .Where(entry => entry.Date.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        var months = Enumerable.Range(1, 12).Select(number =>
        {
            var month = prefix + number.ToString("00", CultureInfo.InvariantCulture);
            var inMonth = entries.Where(entry => entry.Date.StartsWith(month, StringComparison.Ordinal)).ToList();
            return new WorkLogMonth { Month = month, Summary = Summarize(inMonth) };
        }).ToList();

        return new WorkLogYear { Year = year, Entries = entries.OrderBy(entry => entry.Date, StringComparer.Ordinal).ToList(), Summary = Summarize(entries), Months = months };
    }

    public async Task<WorkLogEntry> SaveAsync(string date, WorkLogEntry incoming, CancellationToken cancellationToken)
    {
        var entry = Normalize(date, incoming);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAsync(cancellationToken);
            all.RemoveAll(existing => existing.Date == entry.Date);

            if (!entry.IsEmpty)
            {
                all.Add(entry);
            }

            await PersistAsync(all, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return entry;
    }

    // Period is a month (2026-10) or a whole year (2026).
    public async Task<string> ExportCsvAsync(string period, CancellationToken cancellationToken)
    {
        var prefix = period.Length == 4 && period.All(char.IsAsciiDigit)
            ? period
            : ParseMonth(period).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.AppendLine("Date,Attendance Status,Emergency Leave,Network / Power Status,Daily Work Summary,Escalation Status / Details,Note");

        var entries = (await LoadAsync(cancellationToken))
            .Where(entry => entry.Date.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(entry => entry.Date, StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                entry.Date, entry.Attendance, entry.EmergencyLeave, entry.NetworkPower, entry.Summary, entry.Escalation, entry.Note
            }.Select(CsvCell)));
        }

        return builder.ToString();
    }

    // Comp-off leave is recorded as Leave with "comp-off" in the work summary, so it moves between the two categories.
    internal static IReadOnlyList<WorkLogSummaryRow> Summarize(IReadOnlyCollection<WorkLogEntry> entries)
    {
        static bool IsCompOffLeave(WorkLogEntry entry) =>
            entry.Attendance == "Leave" && entry.Summary.Contains("comp-off", StringComparison.OrdinalIgnoreCase);

        var rows = new List<WorkLogSummaryRow>
        {
            new() { Category = "Working", Days = entries.Count(entry => WorkingValues.Contains(entry.Attendance)) },
            new() { Category = "Leave", Days = entries.Count(entry => entry.Attendance == "Leave" && !IsCompOffLeave(entry)) },
            new() { Category = "Comp-off", Days = entries.Count(entry => entry.Attendance == "Comp-off" || IsCompOffLeave(entry)) },
            new() { Category = "Holiday", Days = entries.Count(entry => entry.Attendance == "Holiday") },
            new() { Category = "Weekend", Days = entries.Count(entry => entry.Attendance == "Weekend") }
        };

        var total = rows.Sum(row => row.Days);
        foreach (var row in rows)
        {
            row.Percentage = total == 0 ? 0 : (double)row.Days / total;
        }

        return rows;
    }

    private static DateTime ParseMonth(string month)
    {
        if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            throw new ArgumentException("Month must look like 2026-10.");
        }

        return start;
    }

    private static WorkLogEntry Normalize(string date, WorkLogEntry incoming)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new ArgumentException("Date must look like 2026-10-12.");
        }

        return new WorkLogEntry
        {
            Date = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Attendance = Choice(incoming.Attendance, AttendanceValues, "Attendance status"),
            EmergencyLeave = Choice(incoming.EmergencyLeave, EmergencyLeaveValues, "Emergency leave"),
            NetworkPower = Choice(incoming.NetworkPower, NetworkPowerValues, "Network / power status"),
            Summary = Text(incoming.Summary, "Daily work summary"),
            Escalation = Text(incoming.Escalation, "Escalation"),
            Note = Text(incoming.Note, "Note")
        };
    }

    private static string Choice(string? value, string[] allowed, string label)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length > 0 && !allowed.Contains(trimmed))
        {
            throw new ArgumentException($"{label} must be one of: {string.Join(", ", allowed)}.");
        }

        return trimmed;
    }

    private static string Text(string? value, string label)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length > MaxTextLength)
        {
            throw new ArgumentException($"{label} is limited to {MaxTextLength} characters.");
        }

        return trimmed;
    }

    // Quoting plus a leading apostrophe stops spreadsheet apps from running cell content as a formula.
    internal static string CsvCell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private async Task<List<WorkLogEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<List<WorkLogEntry>>(stream, SerializerOptions, cancellationToken) ?? [];
    }

    private async Task PersistAsync(List<WorkLogEntry> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);

        var temporary = _storePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries.OrderBy(entry => entry.Date, StringComparer.Ordinal), SerializerOptions), cancellationToken);
        File.Move(temporary, _storePath, overwrite: true);
    }
}
