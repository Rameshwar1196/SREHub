using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aras.SreHub.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Aras.SreHub.Services;

// Files and the index that describes them are encrypted with the application's data-protection keys,
// which on Windows are bound to the signed-in account.
public sealed class SecureFileService
{
    public const int RetentionDays = 30;

    private const int MaxVersions = 5;
    private const int MaxTextBytes = 64 * 1024;
    private const int MaxFileNameLength = 200;
    private const int MaxFolderNameLength = 100;
    private const int MaxNotesLength = 2000;

    private static readonly string[] Infrastructures = ["NPRD", "PROD", "TEST"];
    private static readonly string[] Environments = ["DEV", "SIT", "TEST", "STG", "PROD", "N/A"];

    // The infrastructure decides which environments make sense; files saved before it existed have none and accept any.
    private static readonly Dictionary<string, string[]> EnvironmentsByInfrastructure = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NPRD"] = ["DEV", "SIT", "N/A"],
        ["PROD"] = ["STG", "PROD", "N/A"],
        ["TEST"] = ["TEST", "N/A"]
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _indexPath;
    private readonly string _blobDirectory;
    private readonly string _auditPath;
    private readonly IDataProtector _protector;
    private readonly string[] _types;
    private readonly string[] _seedFolders;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SecureFileService(IWebHostEnvironment environment, IDataProtectionProvider dataProtection, SecureVaultProfile? profile = null)
    {
        profile ??= SecureVaultProfile.Operations;
        var root = Path.Combine(environment.ContentRootPath, "App_Data", profile.Directory);
        _blobDirectory = Path.Combine(root, "blobs");
        _indexPath = Path.Combine(root, "index.bin");
        _auditPath = Path.Combine(root, "audit.log");
        _protector = dataProtection.CreateProtector(profile.Purpose);
        _types = profile.Types;
        _seedFolders = profile.SeedFolders;
    }

    public async Task<SecureTree> GetTreeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);

            if (PurgeExpired(store))
            {
                await SaveAsync(store, cancellationToken);
            }

            return new SecureTree
            {
                Folders = store.Folders.Where(folder => folder.DeletedAt is null).OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                Files = store.Files.Where(file => file.DeletedAt is null).Select(ToView).OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SecureFolder> CreateFolderAsync(string name, string? parentId, string user, CancellationToken cancellationToken)
    {
        var clean = CleanFolderName(name);

        return await MutateAsync(cancellationToken, store =>
        {
            RequireFolder(store, parentId);
            RequireUniqueFolder(store, parentId, clean, null);

            var folder = new SecureFolder { Id = NewId(), Name = clean, ParentId = NullIfEmpty(parentId) };
            store.Folders.Add(folder);
            Audit(user, "create-folder", PathOf(store, folder.Id));
            return folder;
        });
    }

    public async Task<SecureFolder> UpdateFolderAsync(string id, string name, string? parentId, string user, CancellationToken cancellationToken)
    {
        var clean = CleanFolderName(name);

        return await MutateAsync(cancellationToken, store =>
        {
            var folder = RequireFolder(store, id)!;
            var target = NullIfEmpty(parentId);

            if (target is not null && (target == id || Descendants(store, id).Contains(target)))
            {
                throw new ArgumentException("A folder cannot be moved into itself.");
            }

            RequireFolder(store, target);
            RequireUniqueFolder(store, target, clean, id);

            var before = PathOf(store, id);
            folder.Name = clean;
            folder.ParentId = target;
            Audit(user, "rename-folder", $"{before} -> {PathOf(store, id)}");
            return folder;
        });
    }

    public async Task DeleteFolderAsync(string id, string user, CancellationToken cancellationToken)
    {
        await MutateAsync(cancellationToken, store =>
        {
            RequireFolder(store, id);
            var now = DateTimeOffset.UtcNow;
            var path = PathOf(store, id);
            var affected = Descendants(store, id);
            affected.Add(id);

            foreach (var folder in store.Folders.Where(folder => affected.Contains(folder.Id) && folder.DeletedAt is null))
            {
                folder.DeletedAt = now;
            }

            foreach (var file in store.Files.Where(file => file.FolderId is not null && affected.Contains(file.FolderId) && file.DeletedAt is null))
            {
                file.DeletedAt = now;
            }

            Audit(user, "delete-folder", path);
            return true;
        });
    }

    public async Task<SecureFile> AddFileAsync(string? folderId, string fileName, Stream content, SecureFileMetaRequest meta, string user, CancellationToken cancellationToken)
    {
        var name = CleanFileName(fileName);
        var data = await ReadAllAsync(content, cancellationToken);
        var blobId = NewId();

        await WriteBlobAsync(blobId, data, cancellationToken);

        try
        {
            return await MutateAsync(cancellationToken, store =>
            {
                var folder = NullIfEmpty(folderId);
                RequireFolder(store, folder);
                RequireUniqueFile(store, folder, name, null);

                var placement = CleanPlacement(meta.Infrastructure, meta.Environment);

                var file = new StoredFile
                {
                    Id = NewId(),
                    FolderId = folder,
                    Name = name,
                    Type = Choice(meta.Type, _types, "Type"),
                    Environment = placement.Environment,
                    Infrastructure = placement.Infrastructure,
                    Expiry = CleanExpiry(meta.Expiry),
                    Notes = CleanNotes(meta.Notes),
                    UpdatedAt = DateTimeOffset.UtcNow,
                    UpdatedBy = user,
                    Versions = [new StoredVersion { BlobId = blobId, Number = 1, Size = data.Length, UploadedAt = DateTimeOffset.UtcNow, UploadedBy = user }]
                };

                store.Files.Add(file);
                Audit(user, "upload", FilePath(store, file));
                return ToView(file);
            });
        }
        catch
        {
            DeleteBlob(blobId);
            throw;
        }
    }

    public async Task<SecureFile> UpdateFileAsync(string id, SecureFileMetaRequest meta, string user, CancellationToken cancellationToken)
    {
        var name = CleanFileName(meta.Name);

        return await MutateAsync(cancellationToken, store =>
        {
            var file = RequireFile(store, id);
            var folder = NullIfEmpty(meta.FolderId);
            RequireFolder(store, folder);
            RequireUniqueFile(store, folder, name, id);

            var before = FilePath(store, file);
            file.Name = name;
            file.FolderId = folder;
            file.Type = Choice(meta.Type, _types, "Type");
            var placement = CleanPlacement(meta.Infrastructure, meta.Environment);
            file.Environment = placement.Environment;
            file.Infrastructure = placement.Infrastructure;
            file.Expiry = CleanExpiry(meta.Expiry);
            file.Notes = CleanNotes(meta.Notes);
            file.UpdatedAt = DateTimeOffset.UtcNow;
            file.UpdatedBy = user;
            Audit(user, "edit", before == FilePath(store, file) ? before : $"{before} -> {FilePath(store, file)}");
            return ToView(file);
        });
    }

    public async Task<SecureFile> AddVersionAsync(string id, Stream content, string user, CancellationToken cancellationToken)
    {
        var data = await ReadAllAsync(content, cancellationToken);
        var blobId = NewId();
        var removed = new List<string>();

        await WriteBlobAsync(blobId, data, cancellationToken);

        try
        {
            var result = await MutateAsync(cancellationToken, store =>
            {
                var file = RequireFile(store, id);
                var number = file.Versions.Count == 0 ? 1 : file.Versions.Max(version => version.Number) + 1;
                file.Versions.Add(new StoredVersion { BlobId = blobId, Number = number, Size = data.Length, UploadedAt = DateTimeOffset.UtcNow, UploadedBy = user });

                while (file.Versions.Count > MaxVersions)
                {
                    var oldest = file.Versions.OrderBy(version => version.Number).First();
                    removed.Add(oldest.BlobId);
                    file.Versions.Remove(oldest);
                }

                file.UpdatedAt = DateTimeOffset.UtcNow;
                file.UpdatedBy = user;
                Audit(user, "new-version", $"{FilePath(store, file)} (v{number})");
                return ToView(file);
            });

            removed.ForEach(DeleteBlob);
            return result;
        }
        catch
        {
            DeleteBlob(blobId);
            throw;
        }
    }

    public async Task<(byte[] Data, string Name)> OpenAsync(string id, int? version, string user, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);
            var file = RequireFile(store, id);
            var chosen = PickVersion(file, version);
            var data = await ReadBlobAsync(chosen.BlobId, cancellationToken);

            // The access is logged before any data is returned.
            Audit(user, "download", $"{FilePath(store, file)} (v{chosen.Number})");
            return (data, file.Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SecureText> ReadTextAsync(string id, string user, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);
            var file = RequireFile(store, id);
            var latest = PickVersion(file, null);

            if (latest.Size > MaxTextBytes)
            {
                throw new InvalidOperationException($"Only files up to {MaxTextBytes / 1024} KB can be copied as text. Download this one instead.");
            }

            var data = await ReadBlobAsync(latest.BlobId, cancellationToken);

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidOperationException("This file is not text. Download it instead.");
            }

            Audit(user, "copy-text", FilePath(store, file));
            return new SecureText { Text = text };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteFileAsync(string id, string user, CancellationToken cancellationToken)
    {
        await MutateAsync(cancellationToken, store =>
        {
            var file = RequireFile(store, id);
            file.DeletedAt = DateTimeOffset.UtcNow;
            Audit(user, "delete", FilePath(store, file));
            return true;
        });
    }

    public async Task<SecureTrash> GetTrashAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);

            if (PurgeExpired(store))
            {
                await SaveAsync(store, cancellationToken);
            }

            return new SecureTrash
            {
                RetentionDays = RetentionDays,
                Folders = store.Folders.Where(folder => folder.DeletedAt is not null).OrderByDescending(folder => folder.DeletedAt).ToList(),
                Files = store.Files.Where(file => file.DeletedAt is not null).Select(ToView).OrderByDescending(file => file.DeletedAt).ToList()
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(string kind, string id, string user, CancellationToken cancellationToken)
    {
        RequireKind(kind);

        await MutateAsync(cancellationToken, store =>
        {
            if (kind == "file")
            {
                var file = store.Files.Find(existing => existing.Id == id && existing.DeletedAt is not null)
                    ?? throw new FileNotFoundException("That file is not in the recycle bin.");

                var folder = store.Folders.Find(existing => existing.Id == file.FolderId);
                file.FolderId = folder is { DeletedAt: null } ? file.FolderId : null;
                file.Name = UniqueFileName(store, file.FolderId, file.Name, file.Id);
                file.DeletedAt = null;
                Audit(user, "restore", FilePath(store, file));
                return true;
            }

            var restored = store.Folders.Find(existing => existing.Id == id && existing.DeletedAt is not null)
                ?? throw new FileNotFoundException("That folder is not in the recycle bin.");

            var stamp = restored.DeletedAt;
            var parent = store.Folders.Find(existing => existing.Id == restored.ParentId);
            restored.ParentId = parent is { DeletedAt: null } ? restored.ParentId : null;
            restored.Name = UniqueFolderName(store, restored.ParentId, restored.Name, restored.Id);

            // Contents that were removed together with this folder come back with it.
            foreach (var folder in store.Folders.Where(folder => folder.DeletedAt == stamp))
            {
                folder.DeletedAt = null;
            }

            foreach (var file in store.Files.Where(file => file.DeletedAt == stamp))
            {
                file.DeletedAt = null;
            }

            Audit(user, "restore-folder", PathOf(store, restored.Id));
            return true;
        });
    }

    public async Task PurgeAsync(string kind, string id, string user, CancellationToken cancellationToken)
    {
        RequireKind(kind);

        var blobs = new List<string>();

        await MutateAsync(cancellationToken, store =>
        {
            if (kind == "file")
            {
                var file = store.Files.Find(existing => existing.Id == id && existing.DeletedAt is not null)
                    ?? throw new FileNotFoundException("That file is not in the recycle bin.");

                blobs.AddRange(file.Versions.Select(version => version.BlobId));
                store.Files.Remove(file);
                Audit(user, "purge", file.Name);
                return true;
            }

            var folder = store.Folders.Find(existing => existing.Id == id && existing.DeletedAt is not null)
                ?? throw new FileNotFoundException("That folder is not in the recycle bin.");

            var affected = Descendants(store, id);
            affected.Add(id);

            foreach (var file in store.Files.Where(file => file.FolderId is not null && affected.Contains(file.FolderId) && file.DeletedAt is not null).ToList())
            {
                blobs.AddRange(file.Versions.Select(version => version.BlobId));
                store.Files.Remove(file);
            }

            store.Folders.RemoveAll(existing => affected.Contains(existing.Id) && existing.DeletedAt is not null);
            Audit(user, "purge-folder", folder.Name);
            return true;
        });

        blobs.ForEach(DeleteBlob);
    }

    public Task<IReadOnlyList<SecureAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<SecureAuditEntry> entries = [];

        if (File.Exists(_auditPath))
        {
            entries = File.ReadLines(_auditPath)
                .Reverse()
                .Take(Math.Clamp(limit, 1, 1000))
                .Select(line => line.Split('\t'))
                .Where(parts => parts.Length >= 4)
                .Select(parts => new SecureAuditEntry { At = parts[0], User = parts[1], Action = parts[2], Target = parts[3] })
                .ToList();
        }

        return Task.FromResult(entries);
    }

    private static void RequireKind(string kind)
    {
        if (kind is not ("file" or "folder"))
        {
            throw new ArgumentException("Kind must be file or folder.");
        }
    }

    private async Task<T> MutateAsync<T>(CancellationToken cancellationToken, Func<SecureStore, T> change)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadAsync(cancellationToken);
            var result = change(store);
            await SaveAsync(store, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SecureStore> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath))
        {
            var fresh = new SecureStore { Folders = _seedFolders.Select(name => new SecureFolder { Id = NewId(), Name = name }).ToList() };

            if (fresh.Folders.Count > 0)
            {
                await SaveAsync(fresh, cancellationToken);
            }

            return fresh;
        }

        try
        {
            var plain = _protector.Unprotect(await File.ReadAllBytesAsync(_indexPath, cancellationToken));
            var store = JsonSerializer.Deserialize<SecureStore>(plain, SerializerOptions) ?? new SecureStore();

            foreach (var file in store.Files)
            {
                file.Environment = CanonicalEnvironment(file.Environment);
            }

            return store;
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("The secure files cannot be decrypted. They can only be opened by the Windows account that saved them, with the key store in App_Data\\keys.");
        }
    }

    private async Task SaveAsync(SecureStore store, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);

        var temporary = _indexPath + ".tmp";
        await File.WriteAllBytesAsync(temporary, _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(store, SerializerOptions)), cancellationToken);

        if (File.Exists(_indexPath))
        {
            File.Copy(_indexPath, _indexPath + ".bak", overwrite: true);
        }

        File.Move(temporary, _indexPath, overwrite: true);
    }

    private async Task WriteBlobAsync(string blobId, byte[] data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_blobDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_blobDirectory, blobId + ".bin"), _protector.Protect(data), cancellationToken);
    }

    private async Task<byte[]> ReadBlobAsync(string blobId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_blobDirectory, blobId + ".bin");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The stored data for this file is missing.");
        }

        try
        {
            return _protector.Unprotect(await File.ReadAllBytesAsync(path, cancellationToken));
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("This file cannot be decrypted on this machine or account.");
        }
    }

    private void DeleteBlob(string blobId)
    {
        try
        {
            File.Delete(Path.Combine(_blobDirectory, blobId + ".bin"));
        }
        catch (IOException)
        {
            // A leftover encrypted blob is harmless and is not referenced by the index.
        }
    }

    // Anything in the recycle bin for longer than the retention period is removed for good.
    private bool PurgeExpired(SecureStore store)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        var expiredFiles = store.Files.Where(file => file.DeletedAt is not null && file.DeletedAt < cutoff).ToList();
        var expiredFolders = store.Folders.Where(folder => folder.DeletedAt is not null && folder.DeletedAt < cutoff).ToList();

        foreach (var file in expiredFiles)
        {
            file.Versions.ForEach(version => DeleteBlob(version.BlobId));
            store.Files.Remove(file);
        }

        foreach (var folder in expiredFolders)
        {
            store.Folders.Remove(folder);
        }

        return expiredFiles.Count + expiredFolders.Count > 0;
    }

    private void Audit(string user, string action, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_auditPath)!);

        static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        File.AppendAllText(_auditPath, $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\t{Clean(user)}\t{action}\t{Clean(target)}{Environment.NewLine}");
    }

    private static SecureFile ToView(StoredFile file) => new()
    {
        Id = file.Id,
        FolderId = file.FolderId,
        Name = file.Name,
        Type = file.Type,
        Environment = file.Environment,
        Infrastructure = file.Infrastructure,
        Expiry = file.Expiry,
        Notes = file.Notes,
        Size = file.Versions.Count == 0 ? 0 : file.Versions.OrderBy(version => version.Number).Last().Size,
        UpdatedAt = file.UpdatedAt,
        UpdatedBy = file.UpdatedBy,
        DeletedAt = file.DeletedAt,
        Versions = file.Versions.OrderByDescending(version => version.Number)
            .Select(version => new SecureFileVersion { Number = version.Number, Size = version.Size, UploadedAt = version.UploadedAt, UploadedBy = version.UploadedBy })
            .ToList()
    };

    private static StoredVersion PickVersion(StoredFile file, int? number)
    {
        var chosen = number is null
            ? file.Versions.OrderBy(version => version.Number).LastOrDefault()
            : file.Versions.Find(version => version.Number == number);

        return chosen ?? throw new FileNotFoundException("That version does not exist.");
    }

    private static SecureFolder? RequireFolder(SecureStore store, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return store.Folders.Find(folder => folder.Id == id && folder.DeletedAt is null)
            ?? throw new FileNotFoundException("That folder no longer exists.");
    }

    private static StoredFile RequireFile(SecureStore store, string id) =>
        store.Files.Find(file => file.Id == id && file.DeletedAt is null)
            ?? throw new FileNotFoundException("That file no longer exists.");

    private static HashSet<string> Descendants(SecureStore store, string id)
    {
        var found = new HashSet<string>();
        var queue = new Queue<string>([id]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var child in store.Folders.Where(folder => folder.ParentId == current))
            {
                if (found.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return found;
    }

    private static void RequireUniqueFolder(SecureStore store, string? parentId, string name, string? ignoreId)
    {
        var target = NullIfEmpty(parentId);

        if (store.Folders.Any(folder => folder.DeletedAt is null && folder.Id != ignoreId && folder.ParentId == target
            && string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("A folder with that name already exists here.");
        }
    }

    private static void RequireUniqueFile(SecureStore store, string? folderId, string name, string? ignoreId)
    {
        if (store.Files.Any(file => file.DeletedAt is null && file.Id != ignoreId && file.FolderId == folderId
            && string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("A file with that name already exists in this folder. Use \"New version\" to replace its contents.");
        }
    }

    private static string UniqueFileName(SecureStore store, string? folderId, string name, string ignoreId)
    {
        var candidate = name;

        for (var counter = 1; store.Files.Any(file => file.DeletedAt is null && file.Id != ignoreId && file.FolderId == folderId
            && string.Equals(file.Name, candidate, StringComparison.OrdinalIgnoreCase)); counter++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} (restored {counter}){Path.GetExtension(name)}";
        }

        return candidate;
    }

    private static string UniqueFolderName(SecureStore store, string? parentId, string name, string ignoreId)
    {
        var candidate = name;

        for (var counter = 1; store.Folders.Any(folder => folder.DeletedAt is null && folder.Id != ignoreId && folder.ParentId == parentId
            && string.Equals(folder.Name, candidate, StringComparison.OrdinalIgnoreCase)); counter++)
        {
            candidate = $"{name} (restored {counter})";
        }

        return candidate;
    }

    private static string PathOf(SecureStore store, string? folderId)
    {
        var parts = new List<string>();

        for (var current = folderId; current is not null;)
        {
            var folder = store.Folders.Find(existing => existing.Id == current);

            if (folder is null || parts.Count > 50)
            {
                break;
            }

            parts.Insert(0, folder.Name);
            current = folder.ParentId;
        }

        return "/" + string.Join('/', parts);
    }

    private static string FilePath(SecureStore store, StoredFile file) =>
        PathOf(store, file.FolderId).TrimEnd('/') + "/" + file.Name;

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string CleanFileName(string? value)
    {
        var name = Path.GetFileName((value ?? string.Empty).Trim());
        name = new string(name.Where(character => !Path.GetInvalidFileNameChars().Contains(character)).ToArray()).Trim();

        if (name.Length == 0 || name is "." or "..")
        {
            throw new ArgumentException("A file name is required.");
        }

        return name.Length > MaxFileNameLength ? throw new ArgumentException($"File names are limited to {MaxFileNameLength} characters.") : name;
    }

    private static string CleanFolderName(string? value)
    {
        var name = (value ?? string.Empty).Trim();

        if (name.Length == 0 || name is "." or ".." || name.Any(character => char.IsControl(character) || character is '/' or '\\'))
        {
            throw new ArgumentException("Enter a folder name without slashes.");
        }

        return name.Length > MaxFolderNameLength ? throw new ArgumentException($"Folder names are limited to {MaxFolderNameLength} characters.") : name;
    }

    private static (string Infrastructure, string Environment) CleanPlacement(string? infrastructure, string? environment)
    {
        var infra = (infrastructure ?? string.Empty).Trim();

        if (infra.Length > 0)
        {
            infra = Choice(infra, Infrastructures, "Infrastructure");
        }

        var allowed = infra.Length == 0 ? Environments : EnvironmentsByInfrastructure[infra];
        return (infra, Choice(CanonicalEnvironment(environment), allowed, "Environment"));
    }

    // Earlier versions stored Dev, Test, Stage and Prod.
    private static string CanonicalEnvironment(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.Equals(trimmed, "Stage", StringComparison.OrdinalIgnoreCase)
            ? "STG"
            : Environments.FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    private static string Choice(string? value, string[] allowed, string label)
    {
        var trimmed = (value ?? string.Empty).Trim();
        var match = allowed.FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException($"{label} must be one of: {string.Join(", ", allowed)}.");
    }

    private static string CleanExpiry(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length > 0 && !DateTime.TryParseExact(trimmed, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
        {
            throw new ArgumentException("Expiry must look like 2026-12-31.");
        }

        return trimmed;
    }

    private static string CleanNotes(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > MaxNotesLength ? throw new ArgumentException($"Notes are limited to {MaxNotesLength} characters.") : trimmed;
    }
}
