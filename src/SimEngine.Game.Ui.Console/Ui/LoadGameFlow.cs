using Microsoft.Extensions.DependencyInjection;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game;
using Spectre.Console;


namespace SimEngine.Game.Ui.Console.Ui;

/// <summary>
/// Load game: creates a lobby game from a save (paused at the saved tick, with the
/// saved settings) on the local server or on a hosted multiplayer server, so other
/// players can join before the host starts.
/// </summary>
public static class LoadGameFlow
{
    public static GameSession? Run(IServiceProvider services)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold]Load Game[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        var selected = SelectPath();
        if (selected is null)
        {
            return null;
        }

        var path = SaveGamePaths.Resolve(selected);
        var multiplayer = AnsiConsole.Prompt(
            new SelectionPrompt<bool>()
                .Title("Play as:")
                .UseConverter(mp => mp ? "Host multiplayer" : "Single player")
                .AddChoices(false, true));

        return multiplayer ? HostLoaded(path, services) : LoadSinglePlayer(path, services);
    }

    private static GameSession? LoadSinglePlayer(string path, IServiceProvider services)
    {
        try
        {
            LobbyGame? lobby = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Loading {Markup.Escape(path)}...", _ =>
                {
                    var client = services.GetRequiredService<GameClient>();
                    lobby = GameSessionFactory.JoinLoadedGame(client, GameSessionFactory.LoadGame(client, path), services);
                });

            using (lobby!)
            {
                // Single-player keeps debug commands on so `step` works while paused.
                lobby!.UpdateSettings(lobby.State.Settings with { AllowDebugCommands = true });
                return LobbyScreen.Run(lobby);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or TimeoutException or ContentMismatchException or CommandRejectedException or RemoteRequestException)
        {
            MainMenu.ShowNotice($"[red]Could not load save:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    /// <summary>The hosted server only loads plain file names from the shared saves directory.</summary>
    private static GameSession? HostLoaded(string path, IServiceProvider services)
    {
        var savesDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SaveGamePaths.SavesDirectory));
        if (!string.Equals(Path.GetDirectoryName(path), savesDirectory, StringComparison.OrdinalIgnoreCase))
        {
            MainMenu.ShowNotice($"[red]Multiplayer games can only load saves from[/] {Markup.Escape(savesDirectory)}");
            return null;
        }

        var fileName = Path.GetFileName(path);
        var playerName = PlayerProfile.GetOrPromptName();
        var port = HostGameFlow.PromptPort();
        return HostGameFlow.Host(
            port,
            playerName,
            $"Loading {Markup.Escape(fileName)}...",
            (client, owned) => GameSessionFactory.JoinLoadedGame(client, GameSessionFactory.LoadGame(client, fileName), services, owned));
    }

    private static string? SelectPath()
    {
        var saves = SaveGamePaths.List();
        if (saves.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No save files found in the default saves directory.[/]");
            return PromptForPath();
        }

        var choices = saves
            .Select(path => new LoadChoice(
                $"{SaveGamePaths.GetDisplayName(path)}  [dim]({File.GetLastWriteTime(path):yyyy-MM-dd HH:mm})[/]",
                path))
            .Concat([new LoadChoice("Enter path manually", null)])
            .ToArray();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<LoadChoice>()
                .Title("Select a save:")
                .UseConverter(choice => choice.DisplayName)
                .AddChoices(choices));

        return selected.Path ?? PromptForPath();
    }

    private static string PromptForPath()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("Save path:")
                .Validate(path => string.IsNullOrWhiteSpace(path)
                    ? ValidationResult.Error("[red]Enter a save path[/]")
                    : ValidationResult.Success()));
    }

    private sealed record LoadChoice(string DisplayName, string? Path);
}




