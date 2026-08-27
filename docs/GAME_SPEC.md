# COIL — Game Specification v1.0

> Working title. A snake-arena game for Android, built in Godot 4.
> Scope of this document: the **polished vertical slice** (v1). Anything beyond it is marked `[LATER]`.
>
> Build order and task breakdown: [`ROADMAP.md`](./ROADMAP.md) · Layering and performance rules: [`ARCHITECTURE.md`](./ARCHITECTURE.md) · Why each choice was made: [`DECISIONS.md`](./DECISIONS.md)
>
> **Stack:** Godot 4.7 (.NET) · C# simulation, GDScript presentation · landscape only · offline bots

---

## 1. Vision

**One line:** slither.io with a *place* to fight in, a *class* to fight with, and controls that don't fight you.

slither.io's genius is its 10-second onboarding. Its weakness is that after 3 minutes there is nothing left to learn: the map is a void, every snake is identical, and the biggest snake wins by existing. COIL keeps the onboarding and fixes the ceiling.

### Design pillars

| Pillar | Means | Kill criterion |
|---|---|---|
| **Instant** | Tap Play → steering a snake in < 3 seconds. No tutorial, no account, no connection. | If a new player needs to be told anything, cut it. |
| **Readable** | At any moment you can see what will kill you in the next 2 seconds. | Flat pastel art exists to serve this. If a VFX hides a body, it goes. |
| **Skill over size** | Being big is an advantage, not a win condition. | A skilled 200-mass snake must be able to kill a careless 2000-mass snake. |
| **Offline-first** | Full game on a plane, in a tunnel, on 2G. | Nothing in the loop may block on a network call. |

### Explicit improvements over slither.io

1. **Class loadout** — a passive + an active ability chosen before the match. Skill expression beyond steering.
2. **The map is a place** — biomes, obstacles, currents, void pits. Terrain creates the tactics.
3. **Anti-snowball systems** — leader bounty, mass decay at the top, abilities priced in mass.
4. **Real mobile controls** — floating joystick, separate boost, no accidental death from a mis-tap.
5. **Bots that play like people** — offline is a first-class mode, not a fallback.
6. **Honest monetization** — cosmetics only. Classes are earned by playing. `[LATER]`

---

## 2. Core loop

```
Main menu → pick class → arena (endless) → die → death card (stats + XP) → respawn in 1 tap
```

**Session shape:** endless arena, join and leave at will. No round timer, no win screen. A "run" is one life; the run ends when your head hits something it shouldn't.

**The 30-second loop:** eat pellets → grow → spend mass on boost/abilities to cut someone off → eat their corpse → repeat.

**The 10-minute loop:** climb the arena leaderboard, take the leader bounty, bank XP toward the next class/skin.

---

## 3. Controls

**Scheme: floating virtual joystick + action buttons.**

### Layout (default, right-handed; mirrored in settings)

```
┌──────────────────────────────────────────────┐
│  mass ●  1,240        ┃ leaderboard          │
│                       ┃ 1. Rook      3,102   │
│                       ┃ 2. YOU       1,240   │
│                                              │
│                                              │
│                  (arena)                     │
│                                              │
│                                              │
│                                              │
│   ╭────╮                          ╭─────╮    │
│   │ ⊙  │  ← joystick zone         │ ABL │    │
│   ╰────╯     (left 45% of screen) ╰─────╯    │
│                                   ╭─────╮    │
│                                   │BOOST│    │
│                                   ╰─────╯    │
└──────────────────────────────────────────────┘
```

### Joystick behaviour

| Parameter | Value | Note |
|---|---|---|
| Activation zone | Left 45% of screen, full height | Touch anywhere in it to place the stick |
| Stick radius | 110 dp | Anchor = first touch point |
| Dead zone | 0.15 of radius | Below this, heading is held, not zeroed |
| Recenter | On release, stick fades over 0.15s | Heading holds last direction — the snake never stops |
| Drag-out | Stick anchor follows the finger if it exceeds 1.4× radius | Prevents thumb-drift losing control |
| Output | Direction only; magnitude is ignored above dead zone | Steering is a target *heading*, not a velocity |

**Critical rule:** the joystick sets a **target heading**. The snake rotates toward it at `ω_max` (§4.2). Full-speed 180° reversals are impossible — that turn-rate cap *is* the game's core constraint.

### Buttons

- **BOOST** — hold. 96 dp, bottom-right. Repeats haptic tick every 250ms while draining.
- **ABILITY** — tap. 96 dp, above boost. Radial cooldown sweep + mass cost shown on the face. Greyed and non-interactive when unaffordable.
- Both buttons have a 12 dp invisible touch-padding halo.
- Multi-touch is mandatory: steer + boost + ability simultaneously must all register.

### Accessibility
- Left-handed mirror toggle.
- Joystick opacity slider (0–100%).
- Haptics toggle.
- Colorblind palette: class/threat colors also carry a shape tag on the head marker.

---

## 4. Snake simulation

### 4.1 Representation

A snake is **not** a chain of physics bodies. It is:

- `head: Vector2`, `heading: float (radians)`
- `path: PackedVector2Array` — a ring buffer of head positions, resampled to a **fixed arc-length spacing** of `PATH_STEP = 6.0` world units.
- `mass: float` — the single source of truth for size.

Body segments are *sampled from the path*, not simulated. Rendering and collision both read the same path.

```
length_units = 60 + mass * 1.6          # arc length of visible body
segment_count = length_units / PATH_STEP
radius = clamp(14.0 * pow(1.0 + mass / 100.0, 0.25), 14.0, 46.0)
```

Ring buffer is sized to `max_mass` and never reallocated mid-match.

### 4.2 Movement

```
speed_base   = 220 u/s
speed_boost  = 380 u/s          # 1.73×
ω_max        = clamp(900.0 / radius, 2.0, 4.5)   # rad/s
```

Per fixed tick (60 Hz):
1. Read target heading from input (player) or steering behaviour (bot).
2. `heading = rotate_toward(heading, target, ω_max * dt)`
3. `head += Vector2.RIGHT.rotated(heading) * speed * dt`
4. Push `head` into the path if it has travelled ≥ `PATH_STEP` since the last push (interpolate to land exactly on the step boundary — this keeps body spacing frame-rate independent).

Turn radius therefore *grows* with size (`r_turn = speed / ω_max`): big snakes are genuinely less nimble. This is the primary balancing force against mass.

### 4.3 Mass economy

| Source / sink | Amount |
|---|---|
| Start mass | 10 |
| Pellet (normal) | +1 |
| Pellet (boost-drop) | +4 |
| Pellet (corpse chunk) | +6 |
| Boost drain | −9 mass/s, drops one 4-mass pellet every 0.35s behind the tail |
| Ability cast | −3% of current mass, minimum 5 (see §6) |
| Leader decay | −0.4%/s of mass above 500 (§7.3) |
| Death | 70% of mass returned to the world as pellets along the body path |

- **Boost floor:** cannot boost below 25 mass. Button greys out.
- **Magnet radius:** `45 + radius * 1.5`. Pellets inside it accelerate toward the head — a quality-of-life fix for touch precision.
- Boost is a deliberate *leak* (drains 9/s, drops ~11/s of value): the 20% loss is the game's mass sink and prevents runaway inflation.

### 4.4 Death rules

| Event | Result |
|---|---|
| Your head touches another snake's body | You die. |
| Your head touches your own body | **Nothing.** Self-collision is off — it punishes the player for the camera being too zoomed-out to see their own tail. |
| Head-on-head, mass difference > 10% | Smaller snake dies. |
| Head-on-head, within 10% | Both die. |
| Head touches the arena border | You die (with a 1.5s warning: border pulses red, controller rumbles). |
| Head enters a void pit | You die. |

**Respawn:** 2.0s of grace — you are translucent, cannot be killed, and cannot kill. Grace ends early if you boost or cast.

---

## 5. Collision & world queries

Everything uses **one uniform spatial hash**. No Godot physics server, no Area2D, no signals.

```
CELL_SIZE = 96 u        # ≈ 2× max segment radius
```

Three layers in the same grid, keyed separately:
- **Bodies** — every 4th path point is inserted as a *capsule* (point[i] → point[i+4], radius). 4× fewer inserts, zero gap risk because the capsule spans the skipped points.
- **Pellets** — point insert, rebuilt only when a cell's contents change.
- **Hazards** — static, built once at map load.

**Only heads query.** N snakes → N queries per tick, each touching ~9 cells. This is the whole reason the game hits 60 fps with 40 snakes.

Head test: `segment_distance_to_point(capsule_a, capsule_b, head) < radius_head + radius_body`.

Rebuild strategy: bodies re-insert only the head-side cells changed this tick and evict the tail-side cell it left. Full rebuild never happens after init.

---

## 6. Classes

Four classes. Each has **one passive** and **one active**. All four are balanced to the same rule:

> **An active never grants a kill. It creates or denies a cut-off.**

| Class | Passive | Active | Cooldown | Mass cost |
|---|---|---|---|---|
| **Dart** | +8% base speed; boost drain −20% | **Dash** — 0.45s burst at 3.2× speed in the current heading. `ω_max` halves during it (commit to the line). | 14s | 3% |
| **Bulwark** | Radius +12% at equal mass; leader decay halved | **Shield** — absorbs the next lethal body contact within 4s, then breaks (loud, visible, 30% mass shed). | 26s | 5% |
| **Phantom** | Body renders at 55% opacity to *other* players | **Phase** — 1.2s of passing through bodies (not heads, not hazards, not the border). | 22s | 4% |
| **Warden** | Magnet radius +60% | **Snare** — drops a 140 u slow-field behind you for 5s; anyone inside (including you) has speed ×0.7 and cannot boost. | 18s | 4% |

Notes:
- Class is chosen **before** the match and locked for the run. No mid-run swapping — it keeps the read honest ("that's a Phantom, it can phase my wall").
- Every active is **telegraphed**: a 0.2s wind-up animation and a distinct audio cue readable by the victim. No invisible counterplay.
- Costs are a **percentage of mass**, so the leader pays more per cast. This is an anti-snowball lever, not flavour.
- Unlock order: Dart free → Bulwark L2 → Warden L4 → Phantom L6. All four reachable in roughly an hour of play. `[LATER: cosmetic skins per class]`

---

## 7. The arena

### 7.1 Shape

Circular, radius **3500 u**. Border is lethal, drawn as a thick pastel ring with an inward-fading danger gradient starting at 3350 u.

Population: **34 snakes** (you + 33 bots). ~2,600 pellets steady-state, spawned to maintain density, weighted toward biome rules.

### 7.2 Biomes

Four zones, laid out as a fixed hand-authored map for the slice (procedural `[LATER]`):

| Biome | Coverage | Contents | Tactical role |
|---|---|---|---|
| **Meadow** | 45%, outer ring | Open, even low pellet density | Safe farming. Boring on purpose — it makes the center attractive. |
| **Thicket** | 25%, mid-ring clusters | Static rounded obstacles (lethal to touch), dense pellets | Chokepoints. Where kills happen. |
| **Currents** | 20%, two spiral bands | Directional flow, ±70 u/s added to velocity | Free speed one way, a trap the other. Cuts across turn-rate limits. |
| **The Well** | 10%, center | Void pits (instant death), highest-value pellets, leader bounty spawns here | High risk / high reward. The map's gravity well. |

Obstacles and pits are static bodies in the hazard layer of the spatial hash — same query, no extra system.

### 7.3 Anti-snowball systems

Three stacked, deliberately mild:

1. **Leader decay** — the #1 snake loses 0.4%/s of mass above 500. Caps practical mass around 3,000–4,000 rather than ∞.
2. **Bounty** — the #1 snake is marked on every player's minimap edge and glows. Killing them yields **100%** of their mass (vs the normal 70%). Being #1 should feel dangerous.
3. **Percentage ability costs** — see §6.

Combined effect: leading is achievable and prestigious, but it is a target, not a fortress.

### 7.4 Camera

- Follows the head with a 0.12s critically-damped smoothing.
- Zoom scales with radius: `zoom = base_zoom * pow(radius / 14.0, -0.35)`, clamped to [0.55, 1.0].
- Zoom lerps over 0.4s (never snaps — snap zoom is nausea).
- Slight look-ahead: camera centre offsets 40 u along the heading.

---

## 8. Bots

Bots are the entire opponent set in v1. They must not feel like bots.

### 8.1 Architecture

Each bot produces exactly the same output as a player: **a target heading, a boost flag, an ability flag**. They run through the identical simulation path. This is not just cleanliness — it is what makes the network migration in §11 possible.

### 8.2 Steering (weighted blend, evaluated every 4th tick)

| Behaviour | Weight driver |
|---|---|
| **Seek food** | Nearest high-value pellet cluster (from a coarse 256 u food-density grid, not per-pellet) |
| **Avoid body** | Strongest force; raycast fan of 5 rays, 1.5× turn-radius ahead. Dominates all other weights when a hit is imminent. |
| **Avoid border/hazard** | Same fan, treated as body |
| **Cut off** | If a target's projected position in 1.2s is reachable, steer to intercept and boost |
| **Flee** | If a larger snake's head is within 400 u and closing, break away perpendicular |
| **Corpse rush** | On any death within 900 u, high-weight seek for 4s |

### 8.3 Personality & difficulty

Each bot rolls a persona at spawn, which scales the weights and reaction time:

| Persona | Share | Reaction delay | Traits |
|---|---|---|---|
| Grazer | 40% | 260ms | Avoids fights, farms edges, boosts rarely |
| Hunter | 30% | 180ms | High cut-off weight, boosts aggressively |
| Coward | 20% | 220ms | Huge flee weight, corpse-rushes constantly |
| Ace | 10% | 110ms | All weights high, uses abilities correctly, near-optimal cut-offs |

Rules:
- Bots have **no perfect information**: perception is limited to a 1,400 u radius, and each has a per-persona reaction delay applied as an input-buffer lag.
- Bots die. Target ~1 bot death per 6 seconds arena-wide so the world feels alive and corpses are a real food source.
- Difficulty scales by *persona mix*, not by cheating. The player's mass never alters bot stats.

### 8.4 LOD

- Bots > 2,500 u from the camera tick their steering every 12th tick and skip the ray fan (using only the food grid + a cheap body-density lookup).
- Their movement and collision still run every tick — off-screen snakes must remain real, or the leaderboard lies.

---

## 9. Meta progression

Deliberately shallow in v1 — enough to give a session a reason to end well.

- **XP** — `mass_at_death / 10 + kills * 25 + survival_seconds / 4`. Awarded on the death card, animated.
- **Levels** — 1–20 for the slice. Unlocks: classes (§6), then skin patterns.
- **Skins** — a *pattern* (8 in v1: solid, stripe, dots, gradient, chevron, ring, dash, scale) × a *palette* (12 pastel sets). 96 combinations, all cosmetic, all unlocked by level or by daily missions. Zero purchase in v1.
- **Daily missions** — 3 per day, rerolled at local midnight: "eat 400 pellets", "get 3 kills as Warden", "survive 4 minutes". Reward XP.
- **Records** — best mass, best kills, longest life, total playtime. Local only.
- **Leaderboard** — in-arena live top 10 (bots included, named from a curated name pool). Global leaderboard is `[LATER]`.

Storage: a single `user://profile.json`, written on death and on app pause. No account, no cloud, no permissions.

---

## 10. Game feel

The slice lives or dies here. Budget real time for this list.

| Moment | Treatment |
|---|---|
| Eating a pellet | Pellet scales to 0 over 80ms while flying to the head; head pulses +4% scale; soft click; pitch rises with combo |
| Boosting | Trail ribbon, mild chromatic-free bloom on the head, controller rumble tick, FOV-ish zoom-out of 3% |
| Killing someone | 90ms hit-stop, 4 px screen shake, corpse pellets burst outward then settle, kill banner slides in, strong haptic |
| Dying | 0.35s slow-motion to 0.25× speed, desaturate to 40%, camera pulls back 15%, low sine sweep, then the death card |
| Near miss (< 12 u from a body, survived) | Brief white rim-light on the passing body + tiny haptic. Rewards the read. |
| Ability ready | Button flashes once + a soft chime. Never a nag loop. |
| Leader bounty | The bountied snake gets a slow gold shimmer; a pip appears on the minimap ring |

Audio: one music bed (calm, ~90 BPM, loops), a duck to 40% during the death slow-mo. SFX bus separate. Everything mutable in settings and respectful of the phone's silent switch.

---

## 11. Technical architecture

**Engine:** Godot 4.7 (.NET), **Forward Mobile** renderer, **landscape only**.

**Language split** — simulation, bot agents and the bulk MultiMesh writers are **C#**; UI, menus, VFX orchestration and player input are **GDScript**. Bots are C# because they query the spatial hash five times per steer: written in GDScript they would cross the interop boundary ~40 times per tick and hand back everything C# bought.

> Full layering rules, the tick contract, the three sanctioned interop crossings and the performance rules live in [`ARCHITECTURE.md`](./ARCHITECTURE.md). This section is the summary.

### 11.1 The one rule

> **The simulation never touches a Node.**

`sim/` is pure data + functions. It knows nothing about scenes, rendering, or input devices. `game/` observes it and draws it. This is what makes §11.4 a port instead of a rewrite.

### 11.2 Layout

Project and assembly layout is specified in [`ARCHITECTURE.md`](./ARCHITECTURE.md) §2. The conceptual shape:


```
res://
  sim/                     # pure logic — no Node, no rendering, no input
    world.gd               # tick(dt, commands) -> world state
    snake.gd               # path buffer, mass, movement integration
    spatial_hash.gd
    collision.gd
    food.gd
    hazards.gd
    abilities/             # one script per active, pure functions on world state
    commands.gd            # InputCommand: {heading, boost, cast} — the ONLY way to affect the world
  agents/
    player_agent.gd        # touch input  -> InputCommand
    bot_agent.gd           # steering     -> InputCommand
    [LATER] net_agent.gd   # socket       -> InputCommand
  game/                    # presentation
    arena.tscn / arena.gd  # owns World, drives the tick, renders
    renderers/
      snake_renderer.gd    # MultiMeshInstance2D, one per snake, instances = segments
      pellet_renderer.gd   # ONE MultiMeshInstance2D for all pellets
      hazard_renderer.gd
    camera.gd
    vfx/ audio/
  ui/
    hud.tscn  joystick.gd  ability_button.gd
    menu/ class_select/ death_card/ missions/ settings/
  data/                    # .tres Resources — every tunable number in §4/§6/§7
    classes/*.tres  balance.tres  biomes/*.tres  palettes/*.tres
  assets/
```

### 11.3 Tick model

- **Fixed simulation tick: 60 Hz**, in `_physics_process`. Deterministic given the same command stream.
- **Render decoupled**: renderers interpolate between the previous and current sim state by the physics interpolation fraction. On a 90/120 Hz phone this is visibly smoother; on a 45 fps thermal-throttled phone the sim stays correct.
- Max 5 catch-up ticks per frame, then the sim drops time (never spiral-of-death).

### 11.4 Server-ready seams `[LATER]`

Because the sim is pure and every actor emits an `InputCommand`, going online means:
1. Run `sim/` headless in a Godot dedicated-server export (or port it — it's ~1,500 lines of pure logic).
2. Clients send `InputCommand` at 30 Hz; server broadcasts snapshots at 20 Hz.
3. Clients keep their local sim, apply snapshots, reconcile with the standard predict/replay (the fixed tick + command log makes replay trivial).
4. `bot_agent.gd` moves to the server unchanged, and fills empty rooms.

Nothing in v1 should be written in a way that breaks this. When in doubt: does this line of code live in `sim/`? Then it may not reference a Node.

### 11.5 Rendering

- **Snake bodies:** one `MultiMeshInstance2D` per snake, one instance per segment, transforms written in bulk from the path buffer each frame. 40 snakes ≈ 40 draw calls.
- **Pellets:** a single `MultiMeshInstance2D` for all ~2,600 pellets. One draw call.
- **Heads:** separate sprites (they need eyes, class tint, name label).
- No `Sprite2D` per segment. Ever.
- One texture atlas, `filter: linear`, mipmaps off, MSAA off, no post-processing except an optional cheap vignette.
- Flat pastel art means no normal maps, no lights, no shadows. The style is a performance decision as much as an aesthetic one.

### 11.6 Performance targets

| Metric | Target | Floor device |
|---|---|---|
| Frame rate | 60 fps sustained | Snapdragon 6-series (2021), 4 GB RAM |
| Sim tick budget | ≤ 6 ms | 34 snakes, 2,600 pellets |
| Draw calls | ≤ 80 | |
| APK size | ≤ 65 MB | arm64-only; ~20 MB is the .NET runtime (see D-17) |
| Cold start → menu | ≤ 2.0 s | |
| Menu → in arena | ≤ 1.0 s | |
| RAM | ≤ 350 MB | |
| Battery | ≤ 9%/hour above idle | |

Zero allocations in the tick loop: all buffers preallocated, all pellets/snakes/VFX pooled.

---

## 12. Screens

```
Boot (logo, 0.8s, preloads atlas)
 └─ Main Menu ── Play ──────────► Class Select ──► Arena ──► Death Card ──┐
                 Cosmetics                                     │  │        │
                 Missions                              [Play again]│    [Menu]
                 Records                                          │        │
                 Settings                                    (skips class  │
                                                              select) ─────┘
```

- **Class Select** doubles as the loadout screen: 4 cards, passive/active text, the equipped skin, a one-line "plays like" hint.
- **Death Card**: final mass, kills, survival time, arena placement, XP bar animating, mission progress ticks. Two buttons: **Play again** (default, large) and Menu.
- Android back button: from Arena → confirm-quit dialog. Everywhere else → up one level. Never exits the app from a submenu.
- Handles: app pause on incoming call → sim pauses, no death.

---

## 13. Milestones

Each milestone must be **playable on a real phone** before the next begins.

| # | Name | Deliverable | Done when |
|---|---|---|---|
| **M0** | Skeleton | Godot project, Android export working, one snake driven by the joystick, path-based body, camera | You can steer a snake on your phone and the body follows correctly at any speed |
| **M1** | The Loop | Pellets, mass/growth, boost + drop, spatial hash, collision, death, respawn | You can die on a wall and eat pellets to grow. Hash verified against brute force. |
| **M2** | Opponents | Bot agent, 4 personas, corpse drops, live leaderboard, 34 snakes | 34 snakes at 60 fps on the floor device. Profiled, not assumed. |
| **M3** | Depth | 4 classes with passives + actives, cooldown/mass UI, biomes, obstacles, currents, void pits, anti-snowball | Every class is fun for 5 minutes, and a small snake can kill a big one |
| **M4** | Feel | All of §10, audio, haptics, camera polish, death card, class select, menus | A stranger plays it for 10 minutes without being told anything |
| **M5** | Ship-ready | Profile save, XP/levels, skins, missions, settings, back-button handling, pause, icon, store assets | Passes a 30-minute soak on the floor device with no leak, no crash, stable frame time |

**Not in v1 (`[LATER]`, in rough priority order):** online multiplayer, global leaderboards, team modes, IAP/ads, procedural maps, replays, iOS.

---

## 14. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| 34 snakes drops below 60 fps on the floor device | Kills the whole design | Profile at M2, *before* content. The MultiMesh + spatial-hash design exists for this. Fallback: reduce to 24 snakes and shrink the arena — tune `data/balance.tres`, no code change. |
| Joystick feels worse than finger-follow | Core control complaint | Build both behind a settings toggle at M0; the joystick is default. Ship the loser as an option. |
| Classes are unbalanced / one dominates | Skill pillar collapses | All costs and cooldowns live in `.tres`. Instrument kills-per-class-per-hour from M3 and tune on data, not vibes. |
| Bots feel robotic or unfair | Offline pillar collapses | Reaction delay + limited perception + no stat cheating are non-negotiable. Playtest blind: if a tester can name which snakes are bots, iterate. |
| .NET Android export proves unstable | Blocks every device gate | Prove it in `M0-03`, before any gameplay exists. Fallback is GDScript-only with a GDExtension escape hatch for the hash and collision — the pure `Coil.Sim` boundary keeps that a contained swap. |
| C#/GDScript interop cost creeps in | Frame budget blown by marshalling | Only three sanctioned crossings (ARCHITECTURE §3), all bulk data stays in C#. A fourth crossing is a design discussion, not a patch. |
| Scope creep from `[LATER]` | Slice never ships | Anything marked `[LATER]` is closed to discussion until M5 is done. |

---

## 15. Resolved decisions

Everything that was open at v1.0 is now decided. Rationale and revisit conditions for all of them live in [`DECISIONS.md`](./DECISIONS.md).

| Was open | Decided | Revisit if |
|---|---|---|
| Self-collision | **Off** — your own body is harmless (D-13) | Players exploit self-coiling as safe parking at M2 |
| Border: kill or bounce | **Lethal**, with the 1.5 s warning (D-14) | Border deaths dominate first sessions at M4 |
| Warden's Snare symmetry | **Symmetric** — it slows the caster too (D-15) | Warden's kill rate trails the others by >25% at M3 |
| Arena population | **34**, tuned from `balance.tres` | M2-13 profiling says otherwise. This is the release valve for performance. |
| Bot names | Curated pool, no user-generated names in v1 | Never in v1 — no moderation capacity |

## Appendix A — Tuning constants

Everything here lives in `data/balance.tres` and is hot-editable. These are **starting values**, not truths.

```
# Movement
SPEED_BASE            220.0   u/s
SPEED_BOOST           380.0   u/s
OMEGA_NUMERATOR       900.0   -> ω = clamp(900/radius, 2.0, 4.5) rad/s
PATH_STEP               6.0   u

# Size
MASS_START             10.0
RADIUS_BASE            14.0
RADIUS_EXP              0.25
RADIUS_MAX             46.0
LENGTH_BASE            60.0
LENGTH_PER_MASS         1.6

# Economy
BOOST_DRAIN             9.0   mass/s
BOOST_DROP_INTERVAL     0.35  s
BOOST_DROP_MASS         4.0
BOOST_MIN_MASS         25.0
PELLET_MASS             1.0
CORPSE_CHUNK_MASS       6.0
CORPSE_RETURN_RATIO     0.70
BOUNTY_RETURN_RATIO     1.00
MAGNET_BASE            45.0
MAGNET_PER_RADIUS       1.5
LEADER_DECAY_RATE       0.004 /s above threshold
LEADER_DECAY_THRESHOLD  500.0

# World
ARENA_RADIUS         3500.0   u
BORDER_WARN_RADIUS   3350.0   u
SNAKE_COUNT            34
PELLET_TARGET        2600
CELL_SIZE              96.0   u
GRACE_DURATION          2.0   s

# Sim
TICK_RATE              60     Hz
MAX_CATCHUP_TICKS       5
BOT_STEER_INTERVAL      4     ticks (12 when LOD-culled)
BOT_PERCEPTION_RADIUS 1400.0  u
```

## Appendix B — Glossary

- **Mass** — the single scalar defining a snake's size. Drives radius, length, turn rate, and score.
- **Path** — the arc-length-resampled history of head positions; the body's ground truth.
- **InputCommand** — `{heading: float, boost: bool, cast: bool}`. The only channel through which any actor (player, bot, future network peer) affects the world.
- **Cut-off** — steering across an opponent's projected path so their head must hit your body. The game's fundamental offensive act.
- **Slice** — the v1 vertical slice defined by this document.
