using SimEngine.Ids;
using SimEngine.State.Components;
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

        var result = AStarPathfinder.FindPath(
            session.Engine.State.Adjacency,
            startId,
            goalId,
            (_, _) => 1);

        if (!result.Found)
        {
            AnsiConsole.MarkupLine($"[red]No path found[/] from [bold]P#{startId.Value}[/] to [bold]P#{goalId.Value}[/].");
            return;
        }

        var crumbs = result.Nodes.Select(pid =>
        {
            var name = session.Engine.State.Entities.TryGet<ProvinceComponent>(pid.AsEntity(), out var c)
                ? Markup.Escape(c.Name) : $"P#{pid.Value}";
            return $"[bold]{name}[/]";
        });

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
        var adj = session.Engine.State.Adjacency;

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
            var label = session.Engine.State.Entities.TryGet<ProvinceComponent>(n.AsEntity(), out var nc)
                ? Markup.Escape(nc.Name) : $"P#{n.Value}";
            AnsiConsole.MarkupLine($"  [dim]P#{n.Value}[/] {label}");
        }
    }
}




