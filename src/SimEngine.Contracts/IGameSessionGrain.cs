namespace SimEngine.Contracts;

/// <summary>
/// Grain interface for a game session. Each session owns one
/// <c>SimulationEngine</c> instance — the engine exists nowhere else, so the
/// server is the single simulation authority. The grain key is the
/// session/game ID (string).
/// </summary>
public interface IGameSessionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initializes a new game: loads the world identified by
    /// <paramref name="worldId"/> (a server catalog id, not a path), seeds the
    /// game layer, and constructs the engine. Must be called exactly once.
    /// </summary>
    Task<SessionInfo> InitializeAsync(string worldId, DateTimeOffset startDate, ulong seed);

    /// <summary>
    /// Initializes the session from a save file on the server's file system.
    /// Must be called exactly once, instead of <see cref="InitializeAsync"/>.
    /// </summary>
    Task<SessionInfo> InitializeFromSaveAsync(string savePath);

    /// <summary>
    /// Queues a player command to be applied at the next step boundary.
    /// </summary>
    Task EnqueueCommandAsync(PlayerCommand command);

    /// <summary>
    /// Queues a batch of player commands (in order) to be applied at the next
    /// step boundary. Used by <c>IPlayerGrain</c> to flush a player's buffer in
    /// a single grain call.
    /// </summary>
    Task EnqueueCommandsAsync(PlayerCommand[] commands);

    /// <summary>
    /// Registers a player as a member of this session. Idempotent. Membership
    /// is independent of engine initialization, so players may join while the
    /// session is still being set up.
    /// <para>
    /// <paramref name="contentHash"/> is the player's locally computed content
    /// hash (world map/geography plus mods). Once the session has loaded its
    /// content, a mismatch is rejected with <see cref="ContentMismatchException"/>
    /// to protect deterministic lockstep; before then there is no authoritative
    /// hash to compare against and the join is accepted as membership only.
    /// </para>
    /// </summary>
    Task JoinAsync(string playerId, string contentHash);

    /// <summary>
    /// Removes a player from this session. No-op if the player is not a member.
    /// </summary>
    Task LeaveAsync(string playerId);

    /// <summary>
    /// Gets the current session members, ordered ascending for determinism.
    /// </summary>
    Task<string[]> GetPlayersAsync();

    /// <summary>
    /// Gets a full baseline of the session's synced read model. A client fetches
    /// this once on connect, then folds in per-tick <c>SessionStreamUpdate</c>
    /// messages to stay current without polling engine state.
    /// </summary>
    Task<SessionSnapshot> GetSnapshotAsync();

    /// <summary>
    /// Applies queued commands, then advances the simulation by up to
    /// <paramref name="ticks"/> ticks (batched server-side; zero when paused).
    /// </summary>
    Task<TickResult> StepAsync(int ticks);

    /// <summary>
    /// Saves the full deterministic engine snapshot to the given path on the
    /// server's file system. Returns the resolved absolute path.
    /// </summary>
    Task<string> SaveAsync(string savePath);

    /// <summary>Gets the current tick number.</summary>
    Task<long> GetTickNumberAsync();

    /// <summary>Gets the current simulation date.</summary>
    Task<DateTimeOffset> GetCurrentDateAsync();

    /// <summary>
    /// Ends the session: releases the engine and deactivates the grain.
    /// </summary>
    Task ShutdownAsync();
}
