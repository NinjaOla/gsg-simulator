namespace SimEngine.Server.Gateway;

/// <summary>Configuration for a client gateway.</summary>
public sealed record GatewayOptions
{
    /// <summary>Directory where client save/load names are resolved.</summary>
    public string SaveDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "saves");

    /// <summary>
    /// Lets clients save/load at any path (absolute or relative to <see cref="SaveDirectory"/>).
    /// Only for trusted local clients (single-player); remote clients get plain file names only.
    /// </summary>
    public bool AllowArbitrarySavePaths { get; init; }

    /// <summary>How long the gateway waits for the lobby/session actors.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Upper bound for a single debug step request.</summary>
    public int MaxDebugStepTicks { get; init; } = 100_000;

    /// <summary>Upper bound for commands in one request.</summary>
    public int MaxCommandsPerRequest { get; init; } = 256;
}
