namespace SimEngine.Random;

/// <summary>
/// Deterministic pseudo-random source. The contract:
/// <list type="bullet">
///   <item>Same <see cref="Seed"/> produces the same output sequence on
///   every runtime, OS, and architecture.</item>
///   <item>Bounded-range methods are unbiased.</item>
///   <item><see cref="Fork(ulong)"/> yields an independent sub-stream whose
///   output does not affect the parent stream.</item>
///   <item>State is snapshot/restore-able for save/load.</item>
/// </list>
/// </summary>
public interface IDeterministicRandom
{
    ulong Seed { get; }

    uint NextUInt32();
    ulong NextUInt64();

    /// <summary>Uniform in <c>[minInclusive, maxExclusive)</c>, unbiased.</summary>
    int NextInt32(int minInclusive, int maxExclusive);

    /// <summary>Uniform in <c>[minInclusive, maxExclusive)</c>, unbiased.</summary>
    long NextInt64(long minInclusive, long maxExclusive);

    /// <summary>Deterministic value in <c>[0, 1)</c> expressed as <see cref="decimal"/>.</summary>
    decimal NextUnitDecimal();

    /// <summary>Fork an independent sub-stream keyed by <paramref name="streamId"/>.</summary>
    IDeterministicRandom Fork(ulong streamId);

    (ulong s0, ulong s1, ulong s2, ulong s3) SnapshotState();
    void RestoreState(ulong s0, ulong s1, ulong s2, ulong s3);
}
