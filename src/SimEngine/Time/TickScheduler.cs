using System.Globalization;

namespace SimEngine.Time;

/// <summary>
/// Pure cadence predicates over a (previous, current) tick window.
///
/// Two deliberate deviations from the design doc, flagged for the doc owner:
///
/// 1. Daily: the doc specifies
///    <c>curr.Day != prev.Day || curr.DayOfYear != prev.DayOfYear</c>, which
///    is redundant — the <c>||</c> never adds a new true case. We implement
///    the clean intent: the calendar date changed.
///
/// 2. Weekly: the doc says "ISOWeek changes". ISO week numbers wrap per year
///    (week 1 exists in every year), so a week-number comparison alone is not
///    sufficient. We compare <c>(ISOWeek.GetYear, GetWeekOfYear)</c> tuples.
/// </summary>
public static class TickScheduler
{
    public static bool ShouldFire(TickCadence cadence, DateTimeOffset previous, DateTimeOffset current)
    {
        if (previous == current)
        {
            // No time passed — nothing can fire. Even EveryTick requires progress.
            return false;
        }

        return cadence switch
        {
            TickCadence.EveryTick => true,
            TickCadence.Daily     => IsDailyBoundary(previous, current),
            TickCadence.Weekly    => IsWeeklyBoundary(previous, current),
            TickCadence.Monthly   => IsMonthlyBoundary(previous, current),
            TickCadence.Quarterly => IsQuarterlyBoundary(previous, current),
            TickCadence.Yearly    => IsYearlyBoundary(previous, current),
            _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unknown TickCadence"),
        };
    }

    public static bool IsDailyBoundary(DateTimeOffset previous, DateTimeOffset current)
        => previous.Date != current.Date;

    public static bool IsWeeklyBoundary(DateTimeOffset previous, DateTimeOffset current)
    {
        var prevDate = previous.Date;
        var currDate = current.Date;
        var prevKey = (ISOWeek.GetYear(prevDate), ISOWeek.GetWeekOfYear(prevDate));
        var currKey = (ISOWeek.GetYear(currDate), ISOWeek.GetWeekOfYear(currDate));
        return prevKey != currKey;
    }

    public static bool IsMonthlyBoundary(DateTimeOffset previous, DateTimeOffset current)
        => previous.Year != current.Year || previous.Month != current.Month;

    public static bool IsQuarterlyBoundary(DateTimeOffset previous, DateTimeOffset current)
        => previous.Year != current.Year || GetQuarter(previous) != GetQuarter(current);

    public static bool IsYearlyBoundary(DateTimeOffset previous, DateTimeOffset current)
        => previous.Year != current.Year;

    /// <summary>Returns the calendar quarter (1..4) for a given instant.</summary>
    public static int GetQuarter(DateTimeOffset dto) => ((dto.Month - 1) / 3) + 1;
}
