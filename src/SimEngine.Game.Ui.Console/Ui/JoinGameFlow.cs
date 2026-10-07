using System.Net.Sockets;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game;
using SimEngine.Game.Worlds;
using SimEngine.Networking;
using SimEngine.Networking.Tcp;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Ui;

/// <summary>
/// Join multiplayer: connects to a host's server, lists open lobbies, loads the
/// chosen game's world locally, joins with its content hash, and waits in the
/// lobby until the host starts.
/// </summary>
public static class JoinGameFlow
{
    public static GameSession? Run(IServiceProvider services)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold]Join Multiplayer[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        var playerName = PlayerProfile.GetOrPromptName();
        var address = AnsiConsole.Prompt(
            new TextPrompt<string>("Host address [dim](host[[:port]])[/]:")
                .DefaultValue("127.0.0.1")
                .Validate(s => TcpTransport.TryCreateEndpoint(s, ServerMode.DefaultPort, out _)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Enter a host name or IP, optionally with :port[/]")));
        TcpTransport.TryCreateEndpoint(address, ServerMode.DefaultPort, out var endpoint);

        GameClient? client = null;
        try
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Connecting to {Markup.Escape(endpoint!.ToString())}...", _ =>
                {
                    client = GameClient.ConnectAsync(new TransportRegistry(new TcpTransport()), endpoint, playerName)
                        .GetAwaiter().GetResult();
                });

            var listing = PickGame(client!);
            if (listing is null)
            {
                return null;
            }

            if (WorldCatalog.Find(listing.WorldId) is null)
            {
                MainMenu.ShowNotice(
                    $"[red]You don't have the world[/] {Markup.Escape(listing.WorldName)} [dim]({Markup.Escape(listing.WorldId)})[/].");
                return null;
            }

            LobbyGame? lobby = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Loading {Markup.Escape(listing.WorldName)}...", _ =>
                {
                    lobby = GameSessionFactory.JoinLobbyGame(client!, listing, services, new MultiplayerConnection(client!));
                });

            // The lobby game owns the connection now.
            client = null;
            using (lobby!)
            {
                return LobbyScreen.Run(lobby!);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException
            or InvalidDataException or ArgumentException or ContentMismatchException or CommandRejectedException or RemoteRequestException)
        {
            MainMenu.ShowNotice($"[red]Could not join:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
        finally
        {
            client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Lets the player pick an open lobby; <c>null</c> to go back.</summary>
    private static GameListing? PickGame(GameClient client)
    {
        while (true)
        {
            var games = client.ListGamesAsync().GetAwaiter().GetResult()
                .Where(g => g.Stage == SessionStage.Lobby)
                .OrderBy(g => g.CreatedAt)
                .ToArray();

            var choices = games
                .Select(g => new GameChoice(
                    $"{Markup.Escape(g.Name)}  [dim]{Markup.Escape(g.WorldName)} · {g.PlayerCount} player(s)[/]", g))
                .Append(new GameChoice("[dim]Refresh[/]", null, Refresh: true))
                .Append(new GameChoice("[dim]Back[/]", null))
                .ToArray();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<GameChoice>()
                    .Title(games.Length > 0 ? "Select a game:" : "[yellow]No open games on this server.[/]")
                    .UseConverter(c => c.Label)
                    .AddChoices(choices));

            if (!selected.Refresh)
            {
                return selected.Game;
            }
        }
    }

    private sealed record GameChoice(string Label, GameListing? Game, bool Refresh = false);
}
