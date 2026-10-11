#!/usr/bin/env python3
"""C-00 12.10 (queue fairness) · tools/guard-queue.py — who holds the user-data guard windows and who waits. Read-only.

usage: python3 tools/guard-queue.py [--ledger L] [--json] [--waiting]
  --ledger   the guard's window ledger (default: $DD_GUARD_ROOT/ledger, else ~/.dd-userdata-guard of the passwd home —
             the same rule as tools/userdata-guard.sh)
  --json     the same report as JSON
  --waiting  print only the number of queued runs (tools/ug-step.sh reads it)

The report: every OPEN window (L/windows: id, areas, state, minutes open, the owner's pid and whether it is alive, DIR)
and the QUEUE (L/queue tickets, FIFO: position, areas, minutes waiting, the waiter's pid and whether it is alive, DIR).
A window open longer than DD_GUARD_FAIR_WARN_MIN (25) while runs wait is flagged FAIRNESS — a warning, nothing is closed.
It never takes the ledger lock and never writes (an unreadable ticket is listed, not retired).
exit 0 · 64 usage
"""
import argparse
import json
import os
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import userdata_guard_io as io  # noqa: E402


def default_ledger():
    root = os.environ.get("DD_GUARD_ROOT")
    return Path(root) / "ledger" if root else Path(io.pwhome()) / ".dd-userdata-guard"


def read_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (ValueError, OSError):
        return None


def liveness(who, table):
    if not (who or {}).get("pid"):
        return "unknown"
    return "alive" if io.alive(who, table) else "gone"


def report(ledger):
    table = io.ProcTable()
    limit = io.fair_warn_minutes()
    queue = []
    folder = ledger / "queue"
    for name in sorted(os.listdir(folder)) if folder.is_dir() else []:
        m = re.match(io.TICKET_NAME, name)
        if not m:
            continue
        t = read_json(folder / name)
        if t is None:
            queue.append({"ns": int(m.group(1)), "id": m.group(2), "unreadable": True})
            continue
        waiter = t.get("waiter") or {}
        queue.append({"ns": int(m.group(1)), "id": t.get("id") or m.group(2), "dir": t.get("dir"),
                      "areas": t.get("windowAreas") or [], "waitingMin": round(io.age_minutes(t.get("utc")), 1),
                      "waiterPid": waiter.get("pid"), "waiter": liveness(waiter, table)})
    queue.sort(key=lambda q: (q["ns"], q["id"]))
    windows = []
    for rec in io.ledger_records(str(ledger)):
        if rec.get("state") == "closed":
            continue
        owner = io.record_owner(rec)
        age = io.age_minutes(rec.get("utc"))
        areas = set(rec.get("windowAreas") or [])
        waiting = sum(1 for q in queue if not q.get("unreadable") and areas & set(q.get("areas") or []))
        windows.append({"id": rec.get("id"), "dir": rec.get("dir"), "areas": sorted(areas), "state": rec.get("state"),
                        "openMin": round(age, 1), "ownerPid": owner.get("pid"), "owner": liveness(owner, table),
                        "waitingBehind": waiting, "fairness": bool(io.fairness_note(age, waiting, limit))})
    windows.sort(key=lambda w: -w["openMin"])
    return {"ledger": str(ledger), "utc": io.utc_now(), "fairWarnMin": limit, "windows": windows,
            "queue": [{k: v for k, v in q.items() if k != "ns"} for q in queue]}


def text(r):
    out = ["guard queue · %s · %s · fairness warning after %.0f min" % (r["ledger"], r["utc"], r["fairWarnMin"]),
           "OPEN WINDOWS (%d)" % len(r["windows"])]
    for w in r["windows"]:
        out.append("  %-40s %-14s %-12s %6.1f min  owner pid %s (%s)  %s%s" % (
            w["id"], ",".join(w["areas"]), w["state"], w["openMin"], w["ownerPid"], w["owner"], w["dir"],
            "  ⚠ FAIRNESS: %d waiting behind it" % w["waitingBehind"] if w["fairness"] else ""))
    out.append("QUEUE (%d waiting)" % len(r["queue"]))
    for i, q in enumerate(r["queue"], 1):
        if q.get("unreadable"):
            out.append("  %2d  %s  (unreadable ticket)" % (i, q["id"]))
            continue
        out.append("  %2d  %-14s waiting %6.1f min  waiter pid %s (%s)  %s" % (
            i, ",".join(q["areas"]), q["waitingMin"], q["waiterPid"], q["waiter"], q["dir"]))
    return "\n".join(out)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ledger")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--waiting", action="store_true")
    try:
        args = ap.parse_args(argv)
    except SystemExit as e:
        return 0 if e.code == 0 else 64
    ledger = Path(args.ledger) if args.ledger else default_ledger()
    r = report(ledger)
    if args.waiting:
        print(len(r["queue"]))
    elif args.json:
        print(json.dumps(r, indent=2))
    else:
        print(text(r))
    return 0


if __name__ == "__main__":
    sys.exit(main())
