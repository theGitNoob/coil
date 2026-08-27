using Godot;

namespace Coil.Sim;

/// <summary>
/// Compile-time proof that <see cref="Vector2"/> and <see cref="Mathf"/> resolve
/// from the managed-only surface of GodotSharp with no engine present — the
/// reference the whole solution layout rests on (ARCH §2, task M0-01).
/// </summary>
/// <remarks>
/// Delete this once real simulation types make the same point on their own
/// (M0-10, the snake path buffer). It exists only so the reference is verified
/// before anything is built on it.
/// </remarks>
internal static class GodotSharpReference
{
    /// <summary>Unit vector for a heading in radians.</summary>
    internal static Vector2 Direction(float heading)
    {
        return new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
    }
}
