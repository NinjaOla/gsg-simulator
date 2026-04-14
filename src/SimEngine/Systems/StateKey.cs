namespace SimEngine.Systems;

/// <summary>
/// A typed tag identifying a slice of simulation state that systems read
/// from or write to. Systems declare their <see cref="ISimulationSystem.Reads"/>
/// and <see cref="ISimulationSystem.Writes"/> as sets of <see cref="StateKey"/>
/// values; the dependency graph derives scheduling order from those sets.
///
/// A string-backed struct is the Phase 0 choice (vs. <see cref="Type"/>)
/// because concrete component types don't exist yet, and it keeps cross-
/// cutting keys like <c>"core/population"</c> expressible. Construction
/// should be centralized into static readonly constants per game module
/// to avoid typos.
/// </summary>
public readonly record struct StateKey(string Name) : IComparable<StateKey>
{
    public int CompareTo(StateKey other) => string.CompareOrdinal(Name, other.Name);

    public static bool operator <(StateKey left, StateKey right) => left.CompareTo(right) < 0;
    public static bool operator <=(StateKey left, StateKey right) => left.CompareTo(right) <= 0;
    public static bool operator >(StateKey left, StateKey right) => left.CompareTo(right) > 0;
    public static bool operator >=(StateKey left, StateKey right) => left.CompareTo(right) >= 0;

    public override string ToString() => Name;
}
