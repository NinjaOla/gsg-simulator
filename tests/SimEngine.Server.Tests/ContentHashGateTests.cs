using Akka.Actor;
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
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    // The hash a compatible client computes locally from the same world content.
    private static readonly string MatchingHash = ContentHasher.ComputeFromFiles(
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Ack> JoinSessionAsync(string sessionId, string playerId, string contentHash) =>
        _harness.Sessions.Ask<Ack>(new SessionProtocol.Join(sessionId, playerId, contentHash), Timeout, Ct);

    private Task<Ack> PlayerJoinAsync(string playerId, string sessionId, string contentHash) =>
        _harness.Players.Ask<Ack>(new PlayerProtocol.JoinSession(playerId, sessionId, contentHash), Timeout, Ct);

    private async Task<string?> PlayerCurrentSessionAsync(string playerId)
    {
        var result = await _harness.Players.Ask<PlayerProtocol.CurrentSessionResult>(
            new PlayerProtocol.GetCurrentSession(playerId), Timeout, Ct);
        return result.SessionId;
    }

    private Task<string[]> GetPlayersAsync(string sessionId) =>
        _harness.Client.GetSession(sessionId).GetPlayersAsync(Ct);

    private async Task NewInitializedSessionAsync(string sessionId) =>
        await _harness.Client.GetSession(sessionId).InitializeAsync(WorldId, StartDate, seed: 42, Ct);

    [Fact]
    public async Task Join_MatchingContentHash_RegistersPlayer()
    {
        await NewInitializedSessionAsync("gate-match");

        await JoinSessionAsync("gate-match", "player-1", MatchingHash);

        Assert.Equal(["player-1"], await GetPlayersAsync("gate-match"));
    }

    [Fact]
    public async Task Join_MismatchedContentHash_ThrowsContentMismatch()
    {
        await NewInitializedSessionAsync("gate-mismatch");

        await Assert.ThrowsAsync<ContentMismatchException>(
            () => JoinSessionAsync("gate-mismatch", "player-1", "incompatible-hash"));
    }

    [Fact]
    public async Task Join_MismatchedContentHash_DoesNotRegisterPlayer()
    {
        await NewInitializedSessionAsync("gate-mismatch-noregister");

        await Assert.ThrowsAsync<ContentMismatchException>(
            () => JoinSessionAsync("gate-mismatch-noregister", "player-1", "incompatible-hash"));

        Assert.Empty(await GetPlayersAsync("gate-mismatch-noregister"));
    }

    [Fact]
    public async Task Join_UninitializedSession_AcceptsAnyHash()
    {
        // Membership is independent of engine init; with no authoritative hash
        // loaded yet there is nothing to gate against.
        await JoinSessionAsync("gate-uninitialized", "player-1", "any-hash");

        Assert.Equal(["player-1"], await GetPlayersAsync("gate-uninitialized"));
    }

    [Fact]
    public async Task PlayerJoin_MatchingContentHash_RegistersWithSession()
    {
        await NewInitializedSessionAsync("gate-player-match");

        await PlayerJoinAsync("gate-player-ok", "gate-player-match", MatchingHash);

        Assert.Equal("gate-player-match", await PlayerCurrentSessionAsync("gate-player-ok"));
    }

    [Fact]
    public async Task PlayerJoin_MismatchedContentHash_RejectsAndLeavesPlayerUnjoined()
    {
        await NewInitializedSessionAsync("gate-player-mismatch");

        await Assert.ThrowsAsync<ContentMismatchException>(
            () => PlayerJoinAsync("gate-player-bad", "gate-player-mismatch", "incompatible-hash"));

        Assert.Null(await PlayerCurrentSessionAsync("gate-player-bad"));
    }

    [Fact]
    public async Task InitializeFromSave_PreservesContentHash_AcceptsMatchingJoin()
    {
        var savePath = TempSavePath();
        try
        {
            await NewInitializedSessionAsync("gate-save-source");
            await _harness.Client.GetSession("gate-save-source").SaveAsync(savePath, Ct);

            var restored = _harness.Client.GetSession("gate-save-restored");
            await restored.InitializeFromSaveAsync(savePath, Ct);

            await JoinSessionAsync("gate-save-restored", "player-1", MatchingHash);

            Assert.Equal(["player-1"], await GetPlayersAsync("gate-save-restored"));
        }
        finally
        {
            File.Delete(savePath);
        }
    }

    private static string TempSavePath() =>
        Path.Combine(Path.GetTempPath(), $"simengine-gate-test-{Guid.NewGuid():N}.json");
}
