namespace SimEngine.Contracts;

/// <summary>
/// Result returned after a game session processes a tick.
/// </summary>
[GenerateSerializer]
public sealed record TickResult
{
    /// <summary>The tick number after the step completed.</summary>
    [Id(0)]
    public long TickNumber { get; init; }

    /// <summary>The simulation date after the step completed.</summary>
    [Id(1)]
    public DateTimeOffset CurrentDate { get; init; }

    /// <summary>Number of ticks actually executed (may differ from requested if paused).</summary>
    [Id(2)]
    public int TicksExecuted { get; init; }
}
