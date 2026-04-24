namespace SimEngine.ConsoleHost.Game.Commands;

public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }
    string Description { get; }
    string Usage { get; }
    void Execute(GameSession session, string[] args);
}
