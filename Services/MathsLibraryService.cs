using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed partial class MathsLibraryService
{
    public static readonly string[] Statuses = ["To prepare", "Prepared", "Taught"];

    private const int MaxNoteBytes = 512 * 1024;
    private const string NotesFolder = "_notes";
    private const string BookFile = "_book.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Default chapter order; the teacher can override it by adding MATHS/_book.json.
    private static readonly Dictionary<string, string[]> DefaultBook = new()
    {
        ["1. Calculation foundations"] = ["Bramhastra Calculations", "Number System", "LCM & HCF", "Simplification", "Surds & Indices"],
        ["2. Arithmetic"] = ["Ratio", "Proportion", "Percentage", "Average", "Ages", "Profit & Loss", "Discount", "Mixture", "Alligation", "Partnership", "Simple Interest", "Compound Interest"],
        ["3. Time, speed and work"] = ["Time & Work", "Pipe & Cistern", "Work & Wages", "Time, Speed & Distance", "Train", "Boat & Stream", "Race", "Circular Motion"],
        ["4. Algebra"] = ["Algebra", "Quadratic Equation", "AP & GP", "Maxima & Minima"],
        ["5. Geometry and mensuration"] = ["Geometry", "Co-ordinate Geometry", "Mensuration 2D", "Mensuration 3D"],
        ["6. Trigonometry"] = ["Trigonometry", "Height & Distance"],
        ["7. Data and statistics"] = ["Statistics", "Probability", "Data Interpretation"]
    };

    private readonly string _root;
    private readonly object _gate = new();

    public MathsLibraryService(MathsOptions options)
    {
        _root = options.Root;
    }

    public string Root => _root;

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    public MathsLibraryResponse GetLibrary()
    {
        if (!Directory.Exists(_root))
        {
            return new MathsLibraryResponse(_root, false, [], 0);
        }

        var statuses = LoadStatuses();
        var topics = Directory.EnumerateDirectories(_root)
            .Where(folder => !Path.GetFileName(folder).StartsWith('_'))
            .Select(folder => BuildTopic(folder, statuses))
            .ToDictionary(topic => topic.Id, StringComparer.OrdinalIgnoreCase);

        var parts = new List<MathsPart>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (partName, topicNames) in LoadBook())
        {
            var inPart = topicNames
                .Where(name => topics.ContainsKey(name) && placed.Add(name))
                .Select(name => topics[name])
                .ToList();

            if (inPart.Count > 0)
            {
                parts.Add(new MathsPart(partName, inPart));
            }
        }

        var leftovers = topics.Values
            .Where(topic => !placed.Contains(topic.Id))
            .OrderBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (leftovers.Count > 0)
        {
            parts.Add(new MathsPart("More topics", leftovers));
        }

        return new MathsLibraryResponse(_root, true, parts, topics.Values.Sum(topic => topic.Materials.Count));
    }

    public string ResolveFile(string topic, string name)
    {
        var folder = ResolveTopicFolder(topic);
        var file = Directory.EnumerateFiles(folder, "*.pdf")
            .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("That file does not exist in this topic.");

        return file;
    }

    public MathsBookResponse GetBook()
    {
        var library = GetLibrary();

        var parts = library.Parts
            .Select(part => new MathsBookPart(
                part.Name,
                part.Topics.Select(topic => new MathsBookTopic(
                    topic.Id,
                    topic.Name,
                    topic.Status,
                    GetNote(topic.Id).Content,
                    topic.Materials)).ToList()))
            .ToList();

        return new MathsBookResponse(library.Exists, parts);
    }

    public MathsNote GetNote(string topic)
    {
        var path = NotePath(ResolveTopicFolder(topic));

        return File.Exists(path)
            ? new MathsNote(File.ReadAllText(path, Encoding.UTF8), new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero))
            : new MathsNote(string.Empty, null);
    }

    public MathsNote SaveNote(string topic, string content)
    {
        var path = NotePath(ResolveTopicFolder(topic));

        if (Encoding.UTF8.GetByteCount(content) > MaxNoteBytes)
        {
            throw new ArgumentException("Notes are limited to 512 KB.");
        }

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (content.Trim().Length == 0)
            {
                File.Delete(path);
                return new MathsNote(string.Empty, null);
            }

            WriteAtomically(path, content);
        }

        return new MathsNote(content, DateTimeOffset.UtcNow);
    }

    public string SaveStatus(string topic, string status)
    {
        var folder = ResolveTopicFolder(topic);
        var match = Statuses.FirstOrDefault(known => string.Equals(known, status, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Status must be one of: {string.Join(", ", Statuses)}.");

        lock (_gate)
        {
            var statuses = LoadStatuses();
            statuses[Path.GetFileName(folder)] = match;

            Directory.CreateDirectory(Path.Combine(_root, NotesFolder));
            WriteAtomically(StatusPath(), JsonSerializer.Serialize(statuses, JsonOptions));
        }

        return match;
    }

    private MathsTopic BuildTopic(string folder, Dictionary<string, string> statuses)
    {
        var id = Path.GetFileName(folder);

        var materials = Directory.EnumerateFiles(folder, "*.pdf")
            .Select(file =>
            {
                var name = Path.GetFileName(file);
                var (kind, order) = Classify(name);
                return new MathsMaterial(name, CleanTitle(name), kind, order, Math.Round(new FileInfo(file).Length / 1048576d, 1));
            })
            .OrderBy(material => material.KindOrder)
            .ThenBy(material => NaturalKey(material.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MathsTopic(
            id,
            id,
            statuses.GetValueOrDefault(id, Statuses[0]),
            File.Exists(NotePath(folder)),
            materials);
    }

    private static (string Kind, int Order) Classify(string fileName)
    {
        var name = fileName.ToLowerInvariant();

        if (name.Contains("theory")) return ("Theory", 0);
        if (name.Contains("class notes")) return ("Class notes", 1);
        if (name.Contains("classroom")) return ("Classroom sheet", 3);
        if (name.Contains("pro level")) return ("Pro level sheet", 4);
        if (name.Contains("typewise")) return ("Typewise practice", 5);
        if (name.StartsWith("calculation")) return ("Technique", 2);

        return ("Other", 6);
    }

    // Display-only tidy-up; the files on disk are never renamed.
    private static string CleanTitle(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName)
            .Replace("Divisivility", "Divisibility", StringComparison.OrdinalIgnoreCase);

        return WhitespacePattern().Replace(title, " ").Trim();
    }

    private static string NaturalKey(string value) =>
        DigitsPattern().Replace(value, match => match.Value.PadLeft(8, '0'));

    private IEnumerable<KeyValuePair<string, string[]>> LoadBook()
    {
        var custom = Path.Combine(_root, BookFile);

        if (File.Exists(custom))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(custom, Encoding.UTF8), JsonOptions);

                if (parsed is { Count: > 0 })
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // A broken custom file falls back to the default order.
            }
        }

        return DefaultBook;
    }

    private Dictionary<string, string> LoadStatuses()
    {
        var path = StatusPath();

        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            return new Dictionary<string, string>(stored ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private string ResolveTopicFolder(string topic)
    {
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException("The maths folder was not found.");
        }

        return Directory.EnumerateDirectories(_root)
            .FirstOrDefault(folder => !Path.GetFileName(folder).StartsWith('_')
                && string.Equals(Path.GetFileName(folder), topic, StringComparison.OrdinalIgnoreCase))
            ?? throw new DirectoryNotFoundException("That topic does not exist.");
    }

    private string NotePath(string topicFolder) => Path.Combine(_root, NotesFolder, Path.GetFileName(topicFolder) + ".md");

    private string StatusPath() => Path.Combine(_root, NotesFolder, "progress.json");

    private static void WriteAtomically(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
