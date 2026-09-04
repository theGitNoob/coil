using System;
using Godot;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Pins the snake path buffer — spec §4.1 (representation) and §4.2 step 4
/// (push a point when the head has travelled PATH_STEP, interpolating to land
/// exactly on the boundary), M0-10.
///
/// The body is sampled from this path rather than simulated, and rendering and
/// collision both read it, so uneven spacing is not a cosmetic bug: it shows up
/// as a body that stretches when the frame rate dips.
/// </summary>
public sealed class PathRingTests
{
    // Spec Appendix A: PATH_STEP = 6.0 u.
    private const float PathStep = 6.0f;

    // The roadmap's acceptance tolerance for M0-10.
    private const float Tolerance = 0.01f;

    private static (Vector2[] Points, PathRing Ring) NewRing(int capacity = 64)
    {
        Vector2[] points = new Vector2[capacity];
        PathRing ring = new(start: 0, capacity: capacity);
        return (points, ring);
    }

    [Fact]
    public void PathRing_AfterSeeding_HoldsTheSeedPoint()
    {
        (Vector2[] points, PathRing ring) = NewRing();

        ring.Seed(points, new Vector2(10f, 20f));

        Assert.Equal(1, ring.Count);
        Assert.Equal(new Vector2(10f, 20f), ring.PointAt(points, 0));
    }

    [Fact]
    public void PathRing_WhenHeadMovesLessThanPathStep_PushesNothing()
    {
        (Vector2[] points, PathRing ring) = NewRing();
        ring.Seed(points, Vector2.Zero);

        int pushed = ring.Advance(points, new Vector2(PathStep - 0.5f, 0f), PathStep);

        Assert.Equal(0, pushed);
        Assert.Equal(1, ring.Count);
    }

    /// <summary>
    /// §4.2: interpolate to land exactly on the step boundary. Overshooting to
    /// the head position instead is what makes spacing frame-rate dependent.
    /// </summary>
    [Fact]
    public void PathRing_WhenHeadCrossesBoundary_PushesTheBoundaryNotTheHead()
    {
        (Vector2[] points, PathRing ring) = NewRing();
        ring.Seed(points, Vector2.Zero);

        int pushed = ring.Advance(points, new Vector2(9f, 0f), PathStep);

        Assert.Equal(1, pushed);
        Assert.Equal(PathStep, ring.PointAt(points, 0).X, Tolerance);
    }

    [Fact]
    public void PathRing_WhenHeadJumpsSeveralSteps_PushesEveryBoundary()
    {
        (Vector2[] points, PathRing ring) = NewRing();
        ring.Seed(points, Vector2.Zero);

        int pushed = ring.Advance(points, new Vector2(PathStep * 3.5f, 0f), PathStep);

        Assert.Equal(3, pushed);
        Assert.Equal(PathStep * 3f, ring.PointAt(points, 0).X, Tolerance);
        Assert.Equal(PathStep * 2f, ring.PointAt(points, 1).X, Tolerance);
        Assert.Equal(PathStep * 1f, ring.PointAt(points, 2).X, Tolerance);
    }

    /// <summary>
    /// The roadmap's acceptance test: spacing holds across jittered dt. The
    /// head runs in a straight line, so arc length and straight-line distance
    /// are the same thing here and the gap between consecutive points must be
    /// PATH_STEP regardless of how the time step wobbles.
    /// </summary>
    [Fact]
    public void PathRing_AtJitteredDeltaTime_KeepsConstantSpacing()
    {
        (Vector2[] points, PathRing ring) = NewRing(capacity: 256);
        ring.Seed(points, Vector2.Zero);

        // Deterministic jitter — CONVENTIONS §3 forbids unseeded randomness.
        float[] deltas = [1f / 60f, 1f / 144f, 1f / 30f, 0.05f, 1f / 90f, 1f / 20f];
        const float speed = 220f; // Appendix A: SPEED_BASE
        float x = 0f;

        for (int i = 0; i < 60; i++)
        {
            x += speed * deltas[i % deltas.Length];
            ring.Advance(points, new Vector2(x, 0f), PathStep);
        }

        AssertSpacingHolds(points, ring);
    }

    /// <summary>
    /// Same invariant across speed changes — a snake that boosts must not leave
    /// a differently-spaced body behind it (§4.2, SPEED_BASE vs SPEED_BOOST).
    /// </summary>
    [Fact]
    public void PathRing_AtVaryingSpeed_KeepsConstantSpacing()
    {
        (Vector2[] points, PathRing ring) = NewRing(capacity: 256);
        ring.Seed(points, Vector2.Zero);

        float[] speeds = [220f, 380f, 220f, 380f, 90f];
        const float dt = 1f / 60f;
        float x = 0f;

        for (int i = 0; i < 60; i++)
        {
            x += speeds[i % speeds.Length] * dt;
            ring.Advance(points, new Vector2(x, 0f), PathStep);
        }

        AssertSpacingHolds(points, ring);
    }

    /// <summary>
    /// §4.1 says the spacing is an *arc length*. When the head turns, the
    /// straight-line gap between two points is a chord and is therefore shorter
    /// than the distance travelled — so the buffer has to carry arc length
    /// across ticks rather than measure from the last point to the head.
    /// </summary>
    [Fact]
    public void PathRing_WhileTurning_SpacesByArcLengthNotChord()
    {
        (Vector2[] points, PathRing ring) = NewRing(capacity: 256);
        ring.Seed(points, Vector2.Zero);

        const float dt = 1f / 60f;
        const float speed = 220f;
        float heading = 0f;
        Vector2 head = Vector2.Zero;
        float travelled = 0f;

        for (int i = 0; i < 120; i++)
        {
            heading += 3.0f * dt; // rad/s, inside the omega clamp of §4.2
            Vector2 step = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * speed * dt;
            head += step;
            travelled += step.Length();
            ring.Advance(points, head, PathStep);
        }

        // Arc length is what is held constant, so the point count follows the
        // distance travelled, not the displacement.
        int expected = (int)(travelled / PathStep);
        Assert.Equal(expected, ring.Count - 1);

        // Every chord is at most a step, and none has collapsed.
        for (int i = 0; i + 1 < ring.Count; i++)
        {
            float chord = ring.PointAt(points, i).DistanceTo(ring.PointAt(points, i + 1));
            Assert.InRange(chord, PathStep * 0.9f, PathStep + Tolerance);
        }
    }

    [Fact]
    public void PathRing_WhenFull_OverwritesOldestAndNeverGrows()
    {
        const int capacity = 8;
        (Vector2[] points, PathRing ring) = NewRing(capacity);
        ring.Seed(points, Vector2.Zero);

        for (int i = 1; i <= 40; i++)
        {
            ring.Advance(points, new Vector2(PathStep * i, 0f), PathStep);
        }

        Assert.Equal(capacity, ring.Count);
        Assert.Equal(capacity, points.Length);

        // Newest first: the last boundary pushed was at 40 * PATH_STEP.
        Assert.Equal(PathStep * 40f, ring.PointAt(points, 0).X, Tolerance);
        Assert.Equal(PathStep * (40f - capacity + 1), ring.PointAt(points, capacity - 1).X, Tolerance);
    }

    /// <summary>
    /// ARCH §5: nothing in the tick path allocates. The ring is handed the
    /// world's buffer and writes into it.
    /// </summary>
    [Fact]
    public void PathRing_Advance_DoesNotAllocate()
    {
        (Vector2[] points, PathRing ring) = NewRing(capacity: 256);
        ring.Seed(points, Vector2.Zero);
        ring.Advance(points, new Vector2(1f, 0f), PathStep); // warm the path

        long before = GC.GetAllocatedBytesForCurrentThread();

        float x = 1f;
        for (int i = 0; i < 500; i++)
        {
            x += 3.7f;
            ring.Advance(points, new Vector2(x, 0f), PathStep);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    private static void AssertSpacingHolds(Vector2[] points, in PathRing ring)
    {
        Assert.True(ring.Count > 2, "the run pushed too few points to prove anything");

        for (int i = 0; i + 1 < ring.Count; i++)
        {
            float gap = ring.PointAt(points, i).DistanceTo(ring.PointAt(points, i + 1));
            Assert.Equal(PathStep, gap, Tolerance);
        }
    }
}
