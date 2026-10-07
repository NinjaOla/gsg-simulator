using SimEngine.Ids;
using SimEngine.State.Loading;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game.Commands;

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
        foreach (var province in session.World.Provinces)
        {
            if (count++ >= MaxDisplay) break;
            var pid = province.ProvinceId;
            var neighbors = session.World.Adjacency.NeighborsOf(pid).Length;
            var lat = province.CentroidLatE6 / 1_000_000.0;
            var lon = province.CentroidLonE6 / 1_000_000.0;
            table.AddRow(
                pid.Value.ToString(),
                Markup.Escape(province.Name),
                province.Terrain.ToString(),
                $"{lat:F2}",
                $"{lon:F2}",
                neighbors.ToString());
        }

        AnsiConsole.Write(table);

        var total = session.World.Provinces.Count;
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
            if (args.Length == 2)
                AnsiConsole.MarkupLine($"[dim]Tip: to find a path between two provinces use [yellow]path {Markup.Escape(args[0])} {Markup.Escape(args[1])}[/][/]");
            return;
        }

        var (pid, comp) = found.Value;
        var neighbors = session.World.Adjacency.NeighborsOf(pid);
        var lat = comp.CentroidLatE6 / 1_000_000.0;
        var lon = comp.CentroidLonE6 / 1_000_000.0;

        var tree = new Tree($"[bold]{Markup.Escape(comp.Name)}[/]  [dim]P#{pid.Value}[/]");
        tree.AddNode($"Terrain: [yellow]{comp.Terrain}[/]");
        tree.AddNode($"Centroid: {lat:F4} lat, {lon:F4} lon");

        var neighborsNode = tree.AddNode($"Neighbors ({neighbors.Length})");
        foreach (var n in neighbors)
        {
            neighborsNode.AddNode($"P#{n.Value} — {ProvinceLabel(session, n)}");
        }

        AnsiConsole.Write(tree);
    }

    internal static string ProvinceLabel(GameSession session, ProvinceId id) =>
        session.World.TryGetProvince(id, out var p) ? Markup.Escape(p.Name) : $"P#{id.Value}";

    internal static (ProvinceId, ProvinceSeed)? FindProvince(GameSession session, string query)
    {
        if (uint.TryParse(query, out var idVal) && session.World.TryGetProvince(new ProvinceId(idVal), out var byId))
            return (byId.ProvinceId, byId);

        foreach (var p in session.World.Provinces)
        {
            if (p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return (p.ProvinceId, p);
        }

        return null;
    }
}




