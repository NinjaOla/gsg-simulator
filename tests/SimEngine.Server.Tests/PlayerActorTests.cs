using Akka.Actor;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Server.Worlds;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class PlayerActorTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);
    private static readonly string ContentHash = ContentHasher.ComputeFromFiles(
        [
            WorldCatalog.ResolvePath(WorldCatalog.Find(WorldId)!),
            WorldCatalog.ResolveCountriesPath(WorldCatalog.Find(WorldId)!),
        ],
        GameContentDefaults.ContentVersion);

    private ServerTestHarness _harness = null!;

    public async ValueTask InitializeAsync()
    {
        _harness = await ServerTestHarness.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();
    }

    private Task<Ack> JoinAsync(string playerId, string sessionId, string contentHash) =>
        _harness.Players.Ask<Ack>(
            new PlayerProtocol.JoinSession(playerId, sessionId, contentHash), Timeout, Ct);

    private Task<Ack> LeaveAsync(string playerId) =>
        _harness.Players.Ask<Ack>(new PlayerProtocol.LeaveSession(playerId), Timeout, Ct);

    private Task<Ack> SubmitAsync(string playerId, PlayerCommand command) =>
        _harness.Players.Ask<Ack>(new PlayerProtocol.SubmitCommand(playerId, command), Timeout, Ct);

    private Task<int> FlushAsync(string playerId) =>
        _harness.Players.Ask<int>(new PlayerProtocol.FlushCommands(playerId), Timeout, Ct);

    private async Task<string?> CurrentSessionAsync(string playerId)
    {
        var result = await _harness.Players.Ask<PlayerProtocol.CurrentSessionResult>(
            new PlayerProtocol.GetCurrentSession(playerId), Timeout, Ct);
        return result.SessionId;
    }

    private Task<int> BufferedCountAsync(string playerId) =>
        _harness.Players.Ask<int>(new PlayerProtocol.GetBufferedCommandCount(playerId), Timeout, Ct);

    private Task<string[]> PlayersAsync(string sessionId) =>
        _harness.Client.GetSession(sessionId).GetPlayersAsync(Ct);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task JoinSession_RegistersPlayerWithSession()
    {
        await JoinAsync("p-join", "s-join", ContentHash);

        Assert.Equal("s-join", await CurrentSessionAsync("p-join"));
        Assert.Equal(["p-join"], await PlayersAsync("s-join"));
    }

    [Fact]
    public async Task JoinSession_SameSessionTwice_IsIdempotent()
    {
        await JoinAsync("p-idempotent", "s-idempotent", ContentHash);
        await JoinAsync("p-idempotent", "s-idempotent", ContentHash);

        Assert.Equal(["p-idempotent"], await PlayersAsync("s-idempotent"));
    }

    [Fact]
    public async Task JoinSession_AlreadyInDifferentSession_Throws()
    {
        await JoinAsync("p-switch", "s-first", ContentHash);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => JoinAsync("p-switch", "s-second", ContentHash));
    }

    [Fact]
    public async Task LeaveSession_UnregistersPlayer()
    {
        await JoinAsync("p-leave", "s-leave", ContentHash);

        await LeaveAsync("p-leave");

        Assert.Null(await CurrentSessionAsync("p-leave"));
        Assert.Empty(await PlayersAsync("s-leave"));
    }

    [Fact]
    public async Task LeaveSession_NotJoined_IsNoOp()
    {
        await LeaveAsync("p-leave-noop");

        Assert.Null(await CurrentSessionAsync("p-leave-noop"));
    }

    [Fact]
    public async Task SubmitCommand_NotJoined_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SubmitAsync("p-submit-unjoined", new StepPlayerCommand(1)));
    }

    [Fact]
    public async Task SubmitCommand_BuffersCommand()
    {
        await JoinAsync("p-buffer", "s-buffer", ContentHash);

        await SubmitAsync("p-buffer", new StepPlayerCommand(1));
        await SubmitAsync("p-buffer", new PausePlayerCommand());

        Assert.Equal(2, await BufferedCountAsync("p-buffer"));
    }

    [Fact]
    public async Task FlushCommands_NoCommands_ReturnsZero()
    {
        await JoinAsync("p-flush-empty", "s-flush-empty", ContentHash);

        Assert.Equal(0, await FlushAsync("p-flush-empty"));
    }

    [Fact]
    public async Task FlushCommands_ClearsBuffer()
    {
        await JoinAsync("p-flush-clear", "s-flush-clear", ContentHash);
        await SubmitAsync("p-flush-clear", new StepPlayerCommand(1));

        var flushed = await FlushAsync("p-flush-clear");

        Assert.Equal(1, flushed);
        Assert.Equal(0, await BufferedCountAsync("p-flush-clear"));
    }

    [Fact]
    public async Task FlushCommands_ForwardsBufferedCommandsToSession()
    {
        const string sessionId = "s-flush-forward";
        var session = _harness.Client.GetSession(sessionId);
        await session.InitializeAsync(WorldId, StartDate, seed: 42, Ct);

        await JoinAsync("p-flush-forward", sessionId, ContentHash);
        await SubmitAsync("p-flush-forward", new StepPlayerCommand(5));
        await FlushAsync("p-flush-forward");

        // The buffered StepPlayerCommand(5) was enqueued on the session, so a
        // single step drains it and advances five ticks.
        var result = await session.AdvanceAsync(1, Ct);

        Assert.Equal(5, result.TickNumber);
        Assert.Equal(5, result.TicksExecuted);
    }

    [Fact]
    public async Task LeaveSession_DiscardsBufferedCommands()
    {
        await JoinAsync("p-leave-buffer", "s-leave-buffer", ContentHash);
        await SubmitAsync("p-leave-buffer", new StepPlayerCommand(1));

        await LeaveAsync("p-leave-buffer");

        Assert.Equal(0, await BufferedCountAsync("p-leave-buffer"));
    }

    [Fact]
    public async Task MultiplePlayers_JoinSameSession_AreTrackedInOrder()
    {
        const string sessionId = "s-multi";
        await JoinAsync("p-bravo", sessionId, ContentHash);
        await JoinAsync("p-alpha", sessionId, ContentHash);

        Assert.Equal(["p-alpha", "p-bravo"], await PlayersAsync(sessionId));
    }
}
