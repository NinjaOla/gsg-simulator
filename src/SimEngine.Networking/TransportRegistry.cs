namespace SimEngine.Networking;

/// <summary>Resolves an <see cref="ITransport"/> by endpoint scheme.</summary>
public sealed class TransportRegistry
{
    private readonly Dictionary<string, ITransport> _transports = new(StringComparer.Ordinal);

    public TransportRegistry(params IEnumerable<ITransport> transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        foreach (var transport in transports)
        {
            _transports.Add(transport.Scheme, transport);
        }
    }

    public ITransport Get(string scheme) =>
        _transports.TryGetValue(scheme, out var transport)
            ? transport
            : throw new NotSupportedException($"No transport registered for scheme '{scheme}'.");

    public Task<ITransportListener> ListenAsync(TransportEndpoint endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return Get(endpoint.Scheme).ListenAsync(endpoint, ct);
    }

    public Task<ITransportConnection> ConnectAsync(TransportEndpoint endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return Get(endpoint.Scheme).ConnectAsync(endpoint, ct);
    }
}
