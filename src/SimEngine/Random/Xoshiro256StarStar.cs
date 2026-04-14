using System.Numerics;

namespace SimEngine.Random;

/// <summary>
/// xoshiro256** PRNG (Blackman &amp; Vigna). 256 bits of state, well-studied,
/// trivially portable to C++/JS for cross-client multiplayer checks.
/// Seeded via SplitMix64 (the canonical xoshiro seeding procedure).
/// Bounded-range uses Lemire's unbiased "nearly division less" algorithm.
/// </summary>
public sealed class Xoshiro256StarStar : IDeterministicRandom
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public Xoshiro256StarStar(ulong seed)
    {
        Seed = seed;
        var state = seed;
        _s0 = SplitMix64(ref state);
        _s1 = SplitMix64(ref state);
        _s2 = SplitMix64(ref state);
        _s3 = SplitMix64(ref state);

        // xoshiro requires at least one non-zero state word. SplitMix64 from
        // any seed (including 0) produces non-zero output, so this is
        // defensive only — but cheap insurance.
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 1;
        }
    }

    public ulong Seed { get; }

    public ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(_s1 * 5UL, 7) * 9UL;
        var t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);

        return result;
    }

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    public int NextInt32(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                $"maxExclusive ({maxExclusive}) must be greater than minInclusive ({minInclusive}).");
        }

        var range = (uint)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + NextBoundedUInt32(range));
    }

    public long NextInt64(long minInclusive, long maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                $"maxExclusive ({maxExclusive}) must be greater than minInclusive ({minInclusive}).");
        }

        var range = (ulong)((Int128)maxExclusive - minInclusive);
        return (long)((Int128)minInclusive + NextBoundedUInt64(range));
    }

    /// <summary>
    /// Returns a decimal in <c>[0, 1)</c> with 53 bits of entropy. Built from
    /// the top 53 bits of <see cref="NextUInt64"/> divided by 2^53 as decimal,
    /// so every consumer on every platform gets the same value.
    /// </summary>
    public decimal NextUnitDecimal()
    {
        const ulong scale = 1UL << 53; // 9,007,199,254,740,992
        var top53 = NextUInt64() >> 11;
        return (decimal)top53 / (decimal)scale;
    }

    public IDeterministicRandom Fork(ulong streamId)
    {
        // XOR the seed with the stream id through SplitMix64 so that forks are
        // far apart in state space. Name-based stream ids from the engine mean
        // adding or removing unrelated systems doesn't perturb this stream.
        return new Xoshiro256StarStar(Seed ^ MixStreamId(streamId));
    }

    public (ulong s0, ulong s1, ulong s2, ulong s3) SnapshotState() => (_s0, _s1, _s2, _s3);

    public void RestoreState(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0)
        {
            throw new ArgumentException("xoshiro256** state cannot be all zero.");
        }

        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    // --- internals ---------------------------------------------------------

    private uint NextBoundedUInt32(uint range)
    {
        // Lemire's nearly-division-less bounded generator.
        var x = NextUInt32();
        var m = (ulong)x * range;
        var l = (uint)m;
        if (l < range)
        {
            var t = (uint)(0u - range) % range; // == 2^32 % range
            while (l < t)
            {
                x = NextUInt32();
                m = (ulong)x * range;
                l = (uint)m;
            }
        }

        return (uint)(m >> 32);
    }

    private ulong NextBoundedUInt64(ulong range)
    {
        // 128-bit Lemire for 64-bit ranges.
        var x = NextUInt64();
        var m = (UInt128)x * range;
        var l = (ulong)m;
        if (l < range)
        {
            var t = (ulong)(0UL - range) % range;
            while (l < t)
            {
                x = NextUInt64();
                m = (UInt128)x * range;
                l = (ulong)m;
            }
        }

        return (ulong)(m >> 64);
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong MixStreamId(ulong streamId)
    {
        // One SplitMix64 round over the stream id alone, so Fork(0) != Fork(1)
        // even when Seed == 0.
        var s = streamId;
        return SplitMix64(ref s);
    }
}
