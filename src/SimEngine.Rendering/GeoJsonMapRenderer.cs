using System.Globalization;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimEngine.Rendering;

/// <summary>
/// Options controlling how <see cref="GeoJsonMapRenderer"/> draws a world.
/// </summary>
public sealed record MapRenderOptions
{
    /// <summary>Target image width in pixels. Height is derived from the data bounds.</summary>
    public int Width { get; init; } = 1600;

    /// <summary>Uniform padding, in pixels, around the projected data.</summary>
    public int Padding { get; init; } = 24;

    /// <summary>Background fill color.</summary>
    public Color Background { get; init; } = Color.FromPixel(new Rgba32(24, 26, 33));

    /// <summary>Province outline color.</summary>
    public Color BorderColor { get; init; } = Color.FromPixel(new Rgba32(15, 16, 20));

    /// <summary>Province outline thickness in pixels.</summary>
    public float BorderThickness { get; init; } = 1f;

    /// <summary>
    /// Feature property used to group provinces into a shared fill color
    /// (e.g. <c>adm0_a3</c> colors by country). When the property is missing
    /// each feature falls back to its own index, yielding per-province colors.
    /// </summary>
    public string GroupPropertyName { get; init; } = "adm0_a3";
}

/// <summary>
/// Renders a GeoJSON <c>FeatureCollection</c> of province polygons to a PNG.
///
/// The renderer is deliberately decoupled from the simulation state: it reads
/// geometry straight from GeoJSON (Polygon / MultiPolygon), applies an
/// equirectangular projection with a mid-latitude width correction, and fills
/// each province with a per-group color. This keeps it reusable both as an
/// offline tool and as a future in-game map export.
/// </summary>
public static class GeoJsonMapRenderer
{
    /// <summary>Renders <paramref name="geoJsonPath"/> to a PNG at <paramref name="outputPngPath"/>.</summary>
    public static void RenderFileToPng(string geoJsonPath, string outputPngPath, MapRenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(geoJsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPngPath);

        using var input = File.OpenRead(geoJsonPath);
        using var output = File.Create(outputPngPath);
        Render(input, output, options);
    }

    /// <summary>Renders a GeoJSON stream to a PNG written to <paramref name="outputPng"/>.</summary>
    public static void Render(Stream geoJson, Stream outputPng, MapRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geoJson);
        ArgumentNullException.ThrowIfNull(outputPng);
        options ??= new MapRenderOptions();

        var features = ReadFeatures(geoJson, options.GroupPropertyName);
        if (features.Count == 0)
        {
            throw new InvalidOperationException("GeoJSON contained no renderable polygon features.");
        }

        var bounds = GeoBounds.FromFeatures(features);
        var projector = new EquirectangularProjector(bounds, options.Width, options.Padding);
        var colors = BuildGroupColors(features);

        using var image = new Image<Rgba32>(projector.Width, projector.Height);
        var borderPen = Pens.Solid(options.BorderColor, options.BorderThickness);
        image.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(options.Background));

                for (var i = 0; i < features.Count; i++)
                {
                    var feature = features[i];
                    var fill = Brushes.Solid(colors[feature.GroupKey ?? i.ToString(CultureInfo.InvariantCulture)]);

                    foreach (var ring in feature.ExteriorRings)
                    {
                        if (ring.Length < 3)
                        {
                            continue;
                        }

                        var points = new PointF[ring.Length];
                        for (var p = 0; p < ring.Length; p++)
                        {
                            points[p] = projector.Project(ring[p].Lon, ring[p].Lat);
                        }

                        var polygon = new Polygon(points);
                        canvas.Fill(fill, polygon);
                        canvas.Draw(borderPen, polygon);
                    }
                }
            });
        });

        image.SaveAsPng(outputPng);
    }

    private static Dictionary<string, Color> BuildGroupColors(IReadOnlyList<Feature> features)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < features.Count; i++)
        {
            var key = features[i].GroupKey ?? i.ToString(CultureInfo.InvariantCulture);
            if (seen.Add(key))
            {
                keys.Add(key);
            }
        }

        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            var hue = keys.Count == 0 ? 0f : 360f * i / keys.Count;
            colors[keys[i]] = FromHsv(hue, 0.55f, 0.85f);
        }

        return colors;
    }

    private static List<Feature> ReadFeatures(Stream geoJson, string groupPropertyName)
    {
        using var doc = JsonDocument.Parse(geoJson);
        var root = doc.RootElement;
        var features = new List<Feature>();

        if (!root.TryGetProperty("features", out var featureArray) || featureArray.ValueKind != JsonValueKind.Array)
        {
            return features;
        }

        foreach (var feature in featureArray.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? groupKey = null;
            if (feature.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
                && props.TryGetProperty(groupPropertyName, out var groupEl) && groupEl.ValueKind == JsonValueKind.String)
            {
                groupKey = groupEl.GetString();
            }

            var rings = new List<Position[]>();
            AppendExteriorRings(geometry, rings);
            if (rings.Count > 0)
            {
                features.Add(new Feature(groupKey, rings));
            }
        }

        return features;
    }

    private static void AppendExteriorRings(JsonElement geometry, List<Position[]> rings)
    {
        var type = geometry.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        if (!geometry.TryGetProperty("coordinates", out var coords))
        {
            return;
        }

        switch (type)
        {
            case "Polygon":
                AppendPolygonExterior(coords, rings);
                break;
            case "MultiPolygon":
                foreach (var polygon in coords.EnumerateArray())
                {
                    AppendPolygonExterior(polygon, rings);
                }
                break;
        }
    }

    private static void AppendPolygonExterior(JsonElement polygon, List<Position[]> rings)
    {
        // A polygon's first ring is its exterior; holes are ignored for the fill.
        using var ringEnumerator = polygon.EnumerateArray().GetEnumerator();
        if (!ringEnumerator.MoveNext())
        {
            return;
        }

        var exterior = ringEnumerator.Current;
        var positions = new List<Position>();
        foreach (var position in exterior.EnumerateArray())
        {
            var lon = position[0].GetDouble();
            var lat = position[1].GetDouble();
            positions.Add(new Position(lon, lat));
        }

        if (positions.Count >= 3)
        {
            rings.Add(positions.ToArray());
        }
    }

    private static Color FromHsv(float hue, float saturation, float value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60f % 2) - 1));
        var m = value - c;

        (float r, float g, float b) = hue switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return Color.FromPixel(new Rgba32(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255)));
    }

    private readonly record struct Position(double Lon, double Lat);

    private sealed record Feature(string? GroupKey, IReadOnlyList<Position[]> ExteriorRings);

    private sealed class GeoBounds
    {
        public double MinLon { get; private init; }
        public double MaxLon { get; private init; }
        public double MinLat { get; private init; }
        public double MaxLat { get; private init; }

        public static GeoBounds FromFeatures(IReadOnlyList<Feature> features)
        {
            double minLon = double.MaxValue, maxLon = double.MinValue;
            double minLat = double.MaxValue, maxLat = double.MinValue;

            foreach (var feature in features)
            {
                foreach (var ring in feature.ExteriorRings)
                {
                    foreach (var p in ring)
                    {
                        minLon = Math.Min(minLon, p.Lon);
                        maxLon = Math.Max(maxLon, p.Lon);
                        minLat = Math.Min(minLat, p.Lat);
                        maxLat = Math.Max(maxLat, p.Lat);
                    }
                }
            }

            return new GeoBounds
            {
                MinLon = minLon,
                MaxLon = maxLon,
                MinLat = minLat,
                MaxLat = maxLat,
            };
        }
    }

    private sealed class EquirectangularProjector
    {
        private readonly double _minLon;
        private readonly double _maxLat;
        private readonly double _cosLat;
        private readonly double _scale;
        private readonly int _padding;

        public int Width { get; }
        public int Height { get; }

        public EquirectangularProjector(GeoBounds bounds, int width, int padding)
        {
            _minLon = bounds.MinLon;
            _maxLat = bounds.MaxLat;
            _padding = padding;

            var midLat = (bounds.MinLat + bounds.MaxLat) / 2.0;
            _cosLat = Math.Max(Math.Cos(midLat * Math.PI / 180.0), 0.01);

            var dataWidth = Math.Max((bounds.MaxLon - _minLon) * _cosLat, 1e-6);
            var dataHeight = Math.Max(_maxLat - bounds.MinLat, 1e-6);

            var drawWidth = Math.Max(width - (2 * padding), 1);
            _scale = drawWidth / dataWidth;

            Width = width;
            Height = (int)Math.Ceiling(dataHeight * _scale) + (2 * padding);
        }

        public PointF Project(double lon, double lat)
        {
            var x = _padding + ((lon - _minLon) * _cosLat * _scale);
            var y = _padding + ((_maxLat - lat) * _scale);
            return new PointF((float)x, (float)y);
        }
    }
}
