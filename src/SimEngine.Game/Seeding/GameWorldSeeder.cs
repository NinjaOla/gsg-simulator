using SimEngine.Game.Components;
using SimEngine.State;

namespace SimEngine.Game.Seeding;

/// <summary>
/// Seeds game-layer state (country, population, economy) onto a world that
/// has already been loaded into a <see cref="SimulationState"/> by the engine's
/// geo-JSON loader. All provinces are assigned to a single placeholder country.
/// </summary>
public static class GameWorldSeeder
{
    private const long InitialPopulation = 1_000_000;
    private const int AnnualGrowthRateE6 = 5_000;   // 0.5 %/year
    private const int ProductionE2 = 100;             // 1.00 units/month per province

    public static void Seed(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var country = state.Entities.Create();
        state.Entities.Attach(country, new CountryComponent("DEU", "Germany"));
        state.Entities.Attach(country, new TreasuryComponent(0L));

        foreach (var (provinceId, _) in state.Entities.Query<SimEngine.State.Components.ProvinceComponent>())
        {
            state.Entities.Attach(provinceId, new PopulationComponent(InitialPopulation, AnnualGrowthRateE6));
            state.Entities.Attach(provinceId, new EconomyComponent(ProductionE2));
            state.Relationships.Add(country, RelationshipLabel.Owns, provinceId);
        }
    }
}
