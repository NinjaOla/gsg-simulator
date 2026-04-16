using SimEngine.Ids;

namespace SimEngine.State.Loading;

/// <summary>
/// Composition helpers that turn an <see cref="IWorldLoader"/> output into a
/// fully-populated <see cref="SimulationState"/>. Lives in the engine so
/// callers don't have to duplicate the seed → builder → edge-replay loop,
/// and so the canonical replay order is enforced in exactly one place.
/// </summary>
public static class WorldLoaders
{
    public static SimulationState LoadIntoState(IWorldLoader loader, Stream source)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(source);

        var result = loader.Load(source);
        return LoadIntoState(result);
    }

    public static SimulationState LoadIntoState(IWorldLoader loader, string path)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return LoadIntoState(loader, stream);
    }

    public static SimulationState LoadIntoState(WorldLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new WorldBuilder();
        var assignedIds = new ProvinceId[result.Provinces.Count];

        for (var i = 0; i < result.Provinces.Count; i++)
        {
            var seed = result.Provinces[i];
            assignedIds[i] = builder.AddProvince(
                seed.Name,
                seed.Terrain,
                seed.CentroidLatE6,
                seed.CentroidLonE6);
        }

        // Replay each undirected edge exactly once in canonical order:
        // walk the assigned ProvinceIds 1..N, ask the loader's adjacency for
        // each one's neighbors (ascending), and only emit an edge when the
        // neighbor's id is greater than the current id. This sidesteps the
        // documented "FrozenDictionary enumeration order is not guaranteed"
        // caveat on AdjacencyGraph and produces byte-identical builder state
        // across runs.
        foreach (var id in assignedIds)
        {
            foreach (var neighbor in result.Adjacency.NeighborsOf(id))
            {
                if (neighbor.Value > id.Value)
                {
                    builder.AddEdge(id, neighbor);
                }
            }
        }

        return builder.Build();
    }
}
