using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed partial class TerminalService : IDisposable
{
    private readonly TerminalOptions _options;
    private readonly string _auditPath;
    private readonly string _shellPath;
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();
    private readonly Timer _janitor;
    private readonly object _auditLock = new();

    public TerminalService(TerminalOptions options, IWebHostEnvironment environment)
    {
        _options = options;
        _shellPath = ResolveShell();

        var dataFolder = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataFolder);
        _auditPath = Path.Combine(dataFolder, "terminal-audit.log");

        _janitor = new Timer(_ => RemoveIdleSessions(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    [GeneratedRegex("""(?i)(--?(?:value|password|secret|client-secret|token|key|sas-token|account-key)(?:\s+|=))("[^"]*"|'[^']*'|\S+)""")]
    private static partial Regex SecretArgumentPattern();

    public bool IsAllowed(string? email) => _options.IsAllowed(email);

    public TerminalSessionInfo Create(string owner)
    {
        if (_sessions.Values.Count(session => string.Equals(session.Owner, owner, StringComparison.OrdinalIgnoreCase)) >= _options.MaxSessionsPerUser)
        {
            throw new InvalidOperationException($"You already have {_options.MaxSessionsPerUser} open shells. Close one first.");
        }

        var session = new TerminalSession(Guid.NewGuid().ToString("N"), owner, _shellPath, _options);
        _sessions[session.Id] = session;

        return new TerminalSessionInfo(session.Id, session.Shell, session.Cwd);
    }

    public TerminalSession? Find(string id, string owner) =>
        _sessions.TryGetValue(id, out var session)
        && string.Equals(session.Owner, owner, StringComparison.OrdinalIgnoreCase)
            ? session
            : null;

    public void Close(string id, string owner)
    {
        if (Find(id, owner) is not null && _sessions.TryRemove(id, out var session))
        {
            session.Dispose();
        }
    }

    public async IAsyncEnumerable<TerminalEvent> RunAsync(
        TerminalSession session,
        string command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int? exitCode = null;

        try
        {
            await foreach (var item in session.RunAsync(command, cancellationToken))
            {
                if (item.Type == "exit")
                {
                    exitCode = item.ExitCode;
                }

                yield return item;
            }
        }
        finally
        {
            WriteAudit(session, command, exitCode);
        }
    }

    public void Dispose()
    {
        _janitor.Dispose();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }

    private void RemoveIdleSessions()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_options.IdleMinutes);

        foreach (var session in _sessions.Values.Where(item => !item.IsBusy && item.LastActivity < cutoff))
        {
            if (_sessions.TryRemove(session.Id, out _))
            {
                session.Dispose();
            }
        }
    }

    private void WriteAudit(TerminalSession session, string command, int? exitCode)
    {
        var entry = JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.UtcNow,
            user = session.Owner,
            session = session.Id,
            command = SecretArgumentPattern().Replace(command, "$1***"),
            exitCode
        });

        try
        {
            lock (_auditLock)
            {
                File.AppendAllText(_auditPath, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Auditing must not break the shell.
        }
    }

    private static string ResolveShell()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "pwsh.exe" } : new[] { "pwsh" };
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var folder in folders)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(folder, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        throw new InvalidOperationException("PowerShell (pwsh) was not found on PATH.");
    }
}
