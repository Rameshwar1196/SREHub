using Aras.SreHub.Services;

namespace Aras.SreHub.Tests;

public sealed class OfficeLauncherTests
{
    [Theory]
    [InlineData(@"C:\docs\Guide.docx", "WINWORD.EXE")]
    [InlineData(@"C:\docs\Sheet.XLSX", "EXCEL.EXE")]
    [InlineData(@"C:\docs\Slides.pptx", "POWERPNT.EXE")]
    public void ExecutableNameFor_MapsOfficeExtensionsToTheirApps(string path, string expected)
    {
        Assert.Equal(expected, OfficeLauncher.ExecutableNameFor(path));
    }

    [Theory]
    [InlineData(@"C:\docs\notes.md")]
    [InlineData(@"C:\docs\tool.exe")]
    public void ExecutableNameFor_ReturnsNullForOtherFiles(string path)
    {
        Assert.Null(OfficeLauncher.ExecutableNameFor(path));
    }
}
