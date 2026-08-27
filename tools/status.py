#!/usr/bin/env python3
"""COIL project status.

Completion is derived from git history, not from a checklist file, so there is
no second source of truth to drift. A task is done when a commit on the trunk
carries its ID in parentheses -- e.g. "feat(sim): ... (M1-06)".

Usage:  python3 tools/status.py [--next] [--phase M2]
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ROADMAP = ROOT / "docs" / "ROADMAP.md"
ROW = re.compile(r"^\|\s*\*\*([A-Z][0-9]?-[0-9]{2})\*\*\s*\|")
PHASE = re.compile(r"^##\s+(M[0-5])\s+·\s+(.+)$")
TRACK = re.compile(r"^###\s+(Asset track|Automated checks)")
GATE = re.compile(r"^\*\*Phase gate:\*\*\s*(.+)$")


def git(*args):
    try:
        out = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True)
        return out.stdout.strip() if out.returncode == 0 else None
    except FileNotFoundError:
        return None


def parse_roadmap():
    phases, current = [], None
    for line in ROADMAP.read_text().splitlines():
        m = PHASE.match(line)
        if m:
            current = {"id": m.group(1), "name": m.group(2).strip(), "gate": "", "tasks": []}
            phases.append(current)
            continue
        m = TRACK.match(line)
        if m:
            name = m.group(1)
            current = {"id": "A" if "Asset" in name else "C", "name": name, "gate": "", "tasks": []}
            phases.append(current)
            continue
        m = GATE.match(line)
        if m and current:
            current["gate"] = m.group(1).strip()
            continue
        m = ROW.match(line)
        if m and current:
            parts = [p.strip() for p in line.split("|")]
            tid, title, size = parts[1].strip("* "), parts[2], parts[-2]
            files = parts[3] if len(parts) >= 7 else ""
            done = " | ".join(parts[4:-2]) if len(parts) >= 7 else parts[3]
            risk = "⚠" in title or "⚠" in done
            current["tasks"].append({
                "id": tid, "title": title.replace("⚠", "").strip(), "files": files,
                "done_when": done.replace("⚠", "").strip(), "size": size, "risk": risk,
            })
    return [p for p in phases if p["tasks"]]


def completed_ids():
    """IDs mentioned in trunk commit subjects. Falls back to HEAD before main exists."""
    ref = "main" if git("rev-parse", "--verify", "main") else "HEAD"
    log = git("log", "--format=%s", ref)
    if not log:
        return set()
    return set(re.findall(r"\(([A-Z][0-9]?-[0-9]{2})\)", log))


def bar(done, total, width=20):
    filled = round(width * done / total) if total else 0
    return "█" * filled + "░" * (width - filled)


def main():
    if not ROADMAP.exists():
        sys.exit("docs/ROADMAP.md not found — run from the repo root.")

    phases = parse_roadmap()
    is_repo = git("rev-parse", "--is-inside-work-tree") == "true"
    done = completed_ids() if is_repo else set()
    branch = git("rev-parse", "--abbrev-ref", "HEAD") if is_repo else None
    dirty = git("status", "--porcelain") if is_repo else None

    all_tasks = [t for p in phases for t in p["tasks"]]
    known = {t["id"] for t in all_tasks}
    done &= known
    # Rows whose size column is a tick are pointers to work already landed elsewhere.
    done |= {t["id"] for t in all_tasks if t["size"] == "✓"}
    todo = [t for t in all_tasks if t["id"] not in done]

    # A task is in progress when the checked-out branch is named for it.
    active = None
    if branch:
        for t in all_tasks:
            if branch.lower().startswith(t["id"].lower() + "-"):
                active = t
                break

    print("COIL — project status")
    print("=" * 21)
    if not is_repo:
        print("Not a git repository yet. Completion tracking starts at M0-02.")
    else:
        state = f"task {active['id']}, in progress" if active else "no task branch"
        print(f"Branch:       {branch}  ({state})")
        n = len(dirty.splitlines()) if dirty else 0
        print(f"Working tree: {'clean' if n == 0 else f'{n} file(s) changed'}")

    print("\nPhase progress")
    current_phase = None
    for p in phases:
        d = sum(1 for t in p["tasks"] if t["id"] in done)
        marker = ""
        if current_phase is None and d < len(p["tasks"]) and p["id"] not in ("A", "C"):
            current_phase, marker = p, "  ← current"
        print(f"  {p['id']:<3} {p['name'][:16]:<17} [{bar(d, len(p['tasks']))}] {d:>3}/{len(p['tasks'])}{marker}")
    print(f"  {'':<3} {'Overall':<17} [{bar(len(done), len(all_tasks))}] {len(done):>3}/{len(all_tasks)}")

    nxt = active or (todo[0] if todo else None)
    if nxt:
        print(f"\nNext task: {nxt['id']} · {nxt['title']}  ({nxt['size']})")
        if nxt["files"]:
            print(f"  Files:     {nxt['files']}")
        print(f"  Done when: {nxt['done_when']}")
        if nxt["risk"]:
            print("  ⚠ Gate-critical — do not defer this one.")

    if current_phase:
        remaining = sum(1 for t in current_phase["tasks"] if t["id"] not in done)
        print(f"\nGate {current_phase['id']}: BLOCKED — {remaining} task(s) remaining")
        print(f'  "{current_phase["gate"]}"')

    risks = [t["id"] for t in todo if t["risk"]]
    if risks:
        print(f"\nGate-critical tasks outstanding: {', '.join(risks)}")

    print(f"\nNEXT={nxt['id'] if nxt else 'none'}")


if __name__ == "__main__":
    main()
