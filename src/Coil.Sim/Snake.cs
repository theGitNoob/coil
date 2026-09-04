using System;
using Godot;

namespace Coil.Sim;

/// <summary>
/// One snake's path: a ring of head positions resampled to a fixed arc-length
/// spacing of <c>PATH_STEP</c> — spec §4.1.
///
/// The body is not simulated. Segments are sampled from this path, and
/// rendering and collision both read it, so the spacing invariant is the whole
/// point: if it drifts with the frame rate, the body visibly stretches when the
/// device thermally throttles.
///
/// A struct holding a slice of the world's shared point buffer, not a class
/// owning its own array: snakes are slices into shared buffers allocated once
/// at world construction, and there is no per-entity heap object in the tick
/// path (ARCH §4). The buffer is passed in on every call for the same reason.
/// </summary>
public struct PathRing
{
    private readonly int _start;
    private readonly int _capacity;

    private int _count;
    private int _newest;

    /// <summary>Where the head was when <see cref="Advance"/> last ran.</summary>
    private Vector2 _lastHead;

    /// <summary>
    /// Arc length travelled since the last point was pushed, carried across
    /// ticks. This is what makes the spacing an arc length rather than a
    /// straight line from the last point to the head: while turning, the head
    /// travels further than it is displaced, and measuring the gap directly
    /// would stretch the body through every corner.
    /// </summary>
    private float _carry;

    public PathRing(int start, int capacity)
    {
        _start = start;
        _capacity = capacity;
        _count = 0;
        _newest = 0;
        _lastHead = Vector2.Zero;
        _carry = 0f;
    }

    /// <summary>Points currently held, at most the capacity.</summary>
    public readonly int Count => _count;

    /// <summary>Lays down the first point. Call once, at spawn.</summary>
    public void Seed(Span<Vector2> points, Vector2 head)
    {
        _count = 0;
        _newest = 0;
        _carry = 0f;
        _lastHead = head;

        points[_start] = head;
        _count = 1;
    }

    /// <summary>
    /// Advances the path to a new head position, pushing a point at every
    /// <paramref name="pathStep"/> boundary crossed along the way — §4.2 step 4.
    /// The head moves in a straight line within a tick, so interpolating along
    /// that segment lands exactly on each boundary and keeps the spacing
    /// independent of dt.
    /// </summary>
    /// <returns>How many points were pushed, usually 0 or 1.</returns>
    public int Advance(Span<Vector2> points, Vector2 head, float pathStep)
    {
        Vector2 segment = head - _lastHead;
        float segmentLength = segment.Length();

        if (segmentLength <= 0f)
        {
            return 0;
        }

        Vector2 direction = segment / segmentLength;
        float travelled = 0f;
        int pushed = 0;

        // Each boundary sits `pathStep` of arc length past the previous point,
        // and `_carry` is how much of that was already covered before this tick.
        while (_carry + (segmentLength - travelled) >= pathStep)
        {
            travelled += pathStep - _carry;
            Push(points, _lastHead + (direction * travelled));
            _carry = 0f;
            pushed++;
        }

        _carry += segmentLength - travelled;
        _lastHead = head;
        return pushed;
    }

    /// <summary>
    /// A point by age: 0 is the newest, <see cref="Count"/> - 1 the oldest still
    /// held. Body segments are sampled newest-first from the head backwards.
    /// </summary>
    public readonly Vector2 PointAt(ReadOnlySpan<Vector2> points, int age)
    {
        int index = _newest - age;
        if (index < 0)
        {
            index += _capacity;
        }

        return points[_start + index];
    }

    private void Push(Span<Vector2> points, Vector2 point)
    {
        _newest++;
        if (_newest >= _capacity)
        {
            _newest = 0;
        }

        points[_start + _newest] = point;

        if (_count < _capacity)
        {
            _count++;
        }
    }
}
