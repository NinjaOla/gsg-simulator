using Microsoft.Extensions.Hosting;
using SimEngine.Server;
using Spectre.Console;

namespace SimEngine.ConsoleHost;

/// <summary>
/// Runs the ConsoleHost as a standalone network silo: it listens on a loopback
/// gateway so external client processes (other ConsoleHost instances) can
/// connect and share sessions. Selected with the <c>--server</c> switch;
/// without it the host runs in-process single-player mode.
/// </summary>
internal static class ServerMode
{
    /// <summary>The <c>--server</c> switch that selects this mode.</summary>
    internal const string Switch = "--server";

    private const string SiloPortArg = "--silo-port";
    private const string GatewayPortArg = "--gateway-port";

    /// <summary>True when <paramref name="args"/> requests server mode.</summary>
    internal static bool IsRequested(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Array.Exists(args, a => string.Equals(a, Switch, StringComparison.Ordinal));
    }

    /// <summary>
    /// Starts the network silo and blocks until the process is asked to stop
    /// (Ctrl+C / SIGTERM). Returns a process exit code.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!TryParseOptions(args, out var options, out var error))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        using var host = new HostBuilder()
            .UseSimEngineSilo(o =>
            {
                o.SiloPort = options.SiloPort;
                o.GatewayPort = options.GatewayPort;
            })
            .Build();

        await host.StartAsync(cancellationToken);
        RenderBanner(options);

        try
        {
            await host.WaitForShutdownAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C; fall through to a clean stop.
        }

        AnsiConsole.MarkupLine("[dim]Silo stopped.[/]");
        return 0;
    }

    private static bool TryParseOptions(string[] args, out SimEngineSiloOptions options, out string error)
    {
        options = new SimEngineSiloOptions();
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case Switch:
                    break;
                case SiloPortArg:
                    if (!TryTakePort(args, ref i, SiloPortArg, out var siloPort, out error))
                        return false;
                    options.SiloPort = siloPort;
                    break;
                case GatewayPortArg:
                    if (!TryTakePort(args, ref i, GatewayPortArg, out var gatewayPort, out error))
                        return false;
                    options.GatewayPort = gatewayPort;
                    break;
                default:
                    error = $"Unknown argument '{args[i]}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryTakePort(string[] args, ref int index, string name, out int port, out string error)
    {
        port = 0;
        error = string.Empty;

        if (index + 1 >= args.Length)
        {
            error = $"{name} requires a port number.";
            return false;
        }

        var raw = args[++index];
        if (!int.TryParse(raw, out port) || port is < 1 or > 65535)
        {
            error = $"{name} value '{raw}' is not a valid port (1-65535).";
            return false;
        }

        return true;
    }

    private static void RenderBanner(SimEngineSiloOptions options)
    {
        AnsiConsole.Write(
            new Rule("[bold gold1]SimEngine Server[/]").RuleStyle("gold1 dim"));
        AnsiConsole.MarkupLine(
            $"[dim]Listening for clients on[/] [green]localhost:{options.GatewayPort}[/] " +
            $"[dim](silo port {options.SiloPort})[/]");
        AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();
    }
}
