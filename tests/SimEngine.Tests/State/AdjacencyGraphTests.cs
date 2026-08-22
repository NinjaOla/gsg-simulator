using SimEngine.Ids;
using SimEngine.State;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class AdjacencyGraphTests
{
    [Fact]
    public void Empty_HasZeroProvincesAndEdges()
    {
        Assert.Equal(0, AdjacencyGraph.Empty.ProvinceCount);
        Assert.Equal(0, AdjacencyGraph.Empty.EdgeCount);
    }

    [Fact]
    public void NeighborsOf_UnknownProvince_ReturnsEmpty()
    {
        var graph = AdjacencyGraph.Empty;
        Assert.Empty(graph.NeighborsOf(new ProvinceId(42)));
    }

    [Fact]
    public void Builder_UndirectedEdge_RegistersBothEndpoints()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        builder.AddUndirectedEdge(a, b);
        var graph = builder.Build();

        Assert.Equal(2, graph.ProvinceCount);
        Assert.Equal(1, graph.EdgeCount);
        Assert.Equal(new[] { b }, graph.NeighborsOf(a).ToArray());
        Assert.Equal(new[] { a }, graph.NeighborsOf(b).ToArray());
    }

    [Fact]
    public void Builder_DuplicateEdge_IsDeduplicated()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        builder.AddUndirectedEdge(a, b);
        builder.AddUndirectedEdge(a, b);
        builder.AddUndirectedEdge(b, a);
        var graph = builder.Build();
        Assert.Equal(1, graph.EdgeCount);
        Assert.Single(graph.NeighborsOf(a));
    }

    [Fact]
    public void Builder_SelfLoop_Throws()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        Assert.Throws<ArgumentException>(() => builder.AddUndirectedEdge(a, a));
    }

    [Fact]
    public void Builder_NoneEndpoint_Throws()
    {
        var builder = new AdjacencyGraph.Builder();
        Assert.Throws<ArgumentException>(() =>
            builder.AddUndirectedEdge(ProvinceId.None, new ProvinceId(1)));
    }

    [Fact]
    public void NeighborsOf_ReturnsAscendingOrder_RegardlessOfInsertionOrder()
    {
        var builder = new AdjacencyGraph.Builder();
        var center = new ProvinceId(10);
        builder.AddUndirectedEdge(center, new ProvinceId(3));
        builder.AddUndirectedEdge(center, new ProvinceId(1));
        builder.AddUndirectedEdge(center, new ProvinceId(7));
        builder.AddUndirectedEdge(center, new ProvinceId(2));
        var graph = builder.Build();

        var neighbors = graph.NeighborsOf(center).Select(id => id.Value).ToArray();
        Assert.Equal(new uint[] { 1, 2, 3, 7 }, neighbors);
    }

    [Fact]
    public void AreAdjacent_IsSymmetric()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        builder.AddUndirectedEdge(a, b);
        var graph = builder.Build();

        Assert.True(graph.AreAdjacent(a, b));
        Assert.True(graph.AreAdjacent(b, a));
    }

    [Fact]
    public void AreAdjacent_MissingEdge_ReturnsFalse()
    {
        var builder = new AdjacencyGraph.Builder();
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        var c = new ProvinceId(3);
        builder.AddUndirectedEdge(a, b);
        var graph = builder.Build();

        Assert.False(graph.AreAdjacent(a, c));
    }

    [Fact]
    public void Builder_SparseIds_AreHandledNatively()
    {
        // No dense 1..N assumption — the FrozenDictionary layout accepts
        // whatever ids the caller wants.
        var builder = new AdjacencyGraph.Builder();
        builder.AddUndirectedEdge(new ProvinceId(5), new ProvinceId(100));
        builder.AddUndirectedEdge(new ProvinceId(100), new ProvinceId(42));
        var graph = builder.Build();

        Assert.Equal(3, graph.ProvinceCount);
        Assert.Equal(2, graph.EdgeCount);
        Assert.True(graph.AreAdjacent(new ProvinceId(5), new ProvinceId(100)));
        Assert.True(graph.AreAdjacent(new ProvinceId(42), new ProvinceId(100)));
        Assert.False(graph.AreAdjacent(new ProvinceId(5), new ProvinceId(42)));
    }

    [Fact]
    public void Builder_IsolatedProvince_IsIncludedInProvinceCount()
    {
        var builder = new AdjacencyGraph.Builder();
        builder.AddProvince(new ProvinceId(7));
        var graph = builder.Build();
        Assert.Equal(1, graph.ProvinceCount);
        Assert.Equal(0, graph.EdgeCount);
        Assert.Empty(graph.NeighborsOf(new ProvinceId(7)));
    }
}


