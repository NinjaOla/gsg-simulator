using SimEngine.Time;
using Xunit;

namespace SimEngine.Tests.Time;

public sealed class SimulationTimeProviderTests
{
    private static readonly DateTimeOffset Start =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_SetsCurrentAndPreviousToStartDate()
    {
        var tp = new SimulationTimeProvider(Start);
        Assert.Equal(Start, tp.Current);
        Assert.Equal(Start, tp.GetUtcNow());
        Assert.Equal(Start, tp.PreviousTick);
    }

    [Fact]
    public void Advance_AddsDeltaToCurrent()
    {
        var tp = new SimulationTimeProvider(Start);
        tp.Advance(TimeSpan.FromHours(5));
        Assert.Equal(Start + TimeSpan.FromHours(5), tp.Current);
    }

    [Fact]
    public void AdvanceMonths_RespectsCalendarBoundaries()
    {
        var tp = new SimulationTimeProvider(new DateTimeOffset(2025, 1, 31, 0, 0, 0, TimeSpan.Zero));
        tp.AdvanceMonths(1);
        // DateTimeOffset.AddMonths clamps to last day of target month.
        Assert.Equal(new DateTimeOffset(2025, 2, 28, 0, 0, 0, TimeSpan.Zero), tp.Current);
    }

    [Fact]
    public void AdvanceYears_Feb29_FallsBackToFeb28InNonLeapYear()
    {
        var tp = new SimulationTimeProvider(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero));
        tp.AdvanceYears(1);
        Assert.Equal(new DateTimeOffset(2025, 2, 28, 0, 0, 0, TimeSpan.Zero), tp.Current);
    }

    [Fact]
    public void CommitTick_UpdatesPreviousTickToProvidedInstant()
    {
        var tp = new SimulationTimeProvider(Start);
        var after = Start + TimeSpan.FromDays(7);
        tp.Advance(TimeSpan.FromDays(7));
        tp.GetType()
            .GetMethod("CommitTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(tp, new object[] { Start });
        Assert.Equal(Start, tp.PreviousTick);
        Assert.Equal(after, tp.Current);
    }

    [Fact]
    public void IsAssignableTo_TimeProvider()
    {
        var tp = new SimulationTimeProvider(Start);
        Assert.IsAssignableFrom<TimeProvider>(tp);
    }
}


