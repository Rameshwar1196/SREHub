using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed partial class RepositoryService
{
    private static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(10);

    private readonly string _root;

    public RepositoryService(RepositoryOptions options)
    {
        _root = options.Root;
    }

    public string Root => _root;

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9_-])?$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex BranchPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[A-Za-z0-9._~/-]+$")]
    private static partial Regex ScpUrlPattern();

    [GeneratedRegex(@"ahead (\d+)")]
    private static partial Regex AheadPattern();

    [GeneratedRegex(@"behind (\d+)")]
    private static partial Regex BehindPattern();

    private sealed record GitResult(int ExitCode, string Output, string Error);

    public async Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var folders = Directory.EnumerateDirectories(_root)
            .Where(folder => NamePattern().IsMatch(System.IO.Path.GetFileName(folder)) && HasGitMetadata(folder))
            .OrderBy(folder => System.IO.Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase);

        var described = await Task.WhenAll(folders.Select(folder => DescribeAsync(folder, cancellationToken)));
        return described;
    }

    public async Task<RepositoryInfo> CloneAsync(CloneRepositoryRequest request, CancellationToken cancellationToken)
    {
        var url = ValidateUrl(request.Url);
        var name = string.IsNullOrWhiteSpace(request.Name) ? DeriveName(url) : request.Name.Trim();
        ValidateName(name);

        var branch = string.IsNullOrWhiteSpace(request.Branch) ? null : ValidateBranch(request.Branch.Trim());
        var target = System.IO.Path.Combine(_root, name);

        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new InvalidOperationException($"'{name}' already exists in the repository root.");
        }

        var arguments = new List<string> { "clone" };
        if (branch is not null)
        {
            arguments.AddRange(["--branch", branch]);
        }

        arguments.AddRange(["--", url, target]);

        GitResult result;
        try
        {
            result = await RunGitAsync(_root, arguments, NetworkTimeout, cancellationToken);
        }
        catch
        {
            DeleteDirectory(target);
            throw;
        }

        if (result.ExitCode != 0)
        {
            DeleteDirectory(target);
            throw new InvalidOperationException(FormatGitError("clone", result));
        }

        return await DescribeAsync(target, cancellationToken);
    }

    public async Task<RepositoryInfo> FetchAsync(string name, CancellationToken cancellationToken)
    {
        var path = ResolveRepository(name);
        await RunCheckedAsync(path, ["fetch", "--all", "--prune"], NetworkTimeout, cancellationToken);
        return await DescribeAsync(path, cancellationToken);
    }

    public async Task<RepositoryInfo> PullAsync(string name, CancellationToken cancellationToken)
    {
        var path = ResolveRepository(name);
        await RunCheckedAsync(path, ["pull", "--ff-only"], NetworkTimeout, cancellationToken);
        return await DescribeAsync(path, cancellationToken);
    }

    public async Task<RepositoryBranches> GetBranchesAsync(string name, CancellationToken cancellationToken)
    {
        var path = ResolveRepository(name);
        var output = await RunCheckedAsync(
            path, ["for-each-ref", "--format=%(refname)", "refs/heads", "refs/remotes"], LocalTimeout, cancellationToken);

        var branches = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string? branch = null;

            if (line.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                branch = line["refs/heads/".Length..];
            }
            else if (line.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                var remoteAndBranch = line["refs/remotes/".Length..];
                var separator = remoteAndBranch.IndexOf('/');
                branch = separator >= 0 ? remoteAndBranch[(separator + 1)..] : null;
            }

            if (!string.IsNullOrEmpty(branch) && branch != "HEAD")
            {
                branches.Add(branch);
            }
        }

        var info = await DescribeAsync(path, cancellationToken);
        return new RepositoryBranches { Current = info.Branch, Branches = [.. branches] };
    }

    public async Task<RepositoryInfo> SwitchBranchAsync(string name, string branch, CancellationToken cancellationToken)
    {
        var path = ResolveRepository(name);
        await RunCheckedAsync(path, ["switch", ValidateBranch(branch.Trim())], LocalTimeout, cancellationToken);
        return await DescribeAsync(path, cancellationToken);
    }

    public void Open(string name, string target)
    {
        var path = ResolveRepository(name);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Opening repositories is only supported on Windows.");
        }

        ProcessStartInfo info;

        switch (target.ToLowerInvariant())
        {
            case "vscode":
                info = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
                info.ArgumentList.Add("/c");
                info.ArgumentList.Add("code");
                info.ArgumentList.Add(path);
                break;
            case "explorer":
                info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                info.ArgumentList.Add(path);
                break;
            case "terminal":
                info = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = path, Arguments = "-NoExit" };
                break;
            default:
                throw new ArgumentException("Target must be vscode, explorer or terminal.");
        }

        using var process = Process.Start(info);

        if (target.Equals("vscode", StringComparison.OrdinalIgnoreCase)
            && process is not null
            && process.WaitForExit(5000)
            && process.ExitCode != 0)
        {
            throw new InvalidOperationException("The 'code' command was not found. Install the VS Code shell command and add it to PATH.");
        }
    }

    public async Task RemoveAsync(string name, bool force, CancellationToken cancellationToken)
    {
        var path = ResolveRepository(name);

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Linked folders cannot be removed from here.");
        }

        if (!force)
        {
            var info = await DescribeAsync(path, cancellationToken);
            if (info.Changes > 0 || info.Ahead > 0)
            {
                throw new InvalidOperationException(
                    $"'{name}' has uncommitted changes or unpushed commits. Confirm the removal to delete it anyway.");
            }
        }

        DeleteDirectory(path);
    }

    private async Task<RepositoryInfo> DescribeAsync(string path, CancellationToken cancellationToken)
    {
        var info = new RepositoryInfo { Name = System.IO.Path.GetFileName(path), Path = path };

        try
        {
            var statusTask = RunCheckedAsync(path, ["status", "--porcelain=v1", "--branch"], LocalTimeout, cancellationToken);
            var remoteTask = RunGitAsync(path, ["remote", "get-url", "origin"], LocalTimeout, cancellationToken);

            ApplyStatus(info, await statusTask);

            var remote = await remoteTask;
            if (remote.ExitCode == 0)
            {
                info.RemoteUrl = StripCredentials(remote.Output.Trim());
            }

            var fetchHead = System.IO.Path.Combine(path, ".git", "FETCH_HEAD");
            if (File.Exists(fetchHead))
            {
                info.LastFetchedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(fetchHead), TimeSpan.Zero);
            }
        }
        catch (InvalidOperationException exception)
        {
            info.Error = exception.Message;
        }

        return info;
    }

    private static void ApplyStatus(RepositoryInfo info, string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToList();
        var header = lines.FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal));
        info.Changes = lines.Count(line => !line.StartsWith("## ", StringComparison.Ordinal));

        if (header is null)
        {
            return;
        }

        header = header[3..];

        const string noCommits = "No commits yet on ";
        const string initial = "Initial commit on ";

        if (header.StartsWith("HEAD (no branch)", StringComparison.Ordinal))
        {
            info.Detached = true;
            return;
        }

        if (header.StartsWith(noCommits, StringComparison.Ordinal))
        {
            info.Branch = header[noCommits.Length..];
            return;
        }

        if (header.StartsWith(initial, StringComparison.Ordinal))
        {
            info.Branch = header[initial.Length..];
            return;
        }

        var tracking = string.Empty;
        var bracket = header.IndexOf(" [", StringComparison.Ordinal);
        if (bracket >= 0)
        {
            tracking = header[(bracket + 2)..].TrimEnd(']');
            header = header[..bracket];
        }

        var upstream = header.IndexOf("...", StringComparison.Ordinal);
        info.Branch = upstream >= 0 ? header[..upstream] : header;
        info.HasUpstream = upstream >= 0 && tracking != "gone";

        var ahead = AheadPattern().Match(tracking);
        var behind = BehindPattern().Match(tracking);
        info.Ahead = ahead.Success ? int.Parse(ahead.Groups[1].Value) : 0;
        info.Behind = behind.Success ? int.Parse(behind.Groups[1].Value) : 0;
    }

    private static string StripCredentials(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length > 0)
        {
            return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.ToString();
        }

        return url;
    }

    private static bool HasGitMetadata(string folder)
    {
        var git = System.IO.Path.Combine(folder, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    private string ResolveRepository(string name)
    {
        ValidateName(name);

        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, name));

        if (!string.Equals(System.IO.Path.GetDirectoryName(path), _root, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(path)
            || !HasGitMetadata(path))
        {
            throw new DirectoryNotFoundException($"Repository '{name}' was not found.");
        }

        return path;
    }

    private static void ValidateName(string name)
    {
        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException("Repository names may only contain letters, digits, '.', '_' and '-'.");
        }
    }

    private static string ValidateBranch(string branch)
    {
        if (!BranchPattern().IsMatch(branch) || branch.Contains("..", StringComparison.Ordinal) || branch.EndsWith('/') || branch.EndsWith(".lock", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{branch}' is not a valid branch name.");
        }

        return branch;
    }

    private static string ValidateUrl(string value)
    {
        var url = (value ?? string.Empty).Trim();

        if (url.Length == 0)
        {
            throw new ArgumentException("A repository URL is required.");
        }

        if (ScpUrlPattern().IsMatch(url))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "ssh"))
        {
            throw new ArgumentException("Use an https:// or ssh:// URL, or an scp-style address such as git@host:org/repo.git.");
        }

        if (uri.UserInfo.Contains(':'))
        {
            throw new ArgumentException("Do not put a password or token in the URL. Use your Git credential manager or SSH key.");
        }

        return url;
    }

    private static string DeriveName(string url)
    {
        var trimmed = url.TrimEnd('/');
        var start = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf(':')) + 1;
        var name = trimmed[start..];

        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

        // Git marks object files read-only, which blocks recursive deletion on Windows.
        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private static async Task<string> RunCheckedAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(workingDirectory, arguments, timeout, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(FormatGitError(arguments[0], result));
        }

        return result.Output;
    }

    private static string FormatGitError(string command, GitResult result)
    {
        var message = (result.Error.Length > 0 ? result.Error : result.Output).Trim();

        if (message.Length > 1200)
        {
            message = message[^1200..];
        }

        return message.Length == 0 ? $"git {command} failed with exit code {result.ExitCode}." : message;
    }

    private static async Task<GitResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Fail instead of waiting for a console prompt nobody can answer.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = new Process { StartInfo = info };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("Git was not found. Install Git and make sure it is on PATH.");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"git {arguments[0]} timed out after {timeout.TotalMinutes:0.#} minutes.");
        }

        return new GitResult(process.ExitCode, await output, await error);
    }
}
