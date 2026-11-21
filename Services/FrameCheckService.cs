namespace Aras.SreHub.Services;

public sealed record FrameCheckResult(bool Embeddable, string Reason, string FinalUrl);

// Asks a saved link's site, headers only, whether browsers may show it inside another page.
public sealed class FrameCheckService(IHttpClientFactory httpClientFactory, ImportantLinkService links)
{
    public const string ClientName = "framecheck";

    public async Task<FrameCheckResult> CheckAsync(string id, string origin, CancellationToken cancellationToken)
    {
        var url = await links.GetUrlAsync(id, cancellationToken);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new FrameCheckResult(false, "Only web links (http or https) can be shown here.", url);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SreHub/1.0");

        try
        {
            using var response = await httpClientFactory.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var (embeddable, reason) = Evaluate(response, origin);
            return new FrameCheckResult(embeddable, reason, response.RequestMessage?.RequestUri?.ToString() ?? url);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"Could not reach {uri.Host} to check it ({exception.Message}).");
        }
    }

    public static (bool Embeddable, string Reason) Evaluate(HttpResponseMessage response, string origin)
    {
        if (response.Headers.TryGetValues("X-Frame-Options", out var frameOptions))
        {
            var value = string.Join(", ", frameOptions).Trim();

            if (value.Length > 0)
            {
                return (false, $"The site forbids being shown inside other pages (X-Frame-Options: {value}).");
            }
        }

        if (response.Headers.TryGetValues("Content-Security-Policy", out var policies))
        {
            foreach (var policy in policies)
            {
                var ancestors = policy.Split(';', StringSplitOptions.TrimEntries)
                    .FirstOrDefault(directive => directive.StartsWith("frame-ancestors", StringComparison.OrdinalIgnoreCase));

                if (ancestors is null)
                {
                    continue;
                }

                var sources = ancestors["frame-ancestors".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (!sources.Any(source => AllowsOrigin(source, origin)))
                {
                    return (false, $"The site only allows specific pages to show it (Content-Security-Policy: {ancestors}).");
                }
            }
        }

        return (true, "The site does not forbid being shown inside other pages.");
    }

    private static bool AllowsOrigin(string source, string origin) =>
        source == "*" || source.Equals("http:", StringComparison.OrdinalIgnoreCase) || source.Equals(origin, StringComparison.OrdinalIgnoreCase)
        || (origin.Contains("localhost", StringComparison.OrdinalIgnoreCase) && (source.Equals("localhost:*", StringComparison.OrdinalIgnoreCase) || source.Equals("http://localhost:*", StringComparison.OrdinalIgnoreCase)));
}
