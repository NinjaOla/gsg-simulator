using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.Utilities;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System.Text.Json;

namespace SimEngine.Game.Ui.Stride;

internal static class GeoJsonBorderLines
{
    public static Entity CreateEntity(
        global::Stride.Engine.Game game,
        string geoJsonPath,
        float globeRadius,
        Color borderColor,
        int maxSegments)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(geoJsonPath);

        if (!File.Exists(geoJsonPath))
        {
            throw new FileNotFoundException($"GeoJSON file not found: {geoJsonPath}", geoJsonPath);
        }

        using var stream = File.OpenRead(geoJsonPath);
        var segments = ReadSegments(stream, maxSegments);

        using var meshBuilder = new MeshBuilder();
        meshBuilder.WithPrimitiveType(PrimitiveType.LineList);
        meshBuilder.WithIndexType(IndexingType.Int32);
        var positionElement = meshBuilder.WithPosition<Vector3>();

        var borderRadius = globeRadius + 0.01f;

        foreach (var segment in segments)
        {
            var start = GeoProjection.ToUnitSphere(segment.StartLon, segment.StartLat) * borderRadius;
            var end = GeoProjection.ToUnitSphere(segment.EndLon, segment.EndLat) * borderRadius;

            var v0 = meshBuilder.AddVertex();
            meshBuilder.SetElement(v0, positionElement, start);
            meshBuilder.AddIndex(v0);

            var v1 = meshBuilder.AddVertex();
            meshBuilder.SetElement(v1, positionElement, end);
            meshBuilder.AddIndex(v1);
        }

        var model = new Model
        {
            new MaterialInstance { Material = game.CreateFlatMaterial(borderColor) },
            new Mesh
            {
                Draw = meshBuilder.ToMeshDraw(game.GraphicsDevice),
                MaterialIndex = 0,
            },
        };

        return new Entity("GeoJsonBorders")
        {
            new ModelComponent(model),
        };
    }

    private static List<Segment> ReadSegments(Stream geoJson, int maxSegments)
    {
        using var doc = JsonDocument.Parse(geoJson);
        var root = doc.RootElement;
        var segments = new List<Segment>(Math.Min(maxSegments, 16_384));

        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return segments;
        }

        foreach (var feature in features.EnumerateArray())
        {
            if (segments.Count >= maxSegments)
            {
                break;
            }

            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AppendGeometrySegments(geometry, segments, maxSegments);
        }

        return segments;
    }

    private static void AppendGeometrySegments(JsonElement geometry, List<Segment> segments, int maxSegments)
    {
        var type = geometry.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        if (!geometry.TryGetProperty("coordinates", out var coordinates))
        {
            return;
        }

        switch (type)
        {
            case "Polygon":
                AppendPolygonSegments(coordinates, segments, maxSegments);
                break;
            case "MultiPolygon":
                foreach (var polygon in coordinates.EnumerateArray())
                {
                    if (segments.Count >= maxSegments)
                    {
                        break;
                    }

                    AppendPolygonSegments(polygon, segments, maxSegments);
                }
                break;
        }
    }

    private static void AppendPolygonSegments(JsonElement polygon, List<Segment> segments, int maxSegments)
    {
        foreach (var ring in polygon.EnumerateArray())
        {
            if (segments.Count >= maxSegments)
            {
                break;
            }

            var points = new List<(float Lon, float Lat)>();
            foreach (var position in ring.EnumerateArray())
            {
                if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2)
                {
                    continue;
                }

                var lon = (float)position[0].GetDouble();
                var lat = (float)position[1].GetDouble();
                points.Add((lon, lat));
            }

            if (points.Count < 2)
            {
                continue;
            }

            var last = points.Count - 1;
            for (var i = 0; i < last && segments.Count < maxSegments; i++)
            {
                var start = points[i];
                var end = points[i + 1];
                segments.Add(new Segment(start.Lon, start.Lat, end.Lon, end.Lat));
            }

            var firstPoint = points[0];
            var lastPoint = points[last];
            if (segments.Count < maxSegments && !AreNear(firstPoint, lastPoint))
            {
                segments.Add(new Segment(lastPoint.Lon, lastPoint.Lat, firstPoint.Lon, firstPoint.Lat));
            }
        }
    }

    private static bool AreNear((float Lon, float Lat) a, (float Lon, float Lat) b)
    {
        return MathF.Abs(a.Lon - b.Lon) < 0.0001f && MathF.Abs(a.Lat - b.Lat) < 0.0001f;
    }

    private readonly record struct Segment(float StartLon, float StartLat, float EndLon, float EndLat);
}





