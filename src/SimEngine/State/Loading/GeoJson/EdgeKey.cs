namespace SimEngine.State.Loading.GeoJson;

/// <summary>
/// Canonical key for an undirected line segment between two coordinates that
/// have already been snapped to the integer microdegree-x10 grid (E7 scale).
/// The two endpoints are stored in lexicographic order so the same segment
/// hashes identically regardless of which polygon traverses it first.
/// </summary>
internal readonly record struct EdgeKey(
    long ALon,
    long ALat,
    long BLon,
    long BLat)
{
    public static EdgeKey Canonical(long lon1, long lat1, long lon2, long lat2)
    {
        // Lex order on (lon, lat). Stable, total, allocation-free.
        if (lon1 < lon2 || (lon1 == lon2 && lat1 <= lat2))
        {
            return new EdgeKey(lon1, lat1, lon2, lat2);
        }

        return new EdgeKey(lon2, lat2, lon1, lat1);
    }
}
