using SimEngine.Contracts;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game.Commands;

public sealed class StepCommand : ICommand
{
    public string Name => "step";
    public string[] Aliases => ["s"];
    public string Description => "Debug: advance ticks immediately (game must be paused).";
    public string Usage => "step [n|day|week|month|year]";

    public void Execute(GameSession session, string[] args)
    {
        int ticks = ParseTicks(args);
        if (ticks <= 0)
        {
            AnsiConsole.MarkupLine("[red]Invalid step argument.[/] Usage: step [[n|day|week|month|year]]");
            return;
        }

        if (session.State.Stage != SessionStage.Paused)
        {
            AnsiConsole.MarkupLine("[yellow]Pause the game first[/] [dim](Space)[/] to use [yellow]step[/].");
            return;
        }

        var before = session.State.CurrentDate;
        var executed = session.Advance(ticks).TicksExecuted;
        var after = session.State.CurrentDate;
        AnsiConsole.MarkupLine(
            $"[green]{before:yyyy-MM-dd}[/] [dim]->[/] [bold gold1]{after:yyyy-MM-dd}[/]  " +
            $"[dim](+{executed} tick{(executed == 1 ? "" : "s")})[/]");
    }

    private static int ParseTicks(string[] args) =>
        args.Length == 0 ? 1 :
        args[0].ToLowerInvariant() switch
        {
            "day" => 1,
            "week" => 7,
            "month" => 30,
            "year" => 365,
            _ => int.TryParse(args[0], out var n) && n > 0 ? n : -1,
        };
}




