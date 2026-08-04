using Microsoft.Extensions.Configuration;
using Orleans.Streams;
using Orleans.TestingHost;
using SimEngine.Client;
using SimEngine.Contracts;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// Proves the events-out path: a client subscribed to the per-session stream
/// receives tick updates (with game events) pushed by the grain after a step,
/// without polling.
/// </summary>
public sealed class GameSessionStreamTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private TestCluster _cluster = null!;

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryStreams(SessionStreams.ProviderName)
                .AddMemoryGrainStorage("PubSubStore");
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams(SessionStreams.ProviderName);
        }
    }

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task StepAsync_PushesTickUpdateAndEvents_ToSubscribedClient()
    {
        const string sessionId = "stream-session";
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>(sessionId);
        await grain.InitializeAsync(WorldId, StartDate, seed: 42);

        var received = new List<SessionStreamUpdate>();
        var stream = _cluster.Client
            .GetStreamProvider(SessionStreams.ProviderName)
            .GetStream<SessionStreamUpdate>(SessionStreams.For(sessionId));

        var handle = await stream.SubscribeAsync((update, _) =>
        {
            lock (received)
            {
                received.Add(update);
            }

            return Task.CompletedTask;
        });

        try
        {
            // 40 ticks crosses a month boundary, so EconomySystem emits income events.
            await grain.StepAsync(40);

            await WaitUntilAsync(() =>
            {
                lock (received)
                {
                    return received.Count > 0;
                }
            });

            SessionStreamUpdate update;
            lock (received)
            {
                update = received[^1];
            }

            Assert.Equal(40, update.Tick.TickNumber);
            Assert.Equal(40, update.Tick.TicksExecuted);
            Assert.Contains(update.Events, e => e.Contains("collected", StringComparison.Ordinal));
        }
        finally
        {
            await handle.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task DeltaSync_SnapshotThenStreamUpdates_KeepClientCacheCurrent()
    {
        const string sessionId = "delta-session";
        var grain = _cluster.GrainFactory.GetGrain<IGameSessionGrain>(sessionId);
        await grain.InitializeAsync(WorldId, StartDate, seed: 42);

        // A client connects: fetch the baseline snapshot, then subscribe for deltas.
        var snapshot = await grain.GetSnapshotAsync();
        var cache = new SessionStateCache(snapshot);

        Assert.Equal(0, cache.TickNumber);
        Assert.True(cache.TryGetCountry("ALP", out var alphaSeeded));
        Assert.True(cache.TryGetCountry("BET", out var betaSeeded));
        Assert.Equal(0L, alphaSeeded.FundsE2);
        Assert.Equal(0L, betaSeeded.FundsE2);

        var stream = _cluster.Client
            .GetStreamProvider(SessionStreams.ProviderName)
            .GetStream<SessionStreamUpdate>(SessionStreams.For(sessionId));

        var handle = await stream.SubscribeAsync((update, _) =>
        {
            cache.Apply(update);
            return Task.CompletedTask;
        });

        try
        {
            // 40 ticks crosses a month boundary, so EconomySystem changes treasury.
            await grain.StepAsync(40);

            await WaitUntilAsync(() => cache.TickNumber == 40);

            Assert.True(cache.TryGetCountry("ALP", out var alphaUpdated));
            Assert.True(cache.TryGetCountry("BET", out var betaUpdated));
            Assert.True(alphaUpdated.FundsE2 > 0);
            Assert.True(betaUpdated.FundsE2 > 0);

            // The cache must match the authoritative engine snapshot.
            var authoritative = await grain.GetSnapshotAsync();
            Assert.Equal(authoritative.Countries.Single(c => c.Tag == "ALP").FundsE2, alphaUpdated.FundsE2);
            Assert.Equal(authoritative.Countries.Single(c => c.Tag == "BET").FundsE2, betaUpdated.FundsE2);
        }
        finally
        {
            await handle.UnsubscribeAsync();
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
}
