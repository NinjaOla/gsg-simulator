using SimEngine.Game.Components;
using SimEngine.Game.Systems;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.Systems;
using Xunit;

namespace SimEngine.Tests.Game;

public sealed class PopulationSystemTests
{
    private static readonly DateTimeOffset StartDate = new(1836, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PopulationGrowth_AfterOneMonthlyTick_IsCorrect()
    {
        // 1 000 000 population × 5 000 growthRateE6 / 12 000 000 = 416 (integer division)
        const long initialPop = 1_000_000;
        const int growthRateE6 = 5_000;
        const long expectedDelta = initialPop * growthRateE6 / 12_000_000L;

        var (engine, provinceId) = CreateEngine(initialPop, growthRateE6);

        // One daily tick does NOT fire monthly cadence.
        // Advance 31 days to cross a month boundary.
        engine.StepMany(31);

        engine.State.Entities.TryGet<PopulationComponent>(provinceId, out var pop);
        Assert.Equal(initialPop + expectedDelta, pop.Population);
    }

    [Fact]
    public void PopulationGrowth_IsDeterministicAcrossNMonths()
    {
        const int months = 12;

        var (engineA, pidA) = CreateEngine(1_000_000, 5_000);
        var (engineB, pidB) = CreateEngine(1_000_000, 5_000);

        // Each tick = 1 day; 31 days per iteration crosses a month boundary.
        engineA.StepMany(31 * months);
        engineB.StepMany(31 * months);

        engineA.State.Entities.TryGet<PopulationComponent>(pidA, out var popA);
        engineB.State.Entities.TryGet<PopulationComponent>(pidB, out var popB);

        Assert.Equal(popA.Population, popB.Population);
    }

    [Fact]
    public void PopulationGrowth_ZeroRate_LeavesPopulationUnchanged()
    {
        const long initialPop = 500_000;
        var (engine, pid) = CreateEngine(initialPop, 0);

        engine.StepMany(31);

        engine.State.Entities.TryGet<PopulationComponent>(pid, out var pop);
        Assert.Equal(initialPop, pop.Population);
    }

    private static (SimulationEngine engine, EntityId provinceId) CreateEngine(long population, int growthRateE6)
    {
        var state = new SimulationState();
        var province = state.Entities.Create();
        state.Entities.Attach(province, new ProvinceComponent("TestProv", Terrain.Land, 0, 0));
        state.Entities.Attach(province, new PopulationComponent(population, growthRateE6));

        var adjacency = new AdjacencyGraph.Builder();
        adjacency.AddProvince(ProvinceId.OfEntity(province));
        state.Adjacency = adjacency.Build();

        var engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = StartDate,
                Seed = 1,
                DefaultTickDelta = TimeSpan.FromDays(1),
                EnableParallelBatches = false,
                InitialState = state,
            },
            [new PopulationSystem()]);

        return (engine, province);
    }
}
