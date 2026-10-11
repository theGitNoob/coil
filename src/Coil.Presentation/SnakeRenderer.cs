using System;
using Coil.Sim;
using Godot;

namespace Coil.Presentation;

/// <summary>
/// Draws every snake body — spec §11.5, ARCH §6.
///
/// One <see cref="MultiMeshInstance2D"/> per snake, one instance per body
/// segment, so a 543-segment snake is one draw call and there is no node per
/// segment. Heads are separate <see cref="Sprite2D"/>s because they will carry
/// eyes, a class tint and a name label, and there are only ever 34 of them.
///
/// A plain class rather than a Node (D-18): the engine shell is
/// <c>SnakeRendererNode</c> in the game assembly and this does the work. It
/// reads the simulation's arrays directly, which is the whole reason the
/// renderers are C# — per-segment transforms never cross to GDScript (ARCH §3).
///
/// Everything is drawn at the host's interpolated state, not the last tick's
/// (§11.3, M0-14). Segments sit at fixed arc-length steps behind the
/// interpolated head rather than on the raw path points: a point is only pushed
/// every step or so, and a body pinned to them would advance in uneven jumps
/// however smoothly the head moved.
///
/// The art is a placeholder. <c>assets/</c> is empty and the snake atlas is
/// A-02, so this generates a flat disc at construction and tints it. Nothing
/// here survives A-02, and nothing here decides a palette — that is A-01.
/// </summary>
public sealed class SnakeRenderer
{
    /// <summary>
    /// MultiMesh packs a <c>Transform2D</c> as two rows of a three-column
    /// matrix padded to four floats: <c>[x.x, y.x, 0, o.x, x.y, y.y, 0, o.y]</c>.
    /// </summary>
    private const int FloatsPerInstance = 8;

    /// <summary>
    /// Edge of the generated placeholder disc, in pixels. An implementation
    /// detail of the placeholder, not a tunable — A-02 deletes it along with
    /// the rest of the stand-in art.
    /// </summary>
    private const int DiscTextureSize = 64;

    private static readonly Color BodyTint = new(0.36f, 0.78f, 0.62f);
    private static readonly Color HeadTint = new(0.85f, 0.94f, 0.70f);

    private readonly ArenaHost _host;
    private readonly World _world;
    private readonly SimConfig _config;

    private readonly MultiMesh[] _bodyMeshes;
    private readonly MultiMeshInstance2D[] _bodyNodes;
    private readonly Sprite2D[] _headNodes;

    /// <summary>
    /// One scratch buffer, reused for every snake on every frame.
    /// <see cref="MultiMesh.Buffer"/> is a whole-buffer assignment, so writing
    /// through this is one bulk copy per snake rather than one marshalled call
    /// per segment — the roadmap's "in bulk, no marshalling".
    /// </summary>
    private readonly float[] _buffer;

    private readonly int _maxSegments;

    public SnakeRenderer(Node2D root, ArenaHost host, SimConfig config)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(config);

        _host = host;
        _world = host.World;
        _config = config;

        // Setting InstanceCount reallocates the MultiMesh buffer, so it is
        // sized once here for the longest body a snake can have and never
        // touched again — the same reason §4.1 sizes the path ring to MaxMass
        // rather than growing it. Only VisibleInstanceCount moves per frame.
        _maxSegments = SegmentCount(_config.MaxMass, _config.PathStep);
        _buffer = new float[_maxSegments * FloatsPerInstance];

        ImageTexture disc = CreateDiscTexture();
        var quad = new QuadMesh { Size = Vector2.One };

        int slots = _world.Capacity;
        _bodyMeshes = new MultiMesh[slots];
        _bodyNodes = new MultiMeshInstance2D[slots];
        _headNodes = new Sprite2D[slots];

        for (int i = 0; i < slots; i++)
        {
            // Property order matters: changing TransformFormat or Mesh clears
            // the buffer, so InstanceCount is set last.
            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                Mesh = quad,
                InstanceCount = _maxSegments,
            };

            var body = new MultiMeshInstance2D
            {
                Name = $"Body{i}",
                Multimesh = mesh,
                Texture = disc,
                Modulate = BodyTint,
                Visible = false,
            };

            // Above every body rather than just its own: with 34 snakes
            // overlapping, a head behind someone else's tail reads as a bug.
            var head = new Sprite2D
            {
                Name = $"Head{i}",
                Texture = disc,
                Modulate = HeadTint,
                ZIndex = 1,
                Visible = false,
            };

            root.AddChild(body);
            root.AddChild(head);

            _bodyMeshes[i] = mesh;
            _bodyNodes[i] = body;
            _headNodes[i] = head;
        }
    }

    /// <summary>Nodes this renderer owns: one body and one head per slot.</summary>
    public int NodeCount => _bodyNodes.Length + _headNodes.Length;

    /// <summary>Instances preallocated per body, the longest body §4.1 allows.</summary>
    public int MaxSegments => _maxSegments;

    /// <summary>
    /// Rewrites every live snake's transform buffer from the simulation's path
    /// arrays. Called once per frame, not once per tick, at
    /// <paramref name="alpha"/> — the physics interpolation fraction, 0 at the
    /// previous tick and 1 at the current one.
    /// </summary>
    public void Redraw(float alpha)
    {
        for (int i = 0; i < _bodyNodes.Length; i++)
        {
            if (!_world.IsAlive(i))
            {
                _bodyNodes[i].Visible = false;
                _headNodes[i].Visible = false;
                continue;
            }

            DrawSnake(i, alpha);
        }
    }

    private void DrawSnake(int index, float alpha)
    {
        int available = _world.PathCountOf(index);
        float step = _config.PathStep;

        Vector2 headPosition = _host.HeadAt(index, alpha);
        float lead = _host.LeadAt(index, alpha);

        // §4.1: radius comes from mass. The disc is authored at unit size, so
        // the instance scale is the diameter in world units.
        float radius = _host.RadiusAt(index, alpha);
        float diameter = radius * 2f;

        // §4.1: discs every max(PATH_STEP, radius * BODY_DISC_SPACING) of arc
        // length — spaced by radius so a big body is not ten discs deep (M0-23).
        float spacing = Math.Max(step, radius * _config.BodyDiscSpacing);
        int wanted = Math.Min(SegmentCount(_world.MassOf(index), spacing), _maxSegments);

        // Plain float math over the raw ring from here on: this loop runs for
        // every segment of every snake each frame, and the runtime on the
        // handset does not inline PathPointOf or Godot's Vector2 helpers —
        // with them it cost ~0.6 us per segment (M0-19, M0-24).
        ReadOnlySpan<Vector2> ring = _world.PathRingOf(index, out int newest);
        int ringLength = ring.Length;
        Span<float> buffer = _buffer;
        float inverseStep = 1f / step;

        // Culling bounds, grown as segments are written — see the CustomAabb
        // assignment below.
        float minX = headPosition.X;
        float minY = headPosition.Y;
        float maxX = headPosition.X;
        float maxY = headPosition.Y;

        WriteInstance(buffer, 0, diameter, headPosition.X, headPosition.Y);
        int segments = 1;

        for (int k = 1; k < wanted; k++)
        {
            // §4.1: disc k is k spacings of arc length behind the head. The
            // head is `lead` past the newest point and the points are a step
            // apart, so this lands between ages `newer` and `newer + 1`.
            float ages = ((k * spacing) - lead) * inverseStep;
            int newer = (int)ages;

            // The body is as long as the formula asks for or as long as the
            // path has grown, whichever is shorter.
            if (newer + 1 >= available)
            {
                break;
            }

            int a = newest - newer;
            if (a < 0)
            {
                a += ringLength;
            }

            int b = a - 1;
            if (b < 0)
            {
                b += ringLength;
            }

            float t = ages - newer;
            Vector2 near = ring[a];
            Vector2 far = ring[b];
            float x = near.X + ((far.X - near.X) * t);
            float y = near.Y + ((far.Y - near.Y) * t);

            WriteInstance(buffer, k, diameter, x, y);
            segments++;

            if (x < minX)
            {
                minX = x;
            }
            else if (x > maxX)
            {
                maxX = x;
            }

            if (y < minY)
            {
                minY = y;
            }
            else if (y > maxY)
            {
                maxY = y;
            }
        }

        Vector2 min = new(minX, minY);
        Vector2 max = new(maxX, maxY);

        // The one crossing into the engine for this snake's whole body.
        _bodyMeshes[index].Buffer = _buffer;

        // A buffer write does not refresh the bounds Godot culls the body by,
        // so without this the body vanishes once the camera follows it away
        // from where it was first drawn. Padded by the radius: the bounds are
        // of instance origins, the discs extend past them.
        Vector2 pad = new(diameter * 0.5f, diameter * 0.5f);
        Vector2 size = max - min + (pad * 2f);
        _bodyMeshes[index].CustomAabb = new Aabb(
            new Vector3(min.X - pad.X, min.Y - pad.Y, 0f),
            new Vector3(size.X, size.Y, 0f));
        _bodyMeshes[index].VisibleInstanceCount = segments;
        _bodyNodes[index].Visible = segments > 0;

        Sprite2D head = _headNodes[index];
        head.Position = headPosition;
        head.Rotation = _host.HeadingAt(index, alpha);
        head.Scale = Vector2.One * (diameter / DiscTextureSize);
        head.Visible = true;
    }

    /// <summary>One instance's <c>Transform2D</c>: a uniform scale and an origin.</summary>
    private static void WriteInstance(Span<float> buffer, int instance, float diameter, float x, float y)
    {
        int at = instance * FloatsPerInstance;
        buffer[at] = diameter;
        buffer[at + 1] = 0f;
        buffer[at + 2] = 0f;
        buffer[at + 3] = x;
        buffer[at + 4] = 0f;
        buffer[at + 5] = diameter;
        buffer[at + 6] = 0f;
        buffer[at + 7] = y;
    }

    /// <summary>
    /// §4.1: <c>length_units = 60 + mass * 1.6</c>,
    /// <c>segment_count = length_units / disc_spacing</c>. Sized at construction
    /// with the smallest spacing, PATH_STEP, so no later spacing can exceed it.
    /// </summary>
    private int SegmentCount(float mass, float spacing) =>
        (int)((_config.LengthBase + (mass * _config.LengthPerMass)) / spacing);

    /// <summary>
    /// A white disc, generated rather than loaded: the atlas is A-02 and
    /// <c>assets/</c> is deliberately still empty, so this keeps the
    /// placeholder out of the APK and out of A-01/A-02's way.
    /// </summary>
    private static ImageTexture CreateDiscTexture()
    {
        Image image = Image.CreateEmpty(DiscTextureSize, DiscTextureSize, false, Image.Format.Rgba8);

        float centre = DiscTextureSize * 0.5f;
        float radius = centre - 1f;

        for (int y = 0; y < DiscTextureSize; y++)
        {
            for (int x = 0; x < DiscTextureSize; x++)
            {
                float distance = new Vector2(x + 0.5f - centre, y + 0.5f - centre).Length();

                // One pixel of linear falloff, so the placeholder's edge is not
                // a staircase at the zoom the camera will use.
                float alpha = Math.Clamp(radius - distance, 0f, 1f);

                image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
