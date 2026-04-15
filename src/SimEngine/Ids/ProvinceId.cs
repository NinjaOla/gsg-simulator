namespace SimEngine.Ids;

public readonly record struct ProvinceId(uint Value) : IComparable<ProvinceId>
{
    public static ProvinceId None => default;

    public int CompareTo(ProvinceId other) => Value.CompareTo(other.Value);

    public static bool operator <(ProvinceId left, ProvinceId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProvinceId left, ProvinceId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProvinceId left, ProvinceId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProvinceId left, ProvinceId right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// View this <see cref="ProvinceId"/> as its matching
    /// <see cref="EntityId"/>. Provinces live in the unified entity store,
    /// and by convention a province's <see cref="ProvinceId"/> and
    /// <see cref="EntityId"/> share the same underlying value.
    /// </summary>
    public EntityId AsEntity() => new(Value);

    /// <summary>
    /// Wrap an <see cref="EntityId"/> as a <see cref="ProvinceId"/> marker.
    /// Callers are responsible for ensuring the entity actually carries a
    /// <c>ProvinceComponent</c>.
    /// </summary>
    public static ProvinceId OfEntity(EntityId id) => new(id.Value);

    public override string ToString() => $"P#{Value}";
}
