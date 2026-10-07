using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SimEngine.Networking;
using SimEngine.Networking.Tcp;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// A child <c>--server</c> process that hosts a multiplayer game. Starting waits
/// for its <c>READY</c> line; afterwards its output goes to a log file under
/// <see cref="GameTempDirectory"/>. The child exits with this process (its
/// <c>--parent-pid</c> watchdog); disposing kills it right away.
/// </summary>
internal sealed class ServerProcess : IAsyncDisposable
{
    private const int MaxErrorLines = 20;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private readonly ProcessLog _log;
    private readonly Task _stdoutPump;
    private readonly EventHandler _killOnExit;

    private ServerProcess(Process process, ProcessLog log, int port, EventHandler killOnExit)
    {
        _process = process;
        _log = log;
        _killOnExit = killOnExit;
        Port = port;
        ConnectEndpoint = TcpTransport.CreateEndpoint("127.0.0.1", port);
        _stdoutPump = Task.Run(PumpStdoutAsync);
    }

    /// <summary>The port the server listens on (all interfaces).</summary>
    public int Port { get; }

    /// <summary>Where the local client connects.</summary>
    public TransportEndpoint ConnectEndpoint { get; }

    public string LogPath => _log.Path;

    /// <summary>Starts the server on <paramref name="port"/> and waits until it accepts connections.</summary>
    public static async Task<ServerProcess> StartAsync(int port, CancellationToken ct = default)
    {
        var log = new ProcessLog(GameTempDirectory.GetPath(
            $"server-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{port.ToString(CultureInfo.InvariantCulture)}.log"));
        var process = new Process { StartInfo = CreateStartInfo(port) };
        process.ErrorDataReceived += (_, e) => log.WriteError(e.Data);
        void KillOnExit(object? sender, EventArgs e) => Kill(process);

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            log.Dispose();
            throw new InvalidOperationException($"Could not start the server process: {ex.Message}", ex);
        }

        AppDomain.CurrentDomain.ProcessExit += KillOnExit;
        process.BeginErrorReadLine();
        try
        {
            var boundPort = await WaitForReadyAsync(process, log, ct).ConfigureAwait(false);
            return new ServerProcess(process, log, boundPort, KillOnExit);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException or OperationCanceledException)
        {
            AppDomain.CurrentDomain.ProcessExit -= KillOnExit;
            await StopAsync(process).ConfigureAwait(false);
            var errors = log.RecentErrors;
            process.Dispose();
            log.Dispose();
            if (ex is OperationCanceledException)
            {
                throw;
            }

            var details = errors.Length > 0 ? Environment.NewLine + errors : string.Empty;
            throw new InvalidOperationException($"{ex.Message}{details}{Environment.NewLine}Server log: {log.Path}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        AppDomain.CurrentDomain.ProcessExit -= _killOnExit;
        await StopAsync(_process).ConfigureAwait(false);
        try
        {
            await _stdoutPump.WaitAsync(ExitTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        _process.Dispose();
        _log.Dispose();
    }

    private static ProcessStartInfo CreateStartInfo(int port)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the game executable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Started as `dotnet Game.dll`: pass the entry assembly to the host again.
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }

        startInfo.ArgumentList.Add(ServerMode.Switch);
        startInfo.ArgumentList.Add(ServerMode.ListenArg);
        startInfo.ArgumentList.Add(TcpTransport.CreateEndpoint("0.0.0.0", port).ToString());
        startInfo.ArgumentList.Add(ServerMode.ParentPidArg);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        return startInfo;
    }

    /// <summary>Reads stdout into the log until the <c>READY</c> line; returns the bound port.</summary>
    private static async Task<int> WaitForReadyAsync(Process process, ProcessLog log, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(StartTimeout);
        while (true)
        {
            string? line;
            try
            {
                line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for the server to start.");
            }

            if (line is null)
            {
                throw new InvalidOperationException("The server exited before it was ready.");
            }

            log.WriteLine(line);
            if (!line.StartsWith(ServerMode.ReadyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var first = line[ServerMode.ReadyPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!TransportEndpoint.TryParse(first, out var endpoint) || endpoint.Scheme != TcpTransport.SchemeName)
            {
                throw new InvalidOperationException($"The server reported an unexpected endpoint: '{line}'.");
            }

            return TcpTransport.ParseAddress(endpoint).Port;
        }
    }

    private async Task PumpStdoutAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                _log.WriteLine(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static async Task StopAsync(Process process)
    {
        Kill(process);
        try
        {
            // Also waits for the redirected streams to drain.
            await process.WaitForExitAsync().WaitAsync(ExitTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    /// <summary>Thread-safe log file that also keeps the latest stderr lines for error messages.</summary>
    private sealed class ProcessLog : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<string> _errors = new();
        private StreamWriter? _writer;

        public ProcessLog(string path)
        {
            Path = path;
            _writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
        }

        public string Path { get; }

        public string RecentErrors
        {
            get { lock (_gate) { return string.Join(Environment.NewLine, _errors); } }
        }

        public void WriteLine(string line)
        {
            lock (_gate)
            {
                _writer?.WriteLine(line);
            }
        }

        public void WriteError(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _writer?.WriteLine(line);
                _errors.Enqueue(line);
                if (_errors.Count > MaxErrorLines)
                {
                    _errors.Dequeue();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
