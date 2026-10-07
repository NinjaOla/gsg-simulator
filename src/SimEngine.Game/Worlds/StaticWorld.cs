using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;

namespace SimEngine.Game.Worlds;

/// <summary>
/// Client-side, read-only static data for a world (province identity and
/// adjacency), loaded from local content files rather than received from the
/// server. <see cref="ContentHash"/> is presented on join so the server can
/// reject a client whose local content differs.
/// </summary>
public sealed class StaticWorld
{
    private readonly Dictionary<ProvinceId, ProvinceSeed> _byId;

    private StaticWorld(string worldId, string contentHash, ProvinceSeed[] provinces, AdjacencyGraph adjacency)
    {
        WorldId = worldId;
        ContentHash = contentHash;
        Provinces = provinces;
        Adjacency = adjacency;
        _byId = provinces.ToDictionary(p => p.ProvinceId);
    }

    public string WorldId { get; }

    public string ContentHash { get; }

    /// <summary>All provinces, ordered by id.</summary>
    public IReadOnlyList<ProvinceSeed> Provinces { get; }

    public AdjacencyGraph Adjacency { get; }

    public bool TryGetProvince(ProvinceId id, out ProvinceSeed province) =>
        _byId.TryGetValue(id, out province);

    /// <summary>Loads the world's static data from the local content files.</summary>
    public static StaticWorld Load(string worldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);

        var asset = WorldCatalog.Find(worldId)
            ?? throw new ArgumentException($"Unknown world id '{worldId}'.", nameof(worldId));
        var worldPath = WorldCatalog.ResolvePath(asset);
        var countriesPath = WorldCatalog.ResolveCountriesPath(asset);

        WorldLoadResult result;
        using (var stream = new FileStream(worldPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = new GeoJsonWorldLoader().Load(stream);
        }

        // Same adjacency the server's state is built with (includes edge-less provinces).
        var adjacency = WorldLoaders.LoadIntoState(result).Adjacency;
        var provinces = result.Provinces.OrderBy(p => p.ProvinceId.Value).ToArray();
        var contentHash = ContentHasher.ComputeFromFiles([worldPath, countriesPath], GameContentDefaults.ContentVersion);

        return new StaticWorld(worldId, contentHash, provinces, adjacency);
    }
}
