using Coil.Sim;
using Godot;

namespace Coil;

/// <summary>
/// The editable face of spec Appendix A: a Godot resource, so the numbers can be
/// tuned in the inspector and hot-reloaded without a rebuild.
///
/// This type exists in the presentation layer and not in `Coil.Sim` because
/// `Resource` is an engine type and the simulation references none — ARCH §2,
/// §4. Its only job is to be deserialised and turned into a
/// <see cref="SimConfig"/>.
///
/// The defaults here are spec Appendix A's starting values. They are a safety
/// net for a resource created from scratch in the editor, not the source of
/// truth: `data/balance.tres` is.
/// </summary>
[GlobalClass]
public partial class BalanceData : Resource
{
    // --- Movement (§4.2) ---------------------------------------------------
    [Export] public float SpeedBase { get; set; } = 220.0f;
    [Export] public float SpeedBoost { get; set; } = 380.0f;
    [Export] public float OmegaNumerator { get; set; } = 900.0f;
    [Export] public float OmegaMin { get; set; } = 2.0f;
    [Export] public float OmegaMax { get; set; } = 4.5f;
    [Export] public float PathStep { get; set; } = 6.0f;

    // --- Size (§4.1) -------------------------------------------------------
    [Export] public float MassStart { get; set; } = 10.0f;
    [Export] public float RadiusBase { get; set; } = 14.0f;
    [Export] public float RadiusExp { get; set; } = 0.25f;
    [Export] public float RadiusMax { get; set; } = 46.0f;
    [Export] public float RadiusMassDivisor { get; set; } = 100.0f;
    [Export] public float LengthBase { get; set; } = 60.0f;
    [Export] public float LengthPerMass { get; set; } = 1.6f;
    [Export] public float MaxMass { get; set; } = 2000.0f;

    // --- Economy (§4.3) ----------------------------------------------------
    [Export] public float BoostDrain { get; set; } = 9.0f;
    [Export] public float BoostDropInterval { get; set; } = 0.35f;
    [Export] public float BoostDropMass { get; set; } = 4.0f;
    [Export] public float BoostMinMass { get; set; } = 25.0f;
    [Export] public float PelletMass { get; set; } = 1.0f;
    [Export] public float CorpseChunkMass { get; set; } = 6.0f;
    [Export] public float CorpseReturnRatio { get; set; } = 0.70f;
    [Export] public float BountyReturnRatio { get; set; } = 1.00f;
    [Export] public float MagnetBase { get; set; } = 45.0f;
    [Export] public float MagnetPerRadius { get; set; } = 1.5f;
    [Export] public float LeaderDecayRate { get; set; } = 0.004f;
    [Export] public float LeaderDecayThreshold { get; set; } = 500.0f;

    // --- World (§6) --------------------------------------------------------
    [Export] public float ArenaRadius { get; set; } = 3500.0f;
    [Export] public float BorderWarnRadius { get; set; } = 3350.0f;
    [Export] public int SnakeCount { get; set; } = 34;
    [Export] public int PelletTarget { get; set; } = 2600;
    [Export] public float CellSize { get; set; } = 96.0f;
    [Export] public float GraceDuration { get; set; } = 2.0f;

    // --- Sim (§11) ---------------------------------------------------------
    [Export] public int TickRate { get; set; } = 60;
    [Export] public int MaxCatchupTicks { get; set; } = 5;
    [Export] public int BotSteerInterval { get; set; } = 4;
    [Export] public int BotSteerIntervalCulled { get; set; } = 12;
    [Export] public float BotPerceptionRadius { get; set; } = 1400.0f;

    /// <summary>
    /// Projects the resource onto the simulation's immutable config. Every
    /// property of <see cref="SimConfig"/> is `required`, so omitting one here
    /// fails the build rather than shipping a zero into the tick loop.
    /// </summary>
    public SimConfig ToSimConfig() => new()
    {
        SpeedBase = SpeedBase,
        SpeedBoost = SpeedBoost,
        OmegaNumerator = OmegaNumerator,
        OmegaMin = OmegaMin,
        OmegaMax = OmegaMax,
        PathStep = PathStep,

        MassStart = MassStart,
        RadiusBase = RadiusBase,
        RadiusExp = RadiusExp,
        RadiusMax = RadiusMax,
        RadiusMassDivisor = RadiusMassDivisor,
        LengthBase = LengthBase,
        LengthPerMass = LengthPerMass,
        MaxMass = MaxMass,

        BoostDrain = BoostDrain,
        BoostDropInterval = BoostDropInterval,
        BoostDropMass = BoostDropMass,
        BoostMinMass = BoostMinMass,
        PelletMass = PelletMass,
        CorpseChunkMass = CorpseChunkMass,
        CorpseReturnRatio = CorpseReturnRatio,
        BountyReturnRatio = BountyReturnRatio,
        MagnetBase = MagnetBase,
        MagnetPerRadius = MagnetPerRadius,
        LeaderDecayRate = LeaderDecayRate,
        LeaderDecayThreshold = LeaderDecayThreshold,

        ArenaRadius = ArenaRadius,
        BorderWarnRadius = BorderWarnRadius,
        SnakeCount = SnakeCount,
        PelletTarget = PelletTarget,
        CellSize = CellSize,
        GraceDuration = GraceDuration,

        TickRate = TickRate,
        MaxCatchupTicks = MaxCatchupTicks,
        BotSteerInterval = BotSteerInterval,
        BotSteerIntervalCulled = BotSteerIntervalCulled,
        BotPerceptionRadius = BotPerceptionRadius,
    };
}
