using System.IO;
using Godot;

namespace Coil.Sim.Tests.Violations;

// Deliberate architecture violations — one per rule in ARCH §9, M0-09.
//
// A rule nobody has seen fail is a rule nobody knows works. These types exist
// so each rule can be pointed at a known-bad namespace and shown to reject it;
// the real rules run against Coil.Sim and Coil.Agents, which these never touch.
//
// They live in the TEST assembly on purpose. Proving "Coil.Sim must not depend
// on Coil.Presentation" by adding a Coil.Presentation reference here would give
// this project the very dependency the rule exists to forbid, so the stand-in
// namespaces below play that part instead.

/// <summary>Violates: Coil.Sim must not depend on Godot.Node.</summary>
public static class DependsOnGodotNode
{
    public static string NameOf(Node node) => node.Name;
}

/// <summary>Violates: Coil.Sim must not depend on Godot.Resource.</summary>
public static class DependsOnGodotResource
{
    public static string PathOf(Resource resource) => resource.ResourcePath;
}

/// <summary>Violates: Coil.Sim must not depend on System.IO.</summary>
public static class DependsOnSystemIo
{
    public static string Combine(string left, string right) => Path.Combine(left, right);
}

/// <summary>Violates: Coil.Sim must not depend on Coil.Agents.</summary>
public static class DependsOnAgents
{
    public static int Read(Coil.Agents.StandIn stand) => stand.Value;
}

/// <summary>Violates: Coil.Sim and Coil.Agents must not depend on Coil.Presentation.</summary>
public static class DependsOnPresentation
{
    public static int Read(Coil.Presentation.StandIn stand) => stand.Value;
}

/// <summary>Violates: classes in Coil.Sim are sealed (CONVENTIONS §2).</summary>
public class UnsealedClass
{
    public int Value { get; init; }
}
