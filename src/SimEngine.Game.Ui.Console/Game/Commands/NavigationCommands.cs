using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game.Commands;

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
    public string Description => "Save the game.";
    public string Usage => "save <file>";

    public void Execute(GameSession session, string[] args)
    {
        if (args.Length != 1)
        {
            AnsiConsole.MarkupLine($"[yellow]Usage:[/] {Markup.Escape(Usage)}");
            return;
        }

        try
        {
            var resolvedPath = GameSessionFactory.Save(session, args[0]);
            AnsiConsole.MarkupLine(
                $"[green]Saved.[/] {Markup.Escape(resolvedPath)}  [dim]{session.Engine.Time.Current:yyyy-MM-dd} · tick {session.Engine.TickNumber} · {session.ProvinceCount} provinces[/]");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            AnsiConsole.MarkupLine($"[red]Could not save game:[/] {Markup.Escape(ex.Message)}");
        }
    }
}

public sealed class LoadCommand : ICommand
{
    public string Name => "load";
    public string[] Aliases => [];
    public string Description => "Load a saved game.";
    public string Usage => "load <file>";

    public void Execute(GameSession session, string[] args)
    {
        if (args.Length != 1)
        {
            AnsiConsole.MarkupLine($"[yellow]Usage:[/] {Markup.Escape(Usage)}");
            return;
        }

        try
        {
            session.ReplaceWith(GameSessionFactory.Load(args[0], session.Services));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            AnsiConsole.MarkupLine($"[red]Could not load save:[/] {Markup.Escape(ex.Message)}");
        }
    }
}




