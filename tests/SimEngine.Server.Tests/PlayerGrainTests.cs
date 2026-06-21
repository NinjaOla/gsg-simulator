using Orleans.TestingHost;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Server.Worlds;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class PlayerGrainTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string ContentHash = ContentHasher.ComputeFromFile(
        WorldCatalog.ResolvePath(WorldCatalog.Find(WorldId)!),
        GameContentDefaults.ContentVersion);

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

    private IPlayerGrain GetPlayer(string playerId) =>
        _cluster.GrainFactory.GetGrain<IPlayerGrain>(playerId);

    private IGameSessionGrain GetSession(string sessionId) =>
        _cluster.GrainFactory.GetGrain<IGameSessionGrain>(sessionId);

    [Fact]
    public async Task JoinSessionAsync_RegistersPlayerWithSession()
    {
        var player = GetPlayer("p-join");

        await player.JoinSessionAsync("s-join", ContentHash);

        Assert.Equal("s-join", await player.GetCurrentSessionAsync());
        Assert.Equal(["p-join"], await GetSession("s-join").GetPlayersAsync());
    }

    [Fact]
    public async Task JoinSessionAsync_SameSessionTwice_IsIdempotent()
    {
        var player = GetPlayer("p-idempotent");

        await player.JoinSessionAsync("s-idempotent", ContentHash);
        await player.JoinSessionAsync("s-idempotent", ContentHash);

        Assert.Equal(["p-idempotent"], await GetSession("s-idempotent").GetPlayersAsync());
    }

    [Fact]
    public async Task JoinSessionAsync_AlreadyInDifferentSession_Throws()
    {
        var player = GetPlayer("p-switch");
        await player.JoinSessionAsync("s-first", ContentHash);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => player.JoinSessionAsync("s-second", ContentHash));
    }

    [Fact]
    public async Task LeaveSessionAsync_UnregistersPlayer()
    {
        var player = GetPlayer("p-leave");
        await player.JoinSessionAsync("s-leave", ContentHash);

        await player.LeaveSessionAsync();

        Assert.Null(await player.GetCurrentSessionAsync());
        Assert.Empty(await GetSession("s-leave").GetPlayersAsync());
    }

    [Fact]
    public async Task LeaveSessionAsync_NotJoined_IsNoOp()
    {
        var player = GetPlayer("p-leave-noop");

        await player.LeaveSessionAsync();

        Assert.Null(await player.GetCurrentSessionAsync());
    }

    [Fact]
    public async Task SubmitCommandAsync_NotJoined_Throws()
    {
        var player = GetPlayer("p-submit-unjoined");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => player.SubmitCommandAsync(new StepPlayerCommand(1)));
    }

    [Fact]
    public async Task SubmitCommandAsync_BuffersCommand()
    {
        var player = GetPlayer("p-buffer");
        await player.JoinSessionAsync("s-buffer", ContentHash);

        await player.SubmitCommandAsync(new StepPlayerCommand(1));
        await player.SubmitCommandAsync(new PausePlayerCommand());

        Assert.Equal(2, await player.GetBufferedCommandCountAsync());
    }

    [Fact]
    public async Task FlushCommandsAsync_NoCommands_ReturnsZero()
    {
        var player = GetPlayer("p-flush-empty");
        await player.JoinSessionAsync("s-flush-empty", ContentHash);

        Assert.Equal(0, await player.FlushCommandsAsync());
    }

    [Fact]
    public async Task FlushCommandsAsync_ClearsBuffer()
    {
        var player = GetPlayer("p-flush-clear");
        await player.JoinSessionAsync("s-flush-clear", ContentHash);
        await player.SubmitCommandAsync(new StepPlayerCommand(1));

        var flushed = await player.FlushCommandsAsync();

        Assert.Equal(1, flushed);
        Assert.Equal(0, await player.GetBufferedCommandCountAsync());
    }

    [Fact]
    public async Task FlushCommandsAsync_ForwardsBufferedCommandsToSession()
    {
        const string sessionId = "s-flush-forward";
        var session = GetSession(sessionId);
        await session.InitializeAsync(WorldId, StartDate, seed: 42);

        var player = GetPlayer("p-flush-forward");
        await player.JoinSessionAsync(sessionId, ContentHash);
        await player.SubmitCommandAsync(new StepPlayerCommand(5));
        await player.FlushCommandsAsync();

        // The buffered StepPlayerCommand(5) was enqueued on the session, so a
        // single step drains it and advances five ticks.
        var result = await session.StepAsync(1);

        Assert.Equal(5, result.TickNumber);
        Assert.Equal(5, result.TicksExecuted);
    }

    [Fact]
    public async Task LeaveSessionAsync_DiscardsBufferedCommands()
    {
        var player = GetPlayer("p-leave-buffer");
        await player.JoinSessionAsync("s-leave-buffer", ContentHash);
        await player.SubmitCommandAsync(new StepPlayerCommand(1));

        await player.LeaveSessionAsync();

        Assert.Equal(0, await player.GetBufferedCommandCountAsync());
    }

    [Fact]
    public async Task MultiplePlayers_JoinSameSession_AreTrackedInOrder()
    {
        const string sessionId = "s-multi";
        await GetPlayer("p-bravo").JoinSessionAsync(sessionId, ContentHash);
        await GetPlayer("p-alpha").JoinSessionAsync(sessionId, ContentHash);

        Assert.Equal(["p-alpha", "p-bravo"], await GetSession(sessionId).GetPlayersAsync());
    }
}
