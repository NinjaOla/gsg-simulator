using Akka.Actor;
using SimEngine.Client;
using SimEngine.Contracts;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// Proves the events-out path: an actor subscribed to a session receives tick
/// updates (with game events) pushed by the session actor after an advance,
/// without polling.
/// </summary>
public sealed class GameSessionStreamTests : IAsyncLifetime
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
    public async Task Advance_PushesTickUpdateAndEvents_ToSubscribedActor()
    {
        const string sessionId = "stream-session";
        var session = _harness.Client.GetSession(sessionId);
        await session.InitializeAsync(WorldId, StartDate, seed: 42, Ct);

        var collector = new UpdateCollector();
        var subscriber = _harness.System.ActorOf(Props.Create(() => new CollectorActor(collector)));

        await session.SubscribeAsync(subscriber, Ct);
        try
        {
            // 40 ticks crosses a month boundary, so EconomySystem emits income events.
            await session.AdvanceAsync(40, Ct);

            await WaitUntilAsync(() => collector.Count > 0);

            var update = collector.Last!;
            Assert.Equal(40, update.Tick.TickNumber);
            Assert.Equal(40, update.Tick.TicksExecuted);
            Assert.Contains(update.Events, e => e.Contains("collected", StringComparison.Ordinal));
        }
        finally
        {
            await session.UnsubscribeAsync(subscriber, Ct);
        }
    }

    [Fact]
    public async Task DeltaSync_SnapshotThenStreamUpdates_KeepClientCacheCurrent()
    {
        const string sessionId = "delta-session";
        var session = _harness.Client.GetSession(sessionId);
        await session.InitializeAsync(WorldId, StartDate, seed: 42, Ct);

        // A client connects: fetch the baseline snapshot, then subscribe for deltas.
        var snapshot = await session.GetSnapshotAsync(Ct);
        var cache = new SessionStateCache(snapshot);

        Assert.Equal(0, cache.TickNumber);
        Assert.True(cache.TryGetCountry("ALP", out var alphaSeeded));
        Assert.True(cache.TryGetCountry("BET", out var betaSeeded));
        Assert.Equal(0L, alphaSeeded.FundsE2);
        Assert.Equal(0L, betaSeeded.FundsE2);

        var subscriber = _harness.System.ActorOf(Props.Create(() => new CacheApplyActor(cache)));

        await session.SubscribeAsync(subscriber, Ct);
        try
        {
            // 40 ticks crosses a month boundary, so EconomySystem changes treasury.
            await session.AdvanceAsync(40, Ct);

            await WaitUntilAsync(() => cache.TickNumber == 40);

            Assert.True(cache.TryGetCountry("ALP", out var alphaUpdated));
            Assert.True(cache.TryGetCountry("BET", out var betaUpdated));
            Assert.True(alphaUpdated.FundsE2 > 0);
            Assert.True(betaUpdated.FundsE2 > 0);

            // The cache must match the authoritative engine snapshot.
            var authoritative = await session.GetSnapshotAsync(Ct);
            Assert.Equal(authoritative.Countries.Single(c => c.Tag == "ALP").FundsE2, alphaUpdated.FundsE2);
            Assert.Equal(authoritative.Countries.Single(c => c.Tag == "BET").FundsE2, betaUpdated.FundsE2);
        }
        finally
        {
            await session.UnsubscribeAsync(subscriber, Ct);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for a stream update.");
            }

            await Task.Delay(100, ct);
        }
    }

    private sealed class UpdateCollector
    {
        private readonly object _gate = new();
        private SessionStreamUpdate? _last;
        private int _count;

        public int Count
        {
            get { lock (_gate) { return _count; } }
        }

        public SessionStreamUpdate? Last
        {
            get { lock (_gate) { return _last; } }
        }

        public void Add(SessionStreamUpdate update)
        {
            lock (_gate)
            {
                _last = update;
                _count++;
            }
        }
    }

    private sealed class CollectorActor : ReceiveActor
    {
        public CollectorActor(UpdateCollector collector)
        {
            Receive<SessionStreamUpdate>(collector.Add);
        }
    }

    private sealed class CacheApplyActor : ReceiveActor
    {
        public CacheApplyActor(SessionStateCache cache)
        {
            Receive<SessionStreamUpdate>(cache.Apply);
        }
    }
}
