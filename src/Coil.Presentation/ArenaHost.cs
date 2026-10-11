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
///
/// It keeps the previous tick's snapshot of every snake and answers "where is
/// this snake at fraction alpha between the two ticks" — ARCH §6. The current
/// state is the world itself, so only the previous one is copied, once per
/// tick, into arrays allocated here.
/// </summary>
public sealed class ArenaHost
{
    private readonly SimConfig _config;
    private readonly World _world;
    private readonly InputCommand[] _commands;

    // The previous tick's snapshot, one slot per snake.
    private readonly bool[] _previousAlive;
    private readonly Vector2[] _previousHeads;
    private readonly float[] _previousHeadings;
    private readonly float[] _previousRadii;
    private readonly float[] _previousLeads;
    private readonly int[] _previousPushes;

    private long _tickCount;

    public ArenaHost(SimConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _world = new World(config);
        _commands = new InputCommand[config.SnakeCount];

        int slots = _world.Capacity;
        _previousAlive = new bool[slots];
        _previousHeads = new Vector2[slots];
        _previousHeadings = new float[slots];
        _previousRadii = new float[slots];
        _previousLeads = new float[slots];
        _previousPushes = new int[slots];
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
    public void SpawnPlaceholderSnake()
    {
        _world.Spawn(0, Vector2.Zero, heading: 0f, _config.MassStart);

        // A frame can be drawn before the first tick; it must not interpolate
        // from an empty slot.
        CaptureSnapshot();
    }

    /// <summary>Advances the simulation exactly one fixed step.</summary>
    public void Tick()
    {
        // Captured per tick, not per frame: capped to 30 fps the engine runs
        // two ticks a frame, and alpha is a fraction of the last one only.
        CaptureSnapshot();
        _world.Tick(FixedDelta, _commands);
        _tickCount++;
    }

    public Vector2 HeadAt(int index, float alpha) =>
        _previousAlive[index] ? _previousHeads[index].Lerp(_world.HeadOf(index), alpha) : _world.HeadOf(index);

    public float HeadingAt(int index, float alpha) =>
        _previousAlive[index] ? Mathf.LerpAngle(_previousHeadings[index], _world.HeadingOf(index), alpha) : _world.HeadingOf(index);

    public float RadiusAt(int index, float alpha) =>
        _previousAlive[index] ? Mathf.Lerp(_previousRadii[index], _world.RadiusOf(index), alpha) : _world.RadiusOf(index);

    /// <summary>
    /// Arc length the interpolated head sits past the world's current newest
    /// path point. Negative while it has not yet reached points the last tick
    /// pushed — those were laid on the straight line it is still crossing.
    /// </summary>
    public float LeadAt(int index, float alpha)
    {
        float current = _world.LeadOf(index);
        if (!_previousAlive[index])
        {
            return current;
        }

        // Re-express the previous lead against today's newest point: every
        // push since moved that point one step further along the path.
        int arrived = _world.PathPushesOf(index) - _previousPushes[index];
        float previous = _previousLeads[index] - (arrived * _config.PathStep);

        return Mathf.Lerp(previous, current, alpha);
    }

    private void CaptureSnapshot()
    {
        for (int i = 0; i < _previousAlive.Length; i++)
        {
            _previousAlive[i] = _world.IsAlive(i);
            _previousHeads[i] = _world.HeadOf(i);
            _previousHeadings[i] = _world.HeadingOf(i);
            _previousRadii[i] = _world.RadiusOf(i);
            _previousLeads[i] = _world.LeadOf(i);
            _previousPushes[i] = _world.PathPushesOf(i);
        }
    }
}
