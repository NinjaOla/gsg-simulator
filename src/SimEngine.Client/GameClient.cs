using Akka.Actor;
using Akka.Hosting;
using SimEngine.Contracts;

namespace SimEngine.Client;

/// <summary>
/// Client-side facade over the SimEngine server actor topology. Holds resolved
/// references to the session entity host and the singleton lobby, and hands out
/// <see cref="SessionClient"/> handles. Works both in-process (single-player,
/// resolved from the local <see cref="ActorRegistry"/>) and against a remote
/// server (resolved by actor path).
/// </summary>
public sealed class GameClient
{
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(30);

    private readonly ActorSystem _system;
    private readonly IActorRef _sessions;
    private readonly IActorRef _lobby;

    private GameClient(ActorSystem system, IActorRef sessions, IActorRef lobby)
    {
        _system = system;
        _sessions = sessions;
        _lobby = lobby;
    }

    /// <summary>The actor system backing this client.</summary>
    public ActorSystem System => _system;

    /// <summary>
    /// Creates a client bound to an in-process server whose actors are already
    /// registered in <paramref name="registry"/> (single-player / tests).
    /// </summary>
    public static GameClient FromLocalRegistry(ActorSystem system, IReadOnlyActorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(registry);

        var sessions = registry.Get<SessionActorsMarker>();
        var lobby = registry.Get<LobbyMarker>();
        return new GameClient(system, sessions, lobby);
    }

    /// <summary>
    /// Creates a client connected to a remote server by resolving the well-known
    /// session host and lobby actor paths under <paramref name="remoteRootPath"/>
    /// (for example <c>akka.tcp://SimEngineServer@host:port/user</c>).
    /// </summary>
    public static async Task<GameClient> ConnectRemoteAsync(
        ActorSystem system,
        string remoteRootPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteRootPath);

        var root = remoteRootPath.TrimEnd('/');
        var sessions = await system.ActorSelection($"{root}/{ActorNames.Sessions}")
            .ResolveOne(ResolveTimeout, ct).ConfigureAwait(false);
        var lobby = await system.ActorSelection($"{root}/{ActorNames.Lobby}")
            .ResolveOne(ResolveTimeout, ct).ConfigureAwait(false);

        return new GameClient(system, sessions, lobby);
    }

    /// <summary>Gets a handle for the session with the given id.</summary>
    public SessionClient GetSession(string sessionId) => new(_sessions, sessionId);

    /// <summary>Creates and initializes a new game, returning its listing.</summary>
    public Task<GameListing> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default) =>
        _lobby.Ask<GameListing>(new LobbyProtocol.CreateGame(request), ResolveTimeout, ct);

    /// <summary>Lists games created through the lobby.</summary>
    public Task<GameListing[]> ListGamesAsync(CancellationToken ct = default) =>
        _lobby.Ask<GameListing[]>(new LobbyProtocol.ListGames(), ResolveTimeout, ct);

    /// <summary>Gets a single game, or <c>null</c> if unknown.</summary>
    public async Task<GameListing?> GetGameAsync(string sessionId, CancellationToken ct = default)
    {
        var result = await _lobby.Ask<LobbyProtocol.GameLookupResult>(
            new LobbyProtocol.GetGame(sessionId), ResolveTimeout, ct).ConfigureAwait(false);
        return result.Listing;
    }

    /// <summary>Removes a game and shuts its session down.</summary>
    public Task<bool> RemoveGameAsync(string sessionId, CancellationToken ct = default) =>
        _lobby.Ask<bool>(new LobbyProtocol.RemoveGame(sessionId), ResolveTimeout, ct);

    /// <summary>Lists the worlds a game can be created from.</summary>
    public Task<WorldOption[]> ListWorldsAsync(CancellationToken ct = default) =>
        _lobby.Ask<WorldOption[]>(new LobbyProtocol.ListWorlds(), ResolveTimeout, ct);
}
