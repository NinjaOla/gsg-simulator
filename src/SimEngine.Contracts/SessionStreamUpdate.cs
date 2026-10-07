namespace SimEngine.Contracts;

/// <summary>
/// Pushed to the per-session stream after each tick (and on stage, speed,
/// settings, and membership changes) so clients can render without polling. A lagging subscriber may
/// receive several ticks merged into one update. Events are pre-rendered plain
/// text for now; typed event DTOs replace this when the client-side state model lands.
/// </summary>
public sealed record SessionStreamUpdate
{
    /// <summary>The session that produced this update.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Monotonic per-session sequence; subscribers ack this value.</summary>
    public long Sequence { get; init; }

    /// <summary>Tick state after this update (<c>TicksExecuted</c> is 0 for status-only updates).</summary>
    public TickResult Tick { get; init; } = new();

    /// <summary>Lifecycle stage after this update.</summary>
    public SessionStage Stage { get; init; }

    /// <summary>Speed level after this update.</summary>
    public int Speed { get; init; }

    /// <summary>The host player after this update.</summary>
    public string? HostPlayerId { get; init; }

    /// <summary>Game settings after this update.</summary>
    public GameSettings Settings { get; init; } = new();

    /// <summary>Ordered session members after this update.</summary>
    public PlayerInfo[] Players { get; init; } = [];

    /// <summary>Game events emitted during the step, rendered as plain text.</summary>
    public string[] Events { get; init; } = [];

    /// <summary>Countries whose treasury changed during the step (absolute balances).</summary>
    public CountryTreasuryDelta[] CountryDeltas { get; init; } = [];
}
