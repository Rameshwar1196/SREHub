using System.Globalization;
using System.Text;
using System.Text.Json;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed class DutyLogService
{
    private const int MaxTextLength = 500;
    private const int MaxRosterBlocks = 500;
    private const int MaxNames = 100;
    private const int MaxDailyBackups = 60;

    private static readonly string[] HandoverValues = ["Given", "Received", "Pending", "N/A"];
    private static readonly string[] OneNoteValues = ["Updated", "Pending", "N/A"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storePath;
    private readonly string _rosterPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DutyLogService(IWebHostEnvironment environment)
    {
        _storePath = Path.Combine(environment.ContentRootPath, "App_Data", "dutylog.json");
        _rosterPath = Path.Combine(environment.ContentRootPath, "App_Data", "dutyroster.json");
    }

    public async Task<DutyYear> GetYearAsync(int year, CancellationToken cancellationToken)
    {
        if (year < 2000 || year > 2100)
        {
            throw new ArgumentException("Year must be between 2000 and 2100.");
        }

        var prefix = year.ToString("0000", CultureInfo.InvariantCulture) + "-";
        var entries = (await LoadAsync(cancellationToken))
            .Where(entry => entry.Date.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(entry => entry.Date, StringComparer.Ordinal)
            .ToList();

        return new DutyYear { Year = year, Entries = entries };
    }

    public async Task<DutyEntry> SaveAsync(string date, DutyEntry incoming, CancellationToken cancellationToken)
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

    public async Task<DutyRoster> GetRosterAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_rosterPath))
        {
            return new DutyRoster();
        }

        await using var stream = File.OpenRead(_rosterPath);
        return await JsonSerializer.DeserializeAsync<DutyRoster>(stream, SerializerOptions, cancellationToken) ?? new DutyRoster();
    }

    public async Task<DutyRoster> SaveRosterAsync(DutyRoster incoming, CancellationToken cancellationToken)
    {
        var roster = NormalizeRoster(incoming);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_rosterPath)!);

            var temporary = _rosterPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(roster, SerializerOptions), cancellationToken);
            Backup(_rosterPath);
            File.Move(temporary, _rosterPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }

        return roster;
    }

    // From and To are inclusive dates (yyyy-MM-dd).
    public async Task<string> ExportCsvAsync(string from, string to, CancellationToken cancellationToken)
    {
        var start = ParseDay(from).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = ParseDay(to).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.AppendLine("Date,DO Shift,Shift Time,Handover,DO Note");

        var entries = (await LoadAsync(cancellationToken))
            .Where(entry => string.CompareOrdinal(entry.Date, start) >= 0 && string.CompareOrdinal(entry.Date, end) <= 0)
            .OrderBy(entry => entry.Date, StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                entry.Date, entry.DoShift, entry.ShiftTime, entry.Handover, entry.DoNote
            }.Select(WorkLogService.CsvCell)));
        }

        return builder.ToString();
    }

    // Iterations that overlap the period, with the update status of each.
    public async Task<string> ExportOneNoteCsvAsync(string from, string to, CancellationToken cancellationToken)
    {
        var start = ParseDay(from).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = ParseDay(to).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.AppendLine("Iteration,From,To,Owner,Owner Name,Status,Page / Link,Note");

        var roster = await GetRosterAsync(cancellationToken);

        foreach (var item in roster.Iterations.Where(item => string.CompareOrdinal(item.End, start) >= 0 && string.CompareOrdinal(item.Start, end) <= 0).OrderBy(item => item.Start, StringComparer.Ordinal))
        {
            roster.Settings.Names.TryGetValue(item.Owner, out var ownerName);

            builder.AppendLine(string.Join(',', new[]
            {
                item.Label, item.Start, item.End, item.Owner, ownerName ?? string.Empty, item.Status, item.Page, item.Note
            }.Select(WorkLogService.CsvCell)));
        }

        return builder.ToString();
    }

    private static DutyRoster NormalizeRoster(DutyRoster incoming)
    {
        if (incoming.Blocks.Count > MaxRosterBlocks || incoming.Iterations.Count > MaxRosterBlocks)
        {
            throw new ArgumentException($"The roster is limited to {MaxRosterBlocks} blocks and {MaxRosterBlocks} iterations.");
        }

        var blocks = new List<DutyRosterBlock>();

        foreach (var block in incoming.Blocks)
        {
            var start = ParseDay(block.Start);
            var end = ParseDay(block.End);

            if (end < start)
            {
                throw new ArgumentException("A roster block ends before it starts.");
            }

            blocks.Add(new DutyRosterBlock
            {
                Start = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                End = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Label = Text(block.Label, "Label"),
                Japan = Text(block.Japan, "Japan DO"),
                India = Text(block.India, "India DO")
            });
        }

        var iterations = new List<OneNoteIteration>();

        foreach (var item in incoming.Iterations)
        {
            var start = ParseDay(item.Start);
            var end = ParseDay(item.End);

            if (end < start)
            {
                throw new ArgumentException("A OneNote iteration ends before it starts.");
            }

            iterations.Add(new OneNoteIteration
            {
                Start = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                End = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Label = Text(item.Label, "Iteration label"),
                Owner = Text(item.Owner, "OneNote owner"),
                Status = Choice(item.Status, OneNoteValues, "OneNote status"),
                Page = Text(item.Page, "OneNote page"),
                Note = Text(item.Note, "OneNote note")
            });
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, name) in incoming.Settings.Names.Take(MaxNames))
        {
            var key = Text(id, "DO id");
            var value = Text(name, "DO name");

            if (key.Length > 0 && value.Length > 0)
            {
                names[key] = value;
            }
        }

        return new DutyRoster
        {
            Settings = new DutySettings
            {
                MyDoId = Text(incoming.Settings.MyDoId, "My DO id"),
                MyItId = Text(incoming.Settings.MyItId, "My OneNote id"),
                Names = names
            },
            Blocks = blocks.OrderBy(block => block.Start, StringComparer.Ordinal).ToList(),
            Iterations = iterations.OrderBy(item => item.Start, StringComparer.Ordinal).ToList()
        };
    }

    private static DateTime ParseDay(string value)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new ArgumentException("Dates must look like 2026-10-12.");
        }

        return parsed;
    }

    private static DutyEntry Normalize(string date, DutyEntry incoming)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new ArgumentException("Date must look like 2026-10-12.");
        }

        return new DutyEntry
        {
            Date = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DoShift = Text(incoming.DoShift, "DO shift"),
            ShiftTime = Text(incoming.ShiftTime, "Shift time"),
            Handover = Choice(incoming.Handover, HandoverValues, "Handover"),
            DoNote = Text(incoming.DoNote, "DO note")
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

    private async Task<List<DutyEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<List<DutyEntry>>(stream, SerializerOptions, cancellationToken) ?? [];
    }

    // Keeps the previous version next to the file and the first version of each day under App_Data/backups.
    private static void Backup(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var directory = Path.Combine(Path.GetDirectoryName(path)!, "backups");
        Directory.CreateDirectory(directory);

        var stem = Path.GetFileNameWithoutExtension(path);
        var daily = Path.Combine(directory, $"{stem}-{DateTime.Now:yyyyMMdd}.json");

        if (!File.Exists(daily))
        {
            File.Copy(path, daily);
        }

        File.Copy(path, path + ".bak", overwrite: true);

        foreach (var old in Directory.GetFiles(directory, $"{stem}-*.json").OrderByDescending(name => name, StringComparer.Ordinal).Skip(MaxDailyBackups))
        {
            File.Delete(old);
        }
    }

    private async Task PersistAsync(List<DutyEntry> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);

        var temporary = _storePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries.OrderBy(entry => entry.Date, StringComparer.Ordinal), SerializerOptions), cancellationToken);
        Backup(_storePath);
        File.Move(temporary, _storePath, overwrite: true);
    }
}
