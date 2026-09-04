using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// The simulation's cadence is set in project.godot, not in code — spec §11.3,
/// ARCH §4, M0-12. The engine drives the fixed tick and the catch-up cap, so
/// these are the numbers that decide whether a 200 ms stall drops time or
/// spirals.
///
/// Godot defaults the cap to 8. Appendix A says 5. Nothing about a reverted
/// setting is visible until a device thermally throttles, which is exactly the
/// kind of silence this suite exists to break.
/// </summary>
public sealed class EngineTickSettingsTests
{
    [Fact]
    public void ProjectGodot_PhysicsTickRate_MatchesAppendixA()
    {
        SimConfig config = SpecConfig.Create();

        int configured = ReadIntSetting("common/physics_ticks_per_second");

        Assert.Equal(config.TickRate, configured);
    }

    [Fact]
    public void ProjectGodot_CatchUpCap_MatchesAppendixA()
    {
        SimConfig config = SpecConfig.Create();

        int configured = ReadIntSetting("common/max_physics_steps_per_frame");

        Assert.Equal(config.MaxCatchupTicks, configured);
    }

    [Fact]
    public void ProjectGodot_MainScene_IsTheArena()
    {
        string text = ReadProjectGodot();

        // M0-12 replaces the M0-03 boot probe; boot.tscn is deleted in the same
        // commit, so a project still pointing at it would not launch.
        Assert.Contains("run/main_scene=\"res://game/arena.tscn\"", text, StringComparison.Ordinal);
    }

    private static int ReadIntSetting(string key)
    {
        string text = ReadProjectGodot();
        Match match = Regex.Match(text, $@"^{Regex.Escape(key)}=(?<value>\d+)\s*$", RegexOptions.Multiline);

        Assert.True(match.Success, $"project.godot does not set {key}");

        return int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static string ReadProjectGodot()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "project.godot");
        Assert.True(File.Exists(path), $"project.godot was not copied to the test output: {path}");

        return File.ReadAllText(path);
    }
}
