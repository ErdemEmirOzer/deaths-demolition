#!/usr/bin/env python3
"""WP-078 §4 · tier × camera-mode perf matrix (S-10).

Runs PilotPerfProbe windows through tools/qa/wp064_run.py perf (clean-host preflight, host monitor, attempt ledger,
command hash) for every tier and camera mode in tools/qa/perf-targets.json, each window under the C-00 §4.14 user-data
guard: tools/userdata-guard.sh snapshot → window → restore --scope player. The Player gets its own profile
(wp064_run), telemetry and UI files (-ddUiDataRoot), and the tier / mode on the command line — nothing reads or
writes the player's own settings. Every complete window counts; the verdict is tools/qa/wp078_perf_report.py.

usage: wp078_perf.py --bin <app or build dir> --out <root> --commit <sha> [--tiers sinematik,yuksek] [--modes topdown,tp]
                     [--windows N] [--window-seconds 60] [--cooldown 60] [--dry-run]
exit: 0 every window ran (see the report for pass/fail) · 6 a game Player was up and did not close · 7 user-data guard
      mismatch (STOP, report to C-00) · 64 usage
"""
import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PLAYER_EXE = "MacOS/Deaths Demolition - Playable Concept"


def resolve_binary(path):
    """wp064_run wants the Player executable; accept a build dir, an .app or the executable itself."""
    p = Path(path).resolve()
    if p.is_file():
        return p
    apps = [p] if p.suffix == ".app" else sorted(p.glob("*.app"))
    for app in apps:
        candidates = sorted((app / "Contents" / "MacOS").glob("*"))
        if candidates:
            return candidates[0]
    return p


def player_up():
    """A game Player process (executable path — other sessions' shells mention the name in their scripts)."""
    out = subprocess.run(["ps", "-Ao", "comm"], capture_output=True, text=True).stdout
    return any(line.strip().endswith(PLAYER_EXE) for line in out.splitlines())


PLAYER_PREFS_DOMAIN = "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
ENGINE_KEYS = ("Screenmanager",)          # + UnitySelectMonitor: Unity writes the window state on every Player exit
IGNORED_KEYS = ("unity.player_session_count", "unity.player_sessionid")


def export_prefs(target):
    subprocess.run(["defaults", "export", PLAYER_PREFS_DOMAIN, str(target)], capture_output=True)


def changed_prefs(before, after):
    """Keys that differ between two PlayerPrefs plists (launch counters ignored)."""
    import plistlib

    def load(path):
        try:
            with open(path, "rb") as handle:
                return plistlib.load(handle)
        except Exception:
            return {}
    a, b = load(before), load(after)
    return sorted(k for k in set(a) | set(b) if k not in IGNORED_KEYS and a.get(k) != b.get(k))


def guard_verdict(ug, restore_text, after_plist):
    """C-00 25.09: the guard must verify its restore (verified=1); a touched file is acceptable only when it is the player's
    PlayerPrefs plist and every changed key is Unity's own window state, which the guard restores exactly."""
    import re
    fields = dict(re.findall(r"(\w+)=(\S+)", restore_text.strip().splitlines()[-1] if restore_text.strip() else ""))
    if fields.get("verified") != "1":
        return False, "guard verified!=1"
    oos = ug / "out-of-scope-changed.txt"
    if oos.is_file() and oos.stat().st_size > 0:
        return False, "out-of-scope changes"
    if fields.get("touched") in ("0", None):
        return True, "touched=0"
    keys = changed_prefs(ug / "snap" / "PR" / "p1.plist", after_plist)
    (ug / "prefs-diff.txt").write_text("\n".join(keys) + "\n", encoding="utf-8")
    engine = keys and all(k.startswith(ENGINE_KEYS) or k == "UnitySelectMonitor" for k in keys)
    return (True, f"touched={fields.get('touched')} engine window keys only") if engine and fields.get("touched") == "1" \
        else (False, f"touched={fields.get('touched')} keys={keys}")


def guard(command, directory):
    args = [str(REPO / "tools" / "userdata-guard.sh"), command, str(directory)]
    if command == "restore":
        args += ["--scope", "player"]
    return subprocess.run(args, capture_output=True, text=True)


def window_args(tier_name, mode, cell):
    # -ddQualityTier takes the lower-case key (performans|yuksek|sinematik); the probe reports the enum (probeValue)
    args = ["-ddQualityTier", tier_name, "-ddCameraMode", mode,
            "-ddUiDataRoot", str(cell / "ui-data"), "-ddTelemetryDir", str(cell / "telemetry")]
    if mode != "topdown":
        args += ["-ddPilotCameraSweep", "1"]
    return args


def plan(targets, tiers, modes, windows):
    for name in tiers:
        for mode in modes:
            count = windows or int(targets["tiers"][name]["minWindows"])
            for index in range(1, count + 1):
                yield name, mode, index


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bin", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--targets", default=str(HERE / "perf-targets.json"))
    parser.add_argument("--tiers", default="")
    parser.add_argument("--modes", default="")
    parser.add_argument("--windows", type=int, default=0, help="windows per cell (default: the tier's minWindows)")
    parser.add_argument("--window-seconds", type=int, default=0)
    parser.add_argument("--cooldown", type=float, default=60.0)
    parser.add_argument("--player-wait", type=float, default=1800.0, help="seconds to wait for another game Player to close")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    targets = json.loads(Path(args.targets).read_text(encoding="utf-8"))
    tiers = [t for t in args.tiers.split(",") if t] or list(targets["tiers"])
    modes = [m for m in args.modes.split(",") if m] or list(targets["modes"])
    unknown = [t for t in tiers if t not in targets["tiers"]] + [m for m in modes if m not in targets["modes"]]
    if unknown:
        print(f"unknown tier/mode: {unknown}", file=sys.stderr)
        return 64

    root = Path(args.out).resolve()
    root.mkdir(parents=True, exist_ok=True)
    seconds = args.window_seconds or int(targets["windowSeconds"])
    runs = list(plan(targets, tiers, modes, args.windows))
    binary = resolve_binary(args.bin)
    log = root / "wp078-perf.log"

    def note(text):
        line = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()) + " " + text
        print(line)
        with log.open("a", encoding="utf-8") as handle:
            handle.write(line + "\n")

    note(f"plan {len(runs)} windows · tiers={tiers} modes={modes} commit={args.commit}")
    for position, (name, mode, index) in enumerate(runs):
        cell = root / f"perf-{name}-{mode}-{index}"
        command = [sys.executable, str(HERE / "wp064_run.py"), "perf", "--bin", str(binary), "--out", str(cell),
                   "--commit", args.commit, "--window", str(seconds), "--preflight-attempts", "30", "--preflight-wait", "60"]
        # "--extra-arg=<flag>": argparse reads a separate value that starts with "-" as an option
        command += [f"--extra-arg={extra}" for extra in window_args(name, mode, cell)]
        if args.dry_run:
            note("dry-run " + " ".join(command))
            continue

        waited = 0.0
        while player_up():
            if waited >= args.player_wait:
                note(f"STOP {cell.name}: a game Player is still up after {int(waited)} s (never touched)")
                return 6
            time.sleep(60)
            waited += 60

        ug = root / "ug" / cell.name
        snap = guard("snapshot", ug)
        if snap.returncode != 0:
            note(f"STOP {cell.name}: guard snapshot exit {snap.returncode}")
            return 7
        run = subprocess.run(command, capture_output=True, text=True)
        (root / "ug" / (cell.name + ".wp064-run.out")).write_text(run.stdout + run.stderr, encoding="utf-8")
        export_prefs(ug / "prefs-after-run.plist")
        restore = guard("restore", ug)
        (ug / "restore.txt").write_text(restore.stdout + restore.stderr, encoding="utf-8")
        ok, why = guard_verdict(ug, restore.stdout, ug / "prefs-after-run.plist")
        if restore.returncode != 0 or not ok:
            note(f"STOP {cell.name}: guard restore exit {restore.returncode}, {why} — report to C-00")
            return 7
        note(f"{cell.name} wp064_run exit {run.returncode} · guard ok ({why})")
        if run.returncode in (2, 64, 66):
            note(f"STOP {cell.name}: wp064_run usage/input error (exit {run.returncode}) — see ug/<cell>.wp064-run.out")
            return 64
        if position + 1 < len(runs):
            time.sleep(args.cooldown)
    note("done")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
