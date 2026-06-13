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
