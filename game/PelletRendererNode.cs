using Coil.Presentation;
using Coil.Sim;
using Godot;

namespace Coil;

/// <summary>
/// The engine shell for <see cref="PelletRenderer"/> — D-18. Like
/// <c>SnakeRendererNode</c>, it is bound once by the arena host and then drives
/// its own <c>_Process</c>; anything that decides how a pellet looks belongs in
/// Coil.Presentation, not here.
///
/// It sits before <c>SnakeRenderer</c> in <c>arena.tscn</c> so pellets draw
/// beneath the snakes.
/// </summary>
public sealed partial class PelletRendererNode : Node2D
{
    private PelletRenderer? _renderer;

    public void Bind(ArenaHost host, SimConfig config)
    {
        _renderer = new PelletRenderer(this, host.World, config);

        // §11.5: "a single MultiMeshInstance2D for all pellets" — this is the
        // count that would betray a node per pellet.
        GD.Print(
            $"COIL pellet renderer\n"
            + $"nodes         {GetChildCount()} (1 expected)\n"
            + $"instances     {_renderer.InstanceCount} preallocated, {host.World.Food.Count} live");
    }

    public override void _Process(double delta) => _renderer?.Redraw();
}
