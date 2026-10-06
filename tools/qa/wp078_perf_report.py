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
       [--baseline <R1 root> [--r2-measure <E>/r2/r2-P2.json --commit <FINAL> --sha256 <FINAL probe binary>
        --baseline-commit <R1> --baseline-sha256 <R1 probe binary>]]
exit: 0 every gate cell passes · 1 a gate cell fails or lacks windows · 64 usage (P2 never changes the exit: an r2-P2.json
      that is not written is named on stderr and in the .txt — P2 is then BLOCKED by name in the R2 report)
"""
import argparse
import csv
import json
import math
import re
import statistics
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
              "hostClean": attempt.get("hostClean"), "timedOut": attempt.get("timedOut"),
              "startedEpoch": attempt.get("startedEpoch")}
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


P2_FLAG_PCT, P2_FLAG_MS = 0.10, 1.0   # C-00 05.10: R2 report P2 — a cell over BOTH +10 % and +1.0 ms is flagged by name
LOAD_FIELDS = ("activeMin", "activeMax", "liveBodiesMin", "liveBodiesMax", "crowdMemberMin", "crowdMemberMax", "loadIntegrityFloor")


def window_integrity(directory, mode, targets, commit=None, binary_sha256=None):
    """WP-091 P2, "the source is in the recording" (C-00 05.10; KADRO game-test-engineer MUST-1, 06.10): the load and
    scope wp078_report's A4 gate proves on FINAL's windows, for every window a comparison uses, on both sides — the side's
    commit and build, the Run scene at the target resolution, exactly activeEnemies forced and held for the whole window,
    the warm-up and window lengths, the TP/FP sweep, a full CSV. The probe exits 0 even when its own window failed, so a
    counted attempt alone proves none of this. Returns the broken conditions ([] when sound)."""
    keys, want = targets["probeKeys"], targets["activeEnemies"]
    problems = []

    def need(ok, text):
        if not ok:
            problems.append(text)

    def number(value):
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)

    attempt = load(directory / "attempt.json") or {}
    if commit:
        need(attempt.get("executionCommit") == commit, f"executionCommit {attempt.get('executionCommit')!r} is not {commit}")
    if binary_sha256:
        need(attempt.get("binarySha256") == binary_sha256, f"binarySha256 {attempt.get('binarySha256')!r} is not the side's build")
    scene = load(directory / "01-scene-ready.json") or {}
    need(scene.get("scene") == "Run" and [scene.get("screenWidth"), scene.get("screenHeight")] == targets["resolution"],
         f"01-scene-ready: scene {scene.get('scene')!r} at {scene.get('screenWidth')}x{scene.get('screenHeight')}")
    force = load(directory / "02-force-40.json") or {}
    need(force.get("pass") is True and all(type(force.get(k)) is int and force[k] == want
                                           for k in ("configuredSafetyCeiling", "ceiling", "forcedTarget", "achievedCount")),
         f"02-force-40: the forced load is not exactly {want}")
    warm = load(directory / "03-warmup.json") or {}
    need(warm.get("pass") is True and number(warm.get("warmupSeconds")) and warm["warmupSeconds"] >= targets["warmupSeconds"],
         "03-warmup: incomplete")
    window = load(directory / keys["file"]) or {}
    need(window.get("pass") is True and number(window.get("windowSeconds")) and window["windowSeconds"] >= targets["windowSeconds"],
         "04-window: the probe's window did not pass or is short")
    need(all(type(window.get(k)) is int and window[k] == want for k in LOAD_FIELDS) and window.get("loadIntegrityFraction") == 1.0,
         f"04-window: the load was not exactly {want} for the whole window")
    if mode != "topdown":
        need(window.get(keys["sweep"]) is True, "04-window: TP/FP camera sweep missing")
    values = frame_times(directory / "frame-times-ms.csv")
    need(values is not None and len(values) == window.get(keys["frames"]) and sum(values) >= targets["windowSeconds"] * 1000 - 100,
         "frame-times-ms.csv: partial, or disagrees with frameCount")
    return problems


def integrity_failures(root, result, targets, commit=None, binary_sha256=None):
    """{window dir: broken conditions} over the counted windows of one side's report."""
    failures = {}
    for cell in result["cells"]:
        for w in cell["windows"]:
            if w.get("state") == "counted":
                problems = window_integrity(Path(root) / w["dir"], cell["mode"], targets, commit, binary_sha256)
                if problems:
                    failures[w["dir"]] = problems
    return failures


def _means(cell, invalid):
    return [(w["dir"], w["meanMs"], w.get("startedEpoch")) for w in (cell["windows"] if cell else [])
            if w.get("state") == "counted" and isinstance(w.get("meanMs"), (int, float)) and w["dir"] not in invalid]


def _index(name):
    return int(name.rsplit("-", 1)[1])


def compare(r2_result, r1_result, invalid=None):
    """WP-091 P2 (report, never a gate): per tier × mode, the mean of the sound counted windows' frame means, R2 − R1 in ms
    and %. A cell is measured only with the tier's minWindows sound windows on BOTH sides (else "unmeasured", never
    flagged as fine). invalid = {"r2": {dir: problems}, "r1": {dir: problems}} (window_integrity) — those windows never
    count. Each cell also carries the noise and the pairing (KADRO game-test-engineer SHOULD-3): the window means, their
    SD, the SE of the difference, the n-th R2 window paired with the n-th R1 window (their differences, the median, and the
    largest start-time gap of a pair)."""
    invalid = invalid or {}
    r1_cells = {(c["tier"], c["mode"]): c for c in r1_result["cells"]}
    rows = []
    for cell in r2_result["cells"]:
        base = r1_cells.get((cell["tier"], cell["mode"]))
        need = int(cell.get("minWindows") or 1)
        r2w = sorted(_means(cell, invalid.get("r2", {})), key=lambda w: _index(w[0]))
        r1w = sorted(_means(base, invalid.get("r1", {})), key=lambda w: _index(w[0]))
        row = {"tier": cell["tier"], "mode": cell["mode"], "r2Windows": len(r2w), "r1Windows": len(r1w), "minWindows": need,
               "r2WindowMeansMs": [m for _, m, _ in r2w], "r1WindowMeansMs": [m for _, m, _ in r1w]}
        if len(r2w) < need or len(r1w) < need:
            row.update(state="unmeasured", flagged=False)
            rows.append(row)
            continue
        r2_mean = round(statistics.fmean(row["r2WindowMeansMs"]), 4)
        r1_mean = round(statistics.fmean(row["r1WindowMeansMs"]), 4)
        if r1_mean <= 0:
            row.update(state="unmeasured", flagged=False)
            rows.append(row)
            continue
        sd2 = statistics.stdev(row["r2WindowMeansMs"]) if len(r2w) > 1 else 0.0
        sd1 = statistics.stdev(row["r1WindowMeansMs"]) if len(r1w) > 1 else 0.0
        pairs = list(zip(r2w, r1w))
        deltas = [round(a[1] - b[1], 4) for a, b in pairs]
        gaps = [abs(a[2] - b[2]) for a, b in pairs if isinstance(a[2], (int, float)) and isinstance(b[2], (int, float))]
        delta = round(r2_mean - r1_mean, 4)
        pct = round(delta / r1_mean, 4)
        row.update(state="measured", r2MeanMs=r2_mean, r1MeanMs=r1_mean, deltaMs=delta, deltaPct=pct,
                   flagged=pct > P2_FLAG_PCT and delta > P2_FLAG_MS, r2SdMs=round(sd2, 4), r1SdMs=round(sd1, 4),
                   seDeltaMs=round(math.sqrt(sd2 ** 2 / len(r2w) + sd1 ** 2 / len(r1w)), 4), pairDeltasMs=deltas,
                   pairDeltaMedianMs=round(statistics.median(deltas), 4), pairGapMaxS=round(max(gaps), 1) if gaps else None)
        rows.append(row)
    measured = [r for r in rows if r["state"] == "measured"]
    return {"schema": "dd-wp078-perf-compare-v2", "rule": f"flag when deltaPct > {P2_FLAG_PCT} and deltaMs > {P2_FLAG_MS}",
            "cells": rows, "flaggedCells": sum(1 for r in rows if r["flagged"]),
            "unmeasuredCells": sum(1 for r in rows if r["state"] == "unmeasured"),
            "invalidWindows": {side: dict(sorted(v.items())) for side, v in sorted(invalid.items()) if v},
            "worstDeltaMs": max((r["deltaMs"] for r in measured), default=None),
            "worstDeltaPct": max((r["deltaPct"] for r in measured), default=None)}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root")
    parser.add_argument("--targets", default=str(HERE / "perf-targets.json"))
    parser.add_argument("--baseline", help="WP-091 P2: the R1 perf root measured in the same window (report only)")
    parser.add_argument("--r2-measure", help="write the P2 comparison as an R2 measurement artifact (dd-r2-measure-v1) here")
    parser.add_argument("--commit", help="the FINAL commit recorded in the --r2-measure artifact (and every R2 window's)")
    parser.add_argument("--sha256", help="P2: the FINAL release-probe binary every R2 window ran")
    parser.add_argument("--baseline-commit", help="P2: the R1 commit every baseline window ran under")
    parser.add_argument("--baseline-sha256", help="P2: the R1 release-probe binary every baseline window ran")
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
    if args.baseline:
        baseline = Path(args.baseline)
        if not baseline.is_dir():
            print(f"no such baseline directory: {baseline}", file=sys.stderr)
            return 64
        base_result = report(baseline, targets)
        invalid = {"r2": integrity_failures(root, result, targets, args.commit, args.sha256),
                   "r1": integrity_failures(baseline, base_result, targets, args.baseline_commit, args.baseline_sha256)}
        comparison = compare(result, base_result, invalid)
        comparison.update(commit=args.commit, binarySha256=args.sha256, baselineCommit=args.baseline_commit,
                          baselineBinarySha256=args.baseline_sha256)
        (root / "wp078-perf-compare.json").write_text(json.dumps(comparison, indent=2) + "\n", encoding="utf-8")
        lines.append(f"P2 (report) R2 - R1: {comparison['flaggedCells']} flagged, {comparison['unmeasuredCells']} unmeasured, "
                     f"worst {comparison['worstDeltaMs']} ms / {comparison['worstDeltaPct']}")
        for c in comparison["cells"]:
            if c["flagged"]:
                lines.append(f"  P2 FLAG {c['tier']} {c['mode']}: {c['r1MeanMs']} -> {c['r2MeanMs']} ms (+{c['deltaMs']} ms, +{c['deltaPct']:.1%})")
        if args.r2_measure:
            if not (args.commit and len(args.commit) == 40 and args.baseline_commit and len(args.baseline_commit) == 40):
                print("--r2-measure needs --commit and --baseline-commit <40-hex>", file=sys.stderr)
                return 64
            # source in the recording: a broken window on either side, or a cell without its windows, writes nothing
            blocked = [f"{side} {name}: {'; '.join(problems)}" for side, failures in comparison["invalidWindows"].items()
                       for name, problems in failures.items()]
            blocked += [f"unmeasured cell {c['tier']} {c['mode']} (R2 {c['r2Windows']}, R1 {c['r1Windows']} sound windows, "
                        f"{c['minWindows']} each needed)" for c in comparison["cells"] if c["state"] == "unmeasured"]
            if blocked:
                lines.append(f"P2 NOT written ({len(blocked)} problems) — P2 BLOCKED by name in the R2 report:")
                lines += ["  " + b for b in blocked]
                for b in blocked:
                    print("P2: " + b, file=sys.stderr)
            else:
                artifact = {"schema": "dd-r2-measure-v1", "gate": "P2", "commit": args.commit,
                            "source": "tools/qa/wp078_perf_report.py --baseline",
                            "values": {k: comparison[k] for k in ("flaggedCells", "worstDeltaMs", "worstDeltaPct")},
                            # the sound windows used, both sides (r2_gates: samples <= 0 is BLOCKED — KADRO test-engineer MUST-1)
                            "samples": sum(c["r2Windows"] + c["r1Windows"] for c in comparison["cells"]),
                            "binarySha256": args.sha256, "baselineCommit": args.baseline_commit,
                            "baselineBinarySha256": args.baseline_sha256, "cells": comparison["cells"]}
                Path(args.r2_measure).parent.mkdir(parents=True, exist_ok=True)
                Path(args.r2_measure).write_text(json.dumps(artifact, indent=2) + "\n", encoding="utf-8")
    (root / "wp078-perf-report.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0 if result["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
