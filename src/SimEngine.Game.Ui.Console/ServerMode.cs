using System.Globalization;
using System.Net.Sockets;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game;
using SimEngine.Networking;
using SimEngine.Networking.Tcp;
using SimEngine.Server;
using SimEngine.Server.Gateway;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console;

/// <summary>
/// Runs the console as a standalone network server: clients connect to the
/// gateway over TCP (<c>--listen</c>). Selected with <c>--server</c>; the
/// multiplayer host spawns this mode as a child process (<c>--parent-pid</c>
/// stops it when the host exits). Once bound, it prints <c>READY &lt;endpoint...&gt;</c>.
/// </summary>
internal static class ServerMode
{
    /// <summary>The <c>--server</c> switch that selects this mode.</summary>
    internal const string Switch = "--server";

    internal const string ListenArg = "--listen";
    internal const string ParentPidArg = "--parent-pid";

    /// <summary>Prefix of the stdout line that reports the bound endpoints.</summary>
    internal const string ReadyPrefix = "READY ";

    internal const int DefaultPort = 8110;

    private const string SystemName = "SimEngineServer";
    private const string HostArg = "--host";
    private const string PortArg = "--port";
    private const string DefaultHost = "0.0.0.0";

    /// <summary>True when <paramref name="args"/> requests server mode.</summary>
    internal static bool IsRequested(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Array.Exists(args, a => string.Equals(a, Switch, StringComparison.Ordinal));
    }

    /// <summary>
    /// Starts the network server and blocks until the process is asked to stop
    /// (Ctrl+C / SIGTERM / parent exit). Returns a process exit code.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!TryParseOptions(args, out var options, out var error))
        {
            await System.Console.Error.WriteLineAsync(error);
            return 1;
        }

        var transports = new TransportRegistry(new TcpTransport());
        var listeners = new List<ITransportListener>();
        try
        {
            foreach (var endpoint in options.Listen)
            {
                listeners.Add(await transports.ListenAsync(endpoint, cancellationToken));
            }
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or NotSupportedException)
        {
            foreach (var listener in listeners)
            {
                await listener.DisposeAsync();
            }

            await System.Console.Error.WriteLineAsync($"Could not listen: {ex.Message}");
            return 1;
        }

        var gatewayOptions = new GatewayOptions { SaveDirectory = SaveGamePaths.SavesDirectory };
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka(SystemName, builder =>
                {
                    builder.WithSimEngineActors(AkkaExecutionMode.LocalTest);
                    foreach (var listener in listeners)
                    {
                        builder.WithSimEngineGateway(listener, gatewayOptions);
                    }
                });
            })
            .Build();

        await host.StartAsync(cancellationToken);
        RenderBanner(listeners);
        await System.Console.Out.WriteLineAsync(ReadyPrefix + string.Join(' ', listeners.Select(l => l.LocalEndpoint)));
        await System.Console.Out.FlushAsync(cancellationToken);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.ParentPid is int parentPid)
        {
            _ = StopWhenParentExitsAsync(parentPid, stop);
        }

        try
        {
            await host.WaitForShutdownAsync(stop.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C or parent exit; fall through to a clean stop.
        }

        AnsiConsole.MarkupLine("[dim]Server stopped.[/]");
        return 0;
    }

    private static async Task StopWhenParentExitsAsync(int parentPid, CancellationTokenSource stop)
    {
        try
        {
            await ParentProcessWatchdog.WaitForExitAsync(parentPid, stop.Token);
            await stop.CancelAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool TryParseOptions(string[] args, out ServerOptions options, out string error)
    {
        var listen = new List<TransportEndpoint>();
        string? host = null;
        int? port = null;
        int? parentPid = null;
        options = new ServerOptions([], null);
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case Switch:
                    break;
                case ListenArg:
                    if (!TryTakeValue(args, ref i, ListenArg, out var raw, out error))
                        return false;
                    if (!TransportEndpoint.TryParse(raw, out var endpoint))
                    {
                        error = $"{ListenArg} value '{raw}' is not an endpoint (e.g. tcp://0.0.0.0:{DefaultPort}).";
                        return false;
                    }
                    listen.Add(endpoint);
                    break;
                case HostArg:
                    if (!TryTakeValue(args, ref i, HostArg, out var hostValue, out error))
                        return false;
                    host = hostValue;
                    break;
                case PortArg:
                    if (!TryTakeValue(args, ref i, PortArg, out var portValue, out error))
                        return false;
                    if (!int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) || parsedPort > 65535)
                    {
                        error = $"{PortArg} value '{portValue}' is not a valid port (0-65535).";
                        return false;
                    }
                    port = parsedPort;
                    break;
                case ParentPidArg:
                    if (!TryTakeValue(args, ref i, ParentPidArg, out var pidValue, out error))
                        return false;
                    if (!int.TryParse(pidValue, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
                    {
                        error = $"{ParentPidArg} value '{pidValue}' is not a process id.";
                        return false;
                    }
                    parentPid = pid;
                    break;
                default:
                    error = $"Unknown argument '{args[i]}'.";
                    return false;
            }
        }

        // --host/--port are shorthand for one tcp:// listen endpoint.
        if (host is not null || port is not null)
        {
            listen.Add(TcpTransport.CreateEndpoint(host ?? DefaultHost, port ?? DefaultPort));
        }

        if (listen.Count == 0)
        {
            listen.Add(TcpTransport.CreateEndpoint(DefaultHost, DefaultPort));
        }

        options = new ServerOptions(listen, parentPid);
        return true;
    }

    private static bool TryTakeValue(string[] args, ref int index, string name, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (index + 1 >= args.Length)
        {
            error = $"{name} requires a value.";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static void RenderBanner(IEnumerable<ITransportListener> listeners)
    {
        AnsiConsole.Write(
            new Rule("[bold gold1]SimEngine Server[/]").RuleStyle("gold1 dim"));
        foreach (var listener in listeners)
        {
            AnsiConsole.MarkupLine($"[dim]Listening for clients on[/] [green]{Markup.Escape(listener.LocalEndpoint.ToString())}[/]");
        }

        AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();
    }

    private sealed record ServerOptions(IReadOnlyList<TransportEndpoint> Listen, int? ParentPid);
}
