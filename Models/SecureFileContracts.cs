namespace Aras.SreHub.Models;

public sealed class SecureFolder
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class SecureFileVersion
{
    public int Number { get; set; }
    public long Size { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
}

public sealed class SecureFile
{
    public string Id { get; set; } = string.Empty;
    public string? FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Other";
    public string Environment { get; set; } = "N/A";
    public string Infrastructure { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? DeletedAt { get; set; }
    public List<SecureFileVersion> Versions { get; set; } = [];
}

// Persisted index. Blob ids live only in StoredFile and are never sent to the browser.
public sealed class SecureStore
{
    public List<SecureFolder> Folders { get; set; } = [];
    public List<StoredFile> Files { get; set; } = [];
}

public sealed class StoredVersion
{
    public string BlobId { get; set; } = string.Empty;
    public int Number { get; set; }
    public long Size { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
}

public sealed class StoredFile
{
    public string Id { get; set; } = string.Empty;
    public string? FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Other";
    public string Environment { get; set; } = "N/A";
    public string Infrastructure { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? DeletedAt { get; set; }
    public List<StoredVersion> Versions { get; set; } = [];
}

public sealed class SecureTree
{
    public IReadOnlyList<SecureFolder> Folders { get; set; } = [];
    public IReadOnlyList<SecureFile> Files { get; set; } = [];
}

public sealed class SecureTrash
{
    public IReadOnlyList<SecureFolder> Folders { get; set; } = [];
    public IReadOnlyList<SecureFile> Files { get; set; } = [];
    public int RetentionDays { get; set; }
}

public sealed class SecureFolderRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
}

public sealed class SecureFileMetaRequest
{
    public string Name { get; set; } = string.Empty;
    public string? FolderId { get; set; }
    public string Type { get; set; } = "Other";
    public string Environment { get; set; } = "N/A";
    public string Infrastructure { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class SecureAuditEntry
{
    public string At { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}

public sealed class SecureText
{
    public string Text { get; set; } = string.Empty;
}
