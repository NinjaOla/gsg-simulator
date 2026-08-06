using SimEngine.ConsoleHost.Game.Commands;
using Spectre.Console;

namespace SimEngine.ConsoleHost.Game;

public static class GameLoop
{
    public static GameSession? Run(GameSession session)
    {
        using (session)
        {
            var registry = BuildRegistry();
            RenderWelcome(session);

            while (!session.ShouldQuit)
            {
                RenderHeader(session);

                var input = AnsiConsole.Prompt(
                    new TextPrompt<string>("[grey]>[/] ")
                        .AllowEmpty());

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (!registry.TryExecute(session, input))
                    AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(input)}. Type [yellow]help[/].");
            }
        }

        return session.ReplacementSession;
    }

    private static void RenderWelcome(GameSession session)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold gold1]{Markup.Escape(session.WorldName)}[/]").RuleStyle("gold1 dim"));
        AnsiConsole.MarkupLine(
            $"[dim]{session.ProvinceCount} provinces · {session.AdjacencyEdgeCount} borders · " +
            "type [yellow]help[/] for commands[/]");
        AnsiConsole.WriteLine();
    }

    private static void RenderHeader(GameSession session)
    {
        var t = session.Engine.Time.GetUtcNow();
        AnsiConsole.Write(new Rule(
            $"[bold]{t:yyyy-MM-dd}[/]  [dim]tick {session.Engine.TickNumber}[/]")
            .RuleStyle("dim"));
    }

    private static CommandRegistry BuildRegistry()
    {
        ICommand[] baseCommands =
        [
            new StepCommand(),
            new DateCommand(),
            new CountriesCommand(),
            new CountryCommand(),
            new ProvincesCommand(),
            new ProvinceCommand(),
            new PathCommand(),
            new AdjacencyCommand(),
            new EventsCommand(),
            new MapCommand(),
            new SaveCommand(),
            new LoadCommand(),
            new QuitCommand(),
        ];

        // Build registry once to pass to HelpCommand, then rebuild with help included.
        var reg = new CommandRegistry(baseCommands);
        var help = new HelpCommand(reg);
        return new CommandRegistry([help, ..baseCommands]);
    }
}
