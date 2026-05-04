using SimEngine.Contracts;
using Orleans.TestingHost;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class GameSessionGrainTests : IAsyncLifetime
{
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
    public async Task StepAsync_AdvancesTickNumber()
    {
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>("test-session-1");
        await grain.InitializeAsync("test-world", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), seed: 42);

        var result = await grain.StepAsync();

        Assert.Equal(1, result.TickNumber);
        Assert.Equal(1, result.TicksExecuted);
    }

    [Fact]
    public async Task StepAsync_MultipleSteps_AccumulatesTicks()
    {
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>("test-session-2");
        await grain.InitializeAsync("test-world", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), seed: 42);

        await grain.StepAsync();
        await grain.StepAsync();
        var result = await grain.StepAsync();

        Assert.Equal(3, result.TickNumber);
    }

    [Fact]
    public async Task EnqueueCommand_StepCommand_ExecutesMultipleTicks()
    {
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>("test-session-3");
        await grain.InitializeAsync("test-world", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), seed: 42);

        await grain.EnqueueCommandAsync(new StepPlayerCommand(5));
        var result = await grain.StepAsync();

        Assert.Equal(5, result.TickNumber);
        Assert.Equal(5, result.TicksExecuted);
    }

    [Fact]
    public async Task EnqueueCommand_PauseCommand_ExecutesZeroTicks()
    {
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>("test-session-4");
        await grain.InitializeAsync("test-world", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), seed: 42);

        await grain.EnqueueCommandAsync(new PausePlayerCommand());
        var result = await grain.StepAsync();

        Assert.Equal(0, result.TickNumber);
        Assert.Equal(0, result.TicksExecuted);
    }

    [Fact]
    public async Task GetCurrentDateAsync_ReturnsAdvancedDate()
    {
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>("test-session-5");
        var startDate = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await grain.InitializeAsync("test-world", startDate, seed: 42);

        await grain.StepAsync();
        var date = await grain.GetCurrentDateAsync();

        Assert.True(date > startDate);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_Throws()
    {
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>("test-session-6");
        await grain.InitializeAsync("test-world", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), seed: 42);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.InitializeAsync("test-world", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), seed: 42));
    }
}
