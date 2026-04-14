namespace SimEngine.Time;

/// <summary>
/// How often a simulation system runs relative to the real calendar.
/// The engine compares the previous and current tick instants to decide
/// whether a cadence has fired during a given tick.
/// </summary>
public enum TickCadence
{
    EveryTick = 0,
    Daily,
    Weekly,
    Monthly,
    Quarterly,
    Yearly,
}
