namespace SimEngine.Contracts;

/// <summary>
/// Lightweight session status (no world data), used by lobby listings and
/// lobby screens.
/// </summary>
public sealed record SessionStatus
{
    /// <summary>Current lifecycle stage.</summary>
    public SessionStage Stage { get; init; }

    /// <summary>Current speed level.</summary>
    public int Speed { get; init; }

    /// <summary>The host player, or <c>null</c> if nobody has joined.</summary>
    public string? HostPlayerId { get; init; }

    /// <summary>The game's settings.</summary>
    public GameSettings Settings { get; init; } = new();

    /// <summary>Ordered session members.</summary>
    public PlayerInfo[] Players { get; init; } = [];

    /// <summary>Current tick number (0 before initialization).</summary>
    public long TickNumber { get; init; }
}
