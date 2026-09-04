# COIL — Decision log

Every locked decision, why it was made, and what would make us revisit it. The spec says *what*; this says *why*, which is the thing that gets lost.

**Format:** decision, the context that forced it, what it costs, and the signal that would reopen it.

---

### D-01 · Godot 4.7 (.NET) as the engine
**Context.** Top-down 2D .io game, solo developer, Android target, no licensing budget.
**Cost.** Smaller ecosystem than Unity; fewer answers when something breaks; .NET on Android is Godot's least-travelled export path.
**Amended in `M0-20`.** Originally locked to 4.5. The engine packaged for the development machine is 4.7.2, and Godot substitutes its own `GodotSharp` at load time regardless of the pinned `PackageReference` — so a 4.5 pin against a 4.7 engine compiles clean and then diverges at runtime, invisibly, until code touches an API that moved. The pin now tracks the engine exactly.
**Revisit if.** The .NET Android export proves unstable in `M0-03` — the fallback is GDScript-only, which changes D-07, not the engine. A future engine upgrade repeats `M0-20`: bump the three pins and `config/features` together, never one without the others.

### D-02 · Offline bots first, online later
**Context.** Wanted a playable game without server infrastructure or hosting costs, and offline is a genuine mobile advantage.
**Cost.** Bot AI is real work that a multiplayer game wouldn't need. Some of it (`Coil.Agents`) carries over to the server; none of it is wasted.
**Revisit if.** Never for v1. Online is post-M5 and the seams in `ARCHITECTURE.md` exist to make it a port, not a rewrite.

### D-03 · Endless arena, not rounds
**Context.** Purest slither.io feel, lowest onboarding cost, no matchmaking or round-state to build.
**Cost.** No climax, no win screen, weaker session-end hook. Mitigated by the leader bounty and the death card.
**Revisit if.** Playtests at M4 show sessions ending in boredom rather than death. Shrinking-zone rounds are the alternative.

### D-04 · Floating joystick, finger-follow as an option
**Context.** Precision matters once abilities exist; finger-follow makes accidental input costly on a small screen.
**Cost.** Occludes part of the screen; higher learning curve than touch-to-steer.
**Revisit if.** `M0-15` ships both. If testers prefer finger-follow at M4, swap the default — it is a settings change, not a code change.

### D-05 · Class loadout, chosen pre-match
**Context.** The chosen fix for slither.io's flat skill ceiling. Pre-match choice keeps opponents readable.
**Cost.** Balance debt: four classes must stay fair against each other forever. `M3-17` exists to make that data-driven.
**Revisit if.** One class exceeds ~35% of kills after tuning twice.

### D-06 · Flat vector pastel
**Context.** Readability is a design pillar, and 34 snakes on a phone screen is a legibility problem before it is an art problem.
**Cost.** Less distinctive than an organic style. Accepted — it is also the cheapest to produce and the cheapest to render.

### D-07 · Hybrid C# / GDScript from day one
**Context.** The tick loop is the project's single largest risk. C# gives 5–15× on hot loops, real structs, and a compiler that can enforce architecture.
**Consequence.** **Bots must also be C#.** A GDScript bot calling the spatial hash five times per steer would cross the interop boundary ~40× per tick and hand back the entire gain. The split is: simulation, agents and bulk renderers in C#; UI, menus, VFX orchestration and player input in GDScript.
**Cost.** ~20 MB of .NET runtime in the APK (see D-17), a slower edit-run cycle than pure GDScript, and one boundary to be disciplined about.
**Revisit if.** `M0-03` cannot produce a working .NET Android build. Fallback is GDScript-only with a GDExtension escape hatch.

### D-08 · Landscape only
**Context.** The two-thumb layout (joystick left, buttons right) needs the width, and horizontal view distance is what makes cut-offs readable.
**Cost.** Loses the one-handed commute audience. Portrait would roughly double the HUD work (`M4-10`, `M4-14`).
**Revisit if.** Store analytics post-launch show portrait demand. It is a v2 feature, not a v1 toggle.

### D-09 · Floor device: mid-range Android, 2021 or newer
**Context.** The device the perf gates are measured on. All targets in spec §11.5 are calibrated to it.
**Cost.** Nothing below that tier is supported at 60 fps. `SNAKE_COUNT` is the release valve.

### D-10 · Branch per task → GitHub PR
**Context.** 102 tasks and a perf-sensitive tick loop. A pre-merge diff is the review point, and `git bisect` needs clean, single-purpose commits.
**Cost.** PR ceremony on a solo project. Accepted because reviewability was an explicit goal.

### D-11 · Test-first for `Coil.Sim` only
**Context.** Simulation bugs are invisible and expensive; UI bugs are obvious and cheap. Tests are spent where they pay.
**Cost.** No regression net on presentation. Accepted — that is what the on-device build in the definition of done is for.

### D-12 · Strict C# build + NetArchTest
**Context.** The `sim/` purity rule is the load-bearing architectural constraint. A rule enforced only by review is a rule that expires.
**Cost.** `TreatWarningsAsErrors` bites during refactors. Worth it: the compiler now owns the architecture.

### D-13 · Self-collision off
**Context.** At mobile zoom you frequently cannot see your own tail. Dying to it reads as the game cheating, not as a mistake.
**Cost.** Removes a classic skill expression; coiling carries no risk.
**Revisit if.** M2 playtests show players exploiting self-coiling as a safe parking strategy.

### D-14 · Lethal border
**Context.** Keeps the edge hostile, prevents safe edge-farming, and preserves wall-pinning as an offensive tactic.
**Cost.** A common cheap death for new players. Mitigated by the 1.5 s warning in §4.4.
**Revisit if.** M4 stranger tests show border deaths dominating first sessions.

### D-15 · Warden's Snare is symmetric
**Context.** "An active never grants a kill" (§6). A field only your victim suffers is close to a guaranteed kill.
**Cost.** Warden may feel sluggish to play.
**Revisit if.** Warden's kill rate trails the other three by more than 25% at M3. The tuned middle ground is ×0.85 for the caster.

### D-16 · CI runs build + xUnit + architecture rules on every PR
**Context.** The sim tests are pure .NET — no engine boot — so CI is fast enough to be worth blocking on.
**Cost.** Actions minutes and one workflow file. Godot export is **not** in CI: export templates make it slow, and the device build in the definition of done covers it.

### D-17 · APK budget re-baselined: 45 MB → 65 MB
**Context.** A direct consequence of D-07. The .NET runtime adds roughly 20 MB that no amount of asset discipline recovers.
**Cost.** A larger download. Mitigated by exporting **arm64-only** and enabling assembly trimming in `M5-10`.
**Revisit if.** The store install-conversion rate looks size-sensitive. Dropping to GDScript-only is the only real lever, and it is not worth it.

### D-18 · Engine-facing types live in the game assembly, their logic does not
**Context.** Godot registers C# script types **only from the main project assembly**. A `Node` or `Resource` subclass compiled into a referenced library cannot be attached to a scene or resource: Godot answers *"the associated class could not be found"*. Verified in `M0-07` with a controlled A/B — an identical `Resource` subclass instantiates from `Coil.csproj` and fails from `Coil.Presentation.dll`. It is not a path bug; the generator's `res://` path was corrected first (via `GodotProjectDir`, which must be set before `Sdk.props` derives `GodotProjectDirBase64`) and the class still would not resolve.

This contradicted ARCH §2, which placed `ArenaHost`, `SnakeRenderer`, `PelletRenderer` and `BalanceLoader` in `src/Coil.Presentation/`.

**Decision.** The engine-facing *shell* — the `Node` or `Resource` subclass, and any call into a Godot static like `ResourceLoader` — lives in the game assembly. The logic it drives stays in `Coil.Presentation` as a plain class the shell owns and calls. `ArenaHostNode : Node` in the game assembly, `Coil.Presentation.ArenaHost` doing the work.

**Cost.** One shim per engine-facing type, and a boundary that is easy to erode by letting logic drift into the shell. The alternatives were worse: compiling the presentation sources into the game assembly deletes `Coil.Presentation.dll` and the four-project layout `M0-01` established, and moving the layer wholesale gives up the separation ARCH §2 exists to express.

**Revisit if.** Godot gains script registration from referenced assemblies — the engine issue to watch is script type resolution across assemblies. At that point the shells collapse back into `Coil.Presentation` with no change to the logic, which is the point of keeping it out of them.

**Already following it.** `BalanceData` and `BalanceLoader` (`M0-07`). Both are shell by nature: one is a `Resource`, the other is a `ResourceLoader` call, and there is no logic to separate out.
