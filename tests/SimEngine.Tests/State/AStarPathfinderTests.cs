using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Pathfinding;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class AStarPathfinderTests
{
    private static readonly ProvinceEdgeCost UniformCost = (_, _) => 1;

    [Fact]
    public void StartEqualsGoal_ReturnsSingleNodePath()
    {
        var graph = AdjacencyGraph.Empty;
        var a = new ProvinceId(1);
        var result = AStarPathfinder.FindPath(graph, a, a, UniformCost);
        Assert.True(result.Found);
        Assert.Equal(new[] { a }, result.Nodes);
        Assert.Equal(0, result.TotalCost);
    }

    [Fact]
    public void TwoNodeGraph_ReturnsDirectPath()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        builder.AddUndirectedEdge(a, b);
        var graph = builder.Build();

        var result = AStarPathfinder.FindPath(graph, a, b, UniformCost);
        Assert.True(result.Found);
        Assert.Equal(new[] { a, b }, result.Nodes);
        Assert.Equal(1, result.TotalCost);
    }

    [Fact]
    public void UnreachableGoal_ReturnsFoundFalse()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        var c = new ProvinceId(3);
        builder.AddUndirectedEdge(a, b);
        builder.AddProvince(c);
        var graph = builder.Build();

        var result = AStarPathfinder.FindPath(graph, a, c, UniformCost);
        Assert.False(result.Found);
        Assert.Empty(result.Nodes);
        Assert.Equal(0, result.TotalCost);
    }

    [Fact]
    public void Grid_ShortestPathByUniformCost_IsManhattanLength()
    {
        // 3x3 grid of provinces (ids 1..9)
        var graph = BuildGrid(width: 3, height: 3);
        var start = new ProvinceId(1); // (0,0)
        var goal = new ProvinceId(9);  // (2,2)

        var result = AStarPathfinder.FindPath(graph, start, goal, UniformCost);
        Assert.True(result.Found);
        Assert.Equal(4, result.TotalCost); // Manhattan distance
        Assert.Equal(5, result.Nodes.Count); // start + 4 steps
    }

    [Fact]
    public void Grid_WeightedCost_PrefersCheaperDetour()
    {
        // Linear 1-2-3 graph; direct 1->3 is not connected.
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        var c = new ProvinceId(3);
        builder.AddUndirectedEdge(a, b);
        builder.AddUndirectedEdge(b, c);
        var graph = builder.Build();

        ProvinceEdgeCost cost = (from, to) => (from, to) switch
        {
            _ when (from == a && to == b) || (from == b && to == a) => 5,
            _ when (from == b && to == c) || (from == c && to == b) => 3,
            _ => 1,
        };

        var result = AStarPathfinder.FindPath(graph, a, c, cost);
        Assert.True(result.Found);
        Assert.Equal(8, result.TotalCost);
    }

    [Fact]
    public void SameQueryAcrossManyRuns_ProducesByteIdenticalPath()
    {
        // A 5x5 grid has many equal-cost alternate paths between corners.
        // Tie-breaking must be deterministic — run 100 times, assert identical.
        var graph = BuildGrid(width: 5, height: 5);
        var start = new ProvinceId(1);
        var goal = new ProvinceId(25);

        var first = AStarPathfinder.FindPath(graph, start, goal, UniformCost);
        for (var i = 0; i < 100; i++)
        {
            var again = AStarPathfinder.FindPath(graph, start, goal, UniformCost);
            Assert.Equal(first.Nodes, again.Nodes);
            Assert.Equal(first.TotalCost, again.TotalCost);
        }
    }

    [Fact]
    public void AdmissibleHeuristic_StillProducesOptimalPath()
    {
        var graph = BuildGrid(width: 4, height: 4);
        var start = new ProvinceId(1);
        var goal = new ProvinceId(16);

        // Zero heuristic is trivially admissible and equivalent to Dijkstra.
        ProvinceHeuristic zero = (_, _) => 0;

        var dijkstra = AStarPathfinder.FindPath(graph, start, goal, UniformCost);
        var astar = AStarPathfinder.FindPath(graph, start, goal, UniformCost, zero);
        Assert.Equal(dijkstra.TotalCost, astar.TotalCost);
    }

    [Fact]
    public void NegativeEdgeCost_Throws()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        builder.AddUndirectedEdge(a, b);
        var graph = builder.Build();

        Assert.Throws<InvalidOperationException>(() =>
            AStarPathfinder.FindPath(graph, a, b, (_, _) => -1));
    }

    [Fact]
    public void NoneEndpoint_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AStarPathfinder.FindPath(AdjacencyGraph.Empty, ProvinceId.None, new ProvinceId(1), UniformCost));
    }

    /// <summary>
    /// Builds a <paramref name="width"/>×<paramref name="height"/> grid of
    /// provinces with 4-connectivity. Province ids are assigned row-major
    /// starting at 1.
    /// </summary>
    private static AdjacencyGraph BuildGrid(int width, int height)
    {
        var builder = new AdjacencyGraph.Builder();
        ProvinceId At(int x, int y) => new((uint)(y * width + x + 1));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x + 1 < width)
                {
                    builder.AddUndirectedEdge(At(x, y), At(x + 1, y));
                }

                if (y + 1 < height)
                {
                    builder.AddUndirectedEdge(At(x, y), At(x, y + 1));
                }
            }
        }

        return builder.Build();
    }
}


