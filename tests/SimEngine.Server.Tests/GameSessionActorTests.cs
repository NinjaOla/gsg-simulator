using Akka.Actor;
using SimEngine.Client;
using SimEngine.Contracts;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class GameSessionActorTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
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

    private SessionClient GetSession(string sessionId) => _harness.Client.GetSession(sessionId);

    private async Task<SessionClient> NewInitializedSessionAsync(string sessionId, ulong seed = 42)
    {
        var session = GetSession(sessionId);
        await session.InitializeAsync(WorldId, StartDate, seed, TestContext.Current.CancellationToken);
        return session;
    }

    [Fact]
    public async Task InitializeAsync_LoadsWorld_ReturnsSessionInfo()
    {
        var session = GetSession("init-info");

        var info = await session.InitializeAsync(WorldId, StartDate, seed: 42, TestContext.Current.CancellationToken);

        Assert.Equal(4, info.ProvinceCount);
        Assert.True(info.AdjacencyEdgeCount > 0);
        Assert.Equal(0, info.TickNumber);
        Assert.Equal(StartDate, info.CurrentDate);
        Assert.False(string.IsNullOrWhiteSpace(info.WorldName));
    }

    [Fact]
    public async Task InitializeAsync_UnknownWorldId_Throws()
    {
        var session = GetSession("init-unknown-world");

        await Assert.ThrowsAsync<ArgumentException>(
            () => session.InitializeAsync("no-such-world", StartDate, seed: 42, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_Throws()
    {
        var session = await NewInitializedSessionAsync("init-twice");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.InitializeAsync(WorldId, StartDate, seed: 42, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdvanceAsync_AdvancesTickNumber()
    {
        var session = await NewInitializedSessionAsync("step-1");

        var result = await session.AdvanceAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TickNumber);
        Assert.Equal(1, result.TicksExecuted);
    }

    [Fact]
    public async Task AdvanceAsync_Batched_ExecutesAllTicksServerSide()
    {
        var session = await NewInitializedSessionAsync("step-batch");

        var result = await session.AdvanceAsync(40, TestContext.Current.CancellationToken);

        Assert.Equal(40, result.TickNumber);
        Assert.Equal(40, result.TicksExecuted);
        Assert.True(result.CurrentDate > StartDate);
    }

    [Fact]
    public async Task AdvanceAsync_MultipleSteps_AccumulatesTicks()
    {
        var session = await NewInitializedSessionAsync("step-multi");

        await session.AdvanceAsync(1, TestContext.Current.CancellationToken);
        await session.AdvanceAsync(1, TestContext.Current.CancellationToken);
        var result = await session.AdvanceAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TickNumber);
    }

    [Fact]
    public async Task EnqueueCommand_StepCommand_ExecutesMultipleTicks()
    {
        var session = await NewInitializedSessionAsync("cmd-step");

        await session.EnqueueCommandAsync(new StepPlayerCommand(5), TestContext.Current.CancellationToken);
        var result = await session.AdvanceAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.TickNumber);
        Assert.Equal(5, result.TicksExecuted);
    }

    [Fact]
    public async Task EnqueueCommand_Pause_PersistsAcrossSteps_UntilResume()
    {
        var session = await NewInitializedSessionAsync("cmd-pause");

        await session.EnqueueCommandAsync(new PausePlayerCommand(), TestContext.Current.CancellationToken);
        var paused = await session.AdvanceAsync(1, TestContext.Current.CancellationToken);
        var stillPaused = await session.AdvanceAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(0, paused.TicksExecuted);
        Assert.Equal(0, stillPaused.TicksExecuted);
        Assert.Equal(0, stillPaused.TickNumber);

        await session.EnqueueCommandAsync(new ResumePlayerCommand(), TestContext.Current.CancellationToken);
        var resumed = await session.AdvanceAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(1, resumed.TicksExecuted);
        Assert.Equal(1, resumed.TickNumber);
    }

    [Fact]
    public async Task GetCurrentDate_ReturnsAdvancedDate()
    {
        var session = await NewInitializedSessionAsync("date");

        await session.AdvanceAsync(1, TestContext.Current.CancellationToken);
        var date = await _harness.Sessions.Ask<DateTimeOffset>(
            new SessionProtocol.GetCurrentDate("date"),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.True(date > StartDate);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsWorldSummaryAndSeededCountry()
    {
        var session = await NewInitializedSessionAsync("snapshot-fresh");

        var snapshot = await session.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, snapshot.ProvinceCount);
        Assert.True(snapshot.AdjacencyEdgeCount > 0);
        Assert.Equal(0, snapshot.TickNumber);
        Assert.Equal(StartDate, snapshot.CurrentDate);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.WorldName));

        Assert.Equal(2, snapshot.Countries.Length);
        Assert.Contains(snapshot.Countries, c => c.Tag == "ALP" && c.FundsE2 == 0L);
        Assert.Contains(snapshot.Countries, c => c.Tag == "BET" && c.FundsE2 == 0L);
    }

    [Fact]
    public async Task GetSnapshotAsync_AfterSteps_ReflectsAdvancedTreasury()
    {
        var session = await NewInitializedSessionAsync("snapshot-stepped");

        // 40 ticks crosses a month boundary, so EconomySystem collects income.
        await session.AdvanceAsync(40, TestContext.Current.CancellationToken);
        var snapshot = await session.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(40, snapshot.TickNumber);
        Assert.Equal(2, snapshot.Countries.Length);
        Assert.All(snapshot.Countries, country => Assert.True(country.FundsE2 > 0));
    }

    [Fact]
    public async Task ShutdownAsync_ReleasesEngine()
    {
        var session = await NewInitializedSessionAsync("shutdown");
        await session.ShutdownAsync(TestContext.Current.CancellationToken);

        // The next call hits a fresh activation with no engine.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _harness.Sessions.Ask<long>(
                new SessionProtocol.GetTickNumber("shutdown"),
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_ThenInitializeFromSave_ResumesAtSameTickAndDate()
    {
        var savePath = TempSavePath();
        try
        {
            var original = await NewInitializedSessionAsync("save-source");
            var beforeSave = await original.AdvanceAsync(10, TestContext.Current.CancellationToken);
            await original.SaveAsync(savePath, TestContext.Current.CancellationToken);

            var restored = GetSession("save-restored");
            var info = await restored.InitializeFromSaveAsync(savePath, TestContext.Current.CancellationToken);

            Assert.Equal(beforeSave.TickNumber, info.TickNumber);
            Assert.Equal(beforeSave.CurrentDate, info.CurrentDate);
            Assert.Equal(4, info.ProvinceCount);

            var resumed = await restored.AdvanceAsync(5, TestContext.Current.CancellationToken);
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
            var original = await NewInitializedSessionAsync("det-continued");
            await original.AdvanceAsync(40, TestContext.Current.CancellationToken);
            await original.SaveAsync(savePath, TestContext.Current.CancellationToken);

            // Branch A: keep stepping the original session.
            await original.AdvanceAsync(25, TestContext.Current.CancellationToken);
            await original.SaveAsync(continuedPath, TestContext.Current.CancellationToken);

            // Branch B: reload from the save and step the same amount.
            var restored = GetSession("det-resumed");
            await restored.InitializeFromSaveAsync(savePath, TestContext.Current.CancellationToken);
            await restored.AdvanceAsync(25, TestContext.Current.CancellationToken);
            await restored.SaveAsync(resumedPath, TestContext.Current.CancellationToken);

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
        Path.Combine(Path.GetTempPath(), $"simengine-actor-test-{Guid.NewGuid():N}.json");
}
