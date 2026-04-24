using SimEngine.ConsoleHost.Game;
using SimEngine.ConsoleHost.Ui;
using Spectre.Console;

internal static class App
{
    internal static void Run()
    {
        while (true)
        {
            var choice = MainMenu.Show();

            switch (choice)
            {
                case MainMenuChoice.NewGame:
                    var session = NewGameFlow.Run();
                    if (session is not null)
                        GameLoop.Run(session);
                    break;

                case MainMenuChoice.LoadGame:
                    AnsiConsole.MarkupLine("\n[yellow]Save/Load arrives in Phase 3.[/] Press any key.");
                    Console.ReadKey(intercept: true);
                    break;

                case MainMenuChoice.Quit:
                    return;
            }
        }
    }
}
