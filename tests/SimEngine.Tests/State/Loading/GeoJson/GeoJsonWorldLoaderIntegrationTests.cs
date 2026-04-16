using SimEngine.Ids;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Tests.State.Loading.GeoJson;

/// <summary>
/// End-to-end loader tests against the curated 16-Bundesland subset of
/// Natural Earth admin_1. Asserts shape-of-result properties (counts,
/// adjacency degree distribution, determinism) rather than every individual
/// neighbor — the latter would couple us to upstream coordinate revisions
/// that don't change the topology in any meaningful way.
/// </summary>
public sealed class GeoJsonWorldLoaderIntegrationTests
{
    private static string GermanyPath =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "germany_admin1.geojson");

    [Fact]
    public void Load_GermanySubset_Produces16Provinces()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(GermanyPath);
        var result = loader.Load(stream);

        Assert.Equal(16, result.Provinces.Count);
    }

    [Fact]
    public void Load_GermanySubset_AllSeedsHaveNonEmptyNames()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(GermanyPath);
        var result = loader.Load(stream);

        foreach (var seed in result.Provinces)
        {
            Assert.False(string.IsNullOrWhiteSpace(seed.Name));
        }
    }

    [Fact]
    public void Load_GermanySubset_AdjacencyDegreeDistributionIsSane()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(GermanyPath);
        var result = loader.Load(stream);

        // Germany's 16 Bundesländer are well-connected internally with a
        // handful of single-neighbor coastal exceptions. The full graph has
        // ~30 internal borders, so the average degree should sit around 3-5
        // and no province should be wildly over-connected.
        var totalDegree = 0;
        var maxDegree = 0;
        for (uint i = 1; i <= 16; i++)
        {
            var neighbors = result.Adjacency.NeighborsOf(new ProvinceId(i));
            totalDegree += neighbors.Length;
            if (neighbors.Length > maxDegree)
            {
                maxDegree = neighbors.Length;
            }
        }

        var meanDegree = totalDegree / 16.0;
        Assert.InRange(meanDegree, 2.0, 7.0);
        Assert.InRange(maxDegree, 2, 12);
        Assert.True(result.Adjacency.EdgeCount >= 10,
            $"Expected at least 10 internal borders, got {result.Adjacency.EdgeCount}.");
    }

    [Fact]
    public void Load_GermanySubsetTwice_ProducesIdenticalSeedsAndEdges()
    {
        var loader = new GeoJsonWorldLoader();
        var bytes = File.ReadAllBytes(GermanyPath);

        using var stream1 = new MemoryStream(bytes);
        using var stream2 = new MemoryStream(bytes);
        var first = loader.Load(stream1);
        var second = loader.Load(stream2);

        Assert.Equal(first.Provinces, second.Provinces);
        Assert.Equal(first.Adjacency.EdgeCount, second.Adjacency.EdgeCount);

        for (uint i = 1; i <= (uint)first.Provinces.Count; i++)
        {
            var pid = new ProvinceId(i);
            Assert.Equal(
                first.Adjacency.NeighborsOf(pid).ToArray(),
                second.Adjacency.NeighborsOf(pid).ToArray());
        }
    }

    [Fact]
    public void LoadIntoState_GermanySubset_ProducesFullyPopulatedSimulationState()
    {
        var loader = new GeoJsonWorldLoader();
        var state = WorldLoaders.LoadIntoState(loader, GermanyPath);

        Assert.Equal(16, state.Entities.Count);
        Assert.True(state.Adjacency.EdgeCount >= 10);
    }
}
