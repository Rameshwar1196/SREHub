namespace Aras.SreHub.Learning;

public sealed record SubjectSummary(
    string Id,
    string Title,
    string? Description,
    int FileCount,
    int CompletedCount,
    DateTimeOffset UpdatedAt);

public sealed record LearningNode(
    string Name,
    string Path,
    bool IsDirectory,
    string Kind,
    long Size,
    DateTimeOffset UpdatedAt,
    bool Completed,
    IReadOnlyList<LearningNode> Children);

public sealed record SubjectDetail(
    string Id,
    string Title,
    string? Description,
    string? ReadmePath,
    string? ReadmeHtml,
    int FileCount,
    int CompletedCount,
    IReadOnlyList<LearningNode> Files);

public sealed record LearningFileContent(
    string SubjectId,
    string Path,
    string Name,
    string Kind,
    string? Text,
    string? Html,
    string? RawUrl,
    long Size,
    DateTimeOffset UpdatedAt,
    bool Completed,
    bool Editable);

public sealed record SearchHit(
    string SubjectId,
    string SubjectTitle,
    string Path,
    int LineNumber,
    string Line);

public sealed record LinkTarget(
    string SubjectId,
    string Path,
    bool IsDirectory,
    bool Exists,
    string? RawUrl);

public sealed record Flashcard(
    string SubjectId,
    string Path,
    string Question,
    string Answer);

public sealed class CreateSubjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class SaveNoteRequest
{
    public string SubjectId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool Overwrite { get; set; }
}

public sealed class ProgressRequest
{
    public string SubjectId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Completed { get; set; }
}

public sealed class FolderRequest
{
    public string SubjectId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class RenameRequest
{
    public string SubjectId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}

public sealed class MoveRequest
{
    public string SubjectId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string TargetSubjectId { get; set; } = string.Empty;
    public string? TargetFolder { get; set; }
}

public sealed class RenameSubjectRequest
{
    public string NewName { get; set; } = string.Empty;
}

public sealed record EntryLocation(string SubjectId, string Path);
