using Microsoft.Extensions.DependencyInjection;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Ui;

/// <summary>
/// Single-player: creates a game through the lobby, joins as the only player
/// (host), and starts it — the same messages a multiplayer host sends.
/// </summary>
public static class SinglePlayerFlow
{
    public static GameSession? Run(IServiceProvider services)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold]Single Player[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        var options = NewGamePrompt.Run();
        if (options is null)
        {
            return null;
        }

        var quickStart = AnsiConsole.Prompt(
            new SelectionPrompt<bool>()
                .UseConverter(q => q ? "Quick start" : "Open lobby")
                .AddChoices(true, false));

        try
        {
            LobbyGame? lobby = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Loading {Markup.Escape(options.World.DisplayName)}...", _ =>
                {
                    lobby = GameSessionFactory.CreateLobbyGame(
                        services.GetRequiredService<GameClient>(),
                        services,
                        options.World.DisplayName,
                        options.World.WorldId,
                        options.StartDate,
                        options.Seed);
                });

            if (lobby is null)
            {
                throw new InvalidOperationException("Initialization did not produce a game.");
            }

            using (lobby)
            {
                // Single-player keeps debug commands on so `step` works while paused.
                lobby.UpdateSettings(lobby.State.Settings with { AllowDebugCommands = true });
                return quickStart ? lobby.Start() : LobbyScreen.Run(lobby);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or ContentMismatchException or CommandRejectedException or RemoteRequestException)
        {
            MainMenu.ShowNotice($"[red]Could not start game:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }
}
