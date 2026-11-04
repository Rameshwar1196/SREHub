using System.Diagnostics;
using System.Text.RegularExpressions;
using Aras.SreHub.Models;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Aras.SreHub.Services;

public sealed partial class KeyVaultLicenseService
{
    public async Task<KeyVaultSummary> UpdateAsync(KeyVaultRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.KeyVaultName))
        {
            throw new InvalidOperationException("A Key Vault name is required.");
        }

        var variant = LicenseFileParser.ParseVariant(request.LicenseVariant);
        if (variant == LicenseVariant.Auto)
        {
            throw new InvalidOperationException("Select 'key_value_azure' or 'key_value_build_ci'; 'auto' cannot be resolved for a Key Vault update.");
        }

        var licenseFile = LicenseFileParser.Parse(request.LicenseFileContent);
        var licenses = LicenseFileParser.Filter(licenseFile.LicenseData, request.LicenseNames);

        var vaultUri = BuildVaultUri(request.KeyVaultName);
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var summary = new KeyVaultSummary
        {
            KeyVaultName = request.KeyVaultName.Trim(),
            VaultUri = vaultUri.ToString(),
            Variant = LicenseFileParser.ToWireName(variant),
            WhatIf = request.WhatIf,
            StartedAt = startedAt
        };

        var options = new SecretClientOptions();
        options.Retry.MaxRetries = 1;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);

        var client = new SecretClient(vaultUri, new DefaultAzureCredential(), options);

        foreach (var license in licenses)
        {
            summary.Secrets.Add(await SetSecretAsync(
                client,
                license.KeyName,
                license.GetValue(variant),
                license.ExpirationDate,
                LicenseFileParser.ToWireName(variant),
                request.WhatIf,
                cancellationToken));
        }

        if (request.IncludeFrameworkLicenses)
        {
            foreach (var framework in licenseFile.FrameworkLicenses)
            {
                summary.Secrets.Add(await SetSecretAsync(
                    client,
                    framework.KeyName,
                    framework.Value,
                    framework.ExpirationDate,
                    "framework_licenses",
                    request.WhatIf,
                    cancellationToken));
            }
        }

        stopwatch.Stop();

        summary.DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
        summary.TotalSecrets = summary.Secrets.Count;
        summary.Updated = summary.Secrets.Count(secret => secret.Status is "Updated" or "Created" or "WouldUpdate");
        summary.Unchanged = summary.Secrets.Count(secret => secret.Status == "Unchanged");
        summary.Failed = summary.Secrets.Count(secret => secret.Status == "Failed");
        summary.ExecutionStatus = summary.Failed > 0 ? "Failed" : "Success";

        return summary;
    }

    private static async Task<SecretResult> SetSecretAsync(
        SecretClient client,
        string keyName,
        string? value,
        string? expirationDate,
        string source,
        bool whatIf,
        CancellationToken cancellationToken)
    {
        var result = new SecretResult
        {
            KeyName = keyName,
            SecretName = ToSecretName(keyName),
            Source = source,
            ExpirationDate = expirationDate
        };

        try
        {
            if (string.IsNullOrWhiteSpace(keyName))
            {
                throw new InvalidOperationException("The entry has no 'key_name'.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"The entry has no '{source}' value.");
            }

            string? currentValue = null;
            try
            {
                var existing = await client.GetSecretAsync(result.SecretName, cancellationToken: cancellationToken);
                currentValue = existing.Value.Value;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                currentValue = null;
            }

            if (string.Equals(currentValue, value, StringComparison.Ordinal))
            {
                result.Status = "Unchanged";
                return result;
            }

            if (whatIf)
            {
                result.Status = "WouldUpdate";
                return result;
            }

            await client.SetSecretAsync(result.SecretName, value, cancellationToken);
            result.Status = currentValue is null ? "Created" : "Updated";
        }
        catch (Exception exception)
        {
            result.Status = "Failed";
            result.Error = exception.Message;
        }

        return result;
    }

    private static Uri BuildVaultUri(string nameOrUri)
    {
        var value = nameOrUri.Trim().TrimEnd('/');

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(value);
        }

        return new Uri($"https://{value}.vault.azure.net/");
    }

    // Key Vault secret names allow alphanumerics and dashes only.
    private static string ToSecretName(string keyName) => InvalidSecretCharRegex().Replace(keyName.Trim(), "-");

    [GeneratedRegex("[^0-9a-zA-Z-]")]
    private static partial Regex InvalidSecretCharRegex();
}
