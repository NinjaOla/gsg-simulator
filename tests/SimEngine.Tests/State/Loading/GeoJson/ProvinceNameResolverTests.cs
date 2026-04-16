using NetTopologySuite.Features;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Tests.State.Loading.GeoJson;

public sealed class ProvinceNameResolverTests
{
    [Fact]
    public void Resolve_PrefersNameOverEverything()
    {
        var attrs = new AttributesTable
        {
            { "name", "Bavaria" },
            { "name_en", "Bayern (English)" },
            { "name_alt", "BAY" },
            { "adm1_code", "DE-BY" },
        };
        Assert.Equal("Bavaria", ProvinceNameResolver.Resolve(attrs, 0));
    }

    [Fact]
    public void Resolve_FallsBackThroughChain()
    {
        var attrs = new AttributesTable
        {
            { "name", null },
            { "name_en", "" },
            { "name_alt", "Bay-Alt" },
            { "adm1_code", "DE-BY" },
        };
        Assert.Equal("Bay-Alt", ProvinceNameResolver.Resolve(attrs, 7));
    }

    [Fact]
    public void Resolve_FallsBackToAdm1Code()
    {
        var attrs = new AttributesTable
        {
            { "name", null },
            { "name_en", null },
            { "name_alt", null },
            { "adm1_code", "DE-BY" },
        };
        Assert.Equal("DE-BY", ProvinceNameResolver.Resolve(attrs, 0));
    }

    [Fact]
    public void Resolve_AllNullOrMissing_Throws()
    {
        var attrs = new AttributesTable
        {
            { "name", null },
        };
        var ex = Assert.Throws<InvalidDataException>(() => ProvinceNameResolver.Resolve(attrs, 42));
        Assert.Contains("index 42", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_NullAttributes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProvinceNameResolver.Resolve(null!, 0));
    }
}
