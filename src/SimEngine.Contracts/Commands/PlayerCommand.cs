using System.Text.Json.Serialization;

namespace SimEngine.Contracts;

/// <summary>
/// A command submitted by a player. The session validates it against the
/// game's <see cref="GameSettings"/> and applies it between ticks. New
/// commands must be listed here to cross the wire.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PausePlayerCommand), "pause")]
[JsonDerivedType(typeof(ResumePlayerCommand), "resume")]
[JsonDerivedType(typeof(SetSpeedPlayerCommand), "setSpeed")]
public abstract record PlayerCommand;

/// <summary>Pause the simulation (no-op if already paused). Governed by <see cref="GameSettings.PauseControl"/>.</summary>
public sealed record PausePlayerCommand : PlayerCommand;

/// <summary>Resume the simulation (no-op if already running). Governed by <see cref="GameSettings.PauseControl"/>.</summary>
public sealed record ResumePlayerCommand : PlayerCommand;

/// <summary>Change the speed level (1–5). Governed by <see cref="GameSettings.SpeedControl"/>.</summary>
public sealed record SetSpeedPlayerCommand(int Speed) : PlayerCommand;
