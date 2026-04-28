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
    /// Display name used for diagnostics and batch ordering tiebreaking.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Stable save-file identifier for PRNG stream forking. Must be unique
    /// and non-blank within a <see cref="SimulationEngine"/>. Changing this
    /// value invalidates existing saves; rename <see cref="Name"/> instead
    /// for display-only changes.
    /// </summary>
    string Key { get; }

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
