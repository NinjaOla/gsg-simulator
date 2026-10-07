namespace SimEngine.Contracts;

/// <summary>Who may issue a class of session-control command.</summary>
public enum ControlAccess
{
    HostOnly,
    Anyone,
}

/// <summary>
/// Per-game rules chosen by the host in the lobby. Any player may send any
/// command; the session validates it against these settings and rejects
/// disallowed ones with a <see cref="CommandRejectedException"/>.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Slowest speed level.</summary>
    public const int MinSpeed = 1;

    /// <summary>Fastest speed level (no wait between ticks).</summary>
    public const int MaxSpeed = 5;

    /// <summary>Who may pause and resume the game.</summary>
    public ControlAccess PauseControl { get; init; } = ControlAccess.HostOnly;

    /// <summary>Who may change the game speed.</summary>
    public ControlAccess SpeedControl { get; init; } = ControlAccess.HostOnly;

    /// <summary>Whether debug commands (e.g. debug steps) are allowed.</summary>
    public bool AllowDebugCommands { get; init; }

    /// <summary>Speed level (<see cref="MinSpeed"/>–<see cref="MaxSpeed"/>) the game starts at.</summary>
    public int InitialSpeed { get; init; } = 3;

    /// <summary>Whether <paramref name="speed"/> is a valid speed level.</summary>
    public static bool IsValidSpeed(int speed) => speed is >= MinSpeed and <= MaxSpeed;
}
