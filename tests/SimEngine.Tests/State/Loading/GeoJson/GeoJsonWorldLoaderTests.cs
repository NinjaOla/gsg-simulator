using System.Text;
using SimEngine.Ids;
using SimEngine.State.Components;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Tests.State.Loading.GeoJson;

public sealed class GeoJsonWorldLoaderTests
{
    private static string Grid4Path =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "grid4.geojson");

    [Fact]
    public void Load_Grid4_ProducesFourSeedsInFileOrder()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var result = loader.Load(stream);

        Assert.Equal(4, result.Provinces.Count);
        Assert.Equal("A", result.Provinces[0].Name);
        Assert.Equal("B", result.Provinces[1].Name);
        Assert.Equal("C", result.Provinces[2].Name);
        Assert.Equal("D", result.Provinces[3].Name);
        Assert.Equal(new ProvinceId(1), result.Provinces[0].ProvinceId);
        Assert.Equal(new ProvinceId(2), result.Provinces[1].ProvinceId);
        Assert.Equal(new ProvinceId(3), result.Provinces[2].ProvinceId);
        Assert.Equal(new ProvinceId(4), result.Provinces[3].ProvinceId);
    }

    [Fact]
    public void Load_Grid4_ReadsInitialPopulationFromGeoJsonProperties()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var result = loader.Load(stream);

        Assert.Equal(100_000, result.Provinces[0].InitialPopulation);
        Assert.Equal(200_000, result.Provinces[1].InitialPopulation);
        Assert.Equal(300_000, result.Provinces[2].InitialPopulation);
        Assert.Equal(400_000, result.Provinces[3].InitialPopulation);
    }

    [Fact]
    public void Load_Grid4_AllSeedsAreLandTerrain()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var result = loader.Load(stream);

        foreach (var seed in result.Provinces)
        {
            Assert.Equal(Terrain.Land, seed.Terrain);
        }
    }

    [Fact]
    public void Load_Grid4_CentroidsMatchHandComputedMicrodegrees()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var result = loader.Load(stream);

        // A: top-left  square lon[0,1] lat[1,2] → centroid (lat=1.5, lon=0.5)
        Assert.Equal(1_500_000, result.Provinces[0].CentroidLatE6);
        Assert.Equal(500_000, result.Provinces[0].CentroidLonE6);
        // B: top-right square lon[1,2] lat[1,2] → centroid (lat=1.5, lon=1.5)
        Assert.Equal(1_500_000, result.Provinces[1].CentroidLatE6);
        Assert.Equal(1_500_000, result.Provinces[1].CentroidLonE6);
        // C: bot-left  square lon[0,1] lat[0,1] → centroid (lat=0.5, lon=0.5)
        Assert.Equal(500_000, result.Provinces[2].CentroidLatE6);
        Assert.Equal(500_000, result.Provinces[2].CentroidLonE6);
        // D: bot-right square lon[1,2] lat[0,1] → centroid (lat=0.5, lon=1.5)
        Assert.Equal(500_000, result.Provinces[3].CentroidLatE6);
        Assert.Equal(1_500_000, result.Provinces[3].CentroidLonE6);
    }

    [Fact]
    public void Load_Grid4_ProducesFourEdgeAdjacencyGraph()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var result = loader.Load(stream);

        Assert.Equal(4, result.Adjacency.EdgeCount);
        // A↔B, A↔C
        Assert.Equal(new[] { new ProvinceId(2), new ProvinceId(3) },
            result.Adjacency.NeighborsOf(new ProvinceId(1)).ToArray());
        // B↔A, B↔D
        Assert.Equal(new[] { new ProvinceId(1), new ProvinceId(4) },
            result.Adjacency.NeighborsOf(new ProvinceId(2)).ToArray());
        // C↔A, C↔D
        Assert.Equal(new[] { new ProvinceId(1), new ProvinceId(4) },
            result.Adjacency.NeighborsOf(new ProvinceId(3)).ToArray());
        // D↔B, D↔C
        Assert.Equal(new[] { new ProvinceId(2), new ProvinceId(3) },
            result.Adjacency.NeighborsOf(new ProvinceId(4)).ToArray());
    }

    [Fact]
    public void Load_CalledTwice_ProducesIdenticalSeeds()
    {
        var loader = new GeoJsonWorldLoader();
        var bytes = File.ReadAllBytes(Grid4Path);

        using var stream1 = new MemoryStream(bytes);
        using var stream2 = new MemoryStream(bytes);
        var first = loader.Load(stream1);
        var second = loader.Load(stream2);

        Assert.Equal(first.Provinces, second.Provinces);
    }

    [Fact]
    public void Load_FeatureWithoutGeometry_Throws()
    {
        const string Json =
            "{\"type\":\"FeatureCollection\",\"features\":[" +
            "{\"type\":\"Feature\",\"properties\":{\"name\":\"X\"},\"geometry\":null}]}";
        var loader = new GeoJsonWorldLoader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
        Assert.Throws<InvalidDataException>(() => loader.Load(stream));
    }

    [Fact]
    public void Load_MissingProvinceId_Throws()
    {
        const string Json =
            "{\"type\":\"FeatureCollection\",\"features\":[" +
            "{\"type\":\"Feature\",\"properties\":{\"name\":\"X\",\"population\":1000}," +
            "\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,1],[0,0]]]}}]}";

        var loader = new GeoJsonWorldLoader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));

        Assert.Throws<InvalidDataException>(() => loader.Load(stream));
    }

    [Fact]
    public void Load_DuplicateProvinceId_Throws()
    {
        const string Json =
            "{\"type\":\"FeatureCollection\",\"features\":[" +
            "{\"type\":\"Feature\",\"properties\":{\"name\":\"A\",\"province_id\":1,\"population\":1000}," +
            "\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,1],[0,0]]]}}," +
            "{\"type\":\"Feature\",\"properties\":{\"name\":\"B\",\"province_id\":1,\"population\":1000}," +
            "\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[1,0],[2,0],[2,1],[1,1],[1,0]]]}}]}";

        var loader = new GeoJsonWorldLoader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));

        Assert.Throws<InvalidDataException>(() => loader.Load(stream));
    }

    [Fact]
    public void Load_NullStream_Throws()
    {
        var loader = new GeoJsonWorldLoader();
        Assert.Throws<ArgumentNullException>(() => loader.Load(null!));
    }
}


