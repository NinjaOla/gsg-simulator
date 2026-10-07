using Akka.Actor;
using SimEngine.Contracts;
using SimEngine.Game.Worlds;

namespace SimEngine.Server.Actors;

/// <summary>
/// Akka actor for the lobby. Creates game sessions (delegating world load and
/// seeding to the session actors routed through <see cref="_sessions"/>),
/// tracks the games it created, and exposes the server's
/// <see cref="WorldCatalog"/>. The lobby owns no simulation state; listings are
/// in-memory metadata until persistence lands.
/// </summary>
public sealed class LobbyActor : ReceiveActor
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, GameListing> _games = new(StringComparer.Ordinal);
    private readonly IActorRef _sessions;

    /// <summary>Creates the props for the lobby actor.</summary>
    public static Props Props(IActorRef sessions) =>
        Akka.Actor.Props.Create(() => new LobbyActor(sessions));

    public LobbyActor(IActorRef sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;

        Receive<LobbyProtocol.CreateGame>(CreateGame);
        Receive<LobbyProtocol.CreateGameFromSave>(CreateGameFromSave);
        Receive<GameInitialized>(OnGameInitialized);
        Receive<GameInitFailed>(m => m.ReplyTo.Tell(new Status.Failure(m.Error)));
        Receive<LobbyProtocol.ListGames>(_ => { ListGames().PipeTo(Sender); });
        Receive<LobbyProtocol.GetGame>(GetGame);
        Receive<PruneEnded>(PruneEndedGames);
        Receive<LobbyProtocol.RemoveGame>(RemoveGame);
        Receive<RemoveGameDone>(m => m.ReplyTo.Tell(true));
        Receive<LobbyProtocol.ListWorlds>(_ => Sender.Tell(ListWorlds()));
    }

    private void CreateGame(LobbyProtocol.CreateGame msg)
    {
        var request = msg.Request;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.WorldId);
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            Sender.Tell(new Status.Failure(ex));
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var replyTo = Sender;

        // Initialize first: if the world is unknown the session replies with a
        // failure and the game is never registered.
        _sessions.Ask<SessionInfo>(
            new SessionProtocol.Initialize(sessionId, request.WorldId, request.StartDate, request.Seed),
            AskTimeout)
            .ContinueWith<object>(t => t.IsFaulted || t.IsCanceled
                ? new GameInitFailed(Flatten(t.Exception), replyTo)
                : new GameInitialized(sessionId, request.Name, t.Result, replyTo))
            .PipeTo(Self);
    }

    private void CreateGameFromSave(LobbyProtocol.CreateGameFromSave msg)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(msg.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(msg.SavePath);
        }
        catch (ArgumentException ex)
        {
            Sender.Tell(new Status.Failure(ex));
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var replyTo = Sender;
        _sessions.Ask<SessionInfo>(new SessionProtocol.InitializeFromSave(sessionId, msg.SavePath), AskTimeout)
            .ContinueWith<object>(t => t.IsFaulted || t.IsCanceled
                ? new GameInitFailed(Flatten(t.Exception), replyTo)
                : new GameInitialized(sessionId, msg.Name, t.Result, replyTo))
            .PipeTo(Self);
    }

    private void OnGameInitialized(GameInitialized msg)
    {
        var listing = new GameListing
        {
            SessionId = msg.SessionId,
            Name = msg.Name,
            WorldId = msg.Info.WorldId,
            WorldName = msg.Info.WorldName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _games[msg.SessionId] = listing;
        msg.ReplyTo.Tell(listing);
    }

    /// <summary>Lists live games; games whose session has ended are dropped and removed.</summary>
    private Task<GameListing[]> ListGames()
    {
        var self = Self;
        var listings = _games.Values
            .OrderBy(g => g.CreatedAt)
            .ThenBy(g => g.SessionId, StringComparer.Ordinal)
            .Select(WithLiveStatus)
            .ToArray();
        return Task.WhenAll(listings).ContinueWith(
            t =>
            {
                var ended = t.Result.Where(g => g.Stage == SessionStage.Ended).Select(g => g.SessionId).ToArray();
                if (ended.Length > 0)
                {
                    self.Tell(new PruneEnded(ended));
                }

                return t.Result.Where(g => g.Stage != SessionStage.Ended).ToArray();
            },
            TaskScheduler.Default);
    }

    private void GetGame(LobbyProtocol.GetGame msg)
    {
        if (!_games.TryGetValue(msg.SessionId, out var listing))
        {
            Sender.Tell(new LobbyProtocol.GameLookupResult(null));
            return;
        }

        var self = Self;
        WithLiveStatus(listing)
            .ContinueWith(
                t =>
                {
                    if (t.Result.Stage != SessionStage.Ended)
                    {
                        return new LobbyProtocol.GameLookupResult(t.Result);
                    }

                    self.Tell(new PruneEnded([t.Result.SessionId]));
                    return new LobbyProtocol.GameLookupResult(null);
                },
                TaskScheduler.Default)
            .PipeTo(Sender);
    }

    private void PruneEndedGames(PruneEnded msg)
    {
        foreach (var sessionId in msg.SessionIds)
        {
            if (_games.Remove(sessionId))
            {
                _ = _sessions.Ask<Ack>(new SessionProtocol.Shutdown(sessionId), AskTimeout);
            }
        }
    }

    /// <summary>Fills stage and player count from the session; falls back to the stored listing on failure.</summary>
    private Task<GameListing> WithLiveStatus(GameListing listing) =>
        _sessions.Ask<SessionStatus>(new SessionProtocol.GetStatus(listing.SessionId), StatusTimeout)
            .ContinueWith(
                t => t.IsCompletedSuccessfully
                    ? listing with { Stage = t.Result.Stage, PlayerCount = t.Result.Players.Length }
                    : listing,
                TaskScheduler.Default);

    private void RemoveGame(LobbyProtocol.RemoveGame msg)
    {
        if (!_games.Remove(msg.SessionId))
        {
            Sender.Tell(false);
            return;
        }

        var replyTo = Sender;
        _sessions.Ask<Ack>(new SessionProtocol.Shutdown(msg.SessionId), AskTimeout)
            .ContinueWith<object>(_ => new RemoveGameDone(replyTo))
            .PipeTo(Self);
    }

    private static WorldOption[] ListWorlds() =>
        [.. WorldCatalog.All.Select(w => new WorldOption { WorldId = w.WorldId, DisplayName = w.DisplayName })];

    private static Exception Flatten(AggregateException? aggregate)
    {
        var flattened = aggregate?.Flatten().InnerExceptions;
        return flattened is { Count: > 0 }
            ? flattened[0]
            : new InvalidOperationException("Session initialization failed.");
    }

    private sealed record GameInitialized(string SessionId, string Name, SessionInfo Info, IActorRef ReplyTo);

    private sealed record PruneEnded(string[] SessionIds);

    private sealed record GameInitFailed(Exception Error, IActorRef ReplyTo);

    private sealed record RemoveGameDone(IActorRef ReplyTo);
}
