# COIL — Code conventions

How code in this repo is written. Structural rules — what may depend on what — are in `ARCHITECTURE.md`.

The short version: **match the surrounding code.** A file that reads like the ones around it is worth more than a file that is individually clever.

---

## 1. C#

### Build settings — all projects

```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<AnalysisLevel>latest-recommended</AnalysisLevel>
<LangVersion>latest</LangVersion>
```

Warnings are errors. If a warning is genuinely wrong, suppress it **at the line** with a comment explaining why — never at the project level.

### Style

- File-scoped namespaces. One public type per file, named after the file.
- `sealed` by default. Inheritance requires a reason written in a comment; prefer composition (see `Steering/` — behaviours compose, they don't subclass).
- `private readonly` fields prefixed `_`. Public members are `PascalCase`. Locals and parameters `camelCase`.
- `var` only when the type is obvious from the right-hand side. Never for numeric types — `float x = 0f`, not `var x = 0f`.
- Float literals always carry the suffix: `0.35f`, never `0.35`. A silent `double` promotion in the tick loop is a real bug.
- Braces always, even on one-line `if`. Early return over nesting.
- No regions. No `#if` blocks outside build-configuration guards.

### Naming

The same concept carries the same name in C#, GDScript and the spec. `mass` is never `size` or `score`. `heading` is never `angle` or `rotation`. `PATH_STEP` is `PathStep` in C# and `PATH_STEP` in `SimConfig` — same word, appropriate casing. When you rename a concept, rename it in the spec too.

### The tick loop

Everything in §5 of `ARCHITECTURE.md` applies. The short list: no allocation, no LINQ, no exceptions, no dictionaries, index with `for`.

Outside the tick loop — loading, UI backing logic, tests — write normal, readable C#. Optimising cold code is how a codebase becomes unreadable for nothing.

### Comments

Comment **why**, never what. The exception is a non-obvious formula, which gets a one-line note tying it to its spec section:

```csharp
// Turn radius grows with size — spec §4.2. This is the primary counterweight to mass.
float omegaMax = Math.Clamp(_config.OmegaNumerator / radius, 2.0f, 4.5f);
```

No commented-out code. Git remembers it.

---

## 2. GDScript

- **Static typing everywhere.** `var speed: float = 0.0`, typed parameters, typed returns. An untyped GDScript declaration will be flagged in review.
- `snake_case` for files, functions and variables. `PascalCase` for class names. `_leading_underscore` for private.
- Node access via `@export` references or unique names (`%JoystickRoot`), never `get_node("../../Foo")` chains.
- `_process` holds presentation only — interpolation, tweens, animation. Never game logic, never a rule.
- Signals are named for what happened, past tense: `snake_died`, `ability_cast`. Not `on_death`.
- No business logic in GDScript at all. If a rule of the game is being written in `ui/`, it belongs in `Coil.Sim`.

---

## 3. Tests

xUnit, in `tests/Coil.Sim.Tests`. Test-first for anything in `Coil.Sim` (D-11).

- Name: `Method_Scenario_ExpectedResult` — `PathBuffer_AtVariableDeltaTime_KeepsConstantSpacing`.
- Arrange / Act / Assert, separated by blank lines. One behaviour per test.
- Assert on behaviour and invariants, not on implementation details. A refactor that keeps behaviour must not break a test.
- Every test cites its spec section in a comment when the expected value comes from the spec.
- No `Thread.Sleep`, no randomness without an explicit seed, no test that depends on another test.
- The full suite must stay under 10 seconds. It runs on every commit; a slow suite is a suite that gets skipped.

---

## 4. Data and tuning

- Every tunable number lives in `data/balance.tres` and reaches the simulation through `SimConfig`.
- Class definitions, personas, biomes and palettes are `.tres` resources — data, never code.
- Adding a tunable means adding it to `balance.tres`, `SimConfig`, and Appendix A of the spec. All three, in the same commit.

---

## 5. Git

**One task ID = one branch = one PR.**

```bash
git checkout -b m1-06-body-insertion
# ... work ...
git commit -m "feat(sim): insert body capsules into the spatial hash (M1-06)"
gh pr create --title "M1-06 · Body insertion" --body "..."
```

- Conventional commits: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `chore`. Scope is the layer — `sim`, `agents`, `render`, `ui`, `build`.
- The task ID goes at the end of the subject line, in parentheses. That is what makes `git log --grep "M1-"` useful.
- Squash-merge to `main`. `main` is always deployable to the phone.
- One task per PR. A PR that touches two task IDs gets split.

### PR body template

```
Task:      M1-06 · Body insertion
Spec:      §5 Collision & world queries
Done when: <the roadmap's Done when, verbatim>

How to verify on device:
  1. …

Perf delta: tick 4.1ms → 4.3ms (budget 6ms)
```

The perf line is not optional on anything touching the tick loop. "Didn't measure" is a valid answer only on tasks that cannot affect it.

---

## 6. Definition of done

Repeated here because it is the thing most likely to get skipped under time pressure. All seven, every task:

1. The game runs **on the phone** — not just in the editor.
2. Anything in `Coil.Sim` has a test, written first.
3. Architecture tests pass (`dotnet test`).
4. No new allocations in the tick loop.
5. New tunables are in `balance.tres`, not in code.
6. One conventional commit with the task ID.
7. Perf delta recorded if the tick loop was touched.

---

## 7. Review checklist

What to look for when reviewing a PR — in this order, because the first three are the expensive ones to fix later:

1. **Layering** — did anything leak into `Coil.Sim` that shouldn't be there?
2. **Allocation** — anything in the tick path that allocates, boxes, or captures?
3. **Spec fidelity** — does the behaviour match the cited section, including the numbers?
4. Magic numbers that belong in `balance.tres`.
5. Tests that assert implementation instead of behaviour.
6. Scope: is this one task, or did a second one sneak in?
7. Can you verify it on the device in under 60 seconds? If not, the task lacked an observable outcome.
