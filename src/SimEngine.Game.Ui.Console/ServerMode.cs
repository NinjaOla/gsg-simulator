using Akka.Hosting;
using Akka.Remote.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Contracts;
using SimEngine.Server;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console;

/// <summary>
/// Runs the ConsoleHost as a standalone network server: it enables Akka
/// remoting on a loopback endpoint so external client processes (other
/// ConsoleHost instances) can attach and share sessions. Selected with the
/// <c>--server</c> switch; without it the host runs in-process single-player
/// mode. The server survives independently of any client.
/// </summary>
internal static class ServerMode
{
    /// <summary>The <c>--server</c> switch that selects this mode.</summary>
    internal const string Switch = "--server";

    /// <summary>The Akka actor system name that forms the remote address.</summary>
    internal const string SystemName = "SimEngineServer";

    private const string HostArg = "--host";
    private const string PortArg = "--port";
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 8110;

    /// <summary>True when <paramref name="args"/> requests server mode.</summary>
    internal static bool IsRequested(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Array.Exists(args, a => string.Equals(a, Switch, StringComparison.Ordinal));
    }

    /// <summary>
    /// Starts the network server and blocks until the process is asked to stop
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
            .ConfigureServices(services =>
            {
                services.AddSimEngineServer();
                services.AddAkka(SystemName, builder =>
                {
                    builder
                        .WithRemoting(options.Host, options.Port)
                        .WithSimEngineActors(AkkaExecutionMode.LocalTest);
                });
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

        AnsiConsole.MarkupLine("[dim]Server stopped.[/]");
        return 0;
    }

    private static bool TryParseOptions(string[] args, out ServerOptions options, out string error)
    {
        options = new ServerOptions(DefaultHost, DefaultPort);
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case Switch:
                    break;
                case HostArg:
                    if (!TryTakeValue(args, ref i, HostArg, out var host, out error))
                        return false;
                    options = options with { Host = host };
                    break;
                case PortArg:
                    if (!TryTakeValue(args, ref i, PortArg, out var raw, out error))
                        return false;
                    if (!int.TryParse(raw, out var port) || port is < 1 or > 65535)
                    {
                        error = $"{PortArg} value '{raw}' is not a valid port (1-65535).";
                        return false;
                    }
                    options = options with { Port = port };
                    break;
                default:
                    error = $"Unknown argument '{args[i]}'.";
                    return false;
            }
        }

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

    private static void RenderBanner(ServerOptions options)
    {
        AnsiConsole.Write(
            new Rule("[bold gold1]SimEngine Server[/]").RuleStyle("gold1 dim"));
        AnsiConsole.MarkupLine(
            $"[dim]Listening for clients on[/] [green]{options.Host}:{options.Port}[/]");
        AnsiConsole.MarkupLine(
            $"[dim]Remote root path:[/] [green]akka.tcp://{SystemName}@{options.Host}:{options.Port}/user[/]");
        AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();
    }

    private sealed record ServerOptions(string Host, int Port);
}
