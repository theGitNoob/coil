namespace Coil.Sim.Tests;

/// <summary>
/// A <see cref="SimConfig"/> carrying spec Appendix A's starting values, for
/// tests that need a world rather than a config.
///
/// The presentation layer's BalanceData holds the same defaults, but it lives
/// in the game assembly and this project deliberately cannot see it (ARCH §9).
/// <see cref="SimConfigTests"/> asserts these agree with its own independent
/// restatement of Appendix A, so the two copies cannot drift apart quietly.
/// </summary>
internal static class SpecConfig
{
    public static SimConfig Create() => new()
    {
        SpeedBase = 220.0f,
        SpeedBoost = 380.0f,
        OmegaNumerator = 900.0f,
        OmegaMin = 2.0f,
        OmegaMax = 4.5f,
        PathStep = 6.0f,

        MassStart = 10.0f,
        RadiusBase = 14.0f,
        RadiusExp = 0.25f,
        RadiusMax = 46.0f,
        RadiusMassDivisor = 100.0f,
        LengthBase = 60.0f,
        LengthPerMass = 1.6f,
        MaxMass = 2000.0f,

        BoostDrain = 9.0f,
        BoostDropInterval = 0.35f,
        BoostDropMass = 4.0f,
        BoostMinMass = 25.0f,
        PelletMass = 1.0f,
        CorpseChunkMass = 6.0f,
        CorpseReturnRatio = 0.70f,
        BountyReturnRatio = 1.00f,
        MagnetBase = 45.0f,
        MagnetPerRadius = 1.5f,
        LeaderDecayRate = 0.004f,
        LeaderDecayThreshold = 500.0f,

        ArenaRadius = 3500.0f,
        BorderWarnRadius = 3350.0f,
        SnakeCount = 34,
        PelletTarget = 2600,
        CellSize = 96.0f,
        GraceDuration = 2.0f,

        TickRate = 60,
        MaxCatchupTicks = 5,
        BotSteerInterval = 4,
        BotSteerIntervalCulled = 12,
        BotPerceptionRadius = 1400.0f,
    };
}
