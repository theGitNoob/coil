using System;
using System.Linq;
using Godot;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Pins the pellet store — roadmap M1-01 (struct-of-arrays with a free list,
/// allocated once, never resized), spec §4.3 (pellet masses) and ARCH §4–§5.
///
/// Pellets churn constantly — every eat, corpse drop and boost drop is a
/// spawn or a remove — so these are about the free list staying correct and
/// allocation-free, not about gameplay.
/// </summary>
public sealed class FoodTests
{
    [Fact]
    public void Food_New_HasCapacityAndNoLivePellets()
    {
        Food food = new(capacity: 16);

        Assert.Equal(16, food.Capacity);
        Assert.Equal(0, food.Count);
        for (int i = 0; i < food.Capacity; i++)
        {
            Assert.False(food.IsAlive(i));
        }
    }

    /// <summary>
    /// A full pool drops the pellet rather than throwing: a mass die-off must
    /// never raise an exception inside the tick (ARCH §5).
    /// </summary>
    [Fact]
    public void Food_Spawn_FillsDistinctSlotsUntilFull_ThenReturnsMinusOne()
    {
        const int capacity = 8;
        Food food = new(capacity);
        bool[] seen = new bool[capacity];

        for (int i = 0; i < capacity; i++)
        {
            int index = food.Spawn(new Vector2(i, 0f), 1f);

            Assert.InRange(index, 0, capacity - 1);
            Assert.False(seen[index]);
            seen[index] = true;
        }

        int overflow = food.Spawn(Vector2.Zero, 1f);

        Assert.Equal(capacity, food.Count);
        Assert.Equal(-1, overflow);
        Assert.Equal(capacity, food.Count);
    }

    [Fact]
    public void Food_Spawn_StoresPositionAndMass()
    {
        Food food = new(capacity: 4);

        int index = food.Spawn(new Vector2(12.5f, -7f), 4f);

        Assert.True(food.IsAlive(index));
        Assert.Equal(new Vector2(12.5f, -7f), food.PositionOf(index));
        Assert.Equal(4f, food.MassOf(index));
        Assert.Equal(1, food.Count);
    }

    [Fact]
    public void Food_Remove_FreesTheSlotForTheNextSpawn()
    {
        Food food = new(capacity: 3);
        int a = food.Spawn(Vector2.Zero, 1f);
        food.Spawn(Vector2.Zero, 1f);
        food.Spawn(Vector2.Zero, 1f);

        bool removed = food.Remove(a);
        int reused = food.Spawn(new Vector2(5f, 5f), 6f);

        Assert.True(removed);
        Assert.Equal(a, reused);
        Assert.Equal(3, food.Count);
        Assert.Equal(6f, food.MassOf(reused));
    }

    [Fact]
    public void Food_Remove_OfADeadSlot_ReturnsFalseAndChangesNothing()
    {
        Food food = new(capacity: 3);
        int a = food.Spawn(Vector2.Zero, 1f);
        food.Remove(a);

        bool removedAgain = food.Remove(a);
        bool removedNeverSpawned = food.Remove(2);

        // A double free would put the slot on the free list twice and let two
        // pellets share it.
        Assert.False(removedAgain);
        Assert.False(removedNeverSpawned);
        Assert.Equal(0, food.Count);
        int first = food.Spawn(Vector2.Zero, 1f);
        int second = food.Spawn(Vector2.Zero, 1f);
        int third = food.Spawn(Vector2.Zero, 1f);
        Assert.Equal(-1, food.Spawn(Vector2.Zero, 1f));
        Assert.Equal(3, new[] { first, second, third }.Distinct().Count());
    }

    /// <summary>
    /// ARCH §4: determinism. The same operations must hand out the same slots
    /// in the same order, and a fresh pool starts at slot 0.
    /// </summary>
    [Fact]
    public void Food_SpawnOrder_IsDeterministic()
    {
        Food first = new(capacity: 6);
        Food second = new(capacity: 6);

        int[] firstRun = Script(first);
        int[] secondRun = Script(second);

        Assert.Equal(secondRun, firstRun);
        Assert.Equal([0, 1, 2], firstRun[..3]);
    }

    private static int[] Script(Food food)
    {
        int[] results = new int[10];
        results[0] = food.Spawn(Vector2.Zero, 1f);
        results[1] = food.Spawn(Vector2.Zero, 1f);
        results[2] = food.Spawn(Vector2.Zero, 1f);
        food.Remove(results[1]);
        food.Remove(results[0]);
        results[3] = food.Spawn(Vector2.Zero, 4f);
        results[4] = food.Spawn(Vector2.Zero, 4f);
        results[5] = food.Spawn(Vector2.Zero, 4f);
        food.Remove(results[2]);
        results[6] = food.Spawn(Vector2.Zero, 6f);
        results[7] = food.Spawn(Vector2.Zero, 6f);
        results[8] = food.Spawn(Vector2.Zero, 6f);
        results[9] = food.Spawn(Vector2.Zero, 6f);
        return results;
    }

    /// <summary>
    /// ARCH §5: nothing the tick calls allocates. Eats and drops happen every
    /// tick, so a per-op allocation would be a steady GC stream.
    /// </summary>
    [Fact]
    public void Food_SpawnAndRemove_DoNotAllocate()
    {
        Food food = new(capacity: 64);
        for (int i = 0; i < 32; i++)
        {
            food.Spawn(Vector2.Zero, 1f);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            int index = food.Spawn(new Vector2(i, i), 1f);
            food.Remove(i % 64);
            food.Remove(index);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void Food_AfterManyCycles_CapacityNeverChanges()
    {
        const int capacity = 32;
        Food food = new(capacity);

        for (int cycle = 0; cycle < 5_000; cycle++)
        {
            int index = food.Spawn(new Vector2(cycle, 0f), 1f);
            food.Remove(index);
        }

        for (int i = 0; i < capacity; i++)
        {
            Assert.NotEqual(-1, food.Spawn(Vector2.Zero, 1f));
        }

        Assert.Equal(capacity, food.Capacity);
        Assert.Equal(capacity, food.Count);
        Assert.Equal(-1, food.Spawn(Vector2.Zero, 1f));
    }
}
