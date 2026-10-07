using System.Collections.Concurrent;
using SimEngine.Contracts;
using SimEngine.Contracts.Wire;
using SimEngine.Networking;

namespace SimEngine.Client;

/// <summary>
/// Client for a SimEngine server reached over any transport (in-process for
/// single-player, TCP or Steam for multiplayer). The server assigns
/// <see cref="PlayerId"/> at connect time and uses it for every request.
/// </summary>
public sealed class GameClient : IAsyncDisposable
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, SessionSubscription> _subscriptions = new(StringComparer.Ordinal);

    private GameClient(ITransportConnection transport)
    {
        Connection = new ClientConnection(transport, OnPush);
        Connection.Closed.ContinueWith(
            t =>
            {
                foreach (var subscription in _subscriptions.Values)
                {
                    subscription.OnEnded($"Connection lost: {t.Result}");
                }
            },
            TaskScheduler.Default);
    }

    /// <summary>The id the server assigned to this connection.</summary>
    public string PlayerId { get; private set; } = string.Empty;

    /// <summary>Completes with the reason once the connection to the server is closed.</summary>
    public Task<string> Closed => Connection.Closed;

    internal ClientConnection Connection { get; }

    /// <summary>Connects to <paramref name="endpoint"/> and performs the handshake.</summary>
    public static async Task<GameClient> ConnectAsync(
        TransportRegistry transports,
        TransportEndpoint endpoint,
        string playerName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transports);
        var transport = await transports.ConnectAsync(endpoint, ct).ConfigureAwait(false);
        return await ConnectAsync(transport, playerName, ct).ConfigureAwait(false);
    }

    /// <summary>Performs the handshake on an open connection; disposes it on failure.</summary>
    public static async Task<GameClient> ConnectAsync(
        ITransportConnection transport,
        string playerName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);

        var client = new GameClient(transport);
        try
        {
            var welcome = await client.Connection.RequestAsync<ServerMessages.WelcomeReply>(
                id => new ClientMessages.Hello(id, WireProtocol.Version, playerName), HandshakeTimeout, ct).ConfigureAwait(false);
            client.PlayerId = welcome.PlayerId;
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Lists the worlds a game can be created from.</summary>
    public async Task<WorldOption[]> ListWorldsAsync(CancellationToken ct = default) =>
        (await Connection.RequestAsync<ServerMessages.WorldsReply>(
            id => new ClientMessages.ListWorlds(id), RequestTimeout, ct).ConfigureAwait(false)).Worlds;

    /// <summary>Lists games that have not ended.</summary>
    public async Task<GameListing[]> ListGamesAsync(CancellationToken ct = default) =>
        (await Connection.RequestAsync<ServerMessages.GamesReply>(
            id => new ClientMessages.ListGames(id), RequestTimeout, ct).ConfigureAwait(false)).Games;

    /// <summary>Gets a single game, or <c>null</c> if unknown or ended.</summary>
    public async Task<GameListing?> GetGameAsync(string sessionId, CancellationToken ct = default) =>
        (await Connection.RequestAsync<ServerMessages.GameReply>(
            id => new ClientMessages.GetGame(id, sessionId), RequestTimeout, ct).ConfigureAwait(false)).Game;

    /// <summary>Creates a new game in the lobby stage.</summary>
    public async Task<GameListing> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default) =>
        RequireGame(await Connection.RequestAsync<ServerMessages.GameReply>(
            id => new ClientMessages.CreateGame(id, request), RequestTimeout, ct).ConfigureAwait(false));

    /// <summary>Creates a game from a save on the server (a file name in its save directory).</summary>
    public async Task<GameListing> LoadGameAsync(string name, string savePath, CancellationToken ct = default) =>
        RequireGame(await Connection.RequestAsync<ServerMessages.GameReply>(
            id => new ClientMessages.LoadGame(id, name, savePath), RequestTimeout, ct).ConfigureAwait(false));

    /// <summary>Removes a game this client created or hosts. Returns <c>false</c> if it no longer exists.</summary>
    public Task<bool> RemoveGameAsync(string sessionId, CancellationToken ct = default) =>
        Connection.TryRequestAsync(
            id => new ClientMessages.RemoveGame(id, sessionId), WireErrorCode.UnknownSession, RequestTimeout, ct);

    /// <summary>
    /// Joins a game in the lobby stage with the locally computed content hash,
    /// leaving any current game. The first player to join becomes host.
    /// Throws <see cref="ContentMismatchException"/> if the content differs.
    /// </summary>
    public async Task<SessionClient> JoinGameAsync(string sessionId, string contentHash, CancellationToken ct = default)
    {
        await Connection.RequestAsync<ServerMessages.OkReply>(
            id => new ClientMessages.JoinGame(id, sessionId, contentHash), RequestTimeout, ct).ConfigureAwait(false);
        return new SessionClient(this, sessionId);
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    internal void Register(SessionSubscription subscription) =>
        _subscriptions[subscription.SessionId] = subscription;

    internal void Unregister(SessionSubscription subscription) =>
        _subscriptions.TryRemove(KeyValuePair.Create(subscription.SessionId, subscription));

    private void OnPush(ServerMessage message)
    {
        switch (message)
        {
            case ServerMessages.StreamUpdatePush push:
                Find(push.Update.SessionId)?.OnUpdate(push.Update);
                Connection.Send(new ClientMessages.AckUpdate(push.Update.SessionId, push.Update.Sequence));
                break;
            case ServerMessages.StreamSnapshotPush push:
                Find(push.Snapshot.SessionId)?.OnSnapshot(push.Snapshot);
                Connection.Send(new ClientMessages.AckUpdate(push.Snapshot.SessionId, push.Snapshot.Sequence));
                break;
            case ServerMessages.SessionEndedPush push:
                Find(push.SessionId)?.OnEnded(push.Reason);
                break;
        }
    }

    private SessionSubscription? Find(string sessionId) =>
        _subscriptions.TryGetValue(sessionId, out var subscription) ? subscription : null;

    private static GameListing RequireGame(ServerMessages.GameReply reply) =>
        reply.Game ?? throw new InvalidDataException("The server did not return the created game.");
}
