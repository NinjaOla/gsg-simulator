namespace SimEngine.Contracts;

/// <summary>
/// A command submitted by a player to be applied at the next tick boundary.
/// </summary>
[GenerateSerializer]
public abstract record PlayerCommand;

/// <summary>Advance the simulation by one or more ticks.</summary>
[GenerateSerializer]
public sealed record StepPlayerCommand([property: Id(0)] int Ticks = 1) : PlayerCommand;

/// <summary>Pause the simulation (no-op if already paused).</summary>
[GenerateSerializer]
public sealed record PausePlayerCommand : PlayerCommand;

/// <summary>Resume the simulation (no-op if already running).</summary>
[GenerateSerializer]
public sealed record ResumePlayerCommand : PlayerCommand;
