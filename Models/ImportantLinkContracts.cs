namespace Aras.SreHub.Models;

// Persisted shape; the password is only ever stored protected (encrypted).
public sealed class ImportantLink
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? ProtectedPassword { get; set; }
}

public sealed class ImportantLinkRequest
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    // Null or empty keeps the saved password when editing.
    public string? Password { get; set; }

    public bool ClearPassword { get; set; }
}

// What the browser receives: never contains the password itself.
public sealed record ImportantLinkView(
    string Id,
    string Title,
    string Url,
    string Category,
    string Description,
    string Username,
    bool HasPassword);

public sealed record LinkPasswordResponse(string Password);
