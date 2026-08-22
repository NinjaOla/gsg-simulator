using Akka.Actor;
using SimEngine.Contracts;

namespace SimEngine.Server.Actors;

/// <summary>
/// Akka actor representing a single player. Buffers the player's commands and
/// tracks which session the player has joined; flushing forwards the buffered
/// commands to the session actor (routed through <see cref="_sessions"/>),
/// which applies them at its next step boundary. The entity id is the player id.
/// </summary>
public sealed class PlayerActor : ReceiveActor
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(30);

    private readonly List<PlayerCommand> _buffer = [];
    private readonly string _playerId;
    private readonly IActorRef _sessions;
    private string? _sessionId;

    /// <summary>Creates the props for a player actor with the given entity id.</summary>
    public static Props Props(string playerId, IActorRef sessions) =>
        Akka.Actor.Props.Create(() => new PlayerActor(playerId, sessions));

    public PlayerActor(string playerId, IActorRef sessions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentNullException.ThrowIfNull(sessions);
        _playerId = playerId;
        _sessions = sessions;

        Receive<PlayerProtocol.JoinSession>(JoinSession);
        Receive<JoinSucceeded>(m => { _sessionId = m.SessionId; m.ReplyTo.Tell(Ack.Instance); });
        Receive<JoinFailed>(m => m.ReplyTo.Tell(new Status.Failure(m.Error)));
        Receive<PlayerProtocol.LeaveSession>(LeaveSession);
        Receive<LeaveDone>(m => { _sessionId = null; _buffer.Clear(); m.ReplyTo.Tell(Ack.Instance); });
        Receive<PlayerProtocol.SubmitCommand>(SubmitCommand);
        Receive<PlayerProtocol.FlushCommands>(FlushCommands);
        Receive<FlushDone>(m => { _buffer.Clear(); m.ReplyTo.Tell(m.Count); });
        Receive<PlayerProtocol.GetCurrentSession>(_ =>
            Sender.Tell(new PlayerProtocol.CurrentSessionResult(_sessionId)));
        Receive<PlayerProtocol.GetBufferedCommandCount>(_ => Sender.Tell(_buffer.Count));
    }

    private void JoinSession(PlayerProtocol.JoinSession msg)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(msg.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(msg.ContentHash);
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            Sender.Tell(new Status.Failure(ex));
            return;
        }

        if (_sessionId is not null)
        {
            if (string.Equals(_sessionId, msg.SessionId, StringComparison.Ordinal))
            {
                Sender.Tell(Ack.Instance);
                return;
            }

            Sender.Tell(new Status.Failure(new InvalidOperationException(
                $"Player is already in session '{_sessionId}'. Leave it before joining another.")));
            return;
        }

        var replyTo = Sender;
        var sessionId = msg.SessionId;
        _sessions.Ask<Ack>(new SessionProtocol.Join(sessionId, _playerId, msg.ContentHash), AskTimeout)
            .ContinueWith<object>(t => t.IsFaulted || t.IsCanceled
                ? new JoinFailed(Flatten(t.Exception), replyTo)
                : new JoinSucceeded(sessionId, replyTo))
            .PipeTo(Self);
    }

    private void LeaveSession(PlayerProtocol.LeaveSession msg)
    {
        if (_sessionId is null)
        {
            Sender.Tell(Ack.Instance);
            return;
        }

        var replyTo = Sender;
        var sessionId = _sessionId;
        _sessions.Ask<Ack>(new SessionProtocol.Leave(sessionId, _playerId), AskTimeout)
            .ContinueWith<object>(_ => new LeaveDone(replyTo))
            .PipeTo(Self);
    }

    private void SubmitCommand(PlayerProtocol.SubmitCommand msg)
    {
        if (msg.Command is null)
        {
            Sender.Tell(new Status.Failure(new ArgumentNullException(nameof(msg))));
            return;
        }

        if (_sessionId is null)
        {
            Sender.Tell(new Status.Failure(NotJoined()));
            return;
        }

        _buffer.Add(msg.Command);
        Sender.Tell(Ack.Instance);
    }

    private void FlushCommands(PlayerProtocol.FlushCommands msg)
    {
        if (_sessionId is null)
        {
            Sender.Tell(new Status.Failure(NotJoined()));
            return;
        }

        if (_buffer.Count == 0)
        {
            Sender.Tell(0);
            return;
        }

        var commands = _buffer.ToArray();
        var replyTo = Sender;
        _sessions.Ask<Ack>(new SessionProtocol.EnqueueCommands(_sessionId, commands), AskTimeout)
            .ContinueWith<object>(_ => new FlushDone(commands.Length, replyTo))
            .PipeTo(Self);
    }

    private static InvalidOperationException NotJoined() =>
        new("Player has not joined a session. Send a JoinSession message first.");

    private static Exception Flatten(AggregateException? aggregate)
    {
        var flattened = aggregate?.Flatten().InnerExceptions;
        return flattened is { Count: > 0 }
            ? flattened[0]
            : new InvalidOperationException("Join failed.");
    }

    private sealed record JoinSucceeded(string SessionId, IActorRef ReplyTo);

    private sealed record JoinFailed(Exception Error, IActorRef ReplyTo);

    private sealed record LeaveDone(IActorRef ReplyTo);

    private sealed record FlushDone(int Count, IActorRef ReplyTo);
}
