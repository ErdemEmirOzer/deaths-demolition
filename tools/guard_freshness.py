#!/usr/bin/env python3
"""WP-089 (C-00 05.10) · is the canonical driver guard the one on main? (G4 D4 review NIT-1: nothing checked it.)

The canonical guard (DD_GUARD_CANON = K/wp-079/driver/tools/userdata-guard.sh) runs from the CI driver, a detached git
worktree that C-00 moves to main after integrating. This compares the driver's files with main's blobs:
  core  userdata-guard.sh, userdata_attrib.py — a difference means a stale (or locally edited) guard: STALE, the caller
        refuses with 64 before it takes anything (fix: git -C <driver> checkout -q --detach main).
  aux   dd-env.sh, slot.sh — they change on main often; a difference is only a warning (a run is not blocked by every
        tools commit before the driver is synced).
A driver that is not a git checkout with a `main` ref cannot be judged: a warning, never a refusal (the real driver
always is one; fixtures are not).

usage: python3 tools/guard_freshness.py <canonical userdata-guard.sh>
exit: 0 fresh, aux-only difference or unknown (warning lines on stderr) · 64 core stale (one reason line) · 2 usage
"""
import subprocess
import sys
from pathlib import Path

CORE = ("userdata-guard.sh", "userdata_attrib.py")
AUX = ("dd-env.sh", "slot.sh")


def _git(driver, *args):
    r = subprocess.run(["git", "-C", str(driver), *args], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else None


def freshness(guard_path):
    """(state, message): state ∈ fresh | aux | stale | unknown."""
    tools = Path(guard_path).parent
    driver = tools.parent
    if _git(driver, "rev-parse", "--verify", "-q", "main^{commit}") is None:
        return "unknown", "driver %s is not a git checkout with a main ref — guard freshness not checked" % driver
    stale_core, stale_aux = [], []
    for name in CORE + AUX:
        want = _git(driver, "rev-parse", "--verify", "-q", "main:tools/" + name)
        if want is None:
            continue   # not on main (yet): nothing to compare
        f = tools / name
        have = _git(driver, "hash-object", "--", str(f)) if f.is_file() else None
        if have != want:
            (stale_core if name in CORE else stale_aux).append(name)
    fix = "git -C '%s' checkout -q --detach main" % driver
    if stale_core:
        return "stale", "canonical guard on %s differs from main (%s) — refused, nothing taken (64); fix: %s" % (
            driver, ", ".join(stale_core), fix)
    if stale_aux:
        return "aux", "driver %s: %s differ from main (guard itself current) — sync soon: %s" % (driver, ", ".join(stale_aux), fix)
    return "fresh", ""


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) != 1:
        print(__doc__.strip().splitlines()[-3], file=sys.stderr)
        return 2
    state, msg = freshness(argv[0])
    if state == "stale":
        print("guard: " + msg, file=sys.stderr)
        return 64
    if msg:
        print("guard: warning: " + msg, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
