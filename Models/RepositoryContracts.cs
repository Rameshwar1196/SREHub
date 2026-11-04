namespace Aras.SreHub.Models;

public sealed class RepositoryOptions
{
    public const string SectionName = "Repositories";

    public string Root { get; set; } = "repos";

    public void Normalize(string contentRootPath)
    {
        Root = Path.GetFullPath(Path.Combine(contentRootPath, Root)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(Root);
    }
}

public sealed class CloneRepositoryRequest
{
    public string Url { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Branch { get; set; }
}

public sealed class SwitchBranchRequest
{
    public string Branch { get; set; } = string.Empty;
}

public sealed class OpenRepositoryRequest
{
    // vscode, explorer or terminal.
    public string Target { get; set; } = "vscode";
}

public sealed class RepositoryInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? RemoteUrl { get; set; }
    public string? Branch { get; set; }
    public bool Detached { get; set; }
    public bool HasUpstream { get; set; }
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public int Changes { get; set; }
    public DateTimeOffset? LastFetchedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class RepositoryBranches
{
    public string? Current { get; set; }
    public IReadOnlyList<string> Branches { get; set; } = [];
}
