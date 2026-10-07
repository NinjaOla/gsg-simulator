using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Worlds;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// Client-side handle for one game session. All simulation mutations go
/// through <see cref="Session"/> (requests over the client connection). Dynamic state
/// is read from <see cref="State"/> (synced via snapshot + per-tick updates) and
/// static data from <see cref="World"/> (loaded locally); the console never
/// touches the server's engine.
/// </summary>
public sealed class GameSession : IDisposable
{
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(30);

    private readonly GameClient _client;
    private readonly SessionSubscription _subscription;
    private IAsyncDisposable? _owned;

    public SessionClient Session { get; }
    public string PlayerId => Session.PlayerId;
    public SessionStateCache State => _subscription.Cache;
    public StaticWorld World { get; }
    public IServiceProvider Services { get; }
    public string WorldName { get; }
    public string SessionId { get; }
    public bool ShouldQuit { get; set; }
    public bool IsHost => State.HostPlayerId == PlayerId;

    /// <summary>A game loaded from a save on this session's server; the loop opens its lobby after leaving this one.</summary>
    internal GameListing? PendingLoad { get; private set; }

    internal GameClient Client => _client;

    public int ProvinceCount => World.Provinces.Count;
    public int AdjacencyEdgeCount => World.Adjacency.EdgeCount;
    public IReadOnlyList<string> EventLog => State.Events;

    public GameSession(
        GameClient client,
        SessionClient session,
        SessionSubscription subscription,
        StaticWorld world,
        string worldName,
        IServiceProvider services,
        IAsyncDisposable? owned = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);
        ArgumentNullException.ThrowIfNull(services);

        _client = client;
        Session = session;
        _subscription = subscription;
        World = world;
        WorldName = worldName;
        SessionId = session.SessionId;
        Services = services;
        _owned = owned;
    }

    /// <summary>
    /// Runs debug ticks on the (paused) session and waits until <see cref="State"/>
    /// reflects the result, so callers can read the new tick/date immediately.
    /// </summary>
    public TickResult Advance(int ticks)
    {
        // Console host is sync throughout; blocking here is safe (no sync context).
        var result = Session.DebugStepAsync(ticks).GetAwaiter().GetResult();
        if (result.TicksExecuted > 0 && !State.WaitForTick(result.TickNumber, SyncTimeout))
        {
            throw new TimeoutException($"Timed out waiting for tick {result.TickNumber} to sync.");
        }

        return result;
    }

    /// <summary>Sends a command as the local player; throws <see cref="CommandRejectedException"/> on rejection.</summary>
    public void SendCommand(PlayerCommand command)
    {
        Session.SendCommandAsync(command).GetAwaiter().GetResult();
    }

    /// <summary>Ends this game after the loop exits and continues in the lobby of <paramref name="listing"/>.</summary>
    public void ContinueWithLoaded(GameListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        PendingLoad = listing;
        ShouldQuit = true;
    }

    /// <summary>Takes over what the session owns (connection, hosted server) so disposing it keeps them alive.</summary>
    internal IAsyncDisposable? DetachOwned()
    {
        var owned = _owned;
        _owned = null;
        return owned;
    }

    public void Dispose() => Close(_client, Session, _subscription, _owned);

    /// <summary>
    /// Best-effort teardown (the server may already be gone): unsubscribe, leave
    /// (the host leaving ends the session), remove the game if host, then dispose
    /// what the session owns (a multiplayer connection and hosted server).
    /// </summary>
    internal static void Close(GameClient client, SessionClient session, SessionSubscription subscription, IAsyncDisposable? owned)
    {
        var isHost = subscription.Cache.HostPlayerId == session.PlayerId;
        try
        {
            subscription.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        try
        {
            session.LeaveAsync().GetAwaiter().GetResult();
            if (isHost)
            {
                client.RemoveGameAsync(session.SessionId).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        try
        {
            owned?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }
}
