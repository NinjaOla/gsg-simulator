using NetTopologySuite.Geometries;
using SimEngine.Ids;
using SimEngine.State.Components;

namespace SimEngine.State.Loading.GeoJson;

/// <summary>
/// <see cref="IWorldLoader"/> implementation that consumes Natural-Earth-style
/// GeoJSON polygons and produces <see cref="ProvinceSeed"/> records plus an
/// <see cref="AdjacencyGraph"/> derived from shared polygon edges.
///
/// Determinism contract:
/// <list type="bullet">
///   <item>Provinces are ordered by their 0-based index in the source
///     <c>FeatureCollection</c>. Same input bytes → same seed sequence on
///     every run, OS, and CPU.</item>
///   <item>Centroids are quantized to <see cref="int"/> microdegrees with
///     banker's rounding before they cross into game state.</item>
///   <item>Adjacency edges are derived from a snapped-segment hash and
///     emitted via <see cref="AdjacencyGraph.Builder"/>, which already
///     guarantees deterministic neighbor ordering.</item>
/// </list>
/// </summary>
public sealed class GeoJsonWorldLoader : IWorldLoader
{
    public WorldLoadResult Load(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var features = GeoJsonFeatureReader.ReadAll(source);

        var seeds = new List<ProvinceSeed>(features.Count);
        var adjacencyInput = new List<(ProvinceId Id, Geometry Geometry)>(features.Count);

        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            var name = ProvinceNameResolver.Resolve(feature.Attributes, feature.FeatureIndex);
            var (latE6, lonE6) = CentroidQuantizer.Quantize(feature.Geometry);

            seeds.Add(new ProvinceSeed(name, Terrain.Land, latE6, lonE6));

            // ProvinceId is allocated in feature-index order so the loader's
            // adjacency keys line up with the IDs that WorldBuilder will hand
            // out when it consumes the seeds in the same order.
            var provinceId = new ProvinceId((uint)(feature.FeatureIndex + 1));
            adjacencyInput.Add((provinceId, feature.Geometry));
        }

        var adjacency = SharedEdgeAdjacencyBuilder.Build(adjacencyInput);
        return new WorldLoadResult(seeds, adjacency);
    }
}
