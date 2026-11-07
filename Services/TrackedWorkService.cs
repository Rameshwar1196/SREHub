using System.Text.Json;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed class TrackedWorkService
{
    public static readonly string[] Kinds = ["Ticket", "KB"];
    public static readonly string[] Quarters = ["Q1", "Q2", "Q3", "Q4"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TrackedWorkService(IWebHostEnvironment environment)
    {
        _storePath = Path.Combine(environment.ContentRootPath, "App_Data", "tracked-work.json");
    }

    public async Task<IReadOnlyList<TrackedWorkItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        var items = await LoadAsync(cancellationToken);
        return items.OrderByDescending(item => item.Year)
            .ThenByDescending(item => item.Quarter, StringComparer.Ordinal)
            .ThenBy(item => item.Iteration, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<TrackedWorkItem> SaveAsync(string? id, TrackedWorkItem incoming, CancellationToken cancellationToken)
    {
        var item = Normalize(incoming);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(id))
            {
                item.Id = Guid.NewGuid().ToString("N");
                items.Add(item);
            }
            else
            {
                var index = items.FindIndex(existing => existing.Id == id);

                if (index < 0)
                {
                    throw new FileNotFoundException("That entry no longer exists.");
                }

                item.Id = id;
                items[index] = item;
            }

            await PersistAsync(items, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TrackedWorkImportResult> ImportAsync(IEnumerable<TrackedWorkItem> incoming, CancellationToken cancellationToken)
    {
        var normalized = incoming.Select(Normalize).ToList();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadAsync(cancellationToken);
            var known = items.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;

            foreach (var item in normalized)
            {
                if (known.Add(Key(item)))
                {
                    item.Id = Guid.NewGuid().ToString("N");
                    items.Add(item);
                    added++;
                }
            }

            if (added > 0)
            {
                await PersistAsync(items, cancellationToken);
            }

            return new TrackedWorkImportResult(added, normalized.Count - added);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadAsync(cancellationToken);

            if (items.RemoveAll(existing => existing.Id == id) == 0)
            {
                throw new FileNotFoundException("That entry no longer exists.");
            }

            await PersistAsync(items, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Key(TrackedWorkItem item) => $"{item.Kind}|{item.Year}|{item.Quarter}|{item.Iteration}|{item.Title}";

    internal static TrackedWorkItem Normalize(TrackedWorkItem incoming)
    {
        var title = Limit(incoming.Title, 200, "Title");

        if (title.Length == 0)
        {
            throw new ArgumentException("A title is required.");
        }

        var kind = Kinds.FirstOrDefault(known => string.Equals(known, incoming.Kind?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Type must be Ticket or KB.");
        var quarter = Quarters.FirstOrDefault(known => string.Equals(known, incoming.Quarter?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Quarter must be Q1, Q2, Q3 or Q4.");
        var iteration = (incoming.Iteration ?? string.Empty).Trim().ToUpperInvariant();

        if (iteration.Length > 0 && !(iteration.Length == 2 && iteration[0] == 'I' && char.IsAsciiDigit(iteration[1])))
        {
            throw new ArgumentException("Iteration must look like I1 or I2.");
        }

        if (incoming.Year is < 2000 or > 2100)
        {
            throw new ArgumentException("Year must be between 2000 and 2100.");
        }

        var url = Limit(incoming.Url, 2000, "Link");

        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            throw new ArgumentException("The link must start with http:// or https://.");
        }

        return new TrackedWorkItem
        {
            Kind = kind,
            Title = title,
            Url = url,
            Category = Limit(incoming.Category, 100, "Category"),
            Year = incoming.Year,
            Quarter = quarter,
            Iteration = iteration,
            Owner = Limit(incoming.Owner, 100, "Owner"),
            Comment = Limit(incoming.Comment, 1000, "Comment")
        };
    }

    private static string Limit(string? value, int maxLength, string label)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{label} is limited to {maxLength} characters.");
        }

        return trimmed;
    }

    private async Task<List<TrackedWorkItem>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<List<TrackedWorkItem>>(stream, SerializerOptions, cancellationToken) ?? [];
    }

    private async Task PersistAsync(List<TrackedWorkItem> items, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);

        var temporary = _storePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(items, SerializerOptions), cancellationToken);
        File.Move(temporary, _storePath, overwrite: true);
    }
}
