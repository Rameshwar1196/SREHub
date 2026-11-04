using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace Aras.SreHub.Learning;

public sealed partial class LearningLibrary
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".sre-hub", ".vs", ".vscode", "node_modules", "bin", "obj"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".yml", ".yaml", ".json", ".xml", ".csv", ".sh", ".bash", ".ps1", ".psm1",
        ".tf", ".tfvars", ".hcl", ".py", ".go", ".js", ".ts", ".sql", ".ini", ".conf", ".cfg", ".env", ".dockerfile"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp"
    };

    private static readonly HashSet<string> UploadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".drawio", ".zip", ".docx", ".pptx", ".xlsx"
    };

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    private readonly LearningOptions _options;
    private readonly ProgressStore _progress;

    public LearningLibrary(LearningOptions options, ProgressStore progress)
    {
        _options = options;
        _progress = progress;
    }

    public string ContentRoot => _options.ContentRoot;

    public async Task<IReadOnlyList<SubjectSummary>> GetSubjectsAsync(CancellationToken cancellationToken)
    {
        var subjects = new List<SubjectSummary>();

        foreach (var directory in Directory.EnumerateDirectories(ContentRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(directory);
            if (IgnoredDirectories.Contains(name) || name.StartsWith('.'))
            {
                continue;
            }

            var files = EnumerateFiles(directory).ToList();
            var completed = await _progress.GetCompletedAsync(name, cancellationToken);
            var (title, description) = await ReadSubjectHeaderAsync(directory, name, cancellationToken);

            subjects.Add(new SubjectSummary(
                name,
                title,
                description,
                files.Count,
                files.Count(file => completed.Contains(ToRelativePath(directory, file))),
                files.Count == 0 ? Directory.GetLastWriteTimeUtc(directory) : files.Max(File.GetLastWriteTimeUtc)));
        }

        return subjects;
    }

    public async Task<SubjectDetail> GetSubjectAsync(string subjectId, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(subjectId);
        var completed = await _progress.GetCompletedAsync(subjectId, cancellationToken);
        var (title, description) = await ReadSubjectHeaderAsync(directory, subjectId, cancellationToken);

        var readmePath = FindReadme(directory);
        string? readmeHtml = null;
        string? readmeRelative = null;

        if (readmePath is not null)
        {
            readmeRelative = ToRelativePath(directory, readmePath);
            readmeHtml = Markdown.ToHtml(await File.ReadAllTextAsync(readmePath, cancellationToken), Pipeline);
        }

        var tree = BuildTree(directory, directory, completed);
        var fileCount = EnumerateFiles(directory).Count();

        return new SubjectDetail(
            subjectId,
            title,
            description,
            readmeRelative,
            readmeHtml,
            fileCount,
            completed.Count,
            tree);
    }

    public async Task<LearningFileContent> GetFileAsync(string subjectId, string relativePath, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(subjectId);
        var fullPath = ResolveFile(directory, relativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"'{relativePath}' was not found in subject '{subjectId}'.");
        }

        var info = new FileInfo(fullPath);
        var kind = ClassifyKind(fullPath);
        var normalized = ToRelativePath(directory, fullPath);
        var completed = (await _progress.GetCompletedAsync(subjectId, cancellationToken)).Contains(normalized);
        var rawUrl = $"/api/learning/raw?subject={Uri.EscapeDataString(subjectId)}&path={Uri.EscapeDataString(normalized)}";

        string? text = null;
        string? html = null;

        if (kind is "markdown" or "text")
        {
            if (info.Length > 2 * 1024 * 1024)
            {
                throw new InvalidOperationException($"'{relativePath}' is too large to display ({info.Length / 1024} KB).");
            }

            text = await File.ReadAllTextAsync(fullPath, cancellationToken);
            if (kind == "markdown")
            {
                html = Markdown.ToHtml(text, Pipeline);
            }
        }

        return new LearningFileContent(
            subjectId,
            normalized,
            info.Name,
            kind,
            text,
            html,
            kind is "markdown" or "text" ? null : rawUrl,
            info.Length,
            info.LastWriteTimeUtc,
            completed,
            kind is "markdown" or "text");
    }

    public (string Path, string ContentType) ResolveRawFile(string subjectId, string relativePath)
    {
        var directory = ResolveSubjectDirectory(subjectId);
        var fullPath = ResolveFile(directory, relativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"'{relativePath}' was not found in subject '{subjectId}'.");
        }

        return (fullPath, GetContentType(fullPath));
    }

    // Markdown links are relative to the file that contains them and may cross into another subject.
    public LinkTarget ResolveLink(string subjectId, string fromPath, string href)
    {
        var cleaned = (href ?? string.Empty).Split('#')[0].Split('?')[0].Trim().Replace('\\', '/');

        if (cleaned.Length == 0)
        {
            throw new InvalidOperationException("The link has no target.");
        }

        cleaned = Uri.UnescapeDataString(cleaned);

        if (cleaned.StartsWith('/') || SchemeRegex().IsMatch(cleaned))
        {
            throw new InvalidOperationException("Only links inside the learning library can be opened.");
        }

        ValidateSegment(subjectId);

        var fromDirectory = string.IsNullOrWhiteSpace(fromPath)
            ? string.Empty
            : (Path.GetDirectoryName(NormalizeRelativePath(fromPath)) ?? string.Empty).Replace('\\', '/');

        var combined = string.Join('/', new[] { subjectId, fromDirectory, cleaned }.Where(part => part.Length > 0));
        var full = Path.GetFullPath(Path.Combine(ContentRoot, combined.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInside(ContentRoot, full);

        var relative = ToRelativePath(ContentRoot, full).TrimEnd('/');
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new InvalidOperationException("The link points outside every subject.");
        }

        var targetSubject = segments[0];
        var targetPath = string.Join('/', segments.Skip(1));
        var isDirectory = Directory.Exists(full);
        var exists = isDirectory || File.Exists(full);

        var rawUrl = exists && !isDirectory && ClassifyKind(full) is not ("markdown" or "text")
            ? $"/api/learning/raw?subject={Uri.EscapeDataString(targetSubject)}&path={Uri.EscapeDataString(targetPath)}"
            : null;

        return new LinkTarget(targetSubject, targetPath, isDirectory, exists, rawUrl);
    }

    public async Task<SubjectSummary> CreateSubjectAsync(CreateSubjectRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        ValidateSegment(name);

        var directory = Path.Combine(ContentRoot, name);
        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Subject '{name}' already exists.");
        }

        Directory.CreateDirectory(directory);

        var builder = new StringBuilder()
            .AppendLine($"# {Prettify(name)}")
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            builder.AppendLine(request.Description.Trim()).AppendLine();
        }

        builder.AppendLine("## Concepts").AppendLine().AppendLine("## Commands").AppendLine().AppendLine("## Interview questions").AppendLine();

        await File.WriteAllTextAsync(Path.Combine(directory, "README.md"), builder.ToString(), cancellationToken);

        return new SubjectSummary(name, Prettify(name), request.Description?.Trim(), 1, 0, DateTimeOffset.UtcNow);
    }

    public async Task<LearningFileContent> SaveNoteAsync(SaveNoteRequest request, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(request.SubjectId);
        var relativePath = NormalizeRelativePath(request.Path);

        if (!MarkdownExtensions.Contains(Path.GetExtension(relativePath)) &&
            !TextExtensions.Contains(Path.GetExtension(relativePath)))
        {
            relativePath += ".md";
        }

        var fullPath = ResolveFile(directory, relativePath);

        if (File.Exists(fullPath) && !request.Overwrite)
        {
            throw new InvalidOperationException($"'{relativePath}' already exists. Enable overwrite to replace it.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, request.Content ?? string.Empty, cancellationToken);

        return await GetFileAsync(request.SubjectId, ToRelativePath(directory, fullPath), cancellationToken);
    }

    public async Task<LearningFileContent> SaveUploadAsync(
        string subjectId,
        string? targetFolder,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(subjectId);
        var safeName = Regex.Replace(Path.GetFileName(fileName ?? string.Empty).Trim(), @"[^A-Za-z0-9 ._-]", "_").TrimStart('.', '_', '-', ' ');

        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new InvalidOperationException("The uploaded file has no usable name.");
        }

        var extension = Path.GetExtension(safeName);
        if (!MarkdownExtensions.Contains(extension) &&
            !TextExtensions.Contains(extension) &&
            !ImageExtensions.Contains(extension) &&
            !UploadExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File type '{extension}' is not allowed.");
        }

        var relativePath = string.IsNullOrWhiteSpace(targetFolder)
            ? safeName
            : $"{NormalizeRelativePath(targetFolder)}/{safeName}";

        var fullPath = ResolveFile(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var target = File.Create(fullPath))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        return await GetFileAsync(subjectId, ToRelativePath(directory, fullPath), cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, string? subjectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            throw new InvalidOperationException("Enter at least two characters to search.");
        }

        var term = query.Trim();
        var hits = new List<SearchHit>();
        var subjects = string.IsNullOrWhiteSpace(subjectId)
            ? (await GetSubjectsAsync(cancellationToken)).Select(subject => (subject.Id, subject.Title)).ToList()
            : [(subjectId, subjectId)];

        foreach (var (id, title) in subjects)
        {
            var directory = ResolveSubjectDirectory(id);

            foreach (var file in EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ClassifyKind(file) is not ("markdown" or "text"))
                {
                    continue;
                }

                var info = new FileInfo(file);
                if (info.Length > 1024 * 1024)
                {
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in await File.ReadAllLinesAsync(file, cancellationToken))
                {
                    lineNumber++;

                    if (line.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new SearchHit(id, title, ToRelativePath(directory, file), lineNumber, line.Trim()));

                        if (hits.Count >= _options.MaxSearchHits)
                        {
                            return hits;
                        }
                    }
                }
            }
        }

        return hits;
    }

    public async Task<IReadOnlyList<Flashcard>> GetFlashcardsAsync(string? subjectId, CancellationToken cancellationToken)
    {
        var cards = new List<Flashcard>();
        var subjectIds = string.IsNullOrWhiteSpace(subjectId)
            ? (await GetSubjectsAsync(cancellationToken)).Select(subject => subject.Id).ToList()
            : [subjectId];

        foreach (var id in subjectIds)
        {
            var directory = ResolveSubjectDirectory(id);

            foreach (var file in EnumerateFiles(directory))
            {
                if (!MarkdownExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                var markdown = await File.ReadAllTextAsync(file, cancellationToken);
                cards.AddRange(FlashcardParser.Parse(id, ToRelativePath(directory, file), markdown));
            }
        }

        return cards;
    }

    public Task SetProgressAsync(ProgressRequest request, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(request.SubjectId);
        var fullPath = ResolveFile(directory, request.Path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"'{request.Path}' was not found in subject '{request.SubjectId}'.");
        }

        return _progress.SetAsync(request.SubjectId, ToRelativePath(directory, fullPath), request.Completed, cancellationToken);
    }

    public async Task DeleteFileAsync(string subjectId, string path, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(subjectId);
        var fullPath = ResolveFile(directory, path);
        var relativePath = ToRelativePath(directory, fullPath);

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
        else if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        else
        {
            throw new FileNotFoundException($"'{path}' was not found in subject '{subjectId}'.");
        }

        await _progress.RemovePathAsync(subjectId, relativePath, cancellationToken);
    }

    public async Task DeleteSubjectAsync(string subjectId, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(subjectId);

        Directory.Delete(directory, recursive: true);

        await _progress.RemoveSubjectAsync(subjectId, cancellationToken);
    }

    public Task<EntryLocation> CreateFolderAsync(FolderRequest request, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(request.SubjectId);
        var fullPath = ResolveFile(directory, request.Path);

        if (Directory.Exists(fullPath) || File.Exists(fullPath))
        {
            throw new InvalidOperationException($"'{request.Path}' already exists.");
        }

        Directory.CreateDirectory(fullPath);

        return Task.FromResult(new EntryLocation(request.SubjectId, ToRelativePath(directory, fullPath)));
    }

    public async Task<EntryLocation> RenameAsync(RenameRequest request, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(request.SubjectId);
        var source = ResolveFile(directory, request.Path);
        var newName = (request.NewName ?? string.Empty).Trim();

        ValidateSegment(newName);

        if (File.Exists(source))
        {
            EnsureAllowedExtension(newName);
        }
        else if (!Directory.Exists(source))
        {
            throw new FileNotFoundException($"'{request.Path}' was not found in subject '{request.SubjectId}'.");
        }

        var destination = Path.Combine(Path.GetDirectoryName(source)!, newName);

        return await RelocateAsync(request.SubjectId, directory, source, request.SubjectId, directory, destination, cancellationToken);
    }

    public async Task<EntryLocation> MoveAsync(MoveRequest request, CancellationToken cancellationToken)
    {
        var sourceDirectory = ResolveSubjectDirectory(request.SubjectId);
        var source = ResolveFile(sourceDirectory, request.Path);

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            throw new FileNotFoundException($"'{request.Path}' was not found in subject '{request.SubjectId}'.");
        }

        var targetDirectory = ResolveSubjectDirectory(request.TargetSubjectId);
        var folder = (request.TargetFolder ?? string.Empty).Trim().Trim('/', '\\');
        var destinationFolder = folder.Length == 0 ? targetDirectory : ResolveFile(targetDirectory, folder);

        if (File.Exists(destinationFolder))
        {
            throw new InvalidOperationException($"'{folder}' is a file, not a folder.");
        }

        var destination = Path.Combine(destinationFolder, Path.GetFileName(source));

        return await RelocateAsync(request.SubjectId, sourceDirectory, source, request.TargetSubjectId, targetDirectory, destination, cancellationToken);
    }

    public async Task<EntryLocation> RenameSubjectAsync(string subjectId, string newName, CancellationToken cancellationToken)
    {
        var directory = ResolveSubjectDirectory(subjectId);
        var name = (newName ?? string.Empty).Trim();

        ValidateSegment(name);

        var destination = Path.GetFullPath(Path.Combine(ContentRoot, name));
        EnsureInside(ContentRoot, destination);

        if (string.Equals(directory, destination, StringComparison.Ordinal))
        {
            return new EntryLocation(subjectId, string.Empty);
        }

        MoveEntry(directory, destination);
        await _progress.RenameSubjectAsync(subjectId, name, cancellationToken);

        return new EntryLocation(name, string.Empty);
    }

    private async Task<EntryLocation> RelocateAsync(
        string subjectId,
        string sourceDirectory,
        string source,
        string targetSubjectId,
        string targetDirectory,
        string destination,
        CancellationToken cancellationToken)
    {
        destination = Path.GetFullPath(destination);
        EnsureInside(targetDirectory, destination);

        var fromPath = ToRelativePath(sourceDirectory, source);
        var toPath = ToRelativePath(targetDirectory, destination);

        if (string.Equals(source, destination, StringComparison.Ordinal))
        {
            return new EntryLocation(targetSubjectId, toPath);
        }

        var separator = Path.DirectorySeparatorChar;
        if (Directory.Exists(source) && destination.StartsWith(source.TrimEnd(separator) + separator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder cannot be moved into itself.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        MoveEntry(source, destination);

        await _progress.MovePathAsync(subjectId, fromPath, targetSubjectId, toPath, cancellationToken);

        return new EntryLocation(targetSubjectId, toPath);
    }

    private static void MoveEntry(string source, string destination)
    {
        var caseOnlyChange = string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);

        if (!caseOnlyChange && (File.Exists(destination) || Directory.Exists(destination)))
        {
            throw new InvalidOperationException($"'{Path.GetFileName(destination)}' already exists in the destination.");
        }

        var isDirectory = Directory.Exists(source);

        if (caseOnlyChange)
        {
            // Windows treats a case-only rename as the same path, so hop through a temporary name.
            var temporary = $"{source}.{Guid.NewGuid():N}.tmp";
            MoveEntry(source, temporary, isDirectory);
            MoveEntry(temporary, destination, isDirectory);
            return;
        }

        MoveEntry(source, destination, isDirectory);
    }

    private static void MoveEntry(string source, string destination, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private static void EnsureAllowedExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (!MarkdownExtensions.Contains(extension) &&
            !TextExtensions.Contains(extension) &&
            !ImageExtensions.Contains(extension) &&
            !UploadExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File type '{extension}' is not allowed.");
        }
    }

    private IReadOnlyList<LearningNode> BuildTree(string root, string directory, IReadOnlySet<string> completed)
    {
        var nodes = new List<LearningNode>();

        foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(child);
            if (IgnoredDirectories.Contains(name) || name.StartsWith('.'))
            {
                continue;
            }

            nodes.Add(new LearningNode(
                name,
                ToRelativePath(root, child),
                true,
                "folder",
                0,
                Directory.GetLastWriteTimeUtc(child),
                false,
                BuildTree(root, child, completed)));
        }

        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
            {
                continue;
            }

            var info = new FileInfo(file);
            var relative = ToRelativePath(root, file);

            nodes.Add(new LearningNode(
                name,
                relative,
                false,
                ClassifyKind(file),
                info.Length,
                info.LastWriteTimeUtc,
                completed.Contains(relative),
                []));
        }

        return nodes;
    }

    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments.Any(segment => IgnoredDirectories.Contains(segment) || segment.StartsWith('.')))
            {
                continue;
            }

            yield return file;
        }
    }

    private static async Task<(string Title, string? Description)> ReadSubjectHeaderAsync(
        string directory,
        string fallbackName,
        CancellationToken cancellationToken)
    {
        var readme = FindReadme(directory);
        if (readme is null)
        {
            return (Prettify(fallbackName), null);
        }

        string? title = null;
        string? description = null;

        foreach (var line in await File.ReadAllLinesAsync(readme, cancellationToken))
        {
            var text = line.Trim();

            if (title is null && text.StartsWith("# "))
            {
                title = text[2..].Trim();
                continue;
            }

            if (title is not null && description is null && text.Length > 0 && !text.StartsWith('#') && !text.StartsWith("---"))
            {
                description = text;
                break;
            }
        }

        return (title ?? Prettify(fallbackName), description);
    }

    private static string? FindReadme(string directory) =>
        Directory.EnumerateFiles(directory, "*.md")
            .FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), "README", StringComparison.OrdinalIgnoreCase));

    private string ResolveSubjectDirectory(string subjectId)
    {
        ValidateSegment(subjectId);

        var full = Path.GetFullPath(Path.Combine(ContentRoot, subjectId));
        EnsureInside(ContentRoot, full);

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Subject '{subjectId}' was not found.");
        }

        return full;
    }

    private static string ResolveFile(string subjectDirectory, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var full = Path.GetFullPath(Path.Combine(subjectDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInside(subjectDirectory, full);
        return full;
    }

    private static void EnsureInside(string root, string candidate)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("The requested path is outside the learning content folder.");
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        var value = (relativePath ?? string.Empty).Trim().Replace('\\', '/').Trim('/');

        if (value.Length == 0)
        {
            throw new InvalidOperationException("A file path is required.");
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            ValidateSegment(segment);
        }

        return string.Join('/', segments);
    }

    private static void ValidateSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.StartsWith('.'))
        {
            throw new InvalidOperationException($"'{segment}' is not a valid name.");
        }

        if (!SegmentRegex().IsMatch(segment))
        {
            throw new InvalidOperationException($"'{segment}' may only contain letters, digits, spaces and . _ - characters.");
        }
    }

    private static string ToRelativePath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static string ClassifyKind(string path)
    {
        var extension = Path.GetExtension(path);

        if (MarkdownExtensions.Contains(extension)) return "markdown";
        if (TextExtensions.Contains(extension)) return "text";
        if (ImageExtensions.Contains(extension)) return "image";
        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)) return "pdf";
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)) return "word";
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return "excel";
        if (extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase)) return "powerpoint";
        return "binary";
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream"
    };

    private static string Prettify(string name)
    {
        var cleaned = PrefixRegex().Replace(name, string.Empty).Replace('-', ' ').Replace('_', ' ').Trim();
        return cleaned.Length == 0 ? name : char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._-]*$")]
    private static partial Regex SegmentRegex();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:")]
    private static partial Regex SchemeRegex();

    [GeneratedRegex(@"^\d+[-_]")]
    private static partial Regex PrefixRegex();
}
