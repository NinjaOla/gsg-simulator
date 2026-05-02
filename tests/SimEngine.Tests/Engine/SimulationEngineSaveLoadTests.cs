using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimEngine.Events;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.State.Serialization;
using SimEngine.Systems;
using SimEngine.Time;
using Xunit;

namespace SimEngine.Tests.Engine;

public sealed class SimulationEngineSaveLoadTests
{
    private static readonly DateTimeOffset StartDate = new(1836, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SaveLoad_RoundTripsDeterministicSnapshot()
    {
        var engine = CreateEngine();
        engine.StepMany(3);

        var json = SaveToJson(engine);
        var loaded = LoadFromJson(json);

        Assert.Equal(json, SaveToJson(loaded));
        Assert.Equal(engine.Time.Current, loaded.Time.Current);
        Assert.Equal(engine.Time.PreviousTick, loaded.Time.PreviousTick);
        Assert.Equal(engine.TickNumber, loaded.TickNumber);
        Assert.Equal(engine.Options.StartDate, loaded.Options.StartDate);
    }

    [Fact]
    public void SaveLoad_ResumeProducesIdenticalStateAndEvents()
    {
        var continued = CreateEngine();
        continued.StepMany(4);

        var saveJson = SaveToJson(continued);

        var continuedEvents = new List<string>();
        using var continuedSubscription = continued.Events.Subscribe<MutationEvent>(e => continuedEvents.Add(FormatEvent(e)));
        continued.StepMany(6);
        var continuedJson = SaveToJson(continued);

        var resumed = LoadFromJson(saveJson);
        var resumedEvents = new List<string>();
        using var resumedSubscription = resumed.Events.Subscribe<MutationEvent>(e => resumedEvents.Add(FormatEvent(e)));
        resumed.StepMany(6);
        var resumedJson = SaveToJson(resumed);

        Assert.Equal(continuedEvents, resumedEvents);
        Assert.Equal(continuedJson, resumedJson);
    }

    [Fact]
    public void Load_InvalidJson_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => LoadFromJson("{ not json"));
    }

    [Fact]
    public void Load_UnsupportedFormatVersion_ThrowsInvalidDataException()
    {
        var json = SaveToJson(CreateEngine());
        var invalidJson = ReplaceOnce(json, "\"formatVersion\": 1", "\"formatVersion\": 2");

        Assert.Throws<InvalidDataException>(() => LoadFromJson(invalidJson));
    }

    [Fact]
    public void Load_DanglingRelationship_ThrowsInvalidDataException()
    {
        var json = SaveToJson(CreateEngine());
        var invalidJson = ReplaceOnce(json, "\"toEntityId\": 2", "\"toEntityId\": 999");

        Assert.Throws<InvalidDataException>(() => LoadFromJson(invalidJson));
    }

    [Fact]
    public void Load_AllZeroRootRandom_ThrowsInvalidDataException()
    {
        var json = SaveToJson(CreateEngine());
        var invalidJson = MutateJson(json, root =>
        {
            var rootRandom = root["engine"]!["rootRandom"]!.AsObject();
            rootRandom["s0"] = 0;
            rootRandom["s1"] = 0;
            rootRandom["s2"] = 0;
            rootRandom["s3"] = 0;
        });

        Assert.Throws<InvalidDataException>(() => LoadFromJson(invalidJson));
    }

    [Fact]
    public void Load_UnknownComponentSection_ThrowsInvalidDataException()
    {
        var json = SaveToJson(CreateEngine());
        var invalidJson = ReplaceOnce(
            json,
            "SimEngine.State.Components.ProvinceComponent",
            "SimEngine.State.Components.UnknownComponent");

        Assert.Throws<InvalidDataException>(() => LoadFromJson(invalidJson));
    }

    [Fact]
    public void SaveLoad_RenameDisplayName_StillLoads()
    {
        // Saving with one Name and loading with a different Name (but same Key) must succeed.
        var engine = CreateEngine();
        engine.StepMany(2);
        var json = SaveToJson(engine);

        // Load with a system that has the same Key but a different display Name.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var loaded = SimulationEngine.Load(stream, [new RenamedMutationSystem()]);

        Assert.Equal(engine.TickNumber, loaded.TickNumber);
    }

    [Fact]
    public void SaveLoad_ChangeKey_ThrowsInvalidDataException()
    {
        // Changing the Key (not just the display Name) must invalidate the save.
        var engine = CreateEngine();
        var json = SaveToJson(engine);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(stream, [new DifferentKeyMutationSystem()]));
    }

    [Fact]
    public void SaveLoad_StateSectionCodec_RoundTripsNonComponentState()
    {
        var engine = CreateEngineWithStateSectionCodec(new TestMetadataStateSectionCodec());
        engine.State.Metadata["migrationQueue"] = "A->B";

        var json = SaveToJson(engine);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var loaded = SimulationEngine.Load(
            stream,
            [new MutationSystem()],
            stateSectionCodecs: [new TestMetadataStateSectionCodec()]);

        Assert.True(loaded.State.Metadata.TryGetValue("migrationQueue", out var value));
        Assert.Equal("A->B", value);
        Assert.Equal(json, SaveToJson(loaded));
    }

    [Fact]
    public void Load_MissingRequiredStateSection_ThrowsInvalidDataException()
    {
        var engine = CreateEngineWithStateSectionCodec(new TestMetadataStateSectionCodec());
        var json = SaveToJson(engine);
        var invalidJson = MutateJson(json, root =>
        {
            root["engine"]!["state"]!["stateSections"] = new JsonArray();
        });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));
        Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(
                stream,
                [new MutationSystem()],
                stateSectionCodecs: [new RequiredTestMetadataStateSectionCodec()]));
    }

    [Fact]
    public void Load_SaveMetadataMismatch_ThrowsInvalidDataException()
    {
        var engine = CreateEngineWithMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gameId"] = "simengine.game",
            ["scenarioId"] = "earth",
        });

        var json = SaveToJson(engine);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(
                stream,
                [new MutationSystem()],
                expectedSaveMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["gameId"] = "simengine.game",
                    ["scenarioId"] = "mars",
                }));
    }

    private sealed class RenamedMutationSystem : ISimulationSystem
    {
        // Same Key as MutationSystem, different display Name.
        public string Name => "mutation-renamed";
        public string Key => "mutation";
        public TickCadence Cadence => TickCadence.EveryTick;
        public int Order => 0;
        public IReadOnlyCollection<StateKey> Reads => [];
        public IReadOnlyCollection<StateKey> Writes => [ComponentStateKeys.Of<ProvinceComponent>()];
        public void Execute(in SimulationContext ctx) { }
    }

    private sealed class DifferentKeyMutationSystem : ISimulationSystem
    {
        public string Name => "mutation";
        public string Key => "mutation.v2";
        public TickCadence Cadence => TickCadence.EveryTick;
        public int Order => 0;
        public IReadOnlyCollection<StateKey> Reads => [];
        public IReadOnlyCollection<StateKey> Writes => [ComponentStateKeys.Of<ProvinceComponent>()];
        public void Execute(in SimulationContext ctx) { }
    }

    private static SimulationEngine CreateEngine()
    {
        return CreateEngineCore(
            saveMetadata: new Dictionary<string, string>(StringComparer.Ordinal),
            stateSectionCodecs: []);
    }

    private static SimulationEngine CreateEngineWithStateSectionCodec(IStateSectionCodec codec)
    {
        return CreateEngineCore(
            saveMetadata: new Dictionary<string, string>(StringComparer.Ordinal),
            stateSectionCodecs: [codec]);
    }

    private static SimulationEngine CreateEngineWithMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        return CreateEngineCore(
            saveMetadata: metadata,
            stateSectionCodecs: []);
    }

    private static SimulationEngine CreateEngineCore(
        IReadOnlyDictionary<string, string> saveMetadata,
        IReadOnlyList<IStateSectionCodec> stateSectionCodecs)
    {
        var state = new SimulationState();
        var alpha = state.Entities.Create();
        state.Entities.Attach(alpha, new ProvinceComponent("Alpha", Terrain.Land, 100, 200));

        var beta = state.Entities.Create();
        state.Entities.Attach(beta, new ProvinceComponent("Beta", Terrain.Land, 300, 400));

        state.Relationships.Add(alpha, RelationshipLabel.Owns, beta);

        var adjacency = new AdjacencyGraph.Builder();
        adjacency.AddProvince(ProvinceId.OfEntity(alpha));
        adjacency.AddProvince(ProvinceId.OfEntity(beta));
        adjacency.AddUndirectedEdge(ProvinceId.OfEntity(alpha), ProvinceId.OfEntity(beta));
        state.Adjacency = adjacency.Build();

        return new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = StartDate,
                Seed = 12345,
                DefaultTickDelta = TimeSpan.FromDays(1),
                EnableParallelBatches = false,
                InitialState = state,
                SaveMetadata = saveMetadata,
                StateSectionCodecs = stateSectionCodecs,
            },
            [new MutationSystem()]);
    }

    private static string SaveToJson(SimulationEngine engine)
    {
        using var stream = new MemoryStream();
        engine.Save(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static SimulationEngine LoadFromJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return SimulationEngine.Load(stream, [new MutationSystem()]);
    }

    private static string ReplaceOnce(string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected to find '{oldValue}' in save json.");
        return source[..index] + newValue + source[(index + oldValue.Length)..];
    }

    private static string MutateJson(string json, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        Assert.NotNull(root);

        mutate(root);
        return root.ToJsonString(new() { WriteIndented = true });
    }

    private static string FormatEvent(MutationEvent e) => $"{e.Tick}:{e.Draw}:{e.CentroidLatE6}";

    private sealed record MutationEvent(long Tick, int Draw, int CentroidLatE6) : ISimulationEvent;

    private sealed class MutationSystem : ISimulationSystem
    {
        public string Name => "mutation";
        public string Key => "mutation";

        public TickCadence Cadence => TickCadence.EveryTick;

        public int Order => 0;

        public IReadOnlyCollection<StateKey> Reads => [];

        public IReadOnlyCollection<StateKey> Writes => [ComponentStateKeys.Of<ProvinceComponent>()];

        public void Execute(in SimulationContext ctx)
        {
            var delta = ctx.Random.NextInt32(1, 10);
            ref var province = ref ctx.State.Entities.GetRef<ProvinceComponent>(new EntityId(1));
            province = province with { CentroidLatE6 = province.CentroidLatE6 + delta };
            ctx.Events.Publish(new MutationEvent(ctx.TickNumber, delta, province.CentroidLatE6));
        }
    }

    private sealed class TestMetadataStateSectionCodec : IStateSectionCodec
    {
        public string SectionType => "SimEngine.Tests.MetadataState";

        public bool IsRequired => false;

        public JsonElement WriteSection(SimulationState state, JsonSerializerOptions options)
        {
            var entries = state.Metadata
                .Select(entry => new MetadataEntry(entry.Key, entry.Value))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToArray();
            return JsonSerializer.SerializeToElement(entries, options);
        }

        public void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options)
        {
            var entries = payload.Deserialize<MetadataEntry[]>(options)
                ?? throw new InvalidDataException("Metadata payload was missing.");

            foreach (var entry in entries)
            {
                state.Metadata[entry.Key] = entry.Value;
            }
        }
    }

    private sealed class RequiredTestMetadataStateSectionCodec : IStateSectionCodec
    {
        private static readonly TestMetadataStateSectionCodec Inner = new();

        public string SectionType => Inner.SectionType;

        public bool IsRequired => true;

        public JsonElement WriteSection(SimulationState state, JsonSerializerOptions options)
            => Inner.WriteSection(state, options);

        public void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options)
            => Inner.ReadSection(state, payload, options);
    }

    private sealed record MetadataEntry(string Key, string Value);
}
