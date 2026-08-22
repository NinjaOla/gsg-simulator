using Akka.Actor;
using SimEngine.Contracts;
using SimEngine.Server.Worlds;

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
        Receive<GameInitialized>(OnGameInitialized);
        Receive<GameInitFailed>(m => m.ReplyTo.Tell(new Status.Failure(m.Error)));
        Receive<LobbyProtocol.ListGames>(_ => Sender.Tell(ListGames()));
        Receive<LobbyProtocol.GetGame>(msg =>
            Sender.Tell(new LobbyProtocol.GameLookupResult(_games.GetValueOrDefault(msg.SessionId))));
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
                : new GameInitialized(sessionId, request, t.Result, replyTo))
            .PipeTo(Self);
    }

    private void OnGameInitialized(GameInitialized msg)
    {
        var listing = new GameListing
        {
            SessionId = msg.SessionId,
            Name = msg.Request.Name,
            WorldId = msg.Request.WorldId,
            WorldName = msg.Info.WorldName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _games[msg.SessionId] = listing;
        msg.ReplyTo.Tell(listing);
    }

    private GameListing[] ListGames() =>
        [.. _games.Values
            .OrderBy(g => g.CreatedAt)
            .ThenBy(g => g.SessionId, StringComparer.Ordinal)];

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

    private sealed record GameInitialized(string SessionId, CreateGameRequest Request, SessionInfo Info, IActorRef ReplyTo);

    private sealed record GameInitFailed(Exception Error, IActorRef ReplyTo);

    private sealed record RemoveGameDone(IActorRef ReplyTo);
}
