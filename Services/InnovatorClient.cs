using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Aras.SreHub.Services;

public sealed record InnovatorFeatureLicense(string Feature, string ExpirationDate, string ActivationKey);

public sealed class InnovatorClient : IDisposable
{
    private const string FeatureLicenseServiceUrl = "https://www.aras.com/productservices/FeatureLicenseService/FeatureLicenseService.svc";
    private const string ArasServiceNamespace = "http://aras.com/";

    private readonly HttpClient _http;
    private string? _token;

    public InnovatorClient(string url, bool skipCertificateCheck)
    {
        BaseUrl = NormalizeBaseUrl(url);

        var handler = new HttpClientHandler();
        if (skipCertificateCheck)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
    }

    public string BaseUrl { get; }

    public string? Database { get; private set; }

    public static string NormalizeBaseUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        foreach (var suffix in new[] { "/Client", "/Server" })
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^suffix.Length];
            }
        }

        return trimmed;
    }

    public async Task<string> ResolveDatabaseAsync(CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/Server/dblist.aspx";
        string payload;
        try
        {
            payload = await _http.GetStringAsync(url, cancellationToken);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not read the database list from '{url}'. Provide the database explicitly. {exception.Message}");
        }

        var databases = XDocument.Parse(payload)
            .Descendants("DB")
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        if (databases.Count == 0)
        {
            throw new InvalidOperationException($"No databases returned by '{url}'. Provide the database explicitly.");
        }

        if (databases.Count > 1)
        {
            throw new InvalidOperationException($"Instance exposes multiple databases ({string.Join(", ", databases)}). Provide the database explicitly.");
        }

        return databases[0];
    }

    public async Task ConnectAsync(string database, string username, string password, bool noPasswordHash, CancellationToken cancellationToken)
    {
        Database = database;

        var tokenEndpoint = $"{BaseUrl}/OAuthServer/connect/token";

        // Instances configured for SHA256 reject the MD5 hash and expect the raw password instead.
        var secrets = noPasswordHash ? [password] : new[] { HashPassword(password), password };
        var body = string.Empty;

        foreach (var secret in secrets)
        {
            bool succeeded;
            (succeeded, body) = await RequestTokenAsync(tokenEndpoint, username, secret, database, cancellationToken);

            if (succeeded)
            {
                var token = XDocumentHelpers.ReadJsonProperty(body, "access_token");
                _token = string.IsNullOrWhiteSpace(token)
                    ? throw new InvalidOperationException($"Token endpoint '{tokenEndpoint}' did not return an access_token.")
                    : token;

                return;
            }

            if (!body.Contains("incompatible_hash_use_sha256", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        throw new InvalidOperationException($"Login failed at '{tokenEndpoint}'. {Condense(body)}");
    }

    private async Task<(bool Succeeded, string Body)> RequestTokenAsync(
        string tokenEndpoint,
        string username,
        string secret,
        string database,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["scope"] = "Innovator",
            ["client_id"] = "IOMApp",
            ["username"] = username,
            ["password"] = secret,
            ["database"] = database
        });

        using var response = await _http.PostAsync(tokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return (response.IsSuccessStatusCode, body);
    }

    public async Task ImportFeatureLicenseAsync(string licenseData, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/Server/Licensing.asmx/ImportFeatureLicense")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["encryptedFeatureLicense"] = licenseData
            })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.TryAddWithoutValidation("DATABASE", Database);

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Import rejected by Innovator. {Condense(body)}");
        }
    }

    public async Task<InnovatorFeatureLicense?> GetFeatureLicenseAsync(string licenseData, CancellationToken cancellationToken)
    {
        string criteria;
        if (licenseData.Length > 3999)
        {
            // Innovator stores license_data truncated for lookup; the product client matches the same way.
            criteria = $"<license_data condition=\"like\">{SecurityElement.Escape(licenseData[..3999])}%</license_data>";
        }
        else
        {
            criteria = $"<license_data>{SecurityElement.Escape(licenseData)}</license_data>";
        }

        var aml = $"<AML><Item type=\"Feature License\" action=\"get\" select=\"feature,expiration_date,activation_key\">{criteria}</Item></AML>";

        XElement result;
        try
        {
            result = await ApplyAmlAsync(aml, cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("No items of type", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var item = result.Elements().FirstOrDefault(element => element.Name.LocalName == "Item");
        if (item is null)
        {
            return null;
        }

        return new InnovatorFeatureLicense(
            ReadChild(item, "feature"),
            ReadChild(item, "expiration_date"),
            ReadChild(item, "activation_key"));
    }

    public Task SetValidationMethodAsync(string methodName, string methodCode, CancellationToken cancellationToken)
    {
        var name = SecurityElement.Escape(methodName);
        var aml =
            $"<AML><Item type=\"Method\" action=\"merge\" where=\"[Method].[name]='{name}'\">" +
            $"<name>{name}</name>" +
            "<method_type>C#</method_type>" +
            "<execution_allowed_to keyed_name=\"World\" type=\"Identity\">A73B655731924CD0B027E4F4D5FCC0A9</execution_allowed_to>" +
            $"<method_code>{SecurityElement.Escape(methodCode)}</method_code>" +
            "</Item></AML>";

        return ApplyAmlAsync(aml, cancellationToken);
    }

    public async Task<string> InvokeValidationMethodAsync(string methodName, CancellationToken cancellationToken)
    {
        var aml = $"<AML><Item type=\"Method\" action=\"{SecurityElement.Escape(methodName)}\" /></AML>";
        var result = await ApplyAmlAsync(aml, cancellationToken);
        return result.Value;
    }

    public Task RemoveValidationMethodAsync(string methodName, CancellationToken cancellationToken)
    {
        var aml = $"<AML><Item type=\"Method\" action=\"delete\" where=\"[Method].[name]='{SecurityElement.Escape(methodName)}'\" /></AML>";
        return ApplyAmlAsync(aml, cancellationToken);
    }

    /// <summary>
    /// Mirrors the client-side Licenses &gt; Update Feature Tree command: read the framework license key from the
    /// instance, fetch the encrypted tree from the Aras Feature License Service, then post it back to the instance.
    /// </summary>
    public async Task<string> UpdateFeatureTreeAsync(CancellationToken cancellationToken)
    {
        var serverInfo = await PostLicensingAsync("GetServerInfo", null, cancellationToken);

        var frameworkLicenseKey = System.Text.RegularExpressions.Regex
            .Match(serverInfo, "<framework_license_key[^>]*>([^<]+)</framework_license_key>")
            .Groups[1].Value;

        if (string.IsNullOrWhiteSpace(frameworkLicenseKey))
        {
            throw new InvalidOperationException(
                $"Licensing.asmx/GetServerInfo returned no framework_license_key. {Truncate(Condense(serverInfo), 240)}");
        }

        var encryptedFeatureTree = await GetFeatureTreeAsync(frameworkLicenseKey, cancellationToken);

        await PostLicensingAsync(
            "UpdateFeatureTree",
            new Dictionary<string, string> { ["encryptedFeatureTree"] = encryptedFeatureTree },
            cancellationToken);

        return "Licensing.asmx/UpdateFeatureTree";
    }

    private async Task<string> GetFeatureTreeAsync(string frameworkLicenseKey, CancellationToken cancellationToken)
    {
        var soap =
            "<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\"><SOAP-ENV:Body>" +
            "<GetFeatureTree xmlns=\"http://aras.com/\">" +
            $"<frameworkLicenseKey>{SecurityElement.Escape(frameworkLicenseKey)}</frameworkLicenseKey>" +
            "</GetFeatureTree></SOAP-ENV:Body></SOAP-ENV:Envelope>";

        using var request = new HttpRequestMessage(HttpMethod.Post, FeatureLicenseServiceUrl)
        {
            Content = new StringContent(soap, Encoding.UTF8, "text/xml")
        };

        request.Headers.TryAddWithoutValidation("SOAPAction", $"{ArasServiceNamespace}IFeatureLicenseService/GetFeatureTree");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Aras Feature License Service returned {(int)response.StatusCode} for GetFeatureTree. {Truncate(Condense(body), 240)}");
        }

        string tree;
        try
        {
            tree = XDocument.Parse(body)
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("GetFeatureTreeResult", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? string.Empty;
        }
        catch (Exception)
        {
            throw new InvalidOperationException($"Unexpected GetFeatureTree response. {Truncate(Condense(body), 240)}");
        }

        return string.IsNullOrWhiteSpace(tree)
            ? throw new InvalidOperationException($"GetFeatureTree returned an empty feature tree. {Truncate(Condense(body), 240)}")
            : tree;
    }

    private async Task<string> PostLicensingAsync(
        string operation,
        Dictionary<string, string>? parameters,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/Server/Licensing.asmx/{operation}")
        {
            Content = new FormUrlEncodedContent(parameters ?? [])
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.TryAddWithoutValidation("DATABASE", Database);

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return response.IsSuccessStatusCode
            ? body
            : throw new InvalidOperationException(
                $"Licensing.asmx/{operation} returned {(int)response.StatusCode} {response.StatusCode}. {Truncate(Condense(body), 240)}");
    }

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : $"{text[..length]}...";

    private async Task<XElement> ApplyAmlAsync(string aml, CancellationToken cancellationToken)
    {
        var soap =
            "<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            $"<SOAP-ENV:Body><ApplyItem>{aml}</ApplyItem></SOAP-ENV:Body></SOAP-ENV:Envelope>";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/Server/InnovatorServer.aspx")
        {
            Content = new StringContent(soap, Encoding.UTF8, "text/xml")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.TryAddWithoutValidation("SOAPAction", "ApplyItem");
        request.Headers.TryAddWithoutValidation("DATABASE", Database);

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        XDocument document;
        try
        {
            document = XDocument.Parse(body);
        }
        catch (Exception)
        {
            throw new InvalidOperationException($"Unexpected response from Innovator. {Condense(body)}");
        }

        var soapBody = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Body")
            ?? throw new InvalidOperationException($"Unexpected response from Innovator. {Condense(body)}");

        var fault = soapBody.Elements().FirstOrDefault(element => element.Name.LocalName == "Fault");
        if (fault is not null)
        {
            var detail = fault.Elements().FirstOrDefault(element => element.Name.LocalName == "faultstring")?.Value
                ?? fault.Value;
            throw new InvalidOperationException($"Innovator fault: {Condense(detail)}");
        }

        // Innovator returns <Result> directly under <Body> for most actions, but wraps it in
        // <ApplyItemResponse> for others (for example Method delete).
        return soapBody.Elements().FirstOrDefault(element => element.Name.LocalName == "Result")
            ?? soapBody.Descendants().FirstOrDefault(element => element.Name.LocalName == "Result")
            ?? throw new InvalidOperationException($"Innovator returned no <Result>. {Condense(body)}");
    }

    public static string HashPassword(string password)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool ExpirationMatches(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        const DateTimeStyles styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
        if (DateTime.TryParse(expected, CultureInfo.InvariantCulture, styles, out var expectedDate) &&
            DateTime.TryParse(actual, CultureInfo.InvariantCulture, styles, out var actualDate))
        {
            return expectedDate.Date == actualDate.Date;
        }

        return string.Equals(expected.Trim(), actual.Trim(), StringComparison.Ordinal);
    }

    private static string ReadChild(XElement item, string name) =>
        item.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? string.Empty;

    private static string Condense(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    public void Dispose() => _http.Dispose();
}

internal static class XDocumentHelpers
{
    public static string? ReadJsonProperty(string json, string propertyName)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }
}
