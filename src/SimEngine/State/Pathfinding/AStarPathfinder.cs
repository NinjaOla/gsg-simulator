using SimEngine.Ids;

namespace SimEngine.State.Pathfinding;

/// <summary>
/// Deterministic A*/Dijkstra over an <see cref="AdjacencyGraph"/>. All
/// cost math is integer; there are no floats in the search state. Equal-cost
/// tie-breaks resolve by <see cref="ProvinceId.Value"/> ascending, so the
/// same query always returns the same path on the same graph.
/// </summary>
public static class AStarPathfinder
{
    /// <summary>
    /// Finds a least-cost path from <paramref name="start"/> to
    /// <paramref name="goal"/>. When <paramref name="heuristic"/> is null,
    /// the search degrades to Dijkstra (still deterministic, still optimal).
    /// </summary>
    public static PathResult FindPath(
        AdjacencyGraph graph,
        ProvinceId start,
        ProvinceId goal,
        ProvinceEdgeCost cost,
        ProvinceHeuristic? heuristic = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(cost);

        if (start == ProvinceId.None || goal == ProvinceId.None)
        {
            throw new ArgumentException("ProvinceId.None is not a valid pathfinding endpoint.");
        }

        if (start == goal)
        {
            return new PathResult(true, new[] { start }, 0);
        }

        var gScore = new Dictionary<ProvinceId, long>();
        var cameFrom = new Dictionary<ProvinceId, ProvinceId>();
        var open = new SortedSet<OpenNode>();

        gScore[start] = 0;
        var startF = heuristic is null ? 0L : heuristic(start, goal);
        open.Add(new OpenNode(startF, 0, start));

        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);

            if (current.Node == goal)
            {
                return Reconstruct(cameFrom, current.Node, current.G);
            }

            // If we've already discovered a cheaper path to `current.Node`
            // since this entry was enqueued, skip it.
            if (current.G > gScore[current.Node])
            {
                continue;
            }

            var neighbors = graph.NeighborsOf(current.Node);
            foreach (var neighbor in neighbors)
            {
                var edgeCost = cost(current.Node, neighbor);
                if (edgeCost < 0)
                {
                    throw new InvalidOperationException(
                        $"Edge cost from {current.Node} to {neighbor} was negative ({edgeCost}). A* requires non-negative edge costs.");
                }

                var tentative = current.G + edgeCost;
                if (gScore.TryGetValue(neighbor, out var existing) && tentative >= existing)
                {
                    continue;
                }

                gScore[neighbor] = tentative;
                cameFrom[neighbor] = current.Node;
                var h = heuristic is null ? 0L : heuristic(neighbor, goal);
                open.Add(new OpenNode(tentative + h, tentative, neighbor));
            }
        }

        return PathResult.Unreachable;
    }

    private static PathResult Reconstruct(
        Dictionary<ProvinceId, ProvinceId> cameFrom,
        ProvinceId goal,
        long totalCost)
    {
        var reversed = new List<ProvinceId> { goal };
        var cursor = goal;
        while (cameFrom.TryGetValue(cursor, out var previous))
        {
            reversed.Add(previous);
            cursor = previous;
        }

        reversed.Reverse();
        return new PathResult(true, reversed, totalCost);
    }

    /// <summary>
    /// Open-set entry. The three-field ordering (<c>F</c>, <c>G</c>,
    /// <c>NodeValue</c>) forces a total order so <see cref="SortedSet{T}"/>
    /// never treats distinct entries as duplicates and ties resolve
    /// deterministically.
    /// </summary>
    private readonly record struct OpenNode(long F, long G, ProvinceId Node) : IComparable<OpenNode>
    {
        public int CompareTo(OpenNode other)
        {
            var fCmp = F.CompareTo(other.F);
            if (fCmp != 0)
            {
                return fCmp;
            }

            var gCmp = G.CompareTo(other.G);
            if (gCmp != 0)
            {
                return gCmp;
            }

            return Node.CompareTo(other.Node);
        }
    }
}
