namespace SimEngine.Contracts;

/// <summary>
/// A country's synced read model: stable identity plus the dynamic treasury
/// balance. Part of <see cref="SessionSnapshot"/>; per-tick changes arrive as
/// <see cref="CountryTreasuryDelta"/> over the session stream.
/// </summary>
public sealed record CountryState
{
    /// <summary>Stable short identifier (e.g. "DEU").</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Localisation-friendly display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Treasury balance in fixed-point (divide by 100 for display units).</summary>
    public long FundsE2 { get; init; }
}
