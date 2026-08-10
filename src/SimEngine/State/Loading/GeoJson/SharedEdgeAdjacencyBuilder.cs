using System.Diagnostics;
using System.Threading.Tasks;
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
        // coordinate pairs, partitioned by feature index. Ring extraction is
        // read-only per feature, so we do it in parallel into a pre-sized
        // array (each slot written by exactly one task -> no synchronization).
        var ringsByFeature = new List<long[]>[features.Count];
        Parallel.For(0, features.Count, featureIndex =>
        {
            ringsByFeature[featureIndex] = ExtractSnappedRings(features[featureIndex].Geometry);
        });

        for (var featureIndex = 0; featureIndex < features.Count; featureIndex++)
        {
            builder.AddProvince(features[featureIndex].Id);
        }

        // Step 2: collect every distinct snapped vertex and index it spatially
        // so T-junction splitting (step 3) can query only the vertices near a
        // segment instead of scanning the entire global vertex set.
        var vertexIndex = VertexSpatialIndex.Build(ringsByFeature);

        // Step 3: walk every ring's segments, splitting any segment that has
        // an interior vertex on it (T-junction handling), and bucket edges by
        // their canonical key. Each feature emits into its own edge map (no
        // shared mutable state), then we merge the per-feature maps back in
        // ascending feature-index order so the result is identical to the
        // sequential build regardless of thread scheduling.
        var edgeOwnersByFeature = new Dictionary<EdgeKey, EdgeOwners>[features.Count];
        Parallel.For(0, features.Count, featureIndex =>
        {
            var provinceId = features[featureIndex].Id;
            var rings = ringsByFeature[featureIndex];
            var local = new Dictionary<EdgeKey, EdgeOwners>();
            foreach (var ring in rings)
            {
                EmitRingSegments(ring, vertexIndex, provinceId, local);
            }

            edgeOwnersByFeature[featureIndex] = local;
        });

        var edgeOwners = new Dictionary<EdgeKey, EdgeOwners>(capacity: features.Count * 8);
        for (var featureIndex = 0; featureIndex < features.Count; featureIndex++)
        {
            foreach (var (key, local) in edgeOwnersByFeature[featureIndex])
            {
                if (edgeOwners.TryGetValue(key, out var owners))
                {
                    edgeOwners[key] = owners.Merge(local);
                }
                else
                {
                    edgeOwners[key] = local;
                }
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

    /// <summary>
    /// Uniform-grid spatial index over the global snapped vertex set. Built
    /// once, then queried read-only (thread-safe) from many segment emitters.
    /// A segment's colinear-vertex search only needs to inspect vertices whose
    /// grid cells overlap the segment's bounding box, turning the former
    /// O(segments * allVertices) scan into roughly O(segments * localVertices).
    /// </summary>
    private sealed class VertexSpatialIndex
    {
        // 1 grid cell = 1 degree (SnapScale units). Snapped coords are in
        // 1e-7-degree units, so this keeps per-cell vertex counts small while
        // bounding the number of cells a single (short) border segment spans.
        private const long CellSize = (long)SnapScale;

        private readonly Dictionary<(long CellX, long CellY), List<(long Lon, long Lat)>> _cells;

        private VertexSpatialIndex(Dictionary<(long, long), List<(long Lon, long Lat)>> cells)
        {
            _cells = cells;
        }

        public static VertexSpatialIndex Build(IReadOnlyList<List<long[]>> ringsByFeature)
        {
            // De-duplicate vertices first (a shared border vertex appears in
            // many rings) so each distinct vertex is bucketed exactly once.
            var vertices = new HashSet<(long Lon, long Lat)>();
            foreach (var rings in ringsByFeature)
            {
                foreach (var ring in rings)
                {
                    for (var i = 0; i < ring.Length; i += 2)
                    {
                        vertices.Add((ring[i], ring[i + 1]));
                    }
                }
            }

            var cells = new Dictionary<(long, long), List<(long Lon, long Lat)>>();
            foreach (var v in vertices)
            {
                var key = (CellOf(v.Lon), CellOf(v.Lat));
                if (!cells.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    cells[key] = bucket;
                }

                bucket.Add(v);
            }

            return new VertexSpatialIndex(cells);
        }

        /// <summary>
        /// Invokes <paramref name="onVertex"/> for every indexed vertex whose
        /// grid cell overlaps the bounding box of segment (a, b). May yield
        /// vertices outside the exact box; callers still apply the precise
        /// bbox and colinearity tests.
        /// </summary>
        public void ForEachInBounds(
            long aLon, long aLat, long bLon, long bLat,
            Action<(long Lon, long Lat)> onVertex)
        {
            var minCellX = CellOf(Math.Min(aLon, bLon));
            var maxCellX = CellOf(Math.Max(aLon, bLon));
            var minCellY = CellOf(Math.Min(aLat, bLat));
            var maxCellY = CellOf(Math.Max(aLat, bLat));

            for (var cx = minCellX; cx <= maxCellX; cx++)
            {
                for (var cy = minCellY; cy <= maxCellY; cy++)
                {
                    if (_cells.TryGetValue((cx, cy), out var bucket))
                    {
                        foreach (var v in bucket)
                        {
                            onVertex(v);
                        }
                    }
                }
            }
        }

        private static long CellOf(long coord)
        {
            // Floor division so negative coordinates bucket consistently.
            return coord >= 0 ? coord / CellSize : (coord - CellSize + 1) / CellSize;
        }
    }

    private static void EmitRingSegments(
        long[] ring,
        VertexSpatialIndex vertexIndex,
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

            EmitSegmentWithSplits(aLon, aLat, bLon, bLat, vertexIndex, provinceId, edgeOwners);
        }
    }

    private static void EmitSegmentWithSplits(
        long aLon,
        long aLat,
        long bLon,
        long bLat,
        VertexSpatialIndex vertexIndex,
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
        vertexIndex.ForEachInBounds(aLon, aLat, bLon, bLat, v =>
        {
            if ((v.Lon == aLon && v.Lat == aLat) || (v.Lon == bLon && v.Lat == bLat))
            {
                return;
            }

            // Bounding-box reject first — fast and avoids wide multiplies.
            if (v.Lon < Math.Min(aLon, bLon) || v.Lon > Math.Max(aLon, bLon))
            {
                return;
            }

            if (v.Lat < Math.Min(aLat, bLat) || v.Lat > Math.Max(aLat, bLat))
            {
                return;
            }

            Int128 lhs = dx * (v.Lat - aLat);
            Int128 rhs = dy * (v.Lon - aLon);
            if (lhs == rhs)
            {
                splitters.Add(v);
            }
        });

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

        /// <summary>
        /// Folds another bucket into this one. Per-feature local maps are built
        /// for a single province, so <paramref name="other"/> always carries
        /// exactly one owner; merging in ascending feature-index order yields
        /// the same First/Second/OwnerCount as the sequential build.
        /// </summary>
        public EdgeOwners Merge(EdgeOwners other) => With(other.First);

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
