using SimEngine.State.Pathfinding;
using Spectre.Console;

namespace SimEngine.Game.Ui.Console.Game.Commands;

public sealed class PathCommand : ICommand
{
    public string Name => "path";
    public string[] Aliases => ["pt"];
    public string Description => "Find shortest hop path between two provinces.";
    public string Usage => "path <id|name> <id|name>";

    public void Execute(GameSession session, string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] path <id|name> <id|name>");
            return;
        }

        var a = ProvinceCommand.FindProvince(session, args[0]);
        var b = ProvinceCommand.FindProvince(session, args[^1]);

        if (a is null) { AnsiConsole.MarkupLine($"[red]Not found:[/] {Markup.Escape(args[0])}"); return; }
        if (b is null) { AnsiConsole.MarkupLine($"[red]Not found:[/] {Markup.Escape(args[^1])}"); return; }

        var (startId, _) = a.Value;
        var (goalId, _) = b.Value;

        if (startId == goalId)
        {
            AnsiConsole.MarkupLine("[yellow]Start and goal are the same province.[/]");
            return;
        }

        // Local preview on static adjacency; authoritative movement paths are computed by the server.
        var result = AStarPathfinder.FindPath(
            session.World.Adjacency,
            startId,
            goalId,
            (_, _) => 1);

        if (!result.Found)
        {
            AnsiConsole.MarkupLine($"[red]No path found[/] from [bold]P#{startId.Value}[/] to [bold]P#{goalId.Value}[/].");
            return;
        }

        var crumbs = result.Nodes.Select(pid => $"[bold]{ProvinceCommand.ProvinceLabel(session, pid)}[/]");

        AnsiConsole.MarkupLine(string.Join(" [dim]->[/] ", crumbs));
        AnsiConsole.MarkupLine($"[dim]Hops: {result.Nodes.Count - 1}  Cost: {result.TotalCost}[/]");
    }
}

public sealed class AdjacencyCommand : ICommand
{
    public string Name => "adjacency";
    public string[] Aliases => ["adj"];
    public string Description => "Adjacency stats, or neighbors of a specific province.";
    public string Usage => "adjacency [id|name]";

    public void Execute(GameSession session, string[] args)
    {
        var adj = session.World.Adjacency;

        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine($"Provinces: [bold]{adj.ProvinceCount}[/]  Borders: [bold]{adj.EdgeCount}[/]");
            return;
        }

        var query = string.Join(' ', args);
        var found = ProvinceCommand.FindProvince(session, query);
        if (found is null)
        {
            AnsiConsole.MarkupLine($"[red]Province not found:[/] {Markup.Escape(query)}");
            return;
        }

        var (pid, comp) = found.Value;
        var neighbors = adj.NeighborsOf(pid);

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(comp.Name)}[/] (P#{pid.Value}) — {neighbors.Length} neighbor(s):");
        foreach (var n in neighbors)
        {
            AnsiConsole.MarkupLine($"  [dim]P#{n.Value}[/] {ProvinceCommand.ProvinceLabel(session, n)}");
        }
    }
}




