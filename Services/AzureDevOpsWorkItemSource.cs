using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public interface IWorkItemSource
{
    string Name { get; }

    bool IsConfigured { get; }

    int SyncMinutes { get; }

    string SetupHint { get; }

    IStateMapping States { get; }

    Task<IReadOnlyList<ExternalWorkItem>> GetAssignedAsync(DateTime closedSince, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalWorkItem>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken);
}

public sealed class AzureDevOpsWorkItemSource(IHttpClientFactory httpClientFactory, AzureDevOpsOptions options) : IWorkItemSource
{
    public const string ClientName = "azuredevops";

    public string Name => "Azure DevOps";

    public bool IsConfigured => options.IsConfigured;

    public int SyncMinutes => options.SyncMinutes;

    public string SetupHint => "Add Organization, Project and PersonalAccessToken under \"AzureDevOps\" in appsettings.Local.json and restart.";

    public IStateMapping States => options;

    private const string ApiVersion = "7.1";
    private const int MaxItems = 200;
    private const int BatchSize = 100;

    private static readonly string[] Fields =
    [
        "System.Id", "System.WorkItemType", "System.Title", "System.State", "System.TeamProject", "System.IterationPath",
        "Microsoft.VSTS.Common.Priority", "Microsoft.VSTS.Common.ClosedDate", "Microsoft.VSTS.Common.ResolvedDate", "System.ChangedDate"
    ];

    private string OrganizationUrl => $"https://dev.azure.com/{Uri.EscapeDataString(options.Organization)}";

    public async Task<IReadOnlyList<ExternalWorkItem>> GetAssignedAsync(DateTime closedSince, CancellationToken cancellationToken)
    {
        var query = BuildQuery(options, closedSince);
        var url = $"{OrganizationUrl}/{Uri.EscapeDataString(options.Project)}/_apis/wit/wiql?$top={MaxItems}&api-version={ApiVersion}";

        using var document = await SendAsync(url, new { query }, cancellationToken);
        var ids = document.RootElement.GetProperty("workItems").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32().ToString())
            .ToList();

        return await GetByIdsAsync(ids, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalWorkItem>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        var items = new List<ExternalWorkItem>();
        var url = $"{OrganizationUrl}/_apis/wit/workitemsbatch?api-version={ApiVersion}";
        var chunks = ids.Where(id => int.TryParse(id, out _)).Select(int.Parse).Chunk(BatchSize).ToList();

        if (chunks.Count == 0)
        {
            return items;
        }

        var iterationStarts = await GetIterationStartsAsync(cancellationToken);

        foreach (var chunk in chunks)
        {
            using var document = await SendAsync(url, new { ids = chunk, fields = Fields }, cancellationToken);
            items.AddRange(document.RootElement.GetProperty("value").EnumerateArray().Select(item => Parse(item, iterationStarts)));
        }

        return items;
    }

    public static string BuildQuery(AzureDevOpsOptions options, DateTime closedSince)
    {
        static string List(IEnumerable<string> values) => string.Join(", ", values.Select(value => $"'{value.Replace("'", "''")}'"));

        var types = options.WorkItemTypes is { Length: > 0 } ? $" AND [System.WorkItemType] IN ({List(options.WorkItemTypes)})" : string.Empty;
        var finishedRecently = options.Closed.Concat(options.Cancelled).ToList();
        var finished = finishedRecently.Concat(options.Removed);

        return $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{options.Project.Replace("'", "''")}' AND [System.AssignedTo] = @Me"
            + $" AND ([System.State] NOT IN ({List(finished)}) OR ([System.State] IN ({List(finishedRecently)}) AND [System.ChangedDate] >= '{closedSince:yyyy-MM-dd}'))"
            + $"{types} ORDER BY [System.ChangedDate] DESC";
    }

    // Iteration paths on work items omit the "Iteration" node that the classification tree includes.
    private async Task<Dictionary<string, DateTime>> GetIterationStartsAsync(CancellationToken cancellationToken)
    {
        var starts = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var url = $"{OrganizationUrl}/{Uri.EscapeDataString(options.Project)}/_apis/wit/classificationnodes/iterations?$depth=10&api-version={ApiVersion}";

        try
        {
            using var document = await SendAsync(url, null, cancellationToken);
            CollectIterationStarts(document.RootElement, starts);
        }
        catch (InvalidOperationException)
        {
            // Without iteration dates items fall back to the closed date or today.
        }

        return starts;
    }

    private static void CollectIterationStarts(JsonElement node, Dictionary<string, DateTime> starts)
    {
        if (node.TryGetProperty("attributes", out var attributes)
            && attributes.TryGetProperty("startDate", out var startDate)
            && DateTimeOffset.TryParse(startDate.GetString(), out var start))
        {
            var segments = Text(node, "path").Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();

            if (segments.Count > 1)
            {
                segments.RemoveAt(1);
            }

            starts[string.Join('\\', segments)] = start.UtcDateTime.Date;
        }

        if (node.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                CollectIterationStarts(child, starts);
            }
        }
    }

    private ExternalWorkItem Parse(JsonElement item, Dictionary<string, DateTime> iterationStarts)
    {
        var fields = item.GetProperty("fields");
        var id = item.GetProperty("id").GetInt32().ToString();
        var project = Text(fields, "System.TeamProject");
        var state = Text(fields, "System.State");
        var finished = options.Closed.Concat(options.Cancelled).Contains(state, StringComparer.OrdinalIgnoreCase);

        return new ExternalWorkItem(
            id,
            Text(fields, "System.WorkItemType"),
            Text(fields, "System.Title"),
            state,
            fields.TryGetProperty("Microsoft.VSTS.Common.Priority", out var priority) && priority.TryGetInt32(out var value) ? value : null,
            finished ? Date(fields, "Microsoft.VSTS.Common.ClosedDate") ?? Date(fields, "Microsoft.VSTS.Common.ResolvedDate") ?? Date(fields, "System.ChangedDate") : null,
            $"{OrganizationUrl}/{Uri.EscapeDataString(project.Length > 0 ? project : options.Project)}/_workitems/edit/{id}",
            iterationStarts.TryGetValue(Text(fields, "System.IterationPath"), out var iterationStart) ? iterationStart : null,
            Text(fields, "System.IterationPath"));
    }

    private static string Text(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static DateTime? Date(JsonElement fields, string name) =>
        DateTimeOffset.TryParse(Text(fields, name), out var parsed) ? parsed.ToLocalTime().Date : null;

    private async Task<JsonDocument> SendAsync(string url, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{options.PersonalAccessToken}")));

        using var response = await httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var hint = (int)response.StatusCode is 401 or 403 or 203 ? " Check that the personal access token is valid and has the Work Items (Read) scope." : string.Empty;
            throw new InvalidOperationException($"Azure DevOps answered {(int)response.StatusCode} {response.ReasonPhrase}.{hint}");
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Azure DevOps did not return JSON. Check the organization name and personal access token.");
        }
    }
}
