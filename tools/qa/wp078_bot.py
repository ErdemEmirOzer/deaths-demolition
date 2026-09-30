#!/usr/bin/env python3
"""WP-078 · one guarded QaRunBot Player run with a bindable attempt record (S-10).

For the A2 gameplay runs, the A2 route runs (-ddQaBotRouteTour), the A3 AI-flag smokes, the A5 stability soaks and
the real-bot report runs. The run is wrapped in the C-00 §4.14 user-data guard (tools/userdata-guard.sh snapshot →
tools/player-run.sh → restore --scope player; verified=1 required, only Unity's window-state PlayerPrefs may change —
C-00 25.09) and sandboxed (-ddProfilePath, -ddTelemetryDir, QaRunBot's <out>/ui-data). It writes <out>/attempt.json in
the wp064_run shape (schema dd-wp064-attempt-v1: attemptId, kind, executionCommit, binarySha256, commandSha256, status,
exitCode, timedOut, missingArtifacts, artifactSha256) plus a `guard` receipt {snapshotExit, restoreExit, verified,
outOfScopeChanged, touched, prefsDiff}; every output file, route frames included, is hashed into artifactSha256 so
tools/qa/wp078_report.py can bind it. Route frames are never "required": a blocked point stays BLOCKED, not FAIL.

usage: wp078_bot.py --bin <build dir|.app|binary> --out <new dir> --commit <40-hex> --mode topdown|tp|fp
                    [--kind gameplay|route|stability|report|flag] [--seed 61] [--rounds 5] [--tier sinematik]
                    [--extra-arg=<flag>]... [--timeout 3600]
exit: 0 run complete + guard ok · 1 run not complete (see attempt.json) · 6 a game Player is up · 7 guard STOP (report to
      C-00) · 30 guard snapshot blocked · 64 usage
"""
import argparse
import json
import re
import subprocess
import sys
import time
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))
import wp064_run  # noqa: E402  (sha256_file, utc_now, write_json)
import wp078_perf  # noqa: E402  (player_up, guard, export_prefs, guard_verdict, resolve_binary)

KINDS = ("gameplay", "route", "stability", "report", "flag")
REQUIRED = ["command.json", "player.log", "exit.txt", "bot-summary.json", "qa-report.json", "events.txt"]


def build_command(binary, out, args):
    command = [str(binary), "-screen-fullscreen", "0",
               "-ddProfilePath", str(out / "profile-sandbox" / "prototype-profile.json"),
               "-ddQaBot", str(out), "-ddQaBotRounds", str(args.rounds), "-ddQaBotSeed", str(args.seed),
               "-ddQualityTier", args.tier, "-ddCameraMode", args.mode, "-ddTelemetryDir", str(out / "telemetry")]
    if args.kind == "stability":
        command.append("-ddQaBotStability")
    if args.kind == "route":
        command += ["-ddQaBotRouteTour", str(out / "frames")]
    command += args.extra_arg
    command += ["-logFile", str(out / "player.log")]
    return command


def hash_outputs(out):
    """Every file the run produced, relative to the attempt directory (the guard's own folder excluded)."""
    hashes = {}
    for path in sorted(out.rglob("*")):
        rel = path.relative_to(out).as_posix()
        if not path.is_file() or rel == "attempt.json" or rel.startswith("ug/") or rel.startswith("profile-sandbox/"):
            continue
        hashes[rel] = wp064_run.sha256_file(path)
    return hashes


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bin", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--mode", required=True, choices=("topdown", "tp", "fp"))
    parser.add_argument("--kind", default="gameplay", choices=KINDS)
    parser.add_argument("--seed", type=int, default=61)
    parser.add_argument("--rounds", type=int, default=5)
    parser.add_argument("--tier", default="sinematik", choices=("sinematik", "yuksek", "performans"))
    parser.add_argument("--extra-arg", action="append", default=[])
    parser.add_argument("--timeout", type=int, default=3600)
    parser.add_argument("--quiet", action="store_true", help="A5 / DEC-091 clean host: hold the player + Unity slots, wait for load1 < --load-max and "
                        "no foreign Unity, sample the host every 30 s (hostClean = every sample quiet)")
    parser.add_argument("--load-max", type=float, default=6.0)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.commit):
        parser.error("--commit must be a full lowercase 40-hex SHA")

    out = Path(args.out).resolve()
    if out.exists():
        print(f"refusing to overwrite immutable attempt directory: {out}", file=sys.stderr)
        return 64
    binary = wp078_perf.resolve_binary(args.bin)
    if not binary.is_file():
        print(f"Player binary missing: {binary}", file=sys.stderr)
        return 64
    if wp078_perf.player_up():
        print("a game Player is running — not touched, run refused", file=sys.stderr)
        return 6
    out.mkdir(parents=True)
    (out / "profile-sandbox").mkdir()

    command = build_command(binary, out, args)
    wp064_run.write_json(out / "command.json", command)
    attempt = {"schema": "dd-wp064-attempt-v1", "producer": "wp078_bot", "attemptId": str(uuid.uuid4()), "kind": args.kind,
               "mode": args.mode, "seed": args.seed, "rounds": args.rounds, "tier": args.tier, "buildRole": "dev",
               "executionCommit": args.commit, "binary": str(binary), "binarySha256": wp064_run.sha256_file(binary),
               "commandSha256": wp064_run.sha256_file(out / "command.json"), "runnerSha256": wp064_run.sha256_file(Path(__file__)),
               "startedUtc": wp064_run.utc_now(), "startedEpoch": time.time(), "status": "starting"}
    wp064_run.write_json(out / "attempt.json", attempt)

    notes = []
    quiet = wp078_perf.QuietWindow(notes.append, out / ".quiet-hold", args.load_max, 10_000.0, 0.0) if args.quiet else None
    try:
        return run_guarded(args, out, command, attempt, quiet, notes)
    finally:
        if quiet is not None:
            quiet.release()


def run_guarded(args, out, command, attempt, quiet, notes):
    if quiet is not None:
        quiet.acquire()
    ug = out / "ug"
    snap = wp078_perf.guard("snapshot", ug)
    if snap.returncode != 0:
        attempt.update({"status": "guard-blocked", "exitCode": snap.returncode, "completedUtc": wp064_run.utc_now(),
                        "guard": {"snapshotExit": snap.returncode}})
        wp064_run.write_json(out / "attempt.json", attempt)
        return snap.returncode

    timed_out, samples = False, []
    player = subprocess.Popen([str(REPO / "tools" / "player-run.sh")] + command)
    deadline = time.monotonic() + args.timeout
    while player.poll() is None:
        if time.monotonic() > deadline:
            timed_out = True
            player.terminate()
            try:
                player.wait(timeout=30)
            except subprocess.TimeoutExpired:
                player.kill()
            break
        if quiet is not None:
            clean, why = quiet.foreign_quiet()
            samples.append({"utc": wp064_run.utc_now(), "clean": clean, "host": why})
        time.sleep(30 if quiet is not None else 2)
    exit_code = player.wait()
    (out / "exit.txt").write_text(f"exit={exit_code}\n", encoding="utf-8")

    wp078_perf.export_prefs(ug / "prefs-after-run.plist")
    restore = wp078_perf.guard("restore", ug)
    (ug / "restore.txt").write_text(restore.stdout + restore.stderr, encoding="utf-8")
    ok, why = wp078_perf.guard_verdict(ug, restore.stdout, ug / "prefs-after-run.plist")
    fields = dict(re.findall(r"(\w+)=(\S+)", restore.stdout.strip().splitlines()[-1] if restore.stdout.strip() else ""))
    oos = ug / "out-of-scope-changed.txt"
    guard = {"snapshotExit": 0, "restoreExit": restore.returncode, "verified": fields.get("verified") == "1",
             "outOfScopeChanged": bool(oos.is_file() and oos.stat().st_size > 0), "touched": fields.get("touched"), "verdict": why}

    missing = [name for name in REQUIRED if not (out / name).is_file()]
    success = exit_code == 0 and not timed_out and not missing and restore.returncode == 0 and ok
    attempt.update({"status": "complete" if success else "failed", "completedUtc": wp064_run.utc_now(), "completedEpoch": time.time(),
                    "exitCode": exit_code, "timedOut": timed_out, "missingArtifacts": missing,
                    "hostClean": (bool(samples) and all(x["clean"] for x in samples)) if quiet is not None else None,
                    "hostSamples": samples, "quietNotes": notes,
                    "guard": guard, "artifactSha256": hash_outputs(out)})
    wp064_run.write_json(out / "attempt.json", attempt)
    print(f"{args.kind} {args.mode} exit={exit_code} timedOut={timed_out} missing={missing} guard={why} out={out}")
    if restore.returncode != 0 or not ok:
        return 7
    return 0 if success else 1


if __name__ == "__main__":
    raise SystemExit(main())
