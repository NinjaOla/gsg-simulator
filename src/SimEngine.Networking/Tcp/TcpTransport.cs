using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace SimEngine.Networking.Tcp;

/// <summary>Timing options for <see cref="TcpTransport"/>.</summary>
public sealed record TcpTransportOptions
{
    /// <summary>How often each side sends a heartbeat frame.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>A connection that receives nothing (not even heartbeats) for this long is closed.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Maximum time to establish an outgoing connection.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long disposing a connection waits for queued messages to be written.</summary>
    public TimeSpan CloseFlushTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// TCP transport for <c>tcp://host:port</c> endpoints. Each message is one
/// frame with a 4-byte big-endian length prefix (max <see cref="TransportLimits.MaxMessageBytes"/>).
/// A zero-length frame is a heartbeat: both sides send one every
/// <see cref="TcpTransportOptions.HeartbeatInterval"/> and close the connection after
/// <see cref="TcpTransportOptions.IdleTimeout"/> without receiving anything.
/// Port 0 binds an ephemeral port, reported by <see cref="ITransportListener.LocalEndpoint"/>.
/// </summary>
public sealed class TcpTransport : ITransport
{
    public const string SchemeName = "tcp";

    private readonly TcpTransportOptions _options;

    public TcpTransport(TcpTransportOptions? options = null)
    {
        _options = options ?? new TcpTransportOptions();
        if (_options.IdleTimeout <= _options.HeartbeatInterval)
        {
            throw new ArgumentException("The idle timeout must be longer than the heartbeat interval.", nameof(options));
        }
    }

    public string Scheme => SchemeName;

    public async Task<ITransportListener> ListenAsync(TransportEndpoint endpoint, CancellationToken ct = default)
    {
        var (host, port) = ParseAddress(endpoint);
        var address = await ResolveAsync(host, ct).ConfigureAwait(false);
        var listener = new TcpListener(address, port);
        try
        {
            listener.Start();
        }
        catch
        {
            listener.Dispose();
            throw;
        }

        return new Listener(listener, _options);
    }

    public async Task<ITransportConnection> ConnectAsync(TransportEndpoint endpoint, CancellationToken ct = default)
    {
        var (host, port) = ParseAddress(endpoint);
        if (port == 0)
        {
            throw new ArgumentException($"'{endpoint}' has no port to connect to.", nameof(endpoint));
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_options.ConnectTimeout);
            try
            {
                await socket.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out connecting to '{endpoint}'.");
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new Connection(socket, _options);
    }

    /// <summary>Builds a <c>tcp://</c> endpoint, bracketing IPv6 hosts.</summary>
    public static TransportEndpoint CreateEndpoint(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        var formattedHost = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        return new TransportEndpoint(SchemeName, $"{formattedHost}:{port.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Parses user input of the form <c>host</c>, <c>host:port</c>, or <c>[ipv6]:port</c>
    /// into a <c>tcp://</c> endpoint, using <paramref name="defaultPort"/> when no port is given.
    /// </summary>
    public static bool TryCreateEndpoint(string? hostAndPort, int defaultPort, out TransportEndpoint? endpoint)
    {
        endpoint = null;
        var text = hostAndPort?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string host;
        var port = defaultPort;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0 && (rest[0] != ':' || !TryParsePort(rest[1..], out port)))
            {
                return false;
            }
        }
        else
        {
            var colon = text.LastIndexOf(':');
            if (colon >= 0 && text.IndexOf(':', StringComparison.Ordinal) == colon)
            {
                host = text[..colon];
                if (!TryParsePort(text[(colon + 1)..], out port))
                {
                    return false;
                }
            }
            else
            {
                // No port, or a bare IPv6 address.
                host = text;
            }
        }

        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || port is < 1 or > IPEndPoint.MaxPort)
        {
            return false;
        }

        endpoint = CreateEndpoint(host, port);
        return true;
    }

    /// <summary>Splits a <c>tcp://host:port</c> endpoint; throws <see cref="ArgumentException"/> if malformed.</summary>
    public static (string Host, int Port) ParseAddress(TransportEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme != SchemeName)
        {
            throw new ArgumentException($"Expected a '{SchemeName}://' endpoint, got '{endpoint}'.", nameof(endpoint));
        }

        var address = endpoint.Address;
        var colon = address.LastIndexOf(':');
        if (colon <= 0 || !TryParsePort(address[(colon + 1)..], out var port))
        {
            throw new ArgumentException($"'{endpoint}' is not a valid TCP endpoint (expected tcp://host:port).", nameof(endpoint));
        }

        var host = address[..colon];
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }
        else if (host.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{endpoint}' is not a valid TCP endpoint (IPv6 hosts need brackets).", nameof(endpoint));
        }

        if (host.Length == 0)
        {
            throw new ArgumentException($"'{endpoint}' has no host.", nameof(endpoint));
        }

        return (host, port);
    }

    private static bool TryParsePort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port <= IPEndPoint.MaxPort;

    private static async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new SocketException((int)SocketError.HostNotFound);
    }

    private static TransportEndpoint ToEndpoint(EndPoint? endPoint) => endPoint is IPEndPoint ip
        ? CreateEndpoint((ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4() : ip.Address).ToString(), ip.Port)
        : new TransportEndpoint(SchemeName, "unknown:0");

    private sealed class Listener : ITransportListener
    {
        private readonly TcpListener _listener;
        private readonly TcpTransportOptions _options;
        private int _disposed;

        public Listener(TcpListener listener, TcpTransportOptions options)
        {
            _listener = listener;
            _options = options;
            LocalEndpoint = ToEndpoint(listener.LocalEndpoint);
        }

        public TransportEndpoint LocalEndpoint { get; }

        public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (Volatile.Read(ref _disposed) != 0
                && ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                throw new ObjectDisposedException(nameof(Listener), ex);
            }

            socket.NoDelay = true;
            return new Connection(socket, _options);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _listener.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Connection : ITransportConnection
    {
        private const int HeaderBytes = 4;
        private static readonly byte[] HeartbeatFrame = new byte[HeaderBytes];

        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly TcpTransportOptions _options;
        private readonly Channel<ReadOnlyMemory<byte>> _incoming =
            Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Channel<ReadOnlyMemory<byte>> _outgoing =
            Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _readLoop;
        private readonly Task _writeLoop;
        private readonly Task _heartbeatLoop;
        private long _lastReceived;
        private int _aborted;

        public Connection(Socket socket, TcpTransportOptions options)
        {
            _socket = socket;
            _options = options;
            _stream = new NetworkStream(socket, ownsSocket: true);
            RemoteDescription = ToEndpoint(socket.RemoteEndPoint).ToString();
            _lastReceived = Environment.TickCount64;
            _readLoop = Task.Run(ReadLoopAsync);
            _writeLoop = Task.Run(WriteLoopAsync);
            _heartbeatLoop = Task.Run(HeartbeatLoopAsync);
        }

        public string RemoteDescription { get; }

        public ChannelReader<ReadOnlyMemory<byte>> Incoming => _incoming.Reader;

        public Task Completion => _closed.Task;

        public bool TrySend(ReadOnlyMemory<byte> message)
        {
            if (message.Length == 0)
            {
                throw new ArgumentException("Messages must not be empty.", nameof(message));
            }

            if (message.Length > TransportLimits.MaxMessageBytes)
            {
                throw new ArgumentException($"Message exceeds {TransportLimits.MaxMessageBytes} bytes.", nameof(message));
            }

            // One buffer per frame: a copy is needed anyway, and NoDelay sends header and payload together.
            var frame = new byte[HeaderBytes + message.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame, message.Length);
            message.Span.CopyTo(frame.AsSpan(HeaderBytes));
            return !_closed.Task.IsCompleted && _outgoing.Writer.TryWrite(frame);
        }

        public async ValueTask DisposeAsync()
        {
            // Flush what is queued (e.g. a final Disconnecting message), then close.
            _outgoing.Writer.TryComplete();
            await Task.WhenAny(_writeLoop, Task.Delay(_options.CloseFlushTimeout)).ConfigureAwait(false);
            Abort();
            await Task.WhenAll(_readLoop, _writeLoop, _heartbeatLoop).ConfigureAwait(false);
            _cts.Dispose();
        }

        private async Task ReadLoopAsync()
        {
            var header = new byte[HeaderBytes];
            var ct = _cts.Token;
            try
            {
                while (true)
                {
                    if (await _stream.ReadAtLeastAsync(header, HeaderBytes, throwOnEndOfStream: false, ct).ConfigureAwait(false) < HeaderBytes)
                    {
                        break;
                    }

                    Touch();
                    var length = BinaryPrimitives.ReadInt32BigEndian(header);
                    if (length == 0)
                    {
                        continue;
                    }

                    if (length < 0 || length > TransportLimits.MaxMessageBytes)
                    {
                        break;
                    }

                    var payload = new byte[length];
                    var read = 0;
                    while (read < length)
                    {
                        var n = await _stream.ReadAsync(payload.AsMemory(read), ct).ConfigureAwait(false);
                        if (n == 0)
                        {
                            return;
                        }

                        read += n;
                        Touch();
                    }

                    if (!_incoming.Writer.TryWrite(payload))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
            finally
            {
                Abort();
            }
        }

        private async Task WriteLoopAsync()
        {
            var ct = _cts.Token;
            try
            {
                await foreach (var frame in _outgoing.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await _stream.WriteAsync(frame, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
            finally
            {
                Abort();
            }
        }

        private async Task HeartbeatLoopAsync()
        {
            using var timer = new PeriodicTimer(_options.HeartbeatInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
                {
                    if (Environment.TickCount64 - Volatile.Read(ref _lastReceived) > (long)_options.IdleTimeout.TotalMilliseconds)
                    {
                        Abort();
                        return;
                    }

                    _outgoing.Writer.TryWrite(HeartbeatFrame);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void Touch() => Volatile.Write(ref _lastReceived, Environment.TickCount64);

        private void Abort()
        {
            if (Interlocked.Exchange(ref _aborted, 1) != 0)
            {
                return;
            }

            _incoming.Writer.TryComplete();
            _outgoing.Writer.TryComplete();
            _cts.Cancel();
            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
            }

            _stream.Dispose();
            _closed.TrySetResult();
        }
    }
}
