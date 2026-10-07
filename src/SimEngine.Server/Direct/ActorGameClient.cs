using Akka.Actor;
using Akka.Hosting;
using SimEngine.Contracts;

namespace SimEngine.Server.Direct;

/// <summary>
/// Trusted, Akka-level access to the server actor topology for tests, tools,
/// and server-side code. Callers choose the player id freely, so this must
/// never be exposed to untrusted clients; those go through the gateway.
/// </summary>
public sealed class ActorGameClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly IActorRef _sessions;
    private readonly IActorRef _lobby;

    private ActorGameClient(ActorSystem system, IActorRef sessions, IActorRef lobby)
    {
        System = system;
        _sessions = sessions;
        _lobby = lobby;
    }

    /// <summary>The actor system used for asks and subscriber actors.</summary>
    public ActorSystem System { get; }

    /// <summary>Binds to actors registered in a local <see cref="ActorRegistry"/>.</summary>
    public static ActorGameClient FromLocalRegistry(ActorSystem system, IReadOnlyActorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(registry);
        return new ActorGameClient(system, registry.Get<SessionActorsMarker>(), registry.Get<LobbyMarker>());
    }

    /// <summary>
    /// Resolves the actors under a trusted remote root such as
    /// <c>akka.tcp://SimEngineServer@host:port/user</c> (server-to-server only).
    /// </summary>
    public static async Task<ActorGameClient> ConnectRemoteAsync(ActorSystem system, string remoteRootPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteRootPath);

        var root = remoteRootPath.TrimEnd('/');
        var sessions = await system.ActorSelection($"{root}/{ActorNames.Sessions}").ResolveOne(Timeout, ct).ConfigureAwait(false);
        var lobby = await system.ActorSelection($"{root}/{ActorNames.Lobby}").ResolveOne(Timeout, ct).ConfigureAwait(false);
        return new ActorGameClient(system, sessions, lobby);
    }

    public ActorSessionClient GetSession(string sessionId) => new(_sessions, sessionId);

    public Task<GameListing> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default) =>
        _lobby.Ask<GameListing>(new LobbyProtocol.CreateGame(request), Timeout, ct);

    public Task<GameListing> CreateGameFromSaveAsync(string name, string savePath, CancellationToken ct = default) =>
        _lobby.Ask<GameListing>(new LobbyProtocol.CreateGameFromSave(name, savePath), Timeout, ct);

    public Task<GameListing[]> ListGamesAsync(CancellationToken ct = default) =>
        _lobby.Ask<GameListing[]>(new LobbyProtocol.ListGames(), Timeout, ct);

    public async Task<GameListing?> GetGameAsync(string sessionId, CancellationToken ct = default)
    {
        var result = await _lobby.Ask<LobbyProtocol.GameLookupResult>(new LobbyProtocol.GetGame(sessionId), Timeout, ct).ConfigureAwait(false);
        return result.Listing;
    }

    public Task<bool> RemoveGameAsync(string sessionId, CancellationToken ct = default) =>
        _lobby.Ask<bool>(new LobbyProtocol.RemoveGame(sessionId), Timeout, ct);

    public Task<WorldOption[]> ListWorldsAsync(CancellationToken ct = default) =>
        _lobby.Ask<WorldOption[]>(new LobbyProtocol.ListWorlds(), Timeout, ct);
}
