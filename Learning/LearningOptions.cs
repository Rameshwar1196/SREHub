namespace Aras.SreHub.Learning;

public sealed class LearningOptions
{
    public const string SectionName = "Learning";

    public string ContentRoot { get; set; } = "learning-content";

    public int MaxUploadBytes { get; set; } = 25 * 1024 * 1024;

    public int MaxSearchHits { get; set; } = 200;

    public void Normalize(string contentRootPath)
    {
        ContentRoot = Path.GetFullPath(Path.Combine(contentRootPath, ContentRoot));
        Directory.CreateDirectory(ContentRoot);
    }
}
