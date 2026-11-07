namespace Aras.SreHub.Models;

public sealed class TrackedWorkItem
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Quarter { get; set; } = string.Empty;
    public string Iteration { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

public sealed class TrackedWorkImportRequest
{
    public List<TrackedWorkItem> Items { get; set; } = [];
}

public sealed record TrackedWorkImportResult(int Added, int Skipped);
