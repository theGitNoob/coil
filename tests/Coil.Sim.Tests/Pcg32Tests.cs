using System;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Pins the world's PRNG — ARCH §4 (all randomness from a seeded PRNG stored
/// in world state), M1-03.
///
/// Determinism is the contract the C-04 replay test and the §11.3 netcode
/// stand on, so the generator must be bit-exact with the published PCG32, not
/// merely "random-looking".
/// </summary>
public sealed class Pcg32Tests
{
    /// <summary>The reference output of pcg32-demo for pcg32_srandom_r(42, 54).</summary>
    [Fact]
    public void Pcg32_ForTheReferenceSeed_MatchesThePublishedSequence()
    {
        Pcg32 rng = new(seed: 42, stream: 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], rng.NextUInt());
        }
    }

    [Fact]
    public void Pcg32_NextFloat_StaysInTheUnitInterval()
    {
        Pcg32 rng = new(seed: 7, stream: 0);

        for (int i = 0; i < 100_000; i++)
        {
            float u = rng.NextFloat();

            Assert.InRange(u, 0f, 1f);
            Assert.NotEqual(1f, u);
        }
    }

    [Fact]
    public void Pcg32_NextUInt_DoesNotAllocate()
    {
        Pcg32 rng = new(seed: 1, stream: 0);
        rng.NextFloat();
        uint sink = 0;

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            sink ^= rng.NextUInt();
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.NotEqual(0u, sink);
    }
}
