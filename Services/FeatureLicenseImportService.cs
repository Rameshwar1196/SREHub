using System.Diagnostics;
using System.Text.RegularExpressions;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed partial class FeatureLicenseImportService(ILogger<FeatureLicenseImportService> logger)
{
    private const string ValidationMethodCode = """
        Innovator innovator = this.getInnovator();
        Item allFeatureLicenses = innovator.newItem("Feature License", "get");
        allFeatureLicenses = allFeatureLicenses.apply();
        StringBuilder validationLog = new StringBuilder();

        for (int i = 0; i < allFeatureLicenses.getItemCount(); i++)
        {
            Item featureLicense = allFeatureLicenses.getItemByIndex(i);
            string licenseName = featureLicense.getProperty("feature");
            validationLog.AppendLine("Validate '" + licenseName + "'");
            try
            {
                innovator.ConsumeLicense(licenseName);
                validationLog.AppendLine("  OK");
            }
            catch (Aras.Server.Core.InnovatorServerException e)
            {
                validationLog.AppendLine("  ERROR: " + e.Message);
            }
        }

        return innovator.newResult(validationLog.ToString());
        """;

    public async Task<ImportSummary> RunAsync(ImportRequest request, CancellationToken cancellationToken)
    {
        var requestedVariant = LicenseFileParser.ParseVariant(request.LicenseVariant);
        var mode = ParseMode(request.Mode);

        // Validate-only and feature-tree runs never read the file; they inspect whatever is already on the instance.
        var licenses = mode is ImportMode.ValidateOnly or ImportMode.UpdateFeatureTree
            ? []
            : LicenseFileParser.Filter(LicenseFileParser.Parse(request.LicenseFileContent).LicenseData, request.LicenseNames);

        var urls = request.InnovatorUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .ToList();

        if (urls.Count == 0)
        {
            throw new InvalidOperationException("At least one Innovator URL is required.");
        }

        if (urls.Count > 1 && !string.IsNullOrWhiteSpace(request.Database))
        {
            throw new InvalidOperationException("A database name cannot be combined with multiple instances; each instance resolves its own database.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var summary = new ImportSummary
        {
            Variant = LicenseFileParser.ToWireName(requestedVariant),
            Mode = ToWireName(mode),
            StartedAt = startedAt
        };

        foreach (var url in urls)
        {
            summary.Instances.Add(await RunInstanceAsync(url, request, requestedVariant, mode, licenses, cancellationToken));
        }

        stopwatch.Stop();

        var allLicenses = summary.Instances.SelectMany(instance => instance.Licenses).ToList();
        var allChecks = summary.Instances.SelectMany(instance => instance.ValidationResults).ToList();

        summary.DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
        summary.TotalInstances = summary.Instances.Count;
        summary.FailedInstances = summary.Instances.Count(instance => instance.Status != "Success");
        summary.TotalLicenseTasks = allLicenses.Count;
        summary.SuccessfulLicenses = allLicenses.Count(license => license.Status == "Success");
        summary.SkippedLicenses = allLicenses.Count(license => license.Status is "AlreadyExists" or "WouldUpdate");
        summary.FailedLicenses = allLicenses.Count(license => license.Status == "Failed");
        summary.ChecksPassed = allChecks.Count(check => check.Status == "PASS");
        summary.ChecksFailed = allChecks.Count(check => check.Status != "PASS");
        summary.ExecutionStatus = summary.FailedInstances > 0 ? "Failed" : "Success";

        return summary;
    }

    private async Task<InstanceResult> RunInstanceAsync(
        string url,
        ImportRequest request,
        LicenseVariant requestedVariant,
        ImportMode mode,
        IReadOnlyList<LicenseEntry> licenses,
        CancellationToken cancellationToken)
    {
        using var client = new InnovatorClient(url, request.SkipCertificateCheck);
        var instance = new InstanceResult { InstanceUrl = client.BaseUrl, Database = request.Database };

        try
        {
            instance.Database = string.IsNullOrWhiteSpace(request.Database)
                ? await client.ResolveDatabaseAsync(cancellationToken)
                : request.Database;

            await client.ConnectAsync(instance.Database!, request.Username, request.Password, request.NoPasswordHash, cancellationToken);
            instance.LoginStatus = "Success";

            var variant = LicenseFileParser.Resolve(requestedVariant, client.BaseUrl);
            instance.Variant = LicenseFileParser.ToWireName(variant);

            if (mode is not (ImportMode.ValidateOnly or ImportMode.UpdateFeatureTree))
            {
                foreach (var license in licenses)
                {
                    instance.Licenses.Add(await ImportLicenseAsync(client, license, variant, mode, cancellationToken));
                }
            }

            if (mode == ImportMode.UpdateFeatureTree)
            {
                try
                {
                    var method = await client.UpdateFeatureTreeAsync(cancellationToken);
                    instance.FeatureTreeUpdate = $"Success (ran {method})";
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Update Feature Tree failed for {Instance}.", instance.InstanceUrl);
                    instance.FeatureTreeUpdate = $"Failed: {exception.Message}";
                }
            }

            if (ShouldValidate(mode, request.RunValidation))
            {
                await RunValidationAsync(client, instance, request.ValidationMethodName, cancellationToken);
            }

            var failed = instance.Licenses.Count(license => license.Status == "Failed");
            instance.Status = failed > 0
                || instance.ValidationMethod == "Failed"
                || instance.FeatureTreeUpdate.StartsWith("Failed", StringComparison.Ordinal)
                    ? "PartiallyFailed"
                    : "Success";
        }
        catch (Exception exception)
        {
            if (instance.LoginStatus != "Success")
            {
                instance.LoginStatus = "Failed";
            }

            instance.Status = "Failed";
            instance.Error = exception.Message;
        }

        return instance;
    }

    private static async Task<LicenseResult> ImportLicenseAsync(
        InnovatorClient client,
        LicenseEntry license,
        LicenseVariant variant,
        ImportMode mode,
        CancellationToken cancellationToken)
    {
        var variantName = LicenseFileParser.ToWireName(variant);
        var entry = new LicenseResult
        {
            KeyName = license.KeyName,
            Variant = variantName,
            ExpirationDate = license.ExpirationDate
        };

        try
        {
            var licenseData = license.GetValue(variant);
            if (string.IsNullOrWhiteSpace(licenseData))
            {
                throw new InvalidOperationException($"License '{license.KeyName}' has no '{variantName}' value.");
            }

            var existing = await client.GetFeatureLicenseAsync(licenseData, cancellationToken);
            var upToDate = existing is not null && InnovatorClient.ExpirationMatches(license.ExpirationDate, existing.ExpirationDate);

            if (mode != ImportMode.ReimportAll && upToDate)
            {
                entry.ImportStatus = "Skipped";
                entry.ValidationStatus = "Success";
                entry.Feature = existing!.Feature;
                entry.ExpirationDate = existing.ExpirationDate;
                entry.Status = "AlreadyExists";
                return entry;
            }

            if (mode == ImportMode.DryRun)
            {
                entry.ImportStatus = "Skipped";
                entry.ValidationStatus = "Skipped";
                entry.Feature = existing?.Feature;
                entry.Status = "WouldUpdate";
                return entry;
            }

            await client.ImportFeatureLicenseAsync(licenseData, cancellationToken);
            entry.ImportStatus = "Success";

            var verified = await client.GetFeatureLicenseAsync(licenseData, cancellationToken)
                ?? throw new InvalidOperationException("Feature License not found on the instance after import.");

            entry.ValidationStatus = "Success";
            entry.Feature = verified.Feature;
            entry.ExpirationDate = verified.ExpirationDate;
            entry.Status = "Success";
        }
        catch (Exception exception)
        {
            if (entry.ImportStatus != "Success")
            {
                entry.ImportStatus = "Failed";
            }

            if (entry.ValidationStatus != "Success")
            {
                entry.ValidationStatus = "Failed";
            }

            entry.Status = "Failed";
            entry.Error = exception.Message;
        }

        return entry;
    }

    private static async Task RunValidationAsync(
        InnovatorClient client,
        InstanceResult instance,
        string methodName,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.SetValidationMethodAsync(methodName, ValidationMethodCode, cancellationToken);
            var output = await client.InvokeValidationMethodAsync(methodName, cancellationToken);
            var checks = ParseValidationOutput(output);

            instance.ValidationMethod = "Success";
            instance.ValidationOutput = output;
            instance.ValidationResults = checks;
            instance.ValidationErrors = checks.Count(check => check.Status != "PASS");
        }
        catch (Exception exception)
        {
            instance.ValidationMethod = "Failed";
            instance.ValidationOutput = exception.Message;
        }
        finally
        {
            try
            {
                await client.RemoveValidationMethodAsync(methodName, cancellationToken);
                instance.ValidationCleanup = "Success";
            }
            catch (Exception exception)
            {
                instance.ValidationCleanup = $"Failed: {exception.Message}";
            }
        }
    }

    internal static ImportMode ParseMode(string? mode) => (mode ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" or "importmissing" => ImportMode.ImportMissing,
        "reimportall" => ImportMode.ReimportAll,
        "validateonly" => ImportMode.ValidateOnly,
        "dryrun" => ImportMode.DryRun,
        "updatefeaturetree" => ImportMode.UpdateFeatureTree,
        _ => throw new InvalidOperationException($"Unknown run mode '{mode}'.")
    };

    internal static string ToWireName(ImportMode mode) => mode switch
    {
        ImportMode.ReimportAll => "reimportAll",
        ImportMode.ValidateOnly => "validateOnly",
        ImportMode.DryRun => "dryRun",
        ImportMode.UpdateFeatureTree => "updateFeatureTree",
        _ => "importMissing"
    };

    private static bool ShouldValidate(ImportMode mode, bool runValidation) => mode switch
    {
        ImportMode.ValidateOnly or ImportMode.UpdateFeatureTree => true,
        ImportMode.DryRun => false,
        _ => runValidation
    };

    internal static List<ValidationCheck> ParseValidationOutput(string? output)
    {
        var checks = new List<ValidationCheck>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return checks;
        }

        ValidationCheck? current = null;
        foreach (var line in output.Split('\n'))
        {
            var text = line.TrimEnd('\r');

            var header = FeatureLineRegex().Match(text);
            if (header.Success)
            {
                current = new ValidationCheck { Feature = header.Groups["feature"].Value };
                checks.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (OkLineRegex().IsMatch(text))
            {
                current.Status = "PASS";
                continue;
            }

            var error = ErrorLineRegex().Match(text);
            if (error.Success)
            {
                current.Status = "FAIL";
                current.Message = error.Groups["message"].Value.Trim();
            }
        }

        return checks;
    }

    [GeneratedRegex(@"^Validate '(?<feature>.+)'\s*$")]
    private static partial Regex FeatureLineRegex();

    [GeneratedRegex(@"^\s*OK\s*$")]
    private static partial Regex OkLineRegex();

    [GeneratedRegex(@"^\s*ERROR:\s*(?<message>.+)$")]
    private static partial Regex ErrorLineRegex();
}
