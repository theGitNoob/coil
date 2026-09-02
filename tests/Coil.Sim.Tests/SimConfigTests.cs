using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Pins <see cref="SimConfig"/> to spec Appendix A and to the balance resource
/// the presentation layer loads it from (ARCH §4 "Configuration", M0-07).
///
/// The failure this guards against is silent: a constant added to the sim but
/// forgotten in <c>balance.tres</c> leaves a field at its type default, and a
/// snake with a turn rate of zero looks like a physics bug three phases later.
/// </summary>
public sealed class SimConfigTests
{
    /// <summary>
    /// Spec Appendix A, verbatim. Two of these are embedded in Appendix A's own
    /// comment text rather than given their own row: the ω clamp bounds on the
    /// OMEGA_NUMERATOR line (§4.2) and the LOD-culled bot steer interval (§8.4).
    /// </summary>
    private static readonly (string Name, double Value)[] AppendixA =
    {
        // Movement
        ("SpeedBase", 220.0),
        ("SpeedBoost", 380.0),
        ("OmegaNumerator", 900.0),
        ("OmegaMin", 2.0),
        ("OmegaMax", 4.5),
        ("PathStep", 6.0),

        // Size
        ("MassStart", 10.0),
        ("RadiusBase", 14.0),
        ("RadiusExp", 0.25),
        ("RadiusMax", 46.0),
        ("LengthBase", 60.0),
        ("LengthPerMass", 1.6),

        // Economy
        ("BoostDrain", 9.0),
        ("BoostDropInterval", 0.35),
        ("BoostDropMass", 4.0),
        ("BoostMinMass", 25.0),
        ("PelletMass", 1.0),
        ("CorpseChunkMass", 6.0),
        ("CorpseReturnRatio", 0.70),
        ("BountyReturnRatio", 1.00),
        ("MagnetBase", 45.0),
        ("MagnetPerRadius", 1.5),
        ("LeaderDecayRate", 0.004),
        ("LeaderDecayThreshold", 500.0),

        // World
        ("ArenaRadius", 3500.0),
        ("BorderWarnRadius", 3350.0),
        ("SnakeCount", 34),
        ("PelletTarget", 2600),
        ("CellSize", 96.0),
        ("GraceDuration", 2.0),

        // Sim
        ("TickRate", 60),
        ("MaxCatchupTicks", 5),
        ("BotSteerInterval", 4),
        ("BotSteerIntervalCulled", 12),
        ("BotPerceptionRadius", 1400.0),
    };

    private static PropertyInfo[] ConfigProperties =>
        typeof(SimConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// "A test asserts none are left at default" — enforced structurally rather
    /// than by inspecting one instance. `required` makes a missing value a
    /// compile error in the loader, and `init` is what makes the POCO immutable
    /// after construction (ARCH §4).
    /// </summary>
    [Fact]
    public void SimConfig_EveryProperty_IsRequiredAndInitOnly()
    {
        foreach (PropertyInfo property in ConfigProperties)
        {
            Assert.True(
                property.IsDefined(typeof(RequiredMemberAttribute), inherit: false),
                $"{property.Name} is not `required`, so the loader can leave it at default.");

            MethodInfo? setter = property.SetMethod;
            Assert.NotNull(setter);
            Assert.Contains(
                typeof(IsExternalInit),
                setter.ReturnParameter.GetRequiredCustomModifiers());
        }
    }

    [Fact]
    public void SimConfig_Covers_AppendixA_Exactly()
    {
        string[] expected = AppendixA.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] actual = ConfigProperties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SimConfig_IsSealed_AndHasNoGodotDependency()
    {
        Assert.True(typeof(SimConfig).IsSealed, "CONVENTIONS §2: sealed by default.");

        string[] referenced = typeof(SimConfig).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        // GodotSharp is permitted for Vector2/Mathf (ARCH §2); the engine is not.
        Assert.DoesNotContain("Coil.Presentation", referenced);
        Assert.DoesNotContain("Coil.Agents", referenced);
    }

    [Fact]
    public void BalanceResource_DefinesEvery_AppendixA_Constant()
    {
        Dictionary<string, string> resource = ReadBalanceResource();

        string[] missing = AppendixA
            .Select(c => c.Name)
            .Where(name => !resource.ContainsKey(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"data/balance.tres is missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void BalanceResource_Values_MatchAppendixA()
    {
        Dictionary<string, string> resource = ReadBalanceResource();

        foreach ((string name, double expected) in AppendixA)
        {
            Assert.True(resource.TryGetValue(name, out string? raw), $"{name} absent from balance.tres");

            double actual = double.Parse(raw!, CultureInfo.InvariantCulture);
            Assert.Equal(expected, actual, precision: 6);
        }
    }

    /// <summary>
    /// Reads the `key = value` pairs from the `[resource]` block of the .tres.
    /// Text parsing, deliberately: the suite boots no engine (ARCH §2), so it
    /// cannot ask Godot to deserialise the resource for it.
    /// </summary>
    private static Dictionary<string, string> ReadBalanceResource()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "balance.tres");
        Assert.True(File.Exists(path), $"balance.tres was not copied to the test output: {path}");

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool inResourceBlock = false;

        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith('['))
            {
                inResourceBlock = trimmed.StartsWith("[resource]", StringComparison.Ordinal);
                continue;
            }

            if (!inResourceBlock || trimmed.Length == 0)
            {
                continue;
            }

            Match match = Regex.Match(trimmed, @"^(?<key>\w+)\s*=\s*(?<value>.+)$");
            if (match.Success && match.Groups["key"].Value != "script")
            {
                values[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();
            }
        }

        return values;
    }
}
