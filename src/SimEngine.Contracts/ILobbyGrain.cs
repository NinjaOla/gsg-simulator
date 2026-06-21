namespace SimEngine.Contracts;

/// <summary>
/// Well-known keys for lobby grains.
/// </summary>
public static class LobbyKeys
{
    /// <summary>Key for the single shared lobby grain.</summary>
    public const string Default = "default";
}

/// <summary>
/// Grain interface for the lobby: creates game sessions, lists and queries the
/// games it created, and exposes the server's world catalog so clients can pick
/// a world without dealing in file paths. Typically a singleton addressed by
/// <see cref="LobbyKeys.Default"/>.
/// </summary>
public interface ILobbyGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates and initializes a new game session, registers it, and returns
    /// its listing. The lobby assigns the session id. Propagates the session's
    /// initialization errors (for example an unknown world id) without
    /// registering the game.
    /// </summary>
    Task<GameListing> CreateGameAsync(CreateGameRequest request);

    /// <summary>
    /// Lists the games created through this lobby, ordered deterministically.
    /// </summary>
    Task<GameListing[]> ListGamesAsync();

    /// <summary>Gets a single game listing, or <c>null</c> if it is not registered.</summary>
    Task<GameListing?> GetGameAsync(string sessionId);

    /// <summary>
    /// Unregisters a game and shuts its session down. Returns <c>false</c> if no
    /// such game is registered.
    /// </summary>
    Task<bool> RemoveGameAsync(string sessionId);

    /// <summary>Lists the worlds a game can be created from.</summary>
    Task<WorldOption[]> ListWorldsAsync();
}
