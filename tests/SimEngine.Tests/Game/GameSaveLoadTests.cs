using System.Text;
using SimEngine.Game.Components;
using SimEngine.Game.Seeding;
using SimEngine.Game.Serialization;
using SimEngine.Game.Systems;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.State.Serialization;
using SimEngine.Systems;
using Xunit;

namespace SimEngine.Tests.Game;

public sealed class GameSaveLoadTests
{
    private static readonly DateTimeOffset StartDate = new(1836, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SaveLoad_RoundTripsGameComponents()
    {
        var engine = CreateGameEngine();
        engine.StepMany(31); // fire monthly systems once

        var json = SaveToJson(engine);
        var loaded = LoadFromJson(json);

        Assert.Equal(json, SaveToJson(loaded));
        Assert.Equal(engine.TickNumber, loaded.TickNumber);

        var (_, treasury) = loaded.State.Entities.Query<TreasuryComponent>().Single();
        Assert.True(treasury.FundsE2 > 0, "Treasury should have accumulated income.");

        var (_, pop) = loaded.State.Entities.Query<PopulationComponent>().First();
        Assert.True(pop.Population > 1_000_000, "Population should have grown.");
    }

    [Fact]
    public void SaveLoad_ResumeIsDeterministic()
    {
        var continued = CreateGameEngine();
        continued.StepMany(31 * 2);

        var saveJson = SaveToJson(continued);

        continued.StepMany(31 * 3);
        var continuedJson = SaveToJson(continued);

        var resumed = LoadFromJson(saveJson);
        resumed.StepMany(31 * 3);
        var resumedJson = SaveToJson(resumed);

        Assert.Equal(continuedJson, resumedJson);
    }

    [Fact]
    public void Load_UnknownGameSection_WithoutCodecs_ThrowsInvalidDataException()
    {
        var engine = CreateGameEngine();
        var json = SaveToJson(engine);

        // Load without game codecs — should fail because game sections are unrecognised.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(stream, CreateSystems()));
    }

    [Fact]
    public void Load_WithGameCodecs_Succeeds()
    {
        var engine = CreateGameEngine();
        var json = SaveToJson(engine);

        var loaded = LoadFromJson(json);
        Assert.Equal(engine.TickNumber, loaded.TickNumber);
    }

    private static SimulationEngine CreateGameEngine()
    {
        var state = new SimulationState();

        var province = state.Entities.Create();
        state.Entities.Attach(province, new ProvinceComponent("Alpha", Terrain.Land, 0, 0));

        var adjacencyBuilder = new AdjacencyGraph.Builder();
        adjacencyBuilder.AddProvince(ProvinceId.OfEntity(province));
        state.Adjacency = adjacencyBuilder.Build();

        GameWorldSeeder.Seed(state);

        return new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = StartDate,
                Seed = 42,
                DefaultTickDelta = TimeSpan.FromDays(1),
                EnableParallelBatches = false,
                InitialState = state,
                ComponentCodecs = GameCodecs.All,
            },
            CreateSystems());
    }

    private static ISimulationSystem[] CreateSystems() =>
        [new PopulationSystem(), new EconomySystem()];

    private static string SaveToJson(SimulationEngine engine)
    {
        using var stream = new MemoryStream();
        engine.Save(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static SimulationEngine LoadFromJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return SimulationEngine.Load(stream, CreateSystems(), GameCodecs.All);
    }
}
