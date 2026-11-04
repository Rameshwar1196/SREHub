using System.ComponentModel.DataAnnotations;

namespace Aras.SreHub.Models;

public enum ImportMode
{
    ImportMissing,
    ReimportAll,
    ValidateOnly,
    DryRun,
    UpdateFeatureTree
}

public sealed class ImportRequest
{
    public string LicenseFileContent { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string[] InnovatorUrls { get; set; } = [];

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public string LicenseVariant { get; set; } = "auto";

    public string? Database { get; set; }

    public string[]? LicenseNames { get; set; }

    public string ValidationMethodName { get; set; } = "validate_feature_licenses";

    public string Mode { get; set; } = "importMissing";

    public bool RunValidation { get; set; } = true;

    public bool NoPasswordHash { get; set; }

    public bool SkipCertificateCheck { get; set; }
}

public sealed class LicenseResult
{
    public string KeyName { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public string ImportStatus { get; set; } = "NotStarted";
    public string ValidationStatus { get; set; } = "NotStarted";
    public string? Feature { get; set; }
    public string? ExpirationDate { get; set; }
    public string Status { get; set; } = "NotStarted";
    public string? Error { get; set; }
}

public sealed class ValidationCheck
{
    public string Feature { get; set; } = string.Empty;
    public string Status { get; set; } = "UNKNOWN";
    public string? Message { get; set; }
}

public sealed class InstanceResult
{
    public string InstanceUrl { get; set; } = string.Empty;
    public string? Database { get; set; }
    public string? Variant { get; set; }
    public string LoginStatus { get; set; } = "NotStarted";
    public string Status { get; set; } = "NotStarted";
    public List<LicenseResult> Licenses { get; set; } = [];
    public string FeatureTreeUpdate { get; set; } = "NotRun";
    public string ValidationMethod { get; set; } = "NotRun";
    public string ValidationCleanup { get; set; } = "NotRun";
    public int ValidationErrors { get; set; }
    public List<ValidationCheck> ValidationResults { get; set; } = [];
    public string? ValidationOutput { get; set; }
    public string? Error { get; set; }
}

public sealed class ImportSummary
{
    public string Variant { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int TotalInstances { get; set; }
    public int FailedInstances { get; set; }
    public int TotalLicenseTasks { get; set; }
    public int SuccessfulLicenses { get; set; }
    public int SkippedLicenses { get; set; }
    public int FailedLicenses { get; set; }
    public int ChecksPassed { get; set; }
    public int ChecksFailed { get; set; }
    public string ExecutionStatus { get; set; } = "Success";
    public List<InstanceResult> Instances { get; set; } = [];
}
