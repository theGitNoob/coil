using System;
using System.Linq;
using Godot;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Pins the spatial hash — roadmap M1-04 (insert/remove/query on 96 u cells,
/// query allocates nothing), spec §5 and ARCH §4–§5.
///
/// The randomised differential test against brute force is M1-05; these are
/// the focused cases: cell edges, clamping, list integrity and the
/// no-allocation contract.
/// </summary>
public sealed class SpatialHashTests
{
    // Appendix A: CELL_SIZE and ARENA_RADIUS.
    private const float CellSize = 96f;
    private const float Extent = 3500f;

    private static SpatialHash NewHash(int capacity = 64) => new(capacity, CellSize, Extent);

    private static int[] Ids(params int[] ids) => ids;

    // Sorted, so assertions do not depend on visit order unless a test wants it.
    private static int[] Find(SpatialHash hash, Vector2 min, Vector2 max)
    {
        Span<int> buffer = stackalloc int[256];
        int total = hash.Query(min, max, buffer);

        return buffer[..Math.Min(total, buffer.Length)].ToArray().OrderBy(id => id).ToArray();
    }

    private static int[] FindAround(SpatialHash hash, Vector2 centre, float halfSize = 1f) =>
        Find(hash, centre - new Vector2(halfSize, halfSize), centre + new Vector2(halfSize, halfSize));

    [Fact]
    public void SpatialHash_New_HasCapacityCellSizeAndNoItems()
    {
        SpatialHash hash = new(capacity: 32, CellSize, Extent);

        // ceil(2 * 3500 / 96) = ceil(72.9) = 73 (spec §5, Appendix A).
        Assert.Equal(32, hash.Capacity);
        Assert.Equal(0, hash.Count);
        Assert.Equal(CellSize, hash.CellSize);
        Assert.Equal(73, hash.CellsPerSide);
        for (int i = 0; i < hash.Capacity; i++)
        {
            Assert.False(hash.Contains(i));
        }
    }

    [Fact]
    public void SpatialHash_New_RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash(-1, CellSize, Extent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash(8, 0f, Extent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash(8, CellSize, 0f));
    }

    [Fact]
    public void Query_AroundInsertedItem_FindsItAndNotElsewhere()
    {
        SpatialHash hash = NewHash();
        hash.Insert(3, new Vector2(100f, 200f));

        int[] near = FindAround(hash, new Vector2(100f, 200f), halfSize: 5f);
        int[] far = FindAround(hash, new Vector2(-1000f, 900f), halfSize: 5f);

        Assert.Equal(Ids(3), near);
        Assert.Empty(far);
        Assert.True(hash.Contains(3));
        Assert.Equal(1, hash.Count);
        Assert.Equal(new Vector2(100f, 200f), hash.PositionOf(3));
    }

    [Fact]
    public void Query_PointOnMinOrMaxEdge_IsReturned()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(10f, 20f));
        hash.Insert(1, new Vector2(30f, 40f));

        // Bounds are inclusive on all four sides.
        int[] result = Find(hash, new Vector2(10f, 20f), new Vector2(30f, 40f));

        Assert.Equal(Ids(0, 1), result);
    }

    [Fact]
    public void Query_ItemInVisitedCellButOutsideAabb_IsNotReturned()
    {
        SpatialHash hash = NewHash();
        // Same 96 u cell (-2540..-2444 x -2540..-2444), different sides of the box.
        hash.Insert(0, new Vector2(-2500f, -2500f));
        hash.Insert(1, new Vector2(-2460f, -2500f));
        hash.Insert(2, new Vector2(-2500f, -2460f));

        int[] result = Find(hash, new Vector2(-2510f, -2510f), new Vector2(-2490f, -2490f));

        Assert.Equal(Ids(0), result);
    }

    [Fact]
    public void Query_SpanningSeveralCells_ReturnsItemsFromAllOfThem()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(-10f, -10f));
        hash.Insert(1, new Vector2(10f, -10f));
        hash.Insert(2, new Vector2(-10f, 10f));
        hash.Insert(3, new Vector2(10f, 10f));
        hash.Insert(4, new Vector2(500f, 500f));

        // A head's ~9-cell query (spec §5) straddling the origin cell corner.
        int[] result = Find(hash, new Vector2(-150f, -150f), new Vector2(150f, 150f));

        Assert.Equal(Ids(0, 1, 2, 3), result);
    }

    [Fact]
    public void Query_PointsOnCellEdges_AreFoundFromEitherSide()
    {
        SpatialHash hash = NewHash();
        for (int k = 0; k < 6; k++)
        {
            float edge = -Extent + (k * CellSize);
            hash.Insert(k, new Vector2(edge, edge));
        }

        for (int k = 0; k < 6; k++)
        {
            float edge = -Extent + (k * CellSize);
            Vector2 point = new(edge, edge);

            // Boxes that end exactly on the edge, start exactly on it, and straddle it.
            Assert.Contains(k, Find(hash, point - new Vector2(50f, 50f), point));
            Assert.Contains(k, Find(hash, point, point + new Vector2(50f, 50f)));
            Assert.Contains(k, Find(hash, point - new Vector2(1f, 1f), point + new Vector2(1f, 1f)));
        }
    }

    [Fact]
    public void Query_PointJustBelowACellEdge_IsFoundOnlyByBoxesReachingIt()
    {
        SpatialHash hash = NewHash();
        float edge = -Extent + (10f * CellSize);
        float justBelow = MathF.BitDecrement(edge);
        hash.Insert(0, new Vector2(justBelow, 0f));

        int[] startingAtEdge = Find(hash, new Vector2(edge, -1f), new Vector2(edge + 50f, 1f));
        int[] endingAtEdge = Find(hash, new Vector2(edge - 50f, -1f), new Vector2(edge, 1f));

        Assert.Empty(startingAtEdge);
        Assert.Equal(Ids(0), endingAtEdge);
    }

    [Fact]
    public void Query_NegativeQuadrant_FindsItems()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(-1234.5f, -987.25f));
        hash.Insert(1, new Vector2(-1234.5f, 987.25f));

        int[] result = FindAround(hash, new Vector2(-1234.5f, -987.25f), halfSize: 10f);

        Assert.Equal(Ids(0), result);
    }

    [Fact]
    public void Query_PointsOutsideTheGrid_AreClampedAndStillFound()
    {
        SpatialHash hash = NewHash();
        // A head past the border before it dies (spec §5).
        hash.Insert(0, new Vector2(Extent + 500f, Extent + 500f));
        hash.Insert(1, new Vector2(-Extent - 900f, 0f));
        hash.Insert(2, new Vector2(0f, -Extent - 30f));

        int[] first = FindAround(hash, new Vector2(Extent + 500f, Extent + 500f), halfSize: 10f);
        int[] second = FindAround(hash, new Vector2(-Extent - 900f, 0f), halfSize: 10f);
        int[] third = Find(hash, new Vector2(-50f, -Extent - 100f), new Vector2(50f, -Extent));

        Assert.Equal(Ids(0), first);
        Assert.Equal(Ids(1), second);
        Assert.Equal(Ids(2), third);
    }

    /// <summary>
    /// A coordinate past int range must clamp to the far edge, not wrap: on
    /// net8.0 a float-to-int cast overflows to int.MinValue on x64 and
    /// saturates on ARM64, which would also make the two disagree (ARCH §4).
    /// </summary>
    [Fact]
    public void Query_ReachingBeyondIntRange_ClampsToTheFarEdgeAndFindsEverything()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(100f, 0f));
        hash.Insert(1, new Vector2(1e12f, 0f));

        int[] found = Find(hash, new Vector2(0f, -1f), new Vector2(1e12f, 1f));

        Assert.Equal(Ids(0, 1), found);
    }

    [Fact]
    public void Query_FarOutsideTheGrid_ReturnsOnlyItemsInsideItsAabb()
    {
        SpatialHash hash = NewHash();
        // Clamped into the same corner cell as the query corners below.
        hash.Insert(0, new Vector2(Extent - 1f, Extent - 1f));
        hash.Insert(1, new Vector2(Extent + 200f, Extent + 200f));

        int[] beyond = Find(hash, new Vector2(Extent + 5000f, Extent + 5000f), new Vector2(Extent + 6000f, Extent + 6000f));
        int[] huge = Find(hash, new Vector2(-1e9f, -1e9f), new Vector2(1e9f, 1e9f));

        Assert.Empty(beyond);
        Assert.Equal(Ids(0, 1), huge);
    }

    [Fact]
    public void Remove_PresentItem_StopsBeingReturnedAndCountDrops()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(5f, 5f));
        hash.Insert(1, new Vector2(6f, 6f));

        bool removed = hash.Remove(0);

        Assert.True(removed);
        Assert.False(hash.Contains(0));
        Assert.Equal(1, hash.Count);
        Assert.Equal(Ids(1), FindAround(hash, new Vector2(5f, 5f), halfSize: 10f));
    }

    [Fact]
    public void Remove_OfAbsentOrAlreadyRemovedItem_ReturnsFalseAndChangesNothing()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(5f, 5f));
        hash.Insert(1, new Vector2(6f, 6f));
        hash.Remove(0);

        bool again = hash.Remove(0);
        bool neverInserted = hash.Remove(7);

        Assert.False(again);
        Assert.False(neverInserted);
        Assert.Equal(1, hash.Count);
        Assert.Equal(Ids(1), FindAround(hash, new Vector2(5f, 5f), halfSize: 10f));
    }

    [Fact]
    public void Insert_OfPresentItem_ReturnsFalseAndKeepsTheOriginalPosition()
    {
        SpatialHash hash = NewHash();
        hash.Insert(2, new Vector2(10f, 10f));

        bool inserted = hash.Insert(2, new Vector2(900f, 900f));

        Assert.False(inserted);
        Assert.Equal(1, hash.Count);
        Assert.Equal(new Vector2(10f, 10f), hash.PositionOf(2));
        Assert.Equal(Ids(2), FindAround(hash, new Vector2(10f, 10f), halfSize: 5f));
        Assert.Empty(FindAround(hash, new Vector2(900f, 900f), halfSize: 5f));
    }

    [Fact]
    public void Insert_AfterRemove_ReusesTheId()
    {
        SpatialHash hash = NewHash();
        hash.Insert(4, new Vector2(1f, 1f));
        hash.Remove(4);

        bool inserted = hash.Insert(4, new Vector2(-300f, 300f));

        Assert.True(inserted);
        Assert.Equal(1, hash.Count);
        Assert.Equal(Ids(4), FindAround(hash, new Vector2(-300f, 300f), halfSize: 5f));
        Assert.Empty(FindAround(hash, new Vector2(1f, 1f), halfSize: 5f));
    }

    [Fact]
    public void Move_WithinTheSameCell_IsFoundAtTheNewPositionOnly()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(10f, 10f));

        bool moved = hash.Move(0, new Vector2(20f, 20f));

        Assert.True(moved);
        Assert.Equal(new Vector2(20f, 20f), hash.PositionOf(0));
        Assert.Equal(Ids(0), FindAround(hash, new Vector2(20f, 20f), halfSize: 2f));
        Assert.Empty(FindAround(hash, new Vector2(10f, 10f), halfSize: 2f));
        Assert.Equal(1, hash.Count);
    }

    [Fact]
    public void Move_AcrossCells_IsFoundAtTheNewPositionOnly()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(10f, 10f));
        hash.Insert(1, new Vector2(12f, 12f));

        bool moved = hash.Move(0, new Vector2(-2000f, 1500f));

        Assert.True(moved);
        Assert.Equal(Ids(0), FindAround(hash, new Vector2(-2000f, 1500f), halfSize: 2f));
        Assert.Equal(Ids(1), FindAround(hash, new Vector2(10f, 10f), halfSize: 10f));
        Assert.Equal(2, hash.Count);
    }

    [Fact]
    public void Move_OfAbsentItem_ReturnsFalseAndChangesNothing()
    {
        SpatialHash hash = NewHash();

        bool moved = hash.Move(5, new Vector2(1f, 1f));

        Assert.False(moved);
        Assert.False(hash.Contains(5));
        Assert.Equal(0, hash.Count);
        Assert.Empty(Find(hash, new Vector2(-10f, -10f), new Vector2(10f, 10f)));
    }

    /// <summary>
    /// Doubly linked lists: removing the head, a middle node and the tail of
    /// one cell's list must leave every other node reachable.
    /// </summary>
    [Fact]
    public void Remove_FromHeadMiddleAndTailOfACrowdedCell_KeepsTheRest()
    {
        const int crowd = 50;
        SpatialHash hash = NewHash(capacity: crowd);
        for (int i = 0; i < crowd; i++)
        {
            hash.Insert(i, new Vector2(1f + (i * 0.01f), 1f));
        }

        // Insertion pushes at the head, so 49 is the head and 0 the tail.
        hash.Remove(49);
        hash.Remove(25);
        hash.Remove(0);

        int[] expected = Enumerable.Range(0, crowd).Where(i => i != 49 && i != 25 && i != 0).ToArray();
        Assert.Equal(expected, Find(hash, new Vector2(0f, 0f), new Vector2(5f, 5f)));
        Assert.Equal(crowd - 3, hash.Count);
    }

    [Fact]
    public void Move_OutOfACrowdedCell_KeepsTheRest()
    {
        const int crowd = 20;
        SpatialHash hash = NewHash(capacity: crowd);
        for (int i = 0; i < crowd; i++)
        {
            hash.Insert(i, new Vector2(1f, 1f));
        }

        hash.Move(19, new Vector2(1000f, 1000f));
        hash.Move(10, new Vector2(1000f, 1000f));
        hash.Move(0, new Vector2(1000f, 1000f));

        int[] stayed = Enumerable.Range(0, crowd).Where(i => i != 19 && i != 10 && i != 0).ToArray();
        Assert.Equal(stayed, Find(hash, new Vector2(0f, 0f), new Vector2(5f, 5f)));
        Assert.Equal(Ids(0, 10, 19), FindAround(hash, new Vector2(1000f, 1000f), halfSize: 5f));
    }

    [Fact]
    public void Query_WithSmallBuffer_ReturnsTotalAndWritesOnlyTrueMatches()
    {
        SpatialHash hash = NewHash();
        for (int i = 0; i < 10; i++)
        {
            hash.Insert(i, new Vector2(i * 10f, 0f));
        }
        hash.Insert(10, new Vector2(2000f, 2000f));
        Span<int> small = stackalloc int[4];
        small.Fill(-1);

        int total = hash.Query(new Vector2(-1f, -1f), new Vector2(200f, 1f), small);

        // Overflow is detected by the caller as total > results.Length.
        Assert.Equal(10, total);
        Assert.True(total > small.Length);
        foreach (int id in small)
        {
            Assert.InRange(id, 0, 9);
        }
        Assert.Equal(4, small.ToArray().Distinct().Count());
    }

    [Fact]
    public void Query_WithEmptyBuffer_StillCountsMatches()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(1f, 1f));
        hash.Insert(1, new Vector2(2f, 2f));

        int total = hash.Query(new Vector2(0f, 0f), new Vector2(5f, 5f), Span<int>.Empty);

        Assert.Equal(2, total);
    }

    [Fact]
    public void Query_WithInvertedRange_ReturnsZero()
    {
        SpatialHash hash = NewHash();
        hash.Insert(0, new Vector2(0f, 0f));
        Span<int> buffer = stackalloc int[8];

        int invertedX = hash.Query(new Vector2(10f, -10f), new Vector2(-10f, 10f), buffer);
        int invertedY = hash.Query(new Vector2(-10f, 10f), new Vector2(10f, -10f), buffer);

        Assert.Equal(0, invertedX);
        Assert.Equal(0, invertedY);
    }

    /// <summary>
    /// ARCH §4: the same operations give the same results in the same order,
    /// not merely the same set.
    /// </summary>
    [Fact]
    public void SpatialHash_SameOperationSequence_GivesIdenticalResultsInIdenticalOrder()
    {
        SpatialHash first = NewHash(capacity: 128);
        SpatialHash second = NewHash(capacity: 128);

        Script(first);
        Script(second);

        Span<int> a = stackalloc int[128];
        Span<int> b = stackalloc int[128];
        int countA = first.Query(new Vector2(-400f, -400f), new Vector2(400f, 400f), a);
        int countB = second.Query(new Vector2(-400f, -400f), new Vector2(400f, 400f), b);
        Assert.True(countA > 0);
        Assert.Equal(countA, countB);
        Assert.True(a[..countA].SequenceEqual(b[..countB]));

        static void Script(SpatialHash hash)
        {
            for (int i = 0; i < 100; i++)
            {
                hash.Insert(i, new Vector2(((i * 37) % 800) - 400f, ((i * 91) % 800) - 400f));
            }

            for (int i = 0; i < 100; i += 3)
            {
                hash.Move(i, new Vector2(((i * 53) % 600) - 300f, ((i * 17) % 600) - 300f));
            }

            for (int i = 1; i < 100; i += 7)
            {
                hash.Remove(i);
            }
        }
    }

    /// <summary>
    /// ARCH §5: the hash is read and written inside the tick, so a per-call
    /// allocation would be a steady GC stream.
    /// </summary>
    [Fact]
    public void Query_DoesNotAllocate()
    {
        SpatialHash hash = NewHash(capacity: 64);
        for (int i = 0; i < 64; i++)
        {
            hash.Insert(i, new Vector2((i * 20f) - 600f, (i * 7f) - 200f));
        }
        Span<int> buffer = stackalloc int[16];
        hash.Query(new Vector2(-500f, -500f), new Vector2(500f, 500f), buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();

        int sink = 0;
        for (int i = 0; i < 10_000; i++)
        {
            float offset = i % 100;
            sink += hash.Query(new Vector2(-500f + offset, -500f), new Vector2(500f, 500f + offset), buffer);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.True(sink > 0);
    }

    [Fact]
    public void InsertRemoveAndMove_DoNotAllocate()
    {
        SpatialHash hash = NewHash(capacity: 64);
        for (int i = 0; i < 32; i++)
        {
            hash.Insert(i, new Vector2(i, i));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            int id = 32 + (i % 32);
            hash.Insert(id, new Vector2(i % 3000, -(i % 3000)));
            hash.Move(id, new Vector2(-(i % 3000), i % 3000));
            hash.Move(i % 32, new Vector2(i % 500, 0f));
            hash.Remove(id);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
