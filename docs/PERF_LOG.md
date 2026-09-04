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
