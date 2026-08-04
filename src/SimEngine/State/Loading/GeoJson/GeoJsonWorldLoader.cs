using NetTopologySuite.Geometries;
using SimEngine.Ids;
using SimEngine.State.Components;

namespace SimEngine.State.Loading.GeoJson;

/// <summary>
/// <see cref="IWorldLoader"/> implementation that consumes authored GeoJSON
/// polygons and produces <see cref="ProvinceSeed"/> records plus an
/// <see cref="AdjacencyGraph"/> derived from shared polygon edges.
///
/// Determinism contract:
/// <list type="bullet">
///   <item>Province identity comes from the required <c>province_id</c>
///     property on each feature.</item>
///   <item>Centroids are quantized to <see cref="int"/> microdegrees with
///     banker's rounding before they cross into game state.</item>
///   <item>Adjacency edges are derived from a snapped-segment hash and
///     emitted via <see cref="AdjacencyGraph.Builder"/>, which already
///     guarantees deterministic neighbor ordering.</item>
/// </list>
/// </summary>
public sealed class GeoJsonWorldLoader : IWorldLoader
{
    private const string ProvinceIdPropertyName = "province_id";
    private const string PopulationPropertyName = "population";

    public WorldLoadResult Load(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var features = GeoJsonFeatureReader.ReadAll(source);

        var seeds = new List<ProvinceSeed>(features.Count);
        var adjacencyInput = new List<(ProvinceId Id, Geometry Geometry)>(features.Count);
        var seenProvinceIds = new HashSet<ProvinceId>();

        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            var name = ProvinceNameResolver.Resolve(feature.Attributes, feature.FeatureIndex);
            var (latE6, lonE6) = CentroidQuantizer.Quantize(feature.Geometry);
            var provinceId = new ProvinceId(ReadRequiredUInt32Property(
                feature.Attributes,
                ProvinceIdPropertyName,
                feature.FeatureIndex));
            var initialPopulation = ReadRequiredInt64Property(
                feature.Attributes,
                PopulationPropertyName,
                feature.FeatureIndex);

            if (!seenProvinceIds.Add(provinceId))
            {
                throw new InvalidDataException(
                    $"GeoJSON contains duplicate {ProvinceIdPropertyName}={provinceId.Value}.");
            }

            seeds.Add(new ProvinceSeed(provinceId, name, Terrain.Land, latE6, lonE6, initialPopulation));
            adjacencyInput.Add((provinceId, feature.Geometry));
        }

        var adjacency = SharedEdgeAdjacencyBuilder.Build(adjacencyInput);
        return new WorldLoadResult(seeds, adjacency);
    }

    private static uint ReadRequiredUInt32Property(
        NetTopologySuite.Features.IAttributesTable attributes,
        string propertyName,
        int featureIndex)
    {
        var value = ReadRequiredProperty(attributes, propertyName, featureIndex);

        try
        {
            var parsed = Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            if (parsed == 0)
            {
                throw new InvalidDataException(
                    $"GeoJSON feature at index {featureIndex} has {propertyName}=0; ids must be >= 1.");
            }

            return parsed;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"GeoJSON feature at index {featureIndex} has invalid {propertyName} value '{value}'.",
                ex);
        }
    }

    private static long ReadRequiredInt64Property(
        NetTopologySuite.Features.IAttributesTable attributes,
        string propertyName,
        int featureIndex)
    {
        var value = ReadRequiredProperty(attributes, propertyName, featureIndex);

        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"GeoJSON feature at index {featureIndex} has invalid {propertyName} value '{value}'.",
                ex);
        }
    }

    private static object ReadRequiredProperty(
        NetTopologySuite.Features.IAttributesTable attributes,
        string propertyName,
        int featureIndex)
    {
        var availableNames = attributes.GetNames();
        var nameSet = new HashSet<string>(availableNames, StringComparer.Ordinal);
        if (!nameSet.Contains(propertyName))
        {
            throw new InvalidDataException(
                $"GeoJSON feature at index {featureIndex} is missing required '{propertyName}' property.");
        }

        var value = attributes[propertyName];
        if (value is null)
        {
            throw new InvalidDataException(
                $"GeoJSON feature at index {featureIndex} has null '{propertyName}' property.");
        }

        return value;
    }
}
