using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Tests.State.Loading;

public sealed class WorldLoadersTests
{
    private static string Grid4Path =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "grid4.geojson");

    [Fact]
    public void LoadIntoState_Grid4Stream_ProducesStateWithFourEntities()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var state = WorldLoaders.LoadIntoState(loader, stream);

        Assert.Equal(4, state.Entities.Count);
        Assert.Equal(4, state.Adjacency.EdgeCount);
    }

    [Fact]
    public void LoadIntoState_Grid4Path_OverloadOpensFile()
    {
        var loader = new GeoJsonWorldLoader();
        var state = WorldLoaders.LoadIntoState(loader, Grid4Path);

        Assert.Equal(4, state.Entities.Count);
    }

    [Fact]
    public void LoadIntoState_PostBuild_EntityIdMatchesProvinceId()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var state = WorldLoaders.LoadIntoState(loader, stream);

        for (uint i = 1; i <= 4; i++)
        {
            var pid = new ProvinceId(i);
            Assert.True(state.Entities.TryGet<ProvinceComponent>(pid.AsEntity(), out var comp));
            Assert.Equal(pid.Value, pid.AsEntity().Value);
            Assert.False(string.IsNullOrEmpty(comp.Name));
        }
    }

    [Fact]
    public void LoadIntoState_FromResult_EquivalentInputsProduceIdenticalState()
    {
        // Two WorldLoadResults that describe the same logical world but were
        // built independently should produce identical SimulationState — this
        // pins the canonical edge-replay order in WorldLoaders.
        var seeds = new ProvinceSeed[]
        {
            new(new ProvinceId(1), "A", Terrain.Land, 1_500_000, 500_000, 100_000),
            new(new ProvinceId(2), "B", Terrain.Land, 1_500_000, 1_500_000, 100_000),
            new(new ProvinceId(3), "C", Terrain.Land, 500_000, 500_000, 100_000),
            new(new ProvinceId(4), "D", Terrain.Land, 500_000, 1_500_000, 100_000),
        };

        var graph1 = BuildGrid4Graph(addOrder: new[]
        {
            (1u, 2u), (1u, 3u), (2u, 4u), (3u, 4u),
        });
        var graph2 = BuildGrid4Graph(addOrder: new[]
        {
            (4u, 3u), (3u, 1u), (4u, 2u), (2u, 1u),
        });

        var state1 = WorldLoaders.LoadIntoState(new WorldLoadResult(seeds, graph1));
        var state2 = WorldLoaders.LoadIntoState(new WorldLoadResult(seeds, graph2));

        Assert.Equal(state1.Adjacency.EdgeCount, state2.Adjacency.EdgeCount);
        for (uint i = 1; i <= 4; i++)
        {
            var pid = new ProvinceId(i);
            Assert.Equal(
                state1.Adjacency.NeighborsOf(pid).ToArray(),
                state2.Adjacency.NeighborsOf(pid).ToArray());
        }
    }

    [Fact]
    public void LoadIntoState_NullLoader_Throws()
    {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(
            () => WorldLoaders.LoadIntoState((IWorldLoader)null!, stream));
    }

    [Fact]
    public void LoadIntoState_NullResult_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => WorldLoaders.LoadIntoState((WorldLoadResult)null!));
    }

    private static AdjacencyGraph BuildGrid4Graph((uint A, uint B)[] addOrder)
    {
        var builder = new AdjacencyGraph.Builder();
        for (uint i = 1; i <= 4; i++)
        {
            builder.AddProvince(new ProvinceId(i));
        }

        foreach (var (a, b) in addOrder)
        {
            builder.AddUndirectedEdge(new ProvinceId(a), new ProvinceId(b));
        }

        return builder.Build();
    }
}
