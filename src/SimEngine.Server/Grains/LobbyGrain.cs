using SimEngine.Contracts;
using SimEngine.Server.Worlds;

namespace SimEngine.Server.Grains;

/// <summary>
/// Orleans grain for the lobby. Creates game sessions (delegating world load
/// and seeding to <see cref="IGameSessionGrain"/>), tracks the games it created,
/// and exposes the server's <see cref="WorldCatalog"/>. The lobby owns no
/// simulation state; listings are in-memory metadata until persistence lands.
/// </summary>
public sealed class LobbyGrain : Grain, ILobbyGrain
{
    private readonly Dictionary<string, GameListing> _games = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<GameListing> CreateGameAsync(CreateGameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorldId);

        var sessionId = Guid.NewGuid().ToString("N");
        var session = GrainFactory.GetGrain<IGameSessionGrain>(sessionId);

        // Initialize first: if the world is unknown the session throws and the
        // game is never registered.
        var info = await session.InitializeAsync(request.WorldId, request.StartDate, request.Seed);

        var listing = new GameListing
        {
            SessionId = sessionId,
            Name = request.Name,
            WorldId = request.WorldId,
            WorldName = info.WorldName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _games[sessionId] = listing;
        return listing;
    }

    /// <inheritdoc />
    public Task<GameListing[]> ListGamesAsync()
    {
        var games = _games.Values
            .OrderBy(g => g.CreatedAt)
            .ThenBy(g => g.SessionId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(games);
    }

    /// <inheritdoc />
    public Task<GameListing?> GetGameAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return Task.FromResult(_games.GetValueOrDefault(sessionId));
    }

    /// <inheritdoc />
    public async Task<bool> RemoveGameAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_games.Remove(sessionId))
        {
            return false;
        }

        var session = GrainFactory.GetGrain<IGameSessionGrain>(sessionId);
        await session.ShutdownAsync();
        return true;
    }

    /// <inheritdoc />
    public Task<WorldOption[]> ListWorldsAsync()
    {
        var worlds = WorldCatalog.All
            .Select(w => new WorldOption { WorldId = w.WorldId, DisplayName = w.DisplayName })
            .ToArray();
        return Task.FromResult(worlds);
    }
}
