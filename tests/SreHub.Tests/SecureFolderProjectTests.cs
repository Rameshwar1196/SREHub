using Aras.SreHub.Models;
using Aras.SreHub.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Aras.SreHub.Tests;

public sealed class SecureFolderProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "srehub-secure-" + Guid.NewGuid().ToString("N"));
    private readonly SecureFileService _service;

    public SecureFolderProjectTests()
    {
        Directory.CreateDirectory(_root);
        _service = new SecureFileService(new TestEnvironment(_root), new EphemeralDataProtectionProvider(), SecureVaultProfile.Operations);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static List<ProjectLink> Links(params (string Label, string Url)[] links) =>
        links.Select(link => new ProjectLink { Label = link.Label, Url = link.Url }).ToList();

    private Task<SecureFolder> CreateDedicated(string name, params (string Label, string Url)[] links) =>
        _service.CreateFolderAsync(name, null, "tester", CancellationToken.None, dedicated: true, links: Links(links));

    private async Task<SecureFolder> Reload(string id) =>
        (await _service.GetTreeAsync(CancellationToken.None)).Folders.Single(folder => folder.Id == id);

    [Fact]
    public async Task Create_DedicatedProjectKeepsItsTrackerLink()
    {
        var created = await CreateDedicated("Karcher", ("Project Tracker", "https://arascorporation.sharepoint.com/sites/karcher"));

        var stored = await Reload(created.Id);
        Assert.True(stored.Dedicated);
        var link = Assert.Single(stored.Links);
        Assert.Equal("Project Tracker", link.Label);
        Assert.Equal("https://arascorporation.sharepoint.com/sites/karcher", link.Url);
    }

    [Fact]
    public async Task Create_PlainFolderIsNotDedicatedAndIgnoresLinks()
    {
        var created = await _service.CreateFolderAsync("Docs", null, "tester", CancellationToken.None, dedicated: false, links: Links(("Tracker", "https://example.com")));

        Assert.False(created.Dedicated);
        Assert.Empty(created.Links);
    }

    [Fact]
    public async Task Create_RejectsADedicatedProjectInsideAnotherFolder()
    {
        var parent = await _service.CreateFolderAsync("Parent", null, "tester", CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateFolderAsync("Child", parent.Id, "tester", CancellationToken.None, dedicated: true));
    }

    [Fact]
    public async Task Update_MarksAnExistingFolderAsDedicatedWithLinks()
    {
        var folder = await _service.CreateFolderAsync("Medi", null, "tester", CancellationToken.None);

        await _service.UpdateFolderAsync(folder.Id, "Medi", null, "tester", CancellationToken.None, dedicated: true, links: Links(("Project Tracker", "https://example.com/medi")));

        var stored = await Reload(folder.Id);
        Assert.True(stored.Dedicated);
        Assert.Equal("https://example.com/medi", Assert.Single(stored.Links).Url);
    }

    [Fact]
    public async Task Update_RenamingKeepsDedicatedStatusAndLinks()
    {
        var folder = await CreateDedicated("Tokamak", ("Project Tracker", "https://example.com/t"));

        await _service.UpdateFolderAsync(folder.Id, "Tokamak 2", null, "tester", CancellationToken.None);

        var stored = await Reload(folder.Id);
        Assert.Equal("Tokamak 2", stored.Name);
        Assert.True(stored.Dedicated);
        Assert.Single(stored.Links);
    }

    [Fact]
    public async Task Update_ReplacesTheLinkList()
    {
        var folder = await CreateDedicated("Karcher", ("Project Tracker", "https://example.com/a"));

        await _service.UpdateFolderAsync(folder.Id, "Karcher", null, "tester", CancellationToken.None,
            links: Links(("Project Tracker", "https://example.com/b"), ("Deployment Tracker", "https://example.com/c")));

        var stored = await Reload(folder.Id);
        Assert.Equal(["https://example.com/b", "https://example.com/c"], stored.Links.Select(link => link.Url));
    }

    [Fact]
    public async Task Update_RemovingDedicatedClearsTheLinks()
    {
        var folder = await CreateDedicated("Karcher", ("Project Tracker", "https://example.com/a"));

        await _service.UpdateFolderAsync(folder.Id, "Karcher", null, "tester", CancellationToken.None, dedicated: false);

        var stored = await Reload(folder.Id);
        Assert.False(stored.Dedicated);
        Assert.Empty(stored.Links);
    }

    [Fact]
    public async Task Update_MovingAProjectBelowAnotherFolderEndsItsDedicatedStatus()
    {
        var project = await CreateDedicated("Karcher", ("Project Tracker", "https://example.com/a"));
        var other = await _service.CreateFolderAsync("Other projects", null, "tester", CancellationToken.None);

        await _service.UpdateFolderAsync(project.Id, "Karcher", other.Id, "tester", CancellationToken.None);

        Assert.False((await Reload(project.Id)).Dedicated);
    }

    [Fact]
    public async Task Update_RejectsMarkingANestedFolderAsDedicated()
    {
        var parent = await _service.CreateFolderAsync("Parent", null, "tester", CancellationToken.None);
        var child = await _service.CreateFolderAsync("Child", parent.Id, "tester", CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateFolderAsync(child.Id, "Child", parent.Id, "tester", CancellationToken.None, dedicated: true));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a link")]
    [InlineData("ftp://example.com/file")]
    public async Task Links_RejectNonHttpAddresses(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateDedicated("Karcher", ("Project Tracker", url)));
    }

    [Fact]
    public async Task Links_AllowAnEmptyAddressAsAPlaceholder()
    {
        var created = await CreateDedicated("Karcher", ("Project Tracker", ""));

        Assert.Equal(string.Empty, Assert.Single(created.Links).Url);
    }

    [Fact]
    public async Task Links_RejectALinkWithoutALabel()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateDedicated("Karcher", ("", "https://example.com")));
    }

    [Fact]
    public async Task Links_DropCompletelyEmptyRows()
    {
        var created = await CreateDedicated("Karcher", ("", ""), ("Project Tracker", "https://example.com"));

        Assert.Single(created.Links);
    }

    [Fact]
    public async Task Links_AreLimitedToFivePerProject()
    {
        var links = Enumerable.Range(0, 6).Select(index => ($"Link {index}", "https://example.com")).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateDedicated("Karcher", links));
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SreHub.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
