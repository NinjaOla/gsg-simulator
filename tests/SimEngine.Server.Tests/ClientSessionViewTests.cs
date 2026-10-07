using SimEngine.Contracts;
using SimEngine.Game.Worlds;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// Covers the engine-free client path: static data loaded locally must match
/// the server's world, joining with its hash must pass the content gate, and
/// a subscription must keep the read model in sync after an advance.
/// </summary>
public sealed class ClientSessionViewTests : IAsyncLifetime
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StaticWorld_MatchesServerWorld_AndPassesContentGate()
    {
        var session = _harness.Client.GetSession("static-world");
        var info = await session.InitializeAsync(WorldId, StartDate, seed: 42, Ct);

        var world = StaticWorld.Load(info.WorldId);

        Assert.Equal(info.ProvinceCount, world.Provinces.Count);
        Assert.Equal(info.AdjacencyEdgeCount, world.Adjacency.EdgeCount);
        Assert.Equal(info.ContentHash, world.ContentHash);

        await session.JoinAsync("player-1", world.ContentHash, Ct);
        Assert.Equal(["player-1"], await session.GetPlayersAsync(Ct));
    }

    [Fact]
    public async Task Cache_WaitForChange_SignalsOnPauseWithoutTick()
    {
        var session = _harness.Client.GetSession("wait-for-change");
        var info = await session.InitializeAsync(WorldId, StartDate, seed: 42, Ct);
        await session.JoinAsync("host", info.ContentHash, Ct);
        await session.UpdateSettingsAsync("host", new GameSettings { InitialSpeed = 1 }, Ct);
        await session.StartGameAsync("host", ct: Ct);

        var subscription = await _harness.SubscribeAsync(session, Ct);
        try
        {
            var sequence = subscription.Cache.Sequence;
            Assert.False(subscription.Cache.WaitForChange(sequence, TimeSpan.FromMilliseconds(10)));

            await session.SendCommandAsync("host", new PausePlayerCommand(), Ct);

            Assert.True(subscription.Cache.WaitForChange(sequence, TimeSpan.FromSeconds(20)));
            Assert.Equal(SessionStage.Paused, subscription.Cache.Stage);
        }
        finally
        {
            await subscription.DisposeAsync();
        }
    }

    [Fact]
    public async Task Subscription_SyncsCacheAfterAdvance()
    {
        var session = _harness.Client.GetSession("subscription");
        await session.InitializeForDebugAsync(WorldId, StartDate, seed: 42, Ct);

        var subscription = await _harness.SubscribeAsync(session, Ct);
        try
        {
            Assert.Equal(WorldId, subscription.Cache.WorldId);
            Assert.Equal(0, subscription.Cache.TickNumber);
            Assert.Equal(SessionStage.Paused, subscription.Cache.Stage);

            // 40 ticks crosses a month boundary, so EconomySystem changes treasury.
            var result = await session.StepAsync(40, Ct);

            Assert.True(subscription.Cache.WaitForTick(result.TickNumber, TimeSpan.FromSeconds(20)));
            Assert.Equal(result.CurrentDate, subscription.Cache.CurrentDate);
            Assert.True(subscription.Cache.TryGetCountry("ALP", out var alpha));
            Assert.True(alpha.FundsE2 > 0);
            Assert.Equal(2, alpha.ProvinceCount);
        }
        finally
        {
            await subscription.DisposeAsync();
        }
    }
}
