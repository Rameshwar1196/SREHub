using System.Globalization;
using System.Text.Json;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

// Reads the Support Incidents assigned to the user from MyInnovator (the system behind the Internal Portal); never writes.
public sealed class SrePortalWorkItemSource(SrePortalOptions options) : IWorkItemSource
{
    private const int PageSize = 100;
    private const int MaxPages = 5;
    private const int LookupBatch = 20;
    private const string Fields = "id,incident_number,summary,state,priority_level,created_on,closed_on";

    private string? _itemTypeName;

    public string Name => "SRE Portal";

    public bool IsConfigured => options.IsConfigured;

    public int SyncMinutes => options.SyncMinutes;

    public string SetupHint => "Add Username, Password and AssigneeName under \"SrePortal\" in appsettings.Local.json and restart.";

    public IStateMapping States => options;

    public async Task<IReadOnlyList<ExternalWorkItem>> GetAssignedAsync(DateTime closedSince, CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(cancellationToken);
        return await QueryAsync(client, BuildAssignedFilter(options, closedSince), cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalWorkItem>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        using var client = await ConnectAsync(cancellationToken);
        var items = new List<ExternalWorkItem>();

        foreach (var chunk in ids.Chunk(LookupBatch))
        {
            items.AddRange(await QueryAsync(client, BuildLookupFilter(chunk), cancellationToken));
        }

        return items;
    }

    public static string BuildAssignedFilter(SrePortalOptions options, DateTime closedSince)
    {
        var assignee = $"assignee/keyed_name eq {Quote(options.AssigneeName)}";
        var parts = new List<string> { assignee };

        if (!string.IsNullOrWhiteSpace(options.Classification))
        {
            parts.Add($"classification eq {Quote(options.Classification)}");
        }

        var open = string.Join(" and ", options.Closed.Select(state => $"state ne {Quote(state)}"));
        var closed = string.Join(" or ", options.Closed.Select(state => $"state eq {Quote(state)}"));
        parts.Add($"(({open}) or (({closed}) and closed_on ge {closedSince:yyyy-MM-dd}T00:00:00))");

        return string.Join(" and ", parts);
    }

    public static string BuildLookupFilter(IEnumerable<string> incidentNumbers) =>
        "(" + string.Join(" or ", incidentNumbers.Select(number => $"incident_number eq {Quote(number)}")) + ")";

    public static ExternalWorkItem? Parse(JsonElement row, SrePortalOptions options, string? itemTypeName = null)
    {
        var number = Text(row, "incident_number");

        if (number.Length == 0)
        {
            return null;
        }

        var state = Text(row, "state");
        var closed = options.Closed.Contains(state, StringComparer.OrdinalIgnoreCase);
        var itemId = Text(row, "id");
        var typeName = itemTypeName ?? options.ItemType;

        return new ExternalWorkItem(
            number,
            typeName,
            Text(row, "summary"),
            state,
            PriorityOf(Text(row, "priority_level")),
            closed ? DateOf(Text(row, "closed_on")) ?? DateOf(Text(row, "created_on")) : null,
            itemId.Length > 0 ? $"{options.PortalUrl.TrimEnd('/')}/?StartItem={Uri.EscapeDataString(typeName)}:{itemId}" : options.PortalUrl);
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty
            }
            : string.Empty;

    private static DateTime? DateOf(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToLocalTime().Date : null;

    // Priority labels vary ("1", "P2", "Critical"); anything not recognised stays normal.
    private static int? PriorityOf(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());

        if (digits.Length > 0 && int.TryParse(digits[..1], out var level))
        {
            return level;
        }

        return text.Contains("critical", StringComparison.OrdinalIgnoreCase) || text.Contains("urgent", StringComparison.OrdinalIgnoreCase) ? 1
            : text.Contains("low", StringComparison.OrdinalIgnoreCase) ? 4
            : null;
    }

    private async Task<InnovatorClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new InnovatorClient(options.BaseUrl, skipCertificateCheck: false);

        try
        {
            await client.ConnectAsync(options.Database, options.Username, options.Password, noPasswordHash: false, cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // The label shown in the portal ("Support Incident") is not always the technical item type name, so it is looked up once.
    private async Task<string> ResolveItemTypeAsync(InnovatorClient client, CancellationToken cancellationToken)
    {
        if (_itemTypeName is not null)
        {
            return _itemTypeName;
        }

        var configured = options.ItemType.Trim();
        var problems = new List<string>();
        var entitySets = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(await client.GetODataAsync(string.Empty, cancellationToken));
            entitySets = document.RootElement.TryGetProperty("value", out var sets)
                ? sets.EnumerateArray().Select(set => Text(set, "name")).Where(name => name.Length > 0).ToList()
                : [];

            var wanted = Normalize(configured);
            var found = entitySets.FirstOrDefault(name => Normalize(name) == wanted);

            if (found is not null)
            {
                return _itemTypeName = found;
            }
        }
        catch (InvalidOperationException exception)
        {
            problems.Add($"entity list: {exception.Message}");
        }

        try
        {
            var exact = $"label eq {Quote(configured)} or name eq {Quote(configured)} or name eq {Quote(configured.Replace(' ', '_'))}";
            var match = (await LookupItemTypesAsync(client, exact, cancellationToken))
                .OrderByDescending(found => string.Equals(found.Label, configured, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (match.Name is { Length: > 0 })
            {
                return _itemTypeName = match.Name;
            }
        }
        catch (InvalidOperationException exception)
        {
            problems.Add($"item type lookup: {exception.Message}");
        }

        var keyword = Normalize(configured.Split(' ')[^1]);
        var similar = entitySets.Where(name => Normalize(name).Contains(keyword, StringComparison.Ordinal)).Take(15).ToList();

        throw new InvalidOperationException(
            $"MyInnovator has no item type '{configured}'."
            + (similar.Count > 0 ? $" Similar names: {string.Join(", ", similar)}. Set \"ItemType\" under \"SrePortal\" to the right one." : string.Empty)
            + (entitySets.Count > 0 ? $" ({entitySets.Count} entities are available to this login.)" : string.Empty)
            + (problems.Count > 0 ? $" Details: {string.Join("; ", problems)}" : string.Empty));
    }

    private static string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static async Task<List<(string Name, string Label)>> LookupItemTypesAsync(InnovatorClient client, string filter, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await client.GetODataAsync($"ItemType?$select=name,label&$top=10&$filter={Uri.EscapeDataString(filter)}", cancellationToken));

        return document.RootElement.TryGetProperty("value", out var rows)
            ? rows.EnumerateArray().Select(row => (Text(row, "name"), Text(row, "label"))).Where(found => found.Item1.Length > 0).ToList()
            : [];
    }

    private async Task<IReadOnlyList<ExternalWorkItem>> QueryAsync(InnovatorClient client, string filter, CancellationToken cancellationToken)
    {
        var typeName = await ResolveItemTypeAsync(client, cancellationToken);
        var query = $"?$select={Fields}&$filter={Uri.EscapeDataString(filter)}&$top={PageSize}";
        var items = new List<ExternalWorkItem>();

        // Item type names containing spaces are sent as they are; the underscore form is only a fallback for servers that expect it.
        var variants = new[] { typeName, typeName.Replace(' ', '_') }.Distinct().ToList();
        string? failure = null;
        string? next = null;
        string? firstPage = null;

        foreach (var variant in variants)
        {
            try
            {
                next = Uri.EscapeDataString(variant) + query;
                firstPage = await client.GetODataAsync(next, cancellationToken);
                _itemTypeName = variant;
                break;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("404", StringComparison.Ordinal))
            {
                failure ??= exception.Message;
            }
        }

        if (firstPage is null)
        {
            throw new InvalidOperationException($"{failure} Tried: {string.Join(", ", variants.Select(variant => $"'{variant}'"))}. The login may not be allowed to read this item type through the API.");
        }

        for (var page = 0; page < MaxPages; page++)
        {
            using var document = JsonDocument.Parse(firstPage);

            if (document.RootElement.TryGetProperty("value", out var rows))
            {
                items.AddRange(rows.EnumerateArray().Select(row => Parse(row, options, typeName)).OfType<ExternalWorkItem>());
            }

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;

            if (next is null)
            {
                break;
            }

            firstPage = await client.GetODataAsync(next, cancellationToken);
        }

        return items;
    }
}
