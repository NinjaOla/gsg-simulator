using SimEngine.Game.Components;
using SimEngine.Game.Seeding;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Tests.Game;

public sealed class GameWorldSeederTests
{
    private static string Grid4Path =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "grid4.geojson");

    private static string Grid4CountriesPath =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "grid4.countries.json");

    [Fact]
    public void Seed_FromWorldAndCountries_AppliesPopulationAndOwnership()
    {
        var loader = new GeoJsonWorldLoader();
        using var stream = File.OpenRead(Grid4Path);
        var world = loader.Load(stream);
        var state = WorldLoaders.LoadIntoState(world);

        GameWorldSeeder.Seed(state, world, Grid4CountriesPath);

        Assert.True(state.Entities.TryGet<PopulationComponent>(new ProvinceId(1).AsEntity(), out var p1));
        Assert.True(state.Entities.TryGet<PopulationComponent>(new ProvinceId(2).AsEntity(), out var p2));
        Assert.True(state.Entities.TryGet<PopulationComponent>(new ProvinceId(3).AsEntity(), out var p3));
        Assert.True(state.Entities.TryGet<PopulationComponent>(new ProvinceId(4).AsEntity(), out var p4));
        Assert.Equal(100_000, p1.Population);
        Assert.Equal(200_000, p2.Population);
        Assert.Equal(300_000, p3.Population);
        Assert.Equal(400_000, p4.Population);

        var countries = state.Entities.Query<CountryComponent>().ToArray();
        Assert.Equal(2, countries.Length);

        var alpha = countries.Single(c => c.Component.Tag == "ALP");
        var beta = countries.Single(c => c.Component.Tag == "BET");

        Assert.Equal(
            [new ProvinceId(1).AsEntity(), new ProvinceId(2).AsEntity()],
            state.Relationships.GetOutbound(alpha.Id, RelationshipLabel.Owns).ToArray());
        Assert.Equal(
            [new ProvinceId(3).AsEntity()],
            state.Relationships.GetOutbound(beta.Id, RelationshipLabel.Owns).ToArray());

        Assert.Equal(
            [new ProvinceId(1).AsEntity()],
            state.Relationships.GetOutbound(alpha.Id, RelationshipLabel.HasCapital).ToArray());
        Assert.Equal(
            [new ProvinceId(3).AsEntity()],
            state.Relationships.GetOutbound(beta.Id, RelationshipLabel.HasCapital).ToArray());
    }
}


