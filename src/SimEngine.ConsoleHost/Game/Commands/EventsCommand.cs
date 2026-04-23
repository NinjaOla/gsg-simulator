using Spectre.Console;

namespace SimEngine.ConsoleHost.Game.Commands;

public sealed class EventsCommand : ICommand
{
    private const int DefaultTail = 20;

    public string Name => "events";
    public string[] Aliases => ["ev"];
    public string Description => "Show recent engine events.";
    public string Usage => "events [n]";

    public void Execute(GameSession session, string[] args)
    {
        int tail = DefaultTail;
        if (args.Length > 0 && int.TryParse(args[0], out var n) && n > 0)
            tail = n;

        var log = session.EventLog;
        var slice = log.Count <= tail ? (IEnumerable<string>)log : log.Skip(log.Count - tail);

        bool any = false;
        foreach (var entry in slice)
        {
            AnsiConsole.MarkupLine(entry);
            any = true;
        }

        if (!any)
            AnsiConsole.MarkupLine("[dim]No events yet. Try [yellow]step month[/] to generate some.[/]");
    }
}
