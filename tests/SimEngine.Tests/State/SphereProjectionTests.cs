using SimEngine.State.Geo;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class SphereProjectionTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Equator_PrimeMeridian_IsOnPositiveX()
    {
        var (x, y, z) = SphereProjection.LatLonToUnitSphere(0, 0);
        Assert.Equal(1.0, x, Tolerance);
        Assert.Equal(0.0, y, Tolerance);
        Assert.Equal(0.0, z, Tolerance);
    }

    [Fact]
    public void NorthPole_IsOnPositiveZ()
    {
        var (x, y, z) = SphereProjection.LatLonToUnitSphere(90, 0);
        Assert.Equal(0.0, x, Tolerance);
        Assert.Equal(0.0, y, Tolerance);
        Assert.Equal(1.0, z, Tolerance);
    }

    [Fact]
    public void SouthPole_IsOnNegativeZ()
    {
        var (x, y, z) = SphereProjection.LatLonToUnitSphere(-90, 0);
        Assert.Equal(0.0, x, Tolerance);
        Assert.Equal(0.0, y, Tolerance);
        Assert.Equal(-1.0, z, Tolerance);
    }

    [Fact]
    public void Equator_NinetyEast_IsOnPositiveY()
    {
        var (x, y, z) = SphereProjection.LatLonToUnitSphere(0, 90);
        Assert.Equal(0.0, x, Tolerance);
        Assert.Equal(1.0, y, Tolerance);
        Assert.Equal(0.0, z, Tolerance);
    }

    [Fact]
    public void FromMicrodegrees_MatchesDoublePrecisionConversion()
    {
        var (x, y, z) = SphereProjection.FromMicrodegrees(45_000_000, 90_000_000);
        var (xd, yd, zd) = SphereProjection.LatLonToUnitSphere(45.0, 90.0);
        Assert.Equal(xd, x, Tolerance);
        Assert.Equal(yd, y, Tolerance);
        Assert.Equal(zd, z, Tolerance);
    }

    [Fact]
    public void AnyPoint_LiesOnUnitSphere()
    {
        var (x, y, z) = SphereProjection.LatLonToUnitSphere(37.7749, -122.4194);
        var magnitude = Math.Sqrt((x * x) + (y * y) + (z * z));
        Assert.Equal(1.0, magnitude, Tolerance);
    }
}
