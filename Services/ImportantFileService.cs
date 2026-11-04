using System.Text.RegularExpressions;

namespace Aras.SreHub.Services;

public sealed record ImportantFileInfo(string Name, string Kind, long Size, DateTimeOffset UpdatedAt);

public sealed partial class ImportantFileService
{
    private static readonly Dictionary<string, (string Kind, string ContentType)> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("pdf", "application/pdf"),
        [".docx"] = ("word", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        [".doc"] = ("legacy", "application/msword"),
        [".xlsx"] = ("excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        [".xlsm"] = ("excel", "application/vnd.ms-excel.sheet.macroEnabled.12"),
        [".xls"] = ("excel", "application/vnd.ms-excel"),
        [".pptx"] = ("powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        [".ppt"] = ("legacy", "application/vnd.ms-powerpoint")
    };

    private readonly string _root;

    public ImportantFileService(IWebHostEnvironment environment)
    {
        _root = Path.Combine(environment.ContentRootPath, "App_Data", "important-files");
    }

    public IReadOnlyList<ImportantFileInfo> List()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        return new DirectoryInfo(_root).EnumerateFiles()
            .Where(file => Types.ContainsKey(file.Extension))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new ImportantFileInfo(file.Name, Types[file.Extension].Kind, file.Length, file.LastWriteTimeUtc))
            .ToList();
    }

    public (string Path, string ContentType) Resolve(string name)
    {
        var path = ResolvePath(name);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"'{name}' was not found.");
        }

        return (path, Types[Path.GetExtension(path)].ContentType);
    }

    public async Task<ImportantFileInfo> SaveAsync(string name, Stream content, CancellationToken cancellationToken)
    {
        var path = ResolvePath(name);
        Directory.CreateDirectory(_root);

        var temporary = path + ".tmp";
        await using (var target = File.Create(temporary))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        File.Move(temporary, path, overwrite: true);

        var info = new FileInfo(path);
        return new ImportantFileInfo(info.Name, Types[info.Extension].Kind, info.Length, info.LastWriteTimeUtc);
    }

    public void Delete(string name)
    {
        var path = ResolvePath(name);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"'{name}' was not found.");
        }

        File.Delete(path);
    }

    private string ResolvePath(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed != Path.GetFileName(trimmed) || trimmed.Length > 150 || !NameRegex().IsMatch(trimmed) || ReservedRegex().IsMatch(trimmed))
        {
            throw new InvalidOperationException("That file name is not allowed. Avoid \\ / : * ? \" < > | and names such as CON or NUL.");
        }

        if (!Types.ContainsKey(Path.GetExtension(trimmed)))
        {
            throw new InvalidOperationException($"File type '{Path.GetExtension(trimmed)}' is not allowed. Use {string.Join(", ", Types.Keys)}.");
        }

        return Path.Combine(_root, trimmed);
    }

    [GeneratedRegex(@"^[^.\s\\/:*?""<>|\x00-\x1f][^\\/:*?""<>|\x00-\x1f]*[^.\s\\/:*?""<>|\x00-\x1f]$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedRegex();
}
