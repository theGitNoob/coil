using System;
using Godot;

namespace Coil.Sim;

/// <summary>
/// The simulation. Owns all mutable state and advances it on a fixed tick —
/// ARCH §4, spec §4.2.
///
/// State is struct-of-arrays and every buffer is allocated here, at
/// construction, for <c>SnakeCount</c> snakes. Nothing in <see cref="Tick"/>
/// allocates, reads a clock, performs I/O or throws; a snake is a set of
/// indices into these arrays rather than an object, which is what keeps 34 of
/// them inside a 6 ms budget.
/// </summary>
public sealed class World
{
    private readonly SimConfig _config;
    private readonly int _capacity;
    private readonly int _pathCapacity;

    // One shared point buffer for every snake's path; each ring owns a slice.
    private readonly Vector2[] _pathPoints;
    private readonly PathRing[] _paths;

    private readonly Vector2[] _heads;
    private readonly float[] _headings;
    private readonly float[] _masses;
    private readonly bool[] _alive;

    public World(SimConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _capacity = config.SnakeCount;

        // §4.1: the ring is sized to MAX_MASS and never reallocated mid-match.
        // A snake past that mass keeps growing longer, but its body is truncated
        // at the ring rather than triggering an allocation inside the tick.
        float longestBody = config.LengthBase + (config.MaxMass * config.LengthPerMass);
        _pathCapacity = (int)MathF.Ceiling(longestBody / config.PathStep) + 1;

        _pathPoints = new Vector2[_capacity * _pathCapacity];
        _paths = new PathRing[_capacity];
        _heads = new Vector2[_capacity];
        _headings = new float[_capacity];
        _masses = new float[_capacity];
        _alive = new bool[_capacity];

        for (int i = 0; i < _capacity; i++)
        {
            _paths[i] = new PathRing(start: i * _pathCapacity, capacity: _pathCapacity);
        }
    }

    /// <summary>Snake slots in this world, live or not.</summary>
    public int Capacity => _capacity;

    public bool IsAlive(int index) => _alive[index];

    public Vector2 HeadOf(int index) => _heads[index];

    public float HeadingOf(int index) => _headings[index];

    public float MassOf(int index) => _masses[index];

    /// <summary>§4.1: radius = clamp(base * (1 + mass/divisor)^exp, base, max).</summary>
    public float RadiusOf(int index) => Radius(_masses[index]);

    public int PathCountOf(int index) => _paths[index].Count;

    /// <summary>A path point by age: 0 is the newest, nearest the head.</summary>
    public Vector2 PathPointOf(int index, int age) => _paths[index].PointAt(_pathPoints, age);

    /// <summary>
    /// Brings a slot to life. Outside the tick path — the caller does this at
    /// match start or on respawn, never per frame.
    /// </summary>
    public void Spawn(int index, Vector2 position, float heading, float mass)
    {
        _heads[index] = position;
        _headings[index] = WrapAngle(heading);
        _masses[index] = mass;
        _alive[index] = true;
        _paths[index].Seed(_pathPoints, position);
    }

    /// <summary>
    /// Advances every live snake by one fixed step — ARCH §4.
    ///
    /// One command per snake, indexed by slot. A slot the caller had nothing to
    /// say about reads as <c>default</c>, the neutral command, which is why
    /// <see cref="InputCommand"/>'s default must stay neutral.
    /// </summary>
    public void Tick(float dt, ReadOnlySpan<InputCommand> commands)
    {
        for (int i = 0; i < _capacity; i++)
        {
            if (!_alive[i])
            {
                continue;
            }

            InputCommand command = i < commands.Length ? commands[i] : default;

            // §4.2 step 2: rotate toward the target at omega_max, never snap.
            // Turn radius grows with size, which is the balancing force on mass.
            float omegaMax = Math.Clamp(
                _config.OmegaNumerator / Radius(_masses[i]),
                _config.OmegaMin,
                _config.OmegaMax);

            _headings[i] = RotateToward(_headings[i], command.Heading, omegaMax * dt);

            // §4.2 step 3.
            float speed = command.Boost ? _config.SpeedBoost : _config.SpeedBase;
            Vector2 direction = new(MathF.Cos(_headings[i]), MathF.Sin(_headings[i]));
            _heads[i] += direction * speed * dt;

            // §4.2 step 4.
            _paths[i].Advance(_pathPoints, _heads[i], _config.PathStep);
        }
    }

    private float Radius(float mass) => Math.Clamp(
        _config.RadiusBase * MathF.Pow(1f + (mass / _config.RadiusMassDivisor), _config.RadiusExp),
        _config.RadiusBase,
        _config.RadiusMax);

    /// <summary>
    /// Turns <paramref name="from"/> toward <paramref name="to"/> by at most
    /// <paramref name="maxDelta"/>, the short way around.
    /// </summary>
    private static float RotateToward(float from, float to, float maxDelta)
    {
        float difference = WrapAngle(to - from);
        float step = Math.Clamp(difference, -maxDelta, maxDelta);

        return WrapAngle(from + step);
    }

    /// <summary>Folds an angle into (-pi, pi], so headings never drift unbounded.</summary>
    private static float WrapAngle(float angle)
    {
        float wrapped = angle % Mathf.Tau;

        if (wrapped > Mathf.Pi)
        {
            wrapped -= Mathf.Tau;
        }
        else if (wrapped < -Mathf.Pi)
        {
            wrapped += Mathf.Tau;
        }

        return wrapped;
    }
}
