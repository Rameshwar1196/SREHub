using System.IO.Compression;
using System.Text;
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
        [".ppt"] = ("legacy", "application/vnd.ms-powerpoint"),
        [".txt"] = ("text", "text/plain; charset=utf-8"),
        [".md"] = ("text", "text/markdown; charset=utf-8")
    };

    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

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

    public async Task<ImportantFileInfo> CreateBlankAsync(string name, CancellationToken cancellationToken)
    {
        var path = ResolvePath(name);

        if (File.Exists(path))
        {
            throw new InvalidOperationException($"'{Path.GetFileName(path)}' already exists. Choose another name.");
        }

        var content = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" or ".md" => Array.Empty<byte>(),
            ".docx" => BlankDocx(),
            ".xlsx" => BlankXlsx(),
            _ => throw new InvalidOperationException("Only .txt, .md, .docx and .xlsx files can be created here.")
        };

        await using var stream = new MemoryStream(content);
        return await SaveAsync(name, stream, cancellationToken);
    }

    private static byte[] BlankDocx() => BuildPackage(
        ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"),
        ("_rels/.rels", $"<Relationships xmlns=\"{PackageRelationships}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}/officeDocument\" Target=\"word/document.xml\"/></Relationships>"),
        ("word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/></w:body></w:document>"));

    private static byte[] BlankXlsx() => BuildPackage(
        ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>"),
        ("_rels/.rels", $"<Relationships xmlns=\"{PackageRelationships}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"),
        ("xl/workbook.xml", $"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"{OfficeRelationships}\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"),
        ("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{PackageRelationships}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>"),
        ("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData/></worksheet>"));

    private static byte[] BuildPackage(params (string Path, string Xml)[] parts)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in parts)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml);
            }
        }

        return buffer.ToArray();
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
