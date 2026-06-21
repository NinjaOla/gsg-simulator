namespace SimEngine.Server;

/// <summary>
/// Options for hosting a SimEngine silo. The defaults match the in-process
/// single-player setup (Orleans localhost clustering on its default ports with
/// the session stream enabled); a network host overrides the ports so external
/// client processes can connect over the loopback gateway.
/// </summary>
public sealed class SimEngineSiloOptions
{
    /// <summary>Orleans localhost-clustering default silo-to-silo port.</summary>
    public const int DefaultSiloPort = 11111;

    /// <summary>Orleans localhost-clustering default client gateway port.</summary>
    public const int DefaultGatewayPort = 30000;

    /// <summary>Silo-to-silo TCP port (used by membership/clustering).</summary>
    public int SiloPort { get; set; } = DefaultSiloPort;

    /// <summary>
    /// Client gateway TCP port. External clients connect here; set to 0 to host
    /// a silo with no inbound client gateway (in-process clients only).
    /// </summary>
    public int GatewayPort { get; set; } = DefaultGatewayPort;

    /// <summary>
    /// Whether to enable the in-memory session stream (and its <c>PubSubStore</c>).
    /// Required for clients to receive delta-sync updates without polling.
    /// </summary>
    public bool EnableStreams { get; set; } = true;
}
