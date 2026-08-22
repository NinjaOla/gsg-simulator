using SimEngine;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Game.Seeding;
using SimEngine.Server.Worlds;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// Proves the actor adds no simulation divergence: stepping N ticks through the
/// session actor produces a byte-identical save to stepping the same world/seed
/// N ticks on a directly-constructed engine.
/// </summary>
public sealed class ActorEngineParityTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private const ulong Seed = 42;
    private const int Ticks = 40; // crosses a month boundary so game systems fire
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private ServerTestHarness _harness = null!;

    public async ValueTask InitializeAsync()
    {
        _harness = await ServerTestHarness.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();
    }

    [Fact]
    public async Task SteppingViaActor_MatchesDirectEngine_ByteForByte()
    {
        var actorPath = TempSavePath();
        var directPath = TempSavePath();
        try
        {
            var session = _harness.Client.GetSession("parity");
            await session.InitializeAsync(WorldId, StartDate, Seed, TestContext.Current.CancellationToken);
            await session.AdvanceAsync(Ticks, TestContext.Current.CancellationToken);
            await session.SaveAsync(actorPath, TestContext.Current.CancellationToken);

            var engine = BuildEngineLikeTheActorDoes();
            engine.StepMany(Ticks, TestContext.Current.CancellationToken);
            engine.Save(directPath);

            Assert.Equal(
                await File.ReadAllTextAsync(directPath, TestContext.Current.CancellationToken),
                await File.ReadAllTextAsync(actorPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(actorPath);
            File.Delete(directPath);
        }
    }

    private static SimulationEngine BuildEngineLikeTheActorDoes()
    {
        var asset = WorldCatalog.Find(WorldId);
        Assert.NotNull(asset);

        var worldPath = WorldCatalog.ResolvePath(asset);
        var countriesPath = WorldCatalog.ResolveCountriesPath(asset);

        using var worldStream = File.OpenRead(worldPath);
        var worldResult = new GeoJsonWorldLoader().Load(worldStream);
        var state = WorldLoaders.LoadIntoState(worldResult);
        GameWorldSeeder.Seed(state, worldResult, countriesPath);
        state.Metadata["worldName"] = asset.DisplayName;
        state.Metadata[GameManifestMetadata.ScenarioIdKey] = WorldId;

        var definition = GameDefinition.CreateDefault(
            scenarioId: WorldId,
            contentVersion: GameContentDefaults.ContentVersion,
            contentHash: ContentHasher.ComputeFromFiles([worldPath, countriesPath], GameContentDefaults.ContentVersion));

        return new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = StartDate,
                Seed = Seed,
                InitialState = state,
                ComponentCodecs = definition.ComponentCodecs,
                StateSectionCodecs = definition.StateSectionCodecs,
                SaveMetadata = definition.SaveMetadata,
            },
            definition.Systems);
    }

    private static string TempSavePath() =>
        Path.Combine(Path.GetTempPath(), $"simengine-parity-test-{Guid.NewGuid():N}.json");
}
