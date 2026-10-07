namespace SimEngine.Server;

/// <summary>
/// Server-side runtime tuning for game sessions: wall-clock tick pacing per
/// speed level and per-subscriber backlog limits. Injectable so tests can use
/// short intervals.
/// </summary>
public sealed record SessionRuntimeOptions
{
    /// <summary>Production defaults: speed 1 = 5 s, 2 = 2 s, 3 = 1 s, 4 = 0.25 s, 5 = no wait.</summary>
    public static SessionRuntimeOptions Default { get; } = new();

    /// <summary>
    /// Minimum wall-clock time per tick, indexed by speed level − 1. The next
    /// tick starts at <c>max(previousStart + interval, previousFinish)</c>.
    /// </summary>
    public IReadOnlyList<TimeSpan> SpeedIntervals { get; init; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.Zero,
    ];

    /// <summary>
    /// Updates a subscriber may have unacked before further updates are held
    /// back and later merged into one catch-up update.
    /// </summary>
    public int MaxUnackedUpdates { get; init; } = 10;

    /// <summary>
    /// Held-back updates beyond which the backlog is dropped and the subscriber
    /// is resynced with a fresh snapshot instead.
    /// </summary>
    public int MaxHeldUpdates { get; init; } = 500;

    /// <summary>Gets the tick interval for a speed level.</summary>
    public TimeSpan GetInterval(int speed) => SpeedIntervals[speed - 1];
}
