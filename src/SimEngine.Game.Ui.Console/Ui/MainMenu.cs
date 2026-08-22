using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Ui;

public enum MainMenuChoice { NewGame, LoadGame, Quit }

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
                    MainMenuChoice.NewGame  => "New Game",
                    MainMenuChoice.LoadGame => "Load Game",
                    MainMenuChoice.Quit     => "Quit",
                    _                      => c.ToString(),
                })
                .AddChoices(MainMenuChoice.NewGame, MainMenuChoice.LoadGame, MainMenuChoice.Quit));
    }
}




