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
                   wp078_perf_report.py (cross-check, recorded)
  a8  report       assembles <E>/manifest.json from the evidence tree (paths relative to <E>, every artifact with its
                   sha256; a field without real evidence stays absent → the report says BLOCKED) and runs
                   tools/qa/wp078_report.py --manifest … --evidence-root <E> --out <E>/report-<utc>
Order (dependency): a6 → a0 → a1 → a2 → a3 → a5 → a4 → a8, whatever order --stages lists them in.

Each stage writes an execution record <E>/stages/<stage>.json (executionCommit, exitCode, guard, artifactSha256 of the
outputs it produced) — that record is the step provenance the manifest binds. Guarded builds / suites write a unit
record <E>/stages/units/<name>.json. <E>/accept.log gets one UTC line per step.

Stop rules: a guard STOP (exit 7 of a runner or of the Editor guard) stops the whole chain at once; a snapshot / host
wait (30, 75, or 6 = a game Player up) is retried --wait-tries times, then the chain stops. G4 D2 (review MUST-1): a
window that was NOT restored (runner exit 65, userdata-NOT-RESTORED.txt) stops the chain at once, and an unresolved
marker anywhere under <E> stops it before every guarded unit and every retry — never a wait, never moved aside. Each stop prints
"STOP … report to C-00". A failed gameplay / perf / suite result does not stop later independent stages — the report
shows it. The tree must stay at FINAL and clean before every stage (exit 2 otherwise).

usage: wp078_accept.py --final <40-hex> [--evidence DIR] [--stages a6,a0,a1,a2,a3,a5,a4,a8] [--dry-run] [--resume]
                       [--editmode-filter RE] [--playmode-filter RE] [--wait-tries 3] [--wait-seconds 120]
exit: 0 chain ran and the report PASSes (without a8: every selected stage PASS) · 1 an honest non-PASS · 2 HEAD is not
      FINAL / the tree is dirty · 7 guard STOP · 65 a guard window NOT restored (or an unresolved marker under <E>) ·
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
import wp078_perf  # noqa: E402  (resolve_binary)
import wp078_perf_report  # noqa: E402  (CELL: the perf window directory layout)
import wp078_report  # noqa: E402  (AI_FLAGS, AREAS, ROUTE_KEYS, SCHEMA)

PROJECT = "prototype/DeathsDemolition_PlayableConcept"
KADRO_EVIDENCE = Path.home() / "Desktop" / "KADRO" / "context" / "projects" / "deaths-demolition" / "wp-078" / "evidence"
STAGE_ORDER = ("a6", "a0", "a1", "a2", "a3", "a5", "a4", "a8")
PLAYMODE_TIMEOUT_MIN = 90   # A1 full PlayMode watchdog (test.sh default 45 min; main-ci uses 60)
ISOLATED_TIMEOUT_MIN = 10   # one isolated test in its own session (main-ci --alone-timeout default)


def _ci_isolated():
    spec = importlib.util.spec_from_file_location("_wp078_ci_isolated", REPO / "tools" / "ci_isolated.py")
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
                 "a3": ("a3",), "a5": ("a5",), "a4": ("a4",), "a8": ()}
BUILDS = {"dev": "dev", "release": "release", "probe": "release-probe"}   # role → tools/build.sh mode (K1)
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


def binary_of(build_dir):
    path = wp078_perf.resolve_binary(build_dir)
    return path if path.is_file() else None


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
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,40}", pose["name"]) or pose["name"] in names:
            raise ValueError("pose name must be a unique lower-case folder name: " + pose["name"])
        if not re.fullmatch(r"-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?", pose["start"]):
            raise ValueError("start must be x,z,yaw: " + pose["start"])
        names.add(pose["name"])
    return poses


class Chain:
    def __init__(self, final, evidence, stages, dry_run=False, resume=False, editmode_filter="", playmode_filter="",
                 wait_tries=3, wait_seconds=120.0, git=None, runner=None, guard=None, out=print, python=sys.executable,
                 b9_poses=None):
        self.final, self.evidence = final, Path(evidence).resolve()
        self.stages = [s for s in STAGE_ORDER if s in stages]
        self.dry_run, self.resume = dry_run, resume
        self.filters = {"editmode": editmode_filter, "playmode": playmode_filter}
        self.wait_tries, self.wait_seconds = max(1, wait_tries), wait_seconds
        self.git = git or self._git
        self.runner = runner or self._spawn
        self.guard = guard or EditorGuard()
        self.out, self.py = out, python
        self.b9_poses = list(b9_poses or [])   # C-00 01.10: S-05 B9 pose windows, after A4 in the same quiet-window run
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
        self.log(f"START final={self.final} evidence={self.evidence} stages={','.join(self.stages)}"
                 f"{' dry-run' if self.dry_run else ''}{' resume' if self.resume else ''}")
        try:
            for stage in self.stages:
                self.stage = stage
                self.check_terminated()
                if not self.dry_run and not self.verify_tree():
                    raise Stop(2, f"{stage}: repo left FINAL or the tree became dirty")
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
            record = load_json(record_path)
            if (isinstance(record, dict) and record.get("executionCommit") == self.final and record.get("status") == "complete"
                    and guard_ok(record.get("guard")) and map_verifies(record_path.parent, record.get("artifactSha256"))):
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
        code = self.perf_with_wait("wp078-perf", argv)
        report_code = self.execute("wp078-perf-report", [self.py, HERE / "wp078_perf_report.py", root])
        if self.dry_run:
            self.b9_windows()
            return
        attempts = sorted(p / "attempt.json" for p in root.iterdir() if p.is_dir() and wp078_perf_report.CELL.fullmatch(p.name)) if root.is_dir() else []
        # wp078_perf exits 0 only when every window's restore verdict was ok (it returns 7 / 30 / 75 otherwise), so exit 0
        # proves the four guard fields for the whole matrix; any other exit leaves the step without a guard.
        guard = {"snapshotExit": 0, "restoreExit": 0, "verified": 1, "outOfScopeChanged": 0,
                 "source": "wp078_perf exit 0 = every window restored and verified"} if code == 0 else None
        results = [{"name": "wp078-perf", "exit": code, "guard": guard, "files": [root / "attempts-ledger.jsonl"] + attempts},
                   {"name": "wp078-perf-report", "exit": report_code, "gate": False,
                    "files": [root / "wp078-perf-report.json", root / "wp078-perf-report.txt"]}]
        self.finish("a4", results, inputs=[binary], extra={"buildRole": "probe", "perfRoot": "a4/perf"})
        self.b9_windows()

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
            root = self.evidence / "b9" / pose["name"]
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
        manifest = assemble_manifest(self.evidence, self.final, assets)
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


def assemble_manifest(evidence, final, assets_tree):
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
    parser.add_argument("--b9-poses", help="C-00 01.10: S-05 B9 pose list (JSON) run after A4 in the same quiet-window run")
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
    try:
        b9_poses = load_b9_poses(args.b9_poses) if args.b9_poses else []
    except ValueError as error:
        print("--b9-poses: " + str(error), file=sys.stderr)
        return 64
    chain = Chain(args.final, evidence, stages, dry_run=args.dry_run, resume=args.resume,
                  editmode_filter=args.editmode_filter, playmode_filter=args.playmode_filter,
                  wait_tries=args.wait_tries, wait_seconds=args.wait_seconds, b9_poses=b9_poses, **seams)
    previous = signal.signal(signal.SIGTERM, chain.on_sigterm)
    try:
        return chain.run()
    finally:
        signal.signal(signal.SIGTERM, previous)


if __name__ == "__main__":
    raise SystemExit(main())
