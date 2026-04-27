using SimEngine.ConsoleHost.Game;
using SimEngine.ConsoleHost.Ui;

internal static class App
{
    internal static void Run()
    {
        while (true)
        {
            var choice = MainMenu.Show();

            GameSession? session = choice switch
            {
                MainMenuChoice.NewGame => NewGameFlow.Run(),
                MainMenuChoice.LoadGame => LoadGameFlow.Run(),
                MainMenuChoice.Quit => null,
                _ => null,
            };

            if (choice == MainMenuChoice.Quit)
            {
                return;
            }

            while (session is not null)
            {
                session = GameLoop.Run(session);
            }
        }
    }
}
