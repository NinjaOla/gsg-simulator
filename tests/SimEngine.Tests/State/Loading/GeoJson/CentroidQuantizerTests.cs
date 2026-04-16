using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Tests.State.Loading.GeoJson;

public sealed class CentroidQuantizerTests
{
    [Fact]
    public void Quantize_UnitSquareAroundOrigin_ProducesZeroCentroid()
    {
        var polygon = ReadPolygon(
            "{\"type\":\"Polygon\",\"coordinates\":[[[-1,-1],[1,-1],[1,1],[-1,1],[-1,-1]]]}");
        var (latE6, lonE6) = CentroidQuantizer.Quantize(polygon);
        Assert.Equal(0, latE6);
        Assert.Equal(0, lonE6);
    }

    [Fact]
    public void Quantize_UnitSquareOffset_ProducesExpectedCentroidInMicrodegrees()
    {
        // Square covering lon [0, 1], lat [1, 2]; centroid at (lon=0.5, lat=1.5).
        var polygon = ReadPolygon(
            "{\"type\":\"Polygon\",\"coordinates\":[[[0,1],[1,1],[1,2],[0,2],[0,1]]]}");
        var (latE6, lonE6) = CentroidQuantizer.Quantize(polygon);
        Assert.Equal(1_500_000, latE6);
        Assert.Equal(500_000, lonE6);
    }

    [Theory]
    [InlineData(170.0, 170_000_000)]
    [InlineData(-170.0, -170_000_000)]
    [InlineData(180.0, -180_000_000)]   // wraps to -180
    [InlineData(-180.0, -180_000_000)]
    [InlineData(190.0, -170_000_000)]   // out-of-range gets normalized
    [InlineData(-190.0, 170_000_000)]
    [InlineData(540.0, -180_000_000)]
    public void NormalizeLongitude_ProducesValueInExpectedRange(double inputDegrees, int expectedLonE6)
    {
        var normalized = CentroidQuantizer.NormalizeLongitude(inputDegrees);
        var lonE6 = (int)Math.Round(normalized * 1_000_000.0, MidpointRounding.ToEven);
        Assert.Equal(expectedLonE6, lonE6);
    }

    [Fact]
    public void Quantize_MultiPolygonFeature_ProducesAreaWeightedCentroid()
    {
        // Two equal squares, one at (0..1, 0..1) and one at (10..11, 0..1).
        // Area-weighted centroid: lat = 0.5, lon = (0.5 + 10.5)/2 = 5.5.
        var multi = ReadGeometry(
            "{\"type\":\"MultiPolygon\",\"coordinates\":[" +
            "[[[0,0],[1,0],[1,1],[0,1],[0,0]]]," +
            "[[[10,0],[11,0],[11,1],[10,1],[10,0]]]]}");
        var (latE6, lonE6) = CentroidQuantizer.Quantize(multi);
        Assert.Equal(500_000, latE6);
        Assert.Equal(5_500_000, lonE6);
    }

    [Fact]
    public void Quantize_EmptyGeometry_Throws()
    {
        var empty = NetTopologySuite.NtsGeometryServices.Instance
            .CreateGeometryFactory().CreatePolygon();
        Assert.Throws<InvalidDataException>(() => CentroidQuantizer.Quantize(empty));
    }

    private static Polygon ReadPolygon(string json) => (Polygon)ReadGeometry(json);

    private static Geometry ReadGeometry(string json)
        => new GeoJsonReader().Read<Geometry>(json)
           ?? throw new InvalidOperationException("Failed to parse geometry.");
}
