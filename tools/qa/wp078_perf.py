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
      75 snapshot waited on a held lock · 64 usage, DD_GUARD_ROOT set or the canonical guard missing (G4 D4) · 65 a window
      was NOT restored (a Player still up 60 s after it: ug/<cell>/userdata-NOT-RESTORED.txt), or such a marker is still
      unresolved under <out>/ug (nothing started) — STOP, C-00; never retried as a wait · 130/143/129 INT/TERM/HUP (the window is
      still restored first)
Guard (G4 D2/D4, C-00 01.10): the canonical driver guard (guard_receipt.GUARD); on an error or a signal wp064_run gets TERM
(it stops its Player), ≤ 30 s, then KILL; the restore waits ≤ 60 s for no game Player to be left, runs with INT/TERM/HUP
deferred (the guard child ignores them) and only then is the signal raised; the quiet window is released after it.
"""
import argparse
import signal
import json
import re
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PLAYER_EXE = "MacOS/Deaths Demolition - Playable Concept"
# one window's wall time beyond its measured seconds: preflight ~55 s, launch, warm-up, quit (test review
# 06.10: ~190-200 s per 60 s window in practice; the old 90 s let a batch overrun its 25 min)
WINDOW_OVERHEAD_S = 140
BASELINE_FAIL_STOP = 3   # NOTE-4: consecutive R1 windows failed on the build before the R1 side stops


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


def players():
    """Live game Player processes as "pid comm" (executable path — other sessions' shells mention the name in scripts)."""
    # G4 D2 review SHOULD-1: a group Ctrl-C must not kill ps into an empty answer ("no Player" → a restore under a writer);
    # ps ignores INT/TERM/HUP, and a failed ps counts as a live Player (fail closed)
    ps = subprocess.run(["ps", "-Ao", "pid=,comm="], capture_output=True, text=True, preexec_fn=guard_receipt._ignore_signals)
    if ps.returncode != 0:
        return [f"? ps exit {ps.returncode} (counted as a live Player)"]
    return [line.strip() for line in ps.stdout.splitlines() if line.strip().endswith(PLAYER_EXE)]


def player_up():
    return bool(players())


import guard_receipt  # noqa: E402  (C-00 30.09 M-17: the one guard receipt / verdict module)

class QuietWindow:
    """C-00 26.09: measure only in a quiet window — this process holds the 'player' slot (the guard, a child, re-enters it)
    and three holder shells take every Unity slot, so no batch or Player of another session starts; before each window
    load1 < load_max and no foreign Unity / Player process; a window batch lasts at most max_minutes, then everything is
    released for a pause so the other sessions' queues move (FIFO), and taken again."""

    def __init__(self, note, flag, load_max, max_minutes, pause_seconds, wait_max_seconds=1800.0):
        self.note, self.flag, self.load_max = note, flag, load_max
        self.max_seconds, self.pause = max_minutes * 60.0, pause_seconds
        # test review SHOULD-4 (R1 FINAL'' sat a whole hour on load1 6.9-15.3 with every slot held): a host that
        # does not go quiet within wait_max gets every slot back, the queues move, and the window is tried again after pause
        self.wait_max = wait_max_seconds
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
            # a lock is "  <n>.<tag>.<pid>: <utc> <tag>"; slot.sh v4 prints each lock file whole, so its second line
            # "start=<lstart>" (the pid-reuse guard) follows unindented — not a lock (S-10 06.10: R2 FINAL z3 waited on it)
            if re.match(r"^\s+\d+\.\S+: ", line):
                locks.append(line)
        # held >= limit, not ==: slot.sh lowers the limit to 2 under load1 >= 24 / swap >= 10 GB while our three stay
        # held ("(3/2)") — the iCloud load this run is meant to measure through (gt 06.10 SHOULD-1)
        return bool(m) and int(m.group(1)) >= int(m.group(2)) and len(locks) == int(m.group(1)) \
            and all("wp078-quiet" in l for l in locks)

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

    def _take_slots(self):
        subprocess.run([self.slot, "acquire", "player", "wp078-perf"], capture_output=True, text=True)
        self.flag.touch()
        for i in (1, 2, 3):
            script = (f'"{self.slot}" acquire unity wp078-quiet-{i} >/dev/null; while [ -f "{self.flag}" ]; do sleep 2; done; '
                      f'"{self.slot}" release unity wp078-quiet-{i} >/dev/null')
            self.holders.append(subprocess.Popen(["bash", "-c", script]))
        while not self._unity_all_ours():
            time.sleep(10)

    def _give_back(self):
        self.flag.unlink(missing_ok=True)
        for h in self.holders:
            h.wait()
        self.holders = []
        subprocess.run([self.slot, "release", "player", "wp078-perf"], capture_output=True, text=True)

    def _wait_quiet(self):
        """(True, why) once the host is quiet; (False, why) after wait_max seconds of waiting with the slots held."""
        waited = 0.0
        while True:
            quiet, why = self._host_quiet()
            if quiet or waited >= self.wait_max:
                return quiet, why
            time.sleep(15)
            waited += 15

    def acquire(self):
        while True:
            self._take_slots()
            quiet, why = self._wait_quiet()
            if quiet:
                break
            self._give_back()
            self.note(f"quiet window: host not quiet after {self.wait_max:.0f} s ({why}) — every slot given back, retry in {self.pause:.0f} s")
            time.sleep(self.pause)
        self.started = time.monotonic()
        self.note(f"quiet window: all Unity slots + player slot held, {why}")

    def release(self):
        if self.started is None:
            return
        self._give_back()
        self.started = None
        self.note("quiet window released")

    def before_window(self, estimate_seconds):
        """estimate 0 = the second window of a pair budgeted at the pair's start: never a budget boundary here (SHOULD-2);
        a host that stays busy past wait_max still gives every slot back, and then the pair is split (gt 06.10 N-4)."""
        if self.started is not None and estimate_seconds and time.monotonic() - self.started + estimate_seconds > self.max_seconds:
            self.release()
            time.sleep(self.pause)
        while True:
            if self.started is None:
                self.acquire()
            quiet, why = self._wait_quiet()   # a Unity batch that was already running when the slots were taken
            if quiet:
                return why
            self.release()
            self.note(f"quiet window: host not quiet after {self.wait_max:.0f} s ({why}) — released, retry in {self.pause:.0f} s")
            time.sleep(self.pause)


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


def launch(command, grace=30.0):
    """The wp064_run window (a seam for the guard tests). G4 D2 (M12): on an exception or a signal wp064_run gets TERM, ≤
    grace s, then KILL (guard_receipt.stop_child) — subprocess.run's immediate SIGKILL would orphan its Player."""
    proc = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        out, err = proc.communicate()
    except BaseException:
        guard_receipt.stop_child(proc, grace)
        raise
    return subprocess.CompletedProcess(command, proc.returncode, out, err)


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


def window_kind(root, name, mode, index):
    """complete / discarded / failed — wp078_perf_report.classify of the window just run (test review NOTE-4)."""
    import wp078_perf_report as report
    return report.classify(report.load(root / f"perf-{name}-{mode}-{index}" / "attempt.json") or {})


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
    parser.add_argument("--baseline-bin", help="WP-091: the R1 release-probe build, run alternately with --bin in the same window")
    parser.add_argument("--baseline-out", help="WP-091: the R1 perf root (its own cells and ledger)")
    parser.add_argument("--baseline-commit", help="WP-091: the R1 commit recorded for the baseline windows")
    parser.add_argument("--extra-arg", action="append", default=[],
                        help="extra probe argument for every window, repeatable; use the --extra-arg=<value> form for values starting with '-'")
    parser.add_argument("--no-quiet", action="store_true", help="do not take the Unity / player slots (smoke only; never for the gate)")
    parser.add_argument("--load-max", type=float, default=6.0, help="C-00 26.09: measure only below this load1")
    parser.add_argument("--quiet-minutes", type=float, default=25.0)
    parser.add_argument("--quiet-pause", type=float, default=180.0)
    parser.add_argument("--quiet-wait-max", type=float, default=1800.0,
                        help="seconds to wait for a quiet host with every slot held before giving them back and retrying")
    parser.add_argument("--max-discards", type=int, default=6, help="K8: host-discarded re-runs allowed per cell per invocation")
    parser.add_argument("--baseline-fail-stop", type=int, default=BASELINE_FAIL_STOP,
                        help="WP-091: the R1 side stops for the rest of the matrix after this many consecutive windows that "
                             "failed on the R1 build, not on the host (P2 then stays BLOCKED)")
    args = parser.parse_args()
    # C-00 30.09 M-14 / G4 D2: TERM and HUP become SystemExit(128+n), so every finally (restore, quiet release) runs
    for signum in (signal.SIGTERM, signal.SIGHUP):
        if signal.getsignal(signum) in (signal.SIG_DFL, None):   # review SHOULD-4: nohup / `&` keep their SIG_IGN
            signal.signal(signum, lambda n, _frame: sys.exit(128 + n))
    if not args.dry_run:
        refusal = guard_receipt.canon_refusal()
        if refusal:
            print(f"guard refused (G4 D4): {refusal} — nothing started", file=sys.stderr)
            return 64

    if args.baseline_fail_stop < 1:   # gt KP-10: 0 would stop the R1 side after its first window, whatever it was
        print("--baseline-fail-stop must be at least 1", file=sys.stderr)
        return 64
    targets = json.loads(Path(args.targets).read_text(encoding="utf-8"))
    tiers = [t for t in args.tiers.split(",") if t] or list(targets["tiers"])
    modes = [m for m in args.modes.split(",") if m] or list(targets["modes"])
    unknown = [t for t in tiers if t not in targets["tiers"]] + [m for m in modes if m not in targets["modes"]]
    if unknown:
        print(f"unknown tier/mode: {unknown}", file=sys.stderr)
        return 64

    root = Path(args.out).resolve()
    open_markers = guard_receipt.unresolved((root / "ug").glob("*/" + guard_receipt.MARKER))
    if open_markers:
        print(f"unrestored guard window(s), resolve with C-00 first — nothing started: {open_markers}", file=sys.stderr)
        return 65
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
    quiet = None if (args.no_quiet or args.dry_run) else QuietWindow(note, root / ".quiet-hold", args.load_max, args.quiet_minutes,
                                                                     args.quiet_pause, args.quiet_wait_max)
    baseline = None
    if args.baseline_bin:
        # WP-091: the R1 probe build in its own root, recorded under its own commit
        if not (args.baseline_out and args.baseline_commit and re.fullmatch(r"[0-9a-f]{40}", args.baseline_commit)):
            print("--baseline-bin needs --baseline-out and --baseline-commit <40-hex>", file=sys.stderr)
            return 64
        baseline_root = Path(args.baseline_out).resolve()
        baseline_root.mkdir(parents=True, exist_ok=True)
        baseline_args = argparse.Namespace(**{**vars(args), "commit": args.baseline_commit})
        baseline = ("r1:", baseline_args, baseline_root, resolve_binary(args.baseline_bin))
        note(f"baseline (R1) alternating: bin={args.baseline_bin} root={baseline_root} commit={args.baseline_commit}")
    try:
        return run_all(args, runs, root, binary, seconds, quiet, note, baseline)
    finally:
        if quiet is not None:
            quiet.release()


def run_all(args, runs, root, binary, seconds, quiet, note, baseline=None):
    """C-00 30.09 K8: every cell runs until it has its counted windows; a host-discarded window is re-run at the next
    free index in the same root (at most --max-discards extra attempts per cell per invocation).
    WP-091 (C-00 05.10): with a baseline (R1 probe build, its own root and commit) the two builds ALTERNATE window by
    window inside the same quiet window, so the machine state weighs on both alike — ABBA within a cell and the starting
    side swapped from one cell to the next (R2 R1 R1 R2 R2 R1 | R1 R2 R2 R1 …: a drift that favours the second slot of a
    pair cancels; test review SHOULD-1), and a pair is budgeted at its start so a quiet-batch boundary never
    falls between its two windows (SHOULD-2; only a host that stays busy past --quiet-wait-max still splits one)."""
    sides = [("", args, root, binary)]
    if baseline is not None:
        sides.append(baseline)
    first = True
    # test review NOTE-4: an R1 build that fails on its own (not the host) would run max_discards + 1 windows in
    # every cell, ~60 empty windows over the matrix; after baseline_fail_stop consecutive ones it stops for the rest of the
    # matrix. A host discard neither counts nor resets; a counted window resets. FINAL's side is never stopped here.
    r1_failed, fail_stop = 0, getattr(args, "baseline_fail_stop", BASELINE_FAIL_STOP)
    for cell_index, (name, mode, need) in enumerate(runs):
        extra = {tag: 0 for tag, *_ in sides}
        round_index = 0
        while True:
            pending = []
            for tag, side_args, side_root, _ in sides:
                counted, _index = cell_state(side_root, name, mode)
                if counted >= need:
                    continue
                if extra[tag] > args.max_discards:
                    continue
                pending.append(tag)
            if not pending:
                for tag, side_args, side_root, _ in sides:
                    counted, _index = cell_state(side_root, name, mode)
                    label = f"{tag}perf-{name}-{mode}"
                    if counted >= need:
                        note(f"{label}: {counted}/{need} counted")
                    else:
                        note(f"{label}: {counted}/{need} counted after {extra[tag] - 1} re-runs — BLOCKED (host too busy), next cell")
                break
            order = sides if (round_index + cell_index) % 2 == 0 else list(reversed(sides))
            round_index += 1
            pair = [s for s in order if s[0] in pending]
            if quiet is not None and len(pair) > 1:
                quiet.before_window(len(pair) * (seconds + WINDOW_OVERHEAD_S))   # the pair fits in this batch, or a new one
            for slot, (tag, side_args, side_root, side_binary) in enumerate(pair):
                counted, index = cell_state(side_root, name, mode)
                if not first:
                    time.sleep(args.cooldown if quiet is None else min(args.cooldown, 20.0))
                first = False
                estimate = 0 if slot > 0 else (seconds + WINDOW_OVERHEAD_S)   # 0: inside a pair already budgeted
                code = run_window(side_args, side_root, side_binary, seconds, quiet, note, name, mode, index, estimate)
                if code is not None:
                    return code
                extra[tag] += 1 if cell_state(side_root, name, mode)[0] == counted else 0
                if baseline is not None and tag == baseline[0] and not args.dry_run:
                    kind = window_kind(side_root, name, mode, index)
                    r1_failed = r1_failed + 1 if kind == "failed" else 0 if kind == "complete" else r1_failed
                    if r1_failed >= fail_stop:
                        note(f"{tag}perf-{name}-{mode}-{index}: {r1_failed} consecutive R1 windows failed on the build, not the "
                             f"host — the R1 side stops for the rest of the matrix; FINAL's windows go on (P2 BLOCKED: not "
                             f"every cell measured on both sides)")
                        sides = sides[:1]
                        break
            if args.dry_run:
                break
    note("done")
    return 0


def run_window(args, root, binary, seconds, quiet, note, name, mode, index, estimate=None):
    """One guarded window; None to continue, an exit code to stop the whole matrix. estimate: the wall time the quiet
    batch must still have (0 = the second window of a pair budgeted at its start)."""
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
        note(f"{cell.name}: {quiet.before_window(seconds + WINDOW_OVERHEAD_S if estimate is None else estimate)}")
    snap = guard_receipt.run_guard("snapshot", ug)
    code = guard_receipt.snapshot_code(snap.returncode)
    if code:
        note(f"{'BLOCKED' if code in (30, 75) else 'STOP'} {cell.name}: guard snapshot exit {snap.returncode} → {code}; no Player started")
        return code
    run, inflight = None, None
    try:
        run = launch(command)
        (root / "ug").mkdir(parents=True, exist_ok=True)
        (root / "ug" / (cell.name + ".wp064-run.out")).write_text(run.stdout + run.stderr, encoding="utf-8")
    except BaseException as error:   # error, Ctrl-C, SIGTERM/SIGHUP (SystemExit): restore first, then it goes on
        inflight = error
    # C-00 30.09 M-14 / G4 D2: the restore half runs on every path, signals deferred; a Player still up → not restored, 65
    receipt, signals = guard_receipt.finish(ug, "player", players)
    guard_code, why = receipt["code"], receipt["why"]
    if guard_code != guard_receipt.NOT_RESTORED:
        annotate_attempt(cell, receipt)
    if guard_code:
        note(f"STOP {cell.name}: {why} — report to C-00")
    if guard_code == guard_receipt.NOT_RESTORED:
        return guard_code   # the marker wins over a pending signal or error (the matrix stops either way)
    signals.reraise()
    if inflight is not None:
        raise inflight
    if guard_code:
        return guard_code
    note(f"{cell.name} wp064_run exit {run.returncode} · guard ok ({why})")
    if run.returncode in (2, 64, 66):
        note(f"STOP {cell.name}: wp064_run usage/input error (exit {run.returncode}) — see ug/<cell>.wp064-run.out")
        return 64
    return None


if __name__ == "__main__":
    raise SystemExit(main())
