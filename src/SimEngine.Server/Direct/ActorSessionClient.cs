using Akka.Actor;
using SimEngine.Contracts;

namespace SimEngine.Server.Direct;

/// <summary>
/// Trusted, Akka-level handle for one session (see <see cref="ActorGameClient"/>).
/// Translates calls into <see cref="SessionProtocol"/> asks.
/// </summary>
public sealed class ActorSessionClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly IActorRef _sessions;

    public ActorSessionClient(IActorRef sessions, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessions = sessions;
        SessionId = sessionId;
    }

    public string SessionId { get; }

    public Task<SessionInfo> InitializeAsync(string worldId, DateTimeOffset startDate, ulong seed, CancellationToken ct = default) =>
        _sessions.Ask<SessionInfo>(new SessionProtocol.Initialize(SessionId, worldId, startDate, seed), DefaultTimeout, ct);

    public Task<SessionInfo> InitializeFromSaveAsync(string savePath, CancellationToken ct = default) =>
        _sessions.Ask<SessionInfo>(new SessionProtocol.InitializeFromSave(SessionId, savePath), DefaultTimeout, ct);

    public Task SendCommandAsync(string playerId, PlayerCommand command, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.EnqueueCommand(SessionId, playerId, command), DefaultTimeout, ct);

    public Task SendCommandsAsync(string playerId, PlayerCommand[] commands, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.EnqueueCommands(SessionId, playerId, commands), DefaultTimeout, ct);

    public Task StartGameAsync(string playerId, bool paused = false, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.StartGame(SessionId, playerId, paused), DefaultTimeout, ct);

    public Task UpdateSettingsAsync(string playerId, GameSettings settings, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.UpdateSettings(SessionId, playerId, settings), DefaultTimeout, ct);

    public Task<TickResult> DebugStepAsync(string playerId, int ticks, CancellationToken ct = default) =>
        _sessions.Ask<TickResult>(new SessionProtocol.DebugStep(SessionId, playerId, ticks), DefaultTimeout, ct);

    public Task<SessionStatus> GetStatusAsync(CancellationToken ct = default) =>
        _sessions.Ask<SessionStatus>(new SessionProtocol.GetStatus(SessionId), DefaultTimeout, ct);

    public Task<SessionSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        _sessions.Ask<SessionSnapshot>(new SessionProtocol.GetSnapshot(SessionId), DefaultTimeout, ct);

    public Task<string[]> GetPlayersAsync(CancellationToken ct = default) =>
        _sessions.Ask<string[]>(new SessionProtocol.GetPlayers(SessionId), DefaultTimeout, ct);

    public Task JoinAsync(string playerId, string contentHash, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Join(SessionId, playerId, contentHash), DefaultTimeout, ct);

    public Task LeaveAsync(string playerId, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Leave(SessionId, playerId), DefaultTimeout, ct);

    public Task<string> SaveAsync(string savePath, CancellationToken ct = default) =>
        _sessions.Ask<string>(new SessionProtocol.Save(SessionId, savePath), DefaultTimeout, ct);

    public Task SubscribeAsync(IActorRef subscriber, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Subscribe(SessionId, subscriber), DefaultTimeout, ct);

    public Task UnsubscribeAsync(IActorRef subscriber, CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Unsubscribe(SessionId, subscriber), DefaultTimeout, ct);

    public Task ShutdownAsync(CancellationToken ct = default) =>
        _sessions.Ask<Ack>(new SessionProtocol.Shutdown(SessionId), DefaultTimeout, ct);
}
