namespace SimEngine.Contracts;

/// <summary>
/// Grain interface for a game session. Each session manages one
/// <c>SimulationEngine</c> instance and accepts commands from connected players.
/// The grain key is the session/game ID (string).
/// </summary>
public interface IGameSessionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initializes the session with the given world name, start date, and seed.
    /// Must be called once before <see cref="StepAsync"/>.
    /// </summary>
    Task InitializeAsync(string worldName, DateTimeOffset startDate, ulong seed);

    /// <summary>
    /// Queues a player command to be applied on the next step.
    /// </summary>
    Task EnqueueCommandAsync(PlayerCommand command);

    /// <summary>
    /// Advances the simulation by one tick, applying any queued commands first.
    /// Returns the result of the tick.
    /// </summary>
    Task<TickResult> StepAsync();

    /// <summary>
    /// Gets the current tick number.
    /// </summary>
    Task<long> GetTickNumberAsync();

    /// <summary>
    /// Gets the current simulation date.
    /// </summary>
    Task<DateTimeOffset> GetCurrentDateAsync();
}
