namespace SimEngine.Contracts;

/// <summary>
/// A per-tick change to one country's treasury, pushed over the session stream.
/// Carries the absolute balance (not an increment) so applying deltas is
/// order-tolerant and self-correcting against a missed or stale message.
/// </summary>
public sealed record CountryTreasuryDelta
{
    /// <summary>Stable short identifier of the country (e.g. "DEU").</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>New treasury balance in fixed-point (divide by 100 for display units).</summary>
    public long FundsE2 { get; init; }
}
