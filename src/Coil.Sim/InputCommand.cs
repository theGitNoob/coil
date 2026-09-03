namespace Coil.Sim;

/// <summary>
/// The only channel through which any actor affects the world — spec
/// Appendix B, ARCH §3 crossing #1.
///
/// Player, bot and future network peer all emit one of these per tick, which is
/// what makes them indistinguishable to <c>World.Tick</c> and what makes the
/// predict-and-replay netcode in spec §11.3 possible at all.
///
/// A `readonly record struct` rather than a class: commands are handed to the
/// tick as a <c>ReadOnlySpan&lt;InputCommand&gt;</c> (ARCH §4), so the buffer
/// has to be a flat block of value types the GC never sees.
/// </summary>
/// <param name="Heading">
/// Target heading in radians, counter-clockwise from +X. Stored verbatim: the
/// snake rotates toward it at omega_max rather than snapping (spec §4.2), and
/// clamping or wrapping here would hide an agent emitting nonsense. Direction
/// only — the joystick's magnitude is discarded above the dead zone (§3).
/// </param>
/// <param name="Boost">Whether the boost button is held this tick (§3).</param>
/// <param name="Cast">Whether the ability was tapped this tick (§3).</param>
public readonly record struct InputCommand(float Heading, bool Boost, bool Cast);
