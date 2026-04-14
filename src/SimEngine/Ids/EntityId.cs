namespace SimEngine.Ids;

/// <summary>
/// Strongly-typed identifier for a generic engine entity.
/// Value-typed so comparisons and sorts are allocation-free.
/// <see cref="None"/> (value 0) is the canonical "no entity" sentinel.
/// </summary>
public readonly record struct EntityId(uint Value) : IComparable<EntityId>
{
    public static EntityId None => default;

    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    public static bool operator <(EntityId left, EntityId right) => left.CompareTo(right) < 0;
    public static bool operator <=(EntityId left, EntityId right) => left.CompareTo(right) <= 0;
    public static bool operator >(EntityId left, EntityId right) => left.CompareTo(right) > 0;
    public static bool operator >=(EntityId left, EntityId right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"E#{Value}";
}
