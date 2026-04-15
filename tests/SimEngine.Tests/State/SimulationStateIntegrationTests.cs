using SimEngine;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.State.Pathfinding;
using SimEngine.Systems;
using SimEngine.Time;
using Xunit;

namespace SimEngine.Tests.State;

public sealed class SimulationStateIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordingSystem_ObservesEveryProvinceInAscendingOrder()
    {
        // Build a 3x3 grid with one sea province at the center.
        var builder = new WorldBuilder();
        var ids = new ProvinceId[9];
        for (var i = 0; i < 9; i++)
        {
            var terrain = i == 4 ? Terrain.Sea : Terrain.Land;
            ids[i] = builder.AddProvince($"P{i + 1}", terrain);
        }

        ProvinceId At(int x, int y) => ids[(y * 3) + x];
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                if (x + 1 < 3)
                {
                    builder.AddEdge(At(x, y), At(x + 1, y));
                }

                if (y + 1 < 3)
                {
                    builder.AddEdge(At(x, y), At(x, y + 1));
                }
            }
        }

        var state = builder.Build();
        var system = new ProvinceObserverSystem();
        var engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = Start,
                Seed = 42,
                InitialState = state,
            },
            new ISimulationSystem[] { system });

        engine.Step();

        Assert.Single(system.Observations);
        var observed = system.Observations[0];
        Assert.Equal(9, observed.Count);

        // Ascending EntityId iteration — verified by checking ids are monotonically increasing.
        for (var i = 1; i < observed.Count; i++)
        {
            Assert.True(observed[i].Id > observed[i - 1].Id);
        }

        // Center province is sea; all others land.
        Assert.Equal(Terrain.Sea, observed[4].Terrain);
        Assert.All(observed.Where((_, i) => i != 4), row => Assert.Equal(Terrain.Land, row.Terrain));
    }

    [Fact]
    public void AStar_OverStateAdjacency_ReturnsExpectedPath()
    {
        // Straight 4-node chain.
        var builder = new WorldBuilder();
        var a = builder.AddProvince("A");
        var b = builder.AddProvince("B");
        var c = builder.AddProvince("C");
        var d = builder.AddProvince("D");
        builder.AddEdge(a, b);
        builder.AddEdge(b, c);
        builder.AddEdge(c, d);
        var state = builder.Build();

        var result = AStarPathfinder.FindPath(state.Adjacency, a, d, (_, _) => 1);
        Assert.True(result.Found);
        Assert.Equal(new[] { a, b, c, d }, result.Nodes);
        Assert.Equal(3, result.TotalCost);
    }

    [Fact]
    public void EngineWithoutInitialState_ExposesEmptyState()
    {
        var engine = new SimulationEngine(
            new SimulationEngineOptions { StartDate = Start, Seed = 1 },
            Array.Empty<ISimulationSystem>());
        Assert.NotNull(engine.State);
        Assert.Equal(0, engine.State.Entities.Count);
        Assert.Equal(0, engine.State.Adjacency.ProvinceCount);
    }

    [Fact]
    public void SystemSeesSameStateInstanceAsEngine()
    {
        var state = new SimulationState();
        var observer = new StateCaptureSystem();
        var engine = new SimulationEngine(
            new SimulationEngineOptions { StartDate = Start, Seed = 1, InitialState = state },
            new ISimulationSystem[] { observer });
        engine.Step();

        Assert.Same(state, engine.State);
        Assert.Same(state, observer.Observed);
    }

    private sealed class ProvinceObserverSystem : ISimulationSystem
    {
        private readonly List<IReadOnlyList<(EntityId Id, Terrain Terrain)>> _observations = new();

        public string Name => "province-observer";

        public TickCadence Cadence => TickCadence.EveryTick;

        public int Order => 0;

        public IReadOnlyCollection<StateKey> Reads => new[] { ComponentStateKeys.Of<ProvinceComponent>() };

        public IReadOnlyCollection<StateKey> Writes => Array.Empty<StateKey>();

        public IReadOnlyList<IReadOnlyList<(EntityId Id, Terrain Terrain)>> Observations => _observations;

        public void Execute(in SimulationContext ctx)
        {
            var snapshot = ctx.State.Entities.Query<ProvinceComponent>()
                .Select(pair => (pair.Id, pair.Component.Terrain))
                .ToArray();
            _observations.Add(snapshot);
        }
    }

    private sealed class StateCaptureSystem : ISimulationSystem
    {
        public string Name => "state-capture";

        public TickCadence Cadence => TickCadence.EveryTick;

        public int Order => 0;

        public IReadOnlyCollection<StateKey> Reads => Array.Empty<StateKey>();

        public IReadOnlyCollection<StateKey> Writes => Array.Empty<StateKey>();

        public SimulationState? Observed { get; private set; }

        public void Execute(in SimulationContext ctx)
        {
            Observed = ctx.State;
        }
    }
}
