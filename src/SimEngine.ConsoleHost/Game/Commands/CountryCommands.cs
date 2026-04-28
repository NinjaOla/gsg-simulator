using SimEngine.Game.Components;
using SimEngine.Ids;
using SimEngine.State;
using Spectre.Console;

namespace SimEngine.ConsoleHost.Game.Commands;

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
        var found = FindCountry(session, tag);
        if (found is null)
        {
            AnsiConsole.MarkupLine($"[red]No country found:[/] {Markup.Escape(tag)}");
            return;
        }

        var (countryId, country) = found.Value;

        session.Engine.State.Entities.TryGet<TreasuryComponent>(countryId, out var treasury);
        var ownedCount = session.Engine.State.Relationships
            .GetOutbound(countryId, RelationshipLabel.Owns)
            .Count();

        var tree = new Tree($"[bold]{Markup.Escape(country.DisplayName)}[/]  [dim][{Markup.Escape(country.Tag)}][/]");
        tree.AddNode($"Provinces owned: [yellow]{ownedCount}[/]");
        tree.AddNode($"Treasury: [cyan]{treasury.FundsE2 / 100.0:F2}[/]");

        AnsiConsole.Write(tree);
    }

    internal static (EntityId, CountryComponent)? FindCountry(GameSession session, string tag)
    {
        foreach (var (id, country) in session.Engine.State.Entities.Query<CountryComponent>())
        {
            if (string.Equals(country.Tag, tag, StringComparison.OrdinalIgnoreCase)
                || country.DisplayName.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                return (id, country);
            }
        }

        return null;
    }
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

        foreach (var (id, country) in session.Engine.State.Entities.Query<CountryComponent>())
        {
            var provinces = session.Engine.State.Relationships
                .GetOutbound(id, RelationshipLabel.Owns)
                .Count();

            session.Engine.State.Entities.TryGet<TreasuryComponent>(id, out var treasury);

            table.AddRow(
                Markup.Escape(country.Tag),
                Markup.Escape(country.DisplayName),
                provinces.ToString(),
                $"{treasury.FundsE2 / 100.0:F2}");
        }

        AnsiConsole.Write(table);
    }
}
