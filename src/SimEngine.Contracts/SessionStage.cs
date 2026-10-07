namespace SimEngine.Contracts;

/// <summary>Lifecycle stage of a game session.</summary>
public enum SessionStage
{
    /// <summary>Players join and the host edits settings; the engine does not tick.</summary>
    Lobby,

    /// <summary>The session ticks on its own at the current speed.</summary>
    Running,

    /// <summary>Started but not ticking; debug steps are allowed here.</summary>
    Paused,

    /// <summary>The session has been shut down.</summary>
    Ended,
}
