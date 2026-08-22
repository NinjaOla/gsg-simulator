namespace SimEngine.Contracts;

/// <summary>
/// A command submitted by a player to be applied at the next tick boundary.
/// </summary>
public abstract record PlayerCommand;

/// <summary>Advance the simulation by one or more ticks.</summary>
public sealed record StepPlayerCommand(int Ticks = 1) : PlayerCommand;

/// <summary>Pause the simulation (no-op if already paused).</summary>
public sealed record PausePlayerCommand : PlayerCommand;

/// <summary>Resume the simulation (no-op if already running).</summary>
public sealed record ResumePlayerCommand : PlayerCommand;
