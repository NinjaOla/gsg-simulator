using System.Text.Json;
using Stride.Core.Mathematics;

namespace SimEngine.Game.Ui.Stride;

internal sealed class GeoJsonProvinceIndex
{
    private readonly List<ProvinceFeature> features;
    public static GeoJsonProvinceIndex Empty { get; } = new([]);
    public int FeatureCount => features.Count;

    private GeoJsonProvinceIndex(List<ProvinceFeature> features)
    {
        this.features = features;
    }

    public static GeoJsonProvinceIndex Load(string geoJsonPath)
    {
        using var stream = File.OpenRead(geoJsonPath);
        using var doc = JsonDocument.Parse(stream);

        if (!doc.RootElement.TryGetProperty("features", out var featureArray) || featureArray.ValueKind != JsonValueKind.Array)
        {
            return new GeoJsonProvinceIndex([]);
        }

        var features = new List<ProvinceFeature>();

        foreach (var feature in featureArray.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                var polygons = ParseGeometry(geometry);
                if (polygons.Count == 0)
                {
                    continue;
                }

                var (id, name) = ParseIdentity(feature);
                var centroid = ComputeCentroid(polygons);

                features.Add(new ProvinceFeature(id, name, centroid, polygons));
            }
            catch
            {
                // Ignore malformed feature geometry and continue indexing.
            }
        }

        return new GeoJsonProvinceIndex(features);
    }

    public (string Id, string Name, bool IsInside, float BorderDistanceDegrees) Lookup(float lon, float lat)
    {
        ProvinceFeature? containing = null;
        for (var i = 0; i < features.Count; i++)
        {
            var candidate = features[i];
            if (candidate.Contains(lon, lat))
            {
                containing = candidate;
                break;
            }
        }

        if (containing is not null)
        {
            var borderDistance = containing.BorderDistanceDegrees(lon, lat);
            return (containing.Id, containing.Name, true, borderDistance);
        }

        ProvinceFeature? nearest = null;
        var bestDistSq = float.MaxValue;

        for (var i = 0; i < features.Count; i++)
        {
            var candidate = features[i];
            var dx = DeltaLongitudeDegrees(lon, candidate.CentroidLonLat.X);
            var dy = lat - candidate.CentroidLonLat.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                nearest = candidate;
            }
        }

        if (nearest is null)
        {
            return ("unknown", "unknown", false, float.PositiveInfinity);
        }

        return (nearest.Id, nearest.Name, false, nearest.BorderDistanceDegrees(lon, lat));
    }

    private static (string id, string name) ParseIdentity(JsonElement feature)
    {
        if (!feature.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
        {
            return ("unknown", "unknown");
        }

        var id = GetString(props, ["id", "province_id", "adm1_code", "adm1_id", "iso_3166_2"]) ?? "unknown";
        var name = GetString(props, ["name", "name_en", "adm1_name", "NAME", "NAME_1"]) ?? id;
        return (id, name);
    }

    private static string? GetString(JsonElement props, IReadOnlyList<string> keys)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (!props.TryGetProperty(key, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }

        return null;
    }

    private static List<GeoPolygon> ParseGeometry(JsonElement geometry)
    {
        var result = new List<GeoPolygon>();

        if (!geometry.TryGetProperty("type", out var typeEl)
            || !geometry.TryGetProperty("coordinates", out var coordinates))
        {
            return result;
        }

        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var type = typeEl.GetString();

        switch (type)
        {
            case "Polygon":
                AddPolygon(coordinates, result);
                break;
            case "MultiPolygon":
                foreach (var polygon in coordinates.EnumerateArray())
                {
                    if (polygon.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    AddPolygon(polygon, result);
                }
                break;
        }

        return result;
    }

    private static void AddPolygon(JsonElement polygonElement, List<GeoPolygon> result)
    {
        if (polygonElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var rings = new List<Vector2[]>();
        foreach (var ringElement in polygonElement.EnumerateArray())
        {
            var ring = ParseRing(ringElement);
            if (ring.Length >= 3)
            {
                rings.Add(ring);
            }
        }

        if (rings.Count == 0)
        {
            return;
        }

        var exterior = rings[0];
        var holes = rings.Count > 1 ? rings.GetRange(1, rings.Count - 1) : [];
        result.Add(new GeoPolygon(exterior, holes));
    }

    private static Vector2[] ParseRing(JsonElement ringElement)
    {
        if (ringElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var points = new List<Vector2>();
        foreach (var pos in ringElement.EnumerateArray())
        {
            if (pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() < 2)
            {
                continue;
            }

            var lon = (float)pos[0].GetDouble();
            var lat = (float)pos[1].GetDouble();
            points.Add(new Vector2(lon, lat));
        }

        if (points.Count > 1 && NearlyEqual(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points.ToArray();
    }

    private static Vector2 ComputeCentroid(IReadOnlyList<GeoPolygon> polygons)
    {
        var sumX = 0f;
        var sumY = 0f;
        var count = 0;

        for (var i = 0; i < polygons.Count; i++)
        {
            var exterior = polygons[i].Exterior;
            for (var p = 0; p < exterior.Length; p++)
            {
                sumX += exterior[p].X;
                sumY += exterior[p].Y;
                count++;
            }
        }

        if (count == 0)
        {
            return Vector2.Zero;
        }

        return new Vector2(sumX / count, sumY / count);
    }

    private static float DeltaLongitudeDegrees(float a, float b)
    {
        var delta = a - b;
        while (delta > 180f)
        {
            delta -= 360f;
        }

        while (delta < -180f)
        {
            delta += 360f;
        }

        return delta;
    }

    private static bool NearlyEqual(Vector2 a, Vector2 b)
    {
        return MathF.Abs(a.X - b.X) < 0.0001f && MathF.Abs(a.Y - b.Y) < 0.0001f;
    }

    private sealed class ProvinceFeature
    {
        public string Id { get; }
        public string Name { get; }
        public Vector2 CentroidLonLat { get; }
        public IReadOnlyList<GeoPolygon> Polygons { get; }

        public ProvinceFeature(string id, string name, Vector2 centroidLonLat, IReadOnlyList<GeoPolygon> polygons)
        {
            Id = id;
            Name = name;
            CentroidLonLat = centroidLonLat;
            Polygons = polygons;
        }

        public bool Contains(float lon, float lat)
        {
            for (var i = 0; i < Polygons.Count; i++)
            {
                var polygon = Polygons[i];
                if (!IsPointInRing(lon, lat, polygon.Exterior))
                {
                    continue;
                }

                var inHole = false;
                for (var h = 0; h < polygon.Holes.Count; h++)
                {
                    if (IsPointInRing(lon, lat, polygon.Holes[h]))
                    {
                        inHole = true;
                        break;
                    }
                }

                if (!inHole)
                {
                    return true;
                }
            }

            return false;
        }

        public float BorderDistanceDegrees(float lon, float lat)
        {
            var bestSq = float.MaxValue;
            var point = new Vector2(lon, lat);

            for (var i = 0; i < Polygons.Count; i++)
            {
                var polygon = Polygons[i];
                bestSq = MathF.Min(bestSq, RingDistanceSquared(point, polygon.Exterior));
                for (var h = 0; h < polygon.Holes.Count; h++)
                {
                    bestSq = MathF.Min(bestSq, RingDistanceSquared(point, polygon.Holes[h]));
                }
            }

            return MathF.Sqrt(bestSq);
        }

        private static float RingDistanceSquared(Vector2 point, Vector2[] ring)
        {
            if (ring.Length == 0)
            {
                return float.MaxValue;
            }

            var best = float.MaxValue;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                var distSq = SegmentDistanceSquared(point, a, b);
                if (distSq < best)
                {
                    best = distSq;
                }
            }

            return best;
        }

        private static float SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var abLenSq = Vector2.Dot(ab, ab);
            if (abLenSq <= float.Epsilon)
            {
                return Vector2.DistanceSquared(p, a);
            }

            var t = Vector2.Dot(p - a, ab) / abLenSq;
            t = MathUtil.Clamp(t, 0f, 1f);
            var projection = a + (ab * t);
            return Vector2.DistanceSquared(p, projection);
        }

        private static bool IsPointInRing(float lon, float lat, Vector2[] ring)
        {
            var inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var xi = ring[i].X;
                var yi = ring[i].Y;
                var xj = ring[j].X;
                var yj = ring[j].Y;

                var yiAbove = yi > lat;
                var yjAbove = yj > lat;
                if (yiAbove == yjAbove)
                {
                    continue;
                }

                var crossLon = (xj - xi) * (lat - yi) / (yj - yi + float.Epsilon) + xi;
                if (lon < crossLon)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }

    private sealed record GeoPolygon(Vector2[] Exterior, IReadOnlyList<Vector2[]> Holes);
}


