using Akka.Actor;
using Akka.Event;
using SimEngine.Networking;

namespace SimEngine.Server.Gateway;

/// <summary>
/// Network edge of the server: accepts connections from one
/// <see cref="ITransportListener"/> (in-process, TCP, Steam, ...) and spawns a
/// <see cref="ConnectionActor"/> per client. Owns and disposes the listener.
/// </summary>
public sealed class GatewayActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ITransportListener _listener;
    private readonly IActorRef _sessions;
    private readonly IActorRef _lobby;
    private readonly GatewayOptions _options;

    public static Props Props(ITransportListener listener, IActorRef sessions, IActorRef lobby, GatewayOptions options) =>
        Akka.Actor.Props.Create(() => new GatewayActor(listener, sessions, lobby, options));

    public GatewayActor(ITransportListener listener, IActorRef sessions, IActorRef lobby, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(lobby);
        ArgumentNullException.ThrowIfNull(options);
        _listener = listener;
        _sessions = sessions;
        _lobby = lobby;
        _options = options;

        Receive<Accepted>(msg =>
        {
            _log.Info("Client connected from {0}", msg.Connection.RemoteDescription);
            Context.ActorOf(ConnectionActor.Props(msg.Connection, _sessions, _lobby, _options));
            AcceptNext();
        });
        Receive<AcceptFailed>(msg =>
        {
            if (msg.Error is ObjectDisposedException or OperationCanceledException)
            {
                return;
            }

            _log.Warning(msg.Error, "Accept failed on {0}", _listener.LocalEndpoint);
            AcceptNext();
        });
    }

    protected override void PreStart()
    {
        _log.Info("Gateway listening on {0}", _listener.LocalEndpoint);
        AcceptNext();
    }

    // A failed connection is dropped, never restarted with stale state.
    protected override SupervisorStrategy SupervisorStrategy() => new OneForOneStrategy(_ => Directive.Stop);

    protected override void PostStop()
    {
        // Ends the pending accept with ObjectDisposedException.
        _ = _listener.DisposeAsync().AsTask();
        base.PostStop();
    }

    private void AcceptNext() =>
        _listener.AcceptAsync().AsTask().PipeTo(
            Self,
            success: connection => new Accepted(connection),
            failure: error => new AcceptFailed(error));

    private sealed record Accepted(ITransportConnection Connection);

    private sealed record AcceptFailed(Exception Error);
}
