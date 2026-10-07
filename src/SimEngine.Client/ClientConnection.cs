using System.Collections.Concurrent;
using SimEngine.Contracts;
using SimEngine.Contracts.Wire;
using SimEngine.Networking;

namespace SimEngine.Client;

/// <summary>
/// Request/reply and push dispatch over one <see cref="ITransportConnection"/>.
/// Replies are matched by request id; everything else is handed to the push handler
/// on the read-loop thread, in arrival order.
/// </summary>
internal sealed class ClientConnection : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<ServerReply>> _pending = new();
    private readonly TaskCompletionSource<string> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ITransportConnection _transport;
    private readonly Action<ServerMessage> _onPush;
    private readonly Task _readLoop;
    private long _nextRequestId;
    private string? _disconnectReason;

    public ClientConnection(ITransportConnection transport, Action<ServerMessage> onPush)
    {
        _transport = transport;
        _onPush = onPush;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Completes with the reason once the connection is closed.</summary>
    public Task<string> Closed => _closed.Task;

    /// <summary>Sends a request and returns its typed reply; error replies are thrown as exceptions.</summary>
    public async Task<TReply> RequestAsync<TReply>(Func<long, ClientRequest> create, TimeSpan timeout, CancellationToken ct)
        where TReply : ServerReply
    {
        var reply = await SendRequestAsync(create, timeout, ct).ConfigureAwait(false);
        return reply switch
        {
            TReply typed => typed,
            ServerMessages.ErrorReply error => throw ToException(error),
            _ => throw new InvalidDataException($"Unexpected reply '{reply.GetType().Name}'."),
        };
    }

    /// <summary>Like <see cref="RequestAsync{TReply}"/> but returns <c>false</c> for an error with <paramref name="tolerated"/>.</summary>
    public async Task<bool> TryRequestAsync(Func<long, ClientRequest> create, WireErrorCode tolerated, TimeSpan timeout, CancellationToken ct)
    {
        var reply = await SendRequestAsync(create, timeout, ct).ConfigureAwait(false);
        return reply switch
        {
            ServerMessages.ErrorReply error when error.Code == tolerated => false,
            ServerMessages.ErrorReply error => throw ToException(error),
            _ => true,
        };
    }

    /// <summary>Sends a message that has no reply (best effort).</summary>
    public void Send(ClientMessage message) => _transport.TrySend(WireCodec.Encode(message));

    public async ValueTask DisposeAsync()
    {
        Close("Disconnected.");
        await _readLoop.ConfigureAwait(false);
    }

    private async Task<ServerReply> SendRequestAsync(Func<long, ClientRequest> create, TimeSpan timeout, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<ServerReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            if (_closed.Task.IsCompleted || !_transport.TrySend(WireCodec.Encode(create(id))))
            {
                throw new ConnectionLostException(_closed.Task.IsCompleted ? _closed.Task.Result : "The connection was closed.");
            }

            return await completion.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            await foreach (var data in _transport.Incoming.ReadAllAsync().ConfigureAwait(false))
            {
                ServerMessage message;
                try
                {
                    message = WireCodec.DecodeServer(data.Span);
                }
                catch (WireFormatException)
                {
                    _disconnectReason ??= "The server sent an invalid message.";
                    break;
                }

                if (message is ServerReply reply)
                {
                    if (_pending.TryRemove(reply.RequestId, out var completion))
                    {
                        completion.TrySetResult(reply);
                    }

                    continue;
                }

                if (message is ServerMessages.DisconnectingPush disconnecting)
                {
                    _disconnectReason = disconnecting.Reason;
                }

                _onPush(message);
            }
        }
        finally
        {
            Close(_disconnectReason ?? "The connection was closed.");
        }
    }

    private void Close(string reason)
    {
        if (!_closed.TrySetResult(reason))
        {
            return;
        }

        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var completion))
            {
                completion.TrySetException(new ConnectionLostException(reason));
            }
        }

        _ = _transport.DisposeAsync().AsTask();
    }

    private static Exception ToException(ServerMessages.ErrorReply error) => error.Code switch
    {
        WireErrorCode.ContentMismatch => new ContentMismatchException(error.ExpectedHash ?? string.Empty, error.ActualHash ?? string.Empty),
        WireErrorCode.CommandRejected or WireErrorCode.NotAuthorized or WireErrorCode.NotJoined => new CommandRejectedException(error.Message),
        WireErrorCode.BadRequest => new ArgumentException(error.Message),
        WireErrorCode.IoError => new IOException(error.Message),
        WireErrorCode.InvalidState => new InvalidOperationException(error.Message),
        WireErrorCode.Timeout => new TimeoutException(error.Message),
        _ => new RemoteRequestException(error.Code, error.Message),
    };
}
