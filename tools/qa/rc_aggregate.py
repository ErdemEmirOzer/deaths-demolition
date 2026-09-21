#!/usr/bin/env python3
"""Aggregate WP-061 stage evidence into one concise RC report."""
import argparse
import json
import os
import subprocess
import time
import xml.etree.ElementTree as ET


ALLOWED_PLAYMODE_SKIPS = {
    "DD.Art.Tests.PlayMode.SetPassAttributionTests.SetPass_Kaynaklari_40ZombiTamZirh",
}


def read_json(path):
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("out")
    parser.add_argument("--repo", required=True)
    parser.add_argument("--rounds", type=int, required=True)
    parser.add_argument("--start-epoch", type=int, required=True)
    parser.add_argument("--failed-stage", default="")
    args = parser.parse_args()

    stages = ["full-editmode", "full-playmode", "release-smoke", "m3-regression", "soak-exact", "economy-acceptance", "window-modes", "log-scan"]
    stage_results = {name: exit_code(args.out, name) for name in stages}
    edit = xml_counts(os.path.join(args.out, "full-editmode", "editmode.xml"))
    play = xml_counts(os.path.join(args.out, "full-playmode", "playmode.xml"))
    release = read_json(os.path.join(args.out, "release", "report.json"))
    m3 = read_json(os.path.join(args.out, "m3", "qa-report.json"))
    bot = read_json(os.path.join(args.out, "m3", "bot", "bot-summary.json"))
    windows = read_json(os.path.join(args.out, "window-modes", "qa-report.json"))
    logs = read_json(os.path.join(args.out, "log-scan.json"))
    play_skips = testcase_names(os.path.join(args.out, "full-playmode", "playmode.xml"), "skipped")
    allowed_play_skips = [name for name in play_skips if name in ALLOWED_PLAYMODE_SKIPS]
    unexpected_play_skips = [name for name in play_skips if name not in ALLOWED_PLAYMODE_SKIPS]
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

    readability = (m3 or {}).get("gates", [])
    ultra = [g for g in readability if str(g.get("id", "")).endswith("tierEnvContrast.Ultra")]
    soak_ok = bool(bot and bot.get("roundsCompleted", 0) >= args.rounds and bot.get("deaths", 0) == 0 and bot.get("pass"))
    edit_ok = bool(edit and edit["failed"] == 0 and edit["skipped"] == 0 and edit["inconclusive"] == 0 and edit["total"] > 0)
    play_ok = bool(play and play["failed"] == 0 and play["inconclusive"] == 0 and play["total"] > 0 and not unexpected_play_skips)
    tests_ok = edit_ok and play_ok
    stage_ok = all(stage_results[name] == 0 for name in stages)
    overall = stage_ok and tests_ok and all(exact_tests.values()) and soak_ok and bool(release and release.get("ok")) and bool(m3 and m3.get("pass")) and bool(windows and windows.get("pass")) and bool(logs and logs.get("pass")) and bool(ultra) and all(g.get("pass") for g in ultra)

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
        "allowedPlayModeSkips": allowed_play_skips,
        "unexpectedPlayModeSkips": unexpected_play_skips,
        "exactTests": exact_tests,
        "release": release,
        "soak": bot,
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
        f"soak {((bot or {}).get('roundsCompleted', 0))}/{args.rounds} · deaths {((bot or {}).get('deaths', 'missing'))} · upgrades {((bot or {}).get('upgradesBought', 'missing'))}",
        f"Ultra gates {len(ultra)} ({sum(1 for gate in ultra if gate.get('pass'))} pass) · windows {bool(windows and windows.get('pass'))} · logs {bool(logs and logs.get('pass'))}",
        "evidence " + args.out,
    ]
    with open(os.path.join(args.out, "rc-report.txt"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 0 if overall else 1


if __name__ == "__main__":
    raise SystemExit(main())
