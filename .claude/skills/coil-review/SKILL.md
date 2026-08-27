---
name: coil-review
description: Review a completed COIL task against its spec section, the architecture rules and the definition of done, before it merges. Use this whenever the user asks to review a task or PR on this repo, check work before merging, verify a task is actually finished, asks "is this done" or "does this match the spec", or has just finished implementing a task and wants it checked. Also use it when reviewing someone else's diff on the game repo, so the review is anchored to the project's own rules rather than to generic code-review habits.
---

# COIL — review a task

Decide one thing: **does this diff satisfy the task's contract without breaking the rules the project runs on?** Not "is this good code" in the abstract — the roadmap's *Done when* is the acceptance test, and the spec supplies the numbers.

## 1 · Establish the contract before reading the diff

Read these first, in this order, so the diff is judged against something rather than against taste:

1. The task's row in `docs/ROADMAP.md` — **Done when**, files, size band.
2. The spec section it cites in `docs/GAME_SPEC.md` — including the exact constants.
3. `docs/ARCHITECTURE.md` §5 (performance) and §9 (enforcement) if the simulation is touched.

## 2 · Get the diff and run the checks yourself

```bash
gh pr diff <n>                    # or: git diff main...HEAD
dotnet test                       # never trust the PR body's claim that it passes
```

Run the tests. A review that accepts "tests pass" on assertion is not a review. If the branch touches the tick loop and the PR has no perf delta, that is itself a finding.

## 3 · Look, in this order

The order is deliberate — the first three are expensive to fix after merge, the rest are cheap:

1. **Layering.** Did anything leak into `Coil.Sim` — a Godot node type, the scene tree, input, file I/O, a reference to `Coil.Agents` or `Coil.Presentation`? The architecture tests should catch this; if a change also *edited a test to permit it*, that is the most serious finding available.
2. **Allocation on the tick path.** `new`, LINQ, closures that capture, boxing, `params` arrays, string work, dictionary lookups. These pass tests, look clean, and cost the frame budget.
3. **Spec fidelity.** Does the behaviour match the cited section, numbers included? A turn rate of `4.0` where the spec says `4.5` is a real defect even though nothing fails.
4. **Magic numbers** that belong in `balance.tres`.
5. **Tests that assert implementation rather than behaviour** — they pass today and block every future refactor.
6. **Scope.** One task per PR. Two task IDs in one diff gets split.
7. **Verifiability.** Can you check the *Done when* on the device in under 60 seconds? If not, the task lacked an observable outcome and should say how to verify it.

Then the definition of done, all seven, from `CONVENTIONS.md` §6. The one that gets skipped under time pressure: **it has to run on the phone**, not just build.

## 4 · Findings must cite something

Every finding names the rule it violates — a spec section, an `ARCHITECTURE.md` rule, a `CONVENTIONS.md` rule, or a concrete failure scenario with inputs. A finding that cites nothing is a preference, and preferences dressed as findings are how review turns into noise that people learn to skip.

Severity is about consequence, not confidence:

- **BLOCKING** — breaks the layer rule, allocates on the tick path, contradicts the spec, or fails the Done when.
- **SHOULD FIX** — real but survivable: a magic number, a brittle test, a missing perf note.
- **NOTE** — worth knowing, no action required.

## 5 · Verdict

```
M1-06 · Body insertion — FIX FIRST

Contract:  every 4th path point inserted as a capsule; incremental head-insert
           and tail-evict only; no gaps at max speed  (§5)
Tests:     14 passed, 0 failed · architecture rules pass
Perf:      not recorded — tick loop was touched

BLOCKING
  1. SpatialHash.cs:112 — Query() returns a new List per call, so 34 heads
     allocate 34 lists per tick. ARCHITECTURE §5: the query must write into a
     caller-supplied buffer and return a count.

SHOULD FIX
  2. Snake.cs:88 — capsule stride of 4 is a literal; belongs in SimConfig
     alongside PathStep. CONVENTIONS §4.

NOTE
  3. The gap test only covers straight-line motion. A max-turn-rate case would
     exercise the geometry that actually risks gaps.
```

End with `SHIP` or `FIX FIRST` and nothing else — an ambiguous verdict makes the reviewer's job the author's job.

## Two failure modes to avoid

**Rubber-stamping.** If the diff is clean, say so in one line and ship it. But check the *Done when* literally against the code before concluding that — "looks reasonable" is not the same as "does what the contract says".

**Inventing work.** The task is the task. If you spot something genuinely worth doing outside its scope, list it as a NOTE with a proposed task ID; do not make it a blocking finding on someone else's diff.
