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
