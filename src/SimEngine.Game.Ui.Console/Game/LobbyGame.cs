using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Worlds;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// A lobby-stage game the local player has joined and subscribed to. <see cref="Start"/>
/// (host) or <see cref="Enter"/> (after the host started) hands ownership to a
/// <see cref="GameSession"/>; disposing an unstarted lobby game leaves it (ending
/// it if host) and disposes what it owns.
/// </summary>
public sealed class LobbyGame : IDisposable
{
    private readonly SessionSubscription _subscription;
    private readonly IAsyncDisposable? _owned;
    private bool _handedOff;

    internal LobbyGame(
        GameClient client,
        SessionClient session,
        SessionSubscription subscription,
        GameListing listing,
        StaticWorld world,
        IServiceProvider services,
        IAsyncDisposable? owned)
    {
        Client = client;
        Session = session;
        _subscription = subscription;
        Listing = listing;
        World = world;
        Services = services;
        _owned = owned;
    }

    public GameClient Client { get; }
    public SessionClient Session { get; }
    public GameListing Listing { get; }
    public StaticWorld World { get; }
    public string PlayerId => Session.PlayerId;
    public IServiceProvider Services { get; }

    /// <summary>The multiplayer connection (and hosted server) this game owns; <c>null</c> on the local server.</summary>
    internal IAsyncDisposable? Owned => _owned;

    /// <summary>Live lobby state: players, host, settings, and stage.</summary>
    public SessionStateCache State => _subscription.Cache;

    public bool IsHost => State.HostPlayerId == PlayerId;

    /// <summary>Host-only; throws <see cref="CommandRejectedException"/> on rejection.</summary>
    public void UpdateSettings(GameSettings settings) =>
        Session.UpdateSettingsAsync(settings).GetAwaiter().GetResult();

    /// <summary>Host-only: starts the game and returns the running session.</summary>
    public GameSession Start()
    {
        ObjectDisposedException.ThrowIf(_handedOff, this);

        // Console host is sync throughout; blocking here is safe (no sync context).
        Session.StartGameAsync().GetAwaiter().GetResult();
        return HandOff();
    }

    /// <summary>Returns the session once the host has started the game.</summary>
    public GameSession Enter()
    {
        ObjectDisposedException.ThrowIf(_handedOff, this);
        return HandOff();
    }

    public void Dispose()
    {
        if (_handedOff)
        {
            return;
        }

        _handedOff = true;
        GameSession.Close(Client, Session, _subscription, _owned);
    }

    private GameSession HandOff()
    {
        _handedOff = true;
        return new GameSession(Client, Session, _subscription, World, Listing.WorldName, Services, _owned);
    }
}
