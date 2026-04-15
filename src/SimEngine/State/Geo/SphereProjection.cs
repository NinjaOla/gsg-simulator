namespace SimEngine.State.Geo;

/// <summary>
/// Lat/lon → unit-sphere conversion for renderer consumption. This helper
/// intentionally works in <see cref="double"/>: its outputs are never fed
/// back into game state and never participate in determinism-sensitive
/// calculations. Consumers should treat the results as presentational.
/// </summary>
public static class SphereProjection
{
    private const double DegreesToRadians = Math.PI / 180.0;
    private const double MicrodegreeScale = 1_000_000.0;

    /// <summary>
    /// Maps a latitude/longitude in degrees to the corresponding point on
    /// the unit sphere using the standard
    /// <c>(cos φ cos λ, cos φ sin λ, sin φ)</c> parameterization.
    /// </summary>
    public static (double X, double Y, double Z) LatLonToUnitSphere(double latDegrees, double lonDegrees)
    {
        var latRadians = latDegrees * DegreesToRadians;
        var lonRadians = lonDegrees * DegreesToRadians;

        var cosLat = Math.Cos(latRadians);
        var x = cosLat * Math.Cos(lonRadians);
        var y = cosLat * Math.Sin(lonRadians);
        var z = Math.Sin(latRadians);

        return (x, y, z);
    }

    /// <summary>
    /// Convenience overload that accepts microdegree-scaled integer
    /// coordinates as stored on <see cref="Components.ProvinceComponent"/>.
    /// </summary>
    public static (double X, double Y, double Z) FromMicrodegrees(int latE6, int lonE6)
    {
        return LatLonToUnitSphere(latE6 / MicrodegreeScale, lonE6 / MicrodegreeScale);
    }
}
