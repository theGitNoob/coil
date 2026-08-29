using System.Runtime.InteropServices;
using Godot;

namespace Coil;

/// <summary>
/// The gate probe for M0-03: the smallest scene that proves a .NET build runs
/// on the handset rather than merely exporting. Prints the runtime, the process
/// architecture and the renderer to stdout — which is what
/// <c>tools/deploy.sh</c> filters out of logcat — and draws the same lines on
/// screen, so the claim can be read off the device instead of inferred (D-07).
/// </summary>
/// <remarks>
/// Throwaway, like the spike in M0-19. `run/main_scene` points here only until
/// M0-12 lands the real arena host; delete this file and `game/boot.tscn` in
/// that task.
/// </remarks>
public sealed partial class BootProbe : Node2D
{
    public override void _Ready()
    {
        string report = Report();

        // "COIL" is the marker tools/deploy.sh greps for. Android routes
        // GD.Print to logcat under the `godot` tag, where it sits among a few
        // hundred lines of engine startup.
        GD.Print($"COIL boot probe\n{report}");

        GetNode<Label>("%Readout").Text = report;
    }

    private static string Report()
    {
        string engine = (string)Engine.GetVersionInfo()["string"];
        string renderer = (string)ProjectSettings.GetSetting(
            "rendering/renderer/rendering_method");

        return $"""
            runtime   {RuntimeInformation.FrameworkDescription}
            arch      {RuntimeInformation.ProcessArchitecture} on {RuntimeInformation.OSArchitecture}
            engine    {engine}
            renderer  {renderer}
            os        {OS.GetName()} {OS.GetVersion()}
            """;
    }
}
