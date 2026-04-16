using System.Diagnostics;
using NetTopologySuite.Geometries;
using SimEngine.Ids;

namespace SimEngine.State.Loading.GeoJson;

/// <summary>
/// Derives an <see cref="AdjacencyGraph"/> from a set of polygon features by
/// hashing snapped polygon edges and emitting an undirected edge between any
/// two features that share a segment.
///
/// Natural Earth's 10m admin_1 dataset is not watertight: neighboring province
/// polygons frequently differ by a few units in the 7th decimal place along a
/// shared border, so NTS <c>Touches</c> systematically misses those borders.
/// Snapping coordinates to a 1e-7-degree grid (~1.1 cm at the equator) before
/// comparing edges papers over that jitter without falsely connecting
/// provinces that merely share a coastline or a river.
/// </summary>
internal static class SharedEdgeAdjacencyBuilder
{
    /// <summary>1 unit in the snap grid = 1e-7 degrees of arc.</summary>
    internal const double SnapScale = 10_000_000.0;

    public static AdjacencyGraph Build(
        IReadOnlyList<(ProvinceId Id, Geometry Geometry)> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var builder = new AdjacencyGraph.Builder();
        if (features.Count == 0)
        {
            return builder.Build();
        }

        // Step 1: extract every ring of every polygon as a list of snapped
        // coordinate pairs, partitioned by feature index.
        var ringsByFeature = new List<List<long[]>>(features.Count);
        foreach (var (id, geometry) in features)
        {
            builder.AddProvince(id);
            ringsByFeature.Add(ExtractSnappedRings(geometry));
        }

        // Step 2: collect every distinct snapped vertex per ring so we can
        // split T-junctions in step 3.
        var allVertices = CollectVertexSet(ringsByFeature);

        // Step 3: walk every ring's segments, splitting any segment that has
        // an interior vertex on it (T-junction handling), and bucket edges by
        // their canonical key.
        var edgeOwners = new Dictionary<EdgeKey, EdgeOwners>(capacity: features.Count * 8);
        for (var featureIndex = 0; featureIndex < features.Count; featureIndex++)
        {
            var provinceId = features[featureIndex].Id;
            var rings = ringsByFeature[featureIndex];

            foreach (var ring in rings)
            {
                EmitRingSegments(ring, allVertices, provinceId, edgeOwners);
            }
        }

        // Step 4: any edge bucket with two distinct owners produces an
        // undirected adjacency edge. Three+ owners (a sliver / data error)
        // pick the first two deterministically — we record an edge between
        // owner A and owner B and swallow C, with a Debug.WriteLine warning.
        foreach (var (key, owners) in edgeOwners)
        {
            if (owners.Second == ProvinceId.None)
            {
                continue;
            }

            if (owners.OwnerCount > 2)
            {
                Debug.WriteLine(
                    $"SharedEdgeAdjacencyBuilder: edge {key} touched by "
                    + $"{owners.OwnerCount} polygons; using first two ({owners.First}, {owners.Second}).");
            }

            if (owners.First != owners.Second)
            {
                builder.AddUndirectedEdge(owners.First, owners.Second);
            }
        }

        return builder.Build();
    }

    private static List<long[]> ExtractSnappedRings(Geometry geometry)
    {
        var rings = new List<long[]>();
        AppendRings(geometry, rings);
        return rings;
    }

    private static void AppendRings(Geometry geometry, List<long[]> rings)
    {
        switch (geometry)
        {
            case Polygon polygon:
                AppendPolygon(polygon, rings);
                break;
            case MultiPolygon multi:
                for (var i = 0; i < multi.NumGeometries; i++)
                {
                    AppendPolygon((Polygon)multi.GetGeometryN(i), rings);
                }
                break;
            case GeometryCollection collection:
                for (var i = 0; i < collection.NumGeometries; i++)
                {
                    AppendRings(collection.GetGeometryN(i), rings);
                }
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported geometry type for adjacency: {geometry.GeometryType}.");
        }
    }

    private static void AppendPolygon(Polygon polygon, List<long[]> rings)
    {
        rings.Add(SnapRing(polygon.ExteriorRing));
        foreach (var hole in polygon.InteriorRings)
        {
            rings.Add(SnapRing(hole));
        }
    }

    private static long[] SnapRing(LineString ring)
    {
        var coords = ring.Coordinates;
        var snapped = new long[coords.Length * 2];
        for (var i = 0; i < coords.Length; i++)
        {
            var lon = CentroidQuantizer.NormalizeLongitude(coords[i].X);
            snapped[(i * 2) + 0] = (long)Math.Round(lon * SnapScale, MidpointRounding.ToEven);
            snapped[(i * 2) + 1] = (long)Math.Round(coords[i].Y * SnapScale, MidpointRounding.ToEven);
        }

        return snapped;
    }

    private static HashSet<(long Lon, long Lat)> CollectVertexSet(List<List<long[]>> ringsByFeature)
    {
        var set = new HashSet<(long, long)>();
        foreach (var rings in ringsByFeature)
        {
            foreach (var ring in rings)
            {
                for (var i = 0; i < ring.Length; i += 2)
                {
                    set.Add((ring[i], ring[i + 1]));
                }
            }
        }

        return set;
    }

    private static void EmitRingSegments(
        long[] ring,
        HashSet<(long Lon, long Lat)> allVertices,
        ProvinceId provinceId,
        Dictionary<EdgeKey, EdgeOwners> edgeOwners)
    {
        // ring stores coordinates as [lon0, lat0, lon1, lat1, ...]. The last
        // coordinate equals the first in a closed GeoJSON ring, so we walk
        // pairs (i, i+1) for i in 0..n-2.
        var pairCount = (ring.Length / 2) - 1;
        for (var i = 0; i < pairCount; i++)
        {
            var aLon = ring[(i * 2) + 0];
            var aLat = ring[(i * 2) + 1];
            var bLon = ring[((i + 1) * 2) + 0];
            var bLat = ring[((i + 1) * 2) + 1];

            if (aLon == bLon && aLat == bLat)
            {
                // Degenerate zero-length segment; skip.
                continue;
            }

            EmitSegmentWithSplits(aLon, aLat, bLon, bLat, allVertices, provinceId, edgeOwners);
        }
    }

    private static void EmitSegmentWithSplits(
        long aLon,
        long aLat,
        long bLon,
        long bLat,
        HashSet<(long Lon, long Lat)> allVertices,
        ProvinceId provinceId,
        Dictionary<EdgeKey, EdgeOwners> edgeOwners)
    {
        // Find every interior vertex (from any ring of any feature) that lies
        // exactly on the open segment (a, b). They split this segment into
        // sub-segments, which is what makes T-junction adjacency work even
        // when one polygon has a vertex where another doesn't.
        //
        // Working in the integer grid: a point (px, py) lies on segment ab iff
        //   1) the cross product (bx-ax)*(py-ay) - (by-ay)*(px-ax) == 0
        //   2) min(ax,bx) <= px <= max(ax,bx)  AND  min(ay,by) <= py <= max(ay,by)
        //   3) (px, py) != a and (px, py) != b
        // We intentionally use long arithmetic — these are scaled microdegrees,
        // so cross products fit comfortably in Int64 (max ~3.6e9 * 3.6e9 ~ 1.3e19,
        // which exceeds Int64 — so we widen to Int128 for the cross product).
        var dxLong = bLon - aLon;
        var dyLong = bLat - aLat;
        Int128 dx = dxLong;
        Int128 dy = dyLong;

        var splitters = new List<(long Lon, long Lat)>();
        foreach (var v in allVertices)
        {
            if ((v.Lon == aLon && v.Lat == aLat) || (v.Lon == bLon && v.Lat == bLat))
            {
                continue;
            }

            // Bounding-box reject first — fast and avoids wide multiplies.
            if (v.Lon < Math.Min(aLon, bLon) || v.Lon > Math.Max(aLon, bLon))
            {
                continue;
            }

            if (v.Lat < Math.Min(aLat, bLat) || v.Lat > Math.Max(aLat, bLat))
            {
                continue;
            }

            Int128 lhs = dx * (v.Lat - aLat);
            Int128 rhs = dy * (v.Lon - aLon);
            if (lhs == rhs)
            {
                splitters.Add(v);
            }
        }

        if (splitters.Count == 0)
        {
            RecordEdge(aLon, aLat, bLon, bLat, provinceId, edgeOwners);
            return;
        }

        // Order splitters along the segment by signed projection onto (dx, dy).
        // Since splitters are colinear, sorting by either dominant axis works;
        // we sort by the larger of |dx|/|dy| to avoid ties when one is zero.
        if (Math.Abs(dxLong) >= Math.Abs(dyLong))
        {
            splitters.Sort((p, q) => dxLong >= 0 ? p.Lon.CompareTo(q.Lon) : q.Lon.CompareTo(p.Lon));
        }
        else
        {
            splitters.Sort((p, q) => dyLong >= 0 ? p.Lat.CompareTo(q.Lat) : q.Lat.CompareTo(p.Lat));
        }

        var prevLon = aLon;
        var prevLat = aLat;
        foreach (var s in splitters)
        {
            RecordEdge(prevLon, prevLat, s.Lon, s.Lat, provinceId, edgeOwners);
            prevLon = s.Lon;
            prevLat = s.Lat;
        }

        RecordEdge(prevLon, prevLat, bLon, bLat, provinceId, edgeOwners);
    }

    private static void RecordEdge(
        long aLon,
        long aLat,
        long bLon,
        long bLat,
        ProvinceId provinceId,
        Dictionary<EdgeKey, EdgeOwners> edgeOwners)
    {
        if (aLon == bLon && aLat == bLat)
        {
            return;
        }

        var key = EdgeKey.Canonical(aLon, aLat, bLon, bLat);
        if (edgeOwners.TryGetValue(key, out var owners))
        {
            edgeOwners[key] = owners.With(provinceId);
        }
        else
        {
            edgeOwners[key] = EdgeOwners.Initial(provinceId);
        }
    }

    private readonly record struct EdgeOwners(
        ProvinceId First,
        ProvinceId Second,
        int OwnerCount)
    {
        public static EdgeOwners Initial(ProvinceId id) => new(id, ProvinceId.None, 1);

        public EdgeOwners With(ProvinceId id)
        {
            // Same province re-touching a segment (polygon ring quirks) does
            // not increment the owner count — it's the same polygon.
            if (id == First || id == Second)
            {
                return this;
            }

            if (Second == ProvinceId.None)
            {
                return new EdgeOwners(First, id, 2);
            }

            return new EdgeOwners(First, Second, OwnerCount + 1);
        }
    }
}
