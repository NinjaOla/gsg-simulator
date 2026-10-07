using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game.Commands;
using SimEngine.Game.Ui.Console.Ui;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game;

public static class GameLoop
{
    public static GameSession? Run(GameSession session)
    {
        string? endReason;
        IAsyncDisposable? owned = null;
        using (session)
        {
            new GameScreen(session, BuildRegistry()).Run();
            endReason = session.State.EndReason;
            if (session.PendingLoad is not null)
            {
                // The loaded game lives on the same server, so keep the connection.
                owned = session.DetachOwned();
            }
        }

        if (session.PendingLoad is { } loaded)
        {
            return EnterLoadedLobby(session, loaded, owned);
        }

        // Ended by the server (host left, connection lost), not by this player.
        if (endReason is not null)
        {
            MainMenu.ShowNotice($"[yellow]The game ended:[/] {Markup.Escape(endReason)}");
        }

        return null;
    }

    private static GameSession? EnterLoadedLobby(GameSession previous, GameListing listing, IAsyncDisposable? owned)
    {
        try
        {
            var lobby = GameSessionFactory.JoinLoadedGame(previous.Client, listing, previous.Services, owned);
            owned = null;
            using (lobby)
            {
                // No owned connection means the local single-player server.
                if (lobby.Owned is null)
                {
                    lobby.UpdateSettings(lobby.State.Settings with { AllowDebugCommands = true });
                }

                var shareAddresses = lobby.Owned is MultiplayerConnection { Server: { } server }
                    ? HostGameFlow.GetShareAddresses(server.Port)
                    : null;
                return LobbyScreen.Run(lobby, shareAddresses);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or TimeoutException or ArgumentException or InvalidOperationException
            or ContentMismatchException or CommandRejectedException or RemoteRequestException)
        {
            MainMenu.ShowNotice($"[red]Could not open the loaded game:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
        finally
        {
            owned?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static CommandRegistry BuildRegistry()
    {
        ICommand[] baseCommands =
        [
            new StepCommand(),
            new DateCommand(),
            new CountriesCommand(),
            new CountryCommand(),
            new ProvincesCommand(),
            new ProvinceCommand(),
            new PathCommand(),
            new AdjacencyCommand(),
            new EventsCommand(),
            new MapCommand(),
            new SaveCommand(),
            new LoadCommand(),
            new QuitCommand(),
        ];

        // Build registry once to pass to HelpCommand, then rebuild with help included.
        var reg = new CommandRegistry(baseCommands);
        var help = new HelpCommand(reg);
        return new CommandRegistry([help, .. baseCommands]);
    }
}




