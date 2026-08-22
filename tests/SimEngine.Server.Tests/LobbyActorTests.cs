using Akka.Actor;
using SimEngine.Contracts;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class LobbyActorTests : IAsyncLifetime
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

    private static CreateGameRequest Request(string name, string worldId = WorldId, ulong seed = 42) =>
        new() { Name = name, WorldId = worldId, StartDate = StartDate, Seed = seed };

    [Fact]
    public async Task CreateGameAsync_ReturnsListingWithSessionAndWorld()
    {
        var listing = await _harness.Client.CreateGameAsync(Request("My Game"), TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(listing.SessionId));
        Assert.Equal("My Game", listing.Name);
        Assert.Equal(WorldId, listing.WorldId);
        Assert.False(string.IsNullOrWhiteSpace(listing.WorldName));
    }

    [Fact]
    public async Task CreateGameAsync_CreatesPlayableSession()
    {
        var listing = await _harness.Client.CreateGameAsync(Request("Playable"), TestContext.Current.CancellationToken);
        var result = await _harness.Client.GetSession(listing.SessionId).AdvanceAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TickNumber);
    }

    [Fact]
    public async Task CreateGameAsync_UnknownWorld_Throws_AndDoesNotRegister()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Client.CreateGameAsync(Request("Bad", worldId: "no-such-world"), TestContext.Current.CancellationToken));

        Assert.Empty(await _harness.Client.ListGamesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateGameAsync_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _harness.Client.CreateGameAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateGameAsync_BlankName_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Client.CreateGameAsync(Request("   "), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListGamesAsync_ReturnsAllCreatedGames()
    {
        await _harness.Client.CreateGameAsync(Request("One"), TestContext.Current.CancellationToken);
        await _harness.Client.CreateGameAsync(Request("Two"), TestContext.Current.CancellationToken);

        var games = await _harness.Client.ListGamesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, games.Length);
    }

    [Fact]
    public async Task ListGamesAsync_OrderingIsStableAcrossCalls()
    {
        await _harness.Client.CreateGameAsync(Request("A"), TestContext.Current.CancellationToken);
        await _harness.Client.CreateGameAsync(Request("B"), TestContext.Current.CancellationToken);
        await _harness.Client.CreateGameAsync(Request("C"), TestContext.Current.CancellationToken);

        var first = (await _harness.Client.ListGamesAsync(TestContext.Current.CancellationToken)).Select(g => g.SessionId).ToArray();
        var second = (await _harness.Client.ListGamesAsync(TestContext.Current.CancellationToken)).Select(g => g.SessionId).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetGameAsync_ReturnsRegisteredListing()
    {
        var created = await _harness.Client.CreateGameAsync(Request("Findable"), TestContext.Current.CancellationToken);

        var found = await _harness.Client.GetGameAsync(created.SessionId, TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(created.SessionId, found.SessionId);
    }

    [Fact]
    public async Task GetGameAsync_UnknownSession_ReturnsNull()
    {
        Assert.Null(await _harness.Client.GetGameAsync("not-a-session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveGameAsync_UnregistersAndShutsDownSession()
    {
        var listing = await _harness.Client.CreateGameAsync(Request("Doomed"), TestContext.Current.CancellationToken);

        var removed = await _harness.Client.RemoveGameAsync(listing.SessionId, TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.Empty(await _harness.Client.ListGamesAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _harness.Sessions.Ask<long>(
                new SessionProtocol.GetTickNumber(listing.SessionId),
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveGameAsync_UnknownSession_ReturnsFalse()
    {
        Assert.False(await _harness.Client.RemoveGameAsync("not-a-session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListWorldsAsync_ReturnsCatalogWorlds()
    {
        var worlds = await _harness.Client.ListWorldsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(worlds, w => w.WorldId == "grid4");
        Assert.Contains(worlds, w => w.WorldId == "germany_admin1");
    }
}
