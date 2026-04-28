namespace SimEngine.Game.Components;

/// <summary>
/// Per-country treasury. <see cref="FundsE2"/> is the balance in fixed-point
/// (divide by 100 for display units).
/// </summary>
public readonly record struct TreasuryComponent(long FundsE2);
