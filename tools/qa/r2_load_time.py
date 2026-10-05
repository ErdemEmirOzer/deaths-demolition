#!/usr/bin/env python3
"""WP-091 P5 (C-00 05.10 decision (a)): Selection → Run load time, R2 over R1, from the Player's own record.

Every bot session that enters the run through the selection screen (QaRunBot boot flow: GoToSelection → StartRun)
writes <session>/ui-data/load-timing.json through its own -ddUiDataRoot (never the user's data):
  {"Version": 1, "Scenes": ["Selection", "Run"], "Seconds": [1.233, 1.218]}
"Run" is the last real load of the run scene: LoadSceneAsync → activation, the curtain's minimum display time included
(PrototypeUiFlow.RecordLoad; FastTransitions loads are not recorded). The acceptance chain already runs these sessions
in both rounds (A2 gameplay/route × 3 modes, A3 flag runs), so P5 needs no extra Player and no code in either build:
the R1 FINAL evidence is the baseline, the R2 FINAL evidence the subject, dev build against dev build.

Raw (S-09 WP-081 B4, R2 only): "RawSeconds", in Scenes order, is LoadSceneAsync → progress 0.9 — the load itself, no
curtain floor, no activation; -1 = not measured, absent before WP-081 (read as -1). R1 FINAL has none, so it never
enters the ratio: r2RawMedianSeconds is shown next to it for the record.

Floor: the curtain stays at least MinimumDisplaySeconds (1.2 s at R1). R1 measured ~1.22 s in every session, i.e. the
real load was under the floor; the ratio therefore only shows an R2 load that grows past it (floorLimited = true says
so). P5 is a report entry, it never fails.

usage: r2_load_time.py <R2 evidence> --baseline <R1 evidence> --baseline-commit <40-hex> --commit <40-hex> --out r2-P5.json
exit: 0 written · 1 a side has no sample (nothing written: P5 stays BLOCKED in the report) · 64 usage
"""
import argparse
import json
import re
import statistics
import sys
from pathlib import Path

FLOOR_SECONDS = 1.2        # PrototypeUiFlow.MinimumDisplaySeconds at R1 FINAL 84941661
FLOOR_MARGIN = 0.05        # a median within this of the floor is the floor, not the load
STAGES = ("a2", "a3")      # the bot sessions of the chain that enter the run through the selection screen
SHA = re.compile(r"[0-9a-f]{40}")


def _number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and value > 0


def samples(evidence, field="Seconds"):
    """[(session, seconds)] — the "Run" entry of `field` (Seconds, or RawSeconds) in every bot session's
    load-timing.json, in path order; a missing array or -1 is not a sample."""
    out = []
    for stage in STAGES:
        for path in sorted((Path(evidence) / stage).glob("*/ui-data/load-timing.json")):
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
                seconds = dict(zip(data["Scenes"], data.get(field) or [])).get("Run")
            except (OSError, ValueError, KeyError, TypeError, AttributeError):
                continue
            if _number(seconds):
                out.append((f"{stage}/{path.parent.parent.name}", float(seconds)))
    return out


def compare(r2, r1, r2_raw=()):
    """P5 values from two sample lists, or None when a side has nothing; r2_raw (R2 RawSeconds) is recorded, never
    part of the ratio."""
    if not r2 or not r1:
        return None
    raw = [s for _, s in r2_raw]
    r2_median = statistics.median(s for _, s in r2)
    r1_median = statistics.median(s for _, s in r1)
    return {"ratio": round(r2_median / r1_median, 4),
            "r2MedianSeconds": round(r2_median, 4), "r1MedianSeconds": round(r1_median, 4),
            "r2Samples": len(r2), "r1Samples": len(r1),
            "floorSeconds": FLOOR_SECONDS,
            "floorLimited": r1_median <= FLOOR_SECONDS + FLOOR_MARGIN,
            "r2RawMedianSeconds": round(statistics.median(raw), 4) if raw else None,
            "r2RawSamples": len(raw)}


def artifact(r2_evidence, r1_evidence, commit, baseline_commit):
    r2, r1, r2_raw = samples(r2_evidence), samples(r1_evidence), samples(r2_evidence, "RawSeconds")
    values = compare(r2, r1, r2_raw)
    if values is None:
        return None, r2, r1
    return {"schema": "dd-r2-measure-v1", "gate": "P5", "commit": commit, "baselineCommit": baseline_commit,
            "source": "tools/qa/r2_load_time.py (Player load-timing.json, bot boot flow)",
            "values": values, "samples": len(r2),
            "sessions": {"r2": dict(r2), "r1": dict(r1), "r2Raw": dict(r2_raw)}}, r2, r1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("evidence", help="the R2 FINAL evidence root")
    parser.add_argument("--baseline", required=True, help="the R1 FINAL evidence root")
    parser.add_argument("--baseline-commit", required=True)
    parser.add_argument("--commit", required=True, help="the R2 FINAL commit")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    if not (SHA.fullmatch(args.commit) and SHA.fullmatch(args.baseline_commit)):
        print("--commit and --baseline-commit must be 40-hex", file=sys.stderr)
        return 64
    data, r2, r1 = artifact(args.evidence, args.baseline, args.commit, args.baseline_commit)
    if data is None:
        print(f"P5: no samples (R2 {len(r2)}, R1 {len(r1)}) — nothing written, P5 stays BLOCKED", file=sys.stderr)
        return 1
    Path(args.out).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    v = data["values"]
    print(f"P5 ratio {v['ratio']} (R2 {v['r2MedianSeconds']} s / R1 {v['r1MedianSeconds']} s, "
          f"{v['r2Samples']}/{v['r1Samples']} sessions{', floor-limited' if v['floorLimited'] else ''})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
