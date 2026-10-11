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
  2b. (WP-099, C-00 08.10) a changed path with NO live Player at its mtime ("no Player alive": the writer was gone
     between samples, or it was another window's own restore putting the plist back) passes with a WARNING only when
     its mtime lies inside the span of ANOTHER player-scope guard window (window.json utc → its restore summary,
     ± SPAN_SLACK s) — that window's Player run or restore is the writer — and condition 3 holds for that window. The
     mtime must also lie inside this window's own span. A change outside every such window still fails.
     R4 (C-00 11.10, S-07 wp101-once-v2): a window that will never restore is no candidate — one with a NOT-RESTORED
     marker (resolved or not), one without a restore summary that the ledger lists as closed, or one whose owner process
     (window.json receipt.owner pid + start) is gone. Of the windows holding the mtime, a restored one wins over an open
     one, verified=1 first, then the tightest span (an abandoned 10.10 window used to shadow the real, verified writer).
Anything else → FAIL (the caller stops, tell C-00). Player stages never come here: there 32 is a failure.
Owning windows are looked up in the guard's window ledger first (G4 D8: L/windows + L/closed records name their DIR;
L = $DD_GUARD_ROOT/ledger, else ~/.dd-userdata-guard of the passwd home), then — windows taken before D8 have no record —
under the roots (default: the KADRO project; heavy folders pruned) by their window.json.

usage: python3 tools/qa/oos_rule.py <ug dir> [--root DIR]… [--ledger L]… [--wait S]   exit 0 pass (reason on stdout) · 1 fail · 64 usage
"""
import argparse
import calendar
import json
import os
import pwd
import re
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402

PRUNE = {"Library", "Temp", ".git", "Logs", "obj", "node_modules", "asset-production", "Packages", "Assets", "snap",
         "ProjectSettings", "ci-worktree", "driver"}
PID = re.compile(r"\bpid (\d+)\b")
MTIME = re.compile(r"\bmtime (\d\d:\d\d:\d\d)Z")
SUMMARY_UTC = re.compile(r"^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ) userdata-guard restore scope=")
SPAN_SLACK = 0   # s (gt S-2, C-00 08.10: was 2 — a 2 s false-PASS band): whole seconds on both sides, the grant comes
                 # before any write of the run and the restore's summary line after its last write
MARKER = "userdata-NOT-RESTORED.txt"   # guard_receipt.MARKER: the window was never restored (.resolved or not)
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


def ledger_dirs(ledgers):
    for ledger in ledgers:
        for sub in ("windows", "closed"):
            for rec in sorted(Path(ledger, sub).glob("*.json")):
                try:
                    d = json.loads(rec.read_text(encoding="utf-8")).get("dir")
                except (ValueError, OSError):
                    continue
                if d and Path(d).is_dir():
                    yield Path(d)


def default_ledgers():
    root = os.environ.get("DD_GUARD_ROOT")
    return [Path(root) / "ledger"] if root else [Path(pwd.getpwuid(os.getuid()).pw_dir) / ".dd-userdata-guard"]


def windows(roots, own, ledgers=()):
    seen = set()
    for d in ledger_dirs(ledgers):
        if d.resolve() != own and d.resolve() not in seen:
            seen.add(d.resolve())
            yield d
    for root in roots:
        for d, dirs, files in os.walk(root):
            dirs[:] = [x for x in dirs if x not in PRUNE and not x.startswith("worktree")]
            if "window.json" in files and Path(d).resolve() != own and Path(d).resolve() not in seen:
                yield Path(d)


def owner_window(pid, start, roots, own, ledgers=()):
    for w in windows(roots, own, ledgers):
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


def utc_epoch(text):
    return calendar.timegm(time.strptime(text, "%Y-%m-%dT%H:%M:%SZ"))


def span(window):
    """(start, end) epoch of a guard window: window.json utc → the utc of its last restore summary line; end None while
    not restored. None without a readable start."""
    try:
        start = utc_epoch(json.loads((window / "window.json").read_text(encoding="utf-8"))["utc"])
    except (OSError, ValueError, KeyError, TypeError):
        return None
    end = None
    try:
        for line in (window / "restore.txt").read_text(encoding="utf-8", errors="replace").splitlines():
            m = SUMMARY_UTC.match(line)
            if m:
                end = utc_epoch(m.group(1))
    except FileNotFoundError:
        pass
    return start, end


def is_player_window(window):
    try:
        d = json.loads((window / "window.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return False
    return d.get("scope") == "player" or "player" in (d.get("windowAreas") or [])


def full_mtime(hms, own):
    """the attribution's HH:MM:SS (UTC) as an epoch inside this window's own span (start → restore summary, or now)."""
    if own is None:
        return None
    start, end = own[0], own[1] if own[1] is not None else time.time()
    day = start - start % 86400
    h, m, sec = map(int, hms.split(":"))
    for d in range(int((end - day) // 86400) + 1):
        t = day + d * 86400 + h * 3600 + m * 60 + sec
        if start - SPAN_SLACK <= t <= end + SPAN_SLACK:
            return t
    return None


def closed_dirs(ledgers):
    """dirs the guard ledger lists as closed (L/closed records)."""
    out = set()
    for ledger in ledgers:
        for rec in Path(ledger, "closed").glob("*.json"):
            try:
                d = json.loads(rec.read_text(encoding="utf-8")).get("dir")
            except (ValueError, OSError):
                continue
            if d:
                out.add(Path(d).resolve())
    return out


def owner_alive(window):
    """True / False when window.json names its owner (receipt.owner pid + lstart, `ps -o lstart=` form), None if unknown."""
    try:
        owner = ((json.loads((window / "window.json").read_text(encoding="utf-8")).get("receipt") or {}).get("owner") or {})
    except (OSError, ValueError, AttributeError):
        return None
    pid, lstart = owner.get("pid"), owner.get("lstart")
    if not isinstance(pid, int) or not isinstance(lstart, str):
        return None
    env = {k: v for k, v in os.environ.items() if k != "TZ"}
    env["LC_ALL"] = "C"
    r = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True, env=env)
    return r.returncode == 0 and " ".join(r.stdout.split()) == " ".join(lstart.split())


def never_restores(window, closed):
    """R4 (C-00 11.10): a window without a restore summary that will never get one — a NOT-RESTORED marker (resolved or
    not), a closed ledger record, or an owner process that is gone."""
    if (window / MARKER).exists():
        return True
    if window.resolve() in closed:
        return True
    return owner_alive(window) is False


def window_at(t, roots, own, ledgers=()):
    """another player-scope window whose span holds t. A window not restored yet counts while it is open — never one that
    will not restore (never_restores). Several: a restored one first (verified=1 first, then the tightest span), then an
    open one (R4, C-00 11.10: an abandoned 10.10 window shadowed the real writer, S-07 wp101-once-v2)."""
    closed = closed_dirs(ledgers)
    restored, still_open = [], []
    for w in windows(roots, own, ledgers):
        if not is_player_window(w) or (w / MARKER).exists():
            continue
        sp = span(w)
        if not sp or t < sp[0] - SPAN_SLACK:
            continue
        if sp[1] is not None:
            if t <= sp[1] + SPAN_SLACK:
                restored.append((verified(w) is not True, sp[1] - sp[0], str(w), w))
        elif not never_restores(w, closed):
            still_open.append((sp[0], str(w), w))
    if restored:
        return sorted(restored)[0][3]
    return sorted(still_open)[-1][2] if still_open else None


def decide(ug, roots, wait=600.0, sleep=time.sleep, clock=time.monotonic, ledgers=()):
    ug = Path(ug).resolve()
    rows = oos_rows(latest(ug / "out-of-scope-changed.txt").read_text(encoding="utf-8")) \
        if latest(ug / "out-of-scope-changed.txt").is_file() else []
    if not rows:
        return False, "32 without out-of-scope rows"
    off = [r for r in rows if not r.startswith("player  ")]
    if off:
        return False, "condition 1: a change outside the player area: %s" % off[0]
    changed = {r.split("  ", 2)[2].replace(" (launch-counter keys excluded)", "") for r in rows}
    attribution, mtimes = {}, {}
    try:
        for line in (ug / "attribution.txt").read_text(encoding="utf-8", errors="replace").splitlines():
            path, _, rest = line.partition("\t")
            attribution[path] = PID.findall(rest)
            m = MTIME.search(rest)
            if m:
                mtimes[path] = m.group(1)
    except FileNotFoundError:
        return False, "condition 2: no attribution.txt"
    starts = {p: s for p, s, _ in lineage(ug / "players-lineage.tsv")}
    owners, by_time = {}, {}
    own_span = span(ug)
    for path in sorted(changed):
        pids = attribution.get(path, [])
        if not pids:
            t = full_mtime(mtimes[path], own_span) if path in mtimes else None
            w = window_at(t, roots, ug, ledgers) if t is not None else None
            if w is None:
                return False, "condition 2: %s is not attributed to a Player (and its mtime lies in no other guarded player window)" % path
            owners[w] = owners.get(w, "mtime %s" % mtimes[path])
            by_time[w] = "%s mtime %sZ" % (path, mtimes[path])
            continue
        for pid in pids:
            if pid not in starts:
                return False, "condition 2: pid %s (writer of %s) is not in this window's lineage" % (pid, path)
            w = owner_window(pid, starts[pid], roots, ug, ledgers)
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
    why = "player area only, writers = guarded windows verified=1: " + ", ".join(
        "%s → %s" % (pid if str(pid).startswith("mtime") else "pid %s" % pid, w) for w, pid in sorted(owners.items()))
    if by_time:
        why = "WARNING (2b, no Player alive at the change): " + "; ".join(
            "%s inside player window %s" % (c, w) for w, c in sorted(by_time.items())) + " — " + why
    return True, why


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("ug")
    ap.add_argument("--root", action="append")
    ap.add_argument("--wait", type=float, default=600.0)
    ap.add_argument("--ledger", action="append")
    try:
        args = ap.parse_args(argv)
    except SystemExit as e:
        return 0 if e.code == 0 else 64
    if not Path(args.ug).is_dir():
        print("no guard window: %s" % args.ug, file=sys.stderr)
        return 64
    ok, why = decide(args.ug, [Path(r) for r in (args.root or [str(dd_paths.KPROJ)])], args.wait,
                     ledgers=[Path(l) for l in args.ledger] if args.ledger else default_ledgers())
    print(why)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
