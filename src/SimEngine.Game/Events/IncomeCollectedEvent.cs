using SimEngine.Events;
using SimEngine.Ids;

namespace SimEngine.Game.Events;

/// <summary>
/// Published by <see cref="Systems.EconomySystem"/> when a country collects
/// its monthly income. Amounts are fixed-point E2 (1.00 = 100).
/// </summary>
public sealed record IncomeCollectedEvent(
    EntityId Country,
    string CountryTag,
    long IncomeE2,
    long FundsE2,
    DateTimeOffset Date) : ISimulationEvent;
