using SimEngine.Ids;
using SimEngine.State.Components;
using Spectre.Console;

namespace SimEngine.ConsoleHost.Game.Commands;

public sealed class ProvincesCommand : ICommand
{
    private const int MaxDisplay = 100;

    public string Name => "provinces";
    public string[] Aliases => ["ps"];
    public string Description => $"List provinces (first {MaxDisplay} shown).";
    public string Usage => "provinces";

    public void Execute(GameSession session, string[] args)
    {
        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumns("[bold]ID[/]", "[bold]Name[/]", "[bold]Terrain[/]", "[bold]Lat[/]", "[bold]Lon[/]", "[bold]Neighbors[/]");

        int count = 0;
        foreach (var (id, comp) in session.Engine.State.Entities.Query<ProvinceComponent>())
        {
            if (count++ >= MaxDisplay) break;
            var pid = ProvinceId.OfEntity(id);
            var neighbors = session.Engine.State.Adjacency.NeighborsOf(pid).Length;
            var lat = comp.CentroidLatE6 / 1_000_000.0;
            var lon = comp.CentroidLonE6 / 1_000_000.0;
            table.AddRow(
                pid.Value.ToString(),
                Markup.Escape(comp.Name),
                comp.Terrain.ToString(),
                $"{lat:F2}",
                $"{lon:F2}",
                neighbors.ToString());
        }

        AnsiConsole.Write(table);

        var total = session.Engine.State.Entities.CountOf<ProvinceComponent>();
        if (total > MaxDisplay)
            AnsiConsole.MarkupLine($"[dim]... {total - MaxDisplay} more. Use [yellow]province <id>[/] for detail.[/]");
    }
}

public sealed class ProvinceCommand : ICommand
{
    public string Name => "province";
    public string[] Aliases => ["p"];
    public string Description => "Show province detail by id or name.";
    public string Usage => "province <id|name>";

    public void Execute(GameSession session, string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] province <id|name>");
            return;
        }

        var query = string.Join(' ', args);
        var found = FindProvince(session, query);
        if (found is null)
        {
            AnsiConsole.MarkupLine($"[red]No province found:[/] {Markup.Escape(query)}");
            return;
        }

        var (pid, comp) = found.Value;
        var neighbors = session.Engine.State.Adjacency.NeighborsOf(pid);
        var lat = comp.CentroidLatE6 / 1_000_000.0;
        var lon = comp.CentroidLonE6 / 1_000_000.0;

        var tree = new Tree($"[bold]{Markup.Escape(comp.Name)}[/]  [dim]P#{pid.Value}[/]");
        tree.AddNode($"Terrain: [yellow]{comp.Terrain}[/]");
        tree.AddNode($"Centroid: {lat:F4} lat, {lon:F4} lon");

        var neighborsNode = tree.AddNode($"Neighbors ({neighbors.Length})");
        foreach (var n in neighbors)
        {
            var label = session.Engine.State.Entities.TryGet<ProvinceComponent>(n.AsEntity(), out var nc)
                ? Markup.Escape(nc.Name)
                : $"P#{n.Value}";
            neighborsNode.AddNode($"P#{n.Value} — {label}");
        }

        AnsiConsole.Write(tree);
    }

    internal static (ProvinceId, ProvinceComponent)? FindProvince(GameSession session, string query)
    {
        if (uint.TryParse(query, out var idVal))
        {
            var pid = new ProvinceId(idVal);
            if (session.Engine.State.Entities.TryGet<ProvinceComponent>(pid.AsEntity(), out var comp))
                return (pid, comp);
        }

        foreach (var (eid, c) in session.Engine.State.Entities.Query<ProvinceComponent>())
        {
            if (c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return (ProvinceId.OfEntity(eid), c);
        }

        return null;
    }
}
