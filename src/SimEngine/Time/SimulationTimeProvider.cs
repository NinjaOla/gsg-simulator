namespace SimEngine.Time;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock is advanced manually by the
/// simulation engine. Sealed so mods cannot silently replace time.
/// </summary>
public sealed class SimulationTimeProvider : TimeProvider
{
    private DateTimeOffset _currentTime;
    private DateTimeOffset _previousTick;

    public SimulationTimeProvider(DateTimeOffset startDate)
    {
        _currentTime = startDate;
        _previousTick = startDate;
    }

    public override DateTimeOffset GetUtcNow() => _currentTime;

    /// <summary>The current simulation instant.</summary>
    public DateTimeOffset Current => _currentTime;

    /// <summary>
    /// The instant at the start of the most recently committed tick.
    /// Equal to <see cref="Current"/> before the first tick runs.
    /// </summary>
    public DateTimeOffset PreviousTick => _previousTick;

    public void Advance(TimeSpan delta) => _currentTime += delta;

    public void AdvanceMonths(int months) => _currentTime = _currentTime.AddMonths(months);

    public void AdvanceYears(int years) => _currentTime = _currentTime.AddYears(years);

    /// <summary>
    /// Records the start-of-tick instant so that <see cref="PreviousTick"/>
    /// reflects the window that just completed. Called by the engine.
    /// </summary>
    internal void CommitTick(DateTimeOffset previous) => _previousTick = previous;

    internal void Restore(DateTimeOffset currentTime, DateTimeOffset previousTick)
    {
        if (previousTick > currentTime)
        {
            throw new ArgumentOutOfRangeException(nameof(previousTick), "previousTick cannot be later than currentTime.");
        }

        _currentTime = currentTime;
        _previousTick = previousTick;
    }

    /// <summary>Engine-internal rollback used when a tick is cancelled mid-flight.</summary>
    internal void RollbackTo(DateTimeOffset instant) => _currentTime = instant;
}
