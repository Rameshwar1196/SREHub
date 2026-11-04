namespace Aras.SreHub.Models;

public sealed class TerminalOptions
{
    public const string SectionName = "Terminal";

    public bool Enabled { get; set; } = true;

    // Empty means every signed-in user may open a shell.
    public List<string> AllowedUsers { get; set; } = [];

    public string? WorkingDirectory { get; set; }

    public int IdleMinutes { get; set; } = 30;

    public int MaxSessionsPerUser { get; set; } = 3;

    public int MaxOutputBytesPerCommand { get; set; } = 4 * 1024 * 1024;

    public void Normalize(string contentRootPath)
    {
        IdleMinutes = Math.Clamp(IdleMinutes, 1, 1440);
        MaxSessionsPerUser = Math.Clamp(MaxSessionsPerUser, 1, 10);
        MaxOutputBytesPerCommand = Math.Clamp(MaxOutputBytesPerCommand, 64 * 1024, 64 * 1024 * 1024);

        var configured = string.IsNullOrWhiteSpace(WorkingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(Path.Combine(contentRootPath, WorkingDirectory));

        WorkingDirectory = Directory.Exists(configured) ? configured : contentRootPath;
    }

    public bool IsAllowed(string? email) =>
        Enabled
        && !string.IsNullOrWhiteSpace(email)
        && (AllowedUsers.Count == 0 || AllowedUsers.Contains(email, StringComparer.OrdinalIgnoreCase));
}

public sealed class TerminalRunRequest
{
    public string Command { get; set; } = string.Empty;
}

public sealed record TerminalSessionInfo(string Id, string Shell, string WorkingDirectory);

// Type is "out", "err", "notice" or "exit".
public sealed record TerminalEvent(string Type, string? Text = null, int? ExitCode = null, string? Cwd = null);
