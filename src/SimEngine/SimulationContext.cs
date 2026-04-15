using SimEngine.Events;
using SimEngine.Random;
using SimEngine.State;
using SimEngine.Time;

namespace SimEngine;

/// <summary>
/// Readonly view passed to each system on <c>Execute</c>. Allocated once
/// per tick per batch and copied in by <c>in</c> reference, so tick-time
/// cost is zero after the first use.
/// </summary>
public readonly struct SimulationContext
{
    public SimulationTimeProvider Time { get; init; }

    /// <summary>
    /// Per-system forked PRNG. Each system receives an independent stream
    /// keyed by its <see cref="Systems.ISimulationSystem.Name"/>, so parallel
    /// execution order cannot affect the sequence any individual system sees.
    /// </summary>
    public IDeterministicRandom Random { get; init; }

    public IEventBus Events { get; init; }

    /// <summary>
    /// Mutable world state — entity/component store, relationships, and the
    /// province adjacency graph. Shared across every system in the tick, so
    /// systems must use their declared read/write state keys to stay
    /// cooperative under parallel batching.
    /// </summary>
    public SimulationState State { get; init; }

    public long TickNumber { get; init; }

    public DateTimeOffset TickStart { get; init; }

    public DateTimeOffset TickEnd { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
