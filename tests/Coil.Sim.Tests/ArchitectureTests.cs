using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Xunit;

// xunit.v3 also defines TestResult; the architecture rules' one is meant here.
using TestResult = NetArchTest.Rules.TestResult;

namespace Coil.Sim.Tests;

/// <summary>
/// The layer rule, as a build failure rather than a convention — ARCH §9,
/// M0-09.
///
/// Each rule in ARCH §9 appears twice here: once asserted against the real
/// assemblies, and once pointed at a deliberately planted violation in
/// <c>Violations/</c> to prove the rule actually rejects what it claims to. A
/// rule nobody has watched fail is a rule nobody knows works — and a silently
/// vacuous architecture test is worse than none, because it reads as proof.
///
/// If a change requires breaking one of these, the design is wrong. Change the
/// design, not the test.
/// </summary>
public sealed class ArchitectureTests
{
    private static Assembly SimAssembly => typeof(SimConfig).Assembly;

    // Coil.Agents holds no types yet, so there is no typeof() to reach it with.
    // Loading by name keeps the rule in place from today, ready for M2's bots.
    private static Assembly AgentsAssembly => Assembly.Load("Coil.Agents");

    private const string ViolationsNamespace = "Coil.Sim.Tests.Violations";

    private static Assembly ViolationsAssembly => typeof(ArchitectureTests).Assembly;

    private static PredicateList PlantedViolations =>
        Types.InAssembly(ViolationsAssembly).That().ResideInNamespace(ViolationsNamespace);

    // --- ARCH §9: Coil.Sim references no engine type ------------------------

    [Fact]
    public void Sim_DoesNotDependOn_GodotNode()
    {
        TestResult result = Types.InAssembly(SimAssembly)
            .Should().NotHaveDependencyOn("Godot.Node")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain("Godot.Node", result));
    }

    [Fact]
    public void Sim_DoesNotDependOn_GodotResource()
    {
        TestResult result = Types.InAssembly(SimAssembly)
            .Should().NotHaveDependencyOn("Godot.Resource")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain("Godot.Resource", result));
    }

    /// <summary>
    /// The layer rule itself: dependencies point one way, so the simulation
    /// cannot see the layers built on top of it (CLAUDE.md, ARCH §2).
    /// </summary>
    [Fact]
    public void Sim_DoesNotDependOn_AgentsOrPresentation()
    {
        TestResult result = Types.InAssembly(SimAssembly)
            .Should().NotHaveDependencyOnAny("Coil.Agents", "Coil.Presentation")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain("Coil.Agents or Coil.Presentation", result));
    }

    /// <summary>
    /// No file I/O in the simulation — it is a pure (state, commands, dt) →
    /// state, and reading a file would also put a clock and a failure mode
    /// inside the tick (ARCH §4).
    /// </summary>
    [Fact]
    public void Sim_DoesNotDependOn_SystemIo()
    {
        TestResult result = Types.InAssembly(SimAssembly)
            .Should().NotHaveDependencyOn("System.IO")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain("System.IO", result));
    }

    /// <summary>
    /// Vacuous today: Coil.Agents holds no types until M2 lands the bots. The
    /// rule is here so it bites the moment they arrive, and the predicate
    /// itself is proved against a planted violation below — but this specific
    /// assertion is checking an empty assembly, and saying so is cheaper than
    /// someone later mistaking it for evidence.
    /// </summary>
    [Fact]
    public void Agents_DoesNotDependOn_Presentation()
    {
        TestResult result = Types.InAssembly(AgentsAssembly)
            .Should().NotHaveDependencyOn("Coil.Presentation")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain("Coil.Presentation", result));
    }

    /// <summary>CONVENTIONS §2: sealed by default; inheritance needs a reason.</summary>
    [Fact]
    public void Sim_Classes_AreSealed()
    {
        TestResult result = Types.InAssembly(SimAssembly)
            .That().AreClasses()
            .Should().BeSealed()
            .GetResult();

        string offenders = string.Join(", ", result.FailingTypeNames ?? []);
        Assert.True(result.IsSuccessful, $"unsealed classes in Coil.Sim: {offenders}");
    }

    // --- Each rule, proved against a planted violation ----------------------

    [Fact]
    public void Rule_GodotNode_RejectsAPlantedViolation()
    {
        TestResult result = PlantedViolations
            .Should().NotHaveDependencyOn("Godot.Node")
            .GetResult();

        AssertCaught(result, nameof(Violations.DependsOnGodotNode));
    }

    [Fact]
    public void Rule_GodotResource_RejectsAPlantedViolation()
    {
        TestResult result = PlantedViolations
            .Should().NotHaveDependencyOn("Godot.Resource")
            .GetResult();

        AssertCaught(result, nameof(Violations.DependsOnGodotResource));
    }

    [Fact]
    public void Rule_AgentsOrPresentation_RejectsAPlantedViolation()
    {
        TestResult result = PlantedViolations
            .Should().NotHaveDependencyOnAny("Coil.Agents", "Coil.Presentation")
            .GetResult();

        AssertCaught(result, nameof(Violations.DependsOnAgents));
        AssertCaught(result, nameof(Violations.DependsOnPresentation));
    }

    [Fact]
    public void Rule_SystemIo_RejectsAPlantedViolation()
    {
        TestResult result = PlantedViolations
            .Should().NotHaveDependencyOn("System.IO")
            .GetResult();

        AssertCaught(result, nameof(Violations.DependsOnSystemIo));
    }

    [Fact]
    public void Rule_Presentation_RejectsAPlantedViolation()
    {
        TestResult result = PlantedViolations
            .Should().NotHaveDependencyOn("Coil.Presentation")
            .GetResult();

        AssertCaught(result, nameof(Violations.DependsOnPresentation));
    }

    [Fact]
    public void Rule_Sealed_RejectsAPlantedViolation()
    {
        TestResult result = PlantedViolations
            .And().AreClasses()
            .Should().BeSealed()
            .GetResult();

        AssertCaught(result, nameof(Violations.UnsealedClass));
    }

    /// <summary>
    /// The Coil.Sim rules above all pass if the assembly somehow exposes no
    /// types — a green suite proving nothing. This is the tripwire for that.
    /// </summary>
    [Fact]
    public void SimRules_AreNotVacuous()
    {
        Assert.NotEmpty(Types.InAssembly(SimAssembly).GetTypes());
    }

    /// <summary>
    /// A planted violation must be reported by name. Asserting only that the
    /// rule failed would also pass if it failed for an unrelated reason.
    /// </summary>
    private static void AssertCaught(TestResult result, string expectedTypeName)
    {
        Assert.False(result.IsSuccessful, "the rule accepted a deliberate violation");

        IEnumerable<string> failing = result.FailingTypeNames ?? [];
        Assert.Contains(
            expectedTypeName,
            failing.Select(name => name.Split('.').Last()));
    }

    private static string Explain(string forbidden, TestResult result)
    {
        string offenders = string.Join(", ", result.FailingTypeNames ?? []);
        return $"depends on {forbidden}: {offenders}";
    }
}
