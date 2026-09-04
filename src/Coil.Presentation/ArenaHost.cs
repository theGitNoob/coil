using System;
using Coil.Sim;
using Godot;

namespace Coil.Presentation;

/// <summary>
/// Drives the simulation and owns everything the renderers read — ARCH §2,
/// spec §11.3.
///
/// A plain class, not a Node: Godot registers script types only from the main
/// project assembly, so the engine-facing shell lives there and calls into this
/// (D-18). Keeping the logic here is what lets it be reasoned about and reused
/// without a scene tree; the shell should stay empty enough to be boring.
///
/// It does not own the clock. The 60 Hz cadence and the 5-tick catch-up cap are
/// the engine's physics loop, configured in project.godot from Appendix A's
/// TICK_RATE and MAX_CATCHUP_TICKS — reimplementing an accumulator here would
/// duplicate engine machinery and drift from the interpolation fraction the
/// renderers interpolate by.
/// </summary>
public sealed class ArenaHost
{
    private readonly SimConfig _config;
    private readonly World _world;
    private readonly InputCommand[] _commands;

    private long _tickCount;

    public ArenaHost(SimConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _world = new World(config);
        _commands = new InputCommand[config.SnakeCount];
    }

    public World World => _world;

    /// <summary>Fixed steps run since the host was created.</summary>
    public long TickCount => _tickCount;

    /// <summary>The fixed step the simulation advances by — never the frame time.</summary>
    public float FixedDelta => 1f / _config.TickRate;

    /// <summary>
    /// Where agents write their commands before the next <see cref="Tick"/>,
    /// one slot per snake. Reused every tick; the tick path allocates nothing.
    /// </summary>
    public Span<InputCommand> Commands => _commands;

    /// <summary>
    /// Placeholder until M1 lands the spawner: one snake at the arena centre so
    /// the tick has something to advance and the device build has something to
    /// prove.
    /// </summary>
    public void SpawnPlaceholderSnake() =>
        _world.Spawn(0, Vector2.Zero, heading: 0f, _config.MassStart);

    /// <summary>Advances the simulation exactly one fixed step.</summary>
    public void Tick()
    {
        _world.Tick(FixedDelta, _commands);
        _tickCount++;
    }
}
