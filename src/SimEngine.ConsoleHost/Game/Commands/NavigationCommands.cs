using Spectre.Console;

namespace SimEngine.ConsoleHost.Game.Commands;

public sealed class HelpCommand(CommandRegistry registry) : ICommand
{
    public string Name => "help";
    public string[] Aliases => ["h", "?"];
    public string Description => "List available commands.";
    public string Usage => "help";

    public void Execute(GameSession session, string[] args)
    {
        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumns("[bold]Command[/]", "[bold]Aliases[/]", "[bold]Usage[/]", "[bold]Description[/]");
        foreach (var cmd in registry.All)
        {
            var aliases = cmd.Aliases.Length > 0 ? string.Join(", ", cmd.Aliases) : "[dim]-[/]";
            table.AddRow(Markup.Escape(cmd.Name), aliases, Markup.Escape(cmd.Usage), Markup.Escape(cmd.Description));
        }
        AnsiConsole.Write(table);
    }
}

public sealed class DateCommand : ICommand
{
    public string Name => "date";
    public string[] Aliases => ["d"];
    public string Description => "Show current simulation date and tick.";
    public string Usage => "date";

    public void Execute(GameSession session, string[] args)
    {
        var t = session.Engine.Time.GetUtcNow();
        AnsiConsole.MarkupLine($"[bold gold1]{t:yyyy-MM-dd}[/]  [dim]tick {session.Engine.TickNumber}[/]");
    }
}

public sealed class QuitCommand : ICommand
{
    public string Name => "quit";
    public string[] Aliases => ["q", "exit"];
    public string Description => "Return to the main menu.";
    public string Usage => "quit";

    public void Execute(GameSession session, string[] args) => session.ShouldQuit = true;
}

public sealed class SaveCommand : ICommand
{
    public string Name => "save";
    public string[] Aliases => [];
    public string Description => "Save the game. (Phase 3)";
    public string Usage => "save <file>";

    public void Execute(GameSession session, string[] args) =>
        AnsiConsole.MarkupLine("[yellow]Save/Load arrives in Phase 3.[/]");
}

public sealed class LoadCommand : ICommand
{
    public string Name => "load";
    public string[] Aliases => [];
    public string Description => "Load a saved game. (Phase 3)";
    public string Usage => "load <file>";

    public void Execute(GameSession session, string[] args) =>
        AnsiConsole.MarkupLine("[yellow]Save/Load arrives in Phase 3.[/]");
}
