using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace SimEngine.State.Loading.GeoJson;

/// <summary>
/// Thin wrapper over NTS <see cref="GeoJsonReader"/> that yields
/// <see cref="ReadFeature"/> records in source-file order. Isolated so the
/// loader pipeline can be tested with hand-built feature sequences without
/// going through stream parsing.
/// </summary>
internal static class GeoJsonFeatureReader
{
    public static IReadOnlyList<ReadFeature> ReadAll(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var textReader = new StreamReader(source);
        var json = textReader.ReadToEnd();

        var reader = new GeoJsonReader();
        var collection = reader.Read<FeatureCollection>(json)
            ?? throw new InvalidDataException(
                "GeoJSON stream did not deserialize into a FeatureCollection.");

        var output = new List<ReadFeature>(collection.Count);
        for (var i = 0; i < collection.Count; i++)
        {
            var feature = collection[i];
            if (feature.Geometry is null || feature.Geometry.IsEmpty)
            {
                throw new InvalidDataException(
                    $"GeoJSON feature at index {i} is missing geometry.");
            }

            if (feature.Attributes is null)
            {
                throw new InvalidDataException(
                    $"GeoJSON feature at index {i} is missing properties.");
            }

            output.Add(new ReadFeature(i, feature.Geometry, feature.Attributes));
        }

        return output;
    }
}

internal readonly record struct ReadFeature(
    int FeatureIndex,
    Geometry Geometry,
    IAttributesTable Attributes);
