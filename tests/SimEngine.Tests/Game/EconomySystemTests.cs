using SimEngine.Game.Components;
using SimEngine.Game.Systems;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.Systems;
using Xunit;

namespace SimEngine.Tests.Game;

public sealed class EconomySystemTests
{
    private static readonly DateTimeOffset StartDate = new(1836, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TreasuryAccumulates_SumOfProvinceProduction()
    {
        const int prod1 = 150; // 1.50/mo
        const int prod2 = 250; // 2.50/mo
        const int expectedIncome = prod1 + prod2;

        var (engine, _) = CreateEngine([prod1, prod2]);

        // Advance 31 days to fire the monthly EconomySystem once.
        engine.StepMany(31);

        var (_, treasury) = engine.State.Entities.Query<TreasuryComponent>().Single();
        Assert.Equal(expectedIncome, treasury.FundsE2);
    }

    [Fact]
    public void TreasuryAccumulates_OverMultipleMonths()
    {
        const int prod = 100;
        var (engine, _) = CreateEngine([prod]);

        // 3 months = 3 × 31 daily ticks (crosses 3 month boundaries).
        engine.StepMany(31 * 3);

        var (_, treasury) = engine.State.Entities.Query<TreasuryComponent>().Single();
        Assert.Equal(prod * 3L, treasury.FundsE2);
    }

    [Fact]
    public void NoOwnedProvinces_TreasuryStaysZero()
    {
        var state = new SimulationState();
        var country = state.Entities.Create();
        state.Entities.Attach(country, new CountryComponent("TST", "Test"));
        state.Entities.Attach(country, new TreasuryComponent(0L));

        var engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = StartDate,
                Seed = 1,
                DefaultTickDelta = TimeSpan.FromDays(1),
                EnableParallelBatches = false,
                InitialState = state,
            },
            [new EconomySystem()]);

        engine.StepMany(31);

        engine.State.Entities.TryGet<TreasuryComponent>(country, out var treasury);
        Assert.Equal(0L, treasury.FundsE2);
    }

    private static (SimulationEngine engine, EntityId countryId) CreateEngine(int[] productions)
    {
        var state = new SimulationState();

        var country = state.Entities.Create();
        state.Entities.Attach(country, new CountryComponent("DEU", "Germany"));
        state.Entities.Attach(country, new TreasuryComponent(0L));

        var adjacencyBuilder = new AdjacencyGraph.Builder();
        foreach (var prod in productions)
        {
            var province = state.Entities.Create();
            state.Entities.Attach(province, new ProvinceComponent("P", Terrain.Land, 0, 0));
            state.Entities.Attach(province, new EconomyComponent(prod));
            state.Relationships.Add(country, RelationshipLabel.Owns, province);
            adjacencyBuilder.AddProvince(ProvinceId.OfEntity(province));
        }

        state.Adjacency = adjacencyBuilder.Build();

        var engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = StartDate,
                Seed = 1,
                DefaultTickDelta = TimeSpan.FromDays(1),
                EnableParallelBatches = false,
                InitialState = state,
            },
            [new EconomySystem()]);

        return (engine, country);
    }
}
