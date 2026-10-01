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
exit: 0 run complete + guard ok · 1 run not complete (see attempt.json) · 6 a game Player is up · 7 guard STOP (snapshot error,
      restore error or verdict mismatch — report to C-00) · 30 / 75 snapshot waited (live DD Player / lock) · 75 also a failed
      clean-host preflight (Player not launched) · 65 NOT restored — a Player still up 60 s after the run
      (out/ug/userdata-NOT-RESTORED.txt), or such a marker unresolved next to <out> (nothing started) — STOP, C-00
      (ug/userdata-NOT-RESTORED.txt, STOP → C-00) · 64 usage, DD_GUARD_ROOT set or the canonical guard missing (G4 D4)
      · 130/143/129 INT/TERM/HUP (the restore ran first)
Guard (G4 D2/D4, C-00 01.10): the canonical driver guard (guard_receipt.GUARD); on an error or a signal the Player
(player-run.sh) gets TERM, ≤ 30 s, then KILL; the restore waits ≤ 60 s for no game Player to be left and runs with
INT/TERM/HUP deferred; the signal is raised after the attempt record is written.
"""
import argparse
import signal
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
import wp078_perf  # noqa: E402  (player_up, resolve_binary, PLAYER_EXE)
import guard_receipt  # noqa: E402  (C-00 30.09 M-17: the one guard receipt module)

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
    parser.add_argument("--quiet", action="store_true", help="A5 / DEC-091 clean host (C-00 30.09 M-16): wp064_run-style preflight (12 clean 5 s "
                        "samples: load1 < --load-max, no foreign Unity/Blender/Player) and a host sample every 30 s during the run "
                        "(hostClean = every sample clean); only the player slot is held (by the guard), no Unity slots")
    parser.add_argument("--load-max", type=float, default=6.0)
    args = parser.parse_args()
    # C-00 30.09 M-14 / G4 D2: TERM and HUP become SystemExit(128+n), so the restore half runs
    for signum in (signal.SIGTERM, signal.SIGHUP):
        if signal.getsignal(signum) in (signal.SIG_DFL, None):   # review SHOULD-4: nohup / `&` keep their SIG_IGN
            signal.signal(signum, lambda n, _frame: sys.exit(128 + n))
    refusal = guard_receipt.canon_refusal()
    if refusal:
        print(f"guard refused (G4 D4): {refusal} — nothing started", file=sys.stderr)
        return 64
    # G4 D2 review MUST-1: an unresolved NOT-RESTORED marker next to this run stops it — the next snapshot would take the
    # unrestored data as its baseline (wp078_accept checks the whole evidence tree before every unit)
    parent = Path(args.out).resolve().parent
    open_markers = guard_receipt.unresolved(parent.glob("*/ug/" + guard_receipt.MARKER))   # the sibling runs' guard dirs
    if open_markers:
        print(f"unrestored guard window(s), resolve with C-00 first — nothing started: {open_markers}", file=sys.stderr)
        return guard_receipt.NOT_RESTORED
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

    preflight = {"requested": bool(args.quiet)}
    if args.quiet:
        clean, preflight = clean_host_preflight(args.load_max)
        attempt["preflight"] = preflight
        if not clean:
            attempt.update({"status": "preflight-failed", "exitCode": 75, "completedUtc": wp064_run.utc_now()})
            wp064_run.write_json(out / "attempt.json", attempt)
            print(f"clean-host preflight failed; Player not launched: {out}")
            return 75
    return run_guarded(args, out, command, attempt)


def host_sample(load_max, own_player=False):
    """(clean, detail): no foreign Unity / Blender / other game Player (our own Player excluded while it runs); load1 below
    load_max only before the run (during the run the load is ours)."""
    comm = subprocess.run(["ps", "-Ao", "comm"], capture_output=True, text=True).stdout.splitlines()
    unity = [c for c in comm if c.strip().endswith("/MacOS/Unity") or c.strip().endswith("/MacOS/Blender")]
    players = sum(1 for c in comm if c.strip().endswith(wp078_perf.PLAYER_EXE))
    load = float(subprocess.run(["sysctl", "-n", "vm.loadavg"], capture_output=True, text=True).stdout.split()[1])
    foreign_players = players - (1 if own_player else 0)
    clean = not unity and foreign_players <= 0 and (own_player or load < load_max)
    return clean, {"utc": wp064_run.utc_now(), "load1": load, "foreignUnityBlender": len(unity), "foreignPlayers": max(0, foreign_players)}


def clean_host_preflight(load_max, samples=12, interval=5.0, attempts=30, wait=60.0):
    """C-00 30.09 M-16: DEC-091 clean host the wp064_run way — 12 consecutive clean 5 s samples (load1 < load_max, no
    foreign Unity / Blender / Player), up to `attempts` windows; no Unity slots are held during the soak."""
    windows = []
    for attempt in range(1, attempts + 1):
        run = []
        for _ in range(samples):
            clean, detail = host_sample(load_max)
            run.append(detail)
            if not clean:
                break
            time.sleep(interval)
        ok = len(run) == samples
        windows.append({"window": attempt, "pass": ok, "samples": run})
        if ok:
            return True, {"pass": True, "loadMax": load_max, "samplesRequired": samples, "windows": windows[-3:]}
        time.sleep(wait)
    return False, {"pass": False, "loadMax": load_max, "samplesRequired": samples, "windowsUsed": attempts, "windows": windows[-3:]}


def launch(command):
    """The Player through the single player slot (a seam for the guard tests)."""
    return subprocess.Popen([str(REPO / "tools" / "player-run.sh")] + command)


def run_guarded(args, out, command, attempt):
    ug = out / "ug"
    snap = guard_receipt.run_guard("snapshot", ug, "player")
    code = guard_receipt.snapshot_code(snap.returncode)
    if code:
        attempt.update({"status": "guard-blocked", "exitCode": snap.returncode, "completedUtc": wp064_run.utc_now(),
                        "guard": {"snapshotExit": snap.returncode}})
        wp064_run.write_json(out / "attempt.json", attempt)
        return code

    timed_out, samples, exit_code, player, inflight = False, [], None, None, None
    try:
        player = launch(command)
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
            if args.quiet:
                clean, detail = host_sample(args.load_max, own_player=True)
                samples.append(dict(detail, clean=clean))
            time.sleep(30 if args.quiet else 2)
        exit_code = player.wait()
        (out / "exit.txt").write_text(f"exit={exit_code}\n", encoding="utf-8")
    except BaseException as error:   # error, Ctrl-C, SIGTERM/SIGHUP: stop the Player (TERM → 30 s → KILL), restore, go on
        inflight = error
        if player is not None:
            guard_receipt.stop_child(player, 30.0)
    # C-00 30.09 M-14 / G4 D2: the restore half runs on every path, signals deferred; a Player still up → not restored, 65
    receipt, signals = guard_receipt.finish(ug, "player", wp078_perf.players)
    guard_code, why = receipt["code"], receipt["why"]
    attempt["guard"] = dict(receipt, snapshotExit=0, verdict=why)
    wp064_run.write_json(out / "attempt.json", attempt)
    if guard_code == guard_receipt.NOT_RESTORED:
        print(f"STOP: {why} — report to C-00", file=sys.stderr)
        return guard_code
    signals.reraise()
    if inflight is not None:
        raise inflight

    missing = [name for name in REQUIRED if not (out / name).is_file()]
    if exit_code in (30, 75) and not timed_out and not (out / "player.log").is_file() and guard_code == 0:
        # prova 01.10: tools/player-run.sh refused before the Player started (disk gate / slot / live Player → 75 or 30);
        # that is a wait, not a failed run — the caller retries (it was reported as exit 1 and never retried)
        attempt.update({"status": "not-started", "completedUtc": wp064_run.utc_now(), "exitCode": exit_code, "timedOut": False,
                        "missingArtifacts": missing})
        wp064_run.write_json(out / "attempt.json", attempt)
        print(f"{args.kind} {args.mode} not started: player-run exit {exit_code} (wait) guard={why} out={out}")
        return exit_code
    try:
        summary = json.loads((out / "bot-summary.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        summary = None
    if isinstance(summary, dict) and summary.get("inputMeasurable") is False and not timed_out and guard_code == 0:
        # S-02 01.10 input-loss contract: the bot's input never reached the game (pad disabled / pointer held the scheme);
        # the run is UNMEASURED — a wait (75) that the chain and wp078_accept retry, never a finished bad run
        attempt.update({"status": "unmeasurable", "completedUtc": wp064_run.utc_now(), "exitCode": exit_code, "timedOut": False,
                        "missingArtifacts": missing, "artifactSha256": hash_outputs(out),
                        "inputLoss": {k: summary.get(k) for k in ("padDisabledFrames", "pointerAfterPadFrames", "unfocusedFrames")}})
        wp064_run.write_json(out / "attempt.json", attempt)
        print(f"{args.kind} {args.mode} UNMEASURED (input lost) exit={exit_code} guard={why} out={out}")
        return 75
    success = exit_code == 0 and not timed_out and not missing and guard_code == 0
    attempt.update({"status": "complete" if success else "failed", "completedUtc": wp064_run.utc_now(), "completedEpoch": time.time(),
                    "exitCode": exit_code, "timedOut": timed_out, "missingArtifacts": missing,
                    "hostClean": (bool(samples) and all(x["clean"] for x in samples)) if args.quiet else None,
                    "hostSamples": samples, "artifactSha256": hash_outputs(out)})
    wp064_run.write_json(out / "attempt.json", attempt)
    print(f"{args.kind} {args.mode} exit={exit_code} timedOut={timed_out} missing={missing} guard={why} out={out}")
    if guard_code:
        return guard_code
    return 0 if success else 1


if __name__ == "__main__":
    raise SystemExit(main())
