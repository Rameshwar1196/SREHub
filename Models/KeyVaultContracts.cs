using System.ComponentModel.DataAnnotations;

namespace Aras.SreHub.Models;

public sealed class KeyVaultRequest
{
    [Required]
    public string LicenseFileContent { get; set; } = string.Empty;

    [Required]
    public string KeyVaultName { get; set; } = string.Empty;

    public string LicenseVariant { get; set; } = "key_value_azure";

    public string[]? LicenseNames { get; set; }

    public bool IncludeFrameworkLicenses { get; set; } = true;

    // Report the planned changes without writing to the vault.
    public bool WhatIf { get; set; }
}

public sealed class SecretResult
{
    public string KeyName { get; set; } = string.Empty;
    public string SecretName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = "NotStarted";
    public string? ExpirationDate { get; set; }
    public string? Error { get; set; }
}

public sealed class KeyVaultSummary
{
    public string KeyVaultName { get; set; } = string.Empty;
    public string VaultUri { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public bool WhatIf { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int TotalSecrets { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Failed { get; set; }
    public string ExecutionStatus { get; set; } = "Success";
    public List<SecretResult> Secrets { get; set; } = [];
}
