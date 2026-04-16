using NetTopologySuite.Geometries;

namespace SimEngine.State.Loading.GeoJson;

/// <summary>
/// Computes a province centroid in microdegrees from an NTS geometry.
/// Uses NTS <see cref="Geometry.Centroid"/> (area-weighted on MultiPolygon),
/// normalizes longitude into <c>[-180, 180]</c> for antimeridian-crossing
/// inputs, and quantizes with banker's rounding so results are stable across
/// platforms and runtimes.
/// </summary>
internal static class CentroidQuantizer
{
    private const double MicrodegreeScale = 1_000_000.0;

    public static (int LatE6, int LonE6) Quantize(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var centroid = geometry.Centroid;
        if (centroid.IsEmpty)
        {
            throw new InvalidDataException(
                "Cannot compute centroid for empty geometry.");
        }

        var lat = centroid.Y;
        var lon = NormalizeLongitude(centroid.X);

        var latE6 = (int)Math.Round(lat * MicrodegreeScale, MidpointRounding.ToEven);
        var lonE6 = (int)Math.Round(lon * MicrodegreeScale, MidpointRounding.ToEven);

        return (latE6, lonE6);
    }

    /// <summary>
    /// Maps any longitude in degrees into <c>[-180, 180]</c>. GeoJSON files
    /// occasionally store coordinates outside that range when they cross the
    /// antimeridian; we normalize before quantization.
    /// </summary>
    internal static double NormalizeLongitude(double lonDegrees)
    {
        // ((lon + 540) mod 360) - 180 maps any real number into [-180, 180).
        // Using IEEERemainder keeps the result symmetric around zero.
        var shifted = lonDegrees + 540.0;
        var wrapped = shifted - (Math.Floor(shifted / 360.0) * 360.0);
        return wrapped - 180.0;
    }
}
