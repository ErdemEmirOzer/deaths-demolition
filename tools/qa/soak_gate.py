#!/usr/bin/env python3
"""Soak gates (WP-061 exact N/N; WP-064 / DEC-091 split, user decision 2026-09-24).

  soak_gate.py <bot-summary.json> --rounds N [--seed S]
      exact N/N real-bot run (legacy WP-061 contract, still usable as a check)
  soak_gate.py <bot-summary.json> --rounds N --seed S --stability --player-log <player.log>
      STABILITY GATE (MUST): an invulnerable-by-design bot (runMode=stability) completes all N rounds in order,
      no timeout, stabilityPass, 0 error lines in the Player log, bounded memory growth (thresholds below)
  soak_gate.py --report <bot-summary.json> [<bot-summary.json> ...]
      REAL-BOT REPORT (not a gate): reached-round distribution (median/min/max) with death round and cause

Memory thresholds (S-10 proposal, documented in planning/04 §WP-064B): growth from the end of round 2 (pools, shaders
and the world are warm by then) to the end of the last round must stay within
  Mono used heap  <= 48 MB and slope over rounds 3..N <= 4 MB/round   (managed leak guard)
  total allocated <= 256 MB                                           (native + managed, Unity profiler)
  RSS             <= 512 MB (reported only when the platform exposes it; -1 = unavailable)
"""
import argparse
import json
import re
import statistics
import sys

MONO_GROWTH_MAX_MB = 48.0
MONO_SLOPE_MAX_MB_PER_ROUND = 4.0
TOTAL_ALLOC_GROWTH_MAX_MB = 256.0
RSS_GROWTH_MAX_MB = 512.0
LOG_FAILURES = tuple(re.compile(p) for p in (
    r"Exception\b", r"Debug:LogError", r"Debug:LogException", r"Assertion failed", r"Crash!!!",
    r"Native Crash Reporting", r"Segmentation fault", r"\[QaRunBot\] (write failed|artifact directory unavailable)",
    r"Shader error in", r"Metal: Error",
))


def load(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def exact_rounds(summary, rounds):
    records = summary.get("rounds") or []
    return (
        summary.get("roundsRequested") == rounds
        and summary.get("roundsCompleted") == rounds
        and len(records) == rounds
        and [r.get("round") for r in records] == list(range(1, rounds + 1))
        and all(r.get("completed") is True and str(r.get("outcome", "")).lower() == "completed" for r in records)
    ), records


def slope(values):
    n = len(values)
    if n < 2:
        return 0.0
    xs = range(n)
    mx, my = (n - 1) / 2.0, sum(values) / n
    den = sum((x - mx) ** 2 for x in xs)
    return sum((x - mx) * (y - my) for x, y in zip(xs, values)) / den if den else 0.0


def memory_checks(records):
    lines, ok = [], True
    if len(records) < 4:
        return False, ["FAIL memory: fewer than 4 rounds"]
    base, last = records[1], records[-1]
    def field(r, k):
        v = r.get(k)
        return v if isinstance(v, (int, float)) else -1.0
    mono = [field(r, "memMonoUsedMB") for r in records[2:]]
    if min(mono + [field(base, "memMonoUsedMB")]) < 0:
        return False, ["FAIL memory: Mono used heap not sampled"]
    mono_growth = field(last, "memMonoUsedMB") - field(base, "memMonoUsedMB")
    mono_slope = slope(mono)
    alloc_growth = field(last, "memTotalAllocatedMB") - field(base, "memTotalAllocatedMB")
    rss0, rss1 = field(base, "memRssMB"), field(last, "memRssMB")
    checks = [
        ("mono.growth", mono_growth <= MONO_GROWTH_MAX_MB, f"{mono_growth:.1f} MB <= {MONO_GROWTH_MAX_MB}"),
        ("mono.slope", mono_slope <= MONO_SLOPE_MAX_MB_PER_ROUND, f"{mono_slope:.2f} MB/round <= {MONO_SLOPE_MAX_MB_PER_ROUND}"),
        ("alloc.growth", alloc_growth <= TOTAL_ALLOC_GROWTH_MAX_MB, f"{alloc_growth:.1f} MB <= {TOTAL_ALLOC_GROWTH_MAX_MB}"),
    ]
    if rss0 > 0 and rss1 > 0:
        checks.append(("rss.growth", rss1 - rss0 <= RSS_GROWTH_MAX_MB, f"{rss1 - rss0:.1f} MB <= {RSS_GROWTH_MAX_MB}"))
    else:
        lines.append("INFO rss unavailable on this platform (WorkingSet64 = 0) — Mono/total-allocated carry the gate")
    for name, good, text in checks:
        ok &= good
        lines.append(f"{'PASS' if good else 'FAIL'} memory.{name} = {text}")
    return ok, lines


def log_failures(path):
    hits = []
    try:
        with open(path, encoding="utf-8", errors="replace") as handle:
            for number, line in enumerate(handle, 1):
                if any(p.search(line) for p in LOG_FAILURES):
                    hits.append(f"{number}: {line.strip()[:200]}")
    except OSError as error:
        hits.append(f"unreadable: {error}")
    return hits


def report(paths):
    rows = []
    for path in paths:
        try:
            s = load(path)
        except Exception as error:
            print(f"FAIL unreadable {path}: {error}")
            return 1
        rows.append((s.get("roundsCompleted", 0), s.get("deathRound", 0), s.get("deathKilledBy") or s.get("note", ""), s.get("seed"), path))
    reached = [r[0] for r in rows]
    print(f"REPORT real-bot soak (not a gate, DEC-091): runs={len(rows)} rounds completed median={statistics.median(reached)} "
          f"min={min(reached)} max={max(reached)} distribution={sorted(reached)}")
    for completed, death_round, cause, seed, path in rows:
        print(f"  seed={seed} completed={completed} deathRound={death_round or '-'} cause={cause} ({path})")
    return 0


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--report":
        return report(sys.argv[2:])
    parser = argparse.ArgumentParser()
    parser.add_argument("summary")
    parser.add_argument("--rounds", type=int, required=True)
    parser.add_argument("--seed", type=int)
    parser.add_argument("--stability", action="store_true")
    parser.add_argument("--player-log")
    args = parser.parse_args()
    try:
        summary = load(args.summary)
    except Exception as error:
        print(f"FAIL soak summary unreadable: {error}")
        return 1
    ok_rounds, records = exact_rounds(summary, args.rounds)
    seed_ok = args.seed is None or summary.get("seed") == args.seed
    if not args.stability:
        ok = ok_rounds and seed_ok and summary.get("deaths") == 0 and summary.get("pass") is True
        print(f"{'PASS' if ok else 'FAIL'} exact soak seed={summary.get('seed')} requested={summary.get('roundsRequested')} "
              f"completed={summary.get('roundsCompleted')} deaths={summary.get('deaths')} records={len(records)} summaryPass={summary.get('pass')}")
        return 0 if ok else 1

    lines = []
    mode_ok = summary.get("runMode") == "stability" and summary.get("stabilityPass") is True and summary.get("artifactWriteFailed") is not True
    lines.append(f"{'PASS' if mode_ok else 'FAIL'} stability.mode runMode={summary.get('runMode')} stabilityPass={summary.get('stabilityPass')}")
    lines.append(f"{'PASS' if ok_rounds and seed_ok else 'FAIL'} stability.rounds seed={summary.get('seed')} {summary.get('roundsCompleted')}/{args.rounds} ordered, no timeout")
    mem_ok, mem_lines = memory_checks(records)
    lines += mem_lines
    if not args.player_log:
        log_ok, hits = False, ["--player-log is required with --stability"]
    else:
        hits = log_failures(args.player_log)
        log_ok = not hits
    lines.append(f"{'PASS' if log_ok else 'FAIL'} stability.log errors={len(hits)}" + ("" if log_ok else " first: " + hits[0]))
    # S-02 01.10 input-loss contract: a soak whose bot input never reached the game proves nothing (an invulnerable bot
    # standing still completes rounds); absent field = older summary, measured as before
    input_ok = summary.get("inputMeasurable") is not False
    lines.append(f"{'PASS' if input_ok else 'FAIL'} stability.input measurable={summary.get('inputMeasurable')} padDisabled={summary.get('padDisabledFrames')} pointerAfterPad={summary.get('pointerAfterPadFrames')}")
    ok = mode_ok and ok_rounds and seed_ok and mem_ok and log_ok and input_ok
    print(f"{'PASS' if ok else 'FAIL'} soak STABILITY gate (DEC-091)")
    print("\n".join(lines))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
