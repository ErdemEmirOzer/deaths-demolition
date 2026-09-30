#!/usr/bin/env python3
"""WP-078 §4 · verdict of a tier × camera-mode perf matrix against tools/qa/perf-targets.json (S-10).

Reads every <root>/perf-<tier>-<mode>-<n>/ written by wp078_perf.py (wp064_run attempt.json + PilotPerfProbe
04-window.json + frame-times-ms.csv). Each attempt is one of (C-00 30.09 K8):
  * counted   — status complete, exit 0, hostClean, the probe recorded the tier and mode it was asked for;
  * discarded — a host-only discard: (a) status preflight-failed ∧ exit 75, or (b) status failed ∧ hostClean false ∧
                timedOut false ∧ exit 0 ∧ missingArtifacts [] (the Player worked, the host was dirty);
  * failed    — anything else (a crash, timeout or missing artifact on a clean host, a wrong tier/mode): the cell FAILS.
A cell passes when it has >= minWindows counted windows, no failed attempt, and EVERY counted window meets the tier's
mean and stall-filtered p99 (no best-of). Stalls and the filtered p99 are recomputed from frame-times-ms.csv with the
tier's own stallMs (K9: Sinematik 40, Yüksek/Performans 33.33; the probe's native 33.33 fields stay a cross-check);
each window also reports raw p99, p99.9, max, the > 33.33 ms fraction and rawP99OverLimit. Writes
<root>/wp078-perf-report.json + .txt.

usage: wp078_perf_report.py <root> [--targets perf-targets.json]
exit: 0 every gate cell passes · 1 a gate cell fails or lacks windows · 64 usage
"""
import argparse
import csv
import json
import math
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CELL = re.compile(r"^perf-(?P<tier>[a-z]+)-(?P<mode>[a-z]+)-(?P<index>\d+)$")
MODE_NAMES = {"topdown": "TopDown", "tp": "ThirdPerson", "fp": "FirstPerson"}
NATIVE_STALL_MS = 33.33


def load(path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def frame_times(path):
    """Frame times (ms) from the probe CSV; a header row is skipped; None when unreadable or empty."""
    values = []
    try:
        with path.open(newline="", encoding="utf-8") as handle:
            for row in csv.reader(handle):
                if not row:
                    continue
                try:
                    v = float(row[0])
                except ValueError:
                    continue
                if math.isfinite(v) and v > 0:
                    values.append(v)
    except OSError:
        return None
    return values or None


def percentile(sorted_values, p):
    """PilotPerfProbe.Percentile: nearest rank, index = ceil(p·n) − 1."""
    n = len(sorted_values)
    return sorted_values[min(max(math.ceil(p * n) - 1, 0), n - 1)]


def frame_metrics(values, stall_ms):
    """Mean, raw p99/p99.9/max, stalls above stall_ms and the stall-filtered p99 (the probe's formula)."""
    s = sorted(values)
    n = len(s)
    kept = sum(1 for v in s if v <= stall_ms)
    return {"frames": n, "meanMs": round(sum(s) / n, 4), "p99RawMs": percentile(s, 0.99), "p999Ms": percentile(s, 0.999),
            "maxMs": s[-1], "stalls": n - kept, "stallFraction": round((n - kept) / n, 5),
            "p99FilteredMs": percentile(s, min(0.99 * kept / n, 1.0)) if kept else None,
            "over33Fraction": round(sum(1 for v in s if v > NATIVE_STALL_MS) / n, 5)}


def tier_stall_ms(targets, tier):
    return float(targets["tiers"][tier].get("stallMs", targets.get("stallMs", NATIVE_STALL_MS)))


def classify(attempt):
    """counted-candidate / discarded / failed from wp064_run's attempt.json (C-00 30.09 K8)."""
    status, code = attempt.get("status"), attempt.get("exitCode")
    if status == "complete" and code == 0 and attempt.get("hostClean") is True:
        return "complete"
    if status == "preflight-failed" and code == 75:
        return "discarded"
    if (status == "failed" and attempt.get("hostClean") is False and attempt.get("timedOut") is False and code == 0
            and attempt.get("missingArtifacts") == []):
        return "discarded"
    return "failed"


def judge_window(directory, tier, mode, targets):
    """(state, passed, detail) — state counted / discarded / failed."""
    keys = targets["probeKeys"]
    attempt = load(directory / "attempt.json") or {}
    detail = {"dir": directory.name, "status": attempt.get("status"), "exitCode": attempt.get("exitCode"),
              "hostClean": attempt.get("hostClean"), "timedOut": attempt.get("timedOut")}
    kind = classify(attempt)
    if kind != "complete":
        detail["reason"] = "host-only discard" if kind == "discarded" else "attempt did not complete on a clean host"
        return kind, False, detail

    window = load(directory / keys["file"])
    values = frame_times(directory / "frame-times-ms.csv")
    if window is None or values is None:
        detail["reason"] = "complete attempt without 04-window.json / frame-times-ms.csv"
        return "failed", False, detail

    want_tier = targets["tiers"][tier]["probeValue"]
    got_tier, got_mode = window.get(keys["tier"]), window.get(keys["mode"])
    if str(got_tier) != want_tier or str(got_mode) != MODE_NAMES.get(mode, mode):
        detail["reason"] = f"probe ran tier={got_tier} mode={got_mode}"
        return "failed", False, detail

    limits = targets["tiers"][tier]
    m = frame_metrics(values, tier_stall_ms(targets, tier))
    detail.update(m)
    detail["stallMs"] = tier_stall_ms(targets, tier)
    detail["nativeCrossCheck"] = {"frameMeanMs": window.get(keys["mean"]), "frameP99FilteredMs@33.33": window.get(keys["p99Filtered"]),
                                  "framesExcludedAsStalls@33.33": window.get(keys["stallsExcluded"]), "frameCount": window.get(keys["frames"])}
    detail["rawP99OverLimit"] = m["p99RawMs"] > limits["p99FilteredMs"]
    passed = (m["p99FilteredMs"] is not None and m["meanMs"] <= limits["meanMs"] and m["p99FilteredMs"] <= limits["p99FilteredMs"]
              and m["stallFraction"] <= targets["stallFractionMax"])
    return "counted", passed, detail


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
            counted = [w for w in windows if w[0] == "counted"]
            failed = [w for w in windows if w[0] == "failed"]
            discarded = [w for w in windows if w[0] == "discarded"]
            enough = len(counted) >= int(limits["minWindows"])
            passed = enough and not failed and all(w[1] for w in counted)
            rows.append({"tier": tier, "mode": mode, "role": limits["role"], "limitMeanMs": limits["meanMs"],
                         "limitP99Ms": limits["p99FilteredMs"], "stallMs": tier_stall_ms(targets, tier),
                         "counted": len(counted), "failed": len(failed), "discarded": len(discarded),
                         "minWindows": limits["minWindows"], "pass": passed,
                         "rawP99OverLimit": any(w[2].get("rawP99OverLimit") for w in counted),
                         "windows": [dict(w[2], state=w[0]) for w in windows]})
    gate_ok = all(r["pass"] for r in rows if r["role"] == "gate")
    return {"schema": "dd-wp078-perf-report-v2", "pass": gate_ok, "targets": targets.get("source"), "cells": rows}


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
                     f" failed {r['failed']} discarded {r['discarded']} worst mean {worst if worst is not None else '-'} ms"
                     f" (limit {r['limitMeanMs']} / p99 {r['limitP99Ms']} @ stall {r['stallMs']})" + (" 🔴 raw p99 over limit" if r["rawP99OverLimit"] else ""))
    (root / "wp078-perf-report.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0 if result["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
