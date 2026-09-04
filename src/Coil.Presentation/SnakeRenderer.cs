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

    public SnakeRenderer(Node2D root, World world, SimConfig config)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(config);

        _world = world;
        _config = config;

        // Setting InstanceCount reallocates the MultiMesh buffer, so it is
        // sized once here for the longest body a snake can have and never
        // touched again — the same reason §4.1 sizes the path ring to MaxMass
        // rather than growing it. Only VisibleInstanceCount moves per frame.
        _maxSegments = SegmentCount(_config.MaxMass);
        _buffer = new float[_maxSegments * FloatsPerInstance];

        ImageTexture disc = CreateDiscTexture();
        var quad = new QuadMesh { Size = Vector2.One };

        int slots = world.Capacity;
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
    /// arrays. Called once per frame, not once per tick: the simulation state
    /// it reads is whatever the last fixed step left behind.
    /// </summary>
    public void Redraw()
    {
        for (int i = 0; i < _bodyNodes.Length; i++)
        {
            if (!_world.IsAlive(i))
            {
                _bodyNodes[i].Visible = false;
                _headNodes[i].Visible = false;
                continue;
            }

            DrawSnake(i);
        }
    }

    private void DrawSnake(int index)
    {
        // §4.1: the body is sampled from the path, so it is as long as the
        // formula asks for or as long as the path has grown, whichever is
        // shorter. Right after a spawn the ring holds one point.
        int segments = Math.Min(SegmentCount(_world.MassOf(index)), _world.PathCountOf(index));
        segments = Math.Min(segments, _maxSegments);

        // §4.1: radius comes from mass. The disc is authored at unit size, so
        // the instance scale is the diameter in world units.
        float diameter = _world.RadiusOf(index) * 2f;

        for (int age = 0; age < segments; age++)
        {
            Vector2 point = _world.PathPointOf(index, age);
            int at = age * FloatsPerInstance;

            _buffer[at] = diameter;
            _buffer[at + 1] = 0f;
            _buffer[at + 2] = 0f;
            _buffer[at + 3] = point.X;
            _buffer[at + 4] = 0f;
            _buffer[at + 5] = diameter;
            _buffer[at + 6] = 0f;
            _buffer[at + 7] = point.Y;
        }

        // The one crossing into the engine for this snake's whole body.
        _bodyMeshes[index].Buffer = _buffer;
        _bodyMeshes[index].VisibleInstanceCount = segments;
        _bodyNodes[index].Visible = segments > 0;

        Sprite2D head = _headNodes[index];
        head.Position = _world.HeadOf(index);
        head.Rotation = _world.HeadingOf(index);
        head.Scale = Vector2.One * (diameter / DiscTextureSize);
        head.Visible = true;
    }

    /// <summary>
    /// §4.1: <c>length_units = 60 + mass * 1.6</c>,
    /// <c>segment_count = length_units / PATH_STEP</c>.
    /// </summary>
    private int SegmentCount(float mass) =>
        (int)((_config.LengthBase + (mass * _config.LengthPerMass)) / _config.PathStep);

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
