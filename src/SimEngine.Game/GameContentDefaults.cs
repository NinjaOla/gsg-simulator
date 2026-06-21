namespace SimEngine.Game;

/// <summary>
/// Shared content defaults used when real content versioning has not landed
/// yet. Centralized so the server and clients derive the same
/// <see cref="ContentHasher"/> inputs (and therefore the same content hash)
/// instead of each repeating the placeholder literal.
/// </summary>
public static class GameContentDefaults
{
    /// <summary>
    /// Placeholder content version for the shipped game content. Replaced by a
    /// real, content-derived version once mod/content versioning exists.
    /// </summary>
    public const string ContentVersion = "dev";
}
