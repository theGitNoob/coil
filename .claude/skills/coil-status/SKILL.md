---
name: coil-status
description: Report where the COIL game project stands — current phase, tasks done and remaining, the next task, gate status, and outstanding gate-critical risks. Use this whenever the user asks about project status, progress, "where are we", "what's left", "what's next", "how far along", how many tasks remain, whether a phase gate is close, or asks for a summary of the build before deciding what to work on. Also use it at the start of any session that resumes work on the game, so the answer comes from the repo rather than from memory.
---

# COIL status

Report the true state of the build. The point of this skill is that the answer comes from the repo — git history and `docs/ROADMAP.md` — not from what was said earlier in the conversation or in a previous session. Memory of "we finished M0-04" is exactly the thing that goes stale.

## How completion is tracked

There is no checklist file. **A task is done when a commit on `main` carries its ID in parentheses** — `feat(sim): insert body capsules into the spatial hash (M1-06)`. That is deliberate: a separate checklist would be a second source of truth, and the two would drift within a week.

Consequence worth knowing: work sitting on an unmerged branch is **not** done, and the status will say so. That is correct — the definition of done requires the merge.

## Procedure

1. Run the status tool. It parses the roadmap and greps trunk history:

   ```bash
   python3 tools/status.py
   ```

2. Add the things the script cannot see, when they're relevant to what was asked:
   - Open PRs: `gh pr list` — work awaiting review is the most common reason status feels wrong.
   - `docs/PERF_LOG.md` — if the current phase has a perf gate, whether the numbers were actually recorded.
   - Uncommitted work: the script reports the count; look at `git status` if the user needs to know what.

3. Report it. Lead with the answer, not the process.

## Report shape

Keep it short — this is a check-in, not a document. Something like:

```
M0 Skeleton · 4/19 · gate blocked

Next: M0-05 · xUnit harness (S)
  dotnet test runs with no engine boot, under 10s, one real test proves the wiring.

In flight: PR #7 (M0-04, strict build settings) — open, unreviewed
Gate-critical still outstanding: M0-03, M0-09, M0-19
```

Then, only if there is something worth saying: one line on what stands between here and the next gate.

## Judgement worth applying

- **Flag drift.** If gate-critical tasks (marked ⚠) are being skipped while easier ones land, say so plainly. The two that must not slip are `M0-03` (does the .NET Android export work at all) and `M0-09` (architecture rules) — everything after them is built on the assumption they passed.
- **Don't editorialise about pace.** Report what is done and what is next. The user knows how fast they are going.
- **If the numbers look wrong**, check whether commits are missing task IDs before concluding work was not done — a malformed commit subject makes a finished task invisible. That is a real failure mode, and the fix is to note it, not to silently correct the count.
