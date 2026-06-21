using Orleans.TestingHost;
using SimEngine.Contracts;
using Xunit;

namespace SimEngine.Server.Tests;

public sealed class LobbyGrainTests : IAsyncLifetime
{
    private const string WorldId = "grid4";
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

    private ILobbyGrain GetLobby(string key) =>
        _cluster.GrainFactory.GetGrain<ILobbyGrain>(key);

    private IGameSessionGrain GetSession(string sessionId) =>
        _cluster.GrainFactory.GetGrain<IGameSessionGrain>(sessionId);

    private static CreateGameRequest Request(string name, string worldId = WorldId, ulong seed = 42) =>
        new() { Name = name, WorldId = worldId, StartDate = StartDate, Seed = seed };

    [Fact]
    public async Task CreateGameAsync_ReturnsListingWithSessionAndWorld()
    {
        var lobby = GetLobby("lobby-create");

        var listing = await lobby.CreateGameAsync(Request("My Game"));

        Assert.False(string.IsNullOrWhiteSpace(listing.SessionId));
        Assert.Equal("My Game", listing.Name);
        Assert.Equal(WorldId, listing.WorldId);
        Assert.False(string.IsNullOrWhiteSpace(listing.WorldName));
    }

    [Fact]
    public async Task CreateGameAsync_CreatesPlayableSession()
    {
        var lobby = GetLobby("lobby-playable");

        var listing = await lobby.CreateGameAsync(Request("Playable"));
        var result = await GetSession(listing.SessionId).StepAsync(1);

        Assert.Equal(1, result.TickNumber);
    }

    [Fact]
    public async Task CreateGameAsync_UnknownWorld_Throws_AndDoesNotRegister()
    {
        var lobby = GetLobby("lobby-unknown-world");

        await Assert.ThrowsAsync<ArgumentException>(
            () => lobby.CreateGameAsync(Request("Bad", worldId: "no-such-world")));

        Assert.Empty(await lobby.ListGamesAsync());
    }

    [Fact]
    public async Task CreateGameAsync_NullRequest_Throws()
    {
        var lobby = GetLobby("lobby-null-request");

        await Assert.ThrowsAsync<ArgumentNullException>(() => lobby.CreateGameAsync(null!));
    }

    [Fact]
    public async Task CreateGameAsync_BlankName_Throws()
    {
        var lobby = GetLobby("lobby-blank-name");

        await Assert.ThrowsAsync<ArgumentException>(
            () => lobby.CreateGameAsync(Request("   ")));
    }

    [Fact]
    public async Task ListGamesAsync_ReturnsAllCreatedGames()
    {
        var lobby = GetLobby("lobby-list");
        await lobby.CreateGameAsync(Request("One"));
        await lobby.CreateGameAsync(Request("Two"));

        var games = await lobby.ListGamesAsync();

        Assert.Equal(2, games.Length);
    }

    [Fact]
    public async Task ListGamesAsync_OrderingIsStableAcrossCalls()
    {
        var lobby = GetLobby("lobby-stable-order");
        await lobby.CreateGameAsync(Request("A"));
        await lobby.CreateGameAsync(Request("B"));
        await lobby.CreateGameAsync(Request("C"));

        var first = (await lobby.ListGamesAsync()).Select(g => g.SessionId).ToArray();
        var second = (await lobby.ListGamesAsync()).Select(g => g.SessionId).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetGameAsync_ReturnsRegisteredListing()
    {
        var lobby = GetLobby("lobby-get");
        var created = await lobby.CreateGameAsync(Request("Findable"));

        var found = await lobby.GetGameAsync(created.SessionId);

        Assert.NotNull(found);
        Assert.Equal(created.SessionId, found.SessionId);
    }

    [Fact]
    public async Task GetGameAsync_UnknownSession_ReturnsNull()
    {
        var lobby = GetLobby("lobby-get-unknown");

        Assert.Null(await lobby.GetGameAsync("not-a-session"));
    }

    [Fact]
    public async Task RemoveGameAsync_UnregistersAndShutsDownSession()
    {
        var lobby = GetLobby("lobby-remove");
        var listing = await lobby.CreateGameAsync(Request("Doomed"));

        var removed = await lobby.RemoveGameAsync(listing.SessionId);

        Assert.True(removed);
        Assert.Empty(await lobby.ListGamesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => GetSession(listing.SessionId).GetTickNumberAsync());
    }

    [Fact]
    public async Task RemoveGameAsync_UnknownSession_ReturnsFalse()
    {
        var lobby = GetLobby("lobby-remove-unknown");

        Assert.False(await lobby.RemoveGameAsync("not-a-session"));
    }

    [Fact]
    public async Task ListWorldsAsync_ReturnsCatalogWorlds()
    {
        var lobby = GetLobby("lobby-worlds");

        var worlds = await lobby.ListWorldsAsync();

        Assert.Contains(worlds, w => w.WorldId == "grid4");
        Assert.Contains(worlds, w => w.WorldId == "germany_admin1");
    }
}
