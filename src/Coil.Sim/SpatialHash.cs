using System;
using Godot;

namespace Coil.Sim;

/// <summary>
/// The uniform spatial hash, M1-04 — spec §5, <c>CELL_SIZE</c> = 96 u.
///
/// Spec §5 keeps bodies, pellets and hazards "in the same grid, keyed
/// separately"; here that is three instances of this class sharing one
/// geometry. An item is a dense integer id chosen by the caller (a pellet
/// slot, a capsule slot) with one position; only heads query, and a query
/// touches about nine cells.
///
/// Each cell is a doubly linked list threaded through per-item arrays, so
/// insert, remove and move are O(1) and there are no per-cell arrays to
/// overflow in a crowded cell. Every buffer is allocated here and never
/// resized (ARCH §4); nothing in it allocates, throws or hashes, so it is
/// safe inside the tick (ARCH §5).
///
/// A point outside the grid — a head past the border before it dies — is
/// clamped into the nearest edge cell. Query corners go through the same
/// clamp, so any query whose box contains such a point still visits its cell;
/// the exact filter then decides.
///
/// Result order depends only on the sequence of operations (cells row-major,
/// each list from its head, newest first), which keeps the replay
/// deterministic (ARCH §4).
/// </summary>
public sealed class SpatialHash
{
    private readonly float _extent;
    private readonly float _inverseCellSize;

    // First item in each cell, or -1.
    private readonly int[] _cellHead;

    // Per item; -1 means none. _cellOf is -1 while the item is absent.
    private readonly int[] _next;
    private readonly int[] _prev;
    private readonly int[] _cellOf;
    private readonly Vector2[] _positions;

    private int _count;

    /// <summary>
    /// The grid covers the square [-extent, extent]²; the world passes the
    /// arena radius.
    /// </summary>
    internal SpatialHash(int capacity, float cellSize, float extent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(extent);

        CellSize = cellSize;
        _extent = extent;
        _inverseCellSize = 1f / cellSize;
        CellsPerSide = (int)MathF.Ceiling(2f * extent / cellSize);

        _cellHead = new int[CellsPerSide * CellsPerSide];
        _cellHead.AsSpan().Fill(-1);

        _next = new int[capacity];
        _prev = new int[capacity];
        _cellOf = new int[capacity];
        _cellOf.AsSpan().Fill(-1);
        _positions = new Vector2[capacity];
    }

    /// <summary>Item ids the hash can hold: ids run 0 to Capacity - 1.</summary>
    public int Capacity => _cellOf.Length;

    /// <summary>Items currently in the hash.</summary>
    public int Count => _count;

    public float CellSize { get; }

    public int CellsPerSide { get; }

    public bool Contains(int item) => _cellOf[item] >= 0;

    /// <summary>The stored position; meaningful only where <see cref="Contains"/> is true.</summary>
    public Vector2 PositionOf(int item) => _positions[item];

    /// <summary>
    /// Adds an item at the head of its cell's list. Returns false and changes
    /// nothing if the item is already present, so a double insert can never
    /// link a node into two lists.
    /// </summary>
    internal bool Insert(int item, Vector2 position)
    {
        if (_cellOf[item] >= 0)
        {
            return false;
        }

        _positions[item] = position;
        Link(item, CellIndexOf(position));
        _count++;

        return true;
    }

    /// <summary>
    /// Unlinks an item. Returns false and changes nothing if it is absent.
    /// </summary>
    internal bool Remove(int item)
    {
        if (_cellOf[item] < 0)
        {
            return false;
        }

        Unlink(item);
        _count--;

        return true;
    }

    /// <summary>
    /// Updates an item's position, relinking only when it crossed into
    /// another cell — most moves stay in their cell. Returns false and changes
    /// nothing if the item is absent.
    /// </summary>
    internal bool Move(int item, Vector2 position)
    {
        int oldCell = _cellOf[item];
        if (oldCell < 0)
        {
            return false;
        }

        _positions[item] = position;

        int newCell = CellIndexOf(position);
        if (newCell != oldCell)
        {
            Unlink(item);
            Link(item, newCell);
        }

        return true;
    }

    /// <summary>
    /// Writes the ids of every item whose stored position lies in the
    /// inclusive box [<paramref name="min"/>, <paramref name="max"/>] into
    /// <paramref name="results"/>, in visit order, and returns how many
    /// matched. Matches beyond <c>results.Length</c> are counted but not
    /// written, so the caller detects overflow as
    /// <c>count &gt; results.Length</c> (ARCH §5: caller-supplied buffer,
    /// returns a count). An inverted box matches nothing.
    ///
    /// The filter is exact, not "everything in the visited cells", so the
    /// result equals a brute-force scan of the same predicate.
    /// </summary>
    public int Query(Vector2 min, Vector2 max, Span<int> results)
    {
        if (min.X > max.X || min.Y > max.Y)
        {
            return 0;
        }

        int x0 = CellCoord(min.X);
        int x1 = CellCoord(max.X);
        int y0 = CellCoord(min.Y);
        int y1 = CellCoord(max.Y);

        int total = 0;

        for (int cy = y0; cy <= y1; cy++)
        {
            int row = cy * CellsPerSide;

            for (int cx = x0; cx <= x1; cx++)
            {
                for (int item = _cellHead[row + cx]; item >= 0; item = _next[item])
                {
                    Vector2 p = _positions[item];
                    if (p.X < min.X || p.X > max.X || p.Y < min.Y || p.Y > max.Y)
                    {
                        continue;
                    }

                    if (total < results.Length)
                    {
                        results[total] = item;
                    }

                    total++;
                }
            }
        }

        return total;
    }

    // One clamp for points and query corners, so a point and a box edge at
    // the same coordinate always land in the same cell. Clamped as a float,
    // before the cast: past int range the cast wraps on x64 and saturates on
    // ARM64, and NaN must not index the grid.
    private int CellCoord(float value)
    {
        float cell = MathF.Floor((value + _extent) * _inverseCellSize);
        int last = CellsPerSide - 1;

        if (!(cell > 0f))
        {
            return 0;
        }

        return cell < last ? (int)cell : last;
    }

    private int CellIndexOf(Vector2 position) =>
        (CellCoord(position.Y) * CellsPerSide) + CellCoord(position.X);

    private void Link(int item, int cell)
    {
        int head = _cellHead[cell];

        _next[item] = head;
        _prev[item] = -1;
        if (head >= 0)
        {
            _prev[head] = item;
        }

        _cellHead[cell] = item;
        _cellOf[item] = cell;
    }

    private void Unlink(int item)
    {
        int next = _next[item];
        int prev = _prev[item];

        if (prev >= 0)
        {
            _next[prev] = next;
        }
        else
        {
            _cellHead[_cellOf[item]] = next;
        }

        if (next >= 0)
        {
            _prev[next] = prev;
        }

        _cellOf[item] = -1;
    }
}
