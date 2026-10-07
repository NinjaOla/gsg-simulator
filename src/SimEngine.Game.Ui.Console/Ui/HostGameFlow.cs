using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game;
using SimEngine.Networking;
using SimEngine.Networking.Tcp;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Ui;

/// <summary>
/// Host multiplayer: starts a child server on a TCP port, connects to it like
/// any other client, creates a game, and opens the lobby for others to join.
/// The server stops when the host leaves the game.
/// </summary>
public static class HostGameFlow
{
    public static GameSession? Run(IServiceProvider services)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold]Host Multiplayer[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        var playerName = PlayerProfile.GetOrPromptName();
        var port = PromptPort();

        var options = NewGamePrompt.Run();
        if (options is null)
        {
            return null;
        }

        return Host(
            port,
            playerName,
            $"Loading {Markup.Escape(options.World.DisplayName)}...",
            (client, owned) => GameSessionFactory.CreateLobbyGame(
                client,
                services,
                $"{playerName}'s game",
                options.World.WorldId,
                options.StartDate,
                options.Seed,
                owned));
    }

    internal static int PromptPort() =>
        AnsiConsole.Prompt(
            new TextPrompt<int>("Port:")
                .DefaultValue(ServerMode.DefaultPort)
                .Validate(p => p is >= 1 and <= IPEndPoint.MaxPort
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"[red]Enter 1-{IPEndPoint.MaxPort}[/]")));

    /// <summary>
    /// Starts a child server, connects to it, creates the game with <paramref name="createLobby"/>
    /// (which takes ownership of the connection on success), and opens the lobby.
    /// </summary>
    internal static GameSession? Host(
        int port,
        string playerName,
        string loadingStatus,
        Func<GameClient, MultiplayerConnection, LobbyGame> createLobby)
    {
        ServerProcess? server = null;
        GameClient? client = null;
        try
        {
            LobbyGame? lobby = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Starting server...", ctx =>
                {
                    server = ServerProcess.StartAsync(port).GetAwaiter().GetResult();
                    ctx.Status("Connecting...");
                    client = GameClient.ConnectAsync(new TransportRegistry(new TcpTransport()), server.ConnectEndpoint, playerName)
                        .GetAwaiter().GetResult();
                    ctx.Status(loadingStatus);
                    lobby = createLobby(client, new MultiplayerConnection(client, server));
                });

            // The lobby game owns the connection and server now.
            client = null;
            var shareAddresses = GetShareAddresses(server!.Port);
            server = null;

            using (lobby!)
            {
                return LobbyScreen.Run(lobby!, shareAddresses);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException
            or InvalidDataException or ArgumentException or ContentMismatchException or CommandRejectedException or RemoteRequestException)
        {
            MainMenu.ShowNotice($"[red]Could not host the game:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
        finally
        {
            client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>This machine's IPv4 addresses with the port, for other players to type in.</summary>
    internal static string[] GetShareAddresses(int port)
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => $"{a}:{port}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return addresses.Length > 0 ? addresses : [$"127.0.0.1:{port}"];
        }
        catch (NetworkInformationException)
        {
            return [$"127.0.0.1:{port}"];
        }
    }
}
