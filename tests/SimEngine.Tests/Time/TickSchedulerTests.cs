using SimEngine.Time;
using Xunit;

namespace SimEngine.Tests.Time;

public sealed class TickSchedulerTests
{
    // ----- Helpers ---------------------------------------------------------

    private static DateTimeOffset Dt(int y, int mo, int d, int h = 0, int mi = 0)
        => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    // ----- EveryTick / sentinel --------------------------------------------

    [Fact]
    public void EveryTick_FiresWhenTimeAdvances()
    {
        Assert.True(TickScheduler.ShouldFire(
            TickCadence.EveryTick,
            Dt(2025, 1, 1),
            Dt(2025, 1, 1, 1)));
    }

    [Fact]
    public void AnyCadence_DoesNotFireWhenNoTimePasses()
    {
        var t = Dt(2025, 6, 15);
        foreach (var cadence in Enum.GetValues<TickCadence>())
        {
            Assert.False(
                TickScheduler.ShouldFire(cadence, t, t),
                $"Cadence {cadence} should not fire when previous == current.");
        }
    }

    // ----- Daily -----------------------------------------------------------

    [Theory]
    [InlineData(2025,  3, 15,  0, 2025,  3, 15,  1, false)] // same day +1h
    [InlineData(2025,  3, 15, 23, 2025,  3, 16,  1, true)]  // midnight crossing
    [InlineData(2024, 12, 31, 23, 2025,  1,  1,  0, true)]  // year rollover
    [InlineData(2024,  2, 28, 12, 2024,  2, 29, 12, true)]  // leap day entry
    [InlineData(2024,  2, 29, 12, 2024,  3,  1, 12, true)]  // leap day exit
    public void Daily_Boundaries(
        int py, int pm, int pd, int ph,
        int cy, int cm, int cd, int ch,
        bool expected)
    {
        Assert.Equal(
            expected,
            TickScheduler.IsDailyBoundary(Dt(py, pm, pd, ph), Dt(cy, cm, cd, ch)));
    }

    // ----- Weekly (ISO) ----------------------------------------------------

    [Fact]
    public void Weekly_SameWeek_DoesNotFire()
    {
        // 2025-03-18 Tue, 2025-03-19 Wed, same ISO week.
        Assert.False(TickScheduler.IsWeeklyBoundary(Dt(2025, 3, 18), Dt(2025, 3, 19)));
    }

    [Fact]
    public void Weekly_SundayToMonday_Fires()
    {
        // 2025-03-23 Sun, 2025-03-24 Mon. ISO week boundary.
        Assert.True(TickScheduler.IsWeeklyBoundary(Dt(2025, 3, 23), Dt(2025, 3, 24)));
    }

    [Fact]
    public void Weekly_CrossYearSameIsoWeek_DoesNotFire()
    {
        // 2024-12-30 is ISO week 1 of 2025; 2024-12-31 is also ISO week 1 of 2025.
        Assert.False(TickScheduler.IsWeeklyBoundary(Dt(2024, 12, 30), Dt(2024, 12, 31)));
    }

    [Fact]
    public void Weekly_CrossYearDifferentIsoYear_Fires()
    {
        // 2023-12-31 Sun is week 52 of ISO-year 2023; 2024-01-01 Mon is week 1 of ISO-year 2024.
        Assert.True(TickScheduler.IsWeeklyBoundary(Dt(2023, 12, 31), Dt(2024, 1, 1)));
    }

    // ----- Monthly ---------------------------------------------------------

    [Theory]
    [InlineData(2025,  1, 30, 2025,  1, 31, false)]
    [InlineData(2025,  1, 31, 2025,  2,  1, true)]
    [InlineData(2024, 12, 31, 2025,  1,  1, true)]
    [InlineData(2024,  2, 28, 2024,  2, 29, false)] // same month (Feb 2024)
    [InlineData(2024,  2, 29, 2024,  3,  1, true)]
    public void Monthly_Boundaries(
        int py, int pm, int pd,
        int cy, int cm, int cd,
        bool expected)
    {
        Assert.Equal(
            expected,
            TickScheduler.IsMonthlyBoundary(Dt(py, pm, pd), Dt(cy, cm, cd)));
    }

    // ----- Quarterly -------------------------------------------------------

    [Theory]
    [InlineData(2025,  2,  1, 2025,  3,  1, false)] // Q1 -> Q1
    [InlineData(2025,  3, 31, 2025,  4,  1, true)]  // Q1 -> Q2
    [InlineData(2025,  6, 30, 2025,  7,  1, true)]  // Q2 -> Q3
    [InlineData(2025,  9, 30, 2025, 10,  1, true)]  // Q3 -> Q4
    [InlineData(2024, 12, 31, 2025,  1,  1, true)]  // Q4 -> Q1 across year
    public void Quarterly_Boundaries(
        int py, int pm, int pd,
        int cy, int cm, int cd,
        bool expected)
    {
        Assert.Equal(
            expected,
            TickScheduler.IsQuarterlyBoundary(Dt(py, pm, pd), Dt(cy, cm, cd)));
    }

    [Theory]
    [InlineData( 1, 1)]
    [InlineData( 2, 1)]
    [InlineData( 3, 1)]
    [InlineData( 4, 2)]
    [InlineData( 5, 2)]
    [InlineData( 6, 2)]
    [InlineData( 7, 3)]
    [InlineData( 8, 3)]
    [InlineData( 9, 3)]
    [InlineData(10, 4)]
    [InlineData(11, 4)]
    [InlineData(12, 4)]
    public void GetQuarter_MapsMonths(int month, int expectedQuarter)
    {
        Assert.Equal(expectedQuarter, TickScheduler.GetQuarter(Dt(2025, month, 1)));
    }

    // ----- Yearly ----------------------------------------------------------

    [Theory]
    [InlineData(2024, 12, 31, 2025,  1,  1, true)]
    [InlineData(2025,  1,  1, 2025, 12, 31, false)]
    public void Yearly_Boundaries(
        int py, int pm, int pd,
        int cy, int cm, int cd,
        bool expected)
    {
        Assert.Equal(
            expected,
            TickScheduler.IsYearlyBoundary(Dt(py, pm, pd), Dt(cy, cm, cd)));
    }

    // ----- ShouldFire dispatch ---------------------------------------------

    [Fact]
    public void ShouldFire_DispatchesToCorrectPredicate()
    {
        // 2024-12-29 Sun (ISO week 52 of 2024)  →  2025-01-02 Thu (ISO week 1 of 2025)
        // Crosses day, ISO week, month, quarter, and year.
        var prev = Dt(2024, 12, 29);
        var curr = Dt(2025, 1, 2);
        Assert.True(TickScheduler.ShouldFire(TickCadence.Daily, prev, curr));
        Assert.True(TickScheduler.ShouldFire(TickCadence.Weekly, prev, curr));
        Assert.True(TickScheduler.ShouldFire(TickCadence.Monthly, prev, curr));
        Assert.True(TickScheduler.ShouldFire(TickCadence.Quarterly, prev, curr));
        Assert.True(TickScheduler.ShouldFire(TickCadence.Yearly, prev, curr));
        Assert.True(TickScheduler.ShouldFire(TickCadence.EveryTick, prev, curr));
    }
}
