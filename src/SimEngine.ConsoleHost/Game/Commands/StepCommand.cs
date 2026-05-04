using SimEngine.Contracts;
using Spectre.Console;

namespace SimEngine.ConsoleHost.Game.Commands;

public sealed class StepCommand : ICommand
{
    private const int ProgressThreshold = 50;

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

        if (session.Grain is { } grain)
        {
            ExecuteViaGrain(grain, ticks);
        }
        else
        {
            ExecuteLocal(session, ticks);
        }

        var after = session.Engine.Time.GetUtcNow();
        AnsiConsole.MarkupLine(
            $"[green]{before:yyyy-MM-dd}[/] [dim]->[/] [bold gold1]{after:yyyy-MM-dd}[/]  " +
            $"[dim](+{ticks} tick{(ticks == 1 ? "" : "s")})[/]");
    }

    private static void ExecuteViaGrain(IGameSessionGrain grain, int ticks)
    {
        if (ticks > ProgressThreshold)
        {
            AnsiConsole.Progress()
                .AutoClear(true)
                .HideCompleted(true)
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
                .Start(ctx =>
                {
                    var task = ctx.AddTask($"Simulating {ticks} ticks", maxValue: ticks);
                    for (int i = 0; i < ticks; i++)
                    {
                        grain.StepAsync().GetAwaiter().GetResult();
                        task.Increment(1);
                    }
                });
        }
        else
        {
            for (int i = 0; i < ticks; i++)
                grain.StepAsync().GetAwaiter().GetResult();
        }
    }

    private static void ExecuteLocal(GameSession session, int ticks)
    {
        if (ticks > ProgressThreshold)
        {
            AnsiConsole.Progress()
                .AutoClear(true)
                .HideCompleted(true)
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
                .Start(ctx =>
                {
                    var task = ctx.AddTask($"Simulating {ticks} ticks", maxValue: ticks);
                    for (int i = 0; i < ticks; i++)
                    {
                        session.Engine.Step();
                        task.Increment(1);
                    }
                });
        }
        else
        {
            for (int i = 0; i < ticks; i++)
                session.Engine.Step();
        }
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
