#!/usr/bin/env python3
"""WP-077 B5 (S-04): real XP/hour (and Kül/hour) from run telemetry — the DEC-098 open item.

Reads `dd-run-telemetry-v1` / `-v2` run documents (RunTelemetry, WP-045: one `run-<utc>.json` per run) from the directory
given on the command line and prints aggregates only: per-run XP/h, the pooled XP/h (Σ XP / Σ run time), bands by
reached round, and what that pace means for the DEC-098 rings (Kül = XP / 30; ring 1 = 150, rings 1+2 = 2 080,
all = 5 830 Kül — the shipped meta catalog as TreeModel.Tempo reads it). It never prints per-run identities
(no timestamps), never writes next to the input, and has NO default path: the user's own telemetry
(persistentDataPath/telemetry) is user data under §4.14 — only C-00 may point this at it.

    python3 tools/balance/xp_rate.py <telemetry-dir> [--min-seconds 30] [--json out.json]

XP of a run = completedRoundXp + killXp (the run's XP before profile rewards: achievements, first-R10 and other
one-off Kül are not in the telemetry and are reported separately by TreeModel.Tempo). Run time = runSeconds
(Time.time from RunStarted to run end: includes the round-end shop, excludes Pause — timeScale 0).
"""
import argparse
import glob
import json
import os
import statistics

XP_PER_ASH = 30                       # DEC-098
RING_COST = (150, 2080, 5830)         # TreeModel.Tempo on the shipped meta-tree.json (26.09)
MODEL_XP_PER_HOUR = (2643, 3257)      # TreeModel round model: new player (dies R6) … reaches R15
REFERENCE_XP_PER_HOUR = 4500          # WP-077 direction §4.1 / DEC-098 reference


def load(directory, min_seconds):
    docs, skipped = [], 0
    for path in sorted(glob.glob(os.path.join(directory, "run-*.json"))):
        try:
            with open(path, encoding="utf-8") as f:
                d = json.load(f)
        except (OSError, ValueError):
            skipped += 1
            continue
        if d.get("schema") not in ("dd-run-telemetry-v1", "dd-run-telemetry-v2") or float(d.get("runSeconds", 0)) < min_seconds:
            skipped += 1
            continue
        docs.append(d)
    return docs, skipped


def band(reached):
    if reached <= 5:
        return "R1–R5"
    if reached <= 9:
        return "R6–R9"
    if reached <= 14:
        return "R10–R14"
    return "R15+"


def summarise(docs):
    rows = []
    for d in docs:
        xp = int(d.get("completedRoundXp", 0)) + int(d.get("killXp", 0))
        sec = float(d["runSeconds"])
        rows.append(dict(xp=xp, sec=sec, xph=xp * 3600.0 / sec, reached=int(d.get("reachedRound", 0)),
                         weapon=d.get("weapon", "?"), build=d.get("build", "?")))
    if not rows:
        return None
    pooled = sum(r["xp"] for r in rows) * 3600.0 / sum(r["sec"] for r in rows)
    per_run = sorted(r["xph"] for r in rows)
    bands = {}
    for r in rows:
        bands.setdefault(band(r["reached"]), []).append(r)
    return dict(runs=len(rows), hours=sum(r["sec"] for r in rows) / 3600.0, pooled=pooled,
                median=statistics.median(per_run), p25=per_run[len(per_run) // 4], p75=per_run[(3 * len(per_run)) // 4],
                reached_median=statistics.median(r["reached"] for r in rows),
                bands={k: dict(runs=len(v), pooled=sum(x["xp"] for x in v) * 3600.0 / sum(x["sec"] for x in v))
                       for k, v in sorted(bands.items())},
                weapons=sorted({r["weapon"] for r in rows}), builds=sorted({r["build"] for r in rows}))


def rings(xph):
    ash = xph / XP_PER_ASH
    return [c / ash for c in RING_COST] if ash > 0 else [float("inf")] * 3


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("directory")
    ap.add_argument("--min-seconds", type=float, default=30.0, help="ignore aborted runs shorter than this")
    ap.add_argument("--json")
    a = ap.parse_args()
    docs, skipped = load(a.directory, a.min_seconds)
    s = summarise(docs)
    if s is None:
        print("no dd-run-telemetry-v1 runs in %s (skipped %d)" % (a.directory, skipped))
        return 1
    print("runs %d (skipped %d) · %.2f h played · median reached round R%.0f · builds %s" % (
        s["runs"], skipped, s["hours"], s["reached_median"], ", ".join(s["builds"])))
    print("XP/h pooled %.0f · per-run median %.0f (p25 %.0f / p75 %.0f)" % (s["pooled"], s["median"], s["p25"], s["p75"]))
    for k, v in s["bands"].items():
        print("  %-8s %3d runs · XP/h %.0f" % (k, v["runs"], v["pooled"]))
    r = rings(s["pooled"])
    print("Kül/h %.0f → ring 1 %.1f h · rings 1+2 %.1f h · all %.0f h (XP only; one-off Kül not included)" % (
        s["pooled"] / XP_PER_ASH, r[0], r[1], r[2]))
    print("vs model %d–%d XP/h (TreeModel round model) and reference %d XP/h (DEC-098): pooled / reference = %.2f" % (
        MODEL_XP_PER_HOUR[0], MODEL_XP_PER_HOUR[1], REFERENCE_XP_PER_HOUR, s["pooled"] / REFERENCE_XP_PER_HOUR))
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump(dict(s, rings_hours=r), f, indent=1)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
