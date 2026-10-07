using SimEngine.Contracts;
using SimEngine.Contracts.Wire;

namespace SimEngine.Client;

/// <summary>
/// Handle for the session this client has joined. Every request is sent as
/// the connection's player; the session actor stays the sole simulation authority.
/// </summary>
public sealed class SessionClient
{
    internal SessionClient(GameClient client, string sessionId)
    {
        Client = client;
        SessionId = sessionId;
    }

    public string SessionId { get; }

    /// <summary>The local player's id.</summary>
    public string PlayerId => Client.PlayerId;

    internal GameClient Client { get; }

    /// <summary>Sends a command; throws <see cref="CommandRejectedException"/> if the session rejects it.</summary>
    public Task SendCommandAsync(PlayerCommand command, CancellationToken ct = default) =>
        SendCommandsAsync([command], ct);

    /// <summary>Sends commands in order, stopping at the first rejection.</summary>
    public Task SendCommandsAsync(PlayerCommand[] commands, CancellationToken ct = default) =>
        Ok(id => new ClientMessages.SendCommands(id, SessionId, commands), ct);

    /// <summary>Host only: starts the game from the lobby, optionally paused.</summary>
    public Task StartGameAsync(bool paused = false, CancellationToken ct = default) =>
        Ok(id => new ClientMessages.StartGame(id, SessionId, paused), ct);

    /// <summary>Host only, lobby only: replaces the game settings.</summary>
    public Task UpdateSettingsAsync(GameSettings settings, CancellationToken ct = default) =>
        Ok(id => new ClientMessages.UpdateSettings(id, SessionId, settings), ct);

    /// <summary>Debug: runs ticks immediately (paused games with debug commands enabled).</summary>
    public async Task<TickResult> DebugStepAsync(int ticks, CancellationToken ct = default) =>
        (await Client.Connection.RequestAsync<ServerMessages.TickReply>(
            id => new ClientMessages.DebugStep(id, SessionId, ticks), GameClient.RequestTimeout, ct).ConfigureAwait(false)).Result;

    /// <summary>Gets stage, speed, host, settings, and players.</summary>
    public async Task<SessionStatus> GetStatusAsync(CancellationToken ct = default) =>
        (await Client.Connection.RequestAsync<ServerMessages.StatusReply>(
            id => new ClientMessages.GetStatus(id, SessionId), GameClient.RequestTimeout, ct).ConfigureAwait(false)).Status;

    /// <summary>Host only: saves on the server and returns the saved name (or full path on a local server).</summary>
    public async Task<string> SaveAsync(string savePath, CancellationToken ct = default) =>
        (await Client.Connection.RequestAsync<ServerMessages.SavedReply>(
            id => new ClientMessages.SaveGame(id, SessionId, savePath), GameClient.RequestTimeout, ct).ConfigureAwait(false)).Path;

    /// <summary>Leaves the session (no-op if already gone). The host leaving ends the session.</summary>
    public Task LeaveAsync(CancellationToken ct = default) =>
        Client.Connection.TryRequestAsync(
            id => new ClientMessages.LeaveGame(id, SessionId), WireErrorCode.NotJoined, GameClient.RequestTimeout, ct);

    /// <summary>Subscribes and returns a live, self-updating read model.</summary>
    public Task<SessionSubscription> SubscribeAsync(CancellationToken ct = default) =>
        SessionSubscription.StartAsync(this, ct);

    private Task<ServerMessages.OkReply> Ok(Func<long, ClientRequest> create, CancellationToken ct) =>
        Client.Connection.RequestAsync<ServerMessages.OkReply>(create, GameClient.RequestTimeout, ct);
}
