namespace SimEngine.Ids;

public readonly record struct ProvinceId(uint Value) : IComparable<ProvinceId>
{
    public static ProvinceId None => default;

    public int CompareTo(ProvinceId other) => Value.CompareTo(other.Value);

    public static bool operator <(ProvinceId left, ProvinceId right) => left.CompareTo(right) < 0;
    public static bool operator <=(ProvinceId left, ProvinceId right) => left.CompareTo(right) <= 0;
    public static bool operator >(ProvinceId left, ProvinceId right) => left.CompareTo(right) > 0;
    public static bool operator >=(ProvinceId left, ProvinceId right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"P#{Value}";
}
