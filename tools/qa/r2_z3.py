#!/usr/bin/env python3
"""WP-091 Z3 + P3 (S-05, C-00 06.10): render ratios R2 over R1 at one fixed pose, the character budget, and the B9 pose
cells, from the PilotPerfProbe records of the chain's own windows. The one writer of r2-Z3.json and r2-P3.json.

Z3 render ratios. The chain's z3 pose run (wp078_perf --baseline-bin, S-10's alternating runner) measures one fixed pose
(z3-plazan: -ddPilotStart 0,-4,0, the B9 sin-plazan pose) at Sinematik in topdown and tp, 40 zombies with -ddArmor all
in BOTH builds, 5 windows per cell, R2 / R1 / R2 / R1 on the two release-probe builds:
  R2  <E>/z3/perf/perf-<tier>-<mode>-<n>/      R1  <E>/z3/perf-r1/perf-<tier>-<mode>-<n>/
The spawn roll (tier, version, armour, position) is not fixed, so one window is not a measurement: the n-th counted R2
window of a cell pairs with the n-th counted R1 window, each pair gives R2/R1 of the probe's 04-window setPassCallsAvg,
batchesAvg and trianglesAvg, the cell's value is the median over its pairs (>= 5, else BLOCKED), and the gate takes the
worse mode (max). Counters are counts, not times, so the gate is quiet:false (the runner still measures in its window).

Z3 static keys (Unity: SkinnedMeshRenderer bones, imported texture sizes, material counts) come from <E>/r2/z3-static.json,
written in the chain's r2 stage by an EditMode test after the AP-025 import:
  {"schema": "dd-r2-z3-static-v1", "commit": "<FINAL 40-hex>", "source": "<test full name>",
   "values": {<exactly the Z3 keys of r2-gates.json that are not ratios>}}
Without it r2-Z3.json is NOT written (contract: a missing key is BLOCKED, and the gate file exists only when the
measurement ran); the ratios still land in <E>/r2/z3-ratios.json (dd-r2-z3-ratios-v1: diagnostic, never bound).

P3 (report) reads the chain's B9 windows <E>/b9/<pose>/perf-<tier>-<mode>-<n>/ (R2 only; the S-05 pose list given to
wp078_accept --b9-poses): one cell per pose x tier x camera mode with the medians over its counted windows of the probe's
frameMeanMs, frameP99FilteredMs (the probe's 33.33 ms stall filter), setPassCallsAvg, batchesAvg, trianglesAvg,
drawCallsAvg and the bodies on screen. values = {"cells": <cell count>} (contract: plain numbers); the table is the
artifact's top-level "cells", as in P2.

The source must be in the recording (C-00 06.10) — every window that is used, both builds:
  attempt.json      counted (complete, exit 0, hostClean: wp078_perf_report.classify), executionCommit = FINAL (R2) /
                    --r1-commit (R1), one binarySha256 per side and the two sides different builds;
  command.json      -ddPilotStart x,z,yaw present, -ddQualityTier / -ddCameraMode = the cell, Z3: -ddArmor all;
  01-scene-ready    start.set and start.sampled, start x/z/yaw = the requested pose;
  02-force-40       configuredSafetyCeiling = forcedTarget = achievedCount = 40, not timed out; Z3: note "armor all:";
  04-window         qualityTier / cameraMode = the cell, cameraSweep false, activeMin >= 40, frameCount > 0,
                    setPassCallsAvg, batchesAvg, trianglesAvg, drawCallsAvg > 0 (-1 = recorder unavailable);
  06-readability    enemiesOnScreen > 0;
  across windows    one requested pose, sampled positions within --pose-tolerance metres, one screen size.
A window that breaks one: FAIL, the gate's artifact is not written (exit 1), every broken condition on stderr.
A window that did not complete on a clean host (wp078_perf_report.classify "failed": a crash, a timeout, a missing
artifact) FAILS its cell (C-00 30.09 K8) — only host-only discards are skipped. One attempt counts once (attemptId and
resolved path unique, index 1..n without leading zeros). --min-pairs / --pose-tolerance may only tighten the C-00 06.10
values (>= 5 pairs, <= 0.25 m); both are written into the artifact. A stale r2-<gate>.json in the out-dir is renamed
r2-<gate>.json.stale-<utc> (evidence is never deleted) before the gate runs, so a FAIL / BLOCKED run cannot leave an
older PASS bound.

usage: r2_z3.py --evidence <E> --commit <FINAL 40-hex> --r1-commit <R1 40-hex> [--static <E>/r2/z3-static.json]
                [--out-dir <E>/r2] [--gates Z3,P3] [--b9-poses <b9-poses.json>] [--min-pairs 5] [--pose-tolerance 0.25]
exit: 0 every requested artifact written · 1 one or more not written (stderr says which and why; the other is still
      written) · 64 usage
"""
import argparse
import datetime
import hashlib
import os
import json
import math
import re
import statistics
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import r2_gates  # noqa: E402  (the contract: which keys Z3 carries)
import wp078_perf_report as perf  # noqa: E402  (CELL layout, classify = "counted", MODE_NAMES)

SHA = re.compile(r"[0-9a-f]{40}")
CEILING = 40                         # PrototypeConfig.AbsoluteSafetyCeiling; WP-064 / DEC-088: exactly 40 for the whole window
Z3_R2, Z3_R1 = "z3/perf", "z3/perf-r1"
Z3_TIER, Z3_MODES = "sinematik", ("topdown", "tp")
Z3_START = "0,-4,0"                   # -ddPilotStart of the z3 pose run (z3-plazan = B9 sin-plazan); S-10's chain imports it
B9_ROOT = "b9"
MIN_PAIRS = 5                        # C-00 06.10: at least 5 alternating pairs per cell
POSE_TOLERANCE_M = 0.25              # NavMesh.SamplePosition may land elsewhere if R2 moved the mesh near the pose
POSE_EPSILON = 1e-3                  # the requested x/z/yaw are parsed floats on both sides
RATIOS = (("setPassRatio", "setPassCallsAvg"), ("batchRatio", "batchesAvg"), ("trianglesRatio", "trianglesAvg"))
DETAIL_RATIOS = (("drawCallsRatio", "drawCallsAvg"),)           # shown per cell, never a gate key
COUNTERS = ("setPassCallsAvg", "batchesAvg", "trianglesAvg", "drawCallsAvg")
P3_FIELDS = ("frameMeanMs", "frameP99FilteredMs") + COUNTERS
STATIC_SCHEMA, RATIOS_SCHEMA = "dd-r2-z3-static-v1", "dd-r2-z3-ratios-v1"
STAGE_FILES = ("01-scene-ready.json", "02-force-40.json", "04-window.json", "06-readability.json")
INDEX = re.compile(r"[1-9][0-9]*")       # wp078_perf numbers windows 1..n; "01" would be a copy of "1"
ARMOR_NOTE = re.compile(r"armor all: (?P<n>[0-9]+) pieces fitted")   # PilotPerfProbe stage 02 note


def strict_load(path):
    """Parsed JSON, or None when the file is missing, unreadable, not JSON or carries NaN / Infinity."""
    def reject(value):
        raise ValueError("non-finite JSON number " + value)
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"), parse_constant=reject)
    except (OSError, ValueError):
        return None


def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def flag(command, name):
    """The value after the first `name` in the Player argv (PilotPerfProbe.Arg reads the first one as well)."""
    if not isinstance(command, list) or name not in command:
        return None
    i = command.index(name)
    return command[i + 1] if i + 1 < len(command) and isinstance(command[i + 1], str) else None


def parse_start(text):
    """"x,z,yaw" → (x, z, yaw) floats, or None."""
    parts = text.split(",") if isinstance(text, str) else []
    try:
        values = tuple(float(p) for p in parts)
    except ValueError:
        return None
    return values if len(values) == 3 and all(math.isfinite(v) for v in values) else None


def z3_keys(contract):
    """(ratio keys, static keys) of the Z3 gate in contract order: ratios = the probe ratios this tool computes,
    static = every other Z3 key (checks then report) — those come from z3-static.json."""
    gate = next(g for g in contract["gates"] if g["id"] == "Z3")
    keys = [c["key"] for c in gate.get("checks", [])] + list(gate.get("report", []))
    ratio = [k for k, _ in RATIOS]
    missing = [k for k in ratio if k not in keys]
    if missing:
        raise r2_gates.ContractError("Z3 in r2-gates.json lacks the ratio keys " + ", ".join(missing))
    return ratio, [k for k in keys if k not in ratio]


def load_contract():
    return r2_gates.load_contract((HERE / "r2-gates.json").read_text(encoding="utf-8"))


def load_targets():
    return json.loads((HERE / "perf-targets.json").read_text(encoding="utf-8"))


# ------------------------------------------------------------------------------------------------ windows


def cells(root):
    """{(tier, mode): [window dirs by index]} — wp078_perf's perf-<tier>-<mode>-<n> folders directly under root."""
    out = {}
    if not Path(root).is_dir():
        return out
    for d in Path(root).iterdir():
        m = perf.CELL.fullmatch(d.name)
        if d.is_dir() and m:
            out.setdefault((m["tier"], m["mode"]), []).append((int(m["index"]), d))
    return {key: [d for _, d in sorted(dirs)] for key, dirs in out.items()}


def counted(dirs):
    """The windows wp078_perf_report counts (complete, exit 0, hostClean), in index order; the rest never pair."""
    return [d for d in dirs if perf.classify(strict_load(d / "attempt.json") or {}) == "complete"]


def window_problems(dirs, evidence):
    """C-00 30.09 K8 and one attempt per window: a "failed" attempt (not a host-only discard) fails its cell, and no
    attempt may be counted twice (a copied or linked folder, an index with a leading zero)."""
    problems, ids, paths = [], {}, {}
    for d in dirs:
        rel = _rel(d, evidence)
        attempt = strict_load(d / "attempt.json") or {}
        if perf.classify(attempt) == "failed":
            problems.append(f"{rel}: attempt did not complete on a clean host (status {attempt.get('status')}, exit "
                            f"{attempt.get('exitCode')}, hostClean {attempt.get('hostClean')}, timedOut {attempt.get('timedOut')}) "
                            "— K8: the cell FAILS")
        if not INDEX.fullmatch(perf.CELL.fullmatch(d.name)["index"]):
            problems.append(f"{rel}: window index is not 1..n (leading zero: a copy of another window?)")
        real = os.path.realpath(d)
        if real in paths:
            problems.append(f"{rel}: the same folder as {paths[real]} (a link or copy counted twice)")
        paths.setdefault(real, rel)
        attempt_id = attempt.get("attemptId")
        if isinstance(attempt_id, str) and attempt_id:
            if attempt_id in ids:
                problems.append(f"{rel}: attemptId {attempt_id} is also {ids[attempt_id]} (one attempt counted twice)")
            ids.setdefault(attempt_id, rel)
    return problems


def check_window(directory, rel, commit, tier, mode, targets, armor):
    """The source rule for one counted window → (problems, record). The record is what the tool reads from it."""
    problems = []

    def need(ok, what):
        if not ok:
            problems.append(f"{rel}: {what}")
        return ok

    attempt = strict_load(directory / "attempt.json") or {}
    command = strict_load(directory / "command.json")
    s01, s02, s04, s06 = (strict_load(directory / name) for name in STAGE_FILES)
    need(attempt.get("executionCommit") == commit, f"attempt.json executionCommit {attempt.get('executionCommit')} is not {commit}")
    binary = attempt.get("binarySha256")
    need(isinstance(binary, str) and len(binary) == 64, "attempt.json has no binarySha256 (which build ran is unknown)")
    need(isinstance(command, list), "command.json missing or unreadable")
    requested = parse_start(flag(command, "-ddPilotStart"))
    need(requested is not None, f"command.json -ddPilotStart {flag(command, '-ddPilotStart')!r} is not x,z,yaw (no fixed pose)")
    need(flag(command, "-ddQualityTier") == tier, f"command.json -ddQualityTier {flag(command, '-ddQualityTier')} is not {tier}")
    need(flag(command, "-ddCameraMode") == mode, f"command.json -ddCameraMode {flag(command, '-ddCameraMode')} is not {mode}")
    armor_all = (flag(command, "-ddArmor") or "").lower() == "all"   # the probe compares OrdinalIgnoreCase
    if armor:
        need(armor_all, "command.json has no -ddArmor all (C-00: both builds measure the full-armour crowd)")
    record = {"dir": rel, "index": int(perf.CELL.fullmatch(directory.name)["index"]), "startedUtc": attempt.get("startedUtc"),
              "binarySha256": binary, "requested": list(requested) if requested else None, "sampled": None,
              "screen": None, "armorAll": armor_all}

    if need(isinstance(s01, dict), "01-scene-ready.json missing or unreadable"):
        start = s01.get("start") if isinstance(s01.get("start"), dict) else {}
        if need(start.get("set") is True and start.get("sampled") is True,
                "01-scene-ready start pose not set or not sampled on the NavMesh"):
            got = tuple(start.get(k) for k in ("x", "z", "yaw"))
            if requested is not None:
                need(all(number(g) and abs(g - r) <= POSE_EPSILON for g, r in zip(got, requested)),
                     f"01-scene-ready start {got} is not the requested {requested}")
            if number(start.get("sampledX")) and number(start.get("sampledZ")):
                record["sampled"] = [start["sampledX"], start["sampledZ"]]
            else:
                need(False, "01-scene-ready start has no sampledX / sampledZ")
        record["screen"] = f"{s01.get('screenWidth')}x{s01.get('screenHeight')}"

    if need(isinstance(s02, dict), "02-force-40.json missing or unreadable"):
        trio = (s02.get("configuredSafetyCeiling"), s02.get("forcedTarget"), s02.get("achievedCount"))
        need(s02.get("forceTimedOut") is False and all(v == CEILING and number(v) for v in trio),
             f"02-force-40 configured/forced/achieved {trio[0]}/{trio[1]}/{trio[2]} (timed out {s02.get('forceTimedOut')}), not {CEILING}")
        if armor:
            fitted = ARMOR_NOTE.search(str(s02.get("note") or ""))
            need(fitted is not None and int(fitted["n"]) > 0,
                 f"02-force-40 note {s02.get('note')!r}: no 'armor all: N pieces fitted' with N > 0 (the probe fitted no armour)")
        record["achievedCount"] = s02.get("achievedCount")

    if need(isinstance(s04, dict), "04-window.json missing or unreadable"):
        want_tier = targets["tiers"].get(tier, {}).get("probeValue")
        need(s04.get("qualityTier") == want_tier, f"04-window qualityTier {s04.get('qualityTier')} is not {want_tier}")
        want_mode = perf.MODE_NAMES.get(mode, mode)
        need(s04.get("cameraMode") == want_mode, f"04-window cameraMode {s04.get('cameraMode')} is not {want_mode}")
        need(s04.get("cameraSweep") is False, "04-window cameraSweep is not false (the pose was not held)")
        need(number(s04.get("activeMin")) and s04["activeMin"] >= CEILING,
             f"04-window activeMin {s04.get('activeMin')} < {CEILING} (the 40 were not held through the window)")
        need(number(s04.get("frameCount")) and s04["frameCount"] > 0, f"04-window frameCount {s04.get('frameCount')}: no frame rendered")
        for field in COUNTERS:
            need(number(s04.get(field)) and s04[field] > 0, f"04-window {field} {s04.get(field)} (recorder unavailable or nothing drawn)")
        for field in P3_FIELDS:
            record[field] = s04.get(field)

    if need(isinstance(s06, dict), "06-readability.json missing or unreadable"):
        need(number(s06.get("enemiesOnScreen")) and s06["enemiesOnScreen"] > 0,
             f"06-readability enemiesOnScreen {s06.get('enemiesOnScreen')}: no body on screen")
        record["enemiesOnScreen"] = s06.get("enemiesOnScreen")
    return problems, record


def cross_check(records, tolerance, what):
    """One requested pose, sampled positions within tolerance, one screen size across `records`."""
    problems = []
    usable = [r for r in records if r.get("requested") and r.get("sampled")]
    if not usable:
        return problems
    ref = usable[0]
    for r in usable[1:]:
        if any(abs(a - b) > POSE_EPSILON for a, b in zip(r["requested"], ref["requested"])):
            problems.append(f"{r['dir']}: requested pose {r['requested']} differs from {ref['dir']} {ref['requested']} ({what})")
        dist = math.hypot(r["sampled"][0] - ref["sampled"][0], r["sampled"][1] - ref["sampled"][1])
        if dist > tolerance:
            problems.append(f"{r['dir']}: sampled pose {r['sampled']} is {dist:.3f} m from {ref['dir']} {ref['sampled']} "
                            f"(tolerance {tolerance} m, {what})")
    screens = sorted({r["screen"] for r in records if r.get("screen")})
    if len(screens) > 1:
        problems.append(f"screen sizes differ ({what}): " + ", ".join(screens))
    return problems


def _rel(path, evidence):
    try:
        return str(Path(path).relative_to(evidence))
    except ValueError:
        return str(path)


def _median(values):
    vals = [v for v in values if number(v)]
    return round(statistics.median(vals), 4) if vals else None


# ------------------------------------------------------------------------------------------------ Z3


def z3_ratios(evidence, commit, r1_commit, targets, min_pairs=MIN_PAIRS, tolerance=POSE_TOLERANCE_M):
    """The dd-r2-z3-ratios-v1 record: status OK | FAIL (source not in the recording) | BLOCKED (no run, too few pairs);
    values = the three gate ratios only when OK."""
    evidence = Path(evidence)
    roots = {"r2": evidence / Z3_R2, "r1": evidence / Z3_R1}
    out = {"schema": RATIOS_SCHEMA, "commit": commit, "baselineCommit": r1_commit, "status": "OK", "problems": [],
           "tier": Z3_TIER, "modes": list(Z3_MODES), "minPairs": min_pairs, "poseToleranceM": tolerance,
           "roots": {side: _rel(root, evidence) for side, root in roots.items()}, "values": None, "worstMode": None,
           "pose": None, "samples": 0, "cells": []}
    absent = [f"no {_rel(root, evidence)}/ (the z3 pose run did not write here)" for root in roots.values() if not root.is_dir()]
    if absent:
        out.update(status="BLOCKED", problems=absent)
        return out
    groups = {side: cells(root) for side, root in roots.items()}
    failed, blocked, records, binaries = [], [], [], {"r2": set(), "r1": set()}
    for side in ("r2", "r1"):
        failed += window_problems([d for dirs in groups[side].values() for d in dirs], evidence)
    for mode in Z3_MODES:
        key = (Z3_TIER, mode)
        r2c, r1c = counted(groups["r2"].get(key, [])), counted(groups["r1"].get(key, []))
        pairs = list(zip(r2c, r1c))
        cell = {"tier": Z3_TIER, "mode": mode, "pairs": len(pairs), "r2Counted": len(r2c), "r1Counted": len(r1c),
                "medians": None, "pairRows": []}
        if len(pairs) < min_pairs:
            blocked.append(f"{Z3_TIER}-{mode}: {len(pairs)} pairs (R2 {len(r2c)} / R1 {len(r1c)} counted windows), "
                           f"{min_pairs} needed")
        raw = []
        for a, b in pairs:
            pa, ra = check_window(a, _rel(a, evidence), commit, Z3_TIER, mode, targets, armor=True)
            pb, rb = check_window(b, _rel(b, evidence), r1_commit, Z3_TIER, mode, targets, armor=True)
            failed += pa + pb
            records += [ra, rb]
            binaries["r2"].add(ra["binarySha256"])
            binaries["r1"].add(rb["binarySha256"])
            row = {"r2": ra["dir"], "r1": rb["dir"], "r2StartedUtc": ra["startedUtc"], "r1StartedUtc": rb["startedUtc"],
                   "r2EnemiesOnScreen": ra.get("enemiesOnScreen"), "r1EnemiesOnScreen": rb.get("enemiesOnScreen")}
            for name, field in RATIOS + DETAIL_RATIOS:
                row["r2" + field[0].upper() + field[1:]], row["r1" + field[0].upper() + field[1:]] = ra.get(field), rb.get(field)
            if not pa and not pb:
                ratios = {name: ra[field] / rb[field] for name, field in RATIOS + DETAIL_RATIOS}
                raw.append(ratios)
                row.update({name: round(v, 4) for name, v in ratios.items()})
            cell["pairRows"].append(row)
        if raw and len(raw) == len(pairs):
            cell["medians"] = {name: round(statistics.median(r[name] for r in raw), 4) for name, _ in RATIOS + DETAIL_RATIOS}
        out["cells"].append(cell)
        out["samples"] += len(pairs)
    failed += cross_check(records, tolerance, "the z3 pose in both builds and both modes")
    want = parse_start(Z3_START)
    failed += [f"{r['dir']}: requested pose {r['requested']} is not Z3_START {Z3_START}" for r in records
               if r.get("requested") and any(abs(a - b) > POSE_EPSILON for a, b in zip(r["requested"], want))]
    if records:
        if len(binaries["r2"]) != 1 or len(binaries["r1"]) != 1 or binaries["r2"] & binaries["r1"]:
            failed.append(f"builds: R2 windows ran {sorted(map(str, binaries['r2']))}, R1 windows ran "
                          f"{sorted(map(str, binaries['r1']))} — one build per side and two different builds expected")
        ref = next((r for r in records if r.get("requested")), None)
        if ref:
            out["pose"] = {"requested": ref["requested"],
                           "sampledR2": next((r["sampled"] for r in records[0::2] if r.get("sampled")), None),
                           "sampledR1": next((r["sampled"] for r in records[1::2] if r.get("sampled")), None)}
    if failed or blocked:
        out.update(status="FAIL" if failed else "BLOCKED", problems=failed + blocked)
        return out
    worst = {}
    values = {}
    for name, _ in RATIOS:
        cell = max(out["cells"], key=lambda c: c["medians"][name])
        values[name], worst[name] = cell["medians"][name], cell["mode"]
    out.update(values=values, worstMode=worst)
    return out


def load_static(path, commit, static_keys, evidence):
    """(values in contract order, provenance) or (None, problem) for <E>/r2/z3-static.json."""
    path = Path(path)
    if not path.is_file():
        return None, f"no {_rel(path, evidence)} (the static EditMode test has not written it: AP-025 import pending?)"
    data = strict_load(path)
    if not isinstance(data, dict) or data.get("schema") != STATIC_SCHEMA:
        return None, f"{_rel(path, evidence)} is not a {STATIC_SCHEMA} JSON object (NaN / Infinity are refused)"
    if data.get("commit") != commit:
        return None, f"{_rel(path, evidence)} was written on {data.get('commit')}, not the FINAL {commit}"
    values = data.get("values")
    if not isinstance(values, dict):
        return None, f"{_rel(path, evidence)} has no values"
    missing = [k for k in static_keys if not (number(values.get(k)) and values[k] > 0)]   # a count / size / MB: -1 is a sentinel, not a value
    extra = sorted(set(values) - set(static_keys))
    if missing or extra:
        return None, (f"{_rel(path, evidence)} values must be exactly the static Z3 keys as numbers > 0: "
                      + (("missing/non-numeric/<= 0 " + ", ".join(missing)) if missing else "")
                      + ("; " if missing and extra else "") + (("unknown " + ", ".join(extra)) if extra else ""))
    ref = {"path": _rel(path, evidence), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "source": data.get("source")}
    return {k: values[k] for k in static_keys}, ref


def z3_artifact(ratios, static_values, static_ref, ratio_keys, static_keys):
    values = dict(ratios["values"])
    values.update(static_values)
    return {"schema": "dd-r2-measure-v1", "gate": "Z3", "commit": ratios["commit"], "baselineCommit": ratios["baselineCommit"],
            "source": "tools/qa/r2_z3.py (PilotPerfProbe 04-window R2/R1 pairs <E>/z3/perf vs <E>/z3/perf-r1 + <E>/r2/z3-static.json)",
            "values": {k: values[k] for k in list(ratio_keys) + list(static_keys)}, "samples": ratios["samples"],
            "worstMode": ratios["worstMode"], "minPairs": ratios["minPairs"], "poseToleranceM": ratios["poseToleranceM"],
            "pose": ratios["pose"], "cells": ratios["cells"], "static": static_ref}


# ------------------------------------------------------------------------------------------------ P3


def p3_cells(evidence, commit, targets, poses=None, tolerance=POSE_TOLERANCE_M):
    """(status OK | FAIL | BLOCKED, problems, cells, windows) from <E>/b9/<pose>/perf-*; poses = the B9 list (optional):
    every listed pose x tier x mode must have a cell and its requested start must be the listed one."""
    evidence = Path(evidence)
    root = evidence / B9_ROOT
    if not root.is_dir():
        return "BLOCKED", [f"no {B9_ROOT}/ (the B9 pose windows did not run: wp078_accept --b9-poses)"], [], 0
    failed, blocked, out, windows = [], [], [], 0
    listed = {p["name"]: p for p in poses or []}
    seen = set()
    for pose_dir in sorted(d for d in root.iterdir() if d.is_dir()):
        groups = cells(pose_dir)
        if not groups:
            continue
        pose_records = []
        for (tier, mode), dirs in sorted(groups.items()):
            label = f"{B9_ROOT}/{pose_dir.name}/{tier}-{mode}"
            if tier not in targets["tiers"]:
                failed.append(f"{label}: tier {tier} is not in perf-targets.json")
                continue
            seen.add((pose_dir.name, tier, mode))
            failed += window_problems(dirs, evidence)
            used = counted(dirs)
            if not used:
                blocked.append(f"{label}: no counted window ({len(dirs)} attempts)")
            records = []
            for d in used:
                problems, record = check_window(d, _rel(d, evidence), commit, tier, mode, targets, armor=False)
                failed += problems
                records.append(record)
            if pose_dir.name in listed:
                want = parse_start(listed[pose_dir.name]["start"])
                for r in records:
                    if r["requested"] and any(abs(a - b) > POSE_EPSILON for a, b in zip(r["requested"], want)):
                        failed.append(f"{r['dir']}: requested pose {r['requested']} is not the B9 list's {list(want)}")
            pose_records += records
            windows += len(records)
            cell = {"pose": pose_dir.name, "tier": tier, "mode": mode, "windows": len(records),
                    "start": next((r["requested"] for r in records if r["requested"]), None),
                    "sampled": next((r["sampled"] for r in records if r["sampled"]), None),
                    "armorAll": bool(records) and all(r["armorAll"] for r in records)}
            for field in P3_FIELDS + ("enemiesOnScreen",):
                cell[field] = _median(r.get(field) for r in records)
            cell["windowRows"] = [{k: r.get(k) for k in ("dir", "startedUtc") + P3_FIELDS + ("enemiesOnScreen",)} for r in records]
            out.append(cell)
        failed += cross_check(pose_records, tolerance, f"pose {pose_dir.name}")
    for name, pose in listed.items():
        for tier in pose["tiers"].split(","):
            for mode in pose["modes"].split(","):
                if (name, tier, mode) not in seen:
                    blocked.append(f"{B9_ROOT}/{name}/{tier}-{mode}: listed in the B9 poses, no cell")
    if not out and not failed and not blocked:
        blocked.append(f"{B9_ROOT}/ holds no perf-<tier>-<mode>-<n> window")
    status = "FAIL" if failed else "BLOCKED" if blocked else "OK"
    return status, failed + blocked, out, windows


def p3_artifact(commit, cell_rows, windows, tolerance=POSE_TOLERANCE_M):
    return {"schema": "dd-r2-measure-v1", "gate": "P3", "commit": commit,
            "source": "tools/qa/r2_z3.py (PilotPerfProbe 04-window / 06-readability, <E>/b9/<pose>/perf-*)",
            "values": {"cells": len(cell_rows)}, "samples": windows, "poseToleranceM": tolerance, "cells": cell_rows}


# ------------------------------------------------------------------------------------------------ CLI


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def set_aside_stale(out_dir, gate):
    """A previous r2-<gate>.json is renamed (never deleted: it is evidence) before the gate runs again, so a FAIL /
    BLOCKED run cannot leave an older artifact bound as this run's result."""
    path = out_dir / f"r2-{gate}.json"
    if path.exists():
        stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        target = path.with_name(f"{path.name}.stale-{stamp}")
        path.rename(target)
        print(f"{gate}: the previous {path.name} set aside as {target.name}", file=sys.stderr)


def say_problems(gate, verdict, problems):
    print(f"{gate}: {verdict} — nothing written, {gate} stays BLOCKED in the report:", file=sys.stderr)
    for p in problems:
        print("  - " + p, file=sys.stderr)


def run_z3(evidence, out_dir, static_path, commit, r1_commit, targets, contract, min_pairs, tolerance):
    ratio_keys, static_keys = z3_keys(contract)
    set_aside_stale(out_dir, "Z3")
    ratios = z3_ratios(evidence, commit, r1_commit, targets, min_pairs, tolerance)
    write_json(out_dir / "z3-ratios.json", ratios)
    if ratios["status"] != "OK":
        verdict = "FAIL (the source is not in the recording)" if ratios["status"] == "FAIL" else "BLOCKED"
        say_problems("Z3", verdict, ratios["problems"])
        return False
    static_values, ref = load_static(static_path, commit, static_keys, Path(evidence))
    v = ratios["values"]
    line = (f"setPassRatio {v['setPassRatio']} batchRatio {v['batchRatio']} trianglesRatio {v['trianglesRatio']} "
            f"(worst of {', '.join(Z3_MODES)}; {ratios['samples']} pairs)")
    if static_values is None:
        say_problems("Z3", "BLOCKED (static keys)", [ref, "ratios recorded in z3-ratios.json: " + line])
        return False
    write_json(out_dir / "r2-Z3.json", z3_artifact(ratios, static_values, ref, ratio_keys, static_keys))
    print(f"Z3 written: {line}; static from {ref['path']}")
    return True


def run_p3(evidence, out_dir, commit, targets, poses, tolerance):
    set_aside_stale(out_dir, "P3")
    status, problems, cell_rows, windows = p3_cells(evidence, commit, targets, poses, tolerance)
    if status != "OK":
        say_problems("P3", "FAIL (the source is not in the recording)" if status == "FAIL" else "BLOCKED", problems)
        return False
    write_json(out_dir / "r2-P3.json", p3_artifact(commit, cell_rows, windows, tolerance))
    print(f"P3 written: {len(cell_rows)} cells from {windows} windows")
    return True


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--evidence", required=True, help="the R2 FINAL evidence root <E>")
    parser.add_argument("--commit", required=True, help="the R2 FINAL commit, 40-hex")
    parser.add_argument("--r1-commit", help="the R1 FINAL commit, 40-hex (Z3: the baseline windows' executionCommit)")
    parser.add_argument("--static", help="z3-static.json (default <E>/r2/z3-static.json)")
    parser.add_argument("--out-dir", help="where r2-Z3.json / r2-P3.json / z3-ratios.json go (default <E>/r2)")
    parser.add_argument("--gates", default="Z3,P3", help="Z3, P3 or both (default)")
    parser.add_argument("--b9-poses", help="P3: the S-05 B9 pose list (wp078_accept --b9-poses); every listed cell is required")
    parser.add_argument("--min-pairs", type=int, default=MIN_PAIRS)
    parser.add_argument("--pose-tolerance", type=float, default=POSE_TOLERANCE_M, help="metres between sampled poses")
    args = parser.parse_args(argv)
    gates = [g.strip().upper() for g in args.gates.split(",") if g.strip()]
    if not gates or set(gates) - {"Z3", "P3"}:
        print("--gates takes Z3, P3 or Z3,P3", file=sys.stderr)
        return 64
    if not SHA.fullmatch(args.commit) or ("Z3" in gates and not (args.r1_commit and SHA.fullmatch(args.r1_commit))):
        print("--commit (and for Z3 --r1-commit) must be 40-hex", file=sys.stderr)
        return 64
    if "Z3" in gates and args.r1_commit == args.commit:
        print("--r1-commit must differ from --commit (R2 over R1 needs two builds)", file=sys.stderr)
        return 64
    if args.min_pairs < MIN_PAIRS or not (math.isfinite(args.pose_tolerance) and 0 <= args.pose_tolerance <= POSE_TOLERANCE_M):
        print(f"--min-pairs >= {MIN_PAIRS} and 0 <= --pose-tolerance <= {POSE_TOLERANCE_M} (C-00 06.10: tighten only)", file=sys.stderr)
        return 64
    evidence = Path(args.evidence).resolve()
    if not evidence.is_dir():
        print(f"no such evidence root: {evidence}", file=sys.stderr)
        return 64
    poses = None
    if args.b9_poses:
        import wp078_accept   # the one B9 pose list parser (lazy: only P3 with a list needs it)
        try:
            poses = wp078_accept.load_b9_poses(args.b9_poses)
        except (OSError, ValueError) as error:
            print(f"--b9-poses: {error}", file=sys.stderr)
            return 64
    out_dir = Path(args.out_dir).resolve() if args.out_dir else evidence / "r2"
    static = Path(args.static).resolve() if args.static else evidence / "r2" / "z3-static.json"
    targets = load_targets()
    ok = True
    if "Z3" in gates:
        ok = run_z3(evidence, out_dir, static, args.commit, args.r1_commit, targets, load_contract(),
                    args.min_pairs, args.pose_tolerance) and ok
    if "P3" in gates:
        ok = run_p3(evidence, out_dir, args.commit, targets, poses, args.pose_tolerance) and ok
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
