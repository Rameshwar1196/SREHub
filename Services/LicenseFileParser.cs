using System.Text.Json;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public static class LicenseFileParser
{
    public static LicenseFile Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("The license file is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The license file is not valid JSON. {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            JsonElement licenseArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                licenseArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("license_data", out var licenseData))
            {
                licenseArray = licenseData;
            }
            else
            {
                throw new InvalidOperationException("No 'license_data' entries found in the license file.");
            }

            if (licenseArray.ValueKind != JsonValueKind.Array || licenseArray.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("No 'license_data' entries found in the license file.");
            }

            var licenses = licenseArray.EnumerateArray()
                .Select(element => new LicenseEntry
                {
                    KeyName = ReadString(element, "key_name") ?? string.Empty,
                    ValueBuildCi = ReadString(element, "key_value_build_ci"),
                    ValueAzure = ReadString(element, "key_value_azure"),
                    ExpirationDate = ReadString(element, "expiration_date"),
                    ContentType = ReadString(element, "content_type")
                })
                .ToList();

            var frameworkLicenses = new List<FrameworkLicenseEntry>();
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("framework_licenses", out var frameworkArray) &&
                frameworkArray.ValueKind == JsonValueKind.Array)
            {
                frameworkLicenses.AddRange(frameworkArray.EnumerateArray().Select(element => new FrameworkLicenseEntry
                {
                    KeyName = ReadString(element, "key_name") ?? string.Empty,
                    Value = ReadString(element, "key_value"),
                    ContentType = ReadString(element, "content_type"),
                    ExpirationDate = ReadString(element, "expiration_date")
                }));
            }

            return new LicenseFile
            {
                LicenseData = licenses,
                FrameworkLicenses = frameworkLicenses
            };
        }
    }

    public static IReadOnlyList<LicenseEntry> Filter(IReadOnlyList<LicenseEntry> licenses, string[]? names)
    {
        if (names is null || names.Length == 0)
        {
            return licenses;
        }

        var wanted = new HashSet<string>(names.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return licenses;
        }

        var filtered = licenses.Where(license => wanted.Contains(license.KeyName)).ToList();
        if (filtered.Count == 0)
        {
            throw new InvalidOperationException("None of the requested license names were found in the license file.");
        }

        return filtered;
    }

    public static LicenseVariant ParseVariant(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => LicenseVariant.Auto,
        "key_value_azure" => LicenseVariant.KeyValueAzure,
        "key_value_build_ci" => LicenseVariant.KeyValueBuildCi,
        _ => throw new InvalidOperationException($"Unknown license variant '{value}'. Use 'auto', 'key_value_azure' or 'key_value_build_ci'.")
    };

    public static string ToWireName(LicenseVariant variant) => variant switch
    {
        LicenseVariant.KeyValueBuildCi => "key_value_build_ci",
        LicenseVariant.KeyValueAzure => "key_value_azure",
        _ => "auto"
    };

    public static LicenseVariant Resolve(LicenseVariant requested, string baseUrl)
    {
        if (requested != LicenseVariant.Auto)
        {
            return requested;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? LicenseVariant.KeyValueBuildCi
            : LicenseVariant.KeyValueAzure;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }
}
