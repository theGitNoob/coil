using System.Runtime.CompilerServices;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Pins <see cref="InputCommand"/>, the only channel into the simulation
/// (spec Appendix B, ARCH §3 crossing #1, M0-08).
///
/// It is a data carrier with no behaviour, so these assert the properties the
/// rest of the architecture leans on rather than any logic: it is a value type
/// with no references, so `ReadOnlySpan&lt;InputCommand&gt;` can be handed to
/// `World.Tick` with no allocation and no marshalling (ARCH §4, §5).
/// </summary>
public sealed class InputCommandTests
{
    [Fact]
    public void InputCommand_Constructed_ExposesItsFields()
    {
        InputCommand command = new(Heading: 1.25f, Boost: true, Cast: false);

        Assert.Equal(1.25f, command.Heading);
        Assert.True(command.Boost);
        Assert.False(command.Cast);
    }

    /// <summary>
    /// `World.Tick` takes one command per snake as a span. Slots for actors that
    /// produced nothing this tick are `default`, so the default has to be the
    /// neutral command — a default that boosted or cast would drain mass for a
    /// snake nobody steered.
    /// </summary>
    [Fact]
    public void InputCommand_Default_IsNeutral()
    {
        InputCommand command = default;

        Assert.Equal(0f, command.Heading);
        Assert.False(command.Boost);
        Assert.False(command.Cast);
    }

    /// <summary>
    /// The tick path allocates nothing (ARCH §5). A reference field here would
    /// put the whole command buffer on the GC's radar and break the span
    /// contract in ARCH §4.
    /// </summary>
    [Fact]
    public void InputCommand_ForTheTickPath_ContainsNoReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<InputCommand>());
    }

    /// <summary>
    /// Immutability is what makes it safe to pass around by value and by `in`
    /// without defensive copies — the roadmap specifies a readonly struct.
    /// </summary>
    [Fact]
    public void InputCommand_IsAReadonlyValueType()
    {
        Assert.True(typeof(InputCommand).IsValueType);
        Assert.True(typeof(InputCommand).IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
    }

    /// <summary>
    /// Player, bot and future network peer are indistinguishable to `World`
    /// (ARCH, "every actor emits an InputCommand"), so two commands carrying the
    /// same intent must compare equal regardless of who produced them.
    /// </summary>
    [Fact]
    public void InputCommand_WithSameValues_CompareEqual()
    {
        InputCommand left = new(Heading: 0.5f, Boost: true, Cast: true);
        InputCommand right = new(Heading: 0.5f, Boost: true, Cast: true);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void InputCommand_WithDifferentHeading_DoesNotCompareEqual()
    {
        InputCommand left = new(Heading: 0.5f, Boost: false, Cast: false);
        InputCommand right = new(Heading: 0.6f, Boost: false, Cast: false);

        Assert.NotEqual(left, right);
    }

    /// <summary>
    /// Spec §3: the joystick emits a target heading, direction only, and holds
    /// the last direction on release. The struct is a dumb carrier — it does not
    /// wrap, clamp or normalise, because the turn-rate cap in §4.2 is what
    /// resolves a heading, and normalising here would hide a bad agent.
    /// </summary>
    [Theory]
    [InlineData(-7.5f)]
    [InlineData(0f)]
    [InlineData(12.75f)]
    public void InputCommand_AtAnyHeading_StoresItVerbatim(float heading)
    {
        InputCommand command = new(heading, Boost: false, Cast: false);

        Assert.Equal(heading, command.Heading);
    }
}
