# COIL — Build Roadmap

Companion to `docs/GAME_SPEC.md`. The spec says **what** the game is; this says **in what order it gets built and how big each piece is**.

**94 tasks across 6 phases, plus 10 cross-cutting.** Every task is sized to be implemented and reviewed in one sitting. If a task grows past its size band, it was wrong — split it, don't push through it.

---

## How to use this document

### Task anatomy

Every task has an ID (`M2-05`), the files it touches, a **Done when** that is objectively checkable, and a size. Nothing is "done" because it looks done.

| Size | Budget | Meaning |
|---|---|---|
| **S** | ~1–2 h | One file, one concept, obvious test. |
| **M** | ~half a day | Two or three files, or one file with real logic. |
| **L** | — | **Does not exist here on purpose.** Anything that would be L is already split. If one appears, that's the signal to break it down. |

### Definition of done — applies to every task, no exceptions

1. **The game still runs on the phone.** Not the editor — the phone. Every task ends with a deployable build.
2. **Anything in `Coil.Sim` has a test, written first.** xUnit, no engine boot, whole suite under 10 seconds.
3. **`dotnet test` passes**, which includes the NetArchTest layering rules (`M0-09`).
4. **No new allocations in the tick loop.** Verified against the allocation guard (`C-02`), not by eye.
5. **New tunables land in `balance.tres` → `SimConfig` → spec Appendix A** — all three, same commit.
6. **One task = one branch = one PR**, squash-merged. Conventional commit with the ID:
   `feat(sim): insert body capsules into the spatial hash (M1-06)`
7. **Perf delta recorded in the PR** if the tick loop was touched. "Didn't measure" is only valid where it cannot apply.

Full rules in [`CONVENTIONS.md`](./CONVENTIONS.md); layering and performance constraints in [`ARCHITECTURE.md`](./ARCHITECTURE.md).

### The dependency spine

```
M0 Skeleton ──► M1 Loop ──► M2 Opponents ──► M3 Depth ──► M4 Feel ──► M5 Ship
     │              │             │
     └── M0-17 ─────┴─────────────┘
        perf spike        the fork: if 34 snakes miss 60fps here,
        (throwaway)       cut population or change the render plan
                          BEFORE any content exists
```

**Risk-first ordering.** The two biggest project risks are: does the .NET Android export work at all (`M0-03`), and do 34 snakes hold 60 fps on a mid-range phone? Rather than discover that at M2 with a month of work behind it, `M0-17` fakes 34 snakes with synthetic paths and no gameplay, and measures. That task is throwaway code and that's fine — it buys the answer three weeks early.

### What can run in parallel

Mostly it can't, and that's by design — this is a sequential build for one person. The exceptions:

- **Art/audio asset production** runs alongside everything from M1 onward (see the asset track at the end).
- Within M3, the four ability tasks (`M3-04`…`M3-07`) are independent once `M3-02` lands.
- Within M4, the feel tasks (`M4-02`…`M4-06`) are independent once `M4-01` lands.
- Within M5, screens (`M5-05`, `M5-07`) are independent of each other.

---

## M0 · Skeleton

**Phase gate:** you can steer a snake on your phone and the body follows correctly at any speed.
**19 tasks.** This phase builds the machine that all later phases run on. It is the phase most worth being slow and careful in.

| ID | Task | Files | Done when | Sz |
|---|---|---|---|---|
| **M0-01** | Solution + Godot project | `Coil.sln`, `Coil.csproj`, `src/Coil.Sim`, `src/Coil.Agents`, `src/Coil.Presentation` | Godot 4.5 **.NET** project, Forward Mobile, landscape-sensor, 1280×720 `canvas_items` stretch. Four projects build. **Verify `Coil.Sim` (plain `net8.0`) can reference `GodotSharp` for `Vector2` alone** — if version pinning fights back, fall back to a hand-rolled `Vec2` now, not later. ARCH §2 | M |
| **M0-02** | Git + GitHub + PR flow | `.gitignore`, `.gitattributes`, `.github/pull_request_template.md` | Repo initialised and pushed, `.godot/`, `bin/`, `obj/` and keystores ignored, PR template matches CONVENTIONS §5, `main` protected. | S |
| **M0-03** | ⚠ **Android .NET export** | `export_presets.cfg`, `tools/deploy.sh` | One command builds, installs and launches an **arm64 .NET** build with filtered logcat. **This is the riskiest unknown in the stack — prove it before anything is built on it.** D-07 | M |
| **M0-04** | Strict build settings | `Directory.Build.props`, `.editorconfig` | `Nullable`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, analyzers on, shared across all projects. A deliberate warning fails the build. D-12 | S |
| **M0-05** | xUnit harness | `tests/Coil.Sim.Tests/` | `dotnet test` runs with **no engine boot** and finishes under 10 s. One real test proves the wiring. | S |
| **M0-06** | CI workflow | `.github/workflows/ci.yml` | Build + `dotnet test` + architecture rules on every PR. Godot export deliberately excluded — the device build in the DoD covers it. D-16 | S |
| **M0-07** | `SimConfig` + balance data | `src/Coil.Sim/SimConfig.cs`, `data/balance.tres`, `BalanceLoader.cs` | Every constant in spec Appendix A is a field on an immutable POCO, loaded from `.tres` by the presentation layer. A test asserts none are left at default. ARCH §4 | M |
| **M0-08** | `InputCommand` | `src/Coil.Sim/InputCommand.cs` | `readonly struct {Heading, Boost, Cast}`. The only channel into the simulation. | S |
| **M0-09** | ⚠ **Architecture tests** | `tests/Coil.Sim.Tests/ArchitectureTests.cs` | NetArchTest enforces every rule in ARCH §9. Each fails against a deliberately planted violation. **The layer rule stops being a convention and becomes a build failure.** | M |
| **M0-10** | Snake path buffer | `src/Coil.Sim/Snake.cs` | Ring buffer resampled to `PathStep` with boundary interpolation. Test-first: spacing holds to ±0.01 u across jittered `dt` and speed changes. §4.1 | M |
| **M0-11** | `World.Tick` | `src/Coil.Sim/World.cs` | `Tick(float dt, ReadOnlySpan<InputCommand>)` advances all snakes. SoA state, preallocated, zero allocation. §4.2, ARCH §4 | M |
| **M0-12** | Arena host + fixed tick | `src/Coil.Presentation/ArenaHost.cs` | 60 Hz physics tick, max 5 catch-up ticks, interpolation fraction exposed. A 200 ms stall does not spiral. §11.2 | M |
| **M0-13** | Snake renderer | `src/Coil.Presentation/SnakeRenderer.cs` | One `MultiMeshInstance2D` per snake, transforms written in bulk **from C#** with no marshalling. Head is a separate sprite. Zero per-segment nodes. §11.4 | M |
| **M0-14** | Render interpolation | `ArenaHost.cs`, renderer | Previous/current snapshots lerped each frame. Visibly smooth at 120 Hz, still correct capped to 30. §11.2 | M |
| **M0-15** | Camera | `game/camera.gd` | 0.12 s damped follow, 40 u look-ahead, zoom curve clamped 0.55–1.0 with a 0.4 s lerp. §7.3 | S |
| **M0-16** | Floating joystick | `ui/joystick.gd` | Anchors on touch anywhere in the left 45%, 0.15 dead zone, 1.4× drag-out, fades on release, tracks its own touch index. §3 | M |
| **M0-17** | Player agent + boundary | `ui/player_agent.gd`, `ArenaHost.cs` | Joystick → `InputCommand`, **one crossing per tick** (ARCH §3). Finger-follow alternative behind a `control_scheme` setting. D-04 | M |
| **M0-18** | Debug overlay | `ui/debug_overlay.gd` | Three-finger tap toggles fps, frame ms, tick ms, entity counts, allocation count. Stripped from release builds. | S |
| **M0-19** | ⚠ **Perf spike** (throwaway) | `tools/spike_render.tscn` | 34 dummy snakes with synthetic paths, no AI, no collision, on the floor device. Result in `docs/PERF_LOG.md`. **If under 60 fps, stop and fix the render plan before M1.** | M |

**M0 is now 19 tasks** — two more than the pre-stack version, and several reshaped, all of them one-time infrastructure that pays back from M1 onward. `M0-03` and `M0-09` are the two that must not be deferred: the first proves the stack is viable, the second makes the architecture self-enforcing.

---

## M1 · The Loop

**Phase gate:** you can die on a wall and eat pellets to grow; hash output verified against brute force.
**16 tasks.** After this phase the game is playable, alone, and boring — which is exactly correct.

| ID | Task | Files | Done when | Sz |
|---|---|---|---|---|
| **M1-01** | Pellet store | `sim/food.gd` | Struct-of-arrays with a free list. 3,000 pellets allocated once, never resized. | M |
| **M1-02** | Pellet renderer | `game/renderers/pellet_renderer.gd` | All pellets in a single `MultiMeshInstance2D`. Draw-call count confirmed at 1 in the profiler. §11.4 | S |
| **M1-03** | Pellet spawner | `sim/food.gd` | Maintains `PELLET_TARGET` with a per-tick spawn budget, so a mass die-off doesn't spike a frame. | S |
| **M1-04** | Spatial hash | `sim/spatial_hash.gd` | Insert/remove/query on 96 u cells. Query allocates nothing. §5 | M |
| **M1-05** | Hash tests | `tests/test_hash.gd` | Differential against brute force on randomised point sets, including cell-boundary and negative-coordinate cases. | S |
| **M1-06** | Body insertion | `sim/spatial_hash.gd`, `sim/snake.gd` | Every 4th path point inserted as a capsule; incremental head-insert and tail-evict only. Test: no gaps between consecutive capsules at max speed. §5 | M |
| **M1-07** | Head collision | `sim/collision.gd` | Head-vs-capsule via segment-point distance; head-vs-head with the 10% mass rule; self excluded. §4.4 | M |
| **M1-08** | ⚠ Collision differential test | `tests/test_collision.gd` | 600 ticks of scripted motion, hash results identical to brute force. **This is the M1 gate's proof.** | M |
| **M1-09** | Eat + magnet | `sim/food.gd`, `sim/collision.gd` | Pellets inside `45 + radius·1.5` accelerate to the head and are consumed. Mass rises. §4.3 | M |
| **M1-10** | Mass → size wiring | `sim/snake.gd` | Radius, length and ω recompute from mass; camera zoom responds. Growth from 10 to 2,000 mass is visually smooth. §4.1 | S |
| **M1-11** | Boost | `sim/snake.gd`, `ui/` | Speed, 9/s drain, a 4-mass drop every 0.35 s, hard floor at 25 mass, button greys out. §4.3 | M |
| **M1-12** | Death pipeline | `sim/world.gd`, `sim/food.gd` | Kill event → 70% of mass as 6-mass pellets along the body path → teardown. Mass conservation asserted in a test. §4.4 | M |
| **M1-13** | Respawn + grace | `sim/world.gd` | Safe spawn search (no body within 300 u), 2 s translucent grace, ends early on boost or cast. §4.4 | M |
| **M1-14** | Border | `sim/world.gd`, `game/` | Lethal at 3,500 u, pulse warning from 3,350 u. §4.4 | S |
| **M1-15** | Arena backdrop | `game/renderers/` | Ground, border ring, subtle grid for motion reference. Flat pastel, no post-processing. | S |
| **M1-16** | Frame budget check | `docs/PERF_LOG.md` | One snake + 2,600 pellets + hash, measured on the device. Tick time recorded against the ≤6 ms budget. | S |

---

## M2 · Opponents

**Phase gate:** 34 snakes at 60 fps on the floor device — profiled, not assumed.
**14 tasks.** The riskiest phase. Do not start M3 until the gate passes on real hardware.

| ID | Task | Files | Done when | Sz |
|---|---|---|---|---|
| **M2-01** | Agent interface | `src/Coil.Agents/IAgent.cs` | Player and bot are indistinguishable to `World` — both just return an `InputCommand`. §8 | S |
| **M2-02** | Persona resources | `data/personas/*.tres` → C# POCO | Grazer/Hunter/Coward/Ace with share weights, reaction delays and behaviour weights. Assignment matches the 40/30/20/10 split. §8.2 | S |
| **M2-03** | Food density grid | `sim/food.gd` | Coarse 256 u buckets, updated incrementally on spawn/consume. Bots never iterate pellets. §8.1 | M |
| **M2-04** | Ray fan avoidance | `src/Coil.Agents/BotAgent.cs` | 5 rays to 1.5× turn radius, queried through the hash. A bot alone in a walled arena survives 5 minutes. §8.1 | M |
| **M2-05** | Steering blend | `src/Coil.Agents/BotAgent.cs` | Seek / avoid / cut-off / flee / corpse-rush weighted into one target heading. Avoidance dominates when a hit is imminent. §8.1 | M |
| **M2-06** | Reaction delay | `src/Coil.Agents/BotAgent.cs` | Per-persona input ring buffer. An Ace visibly reacts faster than a Grazer at the same distance. §8.2 | S |
| **M2-07** | Perception clamp | `src/Coil.Agents/BotAgent.cs` | Nothing beyond 1,400 u is queried. A bot cannot dodge what it cannot see. §8.2 | S |
| **M2-08** | Bot boost + cast policy | `src/Coil.Agents/BotAgent.cs` | Boosts on committed cut-offs and on flee, never idly. Bots do not go bankrupt boosting. | S |
| **M2-09** | LOD scheduler | `src/Coil.Agents/BotAgent.cs`, `sim/world.gd` | Beyond 2,500 u: steer every 12th tick, skip the ray fan. Movement and collision still every tick. §8.3 | M |
| **M2-10** | Population director | `sim/world.gd` | Holds 34 snakes, staggers respawns, and reports the arena death rate (target ~1 per 6 s). §8.2 | M |
| **M2-11** | Name pool | `data/names.tres` | Curated, moderated list. No duplicates within a match. §15 | S |
| **M2-12** | Leaderboard | `sim/world.gd`, `ui/hud.gd` | Live top 10, sorted without a per-frame full sort, player row pinned when off-list. §9 | M |
| **M2-13** | ⚠ **Profiling pass** | `docs/PERF_LOG.md` | Per-subsystem tick breakdown at full population on the floor device. Every number in spec §11.5 measured and recorded. | M |
| **M2-14** | Blind bot playtest | `docs/PLAYTEST.md` | Three testers play 10 minutes and try to name which snakes are bots. Results written down. Failure here means iterating §8, not shipping. | S |

**Fallback if M2-13 fails:** drop `SNAKE_COUNT` to 24 and `ARENA_RADIUS` to 2,900 in `balance.tres` — a data change, no code. Only if that fails do you move `spatial_hash` and `collision` to C#/GDExtension (spec §14).

---

## M3 · Depth

**Phase gate:** every class is fun for five minutes, and a small snake can kill a big one.
**17 tasks.** This is where the game stops being slither.io.

| ID | Task | Files | Done when | Sz |
|---|---|---|---|---|
| **M3-01** | Class resources | `data/classes/*.tres` | Four classes as data: passive modifiers, active id, cooldown, cost, unlock level. §6 | S |
| **M3-02** | Ability framework | `sim/abilities/` | Cooldown and cost in sim state, 0.2 s wind-up, a cast event emitted for presentation. Adding a fifth ability requires no framework change. §6 | M |
| **M3-03** | Passive modifiers | `sim/snake.gd` | Speed, drain, radius, magnet and decay multipliers applied at stat-compute time, not scattered. §6 | S |
| **M3-04** | Dart · Dash | `sim/abilities/dash.gd` | 0.45 s at 3.2× speed, ω halved during it. Cannot be used to phase through a body. §6 | S |
| **M3-05** | Bulwark · Shield | `sim/abilities/shield.gd` | Absorbs one lethal body contact within 4 s, then breaks and sheds 30% mass. Needs a survive-hook in `collision.gd`. §6 | M |
| **M3-06** | Warden · Snare | `sim/abilities/snare.gd` | A 140 u field in the hazard layer for 5 s: ×0.7 speed, boost disabled, symmetric to the caster. §6 | M |
| **M3-07** | Phantom · Phase | `sim/abilities/phase.gd` | 1.2 s of body pass-through. Heads, hazards and the border still kill. §6 | S |
| **M3-08** | Ability button | `ui/ability_button.gd` | Radial cooldown sweep, mass cost on the face, greyed when unaffordable, wind-up feedback. §3 | M |
| **M3-09** | Hazard layer | `sim/hazards.gd` | Static obstacles authored as data, inserted once into the hash, lethal on head contact. §7.1 | M |
| **M3-10** | Void pits | `sim/hazards.gd` | Instant death regardless of grace or shield. §4.4 | S |
| **M3-11** | Current fields | `sim/hazards.gd` | ±70 u/s added to velocity inside the bands, with a readable flow visual. §7.1 | M |
| **M3-12** | Biome map | `data/biomes/*.tres` | Meadow / Thicket / Currents / The Well authored to the 45-25-20-10 split, driving pellet density and ground tint. §7.1 | M |
| **M3-13** | Leader decay | `sim/world.gd` | Top snake loses 0.4%/s above 500 mass. Halved for Bulwark. §7.2 | S |
| **M3-14** | Bounty | `sim/world.gd`, `game/` | Top snake glows and pays out 100% on death. §7.2 | M |
| **M3-15** | Minimap | `ui/minimap.gd` | Arena ring, self dot, bounty pip on the edge. Costs under 0.3 ms. §7.2 | M |
| **M3-16** | Class select (functional) | `ui/class_select/` | Four cards with passive/active text and lock states. Polish comes at M4-12. §12 | M |
| **M3-17** | Balance telemetry | `sim/world.gd` | Local-only log of kills and deaths per class per session, for the §14 tuning loop. | S |

---

## M4 · Feel

**Phase gate:** a stranger plays for ten minutes without being told anything.
**16 tasks.** Nothing here changes the rules. Everything here decides whether people keep playing.

| ID | Task | Files | Done when | Sz |
|---|---|---|---|---|
| **M4-01** | Event bus + VFX pool | `game/vfx/` | Sim emits typed events; presentation consumes. All effects pooled, zero runtime instantiation. | M |
| **M4-02** | Eat feel | `game/vfx/` | 80 ms scale-to-zero flight, head pulse, click rising in pitch with the combo. §10 | S |
| **M4-03** | Boost feel | `game/vfx/` | Trail ribbon, head highlight, 3% camera pull-back, rumble tick. §10 | S |
| **M4-04** | Kill feel | `game/vfx/` | 90 ms hit-stop, 4 px shake, corpse burst, kill banner, strong haptic. §10 | M |
| **M4-05** | Death sequence | `game/`, `ui/` | 0.35 s slow-mo to 0.25×, desaturate to 40%, 15% pull-back, sine sweep, then the death card. §10 | M |
| **M4-06** | Near-miss | `sim/collision.gd`, `game/vfx/` | Under 12 u and survived → rim light on the passing body + tiny haptic. Detection is free (reuses the collision query). §10 | M |
| **M4-07** | Haptics | `game/haptics.gd` | Android vibration wrapper with named patterns and a settings toggle. No-ops safely on unsupported devices. §3 | M |
| **M4-08** | Audio | `game/audio/` | Music bus + SFX bus, one bed at ~90 BPM, ducking to 40% on death, silent switch respected. §10 | M |
| **M4-09** | Camera polish | `game/camera.gd` | Shake compositing, zoom easing and look-ahead tuned together. No nausea over a 10-minute session. §7.3 | S |
| **M4-10** | HUD final | `ui/hud.gd` | Mass, leaderboard, minimap, buttons — final layout, tested at 16:9 and 20:9, with safe-area insets for notches. §3 | M |
| **M4-11** | Main menu | `ui/menu/` | Play, Cosmetics, Missions, Records, Settings. Cold start to menu under 2 s. §12 | M |
| **M4-12** | Class select final | `ui/class_select/` | Skin preview, "plays like" hint line, polish pass. §12 | S |
| **M4-13** | Death card | `ui/death_card/` | Mass, kills, time, placement, animating XP bar, mission ticks, large Play again. §12 | M |
| **M4-14** | Settings | `ui/settings/` | Handedness mirror, joystick opacity, haptics, audio, control scheme, colorblind mode. All persisted. §3 | M |
| **M4-15** | Colorblind shapes | `game/renderers/` | Head markers carry a shape tag alongside class colour. Verified under a deuteranopia filter. §3 | S |
| **M4-16** | ⚠ Stranger test | `docs/PLAYTEST.md` | Three people who have never seen it, ten minutes each, no instructions, notes written down. | S |

---

## M5 · Ship-ready

**Phase gate:** a 30-minute soak on the floor device with no leak, no crash, stable frame time.
**12 tasks.**

| ID | Task | Files | Done when | Sz |
|---|---|---|---|---|
| **M5-01** | Profile store | `game/profile.gd` | `user://profile.json`, versioned schema, atomic write, corrupt file falls back to defaults instead of crashing. §9 | M |
| **M5-02** | XP + levels | `game/profile.gd` | `mass/10 + kills·25 + seconds/4`, levels 1–20, awarded on the death card. §9 | S |
| **M5-03** | Unlock gating | `ui/class_select/` | Classes unlock at levels 1/2/4/6 with clear locked-state UI. §6 | S |
| **M5-04** | Skin system | `game/renderers/`, `data/palettes/` | 8 patterns × 12 palettes via a shader, no extra draw calls, no atlas growth. §9 | M |
| **M5-05** | Cosmetics screen | `ui/menu/` | Browse, preview on a live snake, equip, see what's still locked. §9 | M |
| **M5-06** | Daily missions | `game/missions.gd` | 3 per day, rerolled at local midnight, progress tracked in-run, claimed on the death card. Clock-change safe. §9 | M |
| **M5-07** | Records screen | `ui/menu/` | Best mass, best kills, longest life, total playtime. §9 | S |
| **M5-08** | Android lifecycle | `game/` | Back button behaviour per §12, pause on focus loss with no death, autosave on pause. | M |
| **M5-09** | Icon + store assets | `assets/store/` | Adaptive icon, splash, feature graphic, screenshots, description. | M |
| **M5-10** | Release build | `export_presets.cfg`, `tools/release.sh` | Release keystore, versionCode/Name, shrinking on, debug overlay stripped, APK ≤ 45 MB verified. §11.5 | M |
| **M5-11** | Soak test | `docs/PERF_LOG.md` | 30 minutes unattended: object count flat, frame time stable, RAM ≤ 350 MB, battery ≤ 9%/h. | M |
| **M5-12** | Play Store compliance | `docs/PRIVACY.md` | Data safety form filled (no data collected, no network), privacy policy published, content rating done. | S |

---

## Cross-cutting tracks

These don't belong to a phase. Start them when their first dependency lands and run them alongside.

### Asset track — starts at M1, must land by M4-10

| ID | Task | Done when | Sz |
|---|---|---|---|
| **A-01** | Palette system | 12 pastel palettes as resources, each verified for body-vs-ground contrast. | S |
| **A-02** | Snake atlas | Body dot, head, eyes, 8 skin patterns, one atlas, no mipmaps. | M |
| **A-03** | Pellet + hazard art | Pellet variants, obstacle shapes, void pit, current flow tile. | M |
| **A-04** | UI kit | Joystick, buttons, panels, iconography, one type scale. | M |
| **A-05** | SFX set | Eat, boost, kill, death, ability ×4, UI. Consistent loudness. | M |
| **A-06** | Music bed | One calm ~90 BPM loop, seamless. | S |

### Automated checks — build each as its subject appears

| ID | Task | Done when | Sz |
|---|---|---|---|
| **C-01** | Architecture rules | Already landed as `M0-09`. Runs on every `dotnet test`. | ✓ |
| **C-02** | Allocation guard | A test fails if the tick loop's object count grows over 600 ticks. Add at M1. | S |
| **C-03** | Mass conservation | Total world mass changes only through the known sinks in §4.3. Add at M1-12. | S |
| **C-04** | ⚠ Determinism check | The same command stream produces byte-identical state after 1,000 ticks. Add at M2 — this is what makes the online port in §11.3 possible. | M |
| **C-05** | Frame-budget regression | `PERF_LOG.md` gets an entry per phase gate. A regression over 10% blocks the gate. | S |

---

## Summary

| Phase | Tasks | Gate |
|---|---|---|
| M0 Skeleton | 19 | Steer a snake on the phone; body correct at any speed |
| M1 The Loop | 16 | Die on a wall, eat to grow; hash proven against brute force |
| M2 Opponents | 14 | **34 snakes at 60 fps on the floor device** |
| M3 Depth | 17 | Every class fun for 5 min; small snake can kill a big one |
| M4 Feel | 16 | A stranger plays 10 minutes with no instructions |
| M5 Ship-ready | 12 | 30-minute soak: no leak, no crash, stable frame time |
| Cross-cutting | 10 | Land alongside their phases |
| **Total** | **104** | |

**The four moments that decide this project:** `M0-03` (does the stack even export?), `M0-19` (does the render plan hold?), `M2-13` (does it hold with 34 snakes?), and `M4-16` (does anyone want to play it?). Everything else is execution.
