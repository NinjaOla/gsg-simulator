using System.Threading.Channels;

namespace SimEngine.Networking;

/// <summary>
/// A message-oriented, reliable, ordered transport (in-process, TCP, Steam, ...).
/// One implementation per <see cref="TransportEndpoint.Scheme"/>.
/// </summary>
public interface ITransport
{
    /// <summary>The endpoint scheme this transport handles.</summary>
    string Scheme { get; }

    /// <summary>Starts listening; the returned listener is bound and ready to accept.</summary>
    Task<ITransportListener> ListenAsync(TransportEndpoint endpoint, CancellationToken ct = default);

    /// <summary>Connects to a listening endpoint.</summary>
    Task<ITransportConnection> ConnectAsync(TransportEndpoint endpoint, CancellationToken ct = default);
}

/// <summary>Accepts incoming connections on a bound endpoint.</summary>
public interface ITransportListener : IAsyncDisposable
{
    /// <summary>The bound endpoint (resolved, e.g. the actual port when 0 was requested).</summary>
    TransportEndpoint LocalEndpoint { get; }

    /// <summary>
    /// Waits for the next connection. Throws <see cref="OperationCanceledException"/>
    /// on cancellation and <see cref="ObjectDisposedException"/> once the listener is disposed.
    /// </summary>
    ValueTask<ITransportConnection> AcceptAsync(CancellationToken ct = default);
}

/// <summary>
/// One reliable, ordered, message-oriented connection. Messages are whole
/// payloads up to <see cref="TransportLimits.MaxMessageBytes"/>.
/// </summary>
public interface ITransportConnection : IAsyncDisposable
{
    /// <summary>Human-readable peer description for logs.</summary>
    string RemoteDescription { get; }

    /// <summary>Received messages in order; completes when the connection closes.</summary>
    ChannelReader<ReadOnlyMemory<byte>> Incoming { get; }

    /// <summary>Completes when the connection is closed by either side.</summary>
    Task Completion { get; }

    /// <summary>
    /// Queues a message for sending without blocking; messages are delivered in
    /// call order. Returns <c>false</c> if the connection is closed.
    /// </summary>
    bool TrySend(ReadOnlyMemory<byte> message);
}

/// <summary>Limits shared by all transports.</summary>
public static class TransportLimits
{
    /// <summary>Largest single message a transport must carry.</summary>
    public const int MaxMessageBytes = 16 * 1024 * 1024;
}
