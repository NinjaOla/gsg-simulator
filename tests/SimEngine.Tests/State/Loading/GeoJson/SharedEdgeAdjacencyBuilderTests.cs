using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SimEngine.Ids;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Tests.State.Loading.GeoJson;

public sealed class SharedEdgeAdjacencyBuilderTests
{
    [Fact]
    public void Build_TwoPolygonsSharingFullEdge_ProducesAdjacency()
    {
        var a = ReadPolygon("[[[0,0],[1,0],[1,1],[0,1],[0,0]]]");
        var b = ReadPolygon("[[[1,0],[2,0],[2,1],[1,1],[1,0]]]");
        var graph = SharedEdgeAdjacencyBuilder.Build(Pair(a, b));

        Assert.Equal(1, graph.EdgeCount);
        Assert.Contains(new ProvinceId(2), graph.NeighborsOf(new ProvinceId(1)));
    }

    [Fact]
    public void Build_TwoPolygonsSharingSingleVertex_ProducesNoAdjacency()
    {
        // Top-left and bottom-right squares meeting only at (1,1).
        var a = ReadPolygon("[[[0,1],[1,1],[1,2],[0,2],[0,1]]]");
        var b = ReadPolygon("[[[1,0],[2,0],[2,1],[1,1],[1,0]]]");
        var graph = SharedEdgeAdjacencyBuilder.Build(Pair(a, b));

        Assert.Equal(0, graph.EdgeCount);
    }

    [Fact]
    public void Build_TJunctionAcrossPolygons_ProducesAdjacency()
    {
        // Polygon A's right edge runs straight from (1,0) to (1,2).
        // Polygon B sits to the right and has an extra mid-vertex at (1,1)
        // — which would make NTS Touches succeed only if the edge happened
        // to coincide exactly. The split-at-shared-vertex pre-pass should
        // still detect the shared border.
        var a = ReadPolygon("[[[0,0],[1,0],[1,2],[0,2],[0,0]]]");
        var b = ReadPolygon("[[[1,0],[2,0],[2,2],[1,2],[1,1],[1,0]]]");
        var graph = SharedEdgeAdjacencyBuilder.Build(Pair(a, b));

        Assert.Equal(1, graph.EdgeCount);
        Assert.Contains(new ProvinceId(2), graph.NeighborsOf(new ProvinceId(1)));
    }

    [Fact]
    public void Build_NearButNotExactCoordinatesWithinTolerance_ProducesAdjacency()
    {
        // B's shared edge is offset by 1e-8 degrees — well inside the 1e-7
        // snap grid, so the snapped segments collide.
        var a = ReadPolygon("[[[0,0],[1,0],[1,1],[0,1],[0,0]]]");
        var b = ReadPolygon(
            "[[[1.00000001,0.00000001],[2,0],[2,1],[1.00000001,1.00000001],[1.00000001,0.00000001]]]");
        var graph = SharedEdgeAdjacencyBuilder.Build(Pair(a, b));

        Assert.Equal(1, graph.EdgeCount);
    }

    [Fact]
    public void Build_MultiPolygonFeature_AllSubPolygonsContributeAdjacency()
    {
        // Feature A is a MultiPolygon of two squares: a left square and a
        // right square. Feature B sits to the right of the right sub-polygon.
        // Adjacency: A↔B once.
        var aMulti = ReadGeometry(
            "{\"type\":\"MultiPolygon\",\"coordinates\":[" +
            "[[[0,0],[1,0],[1,1],[0,1],[0,0]]]," +
            "[[[5,0],[6,0],[6,1],[5,1],[5,0]]]]}");
        var b = ReadPolygon("[[[6,0],[7,0],[7,1],[6,1],[6,0]]]");
        var graph = SharedEdgeAdjacencyBuilder.Build(Pair(aMulti, b));

        Assert.Equal(1, graph.EdgeCount);
        Assert.Contains(new ProvinceId(2), graph.NeighborsOf(new ProvinceId(1)));
    }

    [Fact]
    public void Build_FourSquareGrid_ProducesFourEdges()
    {
        // Same layout as data/custom-test/grid4.geojson. Four 1x1 squares,
        // 4 shared edges, 0 corner-only contacts.
        var a = ReadPolygon("[[[0,1],[1,1],[1,2],[0,2],[0,1]]]");
        var b = ReadPolygon("[[[1,1],[2,1],[2,2],[1,2],[1,1]]]");
        var c = ReadPolygon("[[[0,0],[1,0],[1,1],[0,1],[0,0]]]");
        var d = ReadPolygon("[[[1,0],[2,0],[2,1],[1,1],[1,0]]]");
        var graph = SharedEdgeAdjacencyBuilder.Build(new (ProvinceId, Geometry)[]
        {
            (new ProvinceId(1), a),
            (new ProvinceId(2), b),
            (new ProvinceId(3), c),
            (new ProvinceId(4), d),
        });

        Assert.Equal(4, graph.EdgeCount);
        Assert.Equal(new[] { new ProvinceId(2), new ProvinceId(3) },
            graph.NeighborsOf(new ProvinceId(1)).ToArray());
        Assert.Equal(new[] { new ProvinceId(1), new ProvinceId(4) },
            graph.NeighborsOf(new ProvinceId(2)).ToArray());
        Assert.Equal(new[] { new ProvinceId(1), new ProvinceId(4) },
            graph.NeighborsOf(new ProvinceId(3)).ToArray());
        Assert.Equal(new[] { new ProvinceId(2), new ProvinceId(3) },
            graph.NeighborsOf(new ProvinceId(4)).ToArray());
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmptyGraph()
    {
        var graph = SharedEdgeAdjacencyBuilder.Build(Array.Empty<(ProvinceId, Geometry)>());
        Assert.Equal(0, graph.EdgeCount);
        Assert.Equal(0, graph.ProvinceCount);
    }

    private static (ProvinceId, Geometry)[] Pair(Geometry a, Geometry b) => new[]
    {
        (new ProvinceId(1), a),
        (new ProvinceId(2), b),
    };

    private static Polygon ReadPolygon(string coordinates)
        => (Polygon)ReadGeometry($"{{\"type\":\"Polygon\",\"coordinates\":{coordinates}}}");

    private static Geometry ReadGeometry(string json)
        => new GeoJsonReader().Read<Geometry>(json)
           ?? throw new InvalidOperationException("Failed to parse geometry.");
}


