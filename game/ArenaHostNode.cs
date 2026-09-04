using Coil.Presentation;
using Coil.Sim;
using Godot;

namespace Coil;

/// <summary>
/// The engine shell for <see cref="ArenaHost"/> — D-18.
///
/// Godot registers C# script types only from the main project assembly, so the
/// Node lives here and the logic lives in Coil.Presentation. Deliberately thin:
/// anything that looks like a rule of the game belongs on the other side of
/// this boundary, and nothing mechanical will catch it if it drifts in here.
///
/// The tick runs in _PhysicsProcess, which is the engine's fixed 60 Hz loop
/// (§11.3). project.godot caps it at MAX_CATCHUP_TICKS steps per frame, so a
/// long stall drops time instead of spiralling.
/// </summary>
public sealed partial class ArenaHostNode : Node2D
{
    private ArenaHost? _host;
    private SnakeRendererNode? _snakes;
    private int _ticksThisFrame;

    /// <summary>
    /// How far between the previous and current simulation states this frame
    /// sits, for renderers to interpolate by (§11.3, M0-14).
    /// </summary>
#pragma warning disable CA1822 // Deliberately an instance member: renderers and
    // GDScript reach this through the node they already hold, and a static would
    // not be reachable from GDScript at all. The value is global to the frame.
    public float InterpolationFraction => (float)Engine.GetPhysicsInterpolationFraction();
#pragma warning restore CA1822

    public long TickCount => _host?.TickCount ?? 0;

    public override void _Ready()
    {
        SimConfig config = BalanceLoader.Load();
        _host = new ArenaHost(config);
        _host.SpawnPlaceholderSnake();

        // The host owns the world, so it is the only thing that can hand it to
        // a renderer. The renderer drives its own frame from there.
        _snakes = GetNode<SnakeRendererNode>("SnakeRenderer");
        _snakes.Bind(_host.World, config);

        // Read back rather than assumed: these come from Appendix A via
        // project.godot, and a silently-reverted setting is how a 200 ms stall
        // turns into a spiral nobody notices until the device thermally throttles.
        GD.Print(
            $"COIL arena host\n"
            + $"tick rate     {Engine.PhysicsTicksPerSecond} Hz (config {config.TickRate})\n"
            + $"catch-up cap  {ProjectSettings.GetSetting("physics/common/max_physics_steps_per_frame")} "
            + $"(config {config.MaxCatchupTicks})\n"
            + $"snakes        {config.SnakeCount} slots, 1 spawned");
    }

    public override void _PhysicsProcess(double delta)
    {
        _host?.Tick();
        _ticksThisFrame++;
    }

    public override void _Process(double delta)
    {
        // Quiet unless the engine had to catch up, which is the only interesting
        // case: this is what proves the cap is holding rather than spiralling.
        if (_ticksThisFrame > 1)
        {
            GD.Print(
                $"COIL catch-up: {_ticksThisFrame} ticks in one frame "
                + $"({delta * 1000.0:F0} ms), total {TickCount}");
        }

        _ticksThisFrame = 0;
    }
}
