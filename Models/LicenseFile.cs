namespace Aras.SreHub.Models;

public enum LicenseVariant
{
    Auto,
    KeyValueAzure,
    KeyValueBuildCi
}

public sealed class LicenseEntry
{
    public string KeyName { get; init; } = string.Empty;
    public string? ValueBuildCi { get; init; }
    public string? ValueAzure { get; init; }
    public string? ExpirationDate { get; init; }
    public string? ContentType { get; init; }

    public string? GetValue(LicenseVariant variant) =>
        variant == LicenseVariant.KeyValueBuildCi ? ValueBuildCi : ValueAzure;
}

public sealed class FrameworkLicenseEntry
{
    public string KeyName { get; init; } = string.Empty;
    public string? Value { get; init; }
    public string? ContentType { get; init; }
    public string? ExpirationDate { get; init; }
}

public sealed class LicenseFile
{
    public IReadOnlyList<FrameworkLicenseEntry> FrameworkLicenses { get; init; } = [];
    public IReadOnlyList<LicenseEntry> LicenseData { get; init; } = [];
}

public sealed class PreviewRequest
{
    public string LicenseFileContent { get; set; } = string.Empty;
}
