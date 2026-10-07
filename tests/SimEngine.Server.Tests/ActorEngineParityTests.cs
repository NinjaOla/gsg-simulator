using SimEngine;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Game.Seeding;
using SimEngine.Game.Worlds;
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
            await session.InitializeForDebugAsync(WorldId, StartDate, Seed, TestContext.Current.CancellationToken);
            await session.StepAsync(Ticks, TestContext.Current.CancellationToken);
            await session.SaveAsync(actorPath, TestContext.Current.CancellationToken);

            var engine = BuildEngineLikeTheActorDoes(new GameSettings { AllowDebugCommands = true });
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

    [Fact]
    public async Task TickingViaScheduler_MatchesDirectEngine_ByteForByte()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorPath = TempSavePath();
        var directPath = TempSavePath();
        try
        {
            const string host = "host";
            var settings = new GameSettings { InitialSpeed = GameSettings.MaxSpeed };
            var session = _harness.Client.GetSession("parity-scheduler");
            var info = await session.InitializeAsync(WorldId, StartDate, Seed, ct);
            await session.JoinAsync(host, info.ContentHash, ct);
            await session.UpdateSettingsAsync(host, settings, ct);
            await session.StartGameAsync(host, ct: ct);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while ((await session.GetStatusAsync(ct)).TickNumber < Ticks)
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the scheduler to tick.");
                await Task.Delay(10, ct);
            }

            await session.SendCommandAsync(host, new PausePlayerCommand(), ct);
            var ticked = (await session.GetStatusAsync(ct)).TickNumber;
            await session.SaveAsync(actorPath, ct);

            var engine = BuildEngineLikeTheActorDoes(settings);
            engine.StepMany((int)ticked, ct);
            engine.Save(directPath);

            Assert.Equal(
                await File.ReadAllTextAsync(directPath, ct),
                await File.ReadAllTextAsync(actorPath, ct));
        }
        finally
        {
            File.Delete(actorPath);
            File.Delete(directPath);
        }
    }

    private static SimulationEngine BuildEngineLikeTheActorDoes(GameSettings settings)
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
        // Saves also carry the session settings.
        SessionSaveSettings.Write(state.Metadata, settings);

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
