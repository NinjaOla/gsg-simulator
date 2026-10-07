using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Worlds;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// Creates <see cref="GameSession"/> handles through the <see cref="GameClient"/>
/// (lobby create/load, then join with the locally loaded world's content hash
/// and subscribe to state updates). The player id comes from the connection.
/// </summary>
internal static class GameSessionFactory
{
    /// <summary>
    /// Creates a game through the lobby and joins it as the local player
    /// (the first joiner becomes host). The game stays in the lobby stage until
    /// <see cref="LobbyGame.Start"/>. On success the lobby game owns <paramref name="owned"/>.
    /// </summary>
    public static LobbyGame CreateLobbyGame(
        GameClient client,
        IServiceProvider services,
        string name,
        string worldId,
        DateTimeOffset startDate,
        ulong seed,
        IAsyncDisposable? owned = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);

        var world = StaticWorld.Load(worldId);

        // Console host is sync throughout; blocking here is safe (no sync context).
        var listing = client.CreateGameAsync(new CreateGameRequest
        {
            Name = name,
            WorldId = worldId,
            StartDate = startDate,
            Seed = seed,
        }).GetAwaiter().GetResult();

        try
        {
            return Join(client, listing, world, services, owned);
        }
        catch
        {
            client.RemoveGameAsync(listing.SessionId).ContinueWith(_ => { }, TaskScheduler.Default).GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// Joins another player's lobby with the locally loaded world's content hash.
    /// On success the lobby game owns <paramref name="owned"/>.
    /// </summary>
    public static LobbyGame JoinLobbyGame(GameClient client, GameListing listing, IServiceProvider services, IAsyncDisposable? owned = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(services);

        return Join(client, listing, StaticWorld.Load(listing.WorldId), services, owned);
    }

    /// <summary>Joins and subscribes, so lobby changes and the start reach the cache.</summary>
    private static LobbyGame Join(GameClient client, GameListing listing, StaticWorld world, IServiceProvider services, IAsyncDisposable? owned)
    {
        var session = client.JoinGameAsync(listing.SessionId, world.ContentHash).GetAwaiter().GetResult();
        try
        {
            var subscription = session.SubscribeAsync().GetAwaiter().GetResult();
            return new LobbyGame(client, session, subscription, listing, world, services, owned);
        }
        catch
        {
            session.LeaveAsync().ContinueWith(_ => { }, TaskScheduler.Default).GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// Creates a lobby game from a save on the client's server. Relative names resolve
    /// in the server's save directory; only a local (single-player) server accepts full paths.
    /// </summary>
    public static GameListing LoadGame(GameClient client, string save)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(save);

        // Console host is sync throughout; blocking here is safe (no sync context).
        return client.LoadGameAsync(SaveGamePaths.GetDisplayName(save), save).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Joins a game loaded by <see cref="LoadGame"/> as its host; it stays in the lobby
    /// at the saved tick with the saved settings. On failure the loaded game is removed.
    /// On success the lobby game owns <paramref name="owned"/>.
    /// </summary>
    public static LobbyGame JoinLoadedGame(GameClient client, GameListing listing, IServiceProvider services, IAsyncDisposable? owned = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(services);

        try
        {
            if (WorldCatalog.Find(listing.WorldId) is null)
            {
                throw new InvalidDataException($"The save uses the world '{listing.WorldId}', which is not installed.");
            }

            return Join(client, listing, StaticWorld.Load(listing.WorldId), services, owned);
        }
        catch
        {
            client.RemoveGameAsync(listing.SessionId).ContinueWith(_ => { }, TaskScheduler.Default).GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// Saves on the session's server. Relative names resolve in the server's save
    /// directory; only a local (single-player) server accepts full paths.
    /// </summary>
    public static string Save(GameSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return session.Session.SaveAsync(path).GetAwaiter().GetResult();
    }
}
