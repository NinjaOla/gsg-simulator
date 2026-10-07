using SimEngine.Game.Ui.Console.Game;
using SimEngine.Game.Ui.Console.Ui;

internal static class App
{
    internal static void Run(IServiceProvider services)
    {
        while (true)
        {
            var choice = MainMenu.Show();

            GameSession? session = choice switch
            {
                MainMenuChoice.SinglePlayer => SinglePlayerFlow.Run(services),
                MainMenuChoice.HostMultiplayer => HostGameFlow.Run(services),
                MainMenuChoice.JoinMultiplayer => JoinGameFlow.Run(services),
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




