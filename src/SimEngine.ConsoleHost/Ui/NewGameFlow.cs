using System.Globalization;
using SimEngine.ConsoleHost.Game;
using SimEngine.State;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;
using Spectre.Console;

namespace SimEngine.ConsoleHost.Ui;

public static class NewGameFlow
{
    public static GameSession? Run()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold]New Game[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        var world = AnsiConsole.Prompt(
            new SelectionPrompt<WorldAsset>()
                .Title("Select a world:")
                .UseConverter(w => w.DisplayName)
                .AddChoices(WorldAsset.All));

        if (!File.Exists(world.FullPath))
        {
            AnsiConsole.MarkupLine($"[red]World file not found:[/] {world.FullPath}");
            AnsiConsole.MarkupLine("[dim]Press any key.[/]");
            Console.ReadKey(intercept: true);
            return null;
        }

        var startDateStr = AnsiConsole.Prompt(
            new TextPrompt<string>("Start date [dim](yyyy-MM-dd)[/]:")
                .DefaultValue("1836-01-01")
                .Validate(s =>
                    DateTimeOffset.TryParseExact(s, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Enter a date like 1836-01-01[/]")));

        var startDate = DateTimeOffset.ParseExact(startDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var seedStr = AnsiConsole.Prompt(
            new TextPrompt<string>("PRNG seed [dim](0 = random)[/]:")
                .DefaultValue("0")
                .Validate(s =>
                    ulong.TryParse(s, out _)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Enter a non-negative integer[/]")));

        ulong seed = ulong.Parse(seedStr);
        if (seed == 0)
            seed = (ulong)Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        AnsiConsole.MarkupLine($"[dim]Seed: {seed}[/]");
        AnsiConsole.WriteLine();

        SimulationState? state = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Loading {world.DisplayName}...", _ =>
            {
                var loader = new GeoJsonWorldLoader();
                state = WorldLoaders.LoadIntoState(loader, world.FullPath);
            });

        if (state is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to load world.[/]");
            Console.ReadKey(intercept: true);
            return null;
        }

        var session = GameSessionFactory.CreateNew(state, startDate, seed, world.DisplayName);

        AnsiConsole.MarkupLine(
            $"[green]Ready.[/] {session.ProvinceCount} provinces, {session.AdjacencyEdgeCount} borders.");
        AnsiConsole.MarkupLine("[dim]Press any key to start.[/]");
        Console.ReadKey(intercept: true);

        return session;
    }
}
