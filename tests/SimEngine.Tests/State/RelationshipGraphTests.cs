using SimEngine.Ids;
using SimEngine.State;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class RelationshipGraphTests
{
    private static readonly EntityId A = new(1);
    private static readonly EntityId B = new(2);
    private static readonly EntityId C = new(3);
    private static readonly EntityId D = new(4);

    [Fact]
    public void AddContains_RoundTrip()
    {
        var graph = new RelationshipGraph();
        graph.Add(A, RelationshipLabel.Owns, B);
        Assert.True(graph.Contains(A, RelationshipLabel.Owns, B));
        Assert.False(graph.Contains(A, RelationshipLabel.Vassal, B));
        Assert.False(graph.Contains(B, RelationshipLabel.Owns, A));
    }

    [Fact]
    public void Add_DuplicateEdge_DoesNotIncrementCount()
    {
        var graph = new RelationshipGraph();
        graph.Add(A, RelationshipLabel.Owns, B);
        graph.Add(A, RelationshipLabel.Owns, B);
        Assert.Equal(1, graph.Count);
    }

    [Fact]
    public void Remove_ReturnsFalse_WhenEdgeMissing()
    {
        var graph = new RelationshipGraph();
        Assert.False(graph.Remove(A, RelationshipLabel.Owns, B));
    }

    [Fact]
    public void Remove_RemovesFromBothIndices()
    {
        var graph = new RelationshipGraph();
        graph.Add(A, RelationshipLabel.Owns, B);
        Assert.True(graph.Remove(A, RelationshipLabel.Owns, B));
        Assert.Empty(graph.GetOutbound(A, RelationshipLabel.Owns));
        Assert.Empty(graph.GetInbound(B, RelationshipLabel.Owns));
    }

    [Fact]
    public void GetOutbound_MultiTarget_ReturnsAscendingByEntityId()
    {
        var graph = new RelationshipGraph();
        // Insert in out-of-order sequence.
        graph.Add(A, RelationshipLabel.Owns, D);
        graph.Add(A, RelationshipLabel.Owns, B);
        graph.Add(A, RelationshipLabel.Owns, C);
        Assert.Equal(new[] { B, C, D }, graph.GetOutbound(A, RelationshipLabel.Owns).ToArray());
    }

    [Fact]
    public void GetInbound_MultiSource_ReturnsAscendingByEntityId()
    {
        var graph = new RelationshipGraph();
        graph.Add(C, RelationshipLabel.Owns, A);
        graph.Add(B, RelationshipLabel.Owns, A);
        graph.Add(D, RelationshipLabel.Owns, A);
        Assert.Equal(new[] { B, C, D }, graph.GetInbound(A, RelationshipLabel.Owns).ToArray());
    }

    [Fact]
    public void GetAllOutbound_ReturnsLabelThenTargetOrder()
    {
        var graph = new RelationshipGraph();
        graph.Add(A, RelationshipLabel.Vassal, C);
        graph.Add(A, RelationshipLabel.Owns, B);
        graph.Add(A, RelationshipLabel.AtWarWith, D);
        graph.Add(A, RelationshipLabel.Owns, C);

        var ordered = graph.GetAllOutbound(A).ToArray();
        Assert.Equal(new (RelationshipLabel, EntityId)[]
        {
            (RelationshipLabel.Owns, B),
            (RelationshipLabel.Owns, C),
            (RelationshipLabel.Vassal, C),
            (RelationshipLabel.AtWarWith, D),
        }, ordered);
    }

    [Fact]
    public void GetAllOutbound_UnrelatedEntitiesAreNotEmitted()
    {
        var graph = new RelationshipGraph();
        graph.Add(A, RelationshipLabel.Owns, B);
        graph.Add(C, RelationshipLabel.Owns, D);
        Assert.Equal(new (RelationshipLabel, EntityId)[] { (RelationshipLabel.Owns, B) },
            graph.GetAllOutbound(A).ToArray());
    }

    [Fact]
    public void Remove_LastTargetUnderLabel_CollapsesBucket()
    {
        var graph = new RelationshipGraph();
        graph.Add(A, RelationshipLabel.Owns, B);
        graph.Remove(A, RelationshipLabel.Owns, B);
        graph.Add(A, RelationshipLabel.Owns, C);
        Assert.Equal(new[] { C }, graph.GetOutbound(A, RelationshipLabel.Owns).ToArray());
        Assert.Equal(1, graph.Count);
    }

    [Fact]
    public void Clear_EmptiesEverything()
    {
        var graph = new RelationshipGraph();
        graph.Add(A, RelationshipLabel.Owns, B);
        graph.Add(C, RelationshipLabel.Vassal, D);
        graph.Clear();
        Assert.Equal(0, graph.Count);
        Assert.Empty(graph.GetOutbound(A, RelationshipLabel.Owns));
        Assert.Empty(graph.GetInbound(D, RelationshipLabel.Vassal));
    }
}


