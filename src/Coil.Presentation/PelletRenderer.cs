using System;
using Coil.Sim;
using Godot;

namespace Coil.Presentation;

/// <summary>
/// Draws every pellet — spec §11.5, ARCH §6, M1-02.
///
/// One <see cref="MultiMeshInstance2D"/> with an instance per pool slot, so
/// all ~2,600 pellets are one draw call and the instance count never changes.
/// A dead slot is written as a zero-scale transform rather than compacted away:
/// slots die out of order, and compacting would mean rewriting indices the
/// simulation owns.
///
/// A plain class rather than a Node (D-18); the shell is
/// <c>PelletRendererNode</c>. It reads <see cref="Food"/>'s arrays directly —
/// pellet positions never cross to GDScript (ARCH §3).
///
/// Drawn at the current tick, not interpolated: pellets do not move until the
/// magnet pull (M1-09), which is where interpolation becomes worth having.
/// The art is <see cref="PlaceholderDisc"/>, tinted, until A-02.
/// </summary>
// CA1001: the mesh belongs to its node, and the node to the scene tree once
// added under root; the tree frees both, so disposing here would free twice.
#pragma warning disable CA1001
public sealed class PelletRenderer
#pragma warning restore CA1001
{
    /// <summary>A <c>Transform2D</c> as MultiMesh packs it — see <see cref="SnakeRenderer"/>.</summary>
    private const int FloatsPerInstance = 8;

    private static readonly Color PelletTint = new(0.98f, 0.85f, 0.55f);

    private readonly Food _food;
    private readonly float _drawRadius;
    private readonly float _pelletMass;

    private readonly MultiMesh _mesh;

    /// <summary>Every slot's transform, rewritten on change and handed to the engine in one copy.</summary>
    private readonly float[] _buffer;

    // The Food.Version the buffer was last written at.
    private int _drawnVersion;

    public PelletRenderer(Node2D root, World world, SimConfig config)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(config);

        _food = world.Food;
        _drawRadius = config.PelletDrawRadius;
        _pelletMass = config.PelletMass;
        _buffer = new float[_food.Capacity * FloatsPerInstance];
        _drawnVersion = unchecked(_food.Version - 1);   // so the first Redraw writes

        // Changing TransformFormat or Mesh clears the buffer, so InstanceCount
        // goes last — and is never set again, since setting it reallocates.
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = _food.Capacity,
        };

        // Fixed culling bounds: every pellet lies inside the arena, the
        // spawner's disk and the bodies that drop corpse and boost pellets
        // alike. Padded by the largest pellet the economy makes.
        float extent = config.ArenaRadius + DrawRadius(Math.Max(config.CorpseChunkMass, config.BoostDropMass));
        _mesh.CustomAabb = new Aabb(new Vector3(-extent, -extent, 0f), new Vector3(2f * extent, 2f * extent, 0f));

        var node = new MultiMeshInstance2D
        {
            Name = "Pellets",
            Multimesh = _mesh,
            Texture = PlaceholderDisc.Create(),
            Modulate = PelletTint,
        };

        root.AddChild(node);
    }

    /// <summary>Instances in the one MultiMesh: the pellet pool size, fixed for the match.</summary>
    public int InstanceCount => _mesh.InstanceCount;

    /// <summary>
    /// Rewrites every slot from the pellet store if it changed since the last
    /// call. Called once per frame; between eats the field is static, and the
    /// rewrite costs ~1 ms on the handset (PERF_LOG M1-02).
    /// </summary>
    public void Redraw()
    {
        if (_food.Version == _drawnVersion)
        {
            return;
        }

        _drawnVersion = _food.Version;

        // Raw spans and plain float math over all 3,000 slots: on the handset
        // this loop is 0.80 ms, against 1.23 ms through the per-index
        // accessors, which its runtime does not inline (M0-24, M1-02).
        ReadOnlySpan<bool> alive = _food.Alive;
        ReadOnlySpan<float> masses = _food.Masses;
        ReadOnlySpan<Vector2> positions = _food.Positions;
        Span<float> buffer = _buffer;
        float diameterPerRootMass = 2f * _drawRadius / MathF.Sqrt(_pelletMass);

        for (int i = 0; i < alive.Length; i++)
        {
            int at = i * FloatsPerInstance;

            if (!alive[i])
            {
                buffer.Slice(at, FloatsPerInstance).Clear();
                continue;
            }

            // §11.5, as DrawRadius below. The quad is unit-sized, so the
            // instance scale is the diameter.
            float diameter = diameterPerRootMass * MathF.Sqrt(masses[i]);

            buffer[at] = diameter;
            buffer[at + 1] = 0f;
            buffer[at + 2] = 0f;
            buffer[at + 3] = positions[i].X;
            buffer[at + 4] = 0f;
            buffer[at + 5] = diameter;
            buffer[at + 6] = 0f;
            buffer[at + 7] = positions[i].Y;
        }

        // The one crossing into the engine for every pellet.
        _mesh.Buffer = _buffer;
    }

    /// <summary>§11.5: area proportional to mass carried.</summary>
    private float DrawRadius(float mass) => _drawRadius * MathF.Sqrt(mass / _pelletMass);
}
