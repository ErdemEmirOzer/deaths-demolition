#!/usr/bin/env python3
"""WP-078 §4 · verdict of a tier × camera-mode perf matrix against tools/qa/perf-targets.json (S-10).

Reads every <root>/perf-<tier>-<mode>-<n>/ written by wp078_perf.py (wp064_run attempt.json + PilotPerfProbe
04-window.json). A window counts when its attempt completed (status complete, exit 0) and the probe recorded the tier
and mode it was asked for; a cell passes when it has >= minWindows counted windows and EVERY counted window meets
the tier's mean and stall-filtered p99 with the stall exclusion <= stallFractionMax (no best-of; host-discarded
attempts do not count and are listed). Writes <root>/wp078-perf-report.json + .txt.

usage: wp078_perf_report.py <root> [--targets perf-targets.json]
exit: 0 every gate cell passes · 1 a gate cell fails or lacks windows · 64 usage
"""
import argparse
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CELL = re.compile(r"^perf-(?P<tier>[a-z]+)-(?P<mode>[a-z]+)-(?P<index>\d+)$")
MODE_NAMES = {"topdown": "TopDown", "tp": "ThirdPerson", "fp": "FirstPerson"}


def load(path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def judge_window(directory, tier, mode, targets):
    """(counted, passed, detail) for one window directory."""
    keys = targets["probeKeys"]
    attempt = load(directory / "attempt.json") or {}
    window = load(directory / keys["file"])
    detail = {"dir": directory.name, "status": attempt.get("status"), "exitCode": attempt.get("exitCode")}
    if attempt.get("status") != "complete" or attempt.get("exitCode") != 0 or window is None:
        detail["reason"] = "not a complete window"
        return False, False, detail

    want_tier = targets["tiers"][tier]["probeValue"]
    got_tier, got_mode = window.get(keys["tier"]), window.get(keys["mode"])
    if str(got_tier) != want_tier or str(got_mode) != MODE_NAMES.get(mode, mode):
        detail["reason"] = f"probe ran tier={got_tier} mode={got_mode}"
        return False, False, detail

    limits = targets["tiers"][tier]
    mean, p99 = window.get(keys["mean"]), window.get(keys["p99Filtered"])
    frames, stalls = window.get(keys["frames"]) or 0, window.get(keys["stallsExcluded"]) or 0
    stall_fraction = stalls / frames if frames else 1.0
    detail.update({"meanMs": mean, "p99FilteredMs": p99, "stallFraction": round(stall_fraction, 5), "frames": frames})
    passed = (isinstance(mean, (int, float)) and isinstance(p99, (int, float))
              and mean <= limits["meanMs"] and p99 <= limits["p99FilteredMs"]
              and stall_fraction <= targets["stallFractionMax"])
    return True, passed, detail


def report(root, targets):
    cells = {}
    for directory in sorted(p for p in root.iterdir() if p.is_dir()):
        match = CELL.match(directory.name)
        if not match or match["tier"] not in targets["tiers"]:
            continue
        cells.setdefault((match["tier"], match["mode"]), []).append(directory)

    rows = []
    for tier, limits in targets["tiers"].items():
        for mode in targets["modes"]:
            windows = [judge_window(d, tier, mode, targets) for d in cells.get((tier, mode), [])]
            counted = [w for w in windows if w[0]]
            enough = len(counted) >= int(limits["minWindows"])
            passed = enough and all(w[1] for w in counted)
            rows.append({"tier": tier, "mode": mode, "role": limits["role"], "limitMeanMs": limits["meanMs"],
                         "limitP99Ms": limits["p99FilteredMs"], "counted": len(counted), "minWindows": limits["minWindows"],
                         "pass": passed, "windows": [w[2] for w in windows]})
    gate_ok = all(r["pass"] for r in rows if r["role"] == "gate")
    return {"schema": "dd-wp078-perf-report-v1", "pass": gate_ok, "targets": targets.get("source"), "cells": rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root")
    parser.add_argument("--targets", default=str(HERE / "perf-targets.json"))
    args = parser.parse_args()
    root = Path(args.root)
    if not root.is_dir():
        print(f"no such directory: {root}", file=sys.stderr)
        return 64
    targets = json.loads(Path(args.targets).read_text(encoding="utf-8"))
    result = report(root, targets)
    (root / "wp078-perf-report.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    lines = [f"WP-078 perf {'PASS' if result['pass'] else 'FAIL'}"]
    for r in result["cells"]:
        means = [w.get("meanMs") for w in r["windows"] if "meanMs" in w]
        worst = max(means) if means else None
        lines.append(f"{'PASS' if r['pass'] else 'FAIL'} {r['role']:6} {r['tier']:10} {r['mode']:7} windows {r['counted']}/{r['minWindows']}"
                     f" worst mean {worst if worst is not None else '-'} ms (limit {r['limitMeanMs']} / p99 {r['limitP99Ms']})")
    (root / "wp078-perf-report.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0 if result["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
