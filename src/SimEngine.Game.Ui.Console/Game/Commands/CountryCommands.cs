using SimEngine.Contracts;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game.Commands;

public sealed class CountryCommand : ICommand
{
    public string Name => "country";
    public string[] Aliases => ["c"];
    public string Description => "Show country detail by tag.";
    public string Usage => "country <tag>";

    public void Execute(GameSession session, string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] country <tag>");
            return;
        }

        var tag = args[0];
        var country = FindCountry(session, tag);
        if (country is null)
        {
            AnsiConsole.MarkupLine($"[red]No country found:[/] {Markup.Escape(tag)}");
            return;
        }

        var tree = new Tree($"[bold]{Markup.Escape(country.DisplayName)}[/]  [dim][[{Markup.Escape(country.Tag)}]][/]");
        tree.AddNode($"Provinces owned: [yellow]{country.ProvinceCount}[/]");
        tree.AddNode($"Treasury: [cyan]{country.FundsE2 / 100.0:F2}[/]");

        AnsiConsole.Write(tree);
    }

    internal static CountryState? FindCountry(GameSession session, string tag) =>
        session.State.Countries.FirstOrDefault(c =>
            string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase)
            || c.DisplayName.Contains(tag, StringComparison.OrdinalIgnoreCase));
}

public sealed class CountriesCommand : ICommand
{
    public string Name => "countries";
    public string[] Aliases => ["cs"];
    public string Description => "List all countries.";
    public string Usage => "countries";

    public void Execute(GameSession session, string[] args)
    {
        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumns("[bold]Tag[/]", "[bold]Name[/]", "[bold]Provinces[/]", "[bold]Treasury[/]");

        foreach (var country in session.State.Countries)
        {
            table.AddRow(
                Markup.Escape(country.Tag),
                Markup.Escape(country.DisplayName),
                country.ProvinceCount.ToString(),
                $"{country.FundsE2 / 100.0:F2}");
        }

        AnsiConsole.Write(table);
    }
}




