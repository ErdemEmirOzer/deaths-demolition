#!/usr/bin/env python3
"""WP-078 R1 acceptance chain on one FINAL commit (S-10; C-00 30.09 M-16). Replaces the retired KADRO r1-accept2.sh.

Every step calls the existing tools as subprocesses — nothing is re-implemented here:
  a6  builds       tools/build.sh dev | release | release-probe --out <E>/build-{dev,release,probe}, then
                   tools/qa/library_provenance.py for each — each build under the Editor user-data guard (scope editor)
  a0  provenance   tools/qa/library_provenance.py <repo> <E>/a0/library-provenance.json (read-only, post-build)
  a1  suites       tools/test.sh editmode|playmode --out <E>/a1/<mode> [--filter …] — each under guard_receipt:
                   snapshot → test.sh → finally restore; EditMode scope "editor", PlayMode scope "player" (C-00 30.09
                   lock order player → unity: the snapshot takes the player slot before test.sh takes a Unity slot)
  a2  bots         tools/qa/wp078_bot.py gameplay × topdown/tp/fp (seed 61, 5 rounds, tier sinematik) + route × 3 modes
  a3  AI flags     wp078_bot.py --kind flag, one Player smoke per wp078_report.AI_FLAGS flag × value 0/1 (1 round)
  a5  soak         wp078_bot.py --kind stability --quiet (no Unity slots held) seeds 61 and 26 × 12 rounds, then
                   soak_gate.py --stability (recorded); real-bot report runs (kind report): 5 topdown, 2 tp, 2 fp
  a4  perf         tools/qa/wp078_perf.py --bin <E>/build-probe --out <E>/a4/perf (release-probe, K1), then
                   wp078_perf_report.py (cross-check, recorded); in an R2 round with --r1-evidence the R1 FINAL's own
                   release-probe build alternates with FINAL's window by window (--baseline-bin, root <E>/a4/perf-r1) and the
                   report writes P2's R2 - R1 comparison as <E>/r2/r2-P2.json (WP-091, C-00 05.10)
  b9  poses        S-05's B9 pose windows on the same release-probe build (--b9-poses), one wp078_perf root per pose
  z3  Z3 + P3      R2 round: S-05's fixed-pose Z3 matrix, R1 and FINAL probe builds alternating (constants from
                   tools/qa/r2_z3.py: pose, tier, modes, >= 5 pairs, -ddArmor all), then r2_z3.py writes r2-Z3.json and
                   r2-P3.json (WP-091, C-00 06.10)
  a8  report       assembles <E>/manifest.json from the evidence tree (paths relative to <E>, every artifact with its
                   sha256; a field without real evidence stays absent → the report says BLOCKED) and runs
                   tools/qa/wp078_report.py --manifest … --evidence-root <E> --out <E>/report-<utc>
Order (dependency): a6 → a0 → a1 → a2 → a3 → r2 → a5 → a4 → b9 → z3 → r2q → a8, whatever order --stages lists them in;
a4, b9 and z3 each take the quiet window themselves, so the quiet phase can run in two sittings (a4, then b9 + z3).

Each stage writes an execution record <E>/stages/<stage>.json (executionCommit, exitCode, guard, artifactSha256 of the
outputs it produced) — that record is the step provenance the manifest binds. Guarded builds / suites write a unit
record <E>/stages/units/<name>.json. <E>/accept.log gets one UTC line per step.

Stop rules: a guard STOP (exit 7 of a runner or of the Editor guard) stops the whole chain at once; a snapshot / host
wait (30, 75, or 6 = a game Player up) is retried --wait-tries times, then the chain stops. G4 D2 (review MUST-1): a
window that was NOT restored (runner exit 65, userdata-NOT-RESTORED.txt) stops the chain at once, and an unresolved
marker anywhere under <E> stops it before every guarded unit and every retry — never a wait, never moved aside. Each stop prints
"STOP … report to C-00". A failed gameplay / perf / suite result does not stop later independent stages — the report
shows it. The tree must stay at FINAL and clean before every stage (exit 2 otherwise).

usage: wp078_accept.py --final <40-hex> [--evidence DIR] [--stages a6,a0,a1,a2,a3,r2,a5,a4,b9,z3,r2q,a8] [--dry-run] [--resume]
                       [--editmode-filter RE] [--playmode-filter RE] [--wait-tries 3] [--wait-seconds 120]
exit: 0 chain ran and the report PASSes (without a8: every selected stage PASS) · 1 an honest non-PASS · 2 HEAD is not
      FINAL / the tree is dirty · 3 R2 round: r2_gates_lint finds an ERROR at FINAL (nothing started) · 7 guard STOP · 65 a guard window NOT restored (or an unresolved marker under <E>) ·
      6 / 30 / 75 a wait persisted · 64 usage or existing evidence without --resume · 143 SIGTERM (the current child got SIGTERM and was waited for; Editor guard restored)
"""
import argparse
import hashlib
import json
import os
import importlib.util
import re
import shlex
import signal
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))
import guard_receipt  # noqa: E402  (the one guard receipt module; Editor runs use scope "editor")
import dd_paths  # noqa: E402  (WP-089: the one KADRO root; guard_receipt put tools/ on sys.path)
import wp078_perf  # noqa: E402  (resolve_binary)
import wp078_perf_report  # noqa: E402  (CELL: the perf window directory layout)
import r2_gates  # noqa: E402  (WP-091 R2 measurement sessions)
import r2_load_time  # noqa: E402  (WP-091 P5: Selection → Run from the bot sessions' load-timing.json)
import wp078_report  # noqa: E402  (AI_FLAGS, AREAS, ROUTE_KEYS, SCHEMA)
import r2_gates_lint  # noqa: E402  (WP-091: the R2 contract, its prose and its C# writers agree before a FINAL starts)

PROJECT = "prototype/DeathsDemolition_PlayableConcept"
# C-00 05.10: repo and KADRO are moving off iCloud (~/Dev, symlinks at the old paths). WP-089: the root comes from
# tools/dd_paths.py (the one constant, also behind dd-env.sh DD_KADRO) — never from the environment, so an exported
# DD_KADRO cannot split this tool from the shell tools; move day changes dd_paths.KADRO_REL only.
KADRO_ROOT = dd_paths.KADRO
KADRO_EVIDENCE = dd_paths.KPROJ / "wp-078" / "evidence"
STAGE_ORDER = ("a6", "a0", "a1", "a2", "a3", "r2", "a5", "a4", "b9", "z3", "r2q", "a8")
# main-ci parity (S-01 96d0bfeb, C-00 05.10): the core EditMode + PlayMode runs exclude [Category("Diagnostics")] — an
# [Explicit] Diagnostics test picked up by a class regex (AudioSliceTests.SilahKiti_…) left sg_1 selected and turned 4
# later S-02 tests red. `alone` steps, targeted reruns and the r2/r2q full-name sessions keep no category filter.
CI_CATEGORY = "!^Diagnostics$"
# WP-091 (gt 06.10 B6): owner A/B switches a measurement session must never inherit from the shell that runs the chain —
# with the env names every measuredBy declares, each session starts with all of them unset except its own
OWNER_SWITCHES = ("DD_HOLD_IK", "DD_EDC_WEAPONS")
PLAYMODE_TIMEOUT_MIN = 90   # A1 full PlayMode watchdog (test.sh default 45 min; main-ci uses 60)
ISOLATED_TIMEOUT_MIN = 10   # one isolated test in its own session (main-ci --alone-timeout default)
# KADRO game-test-engineer SHOULD-6 + C-00 06.10 (8626d147, r2_s3's criterion): a quiet r2q session starts with no foreign
# heavy process (HOST_HEAVY), waits for that at most HOST_WAIT_MAX, and the host is sampled every HOST_INTERVAL s while it
# runs. The load average is reported, never a criterion: iCloud I/O keeps load1 at 26-27 here with nothing heavy running.
HOST_WAIT_MAX, HOST_INTERVAL = 1800.0, 5.0
HOST_HEAVY = ("foreignUnity", "foreignPlayer", "foreignBlender", "foreignFfmpeg")


def _ci_isolated():
    spec = importlib.util.spec_from_file_location("_wp078_ci_isolated", REPO / "tools" / "ci_isolated.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def testsh_has_category():
    """tools/test.sh at this checkout knows --category (S-01 96d0bfeb); main-ci makes the same check."""
    try:
        return "--category" in (REPO / "tools" / "test.sh").read_text(encoding="utf-8")
    except OSError:
        return False


def _r2_z3():
    """S-05's tools/qa/r2_z3.py at this tree (the Z3/P3 writer and the one home of the Z3 pose constants), or None."""
    path = REPO / "tools" / "qa" / "r2_z3.py"
    if not path.is_file():
        return None
    spec = importlib.util.spec_from_file_location("_wp078_r2_z3", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _app_manifest():
    spec = importlib.util.spec_from_file_location("_wp078_app_manifest", REPO / "tools" / "app_manifest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def ci_plan():
    """R2 (C-00 01.10 A1 decision): main-ci's PlayMode definition at this tree — the core -testFilter (tools/ci-suites.txt
    minus every tools/ci-isolated.txt entry), each `alone` entry in its own session (main-ci does the same), and each `a3`
    entry, which main-ci never runs and WP-078 A3 runs in its own session. Returns (core, alone, a3); alone/a3 are
    [(step id, filter)] whose id is also the out dir name."""
    ci = _ci_isolated()
    entries = ci.parse_isolated(REPO / "tools" / "ci-isolated.txt")
    core = ci.core_filter(ci.read_suites(REPO / "tools" / "ci-suites.txt"), entries)
    a3 = []
    for pattern, where, _ in entries:
        if where == "a3":
            m = re.match(r"\^?(\w+)", pattern.split("\\.")[-1])
            a3.append((f"a3-isolated-{len(a3) + 1}-{(m.group(1)[:60] if m else 'test')}", f"(?:{pattern})"))
    return core, ci.alone_steps(entries), a3
STAGE_OUTPUTS = {"a6": ("build-dev", "build-release", "build-probe"), "a0": ("a0",), "a1": ("a1",), "a2": ("a2",),
                 "a3": ("a3",), "r2": ("r2",), "a5": ("a5",), "a4": ("a4",), "b9": ("b9",), "z3": ("z3",),
                 "r2q": (), "a8": ()}   # r2q (and z3's r2_z3.py) write into r2/ too
BUILDS = {"dev": "dev", "release": "release", "probe": "release-probe"}   # role → tools/build.sh mode (K1)
R1_ROOT = wp078_report.R1_BASELINE_ROOT   # WP-091 P2: <E>/a4/perf-r1, the R1 probe build's windows (one source: the report's)
B9_NAME = wp078_report.POSE_NAME           # a B9 pose name = its <E>/b9/<name> folder
MODES = ("topdown", "tp", "fp")
TIER = "sinematik"                        # DEC-100 default tier, given explicitly (K5)
GAMEPLAY_SEED, GAMEPLAY_ROUNDS = 61, 5    # A2: TD/TP/FP × 5 rounds, seed 61
ROUTE_ROUNDS = 1                          # the route run owns every frame; no rounds are played (R1–R3)
FLAG_ROUNDS = 1                           # A3: one-round Player smoke per flag value (K4)
STABILITY_SEEDS, STABILITY_ROUNDS = (61, 26), 12   # A5 DEC-091
STABILITY_TIMEOUT = 7200                  # 12 rounds × up to 420 s does not fit wp078_bot's 3600 s default
REPORT_RUNS = {"topdown": 5, "tp": 2, "fp": 2}      # A5 real-bot report coverage (≥5 / ≥2 / ≥2)
REPORT_ROUNDS = 5
GUARD_FIELDS = ("snapshotExit", "restoreExit", "verified", "outOfScopeChanged")
RECORD_SCHEMA = "dd-wp078-accept-record-v1"
STOP = guard_receipt.STOP                 # 7
NOT_RESTORED = guard_receipt.NOT_RESTORED # 65 — G4 D2: STOP, C-00 resolves; never in WAIT_CODES
WAIT_CODES = (6, 30, 75)
RUN_NAME = re.compile(r"^(gameplay|route)-(topdown|tp|fp)$|^stability-\d+$|^report-(topdown|tp|fp)-\d+$|^flag-[a-z0-9]+-[01]$")


class Stop(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code, self.message = code, message


class Terminated(Exception):
    pass


def utc():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def ref(evidence, path):
    """{path relative to the evidence root, artifactSha256 of the actual bytes} — None when the file is not there."""
    path = Path(path)
    if not path.is_file():
        return None
    return {"path": path.resolve().relative_to(Path(evidence).resolve()).as_posix(), "artifactSha256": sha256(path)}


def hash_map(base, paths):
    """artifactSha256 map relative to a record's own directory (what wp078_report.bind resolves)."""
    result = {}
    for path in paths:
        path = Path(path)
        if path.is_file():
            result[os.path.relpath(path.resolve(), Path(base).resolve())] = sha256(path)
    return result


def map_verifies(base, mapping):
    try:
        return isinstance(mapping, dict) and all((Path(base) / rel).is_file() and sha256(Path(base) / rel) == digest
                                                 for rel, digest in mapping.items())
    except OSError:
        return False


def guard_ok(g):
    """The report's rule: snapshot/restore exit 0, verified 1 (or true), no out-of-scope change (0 or false)."""
    if not isinstance(g, dict):
        return False
    exits = all(type(g.get(k)) is int and g[k] == 0 for k in ("snapshotExit", "restoreExit"))
    verified = g.get("verified") is True or (type(g.get("verified")) is int and g["verified"] == 1)
    oos = g.get("outOfScopeChanged") is False or (type(g.get("outOfScopeChanged")) is int and g["outOfScopeChanged"] == 0)
    return exits and verified and oos


def aggregate_guard(receipts):
    """One step guard from its runs' receipts: verified only when every receipt is; a failing receipt is copied as is;
    a missing receipt leaves the step without a guard (BLOCKED in the report, never assumed)."""
    receipts = list(receipts)
    if not receipts or any(not isinstance(r, dict) for r in receipts):
        return None
    bad = [r for r in receipts if not guard_ok(r)]
    if bad:
        return dict({k: bad[0].get(k) for k in GUARD_FIELDS}, receipts=len(receipts))
    return {"snapshotExit": 0, "restoreExit": 0, "verified": 1, "outOfScopeChanged": 0, "receipts": len(receipts)}


def file_sha256(path):
    """sha256 of a build's binary; a dry run may plan against a build that is not there yet."""
    return sha256(path) if Path(path).is_file() else "<sha256 of " + Path(path).name + ">"


def binary_of(build_dir):
    path = wp078_perf.resolve_binary(build_dir)
    return path if path.is_file() else None


def r1_build_proof(r1_evidence, r1_commit):
    """WP-091 P2: the R1 FINAL's release-probe build proves itself from its own evidence before any window runs (KADRO
    test-engineer SHOULD-4): its unit record is complete and built at r1_commit, every file the record hash-bound is
    unchanged (binary, build-report, report, library provenance, build log), build-report.json is a release-probe stamped
    at r1_commit, and the WP-079H app manifest re-hashes the whole .app. ({binary, sha256, appManifestSha256}, []) or
    (None, [the broken conditions])."""
    build, units = Path(r1_evidence) / "build-probe", Path(r1_evidence) / "stages" / "units"
    record = load_json(units / "build-probe.json")
    if not isinstance(record, dict):
        return None, ["no unit record " + str(units / "build-probe.json")]
    problems = []
    binary = binary_of(build)
    if record.get("status") != "complete":
        problems.append("unit record status " + repr(record.get("status")))
    if record.get("executionCommit") != r1_commit:
        problems.append(f"built at {record.get('executionCommit')}, not {r1_commit}")
    if binary is None:
        problems.append("no binary in " + str(build))
    elif record.get("binarySha256") != sha256(binary):
        problems.append("the binary is not the one the unit record holds")
    if not map_verifies(units, record.get("artifactSha256")):
        problems.append("a file the unit record hash-bound changed or is missing")
    report = load_json(build / "build-report.json") or {}
    stamp = report.get("stamp") if isinstance(report.get("stamp"), dict) else {}
    stamped = str(stamp.get("commit") or "")
    if report.get("mode") != "release-probe" or len(stamped) < 7 or not str(r1_commit).startswith(stamped):
        problems.append(f"build-report.json is not a release-probe stamped at {str(r1_commit)[:12]} "
                        f"(mode {report.get('mode')!r}, stamp {stamped!r})")
    manifest = build / "app-manifest.sha256"
    if not manifest.is_file():
        problems.append("no app-manifest.sha256 (WP-079H) to prove the whole .app")
    elif _app_manifest().verify(str(build)) != 0:
        problems.append("app-manifest.sha256 does not verify: a file of the .app changed")
    if problems:
        return None, problems
    return {"binary": binary, "sha256": record["binarySha256"], "appManifestSha256": sha256(manifest)}, []


def bot_dir_files(run_dir):
    """attempt.json and every output wp078_bot hashed into it."""
    attempt = load_json(run_dir / "attempt.json") or {}
    mapping = attempt.get("artifactSha256") if isinstance(attempt.get("artifactSha256"), dict) else {}
    return [run_dir / "attempt.json"] + [run_dir / rel for rel in mapping]


def bot_done(run_dir, kind):
    """A finished run with a restored guard (reused by --resume): complete, or a real-bot report run that ended on a
    documented balance death (Player exit 1/3) with every artifact present and no timeout."""
    attempt = load_json(run_dir / "attempt.json")
    if not isinstance(attempt, dict) or not isinstance(attempt.get("guard"), dict) or attempt["guard"].get("code") != 0:
        return False
    if attempt.get("status") == "complete":
        return True
    return (kind == "report" and attempt.get("status") == "failed" and attempt.get("exitCode") in (1, 3)
            and attempt.get("timedOut") is False and attempt.get("missingArtifacts") == [])


class HostWatch:
    """KADRO game-test-engineer SHOULD-6, C-00 06.10 (8626d147): what "quiet" means for an r2q session, measured, never
    held. r2q cannot take every Unity slot (its own Unity needs one); its PlayMode guard snapshot already holds the player
    slot, so a guarded foreign Player waits for it. Quiet = no foreign Unity, Player, Blender or ffmpeg (not this chain's
    own descendants), the criterion r2_s3 uses; before the session it is waited for at most HOST_WAIT_MAX, during it the
    host is sampled every HOST_INTERVAL s. load1 is recorded, never judged (C-00: the load average does not show CPU here).
    A host that was not quiet is recorded (dirty), never a reason to skip — S1/S2's pile-up gate and S4's dropped-sample
    assert decide the value; the report says KİRLİ."""

    def __init__(self, wait_max=HOST_WAIT_MAX, interval=HOST_INTERVAL, sleep=time.sleep):
        self.wait_max, self.interval, self.sleep = wait_max, interval, sleep
        self._samples, self._stop, self._thread = [], None, None

    def state(self):
        """{load1, foreignUnity, foreignPlayer, foreignBlender, foreignFfmpeg} now; the chain's own descendants are not
        foreign."""
        rows = []
        ps = subprocess.run(["ps", "-Ao", "pid=,ppid=,comm="], capture_output=True, text=True)
        for line in ps.stdout.splitlines():
            parts = line.split(None, 2)
            if len(parts) == 3 and parts[0].isdigit() and parts[1].isdigit():
                rows.append((int(parts[0]), int(parts[1]), parts[2].strip()))
        # gt KP-20: a ps that failed or listed nothing would count four zeros - quiet. It is unreadable instead (sample()
        # makes it an error sample, never quiet); a listing without this process is not the process table either.
        listed = os.getpid() in {pid for pid, _, _ in rows}
        if ps.returncode != 0 or not listed:
            raise OSError(f"ps exit {ps.returncode}, {len(rows)} rows, this process {'listed' if listed else 'not listed'}")
        ours, grew = {os.getpid()}, True
        while grew:
            grew = False
            for pid, ppid, _ in rows:
                if ppid in ours and pid not in ours:
                    ours.add(pid)
                    grew = True
        foreign = [comm for pid, _, comm in rows if pid not in ours]
        try:
            load = float(subprocess.run(["sysctl", "-n", "vm.loadavg"], capture_output=True, text=True).stdout.split()[1])
        except (OSError, ValueError, IndexError):
            load = None   # reported only (C-00 06.10): an unreadable load average never makes the host dirty (gt Ek 4)
        return {"load1": load, "foreignUnity": sum(c.endswith("/MacOS/Unity") for c in foreign),
                "foreignPlayer": sum(c.endswith(wp078_perf.PLAYER_EXE) for c in foreign),
                "foreignBlender": sum(c.endswith("/MacOS/Blender") for c in foreign),
                "foreignFfmpeg": sum(Path(c).name == "ffmpeg" for c in foreign)}

    def sample(self):
        """state(), or — when ps / sysctl cannot be read — a sample that says so: never quiet, never a crash (gt KP-11)."""
        try:
            return self.state()
        except Exception as error:   # noqa: BLE001 — any failure to read the host is recorded, not raised
            return dict({k: None for k in HOST_HEAVY}, load1=None, error=f"{type(error).__name__}: {error}")

    @staticmethod
    def quiet(s):
        # every heavy count read and zero; an unreadable sample has them None, so it is never quiet
        return all(s.get(k) == 0 and not isinstance(s.get(k), bool) for k in HOST_HEAVY)

    def wait_quiet(self):
        """(start sample, quiet, waited seconds)."""
        waited = 0.0
        while True:
            s = self.sample()
            if self.quiet(s) or waited >= self.wait_max:
                return s, self.quiet(s), waited
            self.sleep(15.0)
            waited += 15.0

    def start(self):
        import threading
        self._samples, self._stop = [], threading.Event()

        def run():
            t0 = time.monotonic()
            while not self._stop.is_set():
                self._samples.append(dict(self.sample(), t=round(time.monotonic() - t0, 1)))
                self._stop.wait(self.interval)
        self._thread = threading.Thread(target=run, daemon=True)
        self._thread.start()

    def stop(self):
        if self._thread is not None:
            self._stop.set()
            self._thread.join()
            self._thread = None
        return list(self._samples)


def heavy_text(s):
    """"foreign Unity 1, Player 0, Blender 0, ffmpeg 0" — or the reason the host could not be read."""
    return s.get("error") or ", ".join(f"{'foreign ' if i == 0 else ''}{k[7:] if k != 'foreignFfmpeg' else 'ffmpeg'} {s.get(k)}"
                                       for i, k in enumerate(HOST_HEAVY))


def host_record(step, start, quiet, waited, samples):
    """<E>/r2/<step>/host.json — dirty when the session did not start quiet, a foreign Unity / Player / Blender / ffmpeg
    ran during it, or the host could not be read (gt KP-11: an unknown host is never called quiet). load1 is reported."""
    def most(key, rows, default):
        values = [s[key] for s in rows if isinstance(s.get(key), (int, float)) and not isinstance(s.get(key), bool)]
        return max(values) if values else default
    why = []
    if not quiet:
        why.append(f"start not quiet after {waited:.0f} s: {heavy_text(start)}")
    peaks = {k: most(k, samples, 0) for k in HOST_HEAVY}
    if any(peaks.values()):
        why.append("a foreign heavy process ran during the session (max " + heavy_text(peaks) + ")")
    unreadable = sum(1 for s in samples if s.get("error"))
    if unreadable:
        why.append(f"host unreadable in {unreadable} of {len(samples)} samples")
    if not samples:
        why.append("no host sample was taken during the session")   # gt KP-8: unwatched is not quiet
    return {"schema": "dd-r2-host-v1", "step": step["id"], "gates": step["gates"],
            "criterion": "no foreign Unity / Player / Blender / ffmpeg (C-00 06.10, 8626d147); load1 reported only",
            "waitedSeconds": waited, "quietAtStart": quiet, "start": start, "samples": len(samples),
            "load1Max": most("load1", samples, start.get("load1")),
            **{k + "Max": v for k, v in peaks.items()},
            "unreadableSamples": unreadable, "dirty": bool(why), "dirtyWhy": why}


class EditorGuard:
    """guard_receipt: snapshot → run → (finally) restore + verdict. Scope "editor" by default; a PlayMode unit passes
    "player" so the snapshot holds the player slot before the Unity slot (C-00 30.09 lock order player → unity)."""
    scope = "editor"

    def path(self):
        return guard_receipt.guard_path()

    def snapshot(self, ug, scope=None):
        ug.parent.mkdir(parents=True, exist_ok=True)
        result = guard_receipt.run_guard("snapshot", ug, scope or self.scope)
        (ug.parent / (ug.name + ".snapshot.txt")).write_text(result.stdout + result.stderr, encoding="utf-8")
        return result.returncode

    def close(self, ug, scope=None):
        return guard_receipt.close(ug, scope or self.scope)


B9_POSE_KEYS = {"name", "start", "tiers", "modes"}


def load_b9_poses(path):
    """{"source": …, "poses": [{"name": "sw-corner", "start": "-48,-48,45", "tiers": "yuksek", "modes": "topdown,tp"}]}
    — names are unique folder names, start is x,z,yaw (three numbers), tiers/modes are wp078_perf --tiers/--modes lists."""
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    poses = data.get("poses") if isinstance(data, dict) else None
    if not isinstance(poses, list) or not poses:
        raise ValueError("needs a nonempty 'poses' list")
    names = set()
    for pose in poses:
        if not isinstance(pose, dict) or set(pose) != B9_POSE_KEYS or not all(isinstance(pose[k], str) for k in B9_POSE_KEYS):
            raise ValueError("each pose is exactly {name, start, tiers, modes} strings: " + json.dumps(pose))
        if not re.fullmatch(B9_NAME, pose["name"]) or pose["name"] in names:
            raise ValueError("pose name must be a unique lower-case folder name: " + pose["name"])
        if not re.fullmatch(r"-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?", pose["start"]):
            raise ValueError("start must be x,z,yaw: " + pose["start"])
        names.add(pose["name"])
    return poses


class Chain:
    def __init__(self, final, evidence, stages, dry_run=False, resume=False, editmode_filter="", playmode_filter="",
                 wait_tries=3, wait_seconds=120.0, git=None, runner=None, guard=None, out=print, python=sys.executable,
                 b9_poses=None, round_name="R1", r1_evidence=None, r1_commit=None, b9_poses_path=None, lint=None, host=None):
        self.final, self.evidence = final, Path(evidence).resolve()
        self.stages = [s for s in STAGE_ORDER if s in stages]
        self.dry_run, self.resume = dry_run, resume
        self.filters = {"editmode": editmode_filter, "playmode": playmode_filter}
        self.wait_tries, self.wait_seconds = max(1, wait_tries), wait_seconds
        self.git = git or self._git
        self.runner = runner or self._spawn
        self.guard = guard or EditorGuard()
        self.out, self.py = out, python
        self.round_name = round_name   # WP-091: "R2" adds the r2-gates.json step to the manifest
        self.r1_evidence, self.r1_commit = (Path(r1_evidence).resolve() if r1_evidence else None), r1_commit   # P5 baseline
        self.b9_poses = list(b9_poses or [])   # C-00 01.10: S-05 B9 pose windows, after A4 in the same quiet-window run
        self._r1_proof = None                  # WP-091 P2: the R1 build's proof, taken once before any window (False = failed)
        self.b9_poses_path = b9_poses_path     # the pose list file, for r2_z3.py's P3
        self.lint = lint or (lambda repo: r2_gates_lint.run(repo)[0])   # seam: [r2_gates_lint.Finding]
        self.host = host or HostWatch()   # seam: the r2q host check (SHOULD-6)
        self.child, self.terminated = None, False
        self.planned = []          # (stage, argv) — the dry-run command list
        self.verdicts = {}
        self.report_exit = None
        self.stage = None

    # ---- plumbing -------------------------------------------------------------------------------------------------
    @staticmethod
    def _git(*args):
        return subprocess.run(["git", "-C", str(REPO), *args], capture_output=True, text=True, check=True).stdout.strip()

    def log(self, text):
        line = f"{utc()} {text}"
        self.out(line)
        if not self.dry_run:
            self.evidence.mkdir(parents=True, exist_ok=True)
            with (self.evidence / "accept.log").open("a", encoding="utf-8") as handle:
                handle.write(line + "\n")

    def on_sigterm(self, *_):
        """Forward SIGTERM to the running child only (its own finally restores its guard) and stop after it exits."""
        self.terminated = True
        child = self.child
        if child is not None and child.poll() is None:
            child.send_signal(signal.SIGTERM)

    def check_terminated(self):
        if self.terminated:
            raise Terminated()

    def _spawn(self, argv, log_path):
        self.check_terminated()
        log_path.parent.mkdir(parents=True, exist_ok=True)
        with log_path.open("ab") as handle:
            self.child = subprocess.Popen([str(a) for a in argv], stdout=handle, stderr=subprocess.STDOUT, cwd=str(REPO))
            try:
                code = self.child.wait()
            finally:
                self.child = None
        self.check_terminated()
        return code

    def sleep(self, seconds):
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            self.check_terminated()
            time.sleep(min(1.0, max(0.0, end - time.monotonic())))
        self.check_terminated()

    def execute(self, name, argv, log_path=None):
        argv = [str(a) for a in argv]
        if self.dry_run:
            self.planned.append((self.stage, argv))
            self.out(f"DRY {self.stage}: {shlex.join(argv)}")
            return 0
        self.log(f"RUN {self.stage} {name}: {shlex.join(argv)}")
        code = self.runner(argv, Path(log_path) if log_path else self.evidence / "logs" / (name + ".out"))
        self.log(f"EXIT {self.stage} {name}: {code}")
        return code

    def open_markers(self, under=None):
        root = under or self.evidence
        return guard_receipt.unresolved(root.rglob(guard_receipt.MARKER)) if root.is_dir() else []

    def check_markers(self, name):
        """G4 D2 review MUST-1: an unrestored guard window anywhere under <E> stops the chain before the next snapshot
        (which would take the unrestored data as its baseline)."""
        if self.dry_run:
            return
        found = self.open_markers()
        if found:
            raise Stop(NOT_RESTORED, f"{name}: unrestored guard window(s) {found[:3]} — nothing started; C-00 resolves")

    def move_aside(self, path, tag):
        found = self.open_markers(path)
        if found:   # the marker must stay where the unrestored snapshot is
            raise Stop(NOT_RESTORED, f"not moving {path.name} aside: unrestored guard window(s) {found[:3]} — C-00 resolves")
        k = 1
        while path.with_name(f"{path.name}.{tag}-{k}").exists():
            k += 1
        target = path.with_name(f"{path.name}.{tag}-{k}")
        path.rename(target)
        self.log(f"MOVED {path.relative_to(self.evidence)} → {target.name} (kept, not deleted)")

    def verify_tree(self):
        try:
            return self.git("rev-parse", "HEAD") == self.final and self.git("status", "--porcelain") == ""
        except (subprocess.CalledProcessError, OSError):
            return False

    def stage_record(self, stage):
        return self.evidence / "stages" / f"{stage}.json"

    def unit_record(self, name):
        return self.evidence / "stages" / "units" / f"{name}.json"

    # ---- the chain --------------------------------------------------------------------------------------------------
    def run(self):
        if not self.verify_tree():
            self.out(f"REFUSED: repo HEAD is not FINAL {self.final} or the tree is dirty — no evidence touched")
            return 2
        if self.evidence.resolve().is_relative_to(REPO.resolve()):
            self.out(f"REFUSED: evidence root {self.evidence} is inside the repo (it would dirty the tree)")
            return 64
        if not self.dry_run and not self.resume:
            existing = [str(self.evidence / name) for stage in self.stages for name in STAGE_OUTPUTS[stage]
                        if (self.evidence / name).exists()]
            existing += [str(self.stage_record(s)) for s in self.stages if self.stage_record(s).exists()]
            if existing:
                self.out("REFUSED: evidence already exists (use --resume or a new --evidence): " + ", ".join(existing[:5]))
                return 64
        if self.round_name == "R2":
            refused = self.lint_contract()
            if refused:
                return refused
        self.log(f"START final={self.final} evidence={self.evidence} stages={','.join(self.stages)}"
                 f"{' dry-run' if self.dry_run else ''}{' resume' if self.resume else ''}")
        try:
            for stage in self.stages:
                self.stage = stage
                self.check_terminated()
                if not self.dry_run and not self.verify_tree():
                    raise Stop(2, f"{stage}: repo left FINAL or the tree became dirty")
                if stage in ("a4", "r2") and not self.dry_run:   # resumed or not: the refusal only sees this run's stages (gt KP-18)
                    self.check_baseline(stage)
                if stage != "a8" and not self.dry_run and self.resume and self.stage_passed(stage):
                    self.log(f"SKIP {stage}: verified PASS for FINAL {self.final}")
                    self.verdicts[stage] = "PASS"
                    continue
                getattr(self, "stage_" + stage)()
        except Stop as stop:
            line = f"STOP {self.stage}: {stop.message} (exit {stop.code}) — report to C-00"
            self.log(line)
            print(line, file=sys.stderr)
            return stop.code
        except Terminated:
            line = f"STOP {self.stage}: SIGTERM — current child terminated and waited for (exit 143) — report to C-00"
            self.log(line)
            print(line, file=sys.stderr)
            return 143
        if self.dry_run:
            return 0
        if self.report_exit is not None:
            code = self.report_exit
        else:
            code = 0 if all(self.verdicts.get(s) == "PASS" for s in self.stages) else 1
        self.log(f"DONE exit={code} verdicts={json.dumps(self.verdicts, sort_keys=True)}")
        return code

    def lint_contract(self):
        """WP-091 (OTURUM-ACILIS S-10, C-00 06.10): an R2 FINAL starts only when tools/qa/r2_gates_lint.py finds no ERROR at
        this tree — an ERROR means the judge would read a key nobody writes, a writer the contract ignores, or a session
        that runs nothing, hours later. The WARNs (gates still BLOCKED by name) go to the log and to <E>/r2-gates-lint.json."""
        try:
            findings = self.lint(REPO)
        except (OSError, ValueError) as error:
            self.out(f"REFUSED: r2_gates_lint could not read the contract: {error} — nothing started")
            return 3
        errors = [f for f in findings if f.level == "ERROR"]
        warns = [f for f in findings if f.level == "WARN"]
        if errors:
            self.out(f"REFUSED: r2_gates_lint ERROR {len(errors)} at FINAL {self.final} — nothing started: "
                     + "; ".join(f"{f.code} {f.gate}" for f in errors))
            return 3
        if not self.dry_run:
            write_json(self.evidence / "r2-gates-lint.json", {"schema": "dd-r2-gates-lint-v1", "commit": self.final,
                                                               "counts": {"ERROR": 0, "WARN": len(warns)},
                                                               "findings": [f.as_dict() for f in findings]})
        self.log(f"R2 lint: ERROR 0 · WARN {len(warns)}" + (": " + ", ".join(f"{f.code} {f.gate}" for f in warns) if warns else ""))
        return None

    def stage_passed(self, stage):
        path = self.stage_record(stage)
        record = load_json(path)
        return (isinstance(record, dict) and record.get("executionCommit") == self.final and record.get("verdict") == "PASS"
                and map_verifies(path.parent, record.get("artifactSha256"))
                and map_verifies(path.parent, record.get("inputs", {})))

    def finish(self, stage, results, extra_files=(), guarded=True, inputs=(), verdict=None, extra=None):
        """Write <E>/stages/<stage>.json. exitCode = the first non-zero gating exit; verdict PASS only when every gating
        runner exited 0 (the domain verdict is the report's)."""
        if self.dry_run:
            return None
        gating = [r for r in results if r.get("gate", True)]
        exit_code = next((r["exit"] for r in gating if r["exit"] != 0), 0)
        if verdict is None:
            verdict = "PASS" if gating and all(r["exit"] == 0 for r in gating) else "FAIL"
        path = self.stage_record(stage)
        files = [f for r in results for f in r.get("files", [])] + list(extra_files)
        record = {"schema": RECORD_SCHEMA, "stage": stage, "executionCommit": self.final, "exitCode": exit_code,
                  "status": "complete" if verdict == "PASS" else "recorded" if verdict == "RECORDED" else "failed",
                  "verdict": verdict, "completedUtc": utc(),
                  "units": [{k: (str(v) if isinstance(v, Path) else v) for k, v in r.items() if k != "files"} for r in results],
                  "artifactSha256": hash_map(path.parent, files), "inputs": hash_map(path.parent, inputs)}
        if guarded:
            # every run that held a guard (a result carrying the "guard" key); recorded checks (soak_gate,
            # wp078_perf_report) run no Player and hold no guard
            guard = aggregate_guard(r.get("guard") for r in results if "guard" in r)
            if guard is not None:
                record["guard"] = guard
        record.update(extra or {})
        write_json(path, record)
        self.verdicts[stage] = verdict
        self.log(f"{verdict} {stage} exitCode={exit_code} record={path.relative_to(self.evidence)}")
        return record

    def dev_binary(self, role="dev"):
        record = load_json(self.unit_record("build-" + role))
        binary = binary_of(self.evidence / ("build-" + role))
        if self.dry_run:
            return binary or self.evidence / ("build-" + role)
        if not (isinstance(record, dict) and record.get("status") == "complete" and binary is not None
                and record.get("binarySha256") == sha256(binary)):
            return None
        return binary

    def r1_probe(self):
        """WP-091 P2 (C-00 05.10): in an R2 round, the R1 FINAL's own release-probe build (<--r1-evidence>/build-probe),
        proven by r1_build_proof once, before any window — a dry run proves it too, since the R1 evidence exists before the
        chain (KADRO test-engineer SHOULD-5) — runs alternately with FINAL's in A4. None (A4 runs FINAL alone; P2 BLOCKED in
        the report, the reasons in the log) in an R1 round, without --r1-evidence, or when the build does not prove itself."""
        if self.round_name != "R2":
            return None
        if self.r1_evidence is None:
            self.log("P2: no --r1-evidence/--r1-commit — A4 runs FINAL alone, r2-P2.json not written, P2 BLOCKED in the report")
            return None
        if self._r1_proof is None:
            proof, problems = r1_build_proof(self.r1_evidence, self.r1_commit)
            if proof is None:
                self.log(f"P2: {self.r1_evidence / 'build-probe'} is not the R1 FINAL's proven release-probe build — "
                         + "; ".join(problems) + " — A4 runs FINAL alone, P2 BLOCKED in the report")
            self._r1_proof = proof or False
        return self._r1_proof or None

    def check_baseline(self, stage):
        """KADRO test-engineer SHOULD-2 + gt 06.10 N-11 / KP-18: an R2 run keeps the R1 baseline its records were made with —
        P2 (the a4 record) and P5 (the r2 record, when it wrote r2-P5.json) never rest on two baselines in one report, never
        a skipped A4 that silently leaves P2 out, never a baseline root the manifest would not declare. Checked at the start
        of a4 and of r2, before that stage's sessions, with or without --resume. The a4 record's baseline is the one the run
        asked for (baselineRequested), so an R1 build that did not prove itself — no P2, no baselineCommit — does not read as
        "no baseline" to a run that gives the same one (gt KP-4); a record older than that key falls back to baselineCommit."""
        want = self.r1_commit if self.round_name == "R2" and self.r1_evidence is not None else None
        for other, key, gate in (("a4", "baselineRequested", "P2"), ("r2", "p5BaselineCommit", "P5")):
            if other == stage == "r2":
                continue   # this run computes P5 again (the earlier r2-P5.json is retired first)
            record = load_json(self.stage_record(other))
            if not (isinstance(record, dict) and record.get("executionCommit") == self.final):
                continue
            if other == "r2" and key not in record:
                continue   # that r2 run wrote no r2-P5.json: nothing of it is bound
            had = record.get(key) if key in record else record.get("baselineCommit")
            if had != want:
                raise Stop(64, f"{other} was recorded with R1 baseline {had}, this run gives {want}: {gate} would rest on another "
                               "baseline than the rest of the report — resume with the same --r1-evidence/--r1-commit, or a new --evidence")
        a4 = load_json(self.stage_record("a4"))
        a4_here = isinstance(a4, dict) and a4.get("executionCommit") == self.final   # another FINAL's record is none (gt KP-3)
        if want is None and (self.evidence / R1_ROOT).exists() and not a4_here:
            raise Stop(64, f"{R1_ROOT} exists but this run gives no --r1-evidence/--r1-commit: resume with them, or a new --evidence")

    # ---- Editor-guarded unit (builds, suites) -----------------------------------------------------------------------
    def snapshot_with_wait(self, name, ug, scope="editor"):
        for attempt in range(1, self.wait_tries + 1):
            self.check_terminated()
            self.check_markers(name)
            if ug.exists():
                self.move_aside(ug, "wait")
            raw = self.guard.snapshot(ug, scope)
            code = guard_receipt.snapshot_code(raw)
            if code == 0:
                return
            if code not in (30, 75):
                raise Stop(STOP, f"{name}: Editor guard snapshot exit {raw} → STOP; nothing started")
            if attempt == self.wait_tries:
                raise Stop(code, f"{name}: Editor guard snapshot exit {raw} persisted after {attempt} tries; nothing started")
            self.log(f"WAIT {name}: Editor guard snapshot exit {raw} (try {attempt}/{self.wait_tries}); retry in {self.wait_seconds:.0f} s")
            self.sleep(self.wait_seconds)

    def editor_unit(self, name, commands, out_dir, outputs, extra=lambda: {}, scope="editor"):
        """snapshot (scope editor; PlayMode: player) → commands (each only if the previous exited 0) → finally restore + verdict."""
        ug = self.evidence / "ug" / name
        record_path = self.unit_record(name)
        if self.dry_run:
            guard = self.guard.path()
            self.out(f"DRY {self.stage}: {shlex.join([guard, 'snapshot', str(ug), '--scope', scope])}")
            self.planned.append((self.stage, [guard, "snapshot", str(ug), "--scope", scope]))
            for argv in commands:
                self.execute(name, argv)
            self.out(f"DRY {self.stage}: {shlex.join([guard, 'restore', str(ug), '--scope', scope])} (finally)")
            self.planned.append((self.stage, [guard, "restore", str(ug), "--scope", scope]))
            return None
        if self.resume:
            record = self.reusable_unit(record_path)
            if record is not None:
                self.log(f"SKIP {name}: verified complete unit record")
                return self.unit_result(name, record_path, record)
            if out_dir.exists():
                self.move_aside(out_dir, "prev")
        self.snapshot_with_wait(name, ug, scope)
        exits, receipt = [], None
        try:
            for index, argv in enumerate(commands):
                code = self.execute(name if index == 0 else f"{name}-post{index}", argv)
                exits.append({"argv": [str(a) for a in argv], "exit": code})
                if code != 0:
                    break
        finally:
            receipt = self.guard.close(ug, scope)
            self.log(f"GUARD {name}: restore exit {receipt.get('restoreExit')} verdict {receipt.get('why')} code {receipt.get('code')}")
        guard = dict({k: receipt.get(k) for k in ("restoreExit", "verified", "touched", "outOfScopeChanged")},
                     snapshotExit=0, code=receipt.get("code"), why=receipt.get("why"))
        exit_code = next((e["exit"] for e in exits if e["exit"] != 0), 0)
        ok = exit_code == 0 and receipt.get("code") == 0 and len(exits) == len(commands)
        record = {"schema": RECORD_SCHEMA, "unit": name, "executionCommit": self.final, "exitCode": exit_code,
                  "status": "complete" if ok else "failed", "guard": guard, "commands": exits, "completedUtc": utc(),
                  "artifactSha256": hash_map(record_path.parent, outputs())}
        record.update(extra())
        write_json(record_path, record)
        if receipt.get("code") != 0:
            raise Stop(STOP, f"{name}: Editor guard restore/verdict failed ({receipt.get('why')}); user data NOT confirmed restored")
        return self.unit_result(name, record_path, record)

    def reusable_unit(self, record_path):
        """The unit record a resumed run reuses (verified complete for this FINAL, guard restored), or None."""
        record = load_json(record_path) if self.resume else None
        if (isinstance(record, dict) and record.get("executionCommit") == self.final and record.get("status") == "complete"
                and guard_ok(record.get("guard")) and map_verifies(record_path.parent, record.get("artifactSha256"))):
            return record
        return None

    def unit_result(self, name, record_path, record):
        files = [record_path] + [record_path.parent / rel for rel in record.get("artifactSha256", {})]
        return {"name": name, "exit": record.get("exitCode"), "guard": record.get("guard"), "record": str(record_path),
                "files": files}

    # ---- wp078_bot unit ----------------------------------------------------------------------------------------------
    def bot_argv(self, out, kind, mode, seed, rounds, extra_args=(), quiet=False, timeout=None):
        """wp078_bot on the dev build (K1: QaRunBot exists only in DEVELOPMENT_BUILD)."""
        argv = [self.py, HERE / "wp078_bot.py", "--bin", self.evidence / "build-dev", "--out", out, "--commit", self.final, "--mode", mode, "--kind", kind, "--seed", seed, "--rounds", rounds,
                "--tier", TIER]
        if quiet:
            argv.append("--quiet")
        if timeout:
            argv += ["--timeout", timeout]
        argv += [f"--extra-arg={arg}" for arg in extra_args]
        return [str(a) for a in argv]

    def bot_unit(self, name, out, argv, kind, gate=True):
        if self.dry_run:
            self.execute(name, argv)
            return None
        if out.exists():
            if self.resume and bot_done(out, kind):
                self.log(f"SKIP {name}: finished run with restored guard")
                return self.bot_result(name, out, 0 if load_json(out / "attempt.json").get("status") == "complete" else 1, gate)
            self.move_aside(out, "prev")
        for attempt in range(1, self.wait_tries + 1):
            self.check_markers(name)
            code = self.execute(name, argv)
            if code == STOP:
                raise Stop(STOP, f"{name}: wp078_bot guard STOP (snapshot error, restore error or verdict mismatch)")
            if code == NOT_RESTORED:
                raise Stop(NOT_RESTORED, f"{name}: wp078_bot NOT restored (a Player outlived the run) — C-00 resolves")
            if code == 143:
                raise Terminated()
            if code not in WAIT_CODES:
                return self.bot_result(name, out, code, gate)
            if attempt == self.wait_tries:
                raise Stop(code, f"{name}: wp078_bot exit {code} (Player up / guard wait / host not clean) persisted after {attempt} tries")
            self.log(f"WAIT {name}: wp078_bot exit {code} (try {attempt}/{self.wait_tries}); retry in {self.wait_seconds:.0f} s")
            if out.exists():
                self.move_aside(out, "wait")
            self.sleep(self.wait_seconds)
        raise AssertionError("unreachable")

    def bot_result(self, name, out, code, gate):
        attempt = load_json(out / "attempt.json") or {}
        return {"name": name, "exit": code, "gate": gate, "dir": str(out), "guard": attempt.get("guard"),
                "playerExit": attempt.get("exitCode"), "status": attempt.get("status"), "files": bot_dir_files(out)}

    # ---- stages -----------------------------------------------------------------------------------------------------
    def stage_a6(self):
        results = []
        for role, mode in BUILDS.items():
            out = self.evidence / f"build-{role}"
            commands = [[REPO / "tools" / "build.sh", mode, "--out", out],
                        [self.py, HERE / "library_provenance.py", REPO, out / "library-provenance.json"]]

            def outputs(out=out):
                return [binary_of(out) or out / "missing-binary", out / "build-report.json", out / "report.json",
                        out / "library-provenance.json", out / "build.log"]

            def extra(out=out, role=role):
                fields, binary = {"buildRole": role, "buildMode": BUILDS[role]}, binary_of(out)
                if binary is not None:
                    fields["binarySha256"] = sha256(binary)
                compression = (load_json(out / "report.json") or {}).get("compression")
                if isinstance(compression, str) and compression:
                    fields["compression"] = compression.upper()   # native build.sh report value (lz4hc → LZ4HC)
                return fields
            result = self.editor_unit(f"build-{role}", commands, out, outputs, extra)
            if result:
                results.append(result)
        self.finish("a6", results)

    def stage_a0(self):
        if not self.dry_run and (load_json(self.stage_record("a6")) or {}).get("executionCommit") != self.final:
            self.log("BLOCKED a0: no same-FINAL build ran (A0 is the post-build Library provenance) — not run")
            return
        out = self.evidence / "a0" / "library-provenance.json"
        if not self.dry_run:
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                self.move_aside(out, "prev")
        code = self.execute("a0-library-provenance", [self.py, HERE / "library_provenance.py", REPO, out])
        self.finish("a0", [{"name": "library-provenance", "exit": code, "files": [out]}], guarded=False)

    def stage_a1(self):
        results = []
        category = ["--category", CI_CATEGORY] if testsh_has_category() else []
        if not category:
            self.log("WARNING a1: tools/test.sh has no --category at this SHA — Diagnostics tests are NOT excluded (as main-ci)")
        # R2 (C-00 01.10): A1 PlayMode = main-ci parity — the core filter in one session, then every `alone` entry in its
        # own session (FINAL' dab326ab ran all 567 PlayMode tests in one session: 18 contamination reds + 26 non-core).
        # An explicit --playmode-filter (a targeted rerun) replaces the core and runs no alone steps.
        core, alone, _ = ci_plan()
        playmode_filter = self.filters["playmode"] or core
        for mode in ("editmode", "playmode"):
            out = self.evidence / "a1" / mode
            argv = [REPO / "tools" / "test.sh", mode, "--out", out]
            if mode == "playmode":
                # FINAL 23093940 (01.10): test.sh's PlayMode watchdog defaults to 45 min; the full suite (248 cases, 25 min
                # in main-ci 5cf17f9e) was killed mid-suite at 45 min on the FINAL host. The watchdog only catches a hang.
                argv += ["--timeout-min", str(PLAYMODE_TIMEOUT_MIN), "--filter", playmode_filter]
            elif self.filters[mode]:
                argv += ["--filter", self.filters[mode]]
            if not self.filters[mode]:
                argv += category   # the core / whole suite only: a targeted rerun runs exactly what it names
            result = self.editor_unit(f"a1-{mode}", [argv], out,
                                      lambda out=out, mode=mode: [out / f"{mode}.xml", out / f"{mode}.log", out / "durations.json"],
                                      lambda mode=mode: {"testMode": mode, "filter": self.filters[mode] or None},
                                      scope="player" if mode == "playmode" else "editor")
            if result:
                results.append(result)
        if not self.filters["playmode"]:
            for step_id, step_filter in alone:
                result = self.isolated_unit("a1", step_id, step_filter)
                if result:
                    results.append(result)
        self.finish("a1", results)

    def isolated_unit(self, stage, step_id, step_filter):
        """One ci-isolated test in its own PlayMode session under the player-scope guard → <E>/<stage>/<step id>/."""
        out = self.evidence / stage / step_id
        argv = [REPO / "tools" / "test.sh", "playmode", "--out", out, "--timeout-min", str(ISOLATED_TIMEOUT_MIN),
                "--filter", step_filter]
        return self.editor_unit(f"{stage}-{step_id}", [argv], out,
                                lambda out=out: [out / "playmode.xml", out / "playmode.log", out / "durations.json"],
                                lambda: {"testMode": "playmode", "filter": step_filter, "isolated": True}, scope="player")

    def bot_stage_binary(self, stage):
        binary = self.dev_binary()
        if binary is None:
            self.log(f"BLOCKED {stage}: no complete same-FINAL dev build (build-dev) — not run")
        return binary

    def stage_a2(self):
        binary = self.bot_stage_binary("a2")
        if binary is None:
            return
        results = []
        for mode in MODES:
            out = self.evidence / "a2" / f"gameplay-{mode}"
            results.append(self.bot_unit(out.name, out, self.bot_argv(out, "gameplay", mode, GAMEPLAY_SEED, GAMEPLAY_ROUNDS), "gameplay"))
        for mode in MODES:
            out = self.evidence / "a2" / f"route-{mode}"
            results.append(self.bot_unit(out.name, out, self.bot_argv(out, "route", mode, GAMEPLAY_SEED, ROUTE_ROUNDS), "route"))
        self.finish("a2", [r for r in results if r], inputs=[binary])

    def stage_a3(self):
        binary = self.bot_stage_binary("a3")
        if binary is None:
            return
        results = []
        for flag in wp078_report.AI_FLAGS:
            for value in (0, 1):
                out = self.evidence / "a3" / f"flag-{flag.lstrip('-').lower()}-{value}"
                argv = self.bot_argv(out, "flag", "topdown", GAMEPLAY_SEED, FLAG_ROUNDS, extra_args=(flag, str(value)))
                results.append(self.bot_unit(out.name, out, argv, "flag"))
        # R2 (C-00 01.10): the ci-isolated `a3` entries (main-ci never runs them) — each in its own PlayMode session
        for step_id, step_filter in ci_plan()[2]:
            results.append(self.isolated_unit("a3", step_id, step_filter))
        if self.dry_run:
            return
        results = [r for r in results if r]
        # The A3 regression rows are the A1 suites' NUnit XML (A3_REQUIRED_TEST_IDS): the aiFlags row binds them together
        # with the flag smokes, so this record carries the A1 record and XML it consumed (a changed A1 invalidates it).
        a1 = self.stage_record("a1")
        a1_record = load_json(a1)
        consumed = ([a1] + [self.evidence / "a1" / m / f"{m}.xml" for m in ("editmode", "playmode")]
                    + [self.evidence / "a1" / step_id / "playmode.xml" for step_id, _ in ci_plan()[1]]) if a1_record else []
        verdict = None
        if not a1_record or a1_record.get("executionCommit") != self.final:
            verdict = "BLOCKED"
        elif a1_record.get("verdict") != "PASS" or any(r["exit"] != 0 for r in results):
            verdict = "FAIL"
        a1_exit = a1_record.get("exitCode") if isinstance(a1_record, dict) and type(a1_record.get("exitCode")) is int else 0
        results.append({"name": "a1-suites", "exit": a1_exit, "guard": (a1_record or {}).get("guard"), "files": consumed})
        self.finish("a3", results, verdict=verdict)

    def stage_a5(self):
        binary = self.bot_stage_binary("a5")
        if binary is None:
            return
        results, checks = [], []
        for seed in STABILITY_SEEDS:
            out = self.evidence / "a5" / f"stability-{seed}"
            argv = self.bot_argv(out, "stability", "topdown", seed, STABILITY_ROUNDS, quiet=True, timeout=STABILITY_TIMEOUT)
            result = self.bot_unit(out.name, out, argv, "stability")
            results.append(result)
            gate_argv = [self.py, HERE / "soak_gate.py", out / "bot-summary.json", "--rounds", STABILITY_ROUNDS,
                         "--seed", seed, "--stability", "--player-log", out / "player.log"]
            if self.dry_run or (out / "bot-summary.json").is_file():
                name = f"soak-gate-{seed}"
                code = self.execute(name, gate_argv, log_path=self.evidence / "a5" / f"stability-{seed}.soak-gate.txt")
                checks.append({"name": name, "exit": code, "gate": False,
                               "files": [self.evidence / "a5" / f"stability-{seed}.soak-gate.txt"]})
        for mode, count in REPORT_RUNS.items():
            for n in range(1, count + 1):
                out = self.evidence / "a5" / f"report-{mode}-{n}"
                argv = self.bot_argv(out, "report", mode, GAMEPLAY_SEED + n - 1, REPORT_ROUNDS)
                results.append(self.bot_unit(out.name, out, argv, "report", gate=False))
        self.finish("a5", [r for r in results if r] + checks, inputs=[binary])

    def stage_a4(self):
        binary = self.dev_binary("probe")
        if binary is None:
            self.log("BLOCKED a4: no complete same-FINAL release-probe build (build-probe) — not run")
            return
        root = self.evidence / "a4" / "perf"
        argv = [self.py, HERE / "wp078_perf.py", "--bin", self.evidence / "build-probe", "--out", root, "--commit", self.final]
        report_argv = [self.py, HERE / "wp078_perf_report.py", root]
        # WP-091 P2 (C-00 05.10): in an R2 round the R1 FINAL probe build alternates with FINAL's, ABBA per cell
        # in the same quiet window (its own root and commit, so machine state weighs on both alike); the report compares
        # the two and writes r2-P2.json. The A4 gate itself is judged on FINAL's windows only.
        proof = self.r1_probe()
        baseline_root, p2 = self.evidence / R1_ROOT, self.evidence / "r2" / "r2-P2.json"
        # FINAL's windows already counted before the R1 ones start (a resumed run): the pairs are not interleaved from the
        # start; recorded, and pairGapMaxS in r2-P2.json shows by how much (KADRO test-engineer SHOULD-2)
        fresh = not (root.is_dir() and any(wp078_perf_report.CELL.fullmatch(d.name) for d in root.iterdir()))
        if proof:
            argv += ["--baseline-bin", self.r1_evidence / "build-probe", "--baseline-out", baseline_root,
                     "--baseline-commit", self.r1_commit]
            # both sides' commit and build, so every window P2 uses proves it ran there (KADRO game-test-engineer MUST-1)
            self.retire(p2)   # an older r2-P2.json of this FINAL must not stand in for this evaluation (gt 06.10 N-1)
            report_argv += ["--baseline", baseline_root, "--r2-measure", p2, "--commit", self.final,
                            "--sha256", file_sha256(binary), "--baseline-commit", self.r1_commit, "--baseline-sha256", proof["sha256"]]
            if not fresh:
                self.log(f"P2: FINAL windows were in a4/perf before this run — the R1 windows are not interleaved with them from the start")
        code = self.perf_with_wait("wp078-perf", argv)
        report_code = self.execute("wp078-perf-report", report_argv)
        if self.dry_run:
            return

        def cells(base):
            return sorted(p / "attempt.json" for p in base.iterdir() if p.is_dir() and wp078_perf_report.CELL.fullmatch(p.name)) if base.is_dir() else []
        attempts = cells(root)
        # wp078_perf exits 0 only when every window's restore verdict was ok (it returns 7 / 30 / 75 otherwise), so exit 0
        # proves the four guard fields for the whole matrix; any other exit leaves the step without a guard.
        guard = {"snapshotExit": 0, "restoreExit": 0, "verified": 1, "outOfScopeChanged": 0,
                 "source": "wp078_perf exit 0 = every window restored and verified"} if code == 0 else None
        results = [{"name": "wp078-perf", "exit": code, "guard": guard, "files": [root / "attempts-ledger.jsonl"] + attempts},
                   {"name": "wp078-perf-report", "exit": report_code, "gate": False,
                    "files": [root / "wp078-perf-report.json", root / "wp078-perf-report.txt"]}]
        inputs, extra = [binary], {"buildRole": "probe", "perfRoot": "a4/perf"}
        # the baseline this run asked for, proven or not: check_baseline compares it (gt KP-4)
        extra["baselineRequested"] = self.r1_commit if self.round_name == "R2" and self.r1_evidence is not None else None
        if proof:
            results[0]["files"] += [baseline_root / "attempts-ledger.jsonl"] + cells(baseline_root)
            results[1]["files"] += [root / "wp078-perf-compare.json", p2]
            inputs.append(proof["binary"])
            # the hashes proven before the windows ran, not re-read after them (NOTE-6)
            extra.update(baselineRoot=R1_ROOT, baselineCommit=self.r1_commit, baselineBinarySha256=proof["sha256"],
                         baselineAppManifestSha256=proof["appManifestSha256"], baselineInterleavedFromStart=fresh)
            self.log(f"P2: {'r2-P2.json written' if p2.is_file() else 'r2-P2.json NOT written (report exit ' + str(report_code) + ')'}"
                     f" — R1 {self.r1_commit[:8]} alternating in {baseline_root.relative_to(self.evidence)}")
        self.finish("a4", results, inputs=inputs, extra=extra)

    def retire(self, path):
        """An earlier chain-written artifact is renamed <name>.stale-<utc> before it is computed again, so a run that
        writes nothing leaves nothing behind for the report to bind (kept, never deleted)."""
        path = Path(path)
        if self.dry_run or not path.is_file():
            return
        stamp = time.strftime('%Y%m%dT%H%M%SZ', time.gmtime())
        target, k = path.with_name(f"{path.name}.stale-{stamp}"), 1
        while target.exists():   # two retirements in the same second never overwrite each other (gt 06.10 N-11)
            target, k = path.with_name(f"{path.name}.stale-{stamp}-{k}"), k + 1
        path.rename(target)
        self.log(f"RETIRED {path.relative_to(self.evidence)} → {target.name} (kept)")

    def stage_b9(self):
        if self.b9_poses and self.dev_binary("probe") is None:
            self.log("BLOCKED b9: no complete same-FINAL release-probe build (build-probe) — not run")
            return
        self.b9_windows()

    def stage_z3(self):
        """WP-091 Z3 + P3 (C-00 06.10, BEKLEYEN §20; S-05 r2_z3.py). The fixed-pose matrix: FINAL's and the proven R1 probe
        builds alternating (ABBA) at r2_z3's pose, tier and modes, >= MIN_PAIRS windows per cell, -ddArmor all on both — the
        constants come from r2_z3.py, the tool that judges the windows, so they cannot drift apart. Then r2_z3.py writes
        r2-Z3.json (ratios + <E>/r2/z3-static.json) and r2-P3.json (the B9 windows). Recorded, never a chain gate: the R2
        report judges Z3 and shows P3; a missing tool, pose constant or R1 proof leaves them BLOCKED by name."""
        if self.round_name != "R2":
            self.log("SKIP z3: not an R2 round")
            return
        z3 = _r2_z3()
        if z3 is None:
            self.log("Z3/P3: tools/qa/r2_z3.py is not in the FINAL tree — r2-Z3.json / r2-P3.json not written, BLOCKED in the report")
            return
        results, extra = [], {"source": "tools/qa/r2_z3.py"}
        need = ("Z3_START", "Z3_TIER", "Z3_MODES", "MIN_PAIRS", "Z3_R2", "Z3_R1")
        missing = [n for n in need if not hasattr(z3, n)]
        roots_agree = not missing and (z3.Z3_R2, z3.Z3_R1) == (wp078_report.Z3_ROOT, wp078_report.Z3_R1_ROOT)
        binary, proof = self.dev_binary("probe"), self.r1_probe()
        if missing or not roots_agree:
            self.log(f"Z3: r2_z3.py lacks {missing or 'the report roots ' + str((wp078_report.Z3_ROOT, wp078_report.Z3_R1_ROOT))} — no Z3 windows run")
        elif binary is None or not proof:
            self.log("Z3: no complete FINAL probe build or no proven R1 probe build — no Z3 windows run, Z3 BLOCKED in the report")
        else:
            root, base = self.evidence / z3.Z3_R2, self.evidence / z3.Z3_R1
            argv = [self.py, HERE / "wp078_perf.py", "--bin", self.evidence / "build-probe", "--out", root, "--commit", self.final,
                    "--baseline-bin", self.r1_evidence / "build-probe", "--baseline-out", base, "--baseline-commit", self.r1_commit,
                    "--tiers", z3.Z3_TIER, "--modes", ",".join(z3.Z3_MODES), "--windows", str(z3.MIN_PAIRS),
                    "--extra-arg=-ddPilotStart", "--extra-arg=" + z3.Z3_START, "--extra-arg=-ddArmor", "--extra-arg=all"]
            code = self.perf_with_wait("z3-perf", argv)
            if not self.dry_run:
                attempts = [d / "attempt.json" for side in (root, base) if side.is_dir() for d in sorted(side.iterdir())
                            if d.is_dir() and wp078_perf_report.CELL.fullmatch(d.name)]
                guard = {"snapshotExit": 0, "restoreExit": 0, "verified": 1, "outOfScopeChanged": 0,
                         "source": "wp078_perf exit 0 = every window restored and verified"} if code == 0 else None
                results.append({"name": "z3-perf", "exit": code, "gate": False, "guard": guard,
                                "files": [root / "attempts-ledger.jsonl", base / "attempts-ledger.jsonl"] + attempts})
                extra["z3Roots"] = [{"role": "z3-pose", "path": z3.Z3_R2, "start": z3.Z3_START},
                                    {"role": "r1-baseline", "path": z3.Z3_R1, "commit": self.r1_commit, "binarySha256": proof["sha256"]}]
        tool = [self.py, HERE / "r2_z3.py", "--evidence", self.evidence, "--commit", self.final]
        if self.r1_commit:
            tool += ["--r1-commit", self.r1_commit]
        if self.b9_poses_path:
            tool += ["--b9-poses", self.b9_poses_path]
        code = self.execute("r2-z3", tool, log_path=self.evidence / "z3" / "r2-z3.txt")
        if self.dry_run:
            return
        out = self.evidence / "r2"
        results.append({"name": "r2-z3", "exit": code, "gate": False,
                        "files": [out / "r2-Z3.json", out / "r2-P3.json", out / "z3-ratios.json", self.evidence / "z3" / "r2-z3.txt"]})
        self.log("Z3/P3: " + ", ".join(f"{n} {'written' if (out / n).is_file() else 'NOT written'}" for n in ("r2-Z3.json", "r2-P3.json"))
                 + f" (r2_z3.py exit {code}; reasons in z3/r2-z3.txt)")
        self.finish("z3", results, verdict="RECORDED", extra=extra)

    def perf_with_wait(self, name, argv):
        """One wp078_perf invocation; waits (6/30/75) resume in the same root, 7 is STOP, 143 ends the chain."""
        code = None
        for attempt in range(1, self.wait_tries + 1):
            self.check_markers(name)
            code = self.execute(name, argv)
            if self.dry_run:
                break
            if code == STOP:
                raise Stop(STOP, f"{name} guard STOP (snapshot error, restore error or verdict mismatch)")
            if code == NOT_RESTORED:
                raise Stop(NOT_RESTORED, f"{name} NOT restored (a Player outlived a window) — C-00 resolves")
            if code == 143:
                raise Terminated()
            if code not in WAIT_CODES:
                break
            if attempt == self.wait_tries:
                raise Stop(code, f"{name} exit {code} (Player up / guard wait) persisted after {attempt} tries")
            self.log(f"WAIT {name}: exit {code} (try {attempt}/{self.wait_tries}); resumes in the same root in {self.wait_seconds:.0f} s")
            self.sleep(self.wait_seconds)
        return code

    def b9_windows(self):
        """C-00 01.10: S-05's B9 pose windows (-ddPilotStart x,z,yaw) run right after A4, in the same quiet-window run and
        on the same release-probe build; one wp078_perf root per pose (<E>/b9/<name>). Recorded in <E>/stages/b9.json for
        S-05's B9 report; not an acceptance gate (the chain exit and the A4 verdict ignore it)."""
        if not self.b9_poses:
            return
        results = []
        for pose in self.b9_poses:
            root = self.evidence / wp078_report.B9_ROOT / pose["name"]
            argv = [self.py, HERE / "wp078_perf.py", "--bin", self.evidence / "build-probe", "--out", root, "--commit", self.final,
                    "--tiers", pose["tiers"], "--modes", pose["modes"],
                    "--extra-arg=-ddPilotStart", "--extra-arg=" + pose["start"]]
            code = self.perf_with_wait("b9-" + pose["name"], argv)
            report_code = self.execute("b9-" + pose["name"] + "-report", [self.py, HERE / "wp078_perf_report.py", root])
            if self.dry_run:
                continue
            results.append({"name": "b9-" + pose["name"], "exit": code, "gate": False, "pose": dict(pose),
                             "files": [root / "attempts-ledger.jsonl"]})
            results.append({"name": "b9-" + pose["name"] + "-report", "exit": report_code, "gate": False,
                            "files": [root / "wp078-perf-report.json"]})
        if not self.dry_run:
            self.finish("b9", results, guarded=False, verdict="RECORDED", extra={"buildRole": "probe", "source": "S-05 B9 pose list"})

    def stage_r2(self):
        """WP-091: the owners' R2 measurement tests that need no quiet machine (r2-gates.json measuredBy quiet=false)."""
        self.r2_measurements(quiet=False)

    def stage_r2q(self):
        """WP-091: the quiet-machine R2 measurements (sound captures, render ratios) — run inside the window, after A4."""
        self.r2_measurements(quiet=True)

    def r2_measurements(self, quiet):
        """Each measurement session runs in its own guarded Editor test run (PlayMode: player scope — lock order) with
        DD_R2_MEASURE_DIR=<E>/r2 and DD_R2_COMMIT=<FINAL>; the test writes <E>/r2/r2-<gate id>.json. Recorded, not a
        chain gate: wp078_report judges every R2 gate from those files (a missing one is BLOCKED there)."""
        stage = "r2q" if quiet else "r2"
        if self.round_name != "R2":
            self.log(f"SKIP {stage}: not an R2 round")
            return
        contract = r2_gates.load_contract((HERE / "r2-gates.json").read_text(encoding="utf-8"))
        steps = r2_gates.measurement_steps(contract, quiet)
        measure_dir = self.evidence / "r2"
        switches = set(OWNER_SWITCHES) | {k for g in contract["gates"] for k in (g.get("measuredBy") or {}).get("env", {})}
        results = []
        for step in steps:
            out = measure_dir / step["id"]
            unset = [a for name in sorted(switches - set(step.get("env", {}))) for a in ("-u", name)]
            argv = (["/usr/bin/env", *unset, f"DD_R2_MEASURE_DIR={measure_dir}", f"DD_R2_COMMIT={self.final}"]
                    + [f"{k}={v}" for k, v in r2_gates.expand_env(step.get("env", {}), dd_paths.KPROJ).items()] + [
                    REPO / "tools" / "test.sh", step["suite"], "--out", out, "--filter", step["filter"]])
            if step["suite"] == "playmode":
                argv += ["--timeout-min", str(ISOLATED_TIMEOUT_MIN * 3)]
            if not self.dry_run:
                measure_dir.mkdir(parents=True, exist_ok=True)
            name = f"{stage}-{step['id']}"
            # SHOULD-6: a quiet session waits for a quiet host and is sampled while it runs (a reused unit is not run again)
            watch = quiet and not self.dry_run and self.reusable_unit(self.unit_record(name)) is None
            if quiet and self.dry_run:
                self.out(f"DRY r2q: host check before {name} (no foreign Unity / Player / Blender / ffmpeg, ≤ "
                         f"{self.host.wait_max:.0f} s) + a sample every {self.host.interval:.0f} s → {out}/host.json")
            if watch:
                start, calm, waited = self.host.wait_quiet()
                self.log(f"HOST {name}: {'quiet' if calm else 'NOT quiet'} at start after {waited:.0f} s — {heavy_text(start)}"
                         f" (load1 {start.get('load1')}, reported only)")
                self.host.start()
            try:
                result = self.editor_unit(name, [argv], out,
                                          lambda out=out, step=step: [out / f"{step['suite']}.xml", out / f"{step['suite']}.log"]
                                          + [measure_dir / f"r2-{g}.json" for g in step["gates"]],
                                          lambda step=step: {"r2Gates": step["gates"], "filter": step["filter"], "env": step.get("env", {}), "quiet": step["quiet"]},
                                          scope="player" if step["suite"] == "playmode" else "editor")
            finally:
                samples = self.host.stop() if watch else None
            if watch:
                host = host_record(step, start, calm, waited, samples)
                out.mkdir(parents=True, exist_ok=True)
                write_json(out / "host.json", host)
                keys = ("t", "load1") + HOST_HEAVY
                (out / "host-samples.tsv").write_text("\t".join(keys) + "\n" + "".join(
                    "\t".join(str(s.get(k)) for k in keys) + "\n" for s in samples), encoding="utf-8")
                self.log(f"KİRLİ {name}: " + "; ".join(host["dirtyWhy"]) if host["dirty"] else
                         f"HOST {name}: quiet — {host['samples']} samples, no foreign heavy process (load1 max {host['load1Max']})")
            if result and quiet and (out / "host.json").is_file():
                result = dict(result, files=list(result["files"]) + [out / "host.json", out / "host-samples.tsv"])
            if result:
                results.append(dict(result, gate=False))
        p5 = self.r2_load_time(measure_dir) if not quiet else None
        if not self.dry_run:
            extra = {"r2Steps": [s["id"] for s in steps]}
            if p5 is not None:
                extra["p5"] = p5
            if p5 == "written":
                extra["p5BaselineCommit"] = self.r1_commit   # check_baseline: P5 and P2 on one R1 baseline (gt 06.10 N-11)
            self.finish(stage, results, verdict="RECORDED", extra=extra)

    def r2_load_time(self, measure_dir):
        """P5 (C-00 05.10 (a)): the Selection → Run load time of this chain's own A2/A3 bot sessions over the R1 FINAL
        evidence's, from each session's ui-data/load-timing.json — no Player of its own. Without --r1-evidence P5 stays
        BLOCKED in the report (named). An earlier r2-P5.json is retired first, with or without a baseline this time, so a
        re-evaluation never binds the P5 of an earlier one (S-05 06.10, gt N-1 / N-11)."""
        self.retire(measure_dir / "r2-P5.json")
        if self.r1_evidence is None:
            self.log("P5: no --r1-evidence/--r1-commit — r2-P5.json not written, P5 BLOCKED in the report")
            return "no-baseline"
        if self.dry_run:
            self.out(f"DRY r2: P5 from {self.evidence}/{{a2,a3}}/*/ui-data/load-timing.json over {self.r1_evidence} "
                     f"({self.r1_commit}) → {measure_dir}/r2-P5.json")
            return None
        data, r2, r1 = r2_load_time.artifact(self.evidence, self.r1_evidence, self.final, self.r1_commit)
        if data is None:
            self.log(f"P5: no samples (R2 {len(r2)}, R1 {len(r1)}) — r2-P5.json not written, P5 BLOCKED in the report")
            return "no-samples"
        measure_dir.mkdir(parents=True, exist_ok=True)
        (measure_dir / "r2-P5.json").write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        v = data["values"]
        self.log(f"P5 ratio {v['ratio']} (R2 {v['r2MedianSeconds']} s / R1 {v['r1MedianSeconds']} s, "
                 f"{v['r2Samples']}/{v['r1Samples']} sessions, floorLimited {v['floorLimited']})")
        return "written"

    def stage_a8(self):
        manifest_path = self.evidence / "manifest.json"
        stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
        report_out = self.evidence / f"report-{stamp}"
        k = 1
        while report_out.exists():          # a new directory per report; earlier reports are never overwritten
            k += 1
            report_out = self.evidence / f"report-{stamp}-{k}"
        argv = [self.py, HERE / "wp078_report.py", "--manifest", manifest_path, "--evidence-root", self.evidence, "--out", report_out]
        if self.dry_run:
            self.out(f"DRY a8: assemble {manifest_path} from {self.evidence} (stage records + evidence tree, sha256 of every artifact)")
            self.execute("wp078-report", argv)
            return
        try:
            assets = self.git("rev-parse", f"{self.final}:{PROJECT}/Assets")
        except (subprocess.CalledProcessError, OSError):
            assets = None
        manifest = assemble_manifest(self.evidence, self.final, assets, self.round_name)
        if manifest_path.exists():
            self.move_aside(manifest_path, "prev")
        write_json(manifest_path, manifest)
        self.log(f"MANIFEST {manifest_path.relative_to(self.evidence)} steps={','.join(sorted(manifest['checklist']))}")
        self.report_exit = self.execute("wp078-report", argv)
        self.verdicts["a8"] = {0: "PASS", 1: "NOT_PASS"}.get(self.report_exit, "ERROR")
        self.log(f"REPORT {report_out.relative_to(self.evidence)} exit={self.report_exit}")


# ---- A8 manifest assembly (pure: reads the evidence tree, never runs anything) -------------------------------------------
def _record(evidence, final, stage):
    path = evidence / "stages" / f"{stage}.json"
    record = load_json(path)
    return (path, record) if isinstance(record, dict) and record.get("executionCommit") == final else (path, None)


def _clean(value):
    """Drop keys whose value could not be filled from real evidence (None) — they stay absent, never invented. A guard
    receipt is copied verbatim: the report compares it with the execution record's own copy."""
    if isinstance(value, dict):
        return {k: (v if k == "guard" else _clean(v)) for k, v in value.items() if v is not None}
    if isinstance(value, list):
        return [_clean(v) for v in value if v is not None]
    return value


def _step(evidence, path, record, guarded=True, owner="S-10"):
    step = {"owner": owner, "executionCommit": record.get("executionCommit"), "provenance": ref(evidence, path),
            "evidence": [ref(evidence, path)]}
    if record.get("verdict") in ("PASS", "FAIL", "BLOCKED"):
        step["status"] = record["verdict"]
    if type(record.get("exitCode")) is int:
        step["originalExit"] = record["exitCode"]
    if guarded and isinstance(record.get("guard"), dict):
        step["guard"] = record["guard"]
    return step


def _bot_run(evidence, run_dir):
    attempt = load_json(run_dir / "attempt.json")
    if not isinstance(attempt, dict):
        return None
    entry = {"provenance": ref(evidence, run_dir / "attempt.json"), "buildRole": attempt.get("buildRole"),
             "summary": ref(evidence, run_dir / "bot-summary.json"), "log": ref(evidence, run_dir / "player.log")}
    if type(attempt.get("exitCode")) is int:
        entry["originalExit"] = attempt["exitCode"]
    guard = attempt.get("guard")
    if isinstance(guard, dict):
        entry["guard"] = {k: guard[k] for k in GUARD_FIELDS if k in guard}
    return entry


def _route_run(evidence, run_dir):
    entry = _bot_run(evidence, run_dir)
    if entry is None:
        return None
    frames = {}
    summary = load_json(run_dir / "bot-summary.json") or {}
    rows = summary.get("routeFrames") if isinstance(summary.get("routeFrames"), list) else []
    for row in rows:
        if not isinstance(row, dict) or row.get("key") not in wp078_report.ROUTE_KEYS:
            continue
        if row.get("status") == "captured" and isinstance(row.get("path"), str) and row["path"] and not Path(row["path"]).is_absolute():
            frame = ref(evidence, run_dir / row["path"])
            if frame is not None and (run_dir / row["path"]).resolve().is_relative_to(run_dir.resolve()):
                frames[row["key"]] = frame
        elif row.get("status") == "notApplicable":
            reason = str(row.get("reason", "")).split(":", 1)[0].strip()   # the native reason's identifier
            if reason:
                frames[row["key"]] = {"notApplicable": reason}
    entry["frames"] = frames      # a key the bot did not capture stays absent → BLOCKED, never substituted
    return entry


def _flag_run(evidence, run_dir):
    entry = _bot_run(evidence, run_dir)
    if entry is None:
        return None
    command = load_json(run_dir / "command.json")
    entry["command"] = ref(evidence, run_dir / "command.json")
    if isinstance(command, list):
        for flag in wp078_report.AI_FLAGS:
            positions = [i for i, arg in enumerate(command) if arg == flag]
            if len(positions) == 1 and positions[0] + 1 < len(command) and command[positions[0] + 1] in ("0", "1"):
                entry["flag"], entry["value"] = flag, int(command[positions[0] + 1])
    return entry


def _dirs(parent, pattern):
    if not parent.is_dir():
        return []
    rx = re.compile(pattern)
    return sorted((p for p in parent.iterdir() if p.is_dir() and rx.fullmatch(p.name)),
                  key=lambda p: [int(t) if t.isdigit() else t for t in re.split(r"(\d+)", p.name)])


def r2_section(evidence):
    """WP-091 (C-00 05.10): the R2 gates' evidence — <E>/r2/r2-<gate id>.json (owner measurement, dd-r2-measure-v1),
    <E>/r2/reviews/<gate id>.json (human review, dd-r2-review-v1) and every NUnit XML of the run (A1 core + alone, A3
    isolated, <E>/r2/**/*.xml measurement sessions) for the required test identities. Listed from r2-gates.json, so a
    gate without an artifact is simply absent here and BLOCKED in the report."""
    contract = json.loads((HERE / "r2-gates.json").read_text(encoding="utf-8"))
    gates = {}
    # SHOULD-6: a quiet session's host record (<E>/r2/<step>/host.json) goes with each gate it measured
    hosts = {g: evidence / "r2" / step["id"] / "host.json"
             for step in r2_gates.measurement_steps(r2_gates.load_contract(json.dumps(contract)), True) for g in step["gates"]}
    for gate in contract["gates"]:
        entry = {"measure": ref(evidence, evidence / "r2" / f"r2-{gate['id']}.json"),
                 "review": ref(evidence, evidence / "r2" / "reviews" / f"{gate['id']}.json")}
        if any(entry.values()):
            host = ref(evidence, hosts[gate["id"]]) if gate["id"] in hosts else None
            if host:
                entry["host"] = host
            gates[gate["id"]] = entry
    xml_paths = [evidence / "a1" / "editmode" / "editmode.xml", evidence / "a1" / "playmode" / "playmode.xml"]
    for base in (evidence / "a1", evidence / "a3", evidence / "r2"):
        if base.is_dir():
            xml_paths += sorted(p for p in base.rglob("playmode.xml") if p not in xml_paths)
            xml_paths += sorted(p for p in base.rglob("editmode.xml") if p not in xml_paths)
    return {"owner": "S-10", "gates": gates, "xml": [r for r in (ref(evidence, p) for p in xml_paths) if r]}


def assemble_manifest(evidence, final, assets_tree, round_name=None):
    """The wp078_report manifest from the evidence tree: every path relative to the evidence root, every artifact with
    the sha256 of its bytes. A step or field without real evidence stays absent (the report says BLOCKED)."""
    evidence = Path(evidence).resolve()
    manifest = {"schema": wp078_report.SCHEMA, "commit": final, "final": True, "producer": "tools/qa/wp078_accept.py"}
    if isinstance(assets_tree, str) and re.fullmatch(r"[0-9a-f]{40}", assets_tree):
        manifest["assetsTree"] = assets_tree
    hashes = {}
    for role in BUILDS:
        binary = binary_of(evidence / f"build-{role}")
        if binary is not None:
            hashes[role] = sha256(binary)
    manifest["buildHashes"] = hashes
    checklist, records = {}, {s: _record(evidence, final, s) for s in ("a0", "a1", "a2", "a3", "a4", "a5", "a6")}

    path, record = records["a0"]
    if record:
        checklist["A0"] = dict(_step(evidence, path, record, guarded=False), library=ref(evidence, evidence / "a0" / "library-provenance.json"))

    _, alone, a3_isolated = ci_plan()
    xml = {"editMode": ref(evidence, evidence / "a1" / "editmode" / "editmode.xml"),
           "playMode": ref(evidence, evidence / "a1" / "playmode" / "playmode.xml"),
           # R2: listed from the tree's ci-isolated.txt, not globbed — a missing session shows as a missing file (BLOCKED)
           "playModeAlone": [ref(evidence, evidence / "a1" / step_id / "playmode.xml") for step_id, _ in alone]}
    isolated_xml = [ref(evidence, evidence / "a3" / step_id / "playmode.xml") for step_id, _ in a3_isolated]
    a1_path, a1 = records["a1"]
    if a1:
        checklist["A1"] = dict(_step(evidence, a1_path, a1), xml=xml)

    path, record = records["a2"]
    if record:
        checklist["A2"] = dict(_step(evidence, path, record),
                               runs=[_bot_run(evidence, d) for d in _dirs(evidence / "a2", r"gameplay-(topdown|tp|fp)")],
                               routeRuns=[_route_run(evidence, d) for d in _dirs(evidence / "a2", r"route-(topdown|tp|fp)")])

    a3_path, a3 = records["a3"]
    checks = {}
    xml_refs = [x for x in (xml["editMode"], xml["playMode"], *xml["playModeAlone"], *isolated_xml) if x]
    for area in wp078_report.AREAS:
        if area == "aiFlags":
            if a3:
                row = _step(evidence, a3_path, a3)
                row.pop("owner")            # the area owner is not recorded anywhere → UNKNOWN in the report, never guessed
                row["evidence"] = row["evidence"] + xml_refs
                row["flagRuns"] = [_flag_run(evidence, d) for d in _dirs(evidence / "a3", r"flag-[a-z0-9]+-[01]")]
                checks[area] = row
        elif a3:
            # R2: the A3 record consumes the A1 XMLs (core + alone) and holds its own `a3` isolated sessions, so it is the
            # one execution every area row's XML evidence is bound to
            row = _step(evidence, a3_path, a3)
            row.pop("owner")
            row["evidence"] = row["evidence"] + xml_refs
            checks[area] = row
        elif a1:
            row = _step(evidence, a1_path, a1)
            row.pop("owner")
            row["evidence"] = row["evidence"] + [x for x in xml_refs if x not in isolated_xml]
            checks[area] = row
    if a3:
        checklist["A3"] = dict(_step(evidence, a3_path, a3), checks=checks)

    path, record = records["a4"]
    if record:
        root = evidence / "a4" / "perf"
        attempts = [ref(evidence, d / "attempt.json") for d in _dirs(root, wp078_perf_report.CELL.pattern.strip("^$"))]
        step = dict(_step(evidence, path, record), perfRoot="a4/perf" if root.is_dir() else None,
                    ledger=ref(evidence, root / "attempts-ledger.jsonl"), attempts=[a for a in attempts if a] or None)
        if record.get("buildRole") == "probe":
            step["buildRole"] = "probe"
        # WP-091: the chain's own perf roots beside the gate root (wp078_report.secondary_perf_roots): the R1 baseline of
        # an R2 round and S-05's B9 poses — declared at their fixed places, never counted for the gate
        secondary = []
        if record.get("baselineRoot") == R1_ROOT and (evidence / R1_ROOT).is_dir():
            secondary.append({"role": "r1-baseline", "path": R1_ROOT, "commit": record.get("baselineCommit"),
                              "binarySha256": record.get("baselineBinarySha256")})
        # B9 roots come from the B9 record's poses (the name and the start it ran), never from a folder listing: a full
        # matrix in a folder nobody recorded stays a stray root (KADRO test-engineer MUST-2)
        _, z3 = _record(evidence, final, "z3")
        for entry in (z3 or {}).get("z3Roots", []):   # the Z3 pairs, declared from the z3 record (S-05 r2_z3.py)
            if isinstance(entry, dict) and isinstance(entry.get("path"), str) and (evidence / entry["path"]).is_dir():
                secondary.append(dict(entry))
        _, b9 = _record(evidence, final, "b9")
        for unit in (b9 or {}).get("units", []):
            pose = unit.get("pose") if isinstance(unit, dict) else None
            if isinstance(pose, dict) and isinstance(pose.get("name"), str) and (evidence / wp078_report.B9_ROOT / pose["name"]).is_dir():
                secondary.append({"role": "b9-pose", "path": wp078_report.B9_ROOT + "/" + pose["name"], "start": pose.get("start")})
        if secondary:
            step["secondaryRoots"] = secondary
        checklist["A4"] = step

    path, record = records["a5"]
    if record:
        checklist["A5"] = dict(_step(evidence, path, record),
                               runs=[_bot_run(evidence, d) for d in _dirs(evidence / "a5", r"stability-\d+")],
                               reportRuns=[_bot_run(evidence, d) for d in _dirs(evidence / "a5", r"report-(topdown|tp|fp)-\d+")])

    path, record = records["a6"]
    if record:
        builds = {}
        for role in BUILDS:
            unit = evidence / "stages" / "units" / f"build-{role}.json"
            receipt = load_json(unit)
            if not isinstance(receipt, dict) or receipt.get("executionCommit") != final:
                continue
            out = evidence / f"build-{role}"
            binary = binary_of(out)
            builds[role] = {"provenance": ref(evidence, unit), "guard": receipt.get("guard"),
                            "originalExit": receipt.get("exitCode") if type(receipt.get("exitCode")) is int else None,
                            "library": ref(evidence, out / "library-provenance.json"),
                            "binary": ref(evidence, binary) if binary else None,
                            "buildReport": ref(evidence, out / "build-report.json"), "report": ref(evidence, out / "report.json")}
        checklist["A6"] = dict(_step(evidence, path, record), builds=builds)

    if a1:
        # A7: ProjectIntegrityTests is an EditMode suite (the A1 execution); byte-equal restoration is every guarded
        # stage's restore verified. A guarded stage without a record leaves byteEqual and the status absent (BLOCKED).
        guarded = [records[s][1] for s in ("a6", "a1", "a2", "a3", "a5", "a4")]
        step = dict(_step(evidence, a1_path, a1), integrityXml=xml["editMode"])
        if all(guarded):
            step["byteEqual"] = all(guard_ok(r.get("guard")) for r in guarded)
            if not step["byteEqual"]:
                step["status"] = "FAIL"
        else:
            step.pop("status", None)
        checklist["A7"] = step

    if round_name == "R2":
        manifest["round"] = "R2"
        checklist["R2"] = r2_section(evidence)
    manifest["checklist"] = checklist
    return _clean(manifest)


def parse_stages(text):
    stages = [s.strip().lower() for s in text.split(",") if s.strip()]
    unknown = [s for s in stages if s not in STAGE_ORDER]
    if unknown or not stages:
        raise ValueError(f"unknown stage(s) {unknown}; choose from {','.join(STAGE_ORDER)}")
    return stages


def main(argv=None, **seams):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--final", required=True, help="the FINAL commit, full lowercase 40-hex SHA")
    parser.add_argument("--evidence", help="evidence root (default KADRO wp-078/evidence/<FINAL[:8]>)")
    parser.add_argument("--stages", default=",".join(STAGE_ORDER))
    parser.add_argument("--dry-run", action="store_true", help="print the exact commands, run nothing")
    parser.add_argument("--resume", action="store_true", help="skip a stage whose evidence already passed for the same FINAL")
    parser.add_argument("--editmode-filter", default="", help="A1: test.sh --filter for EditMode (default: the whole suite)")
    parser.add_argument("--playmode-filter", default="", help="A1: test.sh --filter for PlayMode (default: the whole suite)")
    parser.add_argument("--wait-tries", type=int, default=3)
    parser.add_argument("--wait-seconds", type=float, default=120.0)
    parser.add_argument("--round", choices=("R1", "R2"), default="R1", help="WP-091: R2 adds the r2-gates.json gates to the manifest")
    parser.add_argument("--r1-evidence", help="WP-091 P5: the R1 FINAL evidence root (its A2/A3 load-timing.json are the baseline)")
    parser.add_argument("--r1-commit", help="WP-091 P5: the R1 FINAL commit, 40-hex (recorded in r2-P5.json)")
    parser.add_argument("--b9-poses", help="C-00 01.10: S-05 B9 pose list (JSON) for the b9 stage, which runs after a4 "
                                            "(its own stage since WP-091: give it in --stages, e.g. a4,b9)")
    try:
        args = parser.parse_args(argv)
    except SystemExit as error:
        return 0 if error.code == 0 else 64
    if not re.fullmatch(r"[0-9a-f]{40}", args.final):
        print("--final must be a full lowercase 40-hex SHA", file=sys.stderr)
        return 64
    try:
        stages = parse_stages(args.stages)
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 64
    # G4 D4 (C-00 01.10): the canonical driver guard or nothing (the wp078_perf / wp078_bot children check it too)
    refusal = None if args.dry_run or "guard" in seams else guard_receipt.canon_refusal()
    if refusal:
        print(f"guard refused (G4 D4): {refusal} — nothing started", file=sys.stderr)
        return 64
    evidence = Path(args.evidence) if args.evidence else KADRO_EVIDENCE / args.final[:8]
    if bool(args.r1_evidence) != bool(args.r1_commit) or (args.r1_commit and not re.fullmatch(r"[0-9a-f]{40}", args.r1_commit)):
        print("--r1-evidence and --r1-commit go together; --r1-commit is a 40-hex SHA", file=sys.stderr)
        return 64
    if args.r1_commit and (args.r1_commit == args.final or Path(args.r1_evidence).resolve() == evidence.resolve()):
        # P2 would compare FINAL with itself and the report would refuse the root hours later (KADRO test-engineer SHOULD-1)
        print("--r1-evidence/--r1-commit must be the R1 FINAL's, not this FINAL's own", file=sys.stderr)
        return 64
    try:
        b9_poses = load_b9_poses(args.b9_poses) if args.b9_poses else []
    except ValueError as error:
        print("--b9-poses: " + str(error), file=sys.stderr)
        return 64
    if b9_poses and "b9" not in stages:
        # gt 06.10 RB-1: B9 left a4 for its own stage; the R1 FINAL'' form (--stages a4 --b9-poses) would run no pose at all
        print("--b9-poses needs the b9 stage in --stages (B9 is its own stage after a4 since WP-091 7b25a393), e.g. "
              "--stages a4,b9 — nothing started", file=sys.stderr)
        return 64
    chain = Chain(args.final, evidence, stages, dry_run=args.dry_run, resume=args.resume,
                  editmode_filter=args.editmode_filter, playmode_filter=args.playmode_filter,
                  wait_tries=args.wait_tries, wait_seconds=args.wait_seconds, b9_poses=b9_poses, round_name=args.round,
                  b9_poses_path=args.b9_poses,
                  r1_evidence=args.r1_evidence, r1_commit=args.r1_commit, **seams)
    previous = signal.signal(signal.SIGTERM, chain.on_sigterm)
    try:
        return chain.run()
    finally:
        signal.signal(signal.SIGTERM, previous)


if __name__ == "__main__":
    raise SystemExit(main())
