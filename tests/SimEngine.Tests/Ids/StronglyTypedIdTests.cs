using SimEngine.Ids;
using Xunit;

namespace SimEngine.Tests.Ids;

public sealed class StronglyTypedIdTests
{
    [Fact]
    public void ProvinceId_ValueEquality()
    {
        Assert.Equal(new ProvinceId(7), new ProvinceId(7));
        Assert.NotEqual(new ProvinceId(7), new ProvinceId(8));
    }

    [Fact]
    public void ProvinceId_CompareTo_ProducesSortedOrder()
    {
        var ids = new[] { new ProvinceId(3), new ProvinceId(1), new ProvinceId(2) };
        Array.Sort(ids);
        Assert.Equal(new[] { new ProvinceId(1), new ProvinceId(2), new ProvinceId(3) }, ids);
    }

    [Fact]
    public void ProvinceId_ComparisonOperators()
    {
        var a = new ProvinceId(1);
        var b = new ProvinceId(2);
        var aCopy = new ProvinceId(1);
        Assert.True(a < b);
        Assert.True(a <= b);
        Assert.True(b > a);
        Assert.True(b >= a);
        Assert.True(a <= aCopy);
        Assert.True(a >= aCopy);
    }

    [Fact]
    public void None_EqualsDefault()
    {
        Assert.Equal(default, ProvinceId.None);
        Assert.Equal(default, CountryId.None);
        Assert.Equal(default, EntityId.None);
    }

    [Fact]
    public void DifferentIdTypes_AreNotInterchangeable()
    {
        // ProvinceId and CountryId with the same underlying value are distinct
        // types — this is the whole point of strongly-typed ids. We can't
        // assert "fails to compile" at runtime, but we can assert the runtime
        // types differ.
        object p = new ProvinceId(1);
        object c = new CountryId(1);
        Assert.NotEqual(p.GetType(), c.GetType());
        Assert.NotEqual(p, c);
    }

    [Fact]
    public void ToString_IncludesTypePrefix()
    {
        Assert.Equal("P#42", new ProvinceId(42).ToString());
        Assert.Equal("C#42", new CountryId(42).ToString());
        Assert.Equal("E#42", new EntityId(42).ToString());
    }
}


