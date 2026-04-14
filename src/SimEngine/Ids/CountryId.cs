namespace SimEngine.Ids;

public readonly record struct CountryId(uint Value) : IComparable<CountryId>
{
    public static CountryId None => default;

    public int CompareTo(CountryId other) => Value.CompareTo(other.Value);

    public static bool operator <(CountryId left, CountryId right) => left.CompareTo(right) < 0;
    public static bool operator <=(CountryId left, CountryId right) => left.CompareTo(right) <= 0;
    public static bool operator >(CountryId left, CountryId right) => left.CompareTo(right) > 0;
    public static bool operator >=(CountryId left, CountryId right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"C#{Value}";
}
