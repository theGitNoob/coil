---
name: coil-next
description: Pick up and execute the next task in the COIL game build roadmap, end to end — orient, branch, implement test-first, verify, commit, open the PR. Use this whenever the user says to start the next task, continue building the game, work on a specific task ID like M0-03 or M1-07, "pick up where we left off", "keep going", "let's build", or asks what to do next and then wants it done. Also use it when starting implementation work on this repo at all, so the task contract and the definition of done are loaded before any code is written.
---

# COIL — run the next task

Execute one roadmap task from branch to open PR. One task, one branch, one PR — the discipline exists so that every diff is small enough to actually review, and so `git bisect` can find the commit that cost 4fps.

Context that governs this work: `CLAUDE.md`, `docs/ROADMAP.md`, `docs/GAME_SPEC.md`, `docs/ARCHITECTURE.md`, `docs/CONVENTIONS.md`.

## 1 · Orient

```bash
python3 tools/status.py
```

This gives the next task, or the task belonging to the current branch if one is checked out. If the user named a task ID, use theirs instead — but still run the tool, because it will tell you if the task's phase isn't reachable yet.

If a branch is already open for a task, continue it rather than starting a new one. Two half-finished tasks are worse than one finished one.

## 2 · Load the contract

Read, before writing anything:

- The task's roadmap row — the **Done when** is the acceptance test, verbatim.
- The spec section it cites (`§4.2`, `§8.1`…). The numbers in the spec are the numbers to implement; do not re-derive them.
- The relevant part of `ARCHITECTURE.md` if the task touches the simulation, the interop boundary, or rendering.

Then restate the contract back to the user in four lines — task, spec section, done-when, files — before touching code. This is the cheapest possible moment to discover you're about to build the wrong thing.

**If the task looks bigger than its size band** (S ≈ 1–2h, M ≈ half a day), say so now and propose a split into new task IDs. A task that outgrows its band is the single most reliable signal that the breakdown was wrong, and pushing through it produces exactly the unreviewable diff this whole process exists to prevent.

## 3 · Branch

```bash
git checkout main && git pull
git checkout -b m1-06-body-insertion    # <task-id>-<short-slug>, lowercase
```

## 4 · Implement

**Test-first for anything in `Coil.Sim`** — write the failing test, watch it fail, then implement. The simulation is where bugs are invisible: a subtly wrong turn rate looks fine and ruins the game's feel three phases later. Presentation code (`ui/`, VFX, menus) is verified by eye on the device instead, which is why it has no tests.

Name tests for the behaviour they pin: `PathBuffer_AtVariableDeltaTime_KeepsConstantSpacing`. Cite the spec section in a comment when the expected value comes from the spec.

Hold the line on these while writing — they are the constraints that make the frame budget possible, and retrofitting them later means touching forty call sites:

- Nothing in the tick path allocates. No `new`, no LINQ, no closures, no boxing, no string work.
- `Coil.Sim` references no Godot node type, no scene tree, no input, no file I/O.
- Tunables go to `data/balance.tres` → `SimConfig` → spec Appendix A, in the same commit. A literal number in simulation code is a bug.
- Bulk data does not cross the C#/GDScript boundary. There are three sanctioned crossings; a fourth is a design discussion, not a patch.

**Scope discipline.** If you find a real problem outside this task, note it and propose a new task ID — do not fix it here. Widening a task is how a 40-line diff becomes a 400-line one that nobody reviews properly. The exception is a genuine blocker: if the task cannot be completed without the fix, say so and fold it in explicitly.

## 5 · Verify

Verification is what "done" means here — a claim without a check is not a status report, it's a guess.

```bash
dotnet test                 # xUnit + NetArchTest architecture rules
./tools/deploy.sh           # build, install and launch on the device
```

- If the tick loop was touched, measure the delta and record it in `docs/PERF_LOG.md`. "Didn't measure" is only acceptable where the task cannot affect it.
- If the task's Done when names an observable outcome, observe it. Actually run it on the phone.
- If something fails, report it with the output. A failing test reported as a passing one costs far more than the delay.

## 6 · Land it

```bash
git commit -m "feat(sim): insert body capsules into the spatial hash (M1-06)"
gh pr create --title "M1-06 · Body insertion" --body "$(cat <<'EOF'
Task:      M1-06 · Body insertion
Spec:      §5 Collision & world queries
Done when: <verbatim from the roadmap>

How to verify on device:
  1. ...

Perf delta: tick 4.1ms → 4.3ms (budget 6ms)
EOF
)"
```

Conventional commit, scope is the layer (`sim`, `agents`, `render`, `ui`, `build`), task ID in parentheses at the end. **The ID is load-bearing** — `tools/status.py` derives project completion from it, so a commit without it makes the task invisible to every future status check.

## 7 · Report back

Three things, briefly: what landed, how to verify it on the device in under a minute, and what the next task is. If anything was left out or deferred, say so explicitly rather than letting it be discovered later.

## When to stop and ask

Push through ordinary judgement calls — that's the job. Stop and ask when:

- The spec is genuinely ambiguous and two readings produce materially different games. Quote both readings.
- The task is blocked by missing tooling (no Godot, no device, no Android SDK). Say what's missing and what it blocks.
- A gate-critical (⚠) task has just failed its criterion. `M0-03` failing means the stack decision is wrong and D-07 needs reopening — that is the user's call, not a thing to work around quietly.
