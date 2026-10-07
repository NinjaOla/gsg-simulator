using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console.Game;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Ui;

/// <summary>
/// Pre-game lobby: shows the joined players and game settings live from the
/// subscription. The host edits settings and starts; other players wait and
/// enter the game when the host starts it. Used by single-player and multiplayer alike.
/// </summary>
public static class LobbyScreen
{
    private static readonly TimeSpan FrameWait = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Returns the started session, or <c>null</c> if the player left or the lobby
    /// closed. <paramref name="shareAddresses"/> are shown to the host so others can join.
    /// </summary>
    public static GameSession? Run(LobbyGame lobby, IReadOnlyList<string>? shareAddresses = null)
    {
        ArgumentNullException.ThrowIfNull(lobby);

        string? message = null;
        var rendered = -1L;
        var dirty = true;
        while (true)
        {
            var state = lobby.State;
            if (state.EndReason is not null)
            {
                AnsiConsole.Clear();
                MainMenu.ShowNotice($"[yellow]The lobby closed:[/] {Markup.Escape(state.EndReason)}");
                return null;
            }

            if (state.Stage is SessionStage.Running or SessionStage.Paused)
            {
                return lobby.Enter();
            }

            while (System.Console.KeyAvailable)
            {
                var key = System.Console.ReadKey(intercept: true);
                dirty = true;
                if (key.Key == ConsoleKey.Escape)
                {
                    return null;
                }

                try
                {
                    switch (char.ToLowerInvariant(key.KeyChar))
                    {
                        case 's' when lobby.IsHost:
                            return lobby.Start();
                        case 'e' when lobby.IsHost:
                            lobby.UpdateSettings(PromptSettings(state.Settings));
                            message = "[green]Settings updated.[/]";
                            break;
                        case 'l':
                            return null;
                    }
                }
                catch (Exception ex) when (ex is CommandRejectedException or ConnectionLostException or RemoteRequestException)
                {
                    message = $"[red]Rejected:[/] {Markup.Escape(ex.Message)}";
                }
            }

            var sequence = state.Sequence;
            if (dirty || sequence != rendered)
            {
                rendered = sequence;
                dirty = false;
                Render(lobby, shareAddresses, message);
            }

            state.WaitForChange(rendered, FrameWait);
        }
    }

    private static void Render(LobbyGame lobby, IReadOnlyList<string>? shareAddresses, string? message)
    {
        var state = lobby.State;
        var isHost = lobby.IsHost;

        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold]Lobby[/] [dim]{Markup.Escape(lobby.Listing.Name)}[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"World: [bold]{Markup.Escape(lobby.Listing.WorldName)}[/]  [dim]{lobby.World.Provinces.Count} provinces[/]");

        if (shareAddresses is not null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Players can join at:");
            foreach (var address in shareAddresses)
            {
                AnsiConsole.MarkupLine($"  [green]{Markup.Escape(address)}[/]");
            }

            AnsiConsole.MarkupLine("[dim]Players outside your network need the port forwarded on your router.[/]");
        }

        AnsiConsole.WriteLine();
        var players = new Table().Border(TableBorder.Minimal).AddColumns("[bold]Player[/]", "[bold]Role[/]");
        foreach (var player in state.Players)
        {
            var role = player.Id == state.HostPlayerId ? "[gold1]host[/]" : "[dim]player[/]";
            var name = Markup.Escape(player.Name);
            if (player.Id == lobby.PlayerId)
            {
                name += " [dim](you)[/]";
            }

            if (!player.Connected)
            {
                name += " [red](disconnected)[/]";
            }

            players.AddRow(name, role);
        }

        AnsiConsole.Write(players);

        var settings = state.Settings;
        var table = new Table().Border(TableBorder.Minimal).AddColumns("[bold]Setting[/]", "[bold]Value[/]");
        table.AddRow("Pause control", Describe(settings.PauseControl));
        table.AddRow("Speed control", Describe(settings.SpeedControl));
        table.AddRow("Debug commands", settings.AllowDebugCommands ? "allowed" : "disabled");
        table.AddRow("Initial speed", settings.InitialSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AnsiConsole.Write(table);

        if (!isHost)
        {
            AnsiConsole.MarkupLine("[dim]Waiting for the host to start the game...[/]");
        }

        if (message is not null)
        {
            AnsiConsole.MarkupLine(message);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(isHost
            ? "[yellow]s[/] start  [yellow]e[/] edit settings  [yellow]l[/]/[yellow]Esc[/] leave"
            : "[yellow]l[/]/[yellow]Esc[/] leave");
    }

    private static GameSettings PromptSettings(GameSettings current)
    {
        var pause = PromptAccess("Who may pause/resume?", current.PauseControl);
        var speed = PromptAccess("Who may change speed?", current.SpeedControl);
        var debug = AnsiConsole.Prompt(
            new ConfirmationPrompt("Allow debug commands (e.g. step)?") { DefaultValue = current.AllowDebugCommands });
        var initialSpeed = AnsiConsole.Prompt(
            new TextPrompt<int>($"Initial speed [dim]({GameSettings.MinSpeed}-{GameSettings.MaxSpeed})[/]:")
                .DefaultValue(current.InitialSpeed)
                .Validate(s => GameSettings.IsValidSpeed(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"[red]Enter {GameSettings.MinSpeed}-{GameSettings.MaxSpeed}[/]")));

        return current with
        {
            PauseControl = pause,
            SpeedControl = speed,
            AllowDebugCommands = debug,
            InitialSpeed = initialSpeed,
        };
    }

    private static ControlAccess PromptAccess(string title, ControlAccess current) =>
        AnsiConsole.Prompt(
            new SelectionPrompt<ControlAccess>()
                .Title($"{title} [dim](current: {Describe(current)})[/]")
                .UseConverter(Describe)
                .AddChoices(ControlAccess.HostOnly, ControlAccess.Anyone));

    private static string Describe(ControlAccess access) => access switch
    {
        ControlAccess.HostOnly => "host only",
        ControlAccess.Anyone => "anyone",
        _ => access.ToString(),
    };
}
