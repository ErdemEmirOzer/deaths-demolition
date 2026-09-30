#!/usr/bin/env python3
"""Aggregate WP-061 stage evidence into one concise RC report."""
import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path
import time
import xml.etree.ElementTree as ET


# C-00 30.09 Y6 (TANI-TESTI-POLITIKASI): a skip is allowed only when the test case is Skipped, Explicit (label or runstate)
# and carries Category("Diagnostics") (on the case or an enclosing suite) — never by a name list. The former
# SetPassAttributionTests fullname exception is gone: that fixture is [Explicit] + Diagnostics since 25ed95c5 (S-05).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from nunit_skips import classify_skips, is_diagnostic_explicit, skipped_cases  # noqa: E402,F401  (Y6: one rule for every gate)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON key: " + key)
        result[key] = value
    return result


def invalid_constant(value):
    raise ValueError("non-finite JSON number: " + value)


def read_json(path):
    try:
        with open(path, encoding="utf-8") as handle:
            value = json.load(handle, object_pairs_hook=unique_object, parse_constant=invalid_constant)
            return value if isinstance(value, dict) else None
    except Exception:
        return None


def exit_code(root, name):
    try:
        with open(os.path.join(root, "stages", name, "exit.txt"), encoding="utf-8") as handle:
            return int(handle.read().strip())
    except Exception:
        return None


def xml_counts(path):
    try:
        root = ET.parse(path).getroot()
        return {key: int(root.get(key, 0)) for key in ("total", "passed", "failed", "skipped", "inconclusive")}
    except Exception:
        return None


def testcase_passed(path, suffix):
    try:
        root = ET.parse(path).getroot()
        matches = [case for case in root.iter("test-case") if (case.get("fullname") or case.get("name") or "").endswith(suffix)]
        return bool(matches) and all((case.get("result") or "").lower() == "passed" for case in matches)
    except Exception:
        return False


def testcase_names(path, result):
    try:
        root = ET.parse(path).getroot()
        return sorted(
            case.get("fullname") or case.get("name") or "unknown"
            for case in root.iter("test-case")
            if (case.get("result") or "").lower() == result.lower()
        )
    except Exception:
        return []


def normal_bot_report(bot, rounds):
    return bool(isinstance(bot, dict) and bot.get("runMode") == "normal"
                and type(bot.get("seed")) is int and bot["seed"] in (61, 26)
                and type(bot.get("roundsRequested")) is int and bot["roundsRequested"] == rounds
                and type(bot.get("roundsCompleted")) is int and 0 <= bot["roundsCompleted"] <= rounds)


# RC invokes the shipped drivers with these rounds and skins (rc.sh/regression.sh).
READABILITY_ROUNDS = (1, 4, 8, 10, 12)
BOT_GATES = {"bot.alloc.method", "bot.alloc.perFrame", "bot.rounds", "bot.determinism"}
SPAWN_GATES = {"spawnmix.violations", "spawnmix.simulated", "spawnmix.notes"}


def contract_items(report, field, identifier):
    """Never collapse duplicate identifiers or coerce a purported PASS to boolean."""
    items = report.get(field) if isinstance(report, dict) else None
    if not isinstance(items, list) or not items:
        return None
    result = {}
    for item in items:
        if not isinstance(item, dict):
            return None
        key = item.get(identifier)
        if not isinstance(key, str) or not key or key in result or type(item.get("pass")) is not bool or "value" not in item:
            return None
        if field == "gates" and any(not isinstance(item.get(k), str) for k in ("value", "threshold", "note", "source")):
            return None
        result[key] = item
    return result


def qa_contract(report, driver):
    gates = contract_items(report, "gates", "id")
    return gates if (gates and report.get("schema") == "dd-qa-report-v1" and report.get("driver") == driver
                     and type(report.get("pass")) is bool
                     and report["pass"] is all(g["pass"] for g in gates.values())) else None


def m3_required(perf_runs):
    names = {"editmode.qa", "readability.armorForm.dec077", "readability.ultraEvidence",
             "perf.meanMs", "perf.cleanP99Ms", "perf.externalPresentContention", "perf.hudCostMs"} | BOT_GATES | SPAWN_GATES
    names.update(f"perf.run{i}.{kind}" for i in range(1, perf_runs + 1)
                 for kind in ("integrity", "metrics", "meanMs", "cleanP99Ms"))
    names.update(f"readability.r{r}.{kind}" for r in READABILITY_ROUNDS
                 for kind in ("occlusion", "zombieCrowdContrast", *(f"{k}.skin{s}" for s in (0, 1, 2)
                              for k in ("playerEnvContrast", "playerZombieContrast"))))
    return names


def m3_acceptance(report, bot, rounds, perf_runs=3):
    """DEC-091 changes only bot.rounds. All mandatory producer gates remain present."""
    gates = qa_contract(report, "aggregate")
    return bool(gates and type(perf_runs) is int and perf_runs >= 3 and m3_required(perf_runs) <= gates.keys()
                and any(key.endswith("tierEnvContrast.Ultra") for key in gates)
                and all(g["pass"] is True for key, g in gates.items() if key != "bot.rounds")
                and normal_bot_report(bot, rounds))


def child_contracts(root):
    for folder, driver, expected in (("spawn-mix", "SpawnMix", SPAWN_GATES), ("bot", "QaRunBot", BOT_GATES)):
        gates = qa_contract(read_json(root / folder / "qa-report.json"), driver)
        if not gates or gates.keys() != expected:
            return False
    report = read_json(root / "readability" / "qa-report.json")
    gates = qa_contract(report, "QaReadabilityGate")
    summary = read_json(root / "readability" / "readability-summary.json")
    rows = (summary or {}).get("rounds")
    if not gates or not summary or summary.get("schema") != "dd-qa-readability-v1" or summary.get("pass") is not True or not isinstance(rows, list):
        return False
    seen, required = set(), set()
    for row in rows:
        if not isinstance(row, dict) or type(row.get("round")) is not int or row["round"] in seen or row.get("pass") is not True:
            return False
        r = row["round"]
        seen.add(r)
        required.update(f"readability.r{r}.{kind}" for kind in ("occlusion", "zombieCrowdContrast"))
        required.update(f"readability.r{r}.{kind}.skin{s}" for s in (0, 1, 2) for kind in ("playerEnvContrast", "playerZombieContrast"))
        capture = row.get("captureTier")
        pixels, contrasts = row.get("tierPixels"), row.get("tierEnvContrast")
        if type(capture) is not int or capture not in (-1, 0, 1, 2, 3):
            return False
        if capture > 0:
            required.add(f"readability.r{r}.capture.{('Standard', 'Special', 'Elite', 'Ultra')[capture]}")
        if contrasts is not None:
            if not isinstance(contrasts, list) or len(contrasts) != 4 or not isinstance(pixels, list) or len(pixels) != 4 or any(type(p) is not int or p < 0 for p in pixels):
                return False
            for t, name in enumerate(("Standard", "Special", "Elite", "Ultra")):
                if t and pixels[t] > 0:
                    required.update((f"readability.r{r}.tierEnvContrast.{name}", f"readability.r{r}.tierVsStandard.{name}"))
    return seen == set(READABILITY_ROUNDS) and gates.keys() == required


def producer_env(edit, play):
    env = dict(os.environ, DD_QA_PACKAGE="WP-061", DD_REQUIRE_ULTRA_EVIDENCE="1", DD_RC_M3_REPORT_ONLY="0",
               DD_EXTERNAL_EDITMODE_XML=str(Path(edit).resolve()), DD_EXTERNAL_PLAYMODE_XML=str(Path(play).resolve()),
               PYTHONDONTWRITEBYTECODE="1")
    return env


def canonical(value):
    return json.dumps(value, sort_keys=True, allow_nan=False, separators=(",", ":"))


def m3_replay(root, report, bot, rounds, perf_runs, window):
    m3 = (Path(root) / "m3").resolve()
    if not (m3_acceptance(report, bot, rounds, perf_runs) and child_contracts(m3)
            and report.get("rcPass") is True and report.get("rcReportOnlyGates") == ["bot.rounds"]
            and type(report.get("rcOriginalAggregateExit")) is int
            and report["rcOriginalAggregateExit"] == (0 if report["pass"] else 1)):
        return False
    # aggregate.py writes two receipts. Link input children only; its output stays temporary.
    with tempfile.TemporaryDirectory(prefix="dd-rc-verify-") as directory:
        mirror = Path(directory)
        for name in ("build-info.json", "build-dev", "spawn-mix", "bot", "readability",
                     *(f"perf-{i}" for i in range(1, perf_runs + 1))):
            source = m3 / name
            if source.exists():
                (mirror / name).symlink_to(source, target_is_directory=source.is_dir())
        run = subprocess.run([sys.executable, str(Path(__file__).with_name("aggregate.py")), str(mirror),
                              "--window", str(window), "--perf-runs", str(perf_runs)], capture_output=True, text=True,
                             env=producer_env(Path(root) / "full-editmode/editmode.xml", Path(root) / "full-playmode/playmode.xml"))
        replay = read_json(mirror / "qa-report.json")
    if run.returncode not in (0, 1) or not m3_acceptance(replay, bot, rounds, perf_runs) or run.returncode != (0 if replay["pass"] else 1):
        return False
    original_gates, replay_gates = qa_contract(report, "aggregate"), qa_contract(replay, "aggregate")
    def policy(items):
        return {key: {k: v for k, v in item.items() if k != "source" or key not in ("editmode.qa", "readability.armorForm.dec077")}
                for key, item in items.items()}
    return (canonical(policy(original_gates)) == canonical(policy(replay_gates))
            and all(type(report.get(k)) is type(replay.get(k)) and report.get(k) == replay.get(k)
                    for k in ("schema", "driver", "package", "pass", "seed", "window")))


def m3_report_only():
    """The producer runs first; incomplete or malformed child contracts cannot grant RC PASS."""
    original = subprocess.run([sys.executable, str(Path(__file__).with_name("aggregate.py")), *sys.argv[1:]])
    path = Path(sys.argv[1]) / "qa-report.json"
    report = read_json(path)
    bot = read_json(path.parent / "bot/bot-summary.json")
    rounds = (bot or {}).get("roundsRequested")
    try:
        perf_runs = int(sys.argv[sys.argv.index("--perf-runs") + 1]) if "--perf-runs" in sys.argv else 3
    except (ValueError, IndexError):
        perf_runs = 0
    accepted = bool(original.returncode in (0, 1) and type(rounds) is int and rounds >= 12
                    and m3_acceptance(report, bot, rounds, perf_runs) and child_contracts(path.parent)
                    and original.returncode == (0 if report["pass"] else 1))
    if report is not None:
        report.update(rcPass=accepted, rcOriginalAggregateExit=original.returncode, rcReportOnlyGates=["bot.rounds"])
        path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        with (path.parent / "qa-report.txt").open("a", encoding="utf-8") as handle:
            handle.write(f"\nRC DEC-091 {'PASS' if accepted else 'FAIL'}; original aggregate exit={original.returncode}; bot.rounds report only\n")
    return 0 if accepted else (original.returncode or 1)


def stability_evidence(root, rounds, closure, head, repo, window=60):
    summaries = {str(seed): read_json(Path(root) / "m3" / f"soak-{seed}" / "bot-summary.json") for seed in (61, 26)}
    checks = contract_items(closure, "checks", "name")
    # RC always uses wp064_run.py: a legacy external build cannot replace a missing run receipt.
    report_attempts = [read_json(Path(root) / "m3" / name / "attempt.json")
                       for name in ("bot", "real-2", "real-3", "real-4", "real-5")]
    attempts_present = all(item and item.get("schema") == "dd-wp064-attempt-v1" for item in report_attempts)
    raw_ok = all(isinstance(s, dict) and s.get("runMode") == "stability" and s.get("pass") is False
                 and s.get("stabilityPass") is True and type(s.get("seed")) is int and s["seed"] == seed
                 and type(s.get("roundsRequested")) is int and s["roundsRequested"] == rounds
                 and type(s.get("roundsCompleted")) is int and s["roundsCompleted"] == rounds
                 for seed, s in ((seed, summaries[str(seed)]) for seed in (61, 26)))
    if not (checks and raw_ok and attempts_present and closure.get("schema") == "dd-wp064-closure-v4" and closure.get("pass") is True
            and head != "unknown" and closure.get("executionCommit") == head and all(c["pass"] for c in checks.values())):
        return False, summaries
    # Recompute the whole current producer contract, including all build, Library, ledger,
    # perf, log and immutable attempt proofs. Never let closure claims select the inputs.
    run = subprocess.run([sys.executable, str(Path(__file__).with_name("wp064_gate.py")), str(Path(root) / "m3"),
                          "--release", str(Path(root) / "release"), "--commit", head, "--repo", str(repo),
                          "--soak-dirs", "soak-61,soak-26", "--seeds", "61,26",
                          "--report-dirs", "bot,real-2,real-3,real-4,real-5", "--rounds", str(rounds),
                          "--window", str(window), "--verify-only"], capture_output=True, text=True,
                         env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1"))
    try:
        replay = json.loads(run.stdout, object_pairs_hook=unique_object, parse_constant=invalid_constant)
        replay_checks = contract_items(replay, "checks", "name")
        valid = (run.returncode == 0 and replay_checks and replay.get("pass") is True
                 and all(c["pass"] for c in replay_checks.values()) and canonical(closure) == canonical(replay))
    except (ValueError, TypeError):
        valid = False
    return bool(valid), summaries


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("out")
    parser.add_argument("--repo", required=True)
    parser.add_argument("--rounds", type=int, required=True)
    parser.add_argument("--start-epoch", type=int, required=True)
    parser.add_argument("--failed-stage", default="")
    parser.add_argument("--perf-runs", type=int, default=3)
    parser.add_argument("--window", type=int, default=60)
    args = parser.parse_args()

    # release-library is MUST: post-build receipt; wp064-closure validates its content/commit/Assets/ArtifactDB.
    stages = ["full-editmode", "full-playmode", "release-smoke", "release-library", "m3-regression", "soak-stability-61", "soak-stability-26", "wp064-closure", "economy-acceptance", "window-modes", "log-scan"]
    stage_results = {name: exit_code(args.out, name) for name in stages}
    edit = xml_counts(os.path.join(args.out, "full-editmode", "editmode.xml"))
    play = xml_counts(os.path.join(args.out, "full-playmode", "playmode.xml"))
    release = read_json(os.path.join(args.out, "release", "report.json"))
    m3 = read_json(os.path.join(args.out, "m3", "qa-report.json"))
    bot = read_json(os.path.join(args.out, "m3", "bot", "bot-summary.json"))
    closure = read_json(os.path.join(args.out, "m3", "wp064-report.json"))
    library = read_json(os.path.join(args.out, "release", "library-provenance.json"))
    windows = read_json(os.path.join(args.out, "window-modes", "qa-report.json"))
    logs = read_json(os.path.join(args.out, "log-scan.json"))
    allowed_edit_skips, _, unexpected_edit_skips = classify_skips(os.path.join(args.out, "full-editmode", "editmode.xml"))
    allowed_play_skips, _, unexpected_play_skips = classify_skips(os.path.join(args.out, "full-playmode", "playmode.xml"))
    exact_tests = {
        "B11.flow": testcase_passed(os.path.join(args.out, "full-playmode", "playmode.xml"), "FlowSmokeTest.Akis_BootMainMenuSelectionRunOlumResultsMainMenu"),
        "B11.roundHud": testcase_passed(os.path.join(args.out, "full-playmode", "playmode.xml"), "RoundHudSyncTests.TurIlerleyinceHudRakamiDaIlerler"),
        "M11-M16.economy": testcase_passed(os.path.join(args.out, "full-playmode", "playmode.xml"), "GoldUpgradeSliceTests.P10_P19_RoundBitisi_Sweep_Ekran_SatinAlma_SonrakiRound"),
        "M11-M16.metaUi": testcase_passed(os.path.join(args.out, "full-playmode", "playmode.xml"), "MetaUiSliceTests.Results_SunumOzetleEslesir_UpgradeBirKez_HudSonDegeriKorur"),
    }
    try:
        head = subprocess.check_output(["git", "-C", args.repo, "rev-parse", "HEAD"], text=True).strip()
    except Exception:
        head = "unknown"

    readability = qa_contract(m3, "aggregate") or {}
    ultra = [g for key, g in readability.items() if key.endswith("tierEnvContrast.Ultra")]
    soak_ok, stability = stability_evidence(args.out, args.rounds, closure, head, args.repo, args.window)
    bot_report_ok = normal_bot_report(bot, args.rounds)
    m3_ok = m3_replay(args.out, m3, bot, args.rounds, args.perf_runs, args.window)
    library_ok = bool(library and library.get("schema") == "dd-wp064-library-provenance-v1"
                      and library.get("phase") == "post-build" and head != "unknown" and library.get("head") == head)
    # Y6: an EditMode skip is fine only as Skipped ∧ Explicit ∧ Diagnostics; an Ignore skip is an unexpected skip → red
    edit_ok = bool(edit and edit["failed"] == 0 and edit["inconclusive"] == 0 and edit["total"] > 0 and not unexpected_edit_skips
                   and edit["skipped"] == len(allowed_edit_skips))
    play_ok = bool(play and play["failed"] == 0 and play["inconclusive"] == 0 and play["total"] > 0 and not unexpected_play_skips)
    tests_ok = edit_ok and play_ok
    # Y2 (C-00 30.09): rc.sh names a failed stage also for failures outside the eleven stage exits (a real-bot report
    # run's guard, a signal) — the report can never say PASS when the chain says it failed.
    stage_ok = all(stage_results[name] == 0 for name in stages) and not args.failed_stage
    overall = stage_ok and tests_ok and all(exact_tests.values()) and soak_ok and bot_report_ok and library_ok and bool(release and release.get("ok") is True) and m3_ok and bool(windows and windows.get("pass") is True) and bool(logs and logs.get("pass") is True) and bool(ultra) and all(g.get("pass") for g in ultra)

    trace = {
        "B11": ["m3/bot/bot-summary.json", "m3/bot/ui/upgrade.png", "full-playmode/playmode.xml"],
        "C2-C4": ["window-modes/qa-report.json", "window-modes/*.png"],
        "M11-M16": ["m3/bot/bot-summary.json", "m3/bot/ui/upgrade.png", "m3/bot/ui/hud-r2.png", "full-playmode/playmode.xml"],
        "Ultra readability": ["m3/readability/qa-report.json", "m3/bot/tier-frames/*ultra*.png"],
    }
    report = {
        "schema": "dd-rc-report-v1",
        "package": "WP-061",
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "head": head,
        "pass": overall,
        "failedStage": args.failed_stage,
        "stages": stage_results,
        "tests": {"editMode": edit, "playMode": play},
        "allowedEditModeSkips": allowed_edit_skips,
        "unexpectedEditModeSkips": unexpected_edit_skips,
        "allowedPlayModeSkips": allowed_play_skips,
        "unexpectedPlayModeSkips": unexpected_play_skips,
        "exactTests": exact_tests,
        "release": release,
        "soak": bot,
        "stability": {"pass": soak_ok, "summaries": stability, "closure": closure},
        "realBotReportPresent": bot_report_ok,
        "releaseLibrary": {"pass": library_ok, "provenance": library},
        "ultraGates": ultra,
        "windowModes": windows,
        "logScan": logs,
        "traceability": trace,
    }
    with open(os.path.join(args.out, "rc-report.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)

    lines = [
        f"WP-061 RC {'PASS' if overall else 'FAIL'} · head {head[:12]} · failedStage {args.failed_stage or '-'}",
        f"tests edit {edit or 'missing'} · play {play or 'missing'}",
        f"stability seeds 61+26 {'PASS' if soak_ok else 'FAIL'} · real bot REPORT {((bot or {}).get('roundsCompleted', 0))}/{args.rounds} · deaths {((bot or {}).get('deaths', 'missing'))}",
        f"Ultra gates {len(ultra)} ({sum(1 for gate in ultra if gate.get('pass'))} pass) · windows {bool(windows and windows.get('pass'))} · logs {bool(logs and logs.get('pass'))}",
        "evidence " + args.out,
    ]
    with open(os.path.join(args.out, "rc-report.txt"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 0 if overall else 1


if __name__ == "__main__":
    raise SystemExit(m3_report_only() if os.environ.get("DD_RC_M3_REPORT_ONLY") == "1" else main())
