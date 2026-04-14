using SimEngine.Time;

namespace SimEngine.Systems;

/// <summary>
/// A unit of simulation logic that runs on a fixed cadence. Systems declare
/// their read/write state sets up front so the engine can build a dependency
/// graph and schedule them into parallel batches.
/// </summary>
public interface ISimulationSystem
{
    /// <summary>
    /// Stable identifier used for ordering, diagnostics, and deterministic
    /// PRNG forking. Must be unique within a <see cref="SimulationEngine"/>.
    /// </summary>
    string Name { get; }

    TickCadence Cadence { get; }

    /// <summary>
    /// Tiebreaker within a parallel batch. Lower runs first. Systems with the
    /// same <see cref="Order"/> fall back to <see cref="Name"/> (ordinal).
    /// </summary>
    int Order { get; }

    IReadOnlyCollection<StateKey> Reads { get; }
    IReadOnlyCollection<StateKey> Writes { get; }

    /// <summary>
    /// Execute one tick's worth of work. Takes the context by <c>in</c> to
    /// avoid defensive copies and tick-time allocations.
    /// </summary>
    void Execute(in SimulationContext ctx);
}
