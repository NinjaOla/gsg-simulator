using SimEngine.Game.Ui.Console.Game;
using SimEngine.Server.Worlds;
using Spectre.Console;
using System.Globalization;

namespace SimEngine.Game.Ui.Console.Ui;

public static class NewGameFlow
{
    public static GameSession? Run(IServiceProvider services)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold]New Game[/]").RuleStyle("dim grey"));
        AnsiConsole.WriteLine();

        var world = AnsiConsole.Prompt(
            new SelectionPrompt<WorldAssetInfo>()
                .Title("Select a world:")
                .UseConverter(w => w.DisplayName)
                .AddChoices(WorldCatalog.All));

        var worldPath = WorldCatalog.ResolvePath(world);
        var countriesPath = WorldCatalog.ResolveCountriesPath(world);
        if (!File.Exists(worldPath))
        {
            AnsiConsole.MarkupLine($"[red]World file not found:[/] {worldPath}");
            AnsiConsole.MarkupLine("[dim]Press any key.[/]");
            System.Console.ReadKey(intercept: true);
            return null;
        }

        if (!File.Exists(countriesPath))
        {
            AnsiConsole.MarkupLine($"[red]Countries file not found:[/] {countriesPath}");
            AnsiConsole.MarkupLine("[dim]Press any key.[/]");
            System.Console.ReadKey(intercept: true);
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

        try
        {
            GameSession? session = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Loading {world.DisplayName}...", _ =>
                {
                    session = GameSessionFactory.CreateNew(services, world.WorldId, startDate, seed);
                });

            if (session is null)
            {
                throw new InvalidOperationException("Initialization did not produce a game session.");
            }

            AnsiConsole.MarkupLine(
                $"[green]Ready.[/] {session.ProvinceCount} provinces, {session.AdjacencyEdgeCount} borders.");
            AnsiConsole.MarkupLine("[dim]Press any key to start.[/]");
            System.Console.ReadKey(intercept: true);

            return session;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
        {
            AnsiConsole.MarkupLine($"[red]Could not start game:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[dim]Press any key.[/]");
            System.Console.ReadKey(intercept: true);
            return null;
        }
    }
}




