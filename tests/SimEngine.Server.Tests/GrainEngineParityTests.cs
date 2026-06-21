using Orleans.TestingHost;
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
/// Proves the grain adds no simulation divergence: stepping N ticks through
/// <see cref="IGameSessionGrain"/> produces a byte-identical save to stepping
/// the same world/seed N ticks on a directly-constructed engine.
/// </summary>
public sealed class GrainEngineParityTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private const ulong Seed = 42;
    private const int Ticks = 40; // crosses a month boundary so game systems fire
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task SteppingViaGrain_MatchesDirectEngine_ByteForByte()
    {
        var grainPath = TempSavePath();
        var directPath = TempSavePath();
        try
        {
            var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>("parity");
            await grain.InitializeAsync(WorldId, StartDate, Seed);
            await grain.StepAsync(Ticks);
            await grain.SaveAsync(grainPath);

            var engine = BuildEngineLikeTheGrainDoes();
            engine.StepMany(Ticks, TestContext.Current.CancellationToken);
            engine.Save(directPath);

            Assert.Equal(
                await File.ReadAllTextAsync(directPath, TestContext.Current.CancellationToken),
                await File.ReadAllTextAsync(grainPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(grainPath);
            File.Delete(directPath);
        }
    }

    private static SimulationEngine BuildEngineLikeTheGrainDoes()
    {
        var asset = WorldCatalog.Find(WorldId);
        Assert.NotNull(asset);

        var worldPath = WorldCatalog.ResolvePath(asset);
        var state = WorldLoaders.LoadIntoState(new GeoJsonWorldLoader(), worldPath);
        GameWorldSeeder.Seed(state);
        state.Metadata["worldName"] = asset.DisplayName;

        var definition = GameDefinition.CreateDefault(
            scenarioId: WorldId,
            contentVersion: GameContentDefaults.ContentVersion,
            contentHash: ContentHasher.ComputeFromFile(worldPath, GameContentDefaults.ContentVersion));

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
