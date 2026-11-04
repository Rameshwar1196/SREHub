using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Aras.SreHub.Models;

namespace Aras.SreHub.Services;

public sealed class TerminalSession : IDisposable
{
    private const string BootstrapTemplate = """
        $ErrorActionPreference = 'Continue'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        if (Get-Variable -Name PSStyle -ErrorAction SilentlyContinue) { $PSStyle.OutputRendering = 'PlainText' }
        while ($true) {
            $srehubLine = [Console]::In.ReadLine()
            if ($null -eq $srehubLine) { break }
            $global:LASTEXITCODE = 0
            $Error.Clear()
            try {
                $srehubCommand = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($srehubLine))
                if ((Get-Location).Provider.Name -eq 'FileSystem') { [Environment]::CurrentDirectory = (Get-Location).ProviderPath }
                . ([scriptblock]::Create($srehubCommand)) *>&1 | ForEach-Object {
                    if ($_ -is [System.Management.Automation.ErrorRecord]) {
                        $srehubText = if ($_.FullyQualifiedErrorId -like 'NativeCommandError*') { $_.Exception.Message.TrimEnd() } else { ($_ | Out-String).TrimEnd() }
                        [Console]::Error.WriteLine($srehubText)
                    }
                    elseif ($_ -is [System.Management.Automation.WarningRecord]) { 'WARNING: ' + $_.Message }
                    elseif ($_ -is [System.Management.Automation.VerboseRecord]) { 'VERBOSE: ' + $_.Message }
                    elseif ($_ -is [System.Management.Automation.DebugRecord]) { 'DEBUG: ' + $_.Message }
                    elseif ($_ -is [System.Management.Automation.InformationRecord]) { [string]$_.MessageData }
                    else { $_ }
                } | Out-String -Stream -Width 220
            } catch {
                $srehubFailure = if ($_.FullyQualifiedErrorId -eq 'ParseException' -and $_.Exception.InnerException) { $_.Exception.InnerException.Message } else { ($_ | Out-String).TrimEnd() }
                [Console]::Error.WriteLine($srehubFailure)
                $global:LASTEXITCODE = 1
            }
            $srehubCode = if ($LASTEXITCODE) { $LASTEXITCODE } elseif ($Error.Count -gt 0) { 1 } else { 0 }
            $srehubCwd = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Location).Path))
            [Console]::Out.WriteLine('@@MARKER@@|' + $srehubCode + '|' + $srehubCwd)
            [Console]::Error.WriteLine('@@MARKER@@')
        }
        """;

    private readonly TerminalOptions _options;
    private readonly string _shellPath;
    private readonly string _marker = $"<<srehub-{Guid.NewGuid():N}>>";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();

    private Process? _process;
    private Channel<TerminalEvent>? _current;
    private int _markersSeen;
    private int _lastExitCode;
    private long _bytesThisCommand;
    private bool _truncated;
    private bool _disposed;

    public TerminalSession(string id, string owner, string shellPath, TerminalOptions options)
    {
        Id = id;
        Owner = owner;
        _shellPath = shellPath;
        _options = options;
        Cwd = options.WorkingDirectory!;
        LastActivity = DateTimeOffset.UtcNow;
    }

    public string Id { get; }

    public string Owner { get; }

    public string Shell => System.IO.Path.GetFileNameWithoutExtension(_shellPath);

    public string Cwd { get; private set; }

    public DateTimeOffset LastActivity { get; private set; }

    public bool IsBusy => _gate.CurrentCount == 0;

    public async IAsyncEnumerable<TerminalEvent> RunAsync(
        string command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_gate.Wait(0))
        {
            throw new InvalidOperationException("A command is already running in this session. Stop it first.");
        }

        var channel = Channel.CreateUnbounded<TerminalEvent>(new UnboundedChannelOptions { SingleReader = true });

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            LastActivity = DateTimeOffset.UtcNow;

            lock (_sync)
            {
                _current = channel;
                _markersSeen = 0;
                _lastExitCode = 0;
                _bytesThisCommand = 0;
                _truncated = false;
            }

            EnsureProcess();

            using var registration = cancellationToken.Register(Stop);

            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
            try
            {
                await _process!.StandardInput.WriteLineAsync(encoded);
                await _process.StandardInput.FlushAsync(CancellationToken.None);
            }
            catch (IOException)
            {
                Complete(channel, -1, "The shell stopped before it could run the command.");
            }

            await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                yield return item;
            }
        }
        finally
        {
            lock (_sync)
            {
                _current = null;
            }

            LastActivity = DateTimeOffset.UtcNow;
            _gate.Release();
        }
    }

    public void Stop()
    {
        Process? process;
        Channel<TerminalEvent>? channel;

        lock (_sync)
        {
            process = _process;
            channel = _current;
            _process = null;
        }

        KillQuietly(process);

        if (channel is not null)
        {
            Complete(channel, -1, "Stopped. The shell will restart on the next command; session variables are reset.");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    private void EnsureProcess()
    {
        lock (_sync)
        {
            if (_process is { HasExited: false })
            {
                return;
            }
        }

        var script = BootstrapTemplate.Replace("@@MARKER@@", _marker, StringComparison.Ordinal);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var startInfo = new ProcessStartInfo(_shellPath)
        {
            WorkingDirectory = Directory.Exists(Cwd) ? Cwd : _options.WorkingDirectory!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["AZURE_CORE_NO_COLOR"] = "true";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, args) => OnLine(process, args.Data, isError: false);
        process.ErrorDataReceived += (_, args) => OnLine(process, args.Data, isError: true);
        process.Exited += (_, _) => OnExited(process);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        lock (_sync)
        {
            _process = process;
        }
    }

    private void OnLine(Process source, string? line, bool isError)
    {
        if (line is null)
        {
            return;
        }

        Channel<TerminalEvent>? channel;

        lock (_sync)
        {
            if (!ReferenceEquals(source, _process))
            {
                return;
            }

            channel = _current;
        }

        if (channel is null)
        {
            return;
        }

        if (line.StartsWith(_marker, StringComparison.Ordinal))
        {
            OnMarker(channel, line, isError);
            return;
        }

        lock (_sync)
        {
            _bytesThisCommand += line.Length + 1;

            if (_bytesThisCommand > _options.MaxOutputBytesPerCommand)
            {
                if (_truncated)
                {
                    return;
                }

                _truncated = true;
                channel.Writer.TryWrite(new TerminalEvent("notice", "Output limit reached; the rest of the output is discarded."));
                return;
            }
        }

        channel.Writer.TryWrite(new TerminalEvent(isError ? "err" : "out", line.TrimEnd('\r')));
    }

    private void OnMarker(Channel<TerminalEvent> channel, string line, bool isError)
    {
        var finished = false;

        lock (_sync)
        {
            if (!isError)
            {
                var parts = line.Split('|');

                if (parts.Length == 3)
                {
                    _lastExitCode = int.TryParse(parts[1], out var code) ? code : 0;

                    try
                    {
                        Cwd = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                    }
                    catch (FormatException)
                    {
                        // Keep the previous working directory.
                    }
                }
            }

            finished = ++_markersSeen >= 2;
        }

        if (finished)
        {
            Complete(channel, _lastExitCode, null);
        }
    }

    private void OnExited(Process source)
    {
        Channel<TerminalEvent>? channel;

        lock (_sync)
        {
            if (!ReferenceEquals(source, _process))
            {
                return;
            }

            _process = null;
            channel = _current;
        }

        if (channel is not null)
        {
            Complete(channel, -1, "The shell exited. It will restart on the next command.");
        }
    }

    private void Complete(Channel<TerminalEvent> channel, int exitCode, string? notice)
    {
        if (notice is not null)
        {
            channel.Writer.TryWrite(new TerminalEvent("notice", notice));
        }

        channel.Writer.TryWrite(new TerminalEvent("exit", ExitCode: exitCode, Cwd: Cwd));
        channel.Writer.TryComplete();
    }

    private static void KillQuietly(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Process is shutting down.
        }
    }
}
