namespace SimEngine.Contracts;

/// <summary>
/// Message protocol for the lobby actor: creates sessions, lists/queries the
/// games it created, and exposes the server's world catalog.
/// </summary>
public static class LobbyProtocol
{
    /// <summary>
    /// Creates and initializes a new game session and registers it.
    /// Reply: <see cref="GameListing"/>, or <c>Status.Failure</c> on error.
    /// </summary>
    public sealed record CreateGame(CreateGameRequest Request);

    /// <summary>Lists games created through this lobby. Reply: <see cref="GameListing"/><c>[]</c>.</summary>
    public sealed record ListGames;

    /// <summary>Gets a single game. Reply: <see cref="GameLookupResult"/>.</summary>
    public sealed record GetGame(string SessionId);

    /// <summary>Wraps a nullable lobby lookup so Ask never returns a bare null.</summary>
    public sealed record GameLookupResult(GameListing? Listing);

    /// <summary>Unregisters a game and shuts its session down. Reply: <c>bool</c>.</summary>
    public sealed record RemoveGame(string SessionId);

    /// <summary>Lists the worlds a game can be created from. Reply: <see cref="WorldOption"/><c>[]</c>.</summary>
    public sealed record ListWorlds;
}
