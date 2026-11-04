using System.Text.Json;

namespace Aras.SreHub.Learning;

public sealed class ProgressStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, HashSet<string>>? _cache;

    public ProgressStore(LearningOptions options)
    {
        var folder = Path.Combine(options.ContentRoot, ".sre-hub");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "progress.json");
    }

    public async Task<IReadOnlySet<string>> GetCompletedAsync(string subjectId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken);
        return state.TryGetValue(subjectId, out var completed) ? completed : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetAsync(string subjectId, string relativePath, bool completed, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);

            if (!state.TryGetValue(subjectId, out var entries))
            {
                entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                state[subjectId] = entries;
            }

            if (completed)
            {
                entries.Add(relativePath);
            }
            else
            {
                entries.Remove(relativePath);
            }

            var payload = state.ToDictionary(pair => pair.Key, pair => pair.Value.OrderBy(item => item).ToArray());
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(payload, SerializerOptions), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops completion entries for a deleted file, or for every file beneath a deleted folder.</summary>
    public Task RemovePathAsync(string subjectId, string relativePath, CancellationToken cancellationToken) =>
        MutateAsync(state =>
        {
            if (!state.TryGetValue(subjectId, out var entries))
            {
                return;
            }

            var prefix = $"{relativePath}/";
            entries.RemoveWhere(entry =>
                entry.Equals(relativePath, StringComparison.OrdinalIgnoreCase)
                || entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }, cancellationToken);

    public Task RemoveSubjectAsync(string subjectId, CancellationToken cancellationToken) =>
        MutateAsync(state => state.Remove(subjectId), cancellationToken);

    /// <summary>Re-keys completion entries when a file or folder is renamed or moved, including across subjects.</summary>
    public Task MovePathAsync(string fromSubject, string fromPath, string toSubject, string toPath, CancellationToken cancellationToken) =>
        MutateAsync(state =>
        {
            if (!state.TryGetValue(fromSubject, out var source))
            {
                return;
            }

            var prefix = $"{fromPath}/";
            var moved = source
                .Where(entry => entry.Equals(fromPath, StringComparison.OrdinalIgnoreCase)
                    || entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (moved.Count == 0)
            {
                return;
            }

            if (!state.TryGetValue(toSubject, out var target))
            {
                target = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                state[toSubject] = target;
            }

            foreach (var entry in moved)
            {
                source.Remove(entry);
                target.Add(toPath + entry[fromPath.Length..]);
            }
        }, cancellationToken);

    public Task RenameSubjectAsync(string oldId, string newId, CancellationToken cancellationToken) =>
        MutateAsync(state =>
        {
            if (state.Remove(oldId, out var entries))
            {
                state[newId] = entries;
            }
        }, cancellationToken);

    private async Task MutateAsync(Action<Dictionary<string, HashSet<string>>> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            mutate(state);

            var payload = state.ToDictionary(pair => pair.Key, pair => pair.Value.OrderBy(item => item).ToArray());
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(payload, SerializerOptions), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_path))
        {
            _cache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            return _cache;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json, SerializerOptions) ?? [];
            _cache = raw.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            _cache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }

        return _cache;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}
