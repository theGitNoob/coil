```
Task:      <ID> · <Title>
Spec:      §<n> <Section name>
Done when: <the roadmap's Done when, verbatim>

How to verify on device:
  1. …

Perf delta: tick <before>ms → <after>ms (budget 6ms)
```

<!--
The block above is CONVENTIONS §5, verbatim — keep it. The perf line is not
optional on anything touching the tick loop; "didn't measure" is a valid answer
only on tasks that cannot affect it.
-->

### Definition of done — CONVENTIONS §6

- [ ] Runs **on the phone**, not just in the editor
- [ ] Anything in `Coil.Sim` has a test, written first
- [ ] `dotnet test` passes, architecture rules included
- [ ] No new allocations in the tick loop
- [ ] New tunables in `balance.tres` → `SimConfig` → spec Appendix A
- [ ] One conventional commit, task ID in parentheses at the end
- [ ] Perf delta recorded above if the tick loop was touched

<!--
One task = one branch = one PR, squash-merged. A PR touching two task IDs gets
split. The ID in the commit subject is what tools/status.py reads — without it
the task is invisible to every future status check.
-->
