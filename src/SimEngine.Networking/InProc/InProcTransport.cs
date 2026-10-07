using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SimEngine.Networking.InProc;

/// <summary>
/// In-process transport over paired channels. Used by single-player (the
/// client and its host-owned server share one process) and tests. Listeners
/// are scoped to this instance, so client and server must share it.
/// </summary>
public sealed class InProcTransport : ITransport
{
    public const string SchemeName = "inproc";

    private readonly ConcurrentDictionary<string, InProcListener> _listeners = new(StringComparer.Ordinal);
    private long _nextConnectionId;

    public string Scheme => SchemeName;

    public Task<ITransportListener> ListenAsync(TransportEndpoint endpoint, CancellationToken ct = default)
    {
        Validate(endpoint);
        var listener = new InProcListener(endpoint, () => _listeners.TryRemove(endpoint.Address, out _));
        if (!_listeners.TryAdd(endpoint.Address, listener))
        {
            throw new InvalidOperationException($"Endpoint '{endpoint}' is already in use.");
        }

        return Task.FromResult<ITransportListener>(listener);
    }

    public Task<ITransportConnection> ConnectAsync(TransportEndpoint endpoint, CancellationToken ct = default)
    {
        Validate(endpoint);
        ct.ThrowIfCancellationRequested();
        if (!_listeners.TryGetValue(endpoint.Address, out var listener))
        {
            throw new IOException($"Nothing is listening on '{endpoint}'.");
        }

        var id = Interlocked.Increment(ref _nextConnectionId);
        var (client, server) = InProcConnection.CreatePair(endpoint.ToString(), $"inproc client #{id}");
        if (!listener.Offer(server))
        {
            throw new IOException($"Nothing is listening on '{endpoint}'.");
        }

        return Task.FromResult<ITransportConnection>(client);
    }

    private static void Validate(TransportEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme != SchemeName)
        {
            throw new ArgumentException($"Expected an '{SchemeName}://' endpoint, got '{endpoint}'.", nameof(endpoint));
        }
    }

    private sealed class InProcListener(TransportEndpoint endpoint, Action unregister) : ITransportListener
    {
        private readonly Channel<ITransportConnection> _pending = Channel.CreateUnbounded<ITransportConnection>();

        public TransportEndpoint LocalEndpoint { get; } = endpoint;

        public bool Offer(ITransportConnection connection) => _pending.Writer.TryWrite(connection);

        public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken ct = default)
        {
            try
            {
                return await _pending.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException ex)
            {
                throw new ObjectDisposedException(nameof(InProcListener), ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            unregister();
            _pending.Writer.TryComplete();
            while (_pending.Reader.TryRead(out var connection))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class InProcConnection : ITransportConnection
    {
        private readonly Channel<ReadOnlyMemory<byte>> _inbound;
        private readonly Channel<ReadOnlyMemory<byte>> _outbound;
        private readonly TaskCompletionSource _closed;

        private InProcConnection(
            string remoteDescription,
            Channel<ReadOnlyMemory<byte>> inbound,
            Channel<ReadOnlyMemory<byte>> outbound,
            TaskCompletionSource closed)
        {
            RemoteDescription = remoteDescription;
            _inbound = inbound;
            _outbound = outbound;
            _closed = closed;
        }

        public string RemoteDescription { get; }

        public ChannelReader<ReadOnlyMemory<byte>> Incoming => _inbound.Reader;

        public Task Completion => _closed.Task;

        public static (InProcConnection Client, InProcConnection Server) CreatePair(string serverDescription, string clientDescription)
        {
            var toServer = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true });
            var toClient = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true });
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return (
                new InProcConnection(serverDescription, toClient, toServer, closed),
                new InProcConnection(clientDescription, toServer, toClient, closed));
        }

        public bool TrySend(ReadOnlyMemory<byte> message)
        {
            if (message.Length > TransportLimits.MaxMessageBytes)
            {
                throw new ArgumentException($"Message exceeds {TransportLimits.MaxMessageBytes} bytes.", nameof(message));
            }

            // Copy: the caller may reuse its buffer.
            return !_closed.Task.IsCompleted && _outbound.Writer.TryWrite(message.ToArray());
        }

        public ValueTask DisposeAsync()
        {
            _inbound.Writer.TryComplete();
            _outbound.Writer.TryComplete();
            _closed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
