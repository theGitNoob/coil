using System;
using System.Diagnostics;
using Coil.Presentation;
using Coil.Sim;
using Godot;

namespace Coil;

/// <summary>
/// M0-19 perf spike — throwaway by design (roadmap §M0). Answers one question
/// before any content exists: do 34 snakes hold 60 fps on the floor device with
/// the M0 render plan?
///
/// It drives the real <see cref="World"/>, <see cref="ArenaHost"/> and
/// <see cref="SnakeRenderer"/> with synthetic commands — every snake circles at
/// a fixed rate — so the paths are real path rings with no AI and no
/// collision. Two scenarios run back to back and print their numbers:
///
///   A  gameplay-like: masses log-spaced 10..MaxMass, spread over a wide area,
///      zoom 0.66, so some snakes are off-screen and culled.
///   B  worst case: all 34 at MaxMass (the longest body §4.1 allows), all on
///      screen at the minimum zoom of 0.55, overlapping.
///
/// Run it by pointing project.godot's main scene at tools/spike_render.tscn for
/// one deploy. The numbers it printed are in docs/PERF_LOG.md under M0-19.
/// </summary>
public sealed partial class SpikeRenderNode : Node2D
{
    private const float WarmupSeconds = 5f;
    private const float MeasureSeconds = 30f;
    private const int MaxFrames = 8192;

    private readonly float[] _frameMs = new float[MaxFrames];
    private readonly float[] _redrawMs = new float[MaxFrames];

    private SimConfig _config = null!;
    private ArenaHost _host = null!;
    private SnakeRenderer _renderer = null!;
    private Node2D _snakesRoot = null!;
    private Camera2D _camera = null!;
    private Rid _viewport;

    private float[] _turnRates = [];
    private float[] _phases = [];

    private int _scenario;
    private double _clock;
    private ulong _lastUsec;
    private int _frames;
    private int _ticks;
    private double _tickMsSum;
    private double _tickMsMax;
    private double _gpuMsSum;
    private double _cpuRenderMsSum;
    private long _drawCallsSum;
    private int _drawCallsMax;
    private int _windowFrames;
    private double _windowStart;
    private string _windows = "";

    public override void _Ready()
    {
        _config = BalanceLoader.Load();
        _camera = GetNode<Camera2D>("Camera2D");
        _viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
        StartScenario(0);
    }

    private void StartScenario(int scenario)
    {
        _scenario = scenario;
        _snakesRoot?.QueueFree();
        _snakesRoot = new Node2D { Name = $"Snakes{scenario}" };
        AddChild(_snakesRoot);

        _host = new ArenaHost(_config);
        int count = _config.SnakeCount;
        _turnRates = new float[count];
        _phases = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float mass;
            Vector2 centre;
            float circle;

            if (scenario == 0)
            {
                mass = _config.MassStart * MathF.Pow(_config.MaxMass / _config.MassStart, t);
                float angle = i * 2.39996f; // golden angle: an even, deterministic spread
                centre = Vector2.FromAngle(angle) * (300f + (1300f * MathF.Sqrt(t)));
                circle = 450f + (450f * ((i * 7) % 5) / 4f);
            }
            else
            {
                mass = _config.MaxMass;
                centre = new Vector2(-700f + (1400f * ((i % 7) / 6f)), -300f + (600f * ((i / 7) / 4f)));
                circle = 400f;
            }

            _turnRates[i] = _config.SpeedBase / circle * ((i % 2 == 0) ? 1f : -1f);
            _phases[i] = i * 0.7f;
            Vector2 start = centre + (Vector2.FromAngle(_phases[i] - (Mathf.Pi * 0.5f * MathF.Sign(_turnRates[i]))) * circle);
            _host.World.Spawn(i, start, _phases[i], mass);
        }

        // Fill every path ring to full body length before measuring: the
        // longest body is LengthBase + MaxMass * LengthPerMass of arc.
        float longest = _config.LengthBase + (_config.MaxMass * _config.LengthPerMass);
        int warmTicks = (int)(longest / _config.SpeedBase * _config.TickRate) + 60;
        for (int k = 0; k < warmTicks; k++)
        {
            SteerAndTick();
        }

        _renderer = new SnakeRenderer(_snakesRoot, _host, _config);
        _camera.Zoom = Vector2.One * (scenario == 0 ? 0.66f : _config.CameraZoomMin);

        _clock = 0;
        _frames = 0;
        _ticks = 0;
        _tickMsSum = 0;
        _tickMsMax = 0;
        _gpuMsSum = 0;
        _cpuRenderMsSum = 0;
        _drawCallsSum = 0;
        _drawCallsMax = 0;
        _windowFrames = 0;
        _windowStart = WarmupSeconds;
        _windows = "";
        _lastUsec = Time.GetTicksUsec();
    }

    private void SteerAndTick()
    {
        Span<InputCommand> commands = _host.Commands;
        float time = _host.TickCount * _host.FixedDelta;
        for (int i = 0; i < commands.Length; i++)
        {
            commands[i] = new InputCommand(_phases[i] + (_turnRates[i] * time), false, false);
        }

        _host.Tick();
    }

    public override void _PhysicsProcess(double delta)
    {
        long start = Stopwatch.GetTimestamp();
        SteerAndTick();
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        if (_clock >= WarmupSeconds)
        {
            _ticks++;
            _tickMsSum += ms;
            _tickMsMax = Math.Max(_tickMsMax, ms);
        }
    }

    public override void _Process(double delta)
    {
        long start = Stopwatch.GetTimestamp();
        _renderer.Redraw((float)Engine.GetPhysicsInterpolationFraction());
        float redraw = (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ulong now = Time.GetTicksUsec();
        float frame = (now - _lastUsec) / 1000f;
        _lastUsec = now;
        _clock += delta;

        if (_clock < WarmupSeconds)
        {
            return;
        }

        if (_frames < MaxFrames)
        {
            _frameMs[_frames] = frame;
            _redrawMs[_frames] = redraw;
            _frames++;
        }

        _gpuMsSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
        _cpuRenderMsSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
        int drawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _drawCallsSum += drawCalls;
        _drawCallsMax = Math.Max(_drawCallsMax, drawCalls);

        _windowFrames++;
        if (_clock - _windowStart >= 10.0)
        {
            _windows += $" {_windowFrames / (_clock - _windowStart):F1}";
            _windowFrames = 0;
            _windowStart = _clock;
        }

        if (_clock >= WarmupSeconds + MeasureSeconds)
        {
            Report();
            if (_scenario == 0)
            {
                StartScenario(1);
            }
            else
            {
                GD.Print("COIL spike DONE");
                SetProcess(false);
                SetPhysicsProcess(false);
            }
        }
    }

    private void Report()
    {
        int n = _frames;
        float[] frames = _frameMs[..n];
        float[] redraws = _redrawMs[..n];
        float frameAvg = Average(frames);
        Array.Sort(frames);
        Array.Sort(redraws);
        int over60 = 0;
        int over90 = 0;
        for (int i = 0; i < n; i++)
        {
            if (frames[i] > 1000f / 60f)
            {
                over60++;
            }

            if (frames[i] > 1000f / 90f * 1.5f)
            {
                over90++;
            }
        }

        string name = _scenario == 0 ? "A gameplay-like (masses 10..2000, spread, zoom 0.66)" : "B worst case (34 x MaxMass on screen, zoom 0.55)";
        GD.Print(
            $"COIL spike [{name}] {MeasureSeconds:F0} s, {n} frames, refresh {DisplayServer.ScreenGetRefreshRate():F0} Hz\n"
            + $"  fps         {n / MeasureSeconds:F1} avg; per 10 s:{_windows}\n"
            + $"  frame ms    avg {frameAvg:F2}  p50 {Pct(frames, 0.50f):F2}  p95 {Pct(frames, 0.95f):F2}  p99 {Pct(frames, 0.99f):F2}  max {frames[n - 1]:F2}\n"
            + $"  frames > 16.7 ms (missed 60 fps): {over60} ({100f * over60 / n:F2}%), > 16.7 ms counted vs 90 Hz vsync: {over90}\n"
            + $"  tick ms     avg {_tickMsSum / Math.Max(_ticks, 1):F3}  max {_tickMsMax:F3}  ({_ticks} ticks, budget 6 ms)\n"
            + $"  redraw ms   avg {Average(redraws):F3}  p95 {Pct(redraws, 0.95f):F3}  max {redraws[n - 1]:F3}\n"
            + $"  render      cpu {_cpuRenderMsSum / n:F2} ms  gpu {_gpuMsSum / n:F2} ms (avg per frame)\n"
            + $"  draw calls  avg {(double)_drawCallsSum / n:F1}  max {_drawCallsMax}  (budget 80)");
    }

    private static float Average(float[] values)
    {
        double sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }

        return (float)(sum / Math.Max(values.Length, 1));
    }

    private static float Pct(float[] sorted, float p) => sorted[Math.Clamp((int)(p * (sorted.Length - 1)), 0, sorted.Length - 1)];
}
