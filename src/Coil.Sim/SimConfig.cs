namespace Coil.Sim;

/// <summary>
/// Every tuning constant in spec Appendix A, as an immutable POCO — ARCH §4
/// "Configuration".
///
/// `Coil.Sim` cannot read a `.tres`: that is a Godot resource type, and this
/// assembly touches no engine. The presentation layer's `BalanceLoader` reads
/// `data/balance.tres` and hands the result here, so the simulation stays a
/// pure `(state, commands, dt) -> state` with its numbers injected.
///
/// Every property is `required`, which is what makes "a field silently left at
/// its default" a compile error in the loader rather than a snake with a turn
/// rate of zero. A literal number in simulation code is a bug — the exceptions
/// are 0, 1 and mathematical constants.
/// </summary>
public sealed class SimConfig
{
    // --- Movement (§4.2) ---------------------------------------------------

    /// <summary>Cruise speed, u/s.</summary>
    public required float SpeedBase { get; init; }

    /// <summary>Boost speed, u/s — 1.73x base.</summary>
    public required float SpeedBoost { get; init; }

    /// <summary>Numerator of the turn-rate curve: omega_max = 900 / radius.</summary>
    public required float OmegaNumerator { get; init; }

    /// <summary>Lower clamp on omega_max, rad/s. The cap that makes big snakes unwieldy.</summary>
    public required float OmegaMin { get; init; }

    /// <summary>Upper clamp on omega_max, rad/s.</summary>
    public required float OmegaMax { get; init; }

    /// <summary>Arc-length spacing between path samples, u.</summary>
    public required float PathStep { get; init; }

    // --- Size (§4.1) -------------------------------------------------------

    /// <summary>Mass a snake spawns with.</summary>
    public required float MassStart { get; init; }

    /// <summary>Radius at zero mass, u, and the lower clamp on the radius curve.</summary>
    public required float RadiusBase { get; init; }

    /// <summary>Exponent of the radius curve.</summary>
    public required float RadiusExp { get; init; }

    /// <summary>Upper clamp on radius, u.</summary>
    public required float RadiusMax { get; init; }

    /// <summary>Divisor in the radius curve: radius = base * (1 + mass/this)^exp.</summary>
    public required float RadiusMassDivisor { get; init; }

    /// <summary>Body length at zero mass, u.</summary>
    public required float LengthBase { get; init; }

    /// <summary>Body length gained per unit of mass, u.</summary>
    public required float LengthPerMass { get; init; }

    /// <summary>
    /// The mass the path ring is sized for (§4.1: "sized to max_mass and never
    /// reallocated mid-match"). A snake past this keeps growing longer but its
    /// body is truncated at the ring capacity rather than reallocating.
    /// </summary>
    public required float MaxMass { get; init; }

    // --- Economy (§4.3) ----------------------------------------------------

    /// <summary>Mass burned per second while boosting.</summary>
    public required float BoostDrain { get; init; }

    /// <summary>Seconds between boost-drop pellets.</summary>
    public required float BoostDropInterval { get; init; }

    /// <summary>Mass of one boost-drop pellet.</summary>
    public required float BoostDropMass { get; init; }

    /// <summary>Mass floor below which boosting is refused.</summary>
    public required float BoostMinMass { get; init; }

    /// <summary>Mass of a normal pellet.</summary>
    public required float PelletMass { get; init; }

    /// <summary>Mass of one corpse chunk.</summary>
    public required float CorpseChunkMass { get; init; }

    /// <summary>Fraction of a dead snake's mass returned to the arena as corpse.</summary>
    public required float CorpseReturnRatio { get; init; }

    /// <summary>Fraction of a bounty returned to the arena.</summary>
    public required float BountyReturnRatio { get; init; }

    /// <summary>Pellet magnet radius at zero size, u.</summary>
    public required float MagnetBase { get; init; }

    /// <summary>Extra magnet radius per unit of snake radius.</summary>
    public required float MagnetPerRadius { get; init; }

    /// <summary>Mass bled per second above the leader-decay threshold.</summary>
    public required float LeaderDecayRate { get; init; }

    /// <summary>Mass above which leader decay applies.</summary>
    public required float LeaderDecayThreshold { get; init; }

    // --- World (§6) --------------------------------------------------------

    /// <summary>Arena radius, u.</summary>
    public required float ArenaRadius { get; init; }

    /// <summary>Radius at which the border warning shows, u.</summary>
    public required float BorderWarnRadius { get; init; }

    /// <summary>Snakes in a match, player included.</summary>
    public required int SnakeCount { get; init; }

    /// <summary>Pellet population the spawner maintains.</summary>
    public required int PelletTarget { get; init; }

    /// <summary>Spatial hash cell edge, u.</summary>
    public required float CellSize { get; init; }

    /// <summary>Seconds of spawn invulnerability.</summary>
    public required float GraceDuration { get; init; }

    // --- Sim (§11) ---------------------------------------------------------

    /// <summary>Fixed simulation rate, Hz.</summary>
    public required int TickRate { get; init; }

    /// <summary>Catch-up ticks per frame before time is dropped.</summary>
    public required int MaxCatchupTicks { get; init; }

    /// <summary>Ticks between bot steering decisions.</summary>
    public required int BotSteerInterval { get; init; }

    /// <summary>Ticks between steering decisions for an LOD-culled bot (§8.4).</summary>
    public required int BotSteerIntervalCulled { get; init; }

    /// <summary>How far a bot perceives, u.</summary>
    public required float BotPerceptionRadius { get; init; }
}
