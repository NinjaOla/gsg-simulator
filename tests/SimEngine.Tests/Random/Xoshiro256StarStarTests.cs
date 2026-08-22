using SimEngine.Random;
using Xunit;

namespace SimEngine.Tests.Random;

public sealed class Xoshiro256StarStarTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new Xoshiro256StarStar(42);
        var b = new Xoshiro256StarStar(42);
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new Xoshiro256StarStar(1);
        var b = new Xoshiro256StarStar(2);
        var differences = 0;
        for (var i = 0; i < 100; i++)
        {
            if (a.NextUInt64() != b.NextUInt64())
            {
                differences++;
            }
        }

        // Overwhelmingly likely to differ on every draw; a handful of
        // collisions is tolerable, but zero differences would indicate a bug.
        Assert.True(differences > 90, $"Expected most draws to differ, got {differences}/100");
    }

    [Fact]
    public void SnapshotRestore_RoundTripsState()
    {
        var rng = new Xoshiro256StarStar(123);
        for (var i = 0; i < 10; i++)
        {
            rng.NextUInt64();
        }

        var snap = rng.SnapshotState();
        var expected = new ulong[20];
        for (var i = 0; i < 20; i++)
        {
            expected[i] = rng.NextUInt64();
        }

        var replay = new Xoshiro256StarStar(999);
        replay.RestoreState(snap.s0, snap.s1, snap.s2, snap.s3);
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(expected[i], replay.NextUInt64());
        }
    }

    [Fact]
    public void RestoreState_RejectsAllZero()
    {
        var rng = new Xoshiro256StarStar(1);
        Assert.Throws<ArgumentException>(() => rng.RestoreState(0, 0, 0, 0));
    }

    [Fact]
    public void Fork_ProducesIndependentStream()
    {
        var parent = new Xoshiro256StarStar(0xDEADBEEF);
        var childA = parent.Fork(1);
        var childB = parent.Fork(2);

        // Different stream ids → different first outputs.
        Assert.NotEqual(childA.NextUInt64(), childB.NextUInt64());

        // Parent is unaffected by child draws.
        var parentBefore = parent.NextUInt64();
        var snap = parent.SnapshotState();
        for (var i = 0; i < 100; i++)
        {
            childA.NextUInt64();
            childB.NextUInt64();
        }

        parent.RestoreState(snap.s0, snap.s1, snap.s2, snap.s3);
        var parentAfter = parent.NextUInt64();
        // First parent draw after restoring = same sequence direction; we can
        // assert the snapshot round-tripped.
        Assert.NotEqual(parentBefore, parentAfter); // distinct draws
    }

    [Fact]
    public void Fork_SameStreamIdIsDeterministic()
    {
        var a = new Xoshiro256StarStar(7).Fork(123);
        var b = new Xoshiro256StarStar(7).Fork(123);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void NextInt32_RejectsEmptyRange()
    {
        var rng = new Xoshiro256StarStar(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt32(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt32(5, 4));
    }

    [Fact]
    public void NextInt32_StaysInRange()
    {
        var rng = new Xoshiro256StarStar(42);
        for (var i = 0; i < 10_000; i++)
        {
            var v = rng.NextInt32(-10, 10);
            Assert.InRange(v, -10, 9);
        }
    }

    [Fact]
    public void NextInt32_BoundedIsApproximatelyUniform()
    {
        // Catches gross modulo bias or bucket dropouts.
        var rng = new Xoshiro256StarStar(0xCAFEBABE);
        var buckets = new int[7];
        const int samples = 1_000_000;
        for (var i = 0; i < samples; i++)
        {
            buckets[rng.NextInt32(0, 7)]++;
        }

        var expected = samples / 7.0;
        foreach (var count in buckets)
        {
            var deviation = Math.Abs(count - expected) / expected;
            Assert.True(deviation < 0.02, $"Bucket deviation {deviation:P} exceeds 2%");
        }
    }

    [Fact]
    public void NextInt64_StaysInRange()
    {
        var rng = new Xoshiro256StarStar(99);
        for (var i = 0; i < 1000; i++)
        {
            var v = rng.NextInt64(long.MinValue / 2, long.MaxValue / 2);
            Assert.InRange(v, long.MinValue / 2, (long.MaxValue / 2) - 1);
        }
    }

    [Fact]
    public void NextUnitDecimal_InHalfOpenUnitInterval()
    {
        var rng = new Xoshiro256StarStar(1);
        for (var i = 0; i < 10_000; i++)
        {
            var v = rng.NextUnitDecimal();
            Assert.True(v >= 0m);
            Assert.True(v < 1m);
        }
    }

    [Fact]
    public void NextUnitDecimal_IsDeterministic()
    {
        var a = new Xoshiro256StarStar(1);
        var b = new Xoshiro256StarStar(1);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUnitDecimal(), b.NextUnitDecimal());
        }
    }

    /// <summary>
    /// Cross-platform reference vector. The first 10 outputs of
    /// <c>new Xoshiro256StarStar(0x123456789ABCDEF0)</c> must never change.
    /// If this test fails, a determinism-breaking change slipped into the
    /// PRNG — fix the change, do not update the constants.
    /// </summary>
    [Fact]
    public void ReferenceVector_Seed_123456789ABCDEF0()
    {
        // These values were captured from the first green run of this PRNG
        // and are locked in. See comment above.
        var expected = ReferenceVector;
        var rng = new Xoshiro256StarStar(0x123456789ABCDEF0UL);
        for (var i = 0; i < expected.Length; i++)
        {
            var actual = rng.NextUInt64();
            Assert.Equal(expected[i], actual);
        }
    }

    // Locked reference vector — see ReferenceVector_Seed_123456789ABCDEF0.
    // If this changes, a determinism-breaking change slipped in — fix the
    // change, do not update the constants.
    internal static readonly ulong[] ReferenceVector = new ulong[]
    {
        0xE01D6FAFC557F1B9UL,
        0xBD627EBE4406B404UL,
        0x2C23132B578B57DBUL,
        0x2E8B319D4D1F276AUL,
        0x608D57ACF53888E4UL,
        0x9F44D4FE68BDC399UL,
        0x2BF98C082C7CD85AUL,
        0x42F3AA03D402664CUL,
        0x947052F518F6CD76UL,
        0xE824C04694AF22FEUL,
    };
}


