using SimEngine.ConsoleHost.Game;
using SimEngine.ConsoleHost.Ui;

internal static class App
{
    internal static void Run(IServiceProvider services)
    {
        while (true)
        {
            var choice = MainMenu.Show();

            GameSession? session = choice switch
            {
                MainMenuChoice.NewGame => NewGameFlow.Run(services),
                MainMenuChoice.LoadGame => LoadGameFlow.Run(services),
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
