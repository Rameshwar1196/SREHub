using System.Text.Json;
using Aras.SreHub.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Aras.SreHub.Services;

public sealed class ImportantLinkService
{
    private const string DefaultCategory = "Other";
    private const int MaxPasswordLength = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly ImportantLink[] Starters =
    [
        new() { Id = "starter-outlook", Title = "Outlook", Url = "https://outlook.office365.com/", Category = "Prorigo", Description = "Prorigo mail and calendar." },
        new() { Id = "starter-protrak", Title = "Protrak", Url = "https://app.protrak.io/", Category = "Prorigo", Description = "Timesheets and project tracking." },
        new() { Id = "starter-greythr", Title = "greytHR", Url = "https://prorigosoftware.greythr.com/", Category = "Prorigo", Description = "Attendance, leave and payslips." },
        new() { Id = "starter-azure-portal", Title = "Azure Portal", Url = "https://portal.azure.com", Category = "Aras", Description = "Subscriptions, resources and Key Vaults." },
        new() { Id = "starter-azure-devops", Title = "Azure DevOps", Url = "https://dev.azure.com", Category = "Aras", Description = "Repos, pipelines and work items." },
        new() { Id = "starter-pagerduty", Title = "PagerDuty", Url = "https://gcs-com.pagerduty.com", Category = "Aras", Description = "Incidents and on-call schedules." }
    ];

    private readonly string _storePath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ImportantLinkService(IWebHostEnvironment environment, IDataProtectionProvider dataProtection)
    {
        _storePath = Path.Combine(environment.ContentRootPath, "App_Data", "links.json");
        _protector = dataProtection.CreateProtector("SreHub.ImportantLinks.Credentials.v1");
    }

    public async Task<IReadOnlyList<ImportantLinkView>> GetAllAsync(CancellationToken cancellationToken)
    {
        var links = await LoadAsync(cancellationToken);
        return links.OrderBy(link => link.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToView)
            .ToList();
    }

    public async Task<string> RevealPasswordAsync(string id, CancellationToken cancellationToken)
    {
        var links = await LoadAsync(cancellationToken);
        var link = links.Find(existing => existing.Id == id)
            ?? throw new FileNotFoundException("That link no longer exists.");

        if (string.IsNullOrEmpty(link.ProtectedPassword))
        {
            throw new FileNotFoundException("No password is saved for this link.");
        }

        try
        {
            return _protector.Unprotect(link.ProtectedPassword);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException("The saved password can no longer be decrypted on this machine. Save it again.");
        }
    }

    public async Task<ImportantLinkView> SaveAsync(string? id, ImportantLinkRequest incoming, CancellationToken cancellationToken)
    {
        var link = Normalize(incoming);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var links = await LoadAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(id))
            {
                link.Id = Guid.NewGuid().ToString("N");
                link.ProtectedPassword = ProtectOrNull(incoming.Password);
                links.Add(link);
            }
            else
            {
                var index = links.FindIndex(existing => existing.Id == id);

                if (index < 0)
                {
                    throw new FileNotFoundException("That link no longer exists.");
                }

                link.Id = id;
                link.ProtectedPassword = incoming.ClearPassword
                    ? null
                    : ProtectOrNull(incoming.Password) ?? links[index].ProtectedPassword;
                links[index] = link;
            }

            await PersistAsync(links, cancellationToken);
            return ToView(link);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var links = await LoadAsync(cancellationToken);

            if (links.RemoveAll(existing => existing.Id == id) == 0)
            {
                throw new FileNotFoundException("That link no longer exists.");
            }

            await PersistAsync(links, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static ImportantLink Normalize(ImportantLinkRequest incoming)
    {
        var title = Limit(incoming.Title, 100, "Title");
        var url = Limit(incoming.Url, 2000, "URL");

        if (title.Length == 0)
        {
            throw new ArgumentException("A title is required.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The URL must start with http:// or https://.");
        }

        if (incoming.Password is { Length: > MaxPasswordLength })
        {
            throw new ArgumentException($"Password is limited to {MaxPasswordLength} characters.");
        }

        var category = Limit(incoming.Category, 50, "Category");

        return new ImportantLink
        {
            Title = title,
            Url = uri.ToString(),
            Category = category.Length == 0 ? DefaultCategory : category,
            Description = Limit(incoming.Description, 300, "Description"),
            Username = Limit(incoming.Username, 200, "Username")
        };
    }

    private static ImportantLinkView ToView(ImportantLink link) => new(
        link.Id,
        link.Title,
        link.Url,
        link.Category,
        link.Description,
        link.Username,
        !string.IsNullOrEmpty(link.ProtectedPassword));

    private string? ProtectOrNull(string? password) =>
        string.IsNullOrEmpty(password) ? null : _protector.Protect(password);

    private static string Limit(string? value, int maxLength, string label)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{label} is limited to {maxLength} characters.");
        }

        return trimmed;
    }

    // Until the first save the file does not exist, so the starter links are shown instead.
    private async Task<List<ImportantLink>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return [.. Starters.Select(starter => new ImportantLink
            {
                Id = starter.Id,
                Title = starter.Title,
                Url = starter.Url,
                Category = starter.Category,
                Description = starter.Description
            })];
        }

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<List<ImportantLink>>(stream, SerializerOptions, cancellationToken) ?? [];
    }

    private async Task PersistAsync(List<ImportantLink> links, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);

        var temporary = _storePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(links, SerializerOptions), cancellationToken);
        File.Move(temporary, _storePath, overwrite: true);
    }
}
