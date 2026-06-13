using Orleans.TestingHost;
using SimEngine.Contracts;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class GameSessionGrainTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
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

    private IGameSessionGrain GetGrain(string sessionId) =>
        _cluster.GrainFactory.GetGrain<IGameSessionGrain>(sessionId);

    private async Task<IGameSessionGrain> NewInitializedGrainAsync(string sessionId, ulong seed = 42)
    {
        var grain = GetGrain(sessionId);
        await grain.InitializeAsync(WorldId, StartDate, seed);
        return grain;
    }

    [Fact]
    public async Task InitializeAsync_LoadsWorld_ReturnsSessionInfo()
    {
        var grain = GetGrain("init-info");

        var info = await grain.InitializeAsync(WorldId, StartDate, seed: 42);

        Assert.Equal(4, info.ProvinceCount);
        Assert.True(info.AdjacencyEdgeCount > 0);
        Assert.Equal(0, info.TickNumber);
        Assert.Equal(StartDate, info.CurrentDate);
        Assert.False(string.IsNullOrWhiteSpace(info.WorldName));
    }

    [Fact]
    public async Task InitializeAsync_UnknownWorldId_Throws()
    {
        var grain = GetGrain("init-unknown-world");

        await Assert.ThrowsAsync<ArgumentException>(
            () => grain.InitializeAsync("no-such-world", StartDate, seed: 42));
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_Throws()
    {
        var grain = await NewInitializedGrainAsync("init-twice");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.InitializeAsync(WorldId, StartDate, seed: 42));
    }

    [Fact]
    public async Task StepAsync_AdvancesTickNumber()
    {
        var grain = await NewInitializedGrainAsync("step-1");

        var result = await grain.StepAsync(1);

        Assert.Equal(1, result.TickNumber);
        Assert.Equal(1, result.TicksExecuted);
    }

    [Fact]
    public async Task StepAsync_Batched_ExecutesAllTicksServerSide()
    {
        var grain = await NewInitializedGrainAsync("step-batch");

        var result = await grain.StepAsync(40);

        Assert.Equal(40, result.TickNumber);
        Assert.Equal(40, result.TicksExecuted);
        Assert.True(result.CurrentDate > StartDate);
    }

    [Fact]
    public async Task StepAsync_MultipleSteps_AccumulatesTicks()
    {
        var grain = await NewInitializedGrainAsync("step-multi");

        await grain.StepAsync(1);
        await grain.StepAsync(1);
        var result = await grain.StepAsync(1);

        Assert.Equal(3, result.TickNumber);
    }

    [Fact]
    public async Task EnqueueCommand_StepCommand_ExecutesMultipleTicks()
    {
        var grain = await NewInitializedGrainAsync("cmd-step");

        await grain.EnqueueCommandAsync(new StepPlayerCommand(5));
        var result = await grain.StepAsync(1);

        Assert.Equal(5, result.TickNumber);
        Assert.Equal(5, result.TicksExecuted);
    }

    [Fact]
    public async Task EnqueueCommand_Pause_PersistsAcrossSteps_UntilResume()
    {
        var grain = await NewInitializedGrainAsync("cmd-pause");

        await grain.EnqueueCommandAsync(new PausePlayerCommand());
        var paused = await grain.StepAsync(1);
        var stillPaused = await grain.StepAsync(1);

        Assert.Equal(0, paused.TicksExecuted);
        Assert.Equal(0, stillPaused.TicksExecuted);
        Assert.Equal(0, stillPaused.TickNumber);

        await grain.EnqueueCommandAsync(new ResumePlayerCommand());
        var resumed = await grain.StepAsync(1);

        Assert.Equal(1, resumed.TicksExecuted);
        Assert.Equal(1, resumed.TickNumber);
    }

    [Fact]
    public async Task GetCurrentDateAsync_ReturnsAdvancedDate()
    {
        var grain = await NewInitializedGrainAsync("date");

        await grain.StepAsync(1);
        var date = await grain.GetCurrentDateAsync();

        Assert.True(date > StartDate);
    }

    [Fact]
    public async Task ShutdownAsync_ReleasesEngine()
    {
        var grain = await NewInitializedGrainAsync("shutdown");
        await grain.ShutdownAsync();

        // The next call hits a fresh activation with no engine.
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GetTickNumberAsync());
    }

    [Fact]
    public async Task SaveAsync_ThenInitializeFromSave_ResumesAtSameTickAndDate()
    {
        var savePath = TempSavePath();
        try
        {
            var original = await NewInitializedGrainAsync("save-source");
            var beforeSave = await original.StepAsync(10);
            await original.SaveAsync(savePath);

            var restored = GetGrain("save-restored");
            var info = await restored.InitializeFromSaveAsync(savePath);

            Assert.Equal(beforeSave.TickNumber, info.TickNumber);
            Assert.Equal(beforeSave.CurrentDate, info.CurrentDate);
            Assert.Equal(4, info.ProvinceCount);

            var resumed = await restored.StepAsync(5);
            Assert.Equal(15, resumed.TickNumber);
        }
        finally
        {
            File.Delete(savePath);
        }
    }

    [Fact]
    public async Task ResumeFromSave_IsDeterministic_AgainstContinuedSession()
    {
        var savePath = TempSavePath();
        var continuedPath = TempSavePath();
        var resumedPath = TempSavePath();
        try
        {
            // Run 40 ticks (crosses a month boundary so game systems fire), save.
            var original = await NewInitializedGrainAsync("det-continued");
            await original.StepAsync(40);
            await original.SaveAsync(savePath);

            // Branch A: keep stepping the original session.
            await original.StepAsync(25);
            await original.SaveAsync(continuedPath);

            // Branch B: reload from the save and step the same amount.
            var restored = GetGrain("det-resumed");
            await restored.InitializeFromSaveAsync(savePath);
            await restored.StepAsync(25);
            await restored.SaveAsync(resumedPath);

            Assert.Equal(
                await File.ReadAllTextAsync(continuedPath, TestContext.Current.CancellationToken),
                await File.ReadAllTextAsync(resumedPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(savePath);
            File.Delete(continuedPath);
            File.Delete(resumedPath);
        }
    }

    private static string TempSavePath() =>
        Path.Combine(Path.GetTempPath(), $"simengine-grain-test-{Guid.NewGuid():N}.json");
}
