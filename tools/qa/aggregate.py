#!/usr/bin/env python3
"""WP-026 · M3 closure aggregate: merges the per-step artefacts of tools/qa/regression.sh into one
qa-report.json (schema dd-qa-report-v1, same shape the Unity drivers write) and a <= 20-line qa-report.txt.

Gate values are read, never invented here: perf thresholds are ADR-011-3 v3 / DEC-088 (mean <= 8.0 ms, clean p99 <= 10 ms),
readability is DEC-077 (drivers already applied ReadabilityThresholds), bot = N rounds completed, spawn mix = EditMode
QA tests green, armour form test = DD.Art ArmorTests.Okunabilirlik green.
"""
import glob
import json
import math
import os
import re
import sys
import time
import xml.etree.ElementTree as ET

PERF_MEAN_MAX_MS = 8.0      # ADR-011-3 v3 (DEC-088, kullanıcı 2026-09-17): look pass sonrası ortalama ≤ 8,0; p99 10 aynı
PERF_P99_MAX_MS = 10.0      # clean (stall-filtered) p99
HUD_COST_MAX_MS = 0.35      # WP-068 (C-00 24.09): run HUD CPU + GPU per frame, 1080p, 40 bodies (PilotPerfProbe 04h-hud)


def gate(gid, ok, value, threshold="", note="", source=""):
    return {"id": gid, "pass": bool(ok), "value": str(value), "threshold": threshold, "note": note, "source": source}


def read_json(path):
    try:
        with open(path) as f:
            return json.load(f)
    except Exception:
        return None


def finite_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def positive_metric(value):
    return finite_number(value) and value > 0


def xml_counts(path):
    try:
        root = ET.parse(path).getroot()
        return int(root.get("total", 0)), int(root.get("passed", 0)), int(root.get("failed", 0)), int(root.get("skipped", 0)), int(root.get("inconclusive", 0))
    except Exception:
        return None


def testcase_passed(path, name_suffix):
    try:
        root = ET.parse(path).getroot()
        matches = [case for case in root.iter("test-case") if (case.get("fullname") or case.get("name") or "").endswith(name_suffix)]
        return bool(matches) and all((case.get("result") or "").lower() == "passed" for case in matches), len(matches)
    except Exception:
        return False, 0


def load1(path):
    try:
        with open(path) as f:
            m = re.search(r"load averages?: ([0-9.]+)", f.read())
            return m.group(1) if m else ""
    except Exception:
        return ""


def main():
    if len(sys.argv) < 2:
        print("usage: aggregate.py <out-dir> [--window 60] [--perf-runs 3]")
        return 2
    out = sys.argv[1]
    window = 60
    perf_runs = 3
    args = sys.argv[2:]
    for i, a in enumerate(args):
        if a == "--window":
            window = int(args[i + 1])
        if a == "--perf-runs":
            perf_runs = int(args[i + 1])

    gates = []
    notes = []

    # ---- build stamp (WP-025 build-info.json when present)
    build_info = "n/a"
    bi = read_json(os.path.join(out, "build-info.json")) or read_json(os.path.join(out, "build-dev", "build-report.json"))
    if bi:
        keep = {k: bi[k] for k in ("mode", "commit", "describe", "branch", "utc", "unity", "version", "gitDescribe", "gitCommit") if k in bi} or bi
        build_info = json.dumps(keep, separators=(",", ":"))[:240]

    # ---- EditMode QA (spawn-mix simulator, schema, bot mapping)
    em_path = os.environ.get("DD_EXTERNAL_EDITMODE_XML") or os.path.join(out, "editmode-qa.xml")
    em = xml_counts(em_path)
    if em:
        total, passed, failed, skipped, inconclusive = em
        gates.append(gate("editmode.qa", failed == 0 and total > 0 and skipped == 0 and inconclusive == 0,
                          f"{passed}/{total}", "all green, skipped 0", "QA tests or supplied full EditMode suite", em_path))
    else:
        gates.append(gate("editmode.qa", False, "missing", "editmode-qa.xml", "step skipped or crashed", "editmode-qa.xml"))

    # ---- spawn mix (WP-027 SpawnMixSimulator via QaSpawnMixCli)
    sm = read_json(os.path.join(out, "spawn-mix", "qa-report.json"))
    if sm and sm.get("gates"):
        for g in sm["gates"]:
            g = dict(g)
            g["source"] = "spawn-mix/" + (g.get("source") or "qa-report.json")
            gates.append(g)
        if sm.get("summary"):
            notes.append("spawnmix: " + sm["summary"][:160])
    else:
        gates.append(gate("spawnmix.violations", False, "missing", "== 0", "spawn-mix/qa-report.json not written", "spawn-mix/"))

    # ---- bot
    bot = read_json(os.path.join(out, "bot", "qa-report.json"))
    bot_summary = read_json(os.path.join(out, "bot", "bot-summary.json"))
    if bot and bot.get("gates"):
        for g in bot["gates"]:
            g = dict(g)
            g["source"] = "bot/" + (g.get("source") or "qa-report.json")
            gates.append(g)
    else:
        gates.append(gate("bot.rounds", False, "missing", "", "bot/qa-report.json not written", "bot/"))
    if bot_summary:
        notes.append(f"bot: rounds {bot_summary.get('roundsCompleted')}/{bot_summary.get('roundsRequested')} kills {bot_summary.get('totalKills')} deaths {bot_summary.get('deaths')} "
                     f"spawnHash {bot_summary.get('spawnSequenceHash')} outcomeHash {bot_summary.get('outcomeHash')} meanFrame {bot_summary.get('meanFrameMs', 0):.2f} ms")

    # ---- perf (PilotPerfProbe Run window, N runs)
    means, p99s, p99f, stalls, maxes, frames = [], [], [], [], [], []
    draws, tris, texmb, meshmb, rtmb, gcalloc, gen0 = [], [], [], [], [], [], []
    loads = []
    for i in range(1, perf_runs + 1):
        d = os.path.join(out, f"perf-{i}")
        w = read_json(os.path.join(d, "04-window.json"))
        if not w:
            gates.append(gate(f"perf.run{i}", False, "missing", "", "04-window.json absent", f"perf-{i}/"))
            continue
        mean = w.get("frameMeanMs")
        raw_p99 = w.get("frameP99Ms")
        clean_p99 = w.get("frameP99FilteredMs")
        metrics_valid = positive_metric(mean) and positive_metric(raw_p99) and positive_metric(clean_p99)
        means.append(mean if positive_metric(mean) else 1e9)
        p99s.append(raw_p99 if positive_metric(raw_p99) else 1e9)
        p99f.append(clean_p99 if positive_metric(clean_p99) else 1e9)
        stalls.append(w.get("framesExcludedAsStalls", 0)); maxes.append(w.get("frameMaxMs", 0.0)); frames.append(w.get("frameCount", w.get("warmupFrames", 0)))
        draws.append(w.get("drawCallsAvg", 0.0)); tris.append(w.get("trianglesAvg", 0.0)); texmb.append(w.get("textureMemoryMax", 0) / 1048576.0)
        meshmb.append(w.get("meshMemoryMax", 0) / 1048576.0); rtmb.append(w.get("renderTextureMemoryMax", 0) / 1048576.0)
        gcalloc.append(w.get("gcAllocatedInFrameAvg", 0.0)); gen0.append(w.get("gen0CollectionsInWindow", 0))
        loads.append((load1(os.path.join(d, "load-before.txt")), load1(os.path.join(d, "load-after.txt"))))
        gates.append(gate(f"perf.run{i}.integrity", bool(w.get("pass")), w.get("note", "")[:60], "probe stage 04 pass", "", f"perf-{i}/04-window.json"))
        gates.append(gate(f"perf.run{i}.metrics", metrics_valid, "present" if metrics_valid else "missing/non-finite",
                          "frameMeanMs, frameP99Ms, frameP99FilteredMs finite", "", f"perf-{i}/04-window.json"))
        gates.append(gate(f"perf.run{i}.meanMs", metrics_valid and mean <= PERF_MEAN_MAX_MS,
                          f"{mean:.2f}" if positive_metric(mean) else "missing/non-positive", f"<= {PERF_MEAN_MAX_MS:.1f} ms", "independent DEC-088 window", f"perf-{i}/04-window.json"))
        gates.append(gate(f"perf.run{i}.cleanP99Ms", metrics_valid and clean_p99 <= PERF_P99_MAX_MS,
                          f"{clean_p99:.2f}" if positive_metric(clean_p99) else "missing/non-positive", f"<= {PERF_P99_MAX_MS:.1f} ms", "independent DEC-088 window", f"perf-{i}/04-window.json"))
    if means:
        mean_avg = sum(means) / len(means)
        p99f_avg = sum(p99f) / len(p99f)
        # WP-064: every counted window passes independently. Averages remain
        # descriptive and may never hide an over-budget run.
        gates.append(gate("perf.meanMs", len(means) == perf_runs and max(means) <= PERF_MEAN_MAX_MS,
                          f"avg {mean_avg:.2f}; runs {', '.join(f'{m:.2f}' for m in means)}",
                          f"each of {perf_runs} runs <= {PERF_MEAN_MAX_MS:.1f} ms (DEC-088)", "", "perf-*/04-window.json"))
        gates.append(gate("perf.cleanP99Ms", len(p99f) == perf_runs and max(p99f) <= PERF_P99_MAX_MS,
                          f"avg {p99f_avg:.2f}; runs {', '.join(f'{m:.2f}' for m in p99f)}; raw p99 {', '.join(f'{m:.2f}' for m in p99s)}",
                          f"each of {perf_runs} runs <= {PERF_P99_MAX_MS:.1f} ms stall-filtered (DEC-088)", "", "perf-*/04-window.json"))
        gates.append(gate("perf.externalPresentContention", True, f"stalls>33.3ms {sum(stalls)} / max {max(maxes):.1f} ms",
                          "report only", "host presentation stalls (WindowServer/other windows); Gfx.WaitForPresent counter not in PilotPerfProbe — proxy = frames > 33.3 ms", "perf-*/04-window.json"))
        huds = [read_json(os.path.join(out, f"perf-{i}", "04h-hud.json")) for i in range(1, perf_runs + 1)]
        huds = [h for h in huds if h and h.get("pass")]
        if huds:
            costs = [h.get("hudCostMs", 0.0) for h in huds]
            deltas = [h.get("hudFrameDeltaMs", 0.0) for h in huds]
            cost = sum(costs) / len(costs)
            gates.append(gate("perf.hudCostMs", cost <= HUD_COST_MAX_MS,
                              f"{cost:.3f} (runs {', '.join(f'{c:.3f}' for c in costs)}; frame delta {', '.join(f'{d:+.3f}' for d in deltas)})",
                              f"<= {HUD_COST_MAX_MS:.2f} ms UI CPU + GPU (WP-068 HUD budget)", "HUD on/off A/B, -ddPilotHudAB", "perf-*/04h-hud.json"))
        else:
            gates.append(gate("perf.hudCostMs", False, "missing", f"<= {HUD_COST_MAX_MS:.2f} ms", "04h-hud.json absent or its measurement failed", "perf-*/"))
        notes.append(f"perf: draw {sum(draws)/len(draws):.0f} tris {sum(tris)/len(tris)/1e6:.2f} M tex {max(texmb):.0f} MB mesh {max(meshmb):.0f} MB rt {max(rtmb):.0f} MB gc/frame {sum(gcalloc)/len(gcalloc):.0f} B gen0 {sum(gen0)} load1 {loads}")

    # ---- readability (DEC-077, real spawner mix)
    rd = read_json(os.path.join(out, "readability", "qa-report.json"))
    if rd and rd.get("gates"):
        for g in rd["gates"]:
            g = dict(g)
            g["source"] = "readability/" + (g.get("source") or "qa-report.json")
            gates.append(g)
    else:
        gates.append(gate("readability.driver", False, "missing", "", "readability/qa-report.json not written", "readability/"))

    # ---- DEC-077 armour form test (DD.Art PlayMode)
    pm_path = os.environ.get("DD_EXTERNAL_PLAYMODE_XML") or os.path.join(out, "playmode-armor.xml")
    pm = xml_counts(pm_path)
    if pm:
        ok, matches = testcase_passed(pm_path, "ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet")
        gates.append(gate("readability.armorForm.dec077", ok, f"{matches} matching test(s)", ">= 30 % + band min 0.08 / mean 0.12 (ArmorTests.Okunabilirlik)", "", pm_path))
    else:
        gates.append(gate("readability.armorForm.dec077", False, "not run", "ArmorTests.Okunabilirlik", "quick mode skips it; full closure runs it", "playmode-armor.xml"))

    if os.environ.get("DD_REQUIRE_ULTRA_EVIDENCE") == "1":
        ultra = [g for g in gates if str(g.get("id", "")).endswith("tierEnvContrast.Ultra")]
        gates.append(gate("readability.ultraEvidence", bool(ultra) and all(g.get("pass") for g in ultra),
                          f"{len(ultra)} measured gate(s)", ">= 1 passing Ultra environment-contrast measurement",
                          "existing QaReadabilityGate observation only; no gameplay/visual mutation", "readability/qa-report.json"))

    report = {
        "schema": "dd-qa-report-v1",
        "package": os.environ.get("DD_QA_PACKAGE", "WP-026"),
        "driver": "aggregate",
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "buildInfo": build_info,
        "machine": os.uname().machine + " " + os.uname().sysname,
        "window": f"{window} s x {perf_runs} runs, perf-clean rule (Blender/other Unity < 50 %)",
        "load1Before": loads[0][0] if loads else "",
        "load1After": loads[-1][1] if loads else "",
        "seed": (bot_summary or {}).get("seed", 0),
        "pass": all(g["pass"] for g in gates) and len(gates) > 0,
        "summary": " | ".join(notes),
        "gates": gates,
    }
    with open(os.path.join(out, "qa-report.json"), "w") as f:
        json.dump(report, f, indent=2)

    lines = [f"M3 closure {'PASS' if report['pass'] else 'FAIL'} · {report['utc']} · build {build_info[:80]}",
             f"window: {report['window']} · load1 {report['load1Before']} → {report['load1After']}"]
    shown = 0
    for g in gates:
        if shown >= 16:
            lines.append(f"… +{len(gates) - shown} gates in qa-report.json")
            break
        # readability per-skin lines are folded: show only failing ones beyond the first two
        if g["id"].startswith("readability.r") and (".playerEnvDeltaV" in g["id"] or ".playerZombieDeltaV" in g["id"]) and g["pass"]:
            continue
        thr = f" ({g['threshold']})" if g.get("threshold") else ""
        note = f" — {g['note']}" if g.get("note") else ""
        lines.append(f"{'PASS' if g['pass'] else 'FAIL'} {g['id']} = {g['value']}{thr}{note}"[:220])
        shown += 1
    for n in notes[:2]:
        lines.append(n[:220])
    text = "\n".join(lines[:20]) + "\n"
    with open(os.path.join(out, "qa-report.txt"), "w") as f:
        f.write(text)
    print(text, end="")
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
