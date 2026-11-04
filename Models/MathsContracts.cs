namespace Aras.SreHub.Models;

public sealed class MathsOptions
{
    public const string SectionName = "Maths";

    public string Root { get; set; } = "MATHS";

    public void Normalize(string contentRootPath)
    {
        Root = Path.GetFullPath(Path.Combine(contentRootPath, Root))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public sealed record MathsMaterial(string Name, string Title, string Kind, int KindOrder, double SizeMb);

public sealed record MathsTopic(
    string Id,
    string Name,
    string Status,
    bool HasNotes,
    IReadOnlyList<MathsMaterial> Materials);

public sealed record MathsPart(string Name, IReadOnlyList<MathsTopic> Topics);

public sealed record MathsLibraryResponse(string Root, bool Exists, IReadOnlyList<MathsPart> Parts, int TotalFiles);

public sealed record MathsNote(string Content, DateTimeOffset? Updated);

public sealed record MathsBookTopic(
    string Id,
    string Name,
    string Status,
    string Notes,
    IReadOnlyList<MathsMaterial> Materials);

public sealed record MathsBookPart(string Name, IReadOnlyList<MathsBookTopic> Topics);

public sealed record MathsBookResponse(bool Exists, IReadOnlyList<MathsBookPart> Parts);

public sealed class SaveMathsNoteRequest
{
    public string Content { get; set; } = string.Empty;
}

public sealed class SaveMathsStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
