#!/usr/bin/env python3
"""WP-078 §4 · tier × camera-mode perf matrix (S-10).

Runs PilotPerfProbe windows through tools/qa/wp064_run.py perf (clean-host preflight, host monitor, attempt ledger,
command hash) for every tier and camera mode in tools/qa/perf-targets.json, each window under the C-00 §4.14 user-data
guard: tools/userdata-guard.sh snapshot --scope player → window → restore --scope player. The Player gets its own profile
(wp064_run), telemetry and UI files (-ddUiDataRoot), and the tier / mode on the command line — nothing reads or
writes the player's own settings. Every complete window counts; the verdict is tools/qa/wp078_perf_report.py.

usage: wp078_perf.py --bin <app or build dir> --out <root> --commit <sha> [--tiers sinematik,yuksek] [--modes topdown,tp]
                     [--windows N] [--window-seconds 60] [--cooldown 60] [--dry-run]
exit: 0 every window ran (see the report for pass/fail) · 6 a game Player was up and did not close · 7 user-data guard
      STOP (snapshot error, restore error or verdict mismatch — report to C-00) · 30 snapshot waited on a live DD Player ·
      75 snapshot waited on a held lock · 64 usage · 143 SIGTERM (the current window is still restored)
"""
import argparse
import signal
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


import guard_receipt  # noqa: E402  (C-00 30.09 M-17: the one guard receipt / verdict module)

class QuietWindow:
    """C-00 26.09: measure only in a quiet window — this process holds the 'player' slot (the guard, a child, re-enters it)
    and three holder shells take every Unity slot, so no batch or Player of another session starts; before each window
    load1 < load_max and no foreign Unity / Player process; a window batch lasts at most max_minutes, then everything is
    released for a pause so the other sessions' queues move (FIFO), and taken again."""

    def __init__(self, note, flag, load_max, max_minutes, pause_seconds):
        self.note, self.flag, self.load_max = note, flag, load_max
        self.max_seconds, self.pause = max_minutes * 60.0, pause_seconds
        self.slot = str(REPO / "tools" / "slot.sh")
        self.holders, self.started = [], None

    def _status(self):
        return subprocess.run([self.slot, "status"], capture_output=True, text=True).stdout

    def _unity_all_ours(self):
        lines = self._status().splitlines()
        head = next((i for i, l in enumerate(lines) if l.startswith("== unity")), None)
        if head is None:
            return False
        import re
        m = re.search(r"\((\d+)/(\d+)\)", lines[head])
        locks = []
        for line in lines[head + 1:]:
            if line.startswith("==") or line.strip().startswith("kuyruk"):
                break
            locks.append(line)
        return bool(m) and int(m.group(1)) == int(m.group(2)) and locks and all("wp078-quiet" in l for l in locks)

    def _host_quiet(self):
        comm = subprocess.run(["ps", "-Ao", "comm"], capture_output=True, text=True).stdout.splitlines()
        unity = [c for c in comm if c.strip().endswith("/MacOS/Unity")]
        load = float(subprocess.run(["sysctl", "-n", "vm.loadavg"], capture_output=True, text=True).stdout.split()[1])
        return not unity and not player_up() and load < self.load_max, f"load1 {load:.2f} foreign Unity {len(unity)}"

    def foreign_quiet(self):
        """During our own Player run: no foreign Unity editor/batch and no Blender (load is ours, not measured here)."""
        comm = subprocess.run(["ps", "-Ao", "comm"], capture_output=True, text=True).stdout.splitlines()
        foreign = [c for c in comm if c.strip().endswith("/MacOS/Unity") or c.strip().endswith("/MacOS/Blender")]
        return not foreign, f"foreign Unity/Blender {len(foreign)}"

    def acquire(self):
        subprocess.run([self.slot, "acquire", "player", "wp078-perf"], capture_output=True, text=True)
        self.flag.touch()
        for i in (1, 2, 3):
            script = (f'"{self.slot}" acquire unity wp078-quiet-{i} >/dev/null; while [ -f "{self.flag}" ]; do sleep 2; done; '
                      f'"{self.slot}" release unity wp078-quiet-{i} >/dev/null')
            self.holders.append(subprocess.Popen(["bash", "-c", script]))
        while not self._unity_all_ours():
            time.sleep(10)
        while True:
            quiet, why = self._host_quiet()
            if quiet:
                break
            time.sleep(15)
        self.started = time.monotonic()
        self.note(f"quiet window: all Unity slots + player slot held, {why}")

    def release(self):
        if self.started is None:
            return
        self.flag.unlink(missing_ok=True)
        for h in self.holders:
            h.wait()
        self.holders = []
        subprocess.run([self.slot, "release", "player", "wp078-perf"], capture_output=True, text=True)
        self.started = None
        self.note("quiet window released")

    def before_window(self, estimate_seconds):
        if self.started is not None and time.monotonic() - self.started + estimate_seconds > self.max_seconds:
            self.release()
            time.sleep(self.pause)
        if self.started is None:
            self.acquire()
        quiet, why = self._host_quiet()
        while not quiet:                     # a Unity batch that was already running when the slots were taken
            time.sleep(15)
            quiet, why = self._host_quiet()
        return why


def annotate_attempt(cell, receipt):
    """WP-078 A4: add what wp064_run does not record to the window's attempt.json — the guard receipt of this window and
    the scope the report checks, each read from the window's own evidence (command.json flags, 02-force-40.json), never
    assumed. A missing source leaves the field out, so the report says BLOCKED."""
    path = cell / "attempt.json"
    if not path.is_file():
        return
    attempt = json.loads(path.read_text(encoding="utf-8"))
    attempt["guard"] = {"snapshotExit": 0, "restoreExit": receipt.get("restoreExit"), "verified": receipt.get("verified"),
                        "outOfScopeChanged": receipt.get("outOfScopeChanged"), "touched": receipt.get("touched"),
                        "verdict": receipt.get("why"), "code": receipt.get("code")}
    attempt["buildRole"] = "probe"
    try:
        command = json.loads((cell / "command.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        command = []

    def flag(name):
        return command[command.index(name) + 1] if name in command and command.index(name) + 1 < len(command) else None
    resolution = flag("-ddPilotResolution")
    if resolution and "x" in resolution:
        w, h = resolution.split("x", 1)
        attempt["resolution"] = [int(w), int(h)]
    for key, name in (("warmupSeconds", "-ddPilotWarmupSeconds"), ("windowSeconds", "-ddPilotWindowSeconds")):
        if flag(name) is not None:
            attempt[key] = int(float(flag(name)))
    try:
        force = json.loads((cell / "02-force-40.json").read_text(encoding="utf-8"))
        for key in ("achievedCount",):   # PilotPerfProbe stage 02: the live count after forcing the ceiling (40)
            if isinstance(force.get(key), int) and not isinstance(force.get(key), bool):
                attempt["activeEnemies"] = force[key]
                break
    except (OSError, ValueError):
        pass
    path.write_text(json.dumps(attempt, indent=2) + "\n", encoding="utf-8")


def launch(command):
    """The wp064_run window (a seam for the guard tests)."""
    return subprocess.run(command, capture_output=True, text=True)


def window_args(tier_name, mode, cell, extra=()):
    # -ddQualityTier takes the lower-case key (performans|yuksek|sinematik); the probe reports the enum (probeValue)
    args = ["-ddQualityTier", tier_name, "-ddCameraMode", mode,
            "-ddUiDataRoot", str(cell / "ui-data"), "-ddTelemetryDir", str(cell / "telemetry")]
    if mode != "topdown":
        args += ["-ddPilotCameraSweep", "1"]
    # S-05 B9 (30.09): caller probe flags (e.g. -ddPilotStart x,z,yaw) go to every window, after the fixed ones; they
    # land in the window's command.json. One --out root per variant keeps its cells and ledger apart.
    return args + list(extra)


def plan(targets, tiers, modes, windows):
    """(tier, mode, windows needed) per cell — windows = the counted windows each cell must reach."""
    for name in tiers:
        for mode in modes:
            yield name, mode, windows or int(targets["tiers"][name]["minWindows"])


def cell_state(root, name, mode):
    """(counted, next free index) for one cell in this root — C-00 30.09 K8: one root and one ledger per FINAL; a
    resumed run continues at the next free index, and only complete ∧ hostClean attempts count."""
    import wp078_perf_report as report
    counted, highest = 0, 0
    for d in root.glob(f"perf-{name}-{mode}-*"):
        try:
            highest = max(highest, int(d.name.rsplit("-", 1)[1]))
        except ValueError:
            continue
        attempt = report.load(d / "attempt.json") or {}
        if report.classify(attempt) == "complete":
            counted += 1
    return counted, highest + 1


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
    parser.add_argument("--extra-arg", action="append", default=[],
                        help="extra probe argument for every window, repeatable; use the --extra-arg=<value> form for values starting with '-'")
    parser.add_argument("--no-quiet", action="store_true", help="do not take the Unity / player slots (smoke only; never for the gate)")
    parser.add_argument("--load-max", type=float, default=6.0, help="C-00 26.09: measure only below this load1")
    parser.add_argument("--quiet-minutes", type=float, default=25.0)
    parser.add_argument("--quiet-pause", type=float, default=180.0)
    parser.add_argument("--max-discards", type=int, default=6, help="K8: host-discarded re-runs allowed per cell per invocation")
    args = parser.parse_args()
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(143))   # C-00 30.09 M-14: finally blocks (restore) run on SIGTERM

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
    runs = list(plan(targets, tiers, modes, args.windows))   # (tier, mode, windows needed)
    binary = resolve_binary(args.bin)
    log = root / "wp078-perf.log"

    def note(text):
        line = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()) + " " + text
        print(line)
        with log.open("a", encoding="utf-8") as handle:
            handle.write(line + "\n")

    note(f"plan {len(runs)} cells × counted windows {[r[2] for r in runs]} · tiers={tiers} modes={modes} commit={args.commit}")
    quiet = None if (args.no_quiet or args.dry_run) else QuietWindow(note, root / ".quiet-hold", args.load_max, args.quiet_minutes, args.quiet_pause)
    try:
        return run_all(args, runs, root, binary, seconds, quiet, note)
    finally:
        if quiet is not None:
            quiet.release()


def run_all(args, runs, root, binary, seconds, quiet, note):
    """C-00 30.09 K8: every cell runs until it has its counted windows; a host-discarded window is re-run at the next
    free index in the same root (at most --max-discards extra attempts per cell per invocation)."""
    first = True
    for name, mode, need in runs:
        extra = 0
        while True:
            counted, index = cell_state(root, name, mode)
            if counted >= need:
                note(f"perf-{name}-{mode}: {counted}/{need} counted")
                break
            if extra > args.max_discards:
                note(f"perf-{name}-{mode}: {counted}/{need} counted after {extra - 1} re-runs — BLOCKED (host too busy), next cell")
                break
            if not first:
                time.sleep(args.cooldown if quiet is None else min(args.cooldown, 20.0))
            first = False
            code = run_window(args, root, binary, seconds, quiet, note, name, mode, index)
            if code is not None:
                return code
            extra += 1 if cell_state(root, name, mode)[0] == counted else 0
            if args.dry_run:
                break
    note("done")
    return 0


def run_window(args, root, binary, seconds, quiet, note, name, mode, index):
    """One guarded window; None to continue, an exit code to stop the whole matrix."""
    cell = root / f"perf-{name}-{mode}-{index}"
    command = [sys.executable, str(HERE / "wp064_run.py"), "perf", "--bin", str(binary), "--out", str(cell),
               "--commit", args.commit, "--window", str(seconds), "--load-max", str(args.load_max),
               "--preflight-attempts", "3", "--preflight-wait", "20"]
    # "--extra-arg=<flag>": argparse reads a separate value that starts with "-" as an option
    command += [f"--extra-arg={extra}" for extra in window_args(name, mode, cell, getattr(args, "extra_arg", None) or ())]
    if args.dry_run:
        note("dry-run " + " ".join(command))
        return None

    waited = 0.0
    while player_up():
        if waited >= args.player_wait:
            note(f"STOP {cell.name}: a game Player is still up after {int(waited)} s (never touched)")
            return 6
        time.sleep(60)
        waited += 60

    ug = root / "ug" / ("ug-" + cell.name)   # not "perf-…": wp078_report treats any perf-* folder outside the root as a stray window
    if quiet is not None:
        note(f"{cell.name}: {quiet.before_window(seconds + 90)}")
    snap = guard_receipt.run_guard("snapshot", ug)
    code = guard_receipt.snapshot_code(snap.returncode)
    if code:
        note(f"{'BLOCKED' if code in (30, 75) else 'STOP'} {cell.name}: guard snapshot exit {snap.returncode} → {code}; no Player started")
        return code
    run = None
    try:
        run = launch(command)
        (root / "ug").mkdir(parents=True, exist_ok=True)
        (root / "ug" / (cell.name + ".wp064-run.out")).write_text(run.stdout + run.stderr, encoding="utf-8")
    finally:
        # C-00 30.09 M-14: the restore runs on every path — normal end, error, Ctrl-C, SIGTERM (SystemExit 143)
        receipt = guard_receipt.close(ug)
        guard_code, why = receipt["code"], receipt["why"]
        annotate_attempt(cell, receipt)
        if guard_code:
            note(f"STOP {cell.name}: {why} — report to C-00")
    if guard_code:
        return guard_code
    note(f"{cell.name} wp064_run exit {run.returncode} · guard ok ({why})")
    if run.returncode in (2, 64, 66):
        note(f"STOP {cell.name}: wp064_run usage/input error (exit {run.returncode}) — see ug/<cell>.wp064-run.out")
        return 64
    return None


if __name__ == "__main__":
    raise SystemExit(main())
