namespace SimEngine.Contracts;

/// <summary>
/// Result returned after a game session processes a tick.
/// </summary>
public sealed record TickResult
{
    /// <summary>The tick number after the step completed.</summary>
    public long TickNumber { get; init; }

    /// <summary>The simulation date after the step completed.</summary>
    public DateTimeOffset CurrentDate { get; init; }

    /// <summary>Number of ticks actually executed (may differ from requested if paused).</summary>
    public int TicksExecuted { get; init; }
}
