namespace SimEngine.Server.Worlds;

/// <summary>
/// A world the server can load. <see cref="WorldId"/> is the stable identifier
/// clients pass to <c>IGameSessionGrain.InitializeAsync</c>; it is also used as
/// the scenario id in save metadata, so it must never change for a shipped world.
/// </summary>
public sealed record WorldAssetInfo(string WorldId, string DisplayName, string ProvinceFileName, string CountryFileName);

/// <summary>
/// Server-owned catalog of loadable worlds. Content resolution lives on the
/// server so clients never deal in file paths — only world ids.
/// </summary>
public static class WorldCatalog
{
    public static IReadOnlyList<WorldAssetInfo> All { get; } =
    [
        new("grid4", "Grid 4  (2x2 synthetic)", "grid4.geojson", "grid4.countries.json"),
        new("germany_admin1", "Germany  (16 Bundeslander)", "germany_admin1.geojson", "germany_admin1.countries.json"),
        new("europe_west_admin1", "Western Europe  (Germany + neighbors)", "europe_west_admin1.geojson", "europe_west_admin1.countries.json"),
        new("world_admin1", "World  (all countries, admin-1)", "world_admin1.geojson", "world_admin1.countries.json"),
    ];

    /// <summary>Finds a world by its stable id, or null if unknown.</summary>
    public static WorldAssetInfo? Find(string worldId) =>
        All.FirstOrDefault(w => string.Equals(w.WorldId, worldId, StringComparison.Ordinal));

    /// <summary>Resolves the on-disk GeoJSON path for a catalog entry.</summary>
    public static string ResolvePath(WorldAssetInfo asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Path.Combine(AppContext.BaseDirectory, "Worlds", asset.ProvinceFileName);
    }

    /// <summary>Resolves the on-disk country definitions path for a catalog entry.</summary>
    public static string ResolveCountriesPath(WorldAssetInfo asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Path.Combine(AppContext.BaseDirectory, "Worlds", asset.CountryFileName);
    }
}
