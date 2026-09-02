using System;
using Coil.Sim;
using Godot;

namespace Coil;

/// <summary>
/// The one place `data/balance.tres` becomes a <see cref="SimConfig"/> —
/// ARCH §4:
///
///   data/balance.tres --BalanceLoader (Presentation)--> SimConfig --> new World(config)
///
/// Loading lives here rather than in `Coil.Sim` because it is file I/O against
/// an engine resource type, and the simulation does neither.
/// </summary>
public static class BalanceLoader
{
    /// <summary>Where the balance resource lives, as a Godot resource path.</summary>
    public const string DefaultPath = "res://data/balance.tres";

    /// <summary>
    /// Loads the balance resource and projects it onto the simulation config.
    /// Throws rather than substituting defaults: a match silently running on
    /// fallback numbers is a bug that looks like bad game feel.
    /// </summary>
    public static SimConfig Load(string path = DefaultPath)
    {
        BalanceData? data = ResourceLoader.Load<BalanceData>(path);

        if (data is null)
        {
            throw new InvalidOperationException($"balance resource not found or not a BalanceData: {path}");
        }

        return data.ToSimConfig();
    }
}
