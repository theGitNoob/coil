using System;
using Godot;

namespace Coil.Sim;

/// <summary>
/// The pellet store — struct-of-arrays with a free list, M1-01; pellet masses
/// are spec §4.3.
///
/// Every buffer is allocated here, at construction, for <c>PELLET_CAPACITY</c>
/// pellets and is never resized (ARCH §4): eats and drops happen every tick,
/// so spawning and removing must be O(1) and allocation-free. A pellet is an
/// index into these arrays, not an object. Free slots sit on a stack, filled
/// so the first spawns come out as slots 0, 1, 2, ... — slot order is part of
/// the determinism contract (ARCH §4).
///
/// Readers (renderers, the spatial hash) see it through <see cref="World"/>;
/// only the simulation mutates it.
/// </summary>
public sealed class Food
{
    private readonly Vector2[] _positions;
    private readonly float[] _masses;
    private readonly bool[] _alive;

    // Stack of free slot indices; _freeCount is the next push position.
    private readonly int[] _free;
    private int _freeCount;

    internal Food(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        _positions = new Vector2[capacity];
        _masses = new float[capacity];
        _alive = new bool[capacity];
        _free = new int[capacity];

        // Pushed in reverse so slot 0 is on top and spawns take 0, 1, 2, ...
        for (int i = capacity - 1; i >= 0; i--)
        {
            _free[_freeCount++] = i;
        }
    }

    /// <summary>Pellet slots in the pool, live or not.</summary>
    public int Capacity => _alive.Length;

    /// <summary>Live pellets.</summary>
    public int Count => _alive.Length - _freeCount;

    public bool IsAlive(int index) => _alive[index];

    public Vector2 PositionOf(int index) => _positions[index];

    public float MassOf(int index) => _masses[index];

    /// <summary>
    /// Places a pellet in a free slot and returns its index, or -1 when the
    /// pool is full. A full pool drops the pellet rather than throwing: a mass
    /// die-off spawns many pellets in one tick, and nothing the tick calls may
    /// throw (ARCH §5).
    /// </summary>
    internal int Spawn(Vector2 position, float mass)
    {
        if (_freeCount == 0)
        {
            return -1;
        }

        int index = _free[--_freeCount];
        _positions[index] = position;
        _masses[index] = mass;
        _alive[index] = true;

        return index;
    }

    /// <summary>
    /// Frees a live pellet's slot for reuse. Returns false and changes nothing
    /// for a slot that is not alive, so a double remove can never put a slot
    /// on the free stack twice.
    /// </summary>
    internal bool Remove(int index)
    {
        if (!_alive[index])
        {
            return false;
        }

        _alive[index] = false;
        _free[_freeCount++] = index;

        return true;
    }
}
