using System.Text;
using Aras.SreHub.Learning;

namespace Aras.SreHub.Tests;

public sealed class LearningUploadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "srehub-learning-" + Guid.NewGuid().ToString("N"));
    private readonly LearningLibrary _library;

    public LearningUploadTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "04-kubernetes", "KCNA"));
        var options = new LearningOptions { ContentRoot = _root };
        _library = new LearningLibrary(options, new ProgressStore(options));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MemoryStream Bytes(string text = "PK docx") => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Upload_DocxIntoAnExistingFolderIsStoredAndClassifiedAsWord()
    {
        var saved = await _library.SaveUploadAsync("04-kubernetes", "KCNA", "Study Guide.docx", Bytes(), CancellationToken.None);

        Assert.Equal("04-kubernetes", saved.SubjectId);
        Assert.Equal("KCNA/Study Guide.docx", saved.Path);
        Assert.Equal("word", saved.Kind);
        Assert.NotNull(saved.RawUrl);
        Assert.True(File.Exists(Path.Combine(_root, "04-kubernetes", "KCNA", "Study Guide.docx")));
    }

    [Fact]
    public async Task Upload_DocxIsServedWithTheWordContentType()
    {
        await _library.SaveUploadAsync("04-kubernetes", "KCNA", "Study Guide.docx", Bytes(), CancellationToken.None);

        var (_, contentType) = _library.ResolveRawFile("04-kubernetes", "KCNA/Study Guide.docx");

        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", contentType);
    }

    [Theory]
    [InlineData("KCNA (v2) notes [final].docx", "KCNA _v2_ notes _final_.docx")]
    [InlineData("(7) KCNA.docx", "7_ KCNA.docx")]
    [InlineData("KCNA\u2013guide.docx", "KCNA_guide.docx")]
    public async Task Upload_SanitisesAwkwardFileNames(string original, string expected)
    {
        var saved = await _library.SaveUploadAsync("04-kubernetes", "KCNA", original, Bytes(), CancellationToken.None);

        Assert.Equal($"KCNA/{expected}", saved.Path);
    }

    [Fact]
    public async Task Upload_AppearsInTheSubjectTreeUnderItsFolder()
    {
        await _library.SaveUploadAsync("04-kubernetes", "KCNA", "Study Guide.docx", Bytes(), CancellationToken.None);

        var detail = await _library.GetSubjectAsync("04-kubernetes", CancellationToken.None);

        var folder = Assert.Single(detail.Files, node => node.Path == "KCNA");
        Assert.Equal("Study Guide.docx", Assert.Single(folder.Children).Name);
    }

    [Fact]
    public async Task Upload_CreatesTheTargetFolderWhenItDoesNotExist()
    {
        var saved = await _library.SaveUploadAsync("04-kubernetes", "KCNA/exam", "Mock.docx", Bytes(), CancellationToken.None);

        Assert.Equal("KCNA/exam/Mock.docx", saved.Path);
    }

    [Theory]
    [InlineData("Guide.docx")]
    [InlineData("Sheet.xlsx")]
    [InlineData("Slides.pptx")]
    public async Task ResolveDesktopFile_AllowsOfficeFiles(string name)
    {
        await _library.SaveUploadAsync("04-kubernetes", "KCNA", name, Bytes(), CancellationToken.None);

        var path = _library.ResolveDesktopFile("04-kubernetes", $"KCNA/{name}");

        Assert.EndsWith(name, path);
    }

    [Fact]
    public async Task ResolveDesktopFile_RejectsOtherFileTypes()
    {
        await _library.SaveUploadAsync("04-kubernetes", "KCNA", "notes.md", Bytes(), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => _library.ResolveDesktopFile("04-kubernetes", "KCNA/notes.md"));
    }

    [Fact]
    public void ResolveDesktopFile_RejectsPathsOutsideTheSubject()
    {
        Assert.ThrowsAny<Exception>(() => _library.ResolveDesktopFile("04-kubernetes", "../../outside.docx"));
    }

    [Fact]
    public void ResolveDesktopFile_ReportsAMissingFile()
    {
        Assert.Throws<FileNotFoundException>(() => _library.ResolveDesktopFile("04-kubernetes", "KCNA/missing.docx"));
    }

    [Fact]
    public async Task Upload_RejectsDisallowedFileTypes()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _library.SaveUploadAsync("04-kubernetes", "KCNA", "tool.exe", Bytes(), CancellationToken.None));
    }
}
