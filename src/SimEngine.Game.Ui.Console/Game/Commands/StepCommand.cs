using SimEngine.Client;
using SimEngine.Contracts;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game.Commands;

public sealed class StepCommand : ICommand
{
    private const int ProgressThreshold = 50;
    private const int ProgressChunkTicks = 25;

    public string Name => "step";
    public string[] Aliases => ["s"];
    public string Description => "Advance simulation ticks.";
    public string Usage => "step [n|day|week|month|year]";

    public void Execute(GameSession session, string[] args)
    {
        int ticks = ParseTicks(args);
        if (ticks <= 0)
        {
            AnsiConsole.MarkupLine("[red]Invalid step argument.[/] Usage: step [[n|day|week|month|year]]");
            return;
        }

        var before = session.Engine.Time.GetUtcNow();
        var executed = ticks > ProgressThreshold
            ? StepWithProgress(session.Session, ticks)
            : Step(session.Session, ticks);

        if (executed == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Simulation is paused.[/] No ticks executed.");
            return;
        }

        var after = session.Engine.Time.GetUtcNow();
        AnsiConsole.MarkupLine(
            $"[green]{before:yyyy-MM-dd}[/] [dim]->[/] [bold gold1]{after:yyyy-MM-dd}[/]  " +
            $"[dim](+{executed} tick{(executed == 1 ? "" : "s")})[/]");
    }

    private static int Step(SessionClient session, int ticks) =>
        session.AdvanceAsync(ticks).GetAwaiter().GetResult().TicksExecuted;

    private static int StepWithProgress(SessionClient session, int ticks)
    {
        var executed = 0;
        AnsiConsole.Progress()
            .AutoClear(true)
            .HideCompleted(true)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Simulating {ticks} ticks", maxValue: ticks);
                var remaining = ticks;
                while (remaining > 0)
                {
                    var chunk = Math.Min(remaining, ProgressChunkTicks);
                    var result = session.AdvanceAsync(chunk).GetAwaiter().GetResult();
                    executed += result.TicksExecuted;
                    task.Increment(chunk);
                    remaining -= chunk;

                    if (result.TicksExecuted == 0)
                        break; // paused mid-run
                }
            });
        return executed;
    }

    private static int ParseTicks(string[] args) =>
        args.Length == 0 ? 1 :
        args[0].ToLowerInvariant() switch
        {
            "day"   => 1,
            "week"  => 7,
            "month" => 30,
            "year"  => 365,
            _ => int.TryParse(args[0], out var n) && n > 0 ? n : -1,
        };
}




