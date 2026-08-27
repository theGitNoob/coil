# COIL

Snake-arena game for Android. Godot 4.7 (.NET), hybrid C# / GDScript, offline-first with bots.

| Document | What it holds |
|---|---|
| `docs/GAME_SPEC.md` | What the game is. Every rule and number. |
| `docs/ROADMAP.md` | The 102 tasks, in order, with gates. |
| `docs/ARCHITECTURE.md` | Layering, tick contract, performance rules. |
| `docs/CONVENTIONS.md` | How code is written here. |
| `docs/DECISIONS.md` | Why each choice was made, and what would reopen it. |

## The layer rule

```
Coil.Sim  ←  Coil.Agents  ←  Coil.Presentation  ←  ui/ (GDScript)
```

Dependencies point one way. **`Coil.Sim` never references a Godot node type, the scene tree, input, resources, or files** — it is a pure `(state, commands, dt) → state`. Enforced by NetArchTest in `tests/Coil.Sim.Tests`. If a change needs to break this, the design is wrong.

## Language split

- **C#** — simulation, bot agents, and the bulk renderers that write MultiMesh buffers.
- **GDScript** — UI, menus, VFX orchestration, camera glue, player input.
- **Boundary rule:** cross between them a bounded number of times per frame, **never per-entity-per-tick**. There are three sanctioned crossings; see `ARCHITECTURE.md` §3. Bulk data (paths, pellets, transforms) never crosses.

## Tick loop

60 Hz fixed, 6 ms budget, 34 snakes. Inside `Tick` and anything it calls: **no allocation, no LINQ, no exceptions, no dictionaries.** Preallocate at construction, index with `for`, pool everything. Determinism is required — seeded PRNG in world state, no clock reads.

## Working on a task

One task ID = one branch = one PR.

```bash
git checkout -b m1-06-body-insertion
dotnet test                      # xUnit + architecture rules, <10s, no engine
./tools/deploy.sh                # build, install, launch on the device   [after M0-03]
git commit -m "feat(sim): insert body capsules into the spatial hash (M1-06)"
```

Test-first for anything in `Coil.Sim`. Presentation is verified on the device, by eye.

Definition of done is seven items in `CONVENTIONS.md` §6 — all of them, every task. The one people skip: **it has to run on the phone**, not just in the editor.

## Never

- Never create a Node per body segment. MultiMesh, always.
- Never use Godot physics, `Area2D`, or signals inside the simulation. One spatial hash, `SimEvent` structs.
- Never put a tunable number in code. It goes in `data/balance.tres` → `SimConfig` → the spec's Appendix A, in the same commit.
- Never widen a task while you're in it. Open a new task ID.
- Never mark a task done on a "should work". Measure it, or run it on the device.

## Project skills

Three project-local skills wrap this workflow — use them rather than reconstructing it by hand:

| Skill | Use it for |
|---|---|
| `/coil-status` | Where the build stands. Derived from git + the roadmap, never from memory. |
| `/coil-next` | Run the next task end to end: orient, branch, test-first, verify, commit, PR. |
| `/coil-review` | Review a task against its spec section, the architecture rules and the DoD. |

Completion is derived from git: **a task is done when a commit on `main` carries its ID in parentheses.** There is no checklist file to drift. That makes the ID in the commit subject load-bearing — omit it and the task is invisible to `tools/status.py`.

## Current state

**Pre-M0.** No code yet. The next task is `M0-01` in `docs/ROADMAP.md`.
