using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.State.Pathfinding;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class HaversineHeuristicTests
{
    [Fact]
    public void Create_SameProvince_ReturnsZero()
    {
        var builder = new WorldBuilder();
        var province = builder.AddProvince("Alpha", latE6: 0, lonE6: 0);
        var state = builder.Build();

        var heuristic = HaversineHeuristic.Create(state, minimumCostPerKilometer: 1);

        Assert.Equal(0, heuristic(province, province));
    }

    [Fact]
    public void Create_ValidProvincePair_ReturnsNonNegativeValue()
    {
        var builder = new WorldBuilder();
        var alpha = builder.AddProvince("Alpha", latE6: 0, lonE6: 0);
        var beta = builder.AddProvince("Beta", latE6: 1_000_000, lonE6: 0);
        var state = builder.Build();

        var heuristic = HaversineHeuristic.Create(state, minimumCostPerKilometer: 1);

        Assert.True(heuristic(alpha, beta) >= 0);
    }

    [Fact]
    public void Create_SymmetricInputs_ReturnSameValue()
    {
        var builder = new WorldBuilder();
        var alpha = builder.AddProvince("Alpha", latE6: 0, lonE6: 0);
        var beta = builder.AddProvince("Beta", latE6: 3_000_000, lonE6: 2_000_000);
        var state = builder.Build();

        var heuristic = HaversineHeuristic.Create(state, minimumCostPerKilometer: 2);

        Assert.Equal(heuristic(alpha, beta), heuristic(beta, alpha));
    }

    [Fact]
    public void Create_GreaterSeparation_IncreasesLowerBound()
    {
        var builder = new WorldBuilder();
        var alpha = builder.AddProvince("Alpha", latE6: 0, lonE6: 0);
        var beta = builder.AddProvince("Beta", latE6: 1_000_000, lonE6: 0);
        var gamma = builder.AddProvince("Gamma", latE6: 5_000_000, lonE6: 0);
        var state = builder.Build();

        var heuristic = HaversineHeuristic.Create(state, minimumCostPerKilometer: 1);

        Assert.True(heuristic(alpha, gamma) > heuristic(alpha, beta));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveMinimumCostPerKilometer_Throws(int minimumCostPerKilometer)
    {
        var state = new WorldBuilder().Build();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HaversineHeuristic.Create(state, minimumCostPerKilometer));
    }

    [Fact]
    public void Create_MissingProvinceComponent_Throws()
    {
        var adjacency = new AdjacencyGraph.Builder();
        adjacency.AddProvince(new ProvinceId(1));
        var state = new SimulationState(adjacency.Build());

        Assert.Throws<InvalidOperationException>(() =>
            HaversineHeuristic.Create(state, minimumCostPerKilometer: 1));
    }

    [Fact]
    public void Create_LargeScaleFactor_ClampsToIntMaxValue()
    {
        var builder = new WorldBuilder();
        var alpha = builder.AddProvince("Alpha", latE6: 0, lonE6: 0);
        var beta = builder.AddProvince("Beta", latE6: 0, lonE6: 180_000_000);
        var state = builder.Build();

        var heuristic = HaversineHeuristic.Create(state, int.MaxValue);

        Assert.Equal(int.MaxValue, heuristic(alpha, beta));
    }

    [Fact]
    public void FindPath_WithAdmissibleHaversineHeuristic_MatchesDijkstraCost()
    {
        var builder = new WorldBuilder();
        var a = builder.AddProvince("A", latE6: 0, lonE6: 0);
        var b = builder.AddProvince("B", latE6: 1_000_000, lonE6: 0);
        var c = builder.AddProvince("C", latE6: 2_000_000, lonE6: 0);
        var d = builder.AddProvince("D", latE6: 0, lonE6: 1_000_000);
        var e = builder.AddProvince("E", latE6: 1_000_000, lonE6: 1_000_000);
        var f = builder.AddProvince("F", latE6: 2_000_000, lonE6: 1_000_000);
        builder.AddEdge(a, b);
        builder.AddEdge(b, c);
        builder.AddEdge(a, d);
        builder.AddEdge(d, e);
        builder.AddEdge(e, f);
        builder.AddEdge(f, c);
        var state = builder.Build();

        ProvinceEdgeCost cost = (from, to) => (from, to) switch
        {
            _ when (from == a && to == b) || (from == b && to == a) => 200,
            _ when (from == b && to == c) || (from == c && to == b) => 200,
            _ when (from == a && to == d) || (from == d && to == a) => 260,
            _ when (from == d && to == e) || (from == e && to == d) => 260,
            _ when (from == e && to == f) || (from == f && to == e) => 260,
            _ when (from == f && to == c) || (from == c && to == f) => 260,
            _ => throw new InvalidOperationException($"Unexpected edge {from}->{to}.")
        };

        var dijkstra = AStarPathfinder.FindPath(state.Adjacency, a, c, cost);
        var heuristic = HaversineHeuristic.Create(state, minimumCostPerKilometer: 1);
        var astar = AStarPathfinder.FindPath(state.Adjacency, a, c, cost, heuristic);

        Assert.Equal(dijkstra.TotalCost, astar.TotalCost);
    }

    [Fact]
    public void FindPath_WithHaversineHeuristic_RepeatedRunsStayDeterministic()
    {
        var builder = new WorldBuilder();
        var a = builder.AddProvince("A", latE6: 0, lonE6: 0);
        var b = builder.AddProvince("B", latE6: 1_000_000, lonE6: 0);
        var c = builder.AddProvince("C", latE6: 2_000_000, lonE6: 0);
        var d = builder.AddProvince("D", latE6: 3_000_000, lonE6: 0);
        builder.AddEdge(a, b);
        builder.AddEdge(b, c);
        builder.AddEdge(c, d);
        var state = builder.Build();
        var heuristic = HaversineHeuristic.Create(state, minimumCostPerKilometer: 1);

        var first = AStarPathfinder.FindPath(state.Adjacency, a, d, (_, _) => 200, heuristic);

        for (var i = 0; i < 25; i++)
        {
            var again = AStarPathfinder.FindPath(state.Adjacency, a, d, (_, _) => 200, heuristic);
            Assert.Equal(first.Nodes, again.Nodes);
            Assert.Equal(first.TotalCost, again.TotalCost);
        }
    }
}
