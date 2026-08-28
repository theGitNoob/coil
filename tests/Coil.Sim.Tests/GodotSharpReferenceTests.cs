using Godot;
using Xunit;

namespace Coil.Sim.Tests;

/// <summary>
/// Proves the harness wiring and the assumption the solution layout rests on:
/// GodotSharp's <see cref="Vector2"/> and <see cref="Mathf"/> are pure managed
/// value types, usable from a plain net8.0 assembly with no engine running
/// (ARCH §2, M0-01). If this suite ever needs an engine to pass, the layering
/// has been broken.
/// </summary>
public sealed class GodotSharpReferenceTests
{
    // Headings are radians counter-clockwise from +X, so cos/sin map straight
    // onto the unit circle. Tolerance is float epsilon scaled for trig error.
    private const float Tolerance = 1e-6f;

    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(Mathf.Pi / 2f, 0f, 1f)]
    [InlineData(Mathf.Pi, -1f, 0f)]
    [InlineData(3f * Mathf.Pi / 2f, 0f, -1f)]
    public void Direction_AtCardinalHeading_PointsAlongThatAxis(float heading, float expectedX, float expectedY)
    {
        Vector2 direction = GodotSharpReference.Direction(heading);

        Assert.Equal(expectedX, direction.X, Tolerance);
        Assert.Equal(expectedY, direction.Y, Tolerance);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.75f)]
    [InlineData(-2.5f)]
    [InlineData(12.3f)]
    public void Direction_AtAnyHeading_ReturnsUnitVector(float heading)
    {
        Vector2 direction = GodotSharpReference.Direction(heading);

        Assert.Equal(1f, direction.Length(), Tolerance);
    }
}
