using Coil.Presentation;
using Coil.Sim;
using Godot;

namespace Coil;

/// <summary>
/// The engine shell for <see cref="SnakeRenderer"/> — D-18.
///
/// Godot registers C# script types only from the main project assembly, so the
/// Node lives here and every line that decides what a snake looks like lives in
/// Coil.Presentation. Nothing mechanical enforces that; if a formula appears in
/// this file it is in the wrong file.
///
/// It drives its own <c>_Process</c> rather than being called by
/// <c>ArenaHostNode</c>. The simulation has already advanced by then — Godot
/// runs the physics step before the frame, and parents before children — and it
/// keeps the host from growing a call for every renderer M1 adds.
///
/// <c>arena.tscn</c> also carries a bare <c>Camera2D</c>, which is what makes
/// world origin — the arena centre — the centre of the view rather than its
/// top-left corner. It is deliberately unconfigured: the damped follow,
/// look-ahead and zoom curve in §7.4 are M0-15, in GDScript. Until then the
/// placeholder snake drives out of frame after about three seconds, because
/// nothing follows it and nothing steers it yet. This note lives here rather
/// than in the scene file because Godot discards <c>.tscn</c> comments the
/// first time the editor saves.
/// </summary>
public sealed partial class SnakeRendererNode : Node2D
{
    private SnakeRenderer? _renderer;

    /// <summary>
    /// Hands the renderer the world it draws. Called once by the arena host,
    /// which owns the world: a renderer with nothing to read is not a valid
    /// state, so this is a hand-off rather than something _Ready can do alone.
    /// </summary>
    public void Bind(World world, SimConfig config)
    {
        _renderer = new SnakeRenderer(this, world, config);

        // The claim in spec §11.5 is "no Sprite2D per segment, ever", and this
        // is the number that would betray it: one body plus one head per slot,
        // never a child per segment. A regression shows up here as thousands.
        GD.Print(
            $"COIL snake renderer\n"
            + $"nodes         {GetChildCount()} for {world.Capacity} slots "
            + $"({_renderer.NodeCount} expected: 1 body + 1 head each)\n"
            + $"instances     {_renderer.MaxSegments} preallocated per body");
    }

    public override void _Process(double delta) => _renderer?.Redraw();
}
