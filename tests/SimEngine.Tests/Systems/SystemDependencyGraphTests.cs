using SimEngine.Systems;
using SimEngine.Tests.TestSupport;
using SimEngine.Time;
using Xunit;

namespace SimEngine.Tests.Systems;

public sealed class SystemDependencyGraphTests
{
    private static readonly StateKey X = new("X");
    private static readonly StateKey Y = new("Y");
    private static readonly StateKey Z = new("Z");

    private static RecordingSystem S(
        string name,
        StateKey[]? reads = null,
        StateKey[]? writes = null,
        int order = 0)
        => new(name, TickCadence.EveryTick, order, reads, writes);

    [Fact]
    public void Empty_ReturnsZeroBatches()
    {
        var graph = SystemDependencyGraph.Build(Array.Empty<ISimulationSystem>());
        Assert.Empty(graph.Batches);
    }

    [Fact]
    public void SingleSystem_OneBatch()
    {
        var a = S("A");
        var graph = SystemDependencyGraph.Build(new ISimulationSystem[] { a });
        Assert.Single(graph.Batches);
        Assert.Single(graph.Batches[0]);
        Assert.Same(a, graph.Batches[0][0]);
    }

    [Fact]
    public void IndependentReaders_SameBatch()
    {
        var a = S("A", reads: new[] { X });
        var b = S("B", reads: new[] { X });
        var graph = SystemDependencyGraph.Build(new ISimulationSystem[] { a, b });
        Assert.Single(graph.Batches);
        Assert.Equal(2, graph.Batches[0].Count);
    }

    [Fact]
    public void ReaderAndWriter_WriterRunsFirst()
    {
        var writer = S("A_write", writes: new[] { X });
        var reader = S("B_read", reads: new[] { X });
        var graph = SystemDependencyGraph.Build(new ISimulationSystem[] { writer, reader });
        Assert.Equal(2, graph.Batches.Count);
        Assert.Same(writer, graph.Batches[0][0]);
        Assert.Same(reader, graph.Batches[1][0]);
    }

    [Fact]
    public void TwoWriters_Sequential()
    {
        var a = S("A", writes: new[] { X });
        var b = S("B", writes: new[] { X });
        var graph = SystemDependencyGraph.Build(new ISimulationSystem[] { a, b });
        Assert.Equal(2, graph.Batches.Count);
    }

    [Fact]
    public void ReadRead_NotAConflict()
    {
        var systems = new ISimulationSystem[50];
        for (var i = 0; i < 50; i++)
        {
            systems[i] = S($"R{i:D2}", reads: new[] { X });
        }

        var graph = SystemDependencyGraph.Build(systems);
        Assert.Single(graph.Batches);
        Assert.Equal(50, graph.Batches[0].Count);
    }

    [Fact]
    public void Diamond_ThreeBatches()
    {
        // A writes X; B reads X writes Y; C reads X writes Z; D reads Y and Z.
        var a = S("A", writes: new[] { X });
        var b = S("B", reads: new[] { X }, writes: new[] { Y });
        var c = S("C", reads: new[] { X }, writes: new[] { Z });
        var d = S("D", reads: new[] { Y, Z });
        var graph = SystemDependencyGraph.Build(new ISimulationSystem[] { a, b, c, d });

        Assert.Equal(3, graph.Batches.Count);
        Assert.Equal(new[] { "A" }, graph.Batches[0].Select(s => s.Name));
        Assert.Equal(new[] { "B", "C" }, graph.Batches[1].Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new[] { "D" }, graph.Batches[2].Select(s => s.Name));
    }

    [Fact]
    public void DuplicateNames_Throws()
    {
        var a = S("Dup");
        var b = S("Dup");
        Assert.Throws<InvalidOperationException>(
            () => SystemDependencyGraph.Build(new ISimulationSystem[] { a, b }));
    }

    [Fact]
    public void Cycle_Throws_WithCycleMembers()
    {
        // A: writes X, reads Y  ->  must run after whoever writes Y (B)
        // B: writes Y, reads X  ->  must run after whoever writes X (A)
        // Each produces what the other consumes — genuine cycle.
        var a = S("A", reads: new[] { Y }, writes: new[] { X });
        var b = S("B", reads: new[] { X }, writes: new[] { Y });

        var ex = Assert.Throws<SystemDependencyCycleException>(
            () => SystemDependencyGraph.Build(new ISimulationSystem[] { a, b }));
        Assert.Contains("A", ex.Cycle);
        Assert.Contains("B", ex.Cycle);
    }

    [Fact]
    public void EmptyName_Throws()
    {
        var a = S(string.Empty);
        Assert.Throws<InvalidOperationException>(
            () => SystemDependencyGraph.Build(new ISimulationSystem[] { a }));
    }

    [Fact]
    public void BatchOrder_IsStableAcrossInsertionOrder()
    {
        var a = S("A", writes: new[] { X });
        var b = S("B", reads: new[] { X });
        var c = S("C", reads: new[] { X });

        var graph1 = SystemDependencyGraph.Build(new ISimulationSystem[] { a, b, c });
        var graph2 = SystemDependencyGraph.Build(new ISimulationSystem[] { c, b, a });

        Assert.Equal(graph1.Batches.Count, graph2.Batches.Count);
        for (var i = 0; i < graph1.Batches.Count; i++)
        {
            Assert.Equal(
                graph1.Batches[i].Select(s => s.Name).ToArray(),
                graph2.Batches[i].Select(s => s.Name).ToArray());
        }
    }

    [Fact]
    public void Dependencies_ListsDirectPredecessors()
    {
        var a = S("A", writes: new[] { X });
        var b = S("B", reads: new[] { X });
        var graph = SystemDependencyGraph.Build(new ISimulationSystem[] { a, b });

        Assert.Empty(graph.Dependencies["A"]);
        Assert.Equal(new[] { "A" }, graph.Dependencies["B"]);
    }
}
