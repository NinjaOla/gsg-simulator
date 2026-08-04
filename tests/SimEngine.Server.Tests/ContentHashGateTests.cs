using Orleans.TestingHost;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Server.Worlds;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// Proves the content-hash compatibility gate: the server accepts a join whose
/// content hash matches its loaded content and rejects a mismatch, protecting
/// deterministic lockstep. Static data is never sent over the wire; only the
/// hash is exchanged.
/// </summary>
public sealed class ContentHashGateTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // The hash a compatible client computes locally from the same world content.
    private static readonly string MatchingHash = ContentHasher.ComputeFromFiles(
        [
            WorldCatalog.ResolvePath(WorldCatalog.Find(WorldId)!),
            WorldCatalog.ResolveCountriesPath(WorldCatalog.Find(WorldId)!),
        ],
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

    private IGameSessionGrain GetSession(string sessionId) =>
        _cluster.GrainFactory.GetGrain<IGameSessionGrain>(sessionId);

    private IPlayerGrain GetPlayer(string playerId) =>
        _cluster.GrainFactory.GetGrain<IPlayerGrain>(playerId);

    private async Task<IGameSessionGrain> NewInitializedSessionAsync(string sessionId)
    {
        var session = GetSession(sessionId);
        await session.InitializeAsync(WorldId, StartDate, seed: 42);
        return session;
    }

    [Fact]
    public async Task JoinAsync_MatchingContentHash_RegistersPlayer()
    {
        var session = await NewInitializedSessionAsync("gate-match");

        await session.JoinAsync("player-1", MatchingHash);

        Assert.Equal(["player-1"], await session.GetPlayersAsync());
    }

    [Fact]
    public async Task JoinAsync_MismatchedContentHash_ThrowsContentMismatch()
    {
        var session = await NewInitializedSessionAsync("gate-mismatch");

        await Assert.ThrowsAsync<ContentMismatchException>(
            () => session.JoinAsync("player-1", "incompatible-hash"));
    }

    [Fact]
    public async Task JoinAsync_MismatchedContentHash_DoesNotRegisterPlayer()
    {
        var session = await NewInitializedSessionAsync("gate-mismatch-noregister");

        await Assert.ThrowsAsync<ContentMismatchException>(
            () => session.JoinAsync("player-1", "incompatible-hash"));

        Assert.Empty(await session.GetPlayersAsync());
    }

    [Fact]
    public async Task JoinAsync_UninitializedSession_AcceptsAnyHash()
    {
        // Membership is independent of engine init; with no authoritative hash
        // loaded yet there is nothing to gate against.
        var session = GetSession("gate-uninitialized");

        await session.JoinAsync("player-1", "any-hash");

        Assert.Equal(["player-1"], await session.GetPlayersAsync());
    }

    [Fact]
    public async Task PlayerJoin_MatchingContentHash_RegistersWithSession()
    {
        await NewInitializedSessionAsync("gate-player-match");
        var player = GetPlayer("gate-player-ok");

        await player.JoinSessionAsync("gate-player-match", MatchingHash);

        Assert.Equal("gate-player-match", await player.GetCurrentSessionAsync());
    }

    [Fact]
    public async Task PlayerJoin_MismatchedContentHash_RejectsAndLeavesPlayerUnjoined()
    {
        await NewInitializedSessionAsync("gate-player-mismatch");
        var player = GetPlayer("gate-player-bad");

        await Assert.ThrowsAsync<ContentMismatchException>(
            () => player.JoinSessionAsync("gate-player-mismatch", "incompatible-hash"));

        Assert.Null(await player.GetCurrentSessionAsync());
    }

    [Fact]
    public async Task InitializeFromSave_PreservesContentHash_AcceptsMatchingJoin()
    {
        var savePath = TempSavePath();
        try
        {
            var source = await NewInitializedSessionAsync("gate-save-source");
            await source.SaveAsync(savePath);

            var restored = GetSession("gate-save-restored");
            await restored.InitializeFromSaveAsync(savePath);

            await restored.JoinAsync("player-1", MatchingHash);

            Assert.Equal(["player-1"], await restored.GetPlayersAsync());
        }
        finally
        {
            File.Delete(savePath);
        }
    }

    private static string TempSavePath() =>
        Path.Combine(Path.GetTempPath(), $"simengine-gate-test-{Guid.NewGuid():N}.json");
}
