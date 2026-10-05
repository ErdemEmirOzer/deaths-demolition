#!/usr/bin/env python3
"""WP-089 (C-00 05.10, decision (b) on the 30.09 rule) · may an EDITOR-stage guard restore that exited 32 pass?

32 = the stage's own (editor) area restored and verified, another area changed. It passes — with a warning line — only
when all three hold:
  1. every out-of-scope row is in the PLAYER area (`player  <sha|MISSING>  <path>`);
  2. every changed path is attributed (the window's attribution.txt) to Players, and each such Player is part of ANOTHER
     guard window: a players-lineage.tsv row there with the same pid and start time and owned=1 (this window's own
     lineage gives the start time; a pid seen only here is unattributed);
  3. each such owning window's restore is verified=1 (the last summary line of its restore.txt). A window still open
     is waited for (≤ --wait s, default 600), then counts as not verified.
Anything else → FAIL (the caller stops, tell C-00). Player stages never come here: there 32 is a failure.
Owning windows are found under the roots (default: the KADRO project; heavy folders pruned) by their window.json.

usage: python3 tools/qa/oos_rule.py <ug dir> [--root DIR]… [--wait S]   exit 0 pass (reason on stdout) · 1 fail · 64 usage
"""
import argparse
import os
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402

PRUNE = {"Library", "Temp", ".git", "Logs", "obj", "node_modules", "asset-production", "Packages", "Assets", "snap",
         "ProjectSettings", "ci-worktree", "driver"}
PID = re.compile(r"\bpid (\d+)\b")
BAD_NL = "$'\\n'"   # guard 52b7a00f..: the file ended in this literal instead of a newline (C-00 05.10) — read it too


def oos_rows(text):
    """the rows of an out-of-scope-changed file, the literal $'\\n' of the faulty guard read as a line break."""
    return [r for r in text.replace(BAD_NL, "\n").splitlines() if r.strip()]


def latest(path):
    """the file or its highest .N retry copy (the guard publishes later attempts as <name>.N)."""
    copies = [(int(p.suffix[1:]), p) for p in path.parent.glob(path.name + ".*") if p.suffix[1:].isdigit()]
    return max(copies)[1] if copies else path


def lineage(path):
    """[(pid, lstart, owned)] of a players-lineage.tsv (utc, pid, ppid, lstart, owned)."""
    rows = []
    try:
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            cols = line.split("\t")
            if len(cols) == 5:
                rows.append((cols[1], cols[3], cols[4] == "1"))
    except FileNotFoundError:
        pass
    return rows


def windows(roots, own):
    for root in roots:
        for d, dirs, files in os.walk(root):
            dirs[:] = [x for x in dirs if x not in PRUNE and not x.startswith("worktree")]
            if "window.json" in files and Path(d).resolve() != own:
                yield Path(d)


def owner_window(pid, start, roots, own):
    for w in windows(roots, own):
        if any(p == pid and s == start and owned for p, s, owned in lineage(w / "players-lineage.tsv")):
            return w
    return None


def verified(window):
    try:
        lines = [l for l in (window / "restore.txt").read_text(encoding="utf-8", errors="replace").splitlines()
                 if "userdata-guard restore scope=" in l]
    except FileNotFoundError:
        return None   # not restored yet
    return bool(lines) and " verified=1" in lines[-1]


def decide(ug, roots, wait=600.0, sleep=time.sleep, clock=time.monotonic):
    ug = Path(ug).resolve()
    rows = oos_rows(latest(ug / "out-of-scope-changed.txt").read_text(encoding="utf-8")) \
        if latest(ug / "out-of-scope-changed.txt").is_file() else []
    if not rows:
        return False, "32 without out-of-scope rows"
    off = [r for r in rows if not r.startswith("player  ")]
    if off:
        return False, "condition 1: a change outside the player area: %s" % off[0]
    changed = {r.split("  ", 2)[2].replace(" (launch-counter keys excluded)", "") for r in rows}
    attribution = {}
    try:
        for line in (ug / "attribution.txt").read_text(encoding="utf-8", errors="replace").splitlines():
            path, _, rest = line.partition("\t")
            attribution[path] = PID.findall(rest)
    except FileNotFoundError:
        return False, "condition 2: no attribution.txt"
    starts = {p: s for p, s, _ in lineage(ug / "players-lineage.tsv")}
    owners = {}
    for path in sorted(changed):
        pids = attribution.get(path, [])
        if not pids:
            return False, "condition 2: %s is not attributed to a Player" % path
        for pid in pids:
            if pid not in starts:
                return False, "condition 2: pid %s (writer of %s) is not in this window's lineage" % (pid, path)
            w = owner_window(pid, starts[pid], roots, ug)
            if w is None:
                return False, "condition 2: pid %s (writer of %s) belongs to no guarded window" % (pid, path)
            owners[w] = pid
    deadline = clock() + wait
    for w in sorted(owners):
        while True:
            v = verified(w)
            if v is not None or clock() >= deadline:
                break
            sleep(10)
        if v is not True:
            return False, "condition 3: the writer window %s is %s" % (w, "not restored yet" if v is None else "not verified=1")
    return True, "player area only, writers = guarded windows verified=1: " + ", ".join(
        "pid %s → %s" % (pid, w) for w, pid in sorted(owners.items()))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("ug")
    ap.add_argument("--root", action="append")
    ap.add_argument("--wait", type=float, default=600.0)
    try:
        args = ap.parse_args(argv)
    except SystemExit as e:
        return 0 if e.code == 0 else 64
    if not Path(args.ug).is_dir():
        print("no guard window: %s" % args.ug, file=sys.stderr)
        return 64
    ok, why = decide(args.ug, [Path(r) for r in (args.root or [str(dd_paths.KPROJ)])], args.wait)
    print(why)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
