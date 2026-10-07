using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Ui;

public enum MainMenuChoice { SinglePlayer, HostMultiplayer, JoinMultiplayer, LoadGame, Quit }

public static class MainMenu
{
    public static MainMenuChoice Show()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("GSG Engine")
                .Centered()
                .Color(Color.Gold1));

        AnsiConsole.Write(new Rule("[dim]Grand Strategy Simulation[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        return AnsiConsole.Prompt(
            new SelectionPrompt<MainMenuChoice>()
                .Title("What would you like to do?")
                .UseConverter(c => c switch
                {
                    MainMenuChoice.SinglePlayer => "Single player",
                    MainMenuChoice.HostMultiplayer => "Host multiplayer",
                    MainMenuChoice.JoinMultiplayer => "Join multiplayer",
                    MainMenuChoice.LoadGame => "Load game",
                    MainMenuChoice.Quit => "Quit",
                    _ => c.ToString(),
                })
                .AddChoices(Enum.GetValues<MainMenuChoice>()));
    }

    /// <summary>Shows a markup message and waits for a key before returning to the menu.</summary>
    public static void ShowNotice(string markup)
    {
        AnsiConsole.MarkupLine(markup);
        AnsiConsole.MarkupLine("[dim]Press any key.[/]");
        System.Console.ReadKey(intercept: true);
    }
}




