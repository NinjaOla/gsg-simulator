using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class WorldBuilderTests
{
    [Fact]
    public void AddProvince_AllocatesDenseIdsFromOne()
    {
        var builder = new WorldBuilder();
        var a = builder.AddProvince("Aegis");
        var b = builder.AddProvince("Brun");
        var c = builder.AddProvince("Caldora");
        Assert.Equal(new ProvinceId(1), a);
        Assert.Equal(new ProvinceId(2), b);
        Assert.Equal(new ProvinceId(3), c);
    }

    [Fact]
    public void Build_CreatesOneEntityPerProvinceWithComponent()
    {
        var builder = new WorldBuilder();
        var a = builder.AddProvince("Aegis", Terrain.Land, 45_000_000, 9_000_000);
        var b = builder.AddProvince("Brun", Terrain.Sea);
        var state = builder.Build();

        Assert.Equal(2, state.Entities.Count);

        Assert.True(state.Entities.TryGet<ProvinceComponent>(a.AsEntity(), out var aComponent));
        Assert.Equal("Aegis", aComponent.Name);
        Assert.Equal(Terrain.Land, aComponent.Terrain);
        Assert.Equal(45_000_000, aComponent.CentroidLatE6);
        Assert.Equal(9_000_000, aComponent.CentroidLonE6);

        Assert.True(state.Entities.TryGet<ProvinceComponent>(b.AsEntity(), out var bComponent));
        Assert.Equal(Terrain.Sea, bComponent.Terrain);
    }

    [Fact]
    public void Build_PopulatesAdjacencyFromDeclaredEdges()
    {
        var builder = new WorldBuilder();
        var a = builder.AddProvince("A");
        var b = builder.AddProvince("B");
        var c = builder.AddProvince("C");
        builder.AddEdge(a, b);
        builder.AddEdge(b, c);
        var state = builder.Build();

        Assert.Equal(3, state.Adjacency.ProvinceCount);
        Assert.Equal(2, state.Adjacency.EdgeCount);
        Assert.True(state.Adjacency.AreAdjacent(a, b));
        Assert.True(state.Adjacency.AreAdjacent(b, c));
        Assert.False(state.Adjacency.AreAdjacent(a, c));
    }

    [Fact]
    public void AddEdge_UnknownProvince_Throws()
    {
        var builder = new WorldBuilder();
        var a = builder.AddProvince("A");
        Assert.Throws<ArgumentException>(() => builder.AddEdge(a, new ProvinceId(99)));
    }

    [Fact]
    public void Build_IsSingleUse()
    {
        var builder = new WorldBuilder();
        builder.AddProvince("A");
        builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.AddProvince("B"));
    }

    [Fact]
    public void ProvinceIdMatchesEntityIdUnderlyingValue()
    {
        // Provinces are entities — and by convention their ids share .Value.
        var builder = new WorldBuilder();
        var first = builder.AddProvince("A");
        var second = builder.AddProvince("B");
        var state = builder.Build();

        Assert.True(state.Entities.Exists(first.AsEntity()));
        Assert.True(state.Entities.Exists(second.AsEntity()));
        Assert.Equal(first.Value, first.AsEntity().Value);
        Assert.Equal(second.Value, second.AsEntity().Value);
    }
}


