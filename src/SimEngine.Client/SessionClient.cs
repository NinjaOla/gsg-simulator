using Akka.Actor;
using SimEngine.Contracts;

namespace SimEngine.Client;

/// <summary>
/// Client-side handle for a single game session. Wraps the server's session
/// entity host (<see cref="_sessions"/>) and a fixed <see cref="SessionId"/>,
/// translating calls into <see cref="SessionProtocol"/> Ask requests. The
/// session actor is the sole simulation authority; this handle only sends
/// messages and awaits replies.
/// </summary>
public sealed class SessionClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly IActorRef _sessions;

    /// <summary>The session this handle targets.</summary>
    public string SessionId { get; }

    /// <summary>Creates a handle for <paramref name="sessionId"/> routed through the session host.</summary>
    public SessionClient(IActorRef sessions, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessions = sessions;
        SessionId = sessionId;
    }

    /// <summary>Initializes a new game and returns its session info.</summary>
    public Task<SessionInfo> InitializeAsync(string worldId, DateTimeOffset startDate, ulong seed, CancellationToken ct = default) =>
        _sessions.Ask<SessionInfo>(new SessionProtocol.Initialize(SessionId, worldId, startDate, seed), DefaultTimeout, ct);

    /// <summary>Initializes the session from a save file on the server.</summary>
    public Task<SessionInfo> InitializeFromSaveAsync(string savePath, CancellationToken ct = default) =>
        _sessions.Ask<SessionInfo>(new SessionProtocol.InitializeFromSave(SessionId, savePath), DefaultTimeout, ct);

    /// <summary>Queues a single command for the next advance.</summary>
    public Task EnqueueCommandAsync(PlayerCommand command, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.EnqueueCommand(SessionId, command), DefaultTimeout, ct);

    /// <summary>Queues a batch of commands in order.</summary>
    public Task EnqueueCommandsAsync(PlayerCommand[] commands, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.EnqueueCommands(SessionId, commands), DefaultTimeout, ct);

    /// <summary>Applies queued commands then advances the engine by <paramref name="ticks"/>.</summary>
    public Task<TickResult> AdvanceAsync(int ticks, CancellationToken ct = default) =>
        _sessions.Ask<TickResult>(new SessionProtocol.Advance(SessionId, ticks), DefaultTimeout, ct);

    /// <summary>Gets the full synced baseline snapshot.</summary>
    public Task<SessionSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        _sessions.Ask<SessionSnapshot>(new SessionProtocol.GetSnapshot(SessionId), DefaultTimeout, ct);

    /// <summary>Gets the ordered session members.</summary>
    public Task<string[]> GetPlayersAsync(CancellationToken ct = default) =>
        _sessions.Ask<string[]>(new SessionProtocol.GetPlayers(SessionId), DefaultTimeout, ct);

    /// <summary>Saves the engine snapshot and returns the resolved path.</summary>
    public Task<string> SaveAsync(string savePath, CancellationToken ct = default) =>
        _sessions.Ask<string>(new SessionProtocol.Save(SessionId, savePath), DefaultTimeout, ct);

    /// <summary>Subscribes an actor to per-tick <see cref="SessionStreamUpdate"/> broadcasts.</summary>
    public Task SubscribeAsync(IActorRef subscriber, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Subscribe(SessionId, subscriber), DefaultTimeout, ct);

    /// <summary>Unsubscribes a previously subscribed actor.</summary>
    public Task UnsubscribeAsync(IActorRef subscriber, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Unsubscribe(SessionId, subscriber), DefaultTimeout, ct);

    /// <summary>Ends the session and stops the actor.</summary>
    public Task ShutdownAsync(CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Shutdown(SessionId), DefaultTimeout, ct);
}
