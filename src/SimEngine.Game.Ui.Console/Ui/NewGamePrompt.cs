using System.Globalization;
using SimEngine.Game.Worlds;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Ui;

/// <summary>New-game choices shared by single-player and hosting.</summary>
internal sealed record NewGameOptions(WorldAssetInfo World, DateTimeOffset StartDate, ulong Seed);

/// <summary>Prompts for world, start date, and seed.</summary>
internal static class NewGamePrompt
{
    /// <summary>Returns <c>null</c> (after telling the player) if the world's files are missing.</summary>
    public static NewGameOptions? Run()
    {
        var world = AnsiConsole.Prompt(
            new SelectionPrompt<WorldAssetInfo>()
                .Title("Select a world:")
                .UseConverter(w => w.DisplayName)
                .AddChoices(WorldCatalog.All));

        var worldPath = WorldCatalog.ResolvePath(world);
        var countriesPath = WorldCatalog.ResolveCountriesPath(world);
        if (!File.Exists(worldPath))
        {
            MainMenu.ShowNotice($"[red]World file not found:[/] {Markup.Escape(worldPath)}");
            return null;
        }

        if (!File.Exists(countriesPath))
        {
            MainMenu.ShowNotice($"[red]Countries file not found:[/] {Markup.Escape(countriesPath)}");
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
        return new NewGameOptions(world, startDate, seed);
    }
}
