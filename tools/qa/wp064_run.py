#!/usr/bin/env python3
"""Fail-closed WP-064 Player launcher with clean-host evidence.

This runner never changes the measured product. It launches one already-built
development Player, records a 60 s clean preflight and samples competing
processes/load once per second while the Player is alive.

Y2 (C-00 30.09): the Player starts through tools/player-run.sh (the single 'player' slot; a TERM to the wrapper reaches
the Player). command.json stays the Player's own argv (what the closure gate reads); attempt.json names the launcher.
Perf mode runs with -ddUiDataRoot <out>/ui-data and -ddTelemetryDir <out>/telemetry unless the caller passed them
(wp078_perf.py does). User data stays the caller's job: tools/userdata-guard.sh snapshot/restore --scope player around it.
"""
import argparse
import atexit
import hashlib
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent))
import guard_receipt  # noqa: E402  (G4 D2: DeferredSignals for stop_player)

PLAYER_RUN = Path(__file__).resolve().parents[1] / "player-run.sh"


def process_rows():
    output = subprocess.check_output(
        ["ps", "-axo", "pid=,ppid=,pcpu=,pmem=,etime=,comm=,args="],
        text=True,
        errors="replace",
    )
    rows = []
    for line in output.splitlines():
        parts = line.strip().split(None, 6)
        if len(parts) != 7:
            continue
        try:
            rows.append({
                "pid": int(parts[0]), "ppid": int(parts[1]),
                "cpu": float(parts[2]), "mem": float(parts[3]),
                "etime": parts[4], "comm": parts[5], "args": parts[6],
            })
        except ValueError:
            continue
    # C-00 12.10: each process's executable (argv[0]) on its own line end — the comm column above splits at a space
    exes = {}
    for line in subprocess.run(["ps", "-axo", "pid=,comm="], capture_output=True, text=True, errors="replace").stdout.splitlines():
        p, _, c = line.strip().partition(" ")
        if p.isdigit():
            exes[int(p)] = c.strip()
    for row in rows:
        row["exe"] = exes.get(row["pid"], "")
    return rows


def forbidden_target(row):
    """C-00 12.10: what decides "is this a Unity / Blender / Player": the executable (argv[0]) when it is an absolute path —
    a `shasum … ./x/DeathsDemolition.app/Contents/MacOS/…` or a grep naming the path is not one — else the whole command
    line (a relative or unknown argv[0]: the safe side)."""
    exe = row.get("exe") or ""
    return exe if exe.startswith("/") else row["args"]


def forbidden(row, allowed_pid=None, rows_by_pid=None):
    # the launched Player runs under player-run.sh (allowed_pid): it and its descendants are this run, not a competitor
    if row["pid"] in {os.getpid(), allowed_pid} or process_is_descendant(row, rows_by_pid or {}, allowed_pid):
        return False
    command = forbidden_target(row)
    return (
        ("/Unity.app/Contents/MacOS/Unity" in command and "Licensing" not in command)
        or "/Blender.app/Contents/MacOS/Blender" in command
        or ("/DeathsDemolition.app/Contents/MacOS/" in command)
    )


def controller_or_presentation(row):
    """The compositor presents the measured Player's own frames, so its spikes are part of the measurement.

    v2 (S-10 finding 3): no controller/agent exemption any more — a Codex/ChatGPT/Claude process (or anything
    else outside this run's own PID tree) at >= 50 % CPU is a competing workload like any other. The runner and
    the Player's descendants stay exempt through `allowed_pid` / `process_is_descendant`.
    """
    return "SkyLight.framework/Resources/WindowServer" in row["args"]


def process_is_descendant(row, rows_by_pid, allowed_pid):
    if allowed_pid is None:
        return False
    seen = set()
    pid = row["pid"]
    while pid > 1 and pid not in seen:
        if pid == allowed_pid:
            return True
        seen.add(pid)
        parent = rows_by_pid.get(pid)
        if parent is None:
            return False
        pid = parent["ppid"]
    return False


def heavy(row, allowed_pid=None, rows_by_pid=None):
    return (
        row["pid"] not in {os.getpid(), allowed_pid}
        and not process_is_descendant(row, rows_by_pid or {}, allowed_pid)
        and not controller_or_presentation(row)
        and row["cpu"] >= 50.0
    )


def snapshot(allowed_pid=None, own_tree=False):
    """own_tree: allowed_pid is the launched player-run.sh — its descendants (the Player) are not forbidden."""
    rows = process_rows()
    rows_by_pid = {row["pid"]: row for row in rows}
    return {
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "load1": os.getloadavg()[0],
        "forbidden": [r for r in rows if forbidden(r, allowed_pid, rows_by_pid if own_tree else None)],
        "heavy": [r for r in rows if heavy(r, allowed_pid, rows_by_pid)],
        "activeOver5Pct": [r for r in rows if r["cpu"] >= 5.0],
    }


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def utc_now():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def ledger_append(out, row):
    """v2 (finding 6): one JSON line per attempt event in <parent>/attempts-ledger.jsonl (append-only)."""
    ledger = out.parent / "attempts-ledger.jsonl"
    row = dict(row, dir=out.name, utc=utc_now())
    with ledger.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(row, sort_keys=True) + "\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("kind", choices=("perf", "soak"))
    parser.add_argument("--bin", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--window", type=int, default=60)
    parser.add_argument("--rounds", type=int, default=12)
    parser.add_argument("--seed", type=int, default=61)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--extra-arg", action="append", default=[])
    parser.add_argument("--stability", action="store_true", help="DEC-091: invulnerable-by-design stability soak (-ddQaBotStability)")
    parser.add_argument("--preflight-samples", type=int, default=12)
    parser.add_argument("--preflight-interval", type=float, default=5.0)
    parser.add_argument("--load-max", type=float, default=4.0)
    parser.add_argument("--timeout", type=int, default=7200)
    parser.add_argument("--pause-pid", type=int)
    parser.add_argument("--pause-command-contains")
    parser.add_argument("--pause-command-sha256")
    # Clean-host waiting (every window is recorded in preflight.json; the Player starts only after a clean one).
    parser.add_argument("--preflight-attempts", type=int, default=1)
    parser.add_argument("--preflight-wait", type=float, default=60.0)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.commit):
        parser.error("--commit must be a full lowercase 40-hex SHA")
    if args.window < 1 or args.rounds < 1 or args.rounds > 50 or args.seed < 0:
        parser.error("window/rounds/seed outside supported range")
    if args.preflight_samples < 1 or not math.isfinite(args.preflight_interval) or args.preflight_interval <= 0:
        parser.error("preflight sampling must be finite and positive")
    if not math.isfinite(args.load_max) or args.load_max <= 0 or args.timeout < 1:
        parser.error("load/timeout must be finite and positive")
    if args.preflight_attempts < 1 or not math.isfinite(args.preflight_wait) or args.preflight_wait < 0:
        parser.error("preflight attempts/wait must be finite and positive")

    out = Path(args.out).resolve()
    if out.exists():
        print(f"refusing to overwrite immutable attempt directory: {out}", file=sys.stderr)
        return 64
    out.mkdir(parents=True)
    binary = Path(args.bin).resolve()
    attempt = {
        "schema": "dd-wp064-attempt-v1",
        "attemptId": str(uuid.uuid4()),
        "kind": args.kind,
        "executionCommit": args.commit,
        "startedUtc": utc_now(),
        "startedEpoch": time.time(),
        "status": "starting",
        "runnerSha256": sha256_file(Path(__file__).resolve()),
    }
    write_json(out / "attempt.json", attempt)
    ledger_append(out, {"attemptId": attempt["attemptId"], "kind": args.kind, "status": "starting"})
    if not binary.is_file():
        attempt.update({"status": "invalid", "error": "Player binary missing", "completedUtc": utc_now()})
        write_json(out / "attempt.json", attempt)
        print(f"Player binary missing: {binary}", file=sys.stderr)
        return 64
    if not os.access(PLAYER_RUN, os.X_OK):
        attempt.update({"status": "invalid", "error": "tools/player-run.sh missing", "completedUtc": utc_now()})
        write_json(out / "attempt.json", attempt)
        print(f"Player launcher missing or not executable: {PLAYER_RUN}", file=sys.stderr)
        return 66
    attempt.update({
        "binary": str(binary),
        "binarySha256": sha256_file(binary),
        "binaryBytes": binary.stat().st_size,
    })
    write_json(out / "attempt.json", attempt)

    isolation = None
    isolation_stopped = False
    if args.pause_pid is not None:
        if not args.pause_command_contains or not args.pause_command_sha256:
            print("--pause-command-contains and --pause-command-sha256 are required with --pause-pid", file=sys.stderr)
            return 64
        command_line = subprocess.check_output(
            ["ps", "-p", str(args.pause_pid), "-o", "command="],
            text=True,
            errors="replace",
        ).strip()
        command_sha256 = hashlib.sha256(command_line.encode("utf-8")).hexdigest()
        if args.pause_command_contains not in command_line or command_sha256 != args.pause_command_sha256:
            print(f"refusing to signal PID {args.pause_pid}: command identity mismatch", file=sys.stderr)
            return 66
        isolation = {
            "schema": "dd-wp064-resource-isolation-v1",
            "reason": "cross-project temporary resource isolation",
            "pid": args.pause_pid,
            "requiredCommandSubstring": args.pause_command_contains,
            "requiredCommandSha256": args.pause_command_sha256,
            "commandBefore": command_line,
            "commandBeforeSha256": command_sha256,
            "stoppedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "signal": "SIGSTOP",
            "resumeSignal": "SIGCONT",
        }

        def resume_isolated_process():
            nonlocal isolation_stopped
            if not isolation_stopped:
                return
            try:
                os.kill(args.pause_pid, signal.SIGCONT)
                isolation["resumedUtc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
                time.sleep(0.25)
                after = subprocess.check_output(
                    ["ps", "-p", str(args.pause_pid), "-o", "state=,pcpu=,command="],
                    text=True,
                    errors="replace",
                ).strip()
                isolation["processAfterResume"] = after
                isolation["resumeVerified"] = args.pause_command_contains in after
            except (OSError, subprocess.SubprocessError) as error:
                isolation["resumeVerified"] = False
                isolation["resumeError"] = str(error)
            isolation_stopped = False
            write_json(out / "resource-isolation.json", isolation)

        atexit.register(resume_isolated_process)

        def interrupted(signum, _frame):
            raise SystemExit(128 + signum)

        for handled in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(handled, interrupted)
        os.kill(args.pause_pid, signal.SIGSTOP)
        isolation_stopped = True
        write_json(out / "resource-isolation.json", isolation)

    windows = []
    clean = False
    for window_index in range(args.preflight_attempts):
        preflight = []
        window_clean = True
        for index in range(args.preflight_samples):
            # A hash-verified SIGSTOP target can retain a non-zero rolling %CPU in
            # ps for the first sample even though it cannot execute. Keep it in the
            # evidence list, but exclude that isolated PID from the heavy veto.
            snap = snapshot(args.pause_pid)
            snap["sample"] = index + 1
            preflight.append(snap)
            if snap["load1"] > args.load_max or snap["forbidden"] or snap["heavy"]:
                window_clean = False
                break   # this window is dirty; the next one starts after the wait
            if index + 1 < args.preflight_samples:
                time.sleep(args.preflight_interval)
        windows.append({"window": window_index + 1, "pass": window_clean, "samples": preflight})
        if window_clean:
            clean = True
            break
        if window_index + 1 < args.preflight_attempts:
            time.sleep(args.preflight_wait)
    write_json(out / "preflight.json", {
        "pass": clean,
        "loadMax": args.load_max,
        "samplesRequired": args.preflight_samples,
        "intervalSeconds": args.preflight_interval,
        "windowsAllowed": args.preflight_attempts,
        "windowsUsed": len(windows),
        "samples": windows[-1]["samples"] if windows else [],
        "windows": windows,
    })
    if not clean:
        attempt.update({"status": "preflight-failed", "completedUtc": utc_now(), "completedEpoch": time.time(), "exitCode": 75})
        attempt["artifactSha256"] = {"preflight.json": sha256_file(out / "preflight.json")}
        write_json(out / "attempt.json", attempt)
        ledger_append(out, {"attemptId": attempt["attemptId"], "kind": args.kind, "status": "preflight-failed", "exitCode": 75})
        print(f"clean-host preflight failed; Player not launched: {out}")
        return 75

    profile = out / "probe-profile.json"
    command = [str(binary), "-screen-fullscreen", "0", "-ddProfilePath", str(profile)]
    if args.kind == "perf":
        command += [
            "-ddPilotPerfProbe", str(out),
            "-ddPilotScene", "Run",
            "-ddPilotResolution", "1920x1080",
            "-ddPilotWarmupSeconds", "10",
            "-ddPilotWindowSeconds", str(args.window),
        ]
        # Y2: UI files and telemetry of a perf window stay in the attempt directory (a caller's own value wins)
        for flag, default in (("-ddUiDataRoot", out / "ui-data"), ("-ddTelemetryDir", out / "telemetry")):
            if flag not in args.extra_arg:
                command += [flag, str(default)]
    else:
        frames = out / "tier-frames"
        command += [
            "-ddQaBot", str(out),
            "-ddQaBotRounds", str(args.rounds),
            "-ddQaBotSeed", str(args.seed),
            "-ddQaBotFrames", str(frames),
            "-ddTelemetryDir", str(out / "telemetry"),
        ]
        if args.stability:
            command += ["-ddQaBotStability"]
    command += args.extra_arg
    command += ["-logFile", str(out / "player.log")]
    (out / "command.json").write_text(json.dumps(command, indent=2) + "\n", encoding="utf-8")
    attempt.update({
        "status": "running",
        "commandSha256": sha256_file(out / "command.json"),
        "launcher": str(PLAYER_RUN),
        "playerStartedUtc": utc_now(),
    })
    write_json(out / "attempt.json", attempt)

    # G4 D2 (M12, C-00 01.10): a TERM/HUP to this runner is SystemExit(128+n) and a Ctrl-C KeyboardInterrupt — either one
    # stops the Player the same way as the timeout (TERM through player-run.sh, 10 s, then KILL of the tree) before it goes
    # on, so the caller's guard restore never runs while an orphaned Player still writes.
    for handled in (signal.SIGTERM, signal.SIGHUP):
        if signal.getsignal(handled) in (signal.SIG_DFL, None):
            signal.signal(handled, lambda signum, _frame: sys.exit(128 + signum))
    player = subprocess.Popen([str(PLAYER_RUN)] + command)
    try:
        return run_player(args, out, attempt, player)
    except BaseException:
        stop_player(player)
        raise


def stop_player(player):
    """TERM to player-run.sh (it passes the TERM on), ≤ 10 s, then KILL of its descendants and of itself. G4 D2 review
    SHOULD-2: signals meanwhile are deferred and dropped (the exception already on its way wins) — a terminal Ctrl-C
    reaches this runner as INT and, a moment later, as wp078_perf's TERM; that TERM must not cut the KILL fallback."""
    block = guard_receipt.DeferredSignals()
    with block:
        _stop_player(player)
    return block   # the timeout path re-raises what arrived meanwhile; the exception path already has one in flight


def _stop_player(player):
    if player.poll() is not None:
        return
    player.terminate()
    try:
        player.wait(timeout=10)
    except subprocess.TimeoutExpired:
        # Y2: player-run.sh passed the TERM on; a Player that ignores it is killed with its wrapper
        rows = process_rows()
        rows_by_pid = {row["pid"]: row for row in rows}
        for row in rows:
            if row["pid"] != player.pid and process_is_descendant(row, rows_by_pid, player.pid):
                try:
                    os.kill(row["pid"], signal.SIGKILL)
                except OSError:
                    pass
        player.kill()
        player.wait()


def run_player(args, out, attempt, player):
    monitor = []
    contaminated = False
    heavy_streaks = {}
    max_heavy_streak = 0
    sustained_heavy_samples = 5
    deadline = time.monotonic() + args.timeout
    timed_out = False
    while player.poll() is None:
        snap = snapshot(player.pid, own_tree=True)
        monitor.append(snap)
        if snap["forbidden"]:
            contaminated = True
        current_heavy = {row["pid"] for row in snap["heavy"]}
        for pid in list(heavy_streaks):
            heavy_streaks[pid] = heavy_streaks[pid] + 1 if pid in current_heavy else 0
        for pid in current_heavy:
            heavy_streaks.setdefault(pid, 1)
        max_heavy_streak = max([max_heavy_streak] + list(heavy_streaks.values()))
        if max_heavy_streak >= sustained_heavy_samples:
            contaminated = True
        if time.monotonic() >= deadline:
            timed_out = True
            stop_player(player).reraise()
            break
        time.sleep(1.0)
    exit_code = player.wait()
    write_json(out / "host-monitor.json", {
        "pass": not contaminated and not timed_out,
        "timedOut": timed_out,
        "sustainedHeavyThresholdSamples": sustained_heavy_samples,
        "maxHeavyStreakSamples": max_heavy_streak,
        "samples": monitor,
    })
    (out / "exit.txt").write_text(f"exit={exit_code}\n", encoding="utf-8")
    write_json(out / "load-after.json", snapshot())
    required = ["command.json", "preflight.json", "host-monitor.json", "player.log", "exit.txt"]
    required += (["01-scene-ready.json", "02-force-40.json", "03-warmup.json", "04-window.json", "04d-gpu.json", "frame-times-ms.csv"]
                 if args.kind == "perf" else ["bot-summary.json", "qa-report.json", "events.txt"])
    missing = [name for name in required if not (out / name).is_file()]
    artifact_hashes = {name: sha256_file(out / name) for name in required if (out / name).is_file()}
    success = exit_code == 0 and not contaminated and not timed_out and not missing
    attempt.update({
        "status": "complete" if success else "failed",
        "completedUtc": utc_now(),
        "completedEpoch": time.time(),
        "exitCode": exit_code,
        "hostClean": not contaminated,
        "timedOut": timed_out,
        "missingArtifacts": missing,
        "artifactSha256": artifact_hashes,
    })
    write_json(out / "attempt.json", attempt)
    ledger_append(out, {"attemptId": attempt["attemptId"], "kind": args.kind, "status": attempt["status"], "exitCode": exit_code,
                        "hostClean": not contaminated, "timedOut": timed_out, "missingArtifacts": missing})
    print(f"{args.kind} exit={exit_code} hostClean={not contaminated} timedOut={timed_out} missing={missing} out={out}")
    if success:
        return 0
    # 76 = the host contaminated a run that otherwise worked (the caller may discard it and try again; the ledger keeps it)
    return 76 if contaminated and not timed_out and exit_code == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
