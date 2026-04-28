namespace SimEngine.Game.Components;

/// <summary>
/// Per-province economic data. <see cref="ProductionE2"/> is monthly
/// production in fixed-point (divide by 100 for display units).
/// </summary>
public readonly record struct EconomyComponent(int ProductionE2);
