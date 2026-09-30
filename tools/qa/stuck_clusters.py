#!/usr/bin/env python3
"""WP-078 K6 (C-00 30.09) · same-spot stall measure for WP-070 K2 / WP-074 B9 ("takılma > 5 s = 0").

QaRunBot's `stuckRecoveries` counts the bot's own route recoveries (patrol 3 s / combat 1.5 s without progress) and is
a report, not a stall. This measure reads the bot's Player log instead:
  * the 1 Hz trace   `[QaRunBot] t pos=x,y,z time=T mode=… near=N …`
  * stall events     `[QaRunBot] stall-recovery mode=M pos=x,y,z time=T near=N goal=…`
                     `[QaRunBot] unstick-sweep pos=x,y,z time=T (k recoveries at the same spot)`
A cluster is a maximal run of consecutive trace samples that all stay within `radius` metres (planar) of the run's first
sample, lasting at least `min_seconds`, that also contains at least one stall event within `radius` of it — the bot
wanted to move and could not (a bot holding still on purpose has no stall event). Lines without `time=` (logs older
than WP-078 K6) make the measure unavailable: `measurable` is False and the caller reports "ölçülmedi", never PASS.

usage: stuck_clusters.py <player.log> [--min-seconds 5] [--radius 1.0]
exit: 0 no cluster · 1 at least one cluster · 2 not measurable · 64 usage
"""
import argparse
import json
import math
import re
import sys

TRACE = re.compile(r"\[QaRunBot\] t pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+) time=([\d.]+)(?:.*? near=(\d+))?")
EVENT = re.compile(r"\[QaRunBot\] (stall-recovery|unstick-sweep)(?: mode=(\w+))? pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+) time=([\d.]+)(?:.*? near=(\d+))?")
OLD_EVENT = re.compile(r"\[QaRunBot\] (stall-recovery|unstick-sweep)\b")


def _planar(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def parse(text):
    trace, events, old = [], [], 0
    for line in text.splitlines():
        m = TRACE.search(line)
        if m:
            trace.append({"x": float(m.group(1)), "z": float(m.group(3)), "t": float(m.group(4)),
                          "near": int(m.group(5)) if m.group(5) else None})
            continue
        m = EVENT.search(line)
        if m:
            events.append({"kind": m.group(1), "mode": m.group(2), "x": float(m.group(3)), "z": float(m.group(5)),
                           "t": float(m.group(6)), "near": int(m.group(7)) if m.group(7) else None})
        elif OLD_EVENT.search(line):
            old += 1
    return trace, events, old


def clusters(text, min_seconds=5.0, radius=1.0):
    """Return {"measurable": bool, "clusters": [...], "samples": n, "events": n}."""
    trace, events, old = parse(text)
    if not trace or old:
        return {"measurable": False, "clusters": [], "samples": len(trace), "events": len(events),
                "reason": "no timed trace" if not trace else f"{old} stall line(s) without time="}
    found, i = [], 0
    while i < len(trace):
        anchor = (trace[i]["x"], trace[i]["z"])
        j = i
        while j + 1 < len(trace) and _planar(anchor, (trace[j + 1]["x"], trace[j + 1]["z"])) <= radius:
            j += 1
        start, end = trace[i]["t"], trace[j]["t"]
        if end - start >= min_seconds:
            inside = [e for e in events if start - 0.5 <= e["t"] <= end + 0.5 and _planar(anchor, (e["x"], e["z"])) <= radius * 1.5]
            if inside:
                nears = [e["near"] for e in inside if e["near"] is not None] + [s["near"] for s in trace[i:j + 1] if s["near"] is not None]
                found.append({"x": round(anchor[0], 1), "z": round(anchor[1], 1), "start": start, "seconds": round(end - start, 1),
                              "events": len(inside), "modes": sorted({e["mode"] for e in inside if e["mode"]}),
                              "nearMax": max(nears) if nears else None})
            i = j + 1
        else:
            i += 1
    return {"measurable": True, "clusters": found, "samples": len(trace), "events": len(events)}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("log")
    parser.add_argument("--min-seconds", type=float, default=5.0)
    parser.add_argument("--radius", type=float, default=1.0)
    args = parser.parse_args()
    try:
        text = open(args.log, encoding="utf-8", errors="replace").read()
    except OSError as error:
        print(error, file=sys.stderr)
        return 64
    result = clusters(text, args.min_seconds, args.radius)
    print(json.dumps(result, indent=2))
    if not result["measurable"]:
        return 2
    return 1 if result["clusters"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
