# Performance log

Every measurement that has been *taken*, not estimated. One entry per task that
moved a number. Desktop figures are a floor, not a verdict — the budgets in
GAME_SPEC §11.5 are defined on the floor device, and only an on-device entry can
retire a risk.

Budget: **6 ms per tick**, 60 Hz, 34 snakes (CLAUDE.md, §11.5).

| Task | What | Result | Where |
|---|---|---|---|
| M0-03 | .NET build boots on the handset | .NET 8.0.30, Arm64, mobile renderer | SM-A155M, Android 36 |
| M0-11 | `World.Tick`, movement only — 34 snakes, 200k ticks | **0.0066 ms/tick** (6.58 µs), 0 B allocated | Desktop, AMD Ryzen 7 7735HS with Radeon Graphics |
| M0-12 | 200 ms stall recovery, catch-up cap | **5 ticks** in the recovery frame, no cascade | SM-A155M, Android 36 |
| M0-13 | Draw calls for one 12-segment snake | **2** — one body MultiMesh, one head sprite | SM-A155M, Android 36 |
| M0-14 | Snapshot capture added to `ArenaHost.Tick` — 34 snakes, 20k ticks | `World.Tick` 14.98 µs → `ArenaHost.Tick` **18.52 µs** (+3.37 µs capture), 0 B allocated | SM-A155M, Android 36 |
| M0-15 | Camera follow, focus head's on-screen motion over 600 frames at 90 Hz | **≤ 0.003 px/frame** (sd 0.0003), zoom 0.9917 at mass 10 | SM-A155M, Android 36 |
| M0-19 | ⚠ Perf spike, 34 snakes, render plan as of M0-15 | **A gameplay-like: 89.9 fps** (0.22% frames > 16.7 ms). **B worst case, 34 × MaxMass on screen: 39.8 fps**, GPU-bound by overdraw | SM-A155M (Helio G99, Mali-G57 MC2), Android 36 |
| M0-24 | Renderer segment loop over the raw ring, spike scenarios | A: **2.76 → 0.75 ms**/frame; B: **11.42 → 3.32 ms**/frame; positions within 0.0004 u of before | SM-A155M, Android 36 |

## M0-11 · `World.Tick` baseline

First code in the tick loop, so this is a baseline rather than a delta.

Movement only: heading rotation with the omega clamp, head integration, and the
path push. No collision, no pellets, no rendering — the three things that will
actually consume the budget. Read it as "the movement math is free", not as
"the budget is safe".

- **0.0066 ms of the 6 ms budget**, measured over 200,000 ticks after a 5,000
  tick warm-up, 34 snakes all live and steering.
- **Zero bytes allocated**, asserted in `WorldTests` with
  `GC.GetAllocatedBytesForCurrentThread` rather than measured by eye.

Not yet measured on the device: nothing calls `Tick` on the handset until
`ArenaHost` lands in M0-12. **M0-19 is the gate-critical on-device spike** and
is what decides whether the render plan survives.

## M0-12 · `ArenaHost`, stall recovery

The first task to run `World.Tick` on the handset. The cadence is the engine's
physics loop, not our own accumulator: `project.godot` carries Appendix A's
`TICK_RATE` and `MAX_CATCHUP_TICKS`, and `EngineTickSettingsTests` fails if the
two drift apart — Godot defaults the cap to 8.

Measured by inducing a real 200 ms block inside `_Process` (a temporary probe,
removed before commit) and reading logcat on the device:

```
COIL stall probe: blocking 200 ms
COIL catch-up: 5 ticks in one frame (84 ms), total 191
COIL catch-up: 2 ticks in one frame (23 ms), total 193
```

A 200 ms stall owes 12 ticks at 60 Hz. The recovery frame ran **exactly 5** — the
cap — and dropped the remaining 7 rather than carrying them. The frames after it
are ordinary 2-tick frames on a phone occasionally taking 25 ms, not a cascade.
Same behaviour headless on desktop.

Still no rendering: one snake ticks and nothing is drawn until `SnakeRenderer`
in M0-13. **M0-19 remains the gate-critical on-device verdict.**

## M0-13 · `SnakeRenderer`, draw calls

The first task that draws. Spec §11.5 claims a snake body costs one draw call
however many segments it has, and §11.6 budgets 80 draw calls for the frame.
This measures the claim rather than restating it.

Measured with a temporary probe (removed before commit) reading
`Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME` on the handset, on three frames
chosen to land while the snake is still in view — with no camera follow yet it
leaves the frame after about 175 frames:

```
COIL probe frame  60 (drawn):  segments 12, draw calls 2, objects 2, children 68
COIL probe frame  90 (hidden): segments 12, draw calls 0, objects 0, children 68
COIL probe frame 120 (drawn):  segments 12, draw calls 2, objects 2, children 68
```

The delta is the number that matters. **12 body segments cost one draw call**,
plus one for the head sprite; hiding the snake returns the frame to zero. A
`Sprite2D` per segment would have read 13.

- **68 nodes for 34 slots** — one `MultiMeshInstance2D` and one `Sprite2D` each,
  and no child per segment. The boot print reports this permanently, because the
  regression this project cares about would show up here as thousands.
- **543 instances preallocated per body**, the longest body §4.1 allows at
  `MaxMass`. `MultiMesh.InstanceCount` reallocates its buffer, so it is set once
  at construction and only `VisibleInstanceCount` moves per frame — the same
  reason §4.1 sizes the path ring to `MaxMass` rather than growing it.
- The per-frame write is one `MultiMesh.Buffer` assignment per snake from a
  reused scratch array. It copies the whole 543-instance buffer even when 12
  instances are visible: **34 × 543 × 8 floats ≈ 590 KB per frame** at full
  occupancy. Harmless with one snake and unmeasured with 34 — **this is one of
  the costs M0-19 exists to find**, and the alternative (one
  `SetInstanceTransform2D` per segment) is the per-entity marshalling ARCH §3
  rules out.

Geometry checked against §4.1 from the device screenshot rather than by eye
(1280×720 design viewport, `expand` aspect on a 2340×1080 panel, so 1.5 px/u):

| | Measured | §4.1 at mass 10 | |
|---|---|---|---|
| Body thickness | 28.00 u | 28.68 u | disc edge falloff, sub-pixel |
| Body span | 100.00 u | 94.68 u | +5.32 u of head lead, < `PATH_STEP` |
| Vertical centre | 539.5 px | 540 px | world origin is the centre of the view |

The span excess is the path ring's un-pushed carry: the head sprite is drawn at
`HeadOf`, the newest body disc at the newest path point, and those differ by up
to `PATH_STEP = 6.0 u`. It is the intended behaviour showing up in a
measurement, not drift.

Still no tick-loop delta: M0-13 adds nothing to `World.Tick`. **M0-19 remains
the gate-critical on-device verdict**, and it now has a renderer to spike with.

## M0-14 · Render interpolation

The tick loop changed: `ArenaHost.Tick` now copies every slot's head, heading,
radius, path lead and push count before the step, so the renderers have a
previous state to interpolate from. Measured on the handset with a temporary
probe (removed before commit), 34 snakes live and steering, 20,000 ticks after a
600-tick warm-up:

| | µs per tick |
|---|---|
| `World.Tick` alone | 14.98 |
| `ArenaHost.Tick` (capture + `World.Tick`) | 18.52 |
| Capture alone | 3.37 |

**+3.4 µs of the 6 ms budget, 0 B allocated over 1,000 ticks.** The device is
about 2.3× slower than the M0-11 desktop floor for the same `World.Tick`.

### Smoothness, measured rather than eyeballed

The same probe drove the placeholder snake in a circle and recorded, every
frame, how far the drawn head and the drawn 9th body segment moved. Smooth
motion moves the same distance every frame, so the number to read is the
coefficient of variation (sd ÷ mean) of that per-frame displacement. *Raw tick*
is the same measurement taken on the uninterpolated simulation state, which is
what M0-13 drew.

Panel at 90 Hz, the SM-A155M's maximum. Power saving must be off: it caps the
panel at 60 Hz.

| Phase | fps | Head, interpolated | Head, raw tick | Segment 8, interpolated | Spacing error |
|---|---|---|---|---|---|
| Base speed, uncapped | 90.1 | 2.439 u/frame, **cv 0.009** | 2.440, cv 0.709 | cv 0.009 | ≤ 0.0028 u |
| Boost, uncapped | 90.1 | 4.213 u/frame, **cv 0.010** | 4.205, cv 0.711 | cv 0.010 | ≤ 0.0013 u |
| Base speed, `MaxFps = 30` | 30.1 | 7.335 u/frame, cv 0.002 | 7.333, cv 0.000 | cv 0.002 | ≤ 0.0028 u |
| Boost, `MaxFps = 30` | 30.0 | 12.649 u/frame, cv 0.004 | 12.665, cv 0.000 | cv 0.004 | ≤ 0.0013 u |

- **At 90 Hz the raw state judders**: 60 ticks shared over 90 frames means a
  frame moves 0, 1 or 2 ticks' worth (cv 0.71). Interpolated, the head *and the
  body* advance the same distance every frame (cv 0.01).
- **Capped to 30 it stays correct.** The engine runs two ticks a frame, the
  snapshot is taken per tick rather than per frame, and the drawn head never
  trails the simulated one by more than one tick's travel (max 0.39 u at base
  speed, 2.90 u at boost, against 3.67 u and 6.33 u per tick).
- **Body spacing holds while drawn**: consecutive instances stay within
  0.003 u of `PATH_STEP`, through boost ticks that push two path points at once.
  Segments are laid at fixed arc-length steps behind the interpolated head, not
  on the raw path points; pinned to the points, the body would advance a whole
  step every time one was pushed.
- 120 Hz could not be measured: this panel stops at 90 Hz. Nothing in the
  interpolation depends on the frame rate beyond the engine's fraction, but the
  claim is still unverified above 90.

### Seen in passing: redraw cost, for M0-19

The probe also timed `SnakeRenderer.Redraw` at **160–370 µs per frame for one
live snake** (the higher figures are in the 30 fps phases, where the CPU governor
clocks down). That frame cost belongs to M0-13's renderer more than to this
task. Its most likely components are the 543-instance `MultiMesh.Buffer` copy
noted under M0-13 and the 66 `Visible` writes per frame to empty slots.
Neither was isolated here. Scaled naively to 34 snakes it would be several
milliseconds of the 16.7 ms frame. **M0-19 should measure it directly**; it is
not part of the tick budget above.

## M0-15 · Camera

No tick-loop change: the camera and the culling-bounds fix both run per frame.

A temporary probe in `game/camera.gd` (removed before commit) recorded the focus
head's position in screen space, `(head - camera.position) * zoom`, every frame
for 600 frames after a 2 s settle, with the placeholder snake driving straight
at base speed:

```
COIL probe camera: head on screen (-14.65, 0.0) px(design), per-frame screen jitter mean 0.0002 sd 0.0003 max 0.0026, zoom 0.9917, fps 90.0
```

- The head holds still on screen to **0.003 design px per frame**. That holds at
  90 Hz only because the camera follows the *interpolated* head (M0-14);
  following the raw tick state would bring back the 0/1/2-tick judder.
- The −14.65 px offset is the 40 u look-ahead minus the spring's steady lag at
  220 u/s, scaled by zoom.
- Zoom 0.9917 = `(14.34/14)^-0.35`, the §7.4 curve at mass 10.

Found on the way: body MultiMeshes kept the culling bounds of their first draw,
so once the camera followed the snake away from the origin, the body was culled
and only the head was drawn. `SnakeRenderer` now sets `CustomAabb` per body each
frame. Off-screen bodies are therefore genuinely culled now, which M0-19's
draw-call count should reflect.

## M0-19 · ⚠ Perf spike — the render-plan verdict

`tools/spike_render.tscn` (throwaway) drives the real `World`, `ArenaHost` and
`SnakeRenderer` with synthetic commands: every snake circles at a fixed rate,
so the paths are real path rings, with no AI and no collision. Path rings are
pre-filled to full body length before measuring. Run on the handset at its
90 Hz panel, 5 s settle, then 30 s measured per scenario.

The SM-A155M is a MediaTek Helio G99 with a Mali-G57 MC2. It sits around the
spec's floor tier (§11.6: Snapdragon 6-series, 2021). Its GPU is the weaker
part.

| | A — gameplay-like | B — worst case |
|---|---|---|
| Setup | masses log-spaced 10..2000, spread over ~1,600 u, zoom 0.66 | all 34 at MaxMass (543 segments each), all on screen, overlapping, zoom 0.55 |
| fps | **89.9** (per 10 s: 90.1, 89.6) | **39.8** (39.9, 39.7) |
| frame ms | avg 11.12, p50 11.09, p95 12.69, p99 15.22, max 62.94 | avg 25.14, p50 22.44, p95 34.40, p99 37.64 |
| frames > 16.7 ms | 6 of 2,698 (0.22%) | 1,076 of 1,194 (90%) |
| tick ms (34 snakes) | avg 0.074, max 0.673 | avg 0.068, max 0.366 |
| `SnakeRenderer.Redraw` | avg 3.16 ms | avg 10.42 ms |
| GPU per frame | 2.31 ms | **22.28 ms** |
| draw calls | avg 14, max 18 | 35 |

**Verdict: the gameplay-like case holds 90 fps with room to spare; the worst
case does not hold 60.** The tick costs nothing at this stage (0.07 ms of the
6 ms budget). Both failures in B come from rendering.

### Where B's frame goes

A second run with the renderer temporarily instrumented and a knob to space
body discs further apart along the path (stride = path steps per disc; 15 s per
run). The knob and timers were removed before commit.

| Run | fps | GPU ms | Redraw ms | segment loop | `Buffer` set | other node writes |
|---|---|---|---|---|---|---|
| A, stride 1 | 89.8 | 2.24 | 3.46 | 2.76 | 0.72 | 1.05 |
| B, stride 1 | 39.9 | 22.24 | 10.20 | **11.42** | 0.75 | 1.06 |
| B, stride 2 | 61.2 | 11.23 | 6.07 | 6.26 | 0.72 | 1.08 |
| B, stride 3 | 70.3 | 9.31 | 4.51 | 4.22 | 0.68 | 1.02 |

- **GPU: overdraw.** 34 × 543 translucent discs, each ~60 u across, sitting
  6 u apart means every body pixel is covered by about ten discs of the same
  snake. Spacing them 2× apart halves the GPU time; 3× takes B to 70 fps.
- **CPU: the per-segment loop, not the buffer copy.** M0-13 suspected the
  543-instance `Buffer` copy. It costs 0.7 ms per frame for all 34 snakes. The
  loop that computes segment positions costs **~0.6 µs per segment**, which is
  an order of magnitude more than its arithmetic should need. It makes two
  cross-assembly `PathPointOf` calls and several Godot `Vector2` method calls
  per segment, which this runtime does not appear to inline.
- Node property writes (visibility, instance count, bounds, head transform)
  are a flat ~1 ms for 68 nodes.

What to do about B is a render-plan decision, recorded in the M0-19 PR rather
than settled here.

## M0-24 · Segment loop over the raw ring

First of the two M0-19 fixes. `SnakeRenderer` now reads each snake's path ring
as a `ReadOnlySpan<Vector2>` (`World.PathRingOf`) and computes segment
positions and culling bounds with plain float math. Before, it made two
cross-assembly `PathPointOf` calls and several Godot `Vector2` method calls per
segment, none of which the handset's runtime inlined.

Measured with the M0-19 spike and a temporary probe (removed before commit),
15 s per scenario. The probe timed the segment loop alone. Every 10th frame,
outside the timed region, it recomputed every segment the old way and compared:

| | segment loop, M0-19 | segment loop, M0-24 | compared | max difference |
|---|---|---|---|---|
| A — gameplay-like | 2.76 ms | **0.75 ms** | 688,682 segments | 0.00037 u |
| B — 34 × MaxMass | 11.42 ms | **3.32 ms** (≤ ⅓ required) | 2,162,922 segments | 0.00037 u |

The difference is float rounding: `ages` is now a multiply by `1 / PATH_STEP`
instead of a divide. B still runs at ~40 fps because it is GPU-bound (22 ms per
frame), which is M0-23's job. Tick unchanged.

