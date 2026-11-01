namespace Coil.Sim;

/// <summary>
/// The world's random source — PCG32 (O'Neill, pcg32_random_r), ARCH §4.
///
/// A mutable struct held as a field of <see cref="World"/> and passed by
/// <c>ref</c>, so drawing a number allocates nothing and the whole generator
/// state is part of world state. The multiplier is the algorithm's constant,
/// not a tunable.
/// </summary>
internal struct Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>pcg32_srandom_r: <paramref name="stream"/> picks one of 2^63 independent sequences.</summary>
    public Pcg32(ulong seed, ulong stream)
    {
        _state = 0;
        _increment = (stream << 1) | 1;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = (old * Multiplier) + _increment;

        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);

        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Uniform in [0, 1): the top 24 bits, exactly representable as a float.</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / (1 << 24));
}
