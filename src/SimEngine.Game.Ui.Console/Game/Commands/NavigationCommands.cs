using SimEngine.Client;
using SimEngine.Contracts;
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
        AnsiConsole.MarkupLine($"[bold gold1]{session.State.CurrentDate:yyyy-MM-dd}[/]  [dim]tick {session.State.TickNumber}[/]");
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
                $"[green]Saved.[/] {Markup.Escape(resolvedPath)}  [dim]{session.State.CurrentDate:yyyy-MM-dd} · tick {session.State.TickNumber} · {session.ProvinceCount} provinces[/]");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or CommandRejectedException or RemoteRequestException)
        {
            AnsiConsole.MarkupLine($"[red]Could not save game:[/] {Markup.Escape(ex.Message)}");
        }
    }
}

public sealed class LoadCommand : ICommand
{
    public string Name => "load";
    public string[] Aliases => [];
    public string Description => "End this game and load a save into a new lobby (host only).";
    public string Usage => "load <file>";

    public void Execute(GameSession session, string[] args)
    {
        if (args.Length != 1)
        {
            AnsiConsole.MarkupLine($"[yellow]Usage:[/] {Markup.Escape(Usage)}");
            return;
        }

        if (!session.IsHost)
        {
            AnsiConsole.MarkupLine("[red]Only the host can load a game.[/]");
            return;
        }

        try
        {
            // Loading first keeps this game running if the save is rejected.
            session.ContinueWithLoaded(GameSessionFactory.LoadGame(session.Client, args[0]));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TimeoutException or CommandRejectedException or RemoteRequestException)
        {
            AnsiConsole.MarkupLine($"[red]Could not load save:[/] {Markup.Escape(ex.Message)}");
        }
    }
}




