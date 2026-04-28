namespace SimEngine.Game.Components;

/// <summary>
/// Per-province population data. <see cref="GrowthRateE6"/> is the annual
/// growth rate in fixed-point (divide by 1 000 000 for the fraction, so
/// 5 000 = 0.005 = 0.5 %/year).
/// </summary>
public readonly record struct PopulationComponent(long Population, int GrowthRateE6);
