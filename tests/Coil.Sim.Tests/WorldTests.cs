using System;
using Godot;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Pins <see cref="World.Tick"/> — spec §4.2 (movement) and ARCH §4 (tick
/// contract, state layout, determinism), M0-11.
///
/// The turn-rate cap is the game's core constraint (§3: "full-speed 180°
/// reversals are impossible — that turn-rate cap *is* the game's core
/// constraint"), so most of these are about heading rather than position.
/// </summary>
public sealed class WorldTests
{
    private const float Dt = 1f / 60f;      // Appendix A: TICK_RATE = 60
    private const float Tolerance = 1e-3f;

    private static World NewWorld(out SimConfig config)
    {
        config = SpecConfig.Create();
        return new World(config);
    }

    private static InputCommand[] Commands(World world) => new InputCommand[world.Capacity];

    [Fact]
    public void World_BeforeAnySpawn_HoldsNoLiveSnakes()
    {
        World world = NewWorld(out SimConfig config);

        Assert.Equal(config.SnakeCount, world.Capacity);
        for (int i = 0; i < world.Capacity; i++)
        {
            Assert.False(world.IsAlive(i));
        }
    }

    /// <summary>§4.2 step 3: head += RIGHT.rotated(heading) * speed * dt.</summary>
    [Fact]
    public void Tick_MovesTheHeadAlongItsHeading_AtBaseSpeed()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, config.MassStart);
        InputCommand[] commands = Commands(world);
        commands[0] = new InputCommand(Heading: 0f, Boost: false, Cast: false);

        world.Tick(Dt, commands);

        Assert.Equal(config.SpeedBase * Dt, world.HeadOf(0).X, Tolerance);
        Assert.Equal(0f, world.HeadOf(0).Y, Tolerance);
    }

    [Fact]
    public void Tick_WhenBoosting_MovesAtBoostSpeed()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, config.MassStart);
        InputCommand[] commands = Commands(world);
        commands[0] = new InputCommand(Heading: 0f, Boost: true, Cast: false);

        world.Tick(Dt, commands);

        Assert.Equal(config.SpeedBoost * Dt, world.HeadOf(0).X, Tolerance);
    }

    /// <summary>
    /// §4.2 step 2: rotate_toward at omega_max, never a snap. A 180° request
    /// must not be granted in one tick — that cap is the game.
    /// </summary>
    [Fact]
    public void Tick_TowardAFarTarget_TurnsNoFasterThanOmegaMax()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, config.MassStart);
        InputCommand[] commands = Commands(world);
        commands[0] = new InputCommand(Heading: Mathf.Pi, Boost: false, Cast: false);

        world.Tick(Dt, commands);

        float omegaMax = OmegaMaxFor(config, config.MassStart);
        Assert.Equal(omegaMax * Dt, world.HeadingOf(0), Tolerance);
        Assert.True(world.HeadingOf(0) < Mathf.Pi, "a 180 degree reversal was granted in one tick");
    }

    [Fact]
    public void Tick_WhenTheTargetIsWithinReach_AdoptsItExactly()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, config.MassStart);
        InputCommand[] commands = Commands(world);
        float tiny = OmegaMaxFor(config, config.MassStart) * Dt * 0.5f;
        commands[0] = new InputCommand(Heading: tiny, Boost: false, Cast: false);

        world.Tick(Dt, commands);

        Assert.Equal(tiny, world.HeadingOf(0), Tolerance);
    }

    /// <summary>
    /// §4.2 claims big snakes are "genuinely less nimble ... the primary
    /// balancing force against mass". Appendix A's numbers do not deliver it.
    ///
    /// Radius spans 14 (mass 0) to a clamped 46, so OMEGA_NUMERATOR / radius
    /// spans 64.3 down to 19.6 — never below the upper clamp of 4.5. omega_max
    /// is therefore the same for a 10-mass snake and a 2000-mass one, and turn
    /// rate is mass-independent today.
    ///
    /// This pins the arithmetic as it actually is rather than the intent. The
    /// numerator would need to be roughly 63 (4.5 * 14) to 92 (2.0 * 46) for the
    /// clamp bounds to bite; when it is retuned, this test fails and forces the
    /// intent back into the spec instead of letting it stay silently inert.
    /// </summary>
    [Fact]
    public void Tick_TurnRate_IsMassIndependent_BecauseTheUpperClampDominates()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, config.MassStart);
        world.Spawn(1, Vector2.Zero, heading: 0f, mass: config.MaxMass);
        InputCommand[] commands = Commands(world);
        commands[0] = new InputCommand(Heading: Mathf.Pi, Boost: false, Cast: false);
        commands[1] = new InputCommand(Heading: Mathf.Pi, Boost: false, Cast: false);

        world.Tick(Dt, commands);

        Assert.Equal(world.HeadingOf(0), world.HeadingOf(1), Tolerance);
        Assert.Equal(config.OmegaMax * Dt, world.HeadingOf(0), Tolerance);

        // The reason, stated as arithmetic: even the largest radius the curve
        // can reach leaves the numerator above the ceiling.
        Assert.True(
            config.OmegaNumerator / config.RadiusMax > config.OmegaMax,
            "OMEGA_NUMERATOR now lets the clamp bite — §4.2's mass/nimbleness "
                + "trade-off is live again and these tests need rewriting to assert it");
    }

    /// <summary>§4.2: omega_max = clamp(900 / radius, 2.0, 4.5).</summary>
    [Theory]
    [InlineData(10f)]
    [InlineData(500f)]
    [InlineData(2000f)]
    public void Tick_TurnRate_StaysWithinTheConfiguredClamp(float mass)
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, mass);
        InputCommand[] commands = Commands(world);
        commands[0] = new InputCommand(Heading: Mathf.Pi, Boost: false, Cast: false);

        world.Tick(Dt, commands);

        float turned = world.HeadingOf(0) / Dt;
        Assert.InRange(turned, config.OmegaMin - Tolerance, config.OmegaMax + Tolerance);
    }

    /// <summary>§4.1: radius = clamp(14 * (1 + mass/100)^0.25, 14, 46).</summary>
    [Theory]
    [InlineData(0f, 14f)]
    [InlineData(10f, 14.344f)]
    [InlineData(2000f, 29.968f)]
    public void Radius_FollowsTheSpecCurve(float mass, float expected)
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, mass);

        Assert.Equal(expected, world.RadiusOf(0), 0.01f);
    }

    [Fact]
    public void Tick_LaysDownPathPoints_AtPathStepSpacing()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, config.MassStart);
        InputCommand[] commands = Commands(world);

        for (int i = 0; i < 60; i++)
        {
            world.Tick(Dt, commands);
        }

        Assert.True(world.PathCountOf(0) > 2, "no path was laid down");
        for (int age = 0; age + 1 < world.PathCountOf(0); age++)
        {
            float gap = world.PathPointOf(0, age).DistanceTo(world.PathPointOf(0, age + 1));
            Assert.Equal(config.PathStep, gap, 0.01f);
        }
    }

    /// <summary>ARCH §4 and §5: the tick allocates nothing, for any snake count.</summary>
    [Fact]
    public void Tick_ForAFullArena_DoesNotAllocate()
    {
        World world = NewWorld(out SimConfig config);
        for (int i = 0; i < world.Capacity; i++)
        {
            world.Spawn(i, new Vector2(i * 20f, 0f), heading: i * 0.1f, config.MassStart);
        }

        InputCommand[] commands = Commands(world);
        for (int i = 0; i < commands.Length; i++)
        {
            commands[i] = new InputCommand(Heading: i * 0.2f, Boost: i % 3 == 0, Cast: false);
        }

        world.Tick(Dt, commands); // warm every path before measuring
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int t = 0; t < 120; t++)
        {
            world.Tick(Dt, commands);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    /// <summary>
    /// ARCH §4: the same command stream must produce identical state. This is
    /// the precondition for the predict-and-replay netcode in §11.3, and C-04
    /// will extend it to 1,000 ticks.
    /// </summary>
    [Fact]
    public void Tick_ForTheSameCommandStream_IsDeterministic()
    {
        World left = NewWorld(out SimConfig config);
        World right = new(config);

        for (int i = 0; i < 8; i++)
        {
            left.Spawn(i, new Vector2(i * 13f, i * 7f), heading: i * 0.3f, config.MassStart);
            right.Spawn(i, new Vector2(i * 13f, i * 7f), heading: i * 0.3f, config.MassStart);
        }

        InputCommand[] commands = new InputCommand[left.Capacity];

        for (int t = 0; t < 200; t++)
        {
            for (int i = 0; i < 8; i++)
            {
                commands[i] = new InputCommand(Heading: Mathf.Sin((t * 0.1f) + i), Boost: (t + i) % 5 == 0, Cast: false);
            }

            left.Tick(Dt, commands);
            right.Tick(Dt, commands);
        }

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(left.HeadOf(i), right.HeadOf(i));
            Assert.Equal(left.HeadingOf(i), right.HeadingOf(i));
            Assert.Equal(left.PathCountOf(i), right.PathCountOf(i));
        }
    }

    [Fact]
    public void Tick_WhenCommandsAreShorterThanCapacity_TreatsTheRestAsNeutral()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(5, Vector2.Zero, heading: 0f, config.MassStart);

        // Only two commands for a 34-slot arena: slot 5 gets the neutral default.
        world.Tick(Dt, new InputCommand[2]);

        Assert.Equal(0f, world.HeadingOf(5), Tolerance);
        Assert.Equal(config.SpeedBase * Dt, world.HeadOf(5).X, Tolerance);
    }

    [Fact]
    public void Tick_LeavesUnspawnedSlots_Untouched()
    {
        World world = NewWorld(out SimConfig config);
        world.Spawn(0, Vector2.Zero, heading: 0f, config.MassStart);

        world.Tick(Dt, Commands(world));

        Assert.False(world.IsAlive(3));
        Assert.Equal(0, world.PathCountOf(3));
    }

    private static float OmegaMaxFor(SimConfig config, float mass)
    {
        float radius = Math.Clamp(
            config.RadiusBase * MathF.Pow(1f + (mass / config.RadiusMassDivisor), config.RadiusExp),
            config.RadiusBase,
            config.RadiusMax);

        return Math.Clamp(config.OmegaNumerator / radius, config.OmegaMin, config.OmegaMax);
    }
}
