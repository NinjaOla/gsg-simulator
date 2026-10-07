using System.Text;
using Akka.Actor;
using Akka.Event;
using SimEngine.Contracts;
using SimEngine.Contracts.Wire;
using SimEngine.Networking;
using static SimEngine.Contracts.Wire.ServerMessages;

namespace SimEngine.Server.Gateway;

/// <summary>
/// One client connection: decodes wire messages, binds the player identity
/// (assigned at handshake, never taken from the client afterwards), authorizes
/// requests, and translates them to lobby/session actor messages. It is also
/// the session stream subscriber, relaying updates out and acks back in.
/// </summary>
/// <remarks>
/// Requests run asynchronously and their results come back as <see cref="Completed"/>
/// messages, so connection state only changes on the actor thread.
/// </remarks>
public sealed class ConnectionActor : ReceiveActor
{
    private const int MaxPlayerNameLength = 16;
    private const int MaxDisplayNameLength = 24;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly HashSet<string> _createdGames = new(StringComparer.Ordinal);
    private readonly ITransportConnection _connection;
    private readonly IActorRef _sessions;
    private readonly IActorRef _lobby;
    private readonly GatewayOptions _options;

    private string? _playerId;
    private string _playerName = string.Empty;
    private string? _sessionId;
    private bool _subscribed;

    public static Props Props(ITransportConnection connection, IActorRef sessions, IActorRef lobby, GatewayOptions options) =>
        Akka.Actor.Props.Create(() => new ConnectionActor(connection, sessions, lobby, options));

    public ConnectionActor(ITransportConnection connection, IActorRef sessions, IActorRef lobby, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(lobby);
        ArgumentNullException.ThrowIfNull(options);
        _connection = connection;
        _sessions = sessions;
        _lobby = lobby;
        _options = options;

        Receive<Inbound>(OnInbound);
        Receive<Completed>(OnCompleted);
        Receive<ConnectionClosed>(_ => Context.Stop(Self));
        Receive<SessionStreamUpdate>(update =>
        {
            if (update.SessionId == _sessionId)
            {
                Send(new StreamUpdatePush(update));
            }
        });
        Receive<SessionSnapshot>(snapshot =>
        {
            if (snapshot.SessionId == _sessionId)
            {
                Send(new StreamSnapshotPush(snapshot));
            }
        });
        Receive<SessionProtocol.SessionEnded>(ended =>
        {
            if (ended.SessionId == _sessionId)
            {
                _sessionId = null;
                _subscribed = false;
            }

            Send(new SessionEndedPush(ended.SessionId, ended.Reason));
        });
    }

    protected override void PreStart()
    {
        var self = Self;
        var incoming = _connection.Incoming;
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var data in incoming.ReadAllAsync().ConfigureAwait(false))
                {
                    self.Tell(new Inbound(data));
                }
            }
            finally
            {
                self.Tell(ConnectionClosed.Instance);
            }
        });
    }

    protected override void PostStop()
    {
        if (_sessionId is not null && _playerId is not null)
        {
            // Before the start this is a leave; the host disconnecting ends the session for everyone.
            _ = _sessions.Ask<Ack>(new SessionProtocol.Disconnect(_sessionId, _playerId), _options.RequestTimeout);
        }

        _ = _connection.DisposeAsync().AsTask();
        _log.Info("Client {0} ({1}) disconnected", _playerId ?? "(no handshake)", _connection.RemoteDescription);
        base.PostStop();
    }

    private void OnInbound(Inbound inbound)
    {
        ClientMessage message;
        try
        {
            message = WireCodec.DecodeClient(inbound.Data.Span);
        }
        catch (WireFormatException ex)
        {
            _log.Warning("Malformed message from {0}: {1}", _connection.RemoteDescription, ex.Message);
            Disconnect("Malformed message.");
            return;
        }

        if (_playerId is null)
        {
            HandleHandshake(message);
            return;
        }

        switch (message)
        {
            case ClientMessages.AckUpdate ack:
                if (ack.SessionId == _sessionId)
                {
                    _sessions.Tell(new SessionProtocol.AckUpdate(ack.SessionId, ack.Sequence), Self);
                }

                break;
            case SessionRequest request when request.SessionId != _sessionId:
                Send(new ErrorReply(request.RequestId, WireErrorCode.NotJoined, $"You have not joined session '{request.SessionId}'."));
                break;
            case ClientRequest request:
                Handle(request);
                break;
        }
    }

    private void HandleHandshake(ClientMessage message)
    {
        if (message is not ClientMessages.Hello hello)
        {
            if (message is ClientRequest request)
            {
                Send(new ErrorReply(request.RequestId, WireErrorCode.HandshakeRequired, "Send Hello first."));
            }

            Disconnect("Handshake required.");
            return;
        }

        if (hello.ProtocolVersion != WireProtocol.Version)
        {
            Send(new ErrorReply(
                hello.RequestId,
                WireErrorCode.ProtocolVersionMismatch,
                $"The server speaks protocol version {WireProtocol.Version}; the client speaks {hello.ProtocolVersion}."));
            Disconnect("Protocol version mismatch.");
            return;
        }

        _playerId = CreatePlayerId(hello.PlayerName);
        _playerName = CreateDisplayName(hello.PlayerName);
        _log.Info("Client {0} authenticated as {1}", _connection.RemoteDescription, _playerId);
        Send(new WelcomeReply(hello.RequestId, _playerId, WireProtocol.Version));
    }

    private void Handle(ClientRequest request)
    {
        var id = request.RequestId;
        var playerId = _playerId!;
        var playerName = _playerName;
        var timeout = _options.RequestTimeout;
        var self = Self;

        switch (request)
        {
            case ClientMessages.Hello:
                Send(new ErrorReply(id, WireErrorCode.BadRequest, "Already connected."));
                break;

            case ClientMessages.ListWorlds:
                Run(id, async () => Reply(new WorldsReply(id,
                    await _lobby.Ask<WorldOption[]>(new LobbyProtocol.ListWorlds(), timeout).ConfigureAwait(false))));
                break;

            case ClientMessages.ListGames:
                Run(id, async () => Reply(new GamesReply(id,
                    await _lobby.Ask<GameListing[]>(new LobbyProtocol.ListGames(), timeout).ConfigureAwait(false))));
                break;

            case ClientMessages.GetGame msg:
                Run(id, async () => Reply(new GameReply(id, await FindGameAsync(msg.SessionId, timeout).ConfigureAwait(false))));
                break;

            case ClientMessages.CreateGame msg:
                Run(id, async () =>
                {
                    var listing = await _lobby.Ask<GameListing>(new LobbyProtocol.CreateGame(msg.Request), timeout).ConfigureAwait(false);
                    return new Completed(new GameReply(id, listing), () => _createdGames.Add(listing.SessionId));
                });
                break;

            case ClientMessages.LoadGame msg:
                Run(id, async () =>
                {
                    var path = SavePaths.Resolve(msg.SavePath, _options);
                    var listing = await _lobby.Ask<GameListing>(new LobbyProtocol.CreateGameFromSave(msg.Name, path), timeout).ConfigureAwait(false);
                    return new Completed(new GameReply(id, listing), () => _createdGames.Add(listing.SessionId));
                });
                break;

            case ClientMessages.RemoveGame msg:
                {
                    var created = _createdGames.Contains(msg.SessionId);
                    Run(id, async () =>
                    {
                        _ = await FindGameAsync(msg.SessionId, timeout).ConfigureAwait(false)
                            ?? throw new GatewayRejectedException(WireErrorCode.UnknownSession, $"Unknown game '{msg.SessionId}'.");
                        if (!created)
                        {
                            var status = await _sessions.Ask<SessionStatus>(new SessionProtocol.GetStatus(msg.SessionId), timeout).ConfigureAwait(false);
                            if (status.HostPlayerId != playerId)
                            {
                                throw new GatewayRejectedException(WireErrorCode.NotAuthorized, "Only the game's creator or host may remove it.");
                            }
                        }

                        await _lobby.Ask<bool>(new LobbyProtocol.RemoveGame(msg.SessionId), timeout).ConfigureAwait(false);
                        return new Completed(new OkReply(id), () => _createdGames.Remove(msg.SessionId));
                    });
                    break;
                }

            case ClientMessages.JoinGame msg:
                {
                    var current = _sessionId;
                    var subscribed = _subscribed;
                    Run(id, async () =>
                    {
                        _ = await FindGameAsync(msg.SessionId, timeout).ConfigureAwait(false)
                            ?? throw new GatewayRejectedException(WireErrorCode.UnknownSession, $"Unknown game '{msg.SessionId}'.");
                        if (current is not null && current != msg.SessionId)
                        {
                            await LeaveAsync(current, playerId, subscribed, self, timeout).ConfigureAwait(false);
                        }

                        await _sessions.Ask<Ack>(new SessionProtocol.Join(msg.SessionId, playerId, msg.ContentHash, playerName), timeout).ConfigureAwait(false);
                        return new Completed(new OkReply(id), () =>
                        {
                            if (_sessionId != msg.SessionId)
                            {
                                _sessionId = msg.SessionId;
                                _subscribed = false;
                            }
                        });
                    });
                    break;
                }

            case ClientMessages.LeaveGame msg:
                {
                    var subscribed = _subscribed;
                    Run(id, async () =>
                    {
                        await LeaveAsync(msg.SessionId, playerId, subscribed, self, timeout).ConfigureAwait(false);
                        return new Completed(new OkReply(id), () =>
                        {
                            if (_sessionId == msg.SessionId)
                            {
                                _sessionId = null;
                                _subscribed = false;
                            }
                        });
                    });
                    break;
                }

            case ClientMessages.StartGame msg:
                Run(id, async () =>
                {
                    await _sessions.Ask<Ack>(new SessionProtocol.StartGame(msg.SessionId, playerId, msg.Paused), timeout).ConfigureAwait(false);
                    return Reply(new OkReply(id));
                });
                break;

            case ClientMessages.UpdateSettings msg:
                Run(id, async () =>
                {
                    await _sessions.Ask<Ack>(new SessionProtocol.UpdateSettings(msg.SessionId, playerId, msg.Settings), timeout).ConfigureAwait(false);
                    return Reply(new OkReply(id));
                });
                break;

            case ClientMessages.SendCommands msg:
                if (msg.Commands.Length == 0 || msg.Commands.Length > _options.MaxCommandsPerRequest)
                {
                    Send(new ErrorReply(id, WireErrorCode.BadRequest, $"Send between 1 and {_options.MaxCommandsPerRequest} commands."));
                    break;
                }

                Run(id, async () =>
                {
                    await _sessions.Ask<Ack>(new SessionProtocol.EnqueueCommands(msg.SessionId, playerId, msg.Commands), timeout).ConfigureAwait(false);
                    return Reply(new OkReply(id));
                });
                break;

            case ClientMessages.DebugStep msg:
                if (msg.Ticks < 0 || msg.Ticks > _options.MaxDebugStepTicks)
                {
                    Send(new ErrorReply(id, WireErrorCode.BadRequest, $"Ticks must be between 0 and {_options.MaxDebugStepTicks}."));
                    break;
                }

                Run(id, async () => Reply(new TickReply(id,
                    await _sessions.Ask<TickResult>(new SessionProtocol.DebugStep(msg.SessionId, playerId, msg.Ticks), timeout).ConfigureAwait(false))));
                break;

            case ClientMessages.SaveGame msg:
                {
                    var revealPath = _options.AllowArbitrarySavePaths;
                    Run(id, async () =>
                    {
                        var path = SavePaths.Resolve(msg.SavePath, _options);
                        var status = await _sessions.Ask<SessionStatus>(new SessionProtocol.GetStatus(msg.SessionId), timeout).ConfigureAwait(false);
                        if (status.HostPlayerId != playerId)
                        {
                            throw new GatewayRejectedException(WireErrorCode.NotAuthorized, "Only the host may save the game.");
                        }

                        var saved = await _sessions.Ask<string>(new SessionProtocol.Save(msg.SessionId, path), timeout).ConfigureAwait(false);
                        return Reply(new SavedReply(id, revealPath ? saved : Path.GetFileName(saved)));
                    });
                    break;
                }

            case ClientMessages.GetStatus msg:
                Run(id, async () => Reply(new StatusReply(id,
                    await _sessions.Ask<SessionStatus>(new SessionProtocol.GetStatus(msg.SessionId), timeout).ConfigureAwait(false))));
                break;

            case ClientMessages.Subscribe msg:
                // Subscribe before taking the snapshot so no update falls in between.
                Run(id, async () =>
                {
                    await _sessions.Ask<Ack>(new SessionProtocol.Subscribe(msg.SessionId, self), timeout).ConfigureAwait(false);
                    var snapshot = await _sessions.Ask<SessionSnapshot>(new SessionProtocol.GetSnapshot(msg.SessionId), timeout).ConfigureAwait(false);
                    return new Completed(new SnapshotReply(id, snapshot), () => _subscribed = _sessionId == msg.SessionId);
                });
                break;

            case ClientMessages.Unsubscribe msg:
                Run(id, async () =>
                {
                    await _sessions.Ask<Ack>(new SessionProtocol.Unsubscribe(msg.SessionId, self), timeout).ConfigureAwait(false);
                    return new Completed(new OkReply(id), () => _subscribed = false);
                });
                break;

            default:
                Send(new ErrorReply(id, WireErrorCode.BadRequest, $"Unsupported request '{request.GetType().Name}'."));
                break;
        }
    }

    private async Task<GameListing?> FindGameAsync(string sessionId, TimeSpan timeout)
    {
        var result = await _lobby.Ask<LobbyProtocol.GameLookupResult>(new LobbyProtocol.GetGame(sessionId), timeout).ConfigureAwait(false);
        return result.Listing;
    }

    private async Task LeaveAsync(string sessionId, string playerId, bool subscribed, IActorRef self, TimeSpan timeout)
    {
        if (subscribed)
        {
            await _sessions.Ask<Ack>(new SessionProtocol.Unsubscribe(sessionId, self), timeout).ConfigureAwait(false);
        }

        await _sessions.Ask<Ack>(new SessionProtocol.Leave(sessionId, playerId), timeout).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="work"/> off the actor thread and pipes its reply (or mapped error) back.</summary>
    private void Run(long requestId, Func<Task<Completed>> work)
    {
        Task<Completed> task;
        try
        {
            task = work();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            task = Task.FromException<Completed>(ex);
        }

        task.ContinueWith(
                t => t.IsCompletedSuccessfully
                    ? t.Result
                    : new Completed(ToError(requestId, t.Exception?.GetBaseException()), null, t.Exception?.GetBaseException()),
                TaskScheduler.Default)
            .PipeTo(Self);
    }

    private void OnCompleted(Completed completed)
    {
        if (completed.Reply is ErrorReply { Code: WireErrorCode.Internal })
        {
            _log.Error(completed.Error, "Request from {0} failed", _playerId);
        }

        completed.Apply?.Invoke();
        Send(completed.Reply);
    }

    private void Send(ServerMessage message)
    {
        if (!_connection.TrySend(WireCodec.Encode(message)))
        {
            Context.Stop(Self);
        }
    }

    private void Disconnect(string reason)
    {
        Send(new DisconnectingPush(reason));
        Context.Stop(Self);
    }

    private static Completed Reply(ServerMessage reply) => new(reply, null);

    private static ErrorReply ToError(long requestId, Exception? error) => error switch
    {
        GatewayRejectedException rejection => new ErrorReply(requestId, rejection.Code, rejection.Message),
        ContentMismatchException mismatch => new ErrorReply(
            requestId, WireErrorCode.ContentMismatch, mismatch.Message, mismatch.ExpectedHash, mismatch.ActualHash),
        CommandRejectedException rejected => new ErrorReply(requestId, WireErrorCode.CommandRejected, rejected.Message),
        AskTimeoutException or TaskCanceledException => new ErrorReply(requestId, WireErrorCode.Timeout, "The server did not respond in time."),
        ArgumentException argument => new ErrorReply(requestId, WireErrorCode.BadRequest, argument.Message),
        FileNotFoundException or DirectoryNotFoundException => new ErrorReply(requestId, WireErrorCode.IoError, "Save file not found."),
        InvalidDataException invalid => new ErrorReply(requestId, WireErrorCode.IoError, invalid.Message),
        IOException or UnauthorizedAccessException => new ErrorReply(requestId, WireErrorCode.IoError, "Could not read or write the save file."),
        InvalidOperationException invalid => new ErrorReply(requestId, WireErrorCode.InvalidState, invalid.Message),
        _ => new ErrorReply(requestId, WireErrorCode.Internal, "Internal server error."),
    };

    /// <summary>Readable, unique id: sanitized name plus a random suffix.</summary>
    private static string CreatePlayerId(string? name)
    {
        var builder = new StringBuilder(MaxPlayerNameLength);
        foreach (var c in name ?? string.Empty)
        {
            if (builder.Length == MaxPlayerNameLength)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
        }

        var baseName = builder.Length > 0 ? builder.ToString() : "player";
        return $"{baseName}-{Guid.NewGuid():N}"[..(baseName.Length + 7)];
    }

    /// <summary>Display name: printable characters only, trimmed and length-limited.</summary>
    private static string CreateDisplayName(string? name)
    {
        var builder = new StringBuilder(MaxDisplayNameLength);
        foreach (var c in (name ?? string.Empty).Trim())
        {
            if (builder.Length == MaxDisplayNameLength)
            {
                break;
            }

            if (!char.IsControl(c) && !char.IsSurrogate(c))
            {
                builder.Append(c);
            }
        }

        var displayName = builder.ToString().Trim();
        return displayName.Length > 0 ? displayName : "player";
    }

    private sealed record Inbound(ReadOnlyMemory<byte> Data);

    private sealed record ConnectionClosed
    {
        public static readonly ConnectionClosed Instance = new();
    }

    private sealed record Completed(ServerMessage Reply, Action? Apply, Exception? Error = null);

    private sealed class GatewayRejectedException(WireErrorCode code, string message) : Exception(message)
    {
        public WireErrorCode Code { get; } = code;
    }
}
