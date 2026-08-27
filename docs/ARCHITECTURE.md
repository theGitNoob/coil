# COIL — Architecture

How the code is organised, what may depend on what, and the rules that keep the tick loop fast enough to exist. Style rules live in `CONVENTIONS.md`; the *why* behind each choice lives in `DECISIONS.md`.

---

## 1. The shape

```
        ┌─────────────────────────────────────────────┐
        │  ui/            GDScript                    │   menus, HUD, joystick,
        │                                             │   settings, death card
        ├─────────────────────────────────────────────┤
        │  Coil.Presentation     C#                   │   arena host, MultiMesh
        │                                             │   writers, camera, VFX
        ├──────────────────────┬──────────────────────┤
        │  Coil.Agents    C#   │                      │   bot steering
        ├──────────────────────┘                      │
        │  Coil.Sim       C#                          │   the simulation
        └─────────────────────────────────────────────┘

              dependencies point DOWN, never up
```

**The one rule.** `Coil.Sim` never references a Godot node type, the scene tree, input, resources, files, or anything above it. It is a pure function of `(state, commands, dt) → state`.

That rule is not stylistic. It is what makes three things possible:

1. **Tests run without an engine.** `dotnet test` boots no window, loads no scene, and finishes in under a second.
2. **The online port is a port.** Spec §11.3 works only if the simulation can run headless on a server with bots attached and no renderer present.
3. **The perf escape hatch stays cheap.** Swapping the spatial hash for a faster implementation touches one project.

**Enforcement is by compiler and by test, never by review** — see §9.

---

## 2. Solution layout

```
game/
  Coil.sln
  Coil.csproj                    # Godot.NET.Sdk — the game project, references the three libs
  project.godot
  src/
    Coil.Sim/                    # net8.0 + GodotSharp (math types only)
      World.cs                   # tick(dt, commands) — owns all state
      Snake.cs                   # path ring buffer, mass, integration
      SpatialHash.cs
      Collision.cs
      Food.cs
      Hazards.cs
      Abilities/                 # one file per active, pure functions on state
      SimConfig.cs               # plain POCO — every tunable number
      InputCommand.cs
      SimEvent.cs
    Coil.Agents/                 # net8.0 — references Coil.Sim ONLY
      BotAgent.cs
      Personas.cs
      Steering/
    Coil.Presentation/           # Godot.NET.Sdk — references Sim + Agents + full Godot API
      ArenaHost.cs               # drives the tick, owns interpolation
      SnakeRenderer.cs           # MultiMesh writer
      PelletRenderer.cs          # MultiMesh writer
      BalanceLoader.cs           # balance.tres → SimConfig
  tests/
    Coil.Sim.Tests/              # xUnit — references Coil.Sim, NetArchTest
  ui/                            # GDScript only
  game/                          # GDScript glue, scenes, VFX
  data/                          # .tres resources
  assets/
  tools/                         # deploy.sh, test.sh, release.sh
  docs/
```

**Why `Coil.Sim` is a plain `net8.0` library, not a Godot SDK project.** It takes `GodotSharp` as a pinned `PackageReference` for `Vector2` and `Mathf` — pure managed value types that need no engine. Anything in GodotSharp that calls into native code is banned by the architecture tests. If version-pinning GodotSharp against the engine ever becomes painful, the fallback is a hand-rolled `Vec2` struct in `Coil.Sim` and zero Godot dependency at all. **`M0-01` must verify this reference works before anything is built on it.**

---

## 3. The C# ↔ GDScript boundary

The boundary is cheap to cross a few times per frame and ruinous to cross per-entity-per-tick. Marshalling copies arrays.

**The rule: bounded crossings per frame, never per-entity-per-tick.**

There are exactly three sanctioned crossings, and adding a fourth is a design discussion:

| # | Direction | Payload | Frequency |
|---|---|---|---|
| 1 | GDScript → C# | Player `InputCommand` (three fields) | once per tick |
| 2 | C# → GDScript | `HudSnapshot`: own mass, cooldown, leaderboard rows, minimap dots | once per frame |
| 3 | C# → GDScript | Drained `SimEvent` list (kills, eats, casts, near-misses) | once per frame |

Everything bulk — path buffers, pellet positions, per-segment transforms — **never crosses**. `SnakeRenderer` and `PelletRenderer` are C# precisely so they can read simulation arrays directly and write `MultiMesh` buffers with no copy.

**Corollary that catches people out:** bots are C# because they query the spatial hash five times per steer. Written in GDScript, 34 bots would cross the boundary ~40 times per tick and give back everything C# bought.

---

## 4. The simulation

### Tick contract

```csharp
public sealed class World
{
    public void Tick(float dt, ReadOnlySpan<InputCommand> commands);
    public void DrainEvents(List<SimEvent> into);   // caller owns and reuses the list
}
```

- **Fixed `dt` of 1/60.** Never variable. Rendering interpolates; the simulation does not.
- `World` owns **all** mutable state. Nothing outside it holds a reference to a snake's internals.
- `Tick` performs no I/O, no logging, no allocation, and touches no clock.
- Maximum 5 catch-up ticks per frame, then time is dropped (spec §11.2).

### Determinism

The same command stream must produce byte-identical state after 1,000 ticks (`C-04`). This is not a nice-to-have — it is the precondition for the predict-and-replay netcode in spec §11.3.

- All randomness comes from a **seeded PRNG stored in world state**. Never `Random.Shared`, never `Math.Random`, never `Guid`.
- No `DateTime`, `Stopwatch`, or environment reads inside `Coil.Sim`.
- Iteration order over entities is by index, never by hash-set order or dictionary order.

### State layout

Struct-of-arrays, not array-of-structs, for anything the tick loop sweeps:

```csharp
Vector2[] _pathPoints;      // all snakes' path points, one shared buffer
int[]     _pathStart;       // per-snake slice into _pathPoints
int[]     _pathCount;
float[]   _mass;
```

Snakes and pellets are **slices into shared buffers**, allocated once at world construction for `MAX_SNAKES` and `MAX_PELLETS`. There is no per-entity heap object in the tick path, and no collection ever grows during a match.

### Configuration

`Coil.Sim` cannot read a `.tres` file — that is a Godot resource type. The flow is:

```
data/balance.tres  ──BalanceLoader (Presentation)──►  SimConfig (POCO)  ──►  new World(config)
```

`SimConfig` is immutable after construction. Every number in spec Appendix A is a field on it. **A literal number in simulation code is a bug**, with two exceptions: `0`, `1`, and mathematical constants.

---

## 5. Memory and performance rules

The tick budget is 6 ms for 34 snakes and 2,600 pellets. These rules are what buys it.

**Banned inside `Tick` and anything it calls:**

- Allocation of any kind — no `new`, no boxing, no closures, no `params` arrays, no string concatenation, no lambdas that capture.
- LINQ. All of it.
- `foreach` over an interface or a `List<T>` where the enumerator boxes. Index with `for`.
- Exceptions as control flow. Exceptions signal a broken invariant, and only in debug builds.
- `Dictionary<TKey,TValue>` lookups on the hot path. Use dense integer indices.

**Required:**

- Preallocate everything at world construction. `C-02` fails the build if the object count grows over 600 ticks.
- `readonly struct` for value types; pass large ones by `in`.
- `Span<T>` / `ReadOnlySpan<T>` over array copies.
- Pool every transient: events, query results, VFX handles.
- The spatial hash query writes into a caller-supplied buffer and returns a count. It never returns a new collection.

**Measured, not assumed.** Every phase gate records numbers in `docs/PERF_LOG.md`, and `C-05` blocks a gate on a >10% regression.

---

## 6. Rendering

- **Bodies**: one `MultiMeshInstance2D` per snake; `SnakeRenderer` writes the transform buffer in bulk from the path arrays. No node per segment, ever.
- **Pellets**: a single `MultiMeshInstance2D` for all of them. One draw call.
- **Interpolation**: `ArenaHost` keeps the previous and current simulation snapshots and lerps by the physics interpolation fraction each frame. The simulation is never asked to run at render rate.
- Heads are separate sprites — they carry eyes, class tint and a name label, and there are only 34 of them.

---

## 7. Events, not signals

`Coil.Sim` emits value-type `SimEvent` structs into a pooled buffer. It does not know that VFX, audio or haptics exist.

```csharp
public readonly struct SimEvent
{
    public readonly SimEventKind Kind;   // Eat, Kill, Death, Cast, NearMiss, BoostStart…
    public readonly int SnakeId, OtherId;
    public readonly Vector2 Position;
    public readonly float Value;
}
```

`ArenaHost` drains the buffer once per frame and dispatches to presentation. This is why `M4-01` can add screen shake without touching a line of simulation code — and why the same simulation runs on a headless server that discards every event.

---

## 8. Error handling

**Fail loud, never silently.** A simulation invariant that breaks means the world state is already wrong, and continuing produces plausible-looking garbage.

- Invariant checks are `Debug.Assert` — free in release, fatal in development.
- No `try/catch` in the tick loop. Nothing there is recoverable.
- `catch` blocks elsewhere must either handle the error meaningfully or rethrow. An empty catch, or one that logs and continues into a bad state, will be rejected in review.
- Presentation code degrades gracefully (a missing sound is not a crash); simulation code does not.

---

## 9. Testing and enforcement

| Layer | Tool | What it proves |
|---|---|---|
| Simulation | xUnit, no engine | Behaviour matches the spec section it implements |
| Architecture | NetArchTest | The layer rule holds |
| Invariants | xUnit | `C-02` allocation, `C-03` mass conservation, `C-04` determinism, path spacing |
| Performance | On-device, `PERF_LOG.md` | The budgets in §11.5 hold on real hardware |
| Feel | Human playtest | `M2-14`, `M4-16` |

**The architecture rules, as tests:**

```csharp
Types.InAssembly(SimAssembly).Should().NotHaveDependencyOn("Godot.Node");
Types.InAssembly(SimAssembly).Should().NotHaveDependencyOn("Godot.Resource");
Types.InAssembly(SimAssembly).Should().NotHaveDependencyOnAny("Coil.Agents", "Coil.Presentation");
Types.InAssembly(SimAssembly).Should().NotHaveDependencyOn("System.IO");
Types.InAssembly(AgentsAssembly).Should().NotHaveDependencyOn("Coil.Presentation");
Types.InAssembly(SimAssembly).That().AreClasses().Should().BeSealed();
```

If a change requires breaking one of these, the design is wrong. Change the design, not the test.

---

## 10. The online seam

Post-M5, going online must not require rewriting the simulation. Every one of these has to stay true, and each phase gate is the moment to re-check them:

1. `Coil.Sim` runs headless — no Godot node, no scene tree, no window.
2. Every actor emits an `InputCommand`. Player, bot and future network peer are indistinguishable to `World`.
3. `Tick` is deterministic under a fixed `dt` and a seeded PRNG.
4. World state is serialisable as flat arrays — no object graph, no references.
5. `Coil.Agents` depends only on `Coil.Sim`, so bots move to the server unchanged and fill empty rooms.

---

## 11. Anti-patterns

Things that will look reasonable in the moment and cost a phase gate later.

| Anti-pattern | Why it hurts | Do instead |
|---|---|---|
| A `Node2D` per body segment | 34 snakes × 300 segments = 10,000 nodes. Frame budget gone. | MultiMesh transform buffers |
| Godot physics or `Area2D` for collision | Signal dispatch and broadphase you don't control, and it can't run headless | The one spatial hash |
| A tunable number written in code | Balance work turns into a code change and a rebuild | `SimConfig` from `balance.tres` |
| GDScript reaching into simulation arrays | Marshalling copies per entity per frame | The three sanctioned crossings (§3) |
| `sim` emitting a Godot signal | Couples the simulation to the engine and breaks the headless server | `SimEvent` structs, drained per frame |
| LINQ in a steering behaviour | Allocates per bot per tick, 34× per frame | Indexed `for` loops over preallocated buffers |
| "I'll pool it later" | Later is M2, and by then it is in 40 places | Pool it in the task that creates it |
| Widening a task while you're in it | Unreviewable diffs, and the phase gate stops meaning anything | Open a new task ID |
