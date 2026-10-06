"""WP-078 (C-00 30.09 M-16) · tools/qa/wp078_accept.py: the R1 acceptance chain.

Fake git, fake guard and fake tools only — no Unity, no Player, no build, no real userdata-guard. The fake tools write
the files the real ones write (build.sh, library_provenance.py, test.sh, wp078_bot.py, wp078_perf.py), so the manifest
the chain assembles is checked by the real wp078_report.generate_report."""
import argparse
import contextlib
import io
import hashlib
import importlib.util
import json
import re
import os
import shutil
import signal
import struct
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from types import SimpleNamespace
from unittest import mock
import uuid
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]   # tools/qa
sys.path.insert(0, str(HERE))
import guard_receipt  # noqa: E402
import wp078_accept as acc  # noqa: E402
import wp078_bot  # noqa: E402
import wp078_perf  # noqa: E402
import wp078_report  # noqa: E402


def _load_report_fixtures():
    """The A8 report's own fixtures (DOMAIN_IDS, TRACE, EvidenceFixture.perf) — loaded as a module object so its test
    classes are not collected a second time here."""
    spec = importlib.util.spec_from_file_location("_wp078_report_fixtures", HERE / "tests" / "test_wp078_report.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


RF = _load_report_fixtures()
FINAL = RF.FINAL
ASSETS = "c" * 40
PROJECT_ASSETS = f"{FINAL}:{acc.PROJECT}/Assets"
SCENES = RF.SCENES
EXE = "Deaths Demolition - Playable Concept"
BIN_BYTES = {"dev": b"dev", "release": b"release", "release-probe": b"probe"}   # = the report fixture's binaries
OK_GUARD = {"code": 0, "why": "touched=0", "restoreExit": 0, "verified": True, "touched": "0", "outOfScopeChanged": False}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def put(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data if isinstance(data, bytes) else data.encode())


def put_json(path, value):
    put(path, json.dumps(value, indent=2) + "\n")


def png(serial):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    pixel = bytes([serial % 256, (serial // 256) % 256, 7])
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(b"\x00" + pixel)) + chunk(b"IEND", b""))


def nunit(names):
    import xml.etree.ElementTree as ET
    root = ET.Element("test-run", total=str(len(names)), passed=str(len(names)), failed="0", skipped="0", inconclusive="0", result="Passed")
    for name in names:
        ET.SubElement(root, "test-case", fullname=name, name=name, result="Passed")
    return ET.tostring(root)


class FakeGit:
    def __init__(self, head=FINAL, porcelain=""):
        self.head, self.porcelain, self.calls = head, porcelain, 0

    def __call__(self, *args):
        self.calls += 1
        if args == ("rev-parse", "HEAD"):
            return self.head
        if args == ("status", "--porcelain"):
            return self.porcelain() if callable(self.porcelain) else self.porcelain
        if args == ("rev-parse", PROJECT_ASSETS):
            return ASSETS
        raise subprocess.CalledProcessError(128, ["git", *args])


class FakeGuard:
    def __init__(self, snapshot_codes=(), close=None):
        self.snapshot_codes, self.close_receipt, self.calls, self.scopes = list(snapshot_codes), close or dict(OK_GUARD), [], []

    def path(self):
        return "/main/tools/userdata-guard.sh"

    def snapshot(self, ug, scope="editor"):
        self.calls.append(("snapshot", ug.name))
        self.scopes.append(("snapshot", ug.name, scope))
        ug.mkdir(parents=True, exist_ok=True)
        return self.snapshot_codes.pop(0) if self.snapshot_codes else 0

    def close(self, ug, scope="editor"):
        self.calls.append(("close", ug.name))
        self.scopes.append(("close", ug.name, scope))
        return dict(self.close_receipt)


class FakeTools:
    """The native outputs of the real tools, written into the paths the chain passes (the runner seam)."""

    def __init__(self, perf_guard=True, perf_ug_dirs=False):
        self.calls, self.script, self.perf_guard, self.perf_ug_dirs = [], {}, perf_guard, perf_ug_dirs
        self.serial = 0

    def __call__(self, argv, log_path):
        self.calls.append(list(argv))
        script = Path(argv[1]).name if argv[0] == sys.executable else Path(argv[0]).name
        log_path.parent.mkdir(parents=True, exist_ok=True)
        with log_path.open("a", encoding="utf-8") as handle:
            handle.write("fake " + script + "\n")
        return getattr(self, "tool_" + script.replace(".", "_"))(argv, log_path)

    def tool_build_sh(self, argv, _):
        mode, out = argv[1], Path(argv[3])
        put(out / "DeathsDemolition.app" / "Contents" / "MacOS" / EXE, BIN_BYTES[mode])
        native = dict(result="Succeeded", mode=mode, totalErrors=0, totalSize=1000, scenes=len(SCENES), development=mode == "dev",
                      sceneList=list(SCENES), scriptingBackend="Mono2x",
                      stamp=dict(stamped=True, commit=FINAL[:12], describe="wp-078-accept-" + FINAL[:8], mode=mode))
        report = {"mode": mode, "ok": True, "exitCode": 0, "head": FINAL}
        if mode != "dev":
            native["compression"] = report["compression"] = "lz4hc"
        put_json(out / "build-report.json", native)
        put_json(out / "report.json", report)
        put(out / "build.log", "fake build\n")
        return 0

    def tool_library_provenance_py(self, argv, _):
        put_json(Path(argv[3]), dict(schema="dd-wp064-library-provenance-v1", phase="post-build", assetsTree=ASSETS, head=FINAL,
                                     porcelain="", lfsPointersOnDisk=0, lfsMissing=0, library={"artifactDbBytes": 1, "artifactDbMtime": 1.0}))
        return 0

    def tool_env(self, argv, log_path):
        # WP-091 r2 sessions: /usr/bin/env -u NAME … K=V … tools/test.sh …  → strip both, run the fake test.sh
        rest = list(argv[1:])
        while rest and (rest[0] == "-u" or ("=" in rest[0] and not rest[0].startswith("/"))):
            rest = rest[2:] if rest[0] == "-u" else rest[1:]
        assert Path(rest[0]).name == "test.sh", rest[:3]   # the session command, never a switch name
        return self.tool_test_sh(rest, log_path)

    def tool_test_sh(self, argv, _):
        mode, out = argv[1], Path(argv[3])
        part = ".EditMode." if mode == "editmode" else ".PlayMode."
        names = sorted({n for ids in RF.DOMAIN_IDS.values() for n in ids if part in n})
        if mode == "editmode":
            names.append("DD.Prototype.Tests.EditMode.ProjectIntegrityTests.Assets_Guid_Unique")
        if "--filter" in argv:   # Unity -testFilter: .NET regex, search semantics, against the full name
            rx = re.compile(argv[argv.index("--filter") + 1])
            names = [n for n in names if rx.search(n)] or ["DD.Prototype.Tests.PlayMode.Isolated.Placeholder"]
        put(out / f"{mode}.xml", nunit(names))
        put(out / f"{mode}.log", "fake unity\n")
        return 0

    def tool_wp078_bot_py(self, argv, _):
        opts, extra, quiet, i = {}, [], False, 2
        while i < len(argv):
            if argv[i].startswith("--extra-arg="):
                extra.append(argv[i].split("=", 1)[1])
                i += 1
            elif argv[i] == "--quiet":
                quiet, i = True, i + 1
            else:
                opts[argv[i]], i = argv[i + 1], i + 2
        out, kind, mode = Path(opts["--out"]), opts["--kind"], opts["--mode"]
        seed, rounds = int(opts["--seed"]), int(opts["--rounds"])
        planned = self.script.get(out.name)
        code = planned.pop(0) if planned else 0
        if code in (30, 75):
            put_json(out / "attempt.json", {"status": "guard-blocked", "exitCode": code, "guard": {"snapshotExit": code}})
            return code
        if code == 65:   # G4 D2: what wp078_bot leaves when a Player outlived the run — the snapshot is kept, not restored
            put(out / "ug" / guard_receipt.MARKER, "NOT restored\n")
            return code
        if code in (6, 7, 64):
            return code
        binary = wp078_perf.resolve_binary(opts["--bin"])
        ns = argparse.Namespace(kind=kind, mode=mode, seed=seed, rounds=rounds, tier=opts["--tier"], extra_arg=extra)
        command = wp078_bot.build_command(binary, out, ns)
        put_json(out / "command.json", command)
        native = RF.MODE_NAMES[mode]
        failed = code == 1
        summary = dict(cameraMode=native, seed=seed, qualityTier="Sinematik", worldHalfExtent=55.0, timedOut=False,
                       stuckRecoveries=0, unstickSweeps=0)
        log = RF.TRACE
        if kind == "route":
            self.serial += 1
            rows = []
            for key in RF.ROUTE_KEYS:
                if mode == "topdown" and key == "horizon":
                    rows.append(dict(key=key, status="notApplicable", reason="topdown-fixed-camera: yaw fixed north", path="", mode=native, roofAbove=False))
                    continue
                self.serial += 1
                put(out / "frames" / f"route-{key}.png", png(self.serial))
                rows.append(dict(key=key, status="captured", reason="", path=f"frames/route-{key}.png", mode=native, roofAbove=key in ("corridor", "kairo")))
            summary.update(runMode="route", routeFrames=rows, roundsCompleted=0)
            summary["pass"] = True
            log = "[QaRunBot] route-tour done frames=4"
        else:
            records = [dict(round=n, completed=True, outcome="completed", memMonoUsedMB=100, memTotalAllocatedMB=200, memRssMB=300)
                       for n in range(1, rounds + 1)]
            summary.update(runMode="stability" if kind == "stability" else "normal", rounds=records, roundsRequested=rounds,
                           roundsCompleted=rounds, deaths=1 if failed else 0, stabilityPass=kind == "stability")
            summary["pass"] = not failed
            log = "\n".join(f"[Camera] inside-collider round={n} frames=0/60" for n in range(1, rounds + 1)) + "\n" + RF.TRACE
        put_json(out / "bot-summary.json", summary)
        put(out / "player.log", log + "\n")
        player_exit = 3 if failed else 0
        put(out / "exit.txt", f"exit={player_exit}\n")
        put_json(out / "qa-report.json", {"ok": True})
        put(out / "events.txt", "")
        attempt = {"schema": "dd-wp064-attempt-v1", "producer": "wp078_bot", "attemptId": str(uuid.uuid4()), "kind": kind,
                   "mode": mode, "seed": seed, "rounds": rounds, "tier": opts["--tier"], "buildRole": "dev",
                   "executionCommit": opts["--commit"], "binary": str(binary), "binarySha256": sha(binary.read_bytes()),
                   "commandSha256": sha((out / "command.json").read_bytes()), "status": "failed" if failed else "complete",
                   "exitCode": player_exit, "timedOut": False, "missingArtifacts": [], "hostClean": True if quiet else None,
                   "guard": dict(OK_GUARD, snapshotExit=0, verdict="touched=0"), "artifactSha256": wp078_bot.hash_outputs(out)}
        put_json(out / "attempt.json", attempt)
        return 1 if failed else 0

    def tool_soak_gate_py(self, argv, log_path):
        with log_path.open("a", encoding="utf-8") as handle:
            handle.write("PASS soak STABILITY gate (DEC-091)\n")
        return 0

    def tool_wp078_perf_py(self, argv, _):
        root = Path(argv[argv.index("--out") + 1])
        planned = self.script.get("perf:" + root.name)
        if planned and planned[0] == 65:
            planned.pop(0)
            put(root / "ug" / "ug-perf-sinematik-tp-1" / guard_receipt.MARKER, "NOT restored\n")
            return 65
        sides = [(root, None, None)]
        if "--baseline-out" in argv:   # WP-091 P2: the R1 probe build's windows in their own root, under the R1 commit and build
            r1_binary = wp078_perf.resolve_binary(argv[argv.index("--baseline-bin") + 1])
            sides.append((Path(argv[argv.index("--baseline-out") + 1]), argv[argv.index("--baseline-commit") + 1],
                          sha(r1_binary.read_bytes())))
        extra = [a.split("=", 1)[1] for a in argv if a.startswith("--extra-arg=")]   # B9: -ddPilotStart x,z,yaw
        for side_root, commit, binary_sha in sides:
            shutil.copytree(perf_fixture() / "perf", side_root, dirs_exist_ok=True)
            for attempt_path in side_root.glob("perf-*/attempt.json"):
                data = json.loads(attempt_path.read_text())
                if not self.perf_guard:
                    data.pop("guard", None)             # what wp064_run really writes: no guard receipt in attempt.json
                if commit:
                    data["executionCommit"], data["binarySha256"] = commit, binary_sha
                attempt_path.write_text(json.dumps(data))
                if extra:   # the Player's own argv, as wp064_run writes command.json
                    put_json(attempt_path.parent / "command.json", ["Deaths Demolition", "-ddPilotPerfProbe", "x", *extra])
                if self.perf_ug_dirs:
                    (side_root / "ug" / ("ug-" + attempt_path.parent.name)).mkdir(parents=True, exist_ok=True)   # wp078_perf's guard dirs (not perf-*)
        return 0

    def tool_wp078_perf_report_py(self, argv, _):
        if "--baseline" in argv:   # WP-091 P2: the real comparison on the fixture windows writes r2-P2.json
            return subprocess.run([sys.executable, str(HERE / "wp078_perf_report.py"), *argv[2:]], capture_output=True).returncode
        return 0

    def tool_r2_z3_py(self, argv, _):   # S-05's writer: the chain only calls it (its own tests are S-05's)
        return 0

    def tool_wp078_report_py(self, argv, _):
        return wp078_report.main(argv[2:])


_PERF = {}


def perf_fixture():
    """The report fixture's 27 perf windows + attempts ledger (probe binary b"probe"), built once per test run."""
    if "dir" not in _PERF:
        _PERF["tmp"] = tempfile.TemporaryDirectory()
        RF.EvidenceFixture(Path(_PERF["tmp"].name))
        _PERF["dir"] = Path(_PERF["tmp"].name)
    return _PERF["dir"]


def tearDownModule():
    if "tmp" in _PERF:
        _PERF["tmp"].cleanup()


class FakeHost:
    """SHOULD-6 seam: the r2q host check without ps / sysctl; quiet unless told otherwise."""
    wait_max, interval = 1800.0, 5.0

    def __init__(self, start=None, quiet=True, waited=0.0, samples=None):
        self.start_sample = start or {"load1": 27.0, "foreignUnity": 0, "foreignPlayer": 0, "foreignBlender": 0,
                                      "foreignFfmpeg": 0}
        self.calm, self.waited = quiet, waited
        self.samples = samples if samples is not None else [dict(self.start_sample, t=0.0)]
        self.events = []

    def wait_quiet(self):
        self.events.append("wait")
        return self.start_sample, self.calm, self.waited

    def start(self):
        self.events.append("start")

    def stop(self):
        self.events.append("stop")
        return list(self.samples)


def make_chain(evidence, stages=acc.STAGE_ORDER, tools=None, guard=None, git=None, **kw):
    kw.setdefault("lint", lambda repo: [])   # hermetic: the R2 lint pre-step reads this tree's own contract otherwise
    kw.setdefault("host", FakeHost())        # hermetic: the r2q host check reads this machine's load otherwise
    lines = []
    chain = acc.Chain(FINAL, evidence, list(stages), git=git or FakeGit(), runner=tools or FakeTools(), guard=guard or FakeGuard(),
                      out=lines.append, wait_seconds=0, **kw)
    chain.lines = lines
    return chain


def must_not_run(argv, log_path):
    raise AssertionError("dry-run executed " + " ".join(argv))


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.evidence = Path(self.tmp.name) / "evidence"

    def dry(self, stages=acc.STAGE_ORDER, **kw):
        guard = FakeGuard()
        guard.snapshot = guard.close = None       # never called in a dry run
        chain = make_chain(self.evidence, stages, tools=must_not_run, guard=guard, dry_run=True, **kw)
        self.assertEqual(0, chain.run())
        self.assertFalse(self.evidence.exists(), "a dry run must not create evidence")
        return chain

    @staticmethod
    def opt(argv, name):
        return argv[argv.index(name) + 1]


class DryRunTests(Base):
    def planned(self, stage, script):
        chain = self.dry([stage])
        return [argv for s, argv in chain.planned if s == stage and any(Path(a).name == script for a in argv[:2])]

    def test_builds_three_modes_then_library_provenance_under_editor_guard(self):
        chain = self.dry(["a6"])
        builds = [a for _, a in chain.planned if Path(a[0]).name == "build.sh"]
        self.assertEqual([["dev"], ["release"], ["release-probe"]], [a[1:2] for a in builds])
        self.assertEqual([str(self.evidence.resolve() / f"build-{r}") for r in ("dev", "release", "probe")], [self.opt(a, "--out") for a in builds])
        provenance = [a for _, a in chain.planned if Path(a[1]).name == "library_provenance.py"]
        self.assertEqual([str(self.evidence.resolve() / f"build-{r}" / "library-provenance.json") for r in ("dev", "release", "probe")],
                         [a[3] for a in provenance])
        guards = [a for _, a in chain.planned if a[0] == "/main/tools/userdata-guard.sh"]
        self.assertEqual(6, len(guards))
        self.assertTrue(all(a[-2:] == ["--scope", "editor"] for a in guards))

    def test_a0_library_provenance(self):
        argv = self.planned("a0", "library_provenance.py")
        self.assertEqual([[sys.executable, str(HERE / "library_provenance.py"), str(acc.REPO),
                           str(self.evidence.resolve() / "a0" / "library-provenance.json")]], argv)

    def test_a1_core_runs_exclude_diagnostics_like_main_ci(self):
        # S-01 96d0bfeb / C-00 05.10: main-ci's core EditMode + PlayMode pass --category '!^Diagnostics$'
        with mock.patch.object(acc, "testsh_has_category", return_value=True):
            chain = self.dry(["a1"])
        runs = [argv for _, argv in chain.planned if Path(argv[0]).name == "test.sh"]
        alone = acc.ci_plan()[1]
        self.assertEqual(["editmode", "playmode"] + ["playmode"] * len(alone), [r[1] for r in runs])
        self.assertEqual([acc.CI_CATEGORY, acc.CI_CATEGORY], [self.opt(r, "--category") for r in runs[:2]])
        self.assertTrue(all("--category" not in r for r in runs[2:]))   # alone steps name one test, no category

    def test_a1_targeted_rerun_and_r2_sessions_keep_no_category(self):
        with mock.patch.object(acc, "testsh_has_category", return_value=True):
            chain = self.dry(["a1"], editmode_filter="ProjectIntegrityTests", playmode_filter="CoreCombatTests")
        runs = [argv for _, argv in chain.planned if Path(argv[0]).name == "test.sh"]
        self.assertTrue(runs and all("--category" not in r for r in runs))

    def test_a1_without_category_support_warns_and_runs_unfiltered(self):
        with mock.patch.object(acc, "testsh_has_category", return_value=False):
            chain = self.dry(["a1"])
        runs = [argv for _, argv in chain.planned if Path(argv[0]).name == "test.sh"]
        self.assertTrue(all("--category" not in r for r in runs))
        self.assertIn("Diagnostics tests are NOT excluded", "\n".join(chain.lines))

    def test_a1_suites_editmode_scope_editor_playmode_scope_player(self):
        chain = self.dry(["a1"], editmode_filter="ProjectIntegrityTests|ArsenalV3Tests")
        seq = [(Path(a[0]).name, a[1]) for _, a in chain.planned]
        # R2: core PlayMode, then each ci-isolated `alone` entry in its own guarded session (main-ci parity)
        alone = acc.ci_plan()[1]
        self.assertEqual([("userdata-guard.sh", "snapshot"), ("test.sh", "editmode"), ("userdata-guard.sh", "restore")]
                         + [("userdata-guard.sh", "snapshot"), ("test.sh", "playmode"), ("userdata-guard.sh", "restore")] * (1 + len(alone)), seq)
        # C-00 30.09 lock order player → unity: the PlayMode snapshot takes the player slot before test.sh's Unity slot
        scopes = [argv[-1] for _, argv in chain.planned if Path(argv[0]).name == "userdata-guard.sh"]
        self.assertEqual(["editor", "editor"] + ["player", "player"] * (1 + len(acc.ci_plan()[1])), scopes)
        edit = chain.planned[1][1]
        self.assertEqual("ProjectIntegrityTests|ArsenalV3Tests", self.opt(edit, "--filter"))
        self.assertEqual(acc.ci_plan()[0], self.opt(chain.planned[4][1], "--filter"))   # R2: main-ci core, not the whole suite
        # FINAL 23093940: the full PlayMode suite outgrew test.sh's 45 min default watchdog
        self.assertEqual(str(acc.PLAYMODE_TIMEOUT_MIN), self.opt(chain.planned[4][1], "--timeout-min"))
        self.assertNotIn("--timeout-min", chain.planned[1][1])

    def test_a2_gameplay_five_rounds_seed61_sinematik_and_route_runs(self):
        bots = self.planned("a2", "wp078_bot.py")
        self.assertEqual(6, len(bots))
        gameplay, route = bots[:3], bots[3:]
        for argv, mode in zip(gameplay, acc.MODES):
            self.assertEqual(("gameplay", mode, "61", "5", "sinematik"),
                             tuple(self.opt(argv, k) for k in ("--kind", "--mode", "--seed", "--rounds", "--tier")))
            self.assertEqual(str(self.evidence.resolve() / "build-dev"), self.opt(argv, "--bin"))
            self.assertEqual(FINAL, self.opt(argv, "--commit"))
            self.assertNotIn("--quiet", argv)
        self.assertEqual([("route", m) for m in acc.MODES], [(self.opt(a, "--kind"), self.opt(a, "--mode")) for a in route])
        self.assertEqual(len({self.opt(a, "--out") for a in bots}), 6)

    def test_a3_flag_runs_follow_ai_flags_values_0_1_one_round(self):
        bots = self.planned("a3", "wp078_bot.py")
        self.assertEqual(2 * len(wp078_report.AI_FLAGS), len(bots))
        expected = [(flag, str(value)) for flag in wp078_report.AI_FLAGS for value in (0, 1)]
        got = [tuple(a.split("=", 1)[1] for a in argv if a.startswith("--extra-arg=")) for argv in bots]
        self.assertEqual(expected, got)
        for argv in bots:
            self.assertEqual(("flag", "1", "sinematik"), (self.opt(argv, "--kind"), self.opt(argv, "--rounds"), self.opt(argv, "--tier")))

    def test_a5_stability_seeds_61_26_twelve_rounds_quiet_and_report_runs(self):
        chain = self.dry(["a5"])
        bots = [a for s, a in chain.planned if Path(a[1]).name == "wp078_bot.py"]
        stability = [a for a in bots if self.opt(a, "--kind") == "stability"]
        self.assertEqual(["61", "26"], [self.opt(a, "--seed") for a in stability])
        for argv in stability:
            self.assertEqual(("topdown", "12", "sinematik"), (self.opt(argv, "--mode"), self.opt(argv, "--rounds"), self.opt(argv, "--tier")))
            self.assertIn("--quiet", argv)                      # M-16: clean host, no Unity slots held
            self.assertGreaterEqual(int(self.opt(argv, "--timeout")), 12 * 420)
        reports = [a for a in bots if self.opt(a, "--kind") == "report"]
        counts = {m: sum(1 for a in reports if self.opt(a, "--mode") == m) for m in acc.MODES}
        self.assertEqual({"topdown": 5, "tp": 2, "fp": 2}, counts)
        self.assertFalse(any("--quiet" in a for a in reports))
        gates = [a for s, a in chain.planned if Path(a[1]).name == "soak_gate.py"]
        self.assertEqual(2, len(gates))
        for argv, seed in zip(gates, ("61", "26")):
            self.assertIn("--stability", argv)
            self.assertEqual((seed, "12"), (self.opt(argv, "--seed"), self.opt(argv, "--rounds")))
        self.assertEqual(9 + 2, len(bots))

    def test_a4_perf_on_the_release_probe_build(self):
        perf = self.planned("a4", "wp078_perf.py")
        self.assertEqual(1, len(perf))
        self.assertEqual(str(self.evidence.resolve() / "build-probe"), self.opt(perf[0], "--bin"))
        self.assertEqual(str(self.evidence.resolve() / "a4" / "perf"), self.opt(perf[0], "--out"))
        self.assertEqual(FINAL, self.opt(perf[0], "--commit"))
        self.assertEqual(1, len(self.planned("a4", "wp078_perf_report.py")))

    def test_a8_report_command(self):
        report = self.planned("a8", "wp078_report.py")[0]
        self.assertEqual(str(self.evidence.resolve() / "manifest.json"), self.opt(report, "--manifest"))
        self.assertEqual(str(self.evidence.resolve()), self.opt(report, "--evidence-root"))
        self.assertTrue(Path(self.opt(report, "--out")).name.startswith("report-"))

    def test_stages_run_in_dependency_order(self):
        chain = self.dry(["a8", "a4", "a2", "a6"])
        self.assertEqual(["a6", "a2", "a4", "a8"], chain.stages)
        order = []
        for stage, _ in chain.planned:
            if not order or order[-1] != stage:
                order.append(stage)
        self.assertEqual(["a6", "a2", "a4", "a8"], order)

    def test_cli_dry_run_via_main(self):
        lines = []
        code = acc.main(["--final", FINAL, "--evidence", str(self.evidence), "--stages", "a4", "--dry-run"],
                        git=FakeGit(), runner=must_not_run, guard=FakeGuard(), out=lines.append)
        self.assertEqual(0, code)
        self.assertTrue(any("wp078_perf.py" in line and "build-probe" in line for line in lines))


class RefusalTests(Base):
    def test_head_not_final_refused(self):
        tools = FakeTools()
        chain = make_chain(self.evidence, tools=tools, git=FakeGit(head="b" * 40))
        self.assertEqual(2, chain.run())
        self.assertEqual([], tools.calls)
        self.assertFalse(self.evidence.exists())

    def test_dirty_tree_refused_also_in_dry_run(self):
        chain = make_chain(self.evidence, tools=must_not_run, git=FakeGit(porcelain=" M tools/qa/x.py"), dry_run=True)
        self.assertEqual(2, chain.run())
        self.assertTrue(any("REFUSED" in line for line in chain.lines))

    def test_cli_usage_errors(self):
        for argv in (["--final", "abc"], ["--final", FINAL.upper()], ["--final", FINAL, "--stages", "a7"], ["--final", FINAL, "--stages", ""]):
            with self.subTest(argv=argv):
                self.assertEqual(64, acc.main(argv, git=FakeGit(), runner=must_not_run, guard=FakeGuard(), out=lambda _: None))

    def test_existing_evidence_needs_resume(self):
        (self.evidence / "a2").mkdir(parents=True)
        tools = FakeTools()
        self.assertEqual(64, make_chain(self.evidence, ["a2"], tools=tools).run())
        self.assertEqual([], tools.calls)

    def test_tree_changing_mid_chain_stops(self):
        state = {"n": 0}

        def porcelain():
            state["n"] += 1
            return "" if state["n"] <= 2 else " M Assets/x"
        chain = make_chain(self.evidence, ["a6", "a0"], git=FakeGit(porcelain=porcelain))
        self.assertEqual(2, chain.run())
        self.assertTrue(any("STOP" in line and "report to C-00" in line for line in chain.lines))


class B9PoseTests(Base):
    """C-00 01.10: S-05 B9 pose windows run after A4 on the same probe build, one root per pose, never gating."""
    POSES = [{"name": "sw-corner", "start": "-48,-48,45", "tiers": "yuksek", "modes": "topdown,tp"},
             {"name": "plaza-n", "start": "0,30,180", "tiers": "yuksek", "modes": "tp"}]

    def test_poses_follow_a4_with_their_start_flag(self):
        chain = self.dry(["a4", "b9"], b9_poses=self.POSES)
        perf = [a for _, a in chain.planned if Path(a[1]).name == "wp078_perf.py"]
        self.assertEqual(3, len(perf))
        self.assertEqual(str(self.evidence.resolve() / "a4" / "perf"), self.opt(perf[0], "--out"))
        for argv, pose in zip(perf[1:], self.POSES):
            self.assertEqual(str(self.evidence.resolve() / "b9" / pose["name"]), self.opt(argv, "--out"))
            self.assertEqual(str(self.evidence.resolve() / "build-probe"), self.opt(argv, "--bin"))
            self.assertEqual((pose["tiers"], pose["modes"]), (self.opt(argv, "--tiers"), self.opt(argv, "--modes")))
            self.assertEqual(["--extra-arg=-ddPilotStart", "--extra-arg=" + pose["start"]], argv[-2:])

    def test_no_poses_no_b9(self):
        chain = self.dry(["a4", "b9"])
        self.assertEqual([], [argv for stage, argv in chain.planned if stage == "b9"])   # path-independent (gt N-3)

    def test_poses_without_the_b9_stage_are_refused(self):
        # gt 06.10 RB-1: the R1 FINAL'' form "--stages a4 --b9-poses" ran B9 inside a4; with b9 a stage of its own it
        # would have run no pose at all, silently
        path = Path(self.tmp.name) / "poses.json"
        path.write_text(json.dumps({"poses": self.POSES}))
        base = ["--final", FINAL, "--evidence", str(self.evidence), "--dry-run", "--b9-poses", str(path)]
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            self.assertEqual(64, acc.main(base + ["--stages", "a4"], git=FakeGit(), guard=FakeGuard(), runner=must_not_run))
        self.assertIn("needs the b9 stage", err.getvalue())
        lines = []
        self.assertEqual(0, acc.main(base + ["--stages", "a4,b9"], git=FakeGit(), guard=FakeGuard(), runner=must_not_run,
                                     out=lines.append, lint=lambda repo: []))
        self.assertEqual(len(self.POSES), sum(1 for line in lines if line.startswith("DRY b9:") and "wp078_perf.py" in line))

    def test_pose_file_validation(self):
        path = Path(self.tmp.name) / "poses.json"
        path.write_text(json.dumps({"poses": self.POSES}))
        self.assertEqual(self.POSES, acc.load_b9_poses(path))
        for bad in ([], [dict(self.POSES[0], start="-48,-48")], [dict(self.POSES[0], name="SW corner")],
                    [self.POSES[0], self.POSES[0]], [dict(self.POSES[0], extra="x")]):
            with self.subTest(bad=bad):
                path.write_text(json.dumps({"poses": bad}))
                with self.assertRaises(ValueError):
                    acc.load_b9_poses(path)


R1 = "d" * 40


def r1_evidence(base, commit=R1, binary=b"r1probe", recorded=None, status="complete", mode="release-probe", tamper=None):
    """An R1 FINAL evidence root as wp078_accept left it (01.10): build-probe with its build report and WP-079H app
    manifest, and the unit record that hash-binds the binary and the build report."""
    root = Path(base) / "r1-evidence"
    build, units = root / "build-probe", root / "stages" / "units"
    exe = build / "DeathsDemolition.app" / "Contents" / "MacOS" / EXE
    data = build / "DeathsDemolition.app" / "Contents" / "Resources" / "data.unity3d"
    put(exe, binary)
    put(data, b"r1 content")
    put_json(build / "build-report.json", {"mode": mode, "result": "Succeeded", "stamp": {"commit": commit[:12], "mode": mode}})
    acc._app_manifest().write(str(build), "DeathsDemolition.app")
    put_json(units / "build-probe.json", {"schema": acc.RECORD_SCHEMA, "unit": "build-probe", "executionCommit": commit,
                                          "status": status, "binarySha256": recorded or sha(binary),
                                          "artifactSha256": acc.hash_map(units, [exe, build / "build-report.json"])})
    if tamper == "content":
        put(data, b"changed after the build")
    elif tamper == "record":
        (units / "build-probe.json").unlink()
    elif tamper == "report":
        put_json(build / "build-report.json", {"mode": mode, "stamp": {"commit": commit[:12]}, "edited": True})
    return root


class P2BaselineTests(Base):
    """WP-091 P2 (C-00 05.10): an R2 round's A4 alternates the R1 FINAL probe build with FINAL's and writes r2-P2.json."""

    def planned_a4(self, **kw):
        chain = self.dry(["a4"], **kw)
        perf = [a for s, a in chain.planned if s == "a4" and Path(a[1]).name == "wp078_perf.py"]
        report = [a for s, a in chain.planned if s == "a4" and Path(a[1]).name == "wp078_perf_report.py"]
        return chain, perf[0], report[0]

    def test_an_r2_round_alternates_the_r1_probe_build_and_writes_p2(self):
        r1 = r1_evidence(self.tmp.name)
        _, perf, report = self.planned_a4(round_name="R2", r1_evidence=r1, r1_commit=R1)
        e = self.evidence.resolve()
        self.assertEqual(str(e / "a4" / "perf"), self.opt(perf, "--out"))   # the gate root is unchanged
        self.assertEqual((str(r1.resolve() / "build-probe"), str(e / "a4" / "perf-r1"), R1),
                         (self.opt(perf, "--baseline-bin"), self.opt(perf, "--baseline-out"), self.opt(perf, "--baseline-commit")))
        self.assertEqual((str(e / "a4" / "perf"), str(e / "a4" / "perf-r1"), str(e / "r2" / "r2-P2.json"), FINAL, R1, sha(b"r1probe")),
                         (report[2], self.opt(report, "--baseline"), self.opt(report, "--r2-measure"), self.opt(report, "--commit"),
                          self.opt(report, "--baseline-commit"), self.opt(report, "--baseline-sha256")))

    def test_an_r1_round_never_alternates(self):
        _, perf, report = self.planned_a4(round_name="R1", r1_evidence=r1_evidence(self.tmp.name), r1_commit=R1)
        self.assertNotIn("--baseline-bin", perf)
        self.assertNotIn("--r2-measure", report)

    def test_without_an_r1_evidence_p2_is_blocked_by_name(self):
        chain, perf, report = self.planned_a4(round_name="R2")
        self.assertNotIn("--baseline-bin", perf)
        self.assertNotIn("--baseline", report)
        self.assertTrue(any("P2 BLOCKED" in line for line in chain.lines), chain.lines)

    def test_an_r1_build_that_does_not_prove_itself_is_never_planned_even_in_a_dry_run(self):
        # KADRO test-engineer SHOULD-4/-5 and mutant M1: every condition of the proof, checked before any window
        cases = {"built at": dict(commit="e" * 40), "not the one the unit record holds": dict(recorded="0" * 64),
                 "status 'failed'": dict(status="failed"), "no unit record": dict(tamper="record"),
                 "a file of the .app changed": dict(tamper="content"), "hash-bound changed": dict(tamper="report"),
                 "not a release-probe": dict(mode="release")}
        for reason, kw in cases.items():
            with self.subTest(reason=reason):
                self.setUp()
                chain, perf, report = self.planned_a4(round_name="R2", r1_evidence=r1_evidence(self.tmp.name, **kw), r1_commit=R1)
                self.assertNotIn("--baseline-bin", perf)
                self.assertNotIn("--r2-measure", report)
                self.assertTrue(any("not the R1 FINAL's proven" in line and reason in line for line in chain.lines), chain.lines)

    def test_an_unproven_r1_build_never_runs_in_a_real_chain_either(self):
        tools = FakeTools()
        make_chain(self.evidence, ["a6", "a4"], tools=tools, round_name="R2",
                   r1_evidence=r1_evidence(self.tmp.name, tamper="content"), r1_commit=R1).run()
        perf = [c for c in tools.calls if c[0] == sys.executable and Path(c[1]).name == "wp078_perf.py"]
        self.assertEqual(1, len(perf))
        self.assertNotIn("--baseline-bin", perf[0])
        self.assertIn("a file of the .app changed", (self.evidence / "accept.log").read_text())
        self.assertFalse((self.evidence / "a4" / "perf-r1").exists())
        self.assertFalse((self.evidence / "r2" / "r2-P2.json").exists())

    def test_this_finals_own_evidence_or_commit_is_refused_as_r1(self):
        # KADRO test-engineer SHOULD-1: P2 would compare FINAL with itself, and the report would refuse the root hours later
        r1 = r1_evidence(self.tmp.name)
        for args in (["--r1-evidence", str(r1), "--r1-commit", FINAL], ["--r1-evidence", str(self.evidence), "--r1-commit", R1]):
            with self.subTest(args=args):
                self.assertEqual(64, acc.main(["--final", FINAL, "--evidence", str(self.evidence), "--round", "R2", "--stages", "a4",
                                               "--dry-run", *args]))

    def test_a_resumed_a4_keeps_the_baseline_it_was_recorded_with(self):
        # KADRO test-engineer SHOULD-2: never two R1 baselines in one report, never a baseline root nobody declares
        r1 = r1_evidence(self.tmp.name)
        self.assertNotEqual(64, make_chain(self.evidence, ["a6", "a4"], round_name="R2", r1_evidence=r1, r1_commit=R1).run())
        other = r1_evidence(Path(self.tmp.name) / "other", commit="e" * 40)
        for kw, reason in ((dict(round_name="R2", r1_evidence=other, r1_commit="e" * 40), "recorded with R1 baseline " + R1),
                           (dict(round_name="R2"), "recorded with R1 baseline " + R1)):
            with self.subTest(**{k: str(v) for k, v in kw.items()}):
                self.assertEqual(64, make_chain(self.evidence, ["a4"], resume=True, **kw).run())
                self.assertIn(reason, (self.evidence / "accept.log").read_text())
        self.stage_record = self.evidence / "stages" / "a4.json"
        self.stage_record.rename(self.stage_record.with_name("a4.json.moved"))   # no record, but the baseline root stays
        self.assertEqual(64, make_chain(self.evidence, ["a4"], resume=True, round_name="R2").run())
        self.assertIn("perf-r1 exists but this run gives no --r1-evidence", (self.evidence / "accept.log").read_text())

    def test_an_earlier_p2_and_p5_are_retired_before_they_are_computed_again(self):
        # gt 06.10 N-1 / S-05: a re-evaluation that writes nothing must leave nothing behind for the report to bind
        put_json(self.evidence / "r2" / "r2-P2.json", {"gate": "P2", "stale": True})
        put_json(self.evidence / "r2" / "r2-P5.json", {"gate": "P5", "stale": True})
        r1 = r1_evidence(self.tmp.name)
        chain = make_chain(self.evidence, ["a6", "a4"], round_name="R2", r1_evidence=r1, r1_commit=R1, resume=True)
        chain.run()
        self.assertFalse(json.loads((self.evidence / "r2" / "r2-P2.json").read_text()).get("stale"))   # recomputed
        self.assertEqual(1, len(list((self.evidence / "r2").glob("r2-P2.json.stale-*"))))
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            make_chain(self.evidence, ["r2"], round_name="R2", r1_evidence=r1, r1_commit=R1, resume=True).run()
        self.assertFalse((self.evidence / "r2" / "r2-P5.json").exists())   # no samples this time: nothing bound
        self.assertEqual(1, len(list((self.evidence / "r2").glob("r2-P5.json.stale-*"))))
        self.assertIn("RETIRED r2/r2-P5.json", (self.evidence / "accept.log").read_text())

    def test_a_rerun_without_a_baseline_retires_the_earlier_p5_too(self):
        # gt 06.10 N-11: the early return without --r1-evidence used to leave the earlier r2-P5.json for the report to bind
        put_json(self.evidence / "r2" / "r2-P5.json", {"gate": "P5", "stale": True})
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            make_chain(self.evidence, ["r2"], round_name="R2", resume=True).run()
        self.assertFalse((self.evidence / "r2" / "r2-P5.json").exists())
        self.assertEqual(1, len(list((self.evidence / "r2").glob("r2-P5.json.stale-*"))))
        self.assertIn("P5 BLOCKED", (self.evidence / "accept.log").read_text())

    def test_two_retirements_in_one_second_keep_both(self):
        # gt 06.10 N-11: the .stale-<utc> name has a one-second resolution
        chain = make_chain(self.evidence, ["r2"], round_name="R2")
        r2 = chain.evidence / "r2"
        with mock.patch.object(acc.time, "strftime", return_value="20261006T080000Z"):
            for n in (1, 2, 3):
                put_json(r2 / "r2-P5.json", {"gate": "P5", "n": n})
                chain.retire(r2 / "r2-P5.json")
        kept = sorted(p.name for p in r2.glob("r2-P5.json.stale-*"))
        self.assertEqual(["r2-P5.json.stale-20261006T080000Z", "r2-P5.json.stale-20261006T080000Z-1",
                          "r2-P5.json.stale-20261006T080000Z-2"], kept)
        self.assertEqual([1, 2, 3], [json.loads((r2 / k).read_text())["n"] for k in kept])

    def p5_run(self, r1, commit):
        for root, seconds in ((self.evidence, 1.30), (r1, 1.22)):
            put_json(Path(root) / "a2" / "gameplay-tp" / "ui-data" / "load-timing.json",
                     {"Version": 1, "Scenes": ["Selection", "Run"], "Seconds": [1.2, seconds]})
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            make_chain(self.evidence, ["r2"], round_name="R2", r1_evidence=r1, r1_commit=commit).run()
        self.assertTrue((self.evidence / "r2" / "r2-P5.json").is_file())

    def test_p5_and_p2_never_rest_on_two_baselines(self):
        # gt 06.10 N-11: the 64 that kept a resumed A4 on its baseline (SHOULD-2) now also binds P5 - both ways
        r1 = r1_evidence(self.tmp.name)
        self.p5_run(r1, R1)
        self.assertEqual(R1, json.loads((self.evidence / "stages" / "r2.json").read_text())["p5BaselineCommit"])
        other = r1_evidence(Path(self.tmp.name) / "other", commit="e" * 40)
        for kw in (dict(), dict(r1_evidence=other, r1_commit="e" * 40)):
            with self.subTest(baseline=kw.get("r1_commit")):
                self.assertEqual(64, make_chain(self.evidence, ["a4"], resume=True, round_name="R2", **kw).run())
                self.assertIn("r2 was recorded with R1 baseline " + R1, (self.evidence / "accept.log").read_text())
                self.assertIn("P5 would rest on another baseline", (self.evidence / "accept.log").read_text())
        self.assertNotEqual(64, make_chain(self.evidence, ["a6", "a4"], resume=True, round_name="R2", r1_evidence=r1,
                                           r1_commit=R1).run())   # the same baseline: A4 runs
        steps = []
        for kw in (dict(), dict(r1_evidence=other, r1_commit="e" * 40)):
            with self.subTest(baseline=kw.get("r1_commit"), stage="r2"):
                with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: steps.append(1) or []):
                    self.assertEqual(64, make_chain(self.evidence, ["r2"], resume=True, round_name="R2", **kw).run())
                self.assertIn("a4 was recorded with R1 baseline " + R1, (self.evidence / "accept.log").read_text())
        self.assertEqual([], steps)   # stopped before any measurement session
        self.assertTrue((self.evidence / "r2" / "r2-P5.json").is_file())   # and before P5 was retired

    def test_another_finals_a4_record_does_not_hide_a_baseline_root(self):
        # gt 06.10 KP-3: before 3959a1d5 an a4 record of another FINAL counted as none, so a4/perf-r1 without a baseline 64'd
        put_json(self.evidence / "stages" / "a4.json", {"executionCommit": "f" * 40, "baselineCommit": R1})
        (self.evidence / "a4" / "perf-r1").mkdir(parents=True)
        for stage in ("a4", "r2"):
            with self.subTest(stage=stage):
                with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
                    self.assertEqual(64, make_chain(self.evidence, [stage], resume=True, round_name="R2").run())
                self.assertIn("perf-r1 exists but this run gives no --r1-evidence", (self.evidence / "accept.log").read_text())

    def test_a_dry_run_never_retires(self):
        # gt 06.10 KP-9: P5's retirement now comes before the dry-run return, so retire's own guard is what keeps the file
        put_json(self.evidence / "r2" / "r2-P5.json", {"gate": "P5"})
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            make_chain(self.evidence, ["r2"], round_name="R2", dry_run=True).run()
        self.assertTrue((self.evidence / "r2" / "r2-P5.json").is_file())
        self.assertEqual([], list((self.evidence / "r2").glob("r2-P5.json.stale-*")))

    def test_a_run_without_resume_on_a_p5_of_another_baseline_stops_too(self):
        # gt 06.10 KP-18: the existing-evidence refusal sees only the selected stages' outputs, so `--stages a4` without
        # --resume used to run A4 on baseline Y next to a P5 made on R1
        r1 = r1_evidence(self.tmp.name)
        self.p5_run(r1, R1)
        other = r1_evidence(Path(self.tmp.name) / "other", commit="e" * 40)
        self.assertEqual(64, make_chain(self.evidence, ["a4"], round_name="R2", r1_evidence=other, r1_commit="e" * 40).run())
        self.assertIn("r2 was recorded with R1 baseline " + R1, (self.evidence / "accept.log").read_text())
        self.assertFalse((self.evidence / "a4").exists())

    def test_an_unproven_r1_build_still_records_the_baseline_the_run_asked_for(self):
        # gt 06.10 KP-4: A4 on an R1 build that does not prove itself writes no baselineCommit; the same baseline must not
        # read as "None" to the next run (it used to 64), another one still stops
        r1 = r1_evidence(self.tmp.name, tamper="content")
        make_chain(self.evidence, ["a6", "a4"], round_name="R2", r1_evidence=r1, r1_commit=R1).run()
        record = json.loads((self.evidence / "stages" / "a4.json").read_text())
        self.assertEqual((R1, None), (record["baselineRequested"], record.get("baselineCommit")))
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            self.assertNotEqual(64, make_chain(self.evidence, ["r2"], round_name="R2", r1_evidence=r1, r1_commit=R1).run())
            other = r1_evidence(Path(self.tmp.name) / "other", commit="e" * 40)
            self.assertEqual(64, make_chain(self.evidence, ["r2"], round_name="R2", resume=True, r1_evidence=other,
                                            r1_commit="e" * 40).run())
        self.assertIn("a4 was recorded with R1 baseline " + R1, (self.evidence / "accept.log").read_text())

    def test_an_a4_record_older_than_baseline_requested_falls_back_to_baseline_commit(self):
        put_json(self.evidence / "stages" / "a4.json", {"executionCommit": FINAL, "baselineCommit": R1})
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            self.assertEqual(64, make_chain(self.evidence, ["r2"], resume=True, round_name="R2").run())
        self.assertIn("a4 was recorded with R1 baseline " + R1, (self.evidence / "accept.log").read_text())

    def test_an_r2_run_that_wrote_no_p5_binds_no_baseline(self):
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            make_chain(self.evidence, ["r2"], round_name="R2", r1_evidence=Path(self.tmp.name), r1_commit="e" * 40).run()
        self.assertNotIn("p5BaselineCommit", json.loads((self.evidence / "stages" / "r2.json").read_text()))
        r1 = r1_evidence(self.tmp.name)
        self.assertNotEqual(64, make_chain(self.evidence, ["a6", "a4"], resume=True, round_name="R2", r1_evidence=r1,
                                           r1_commit=R1).run())

    def test_the_a4_record_binds_both_roots_and_the_r1_build(self):
        tools = FakeTools()
        r1 = r1_evidence(self.tmp.name)
        chain = make_chain(self.evidence, ["a6", "a4"], tools=tools, round_name="R2", r1_evidence=r1, r1_commit=R1)
        chain.run()
        self.assertEqual("PASS", chain.verdicts["a4"])
        record = json.loads((self.evidence / "stages" / "a4.json").read_text())
        self.assertEqual(("a4/perf-r1", R1, sha(b"r1probe"), sha((r1 / "build-probe" / "app-manifest.sha256").read_bytes()), True),
                         (record["baselineRoot"], record["baselineCommit"], record["baselineBinarySha256"],
                          record["baselineAppManifestSha256"], record["baselineInterleavedFromStart"]))
        bound = record["artifactSha256"]
        windows = sum(1 for k in bound if k.startswith("../a4/perf/perf-") and k.endswith("/attempt.json"))
        self.assertEqual(windows, sum(1 for k in bound if k.startswith("../a4/perf-r1/perf-") and k.endswith("/attempt.json")))
        self.assertIn("../a4/perf-r1/attempts-ledger.jsonl", bound)
        self.assertIn("../r2/r2-P2.json", bound)
        p2 = json.loads((self.evidence / "r2" / "r2-P2.json").read_text())
        self.assertEqual((2 * windows, R1, sha(b"r1probe")), (p2["samples"], p2["baselineCommit"], p2["baselineBinarySha256"]))
        self.assertTrue(any(k.endswith("build-probe/DeathsDemolition.app/Contents/MacOS/" + EXE) and "r1-evidence" in k
                            for k in record["inputs"]), record["inputs"])
        self.assertTrue(chain.stage_passed("a4"))   # --resume re-verifies every bound file, the R1 binary included


# Z3_START differs from r2_z3.py's own "0,-4,0", so a start hard-coded in the chain is told apart (gt 06.10 N-5)
FAKE_Z3 = SimpleNamespace(Z3_START="3,-7,45", Z3_TIER="sinematik", Z3_MODES=("topdown", "tp"), MIN_PAIRS=5,
                          Z3_R2="z3/perf", Z3_R1="z3/perf-r1")


class Z3StageTests(Base):
    """WP-091 Z3 + P3 (C-00 06.10 §20): the chain runs S-05's fixed-pose matrix from r2_z3.py's own constants and calls it."""

    def planned(self, z3=FAKE_Z3, **kw):
        with mock.patch.object(acc, "_r2_z3", return_value=z3):
            chain = self.dry(["z3"], **kw)
        perf = [a for s, a in chain.planned if s == "z3" and Path(a[1]).name == "wp078_perf.py"]
        tool = [a for s, a in chain.planned if s == "z3" and Path(a[1]).name == "r2_z3.py"]
        return chain, perf, tool

    def test_the_fixed_pose_pairs_come_from_r2_z3_and_alternate_with_the_proven_r1_build(self):
        r1 = r1_evidence(self.tmp.name)
        _, perf, tool = self.planned(round_name="R2", r1_evidence=r1, r1_commit=R1, b9_poses_path="/k/b9-poses.json")
        e = self.evidence.resolve()
        self.assertEqual(1, len(perf))
        argv = perf[0]
        self.assertEqual((str(e / "z3" / "perf"), str(e / "z3" / "perf-r1"), R1, "sinematik", "topdown,tp", "5"),
                         (self.opt(argv, "--out"), self.opt(argv, "--baseline-out"), self.opt(argv, "--baseline-commit"),
                          self.opt(argv, "--tiers"), self.opt(argv, "--modes"), self.opt(argv, "--windows")))
        self.assertEqual(["--extra-arg=-ddPilotStart", "--extra-arg=3,-7,45", "--extra-arg=-ddArmor", "--extra-arg=all"], argv[-4:])
        self.assertEqual([str(e), FINAL, R1, "/k/b9-poses.json"],
                         [self.opt(tool[0], k) for k in ("--evidence", "--commit", "--r1-commit", "--b9-poses")])

    def test_without_r2_z3_nothing_runs_and_z3_p3_stay_blocked_by_name(self):
        chain, perf, tool = self.planned(z3=None, round_name="R2", r1_evidence=r1_evidence(self.tmp.name), r1_commit=R1)
        self.assertEqual(([], []), (perf, tool))
        self.assertTrue(any("r2_z3.py is not in the FINAL tree" in line for line in chain.lines), chain.lines)

    def test_no_pose_constant_or_no_r1_proof_runs_no_window_but_still_calls_the_writer(self):
        no_start = SimpleNamespace(**{k: v for k, v in vars(FAKE_Z3).items() if k != "Z3_START"})
        for z3, kw in ((no_start, dict(r1_evidence=r1_evidence(self.tmp.name), r1_commit=R1)), (FAKE_Z3, {})):
            with self.subTest(start=hasattr(z3, "Z3_START"), r1=bool(kw)):
                self.setUp()
                _, perf, tool = self.planned(z3=z3, round_name="R2", **kw)
                self.assertEqual([], perf)
                self.assertEqual(1, len(tool))   # P3 from the B9 windows does not need the R1 build

    def test_an_r1_round_skips_z3(self):
        chain, perf, tool = self.planned(round_name="R1")
        self.assertEqual(([], []), (perf, tool))

    def test_the_z3_roots_reach_the_report_as_declared_secondary_roots(self):
        with mock.patch.object(acc, "_r2_z3", return_value=FAKE_Z3):
            code = make_chain(self.evidence, [s for s in acc.STAGE_ORDER if s != "a8"], round_name="R2",
                              r1_evidence=r1_evidence(self.tmp.name), r1_commit=R1).run()
        self.assertNotIn(code, (2, 7, 64, 65))
        record = json.loads((self.evidence / "stages" / "z3.json").read_text())
        self.assertEqual("RECORDED", record["verdict"])
        make_chain(self.evidence, ["a8"], resume=True, round_name="R2").run()
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        roots = {r["path"]: r for r in manifest["checklist"]["A4"]["secondaryRoots"]}
        self.assertEqual({"role": "z3-pose", "path": "z3/perf", "start": "3,-7,45"}, roots["z3/perf"])
        self.assertEqual(("r1-baseline", R1, sha(b"r1probe")), (roots["z3/perf-r1"]["role"], roots["z3/perf-r1"]["commit"],
                                                                roots["z3/perf-r1"]["binarySha256"]))
        report = json.loads(sorted(self.evidence.glob("report-*/wp078-report.json"))[-1].read_text())
        self.assertEqual([], [f for f in report["failures"] if f["step"] == "A4"])


def r2_gates_lint_finding(level, code, gate):
    return acc.r2_gates_lint.Finding(level, code, gate, "test finding")


class HostWatchTests(unittest.TestCase):
    """SHOULD-6: the host state counts only foreign processes; the wait gives up after wait_max (no real ps / sysctl)."""

    def ps(self, rows, load="{ 7.50 6.00 5.00 }"):
        me = os.getpid()
        table = "\n".join(f"{pid} {ppid} {comm}" for pid, ppid, comm in rows(me))

        def run(argv, **kw):
            return SimpleNamespace(stdout=table if argv[0] == "ps" else load, returncode=0)
        return run

    def test_the_chains_own_unity_is_not_foreign(self):
        rows = lambda me: [(me, 1, "python3"), (900001, me, "/bin/bash"),
                           (900002, 900001, "/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity"),
                           (900003, 1, "/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity"),
                           (900004, 1, "/x/DD.app/Contents/MacOS/Deaths Demolition - Playable Concept"),
                           (900005, 1, "/Applications/Blender.app/Contents/MacOS/Blender"),
                           (900006, 1, "/opt/homebrew/bin/ffmpeg"), (900007, 900001, "/opt/homebrew/bin/ffmpeg"),
                           (900008, 1, "ffmpeg")]   # gt Ek 4: started from PATH, `ps comm` is the bare name
        with mock.patch.object(acc.subprocess, "run", side_effect=self.ps(rows)):
            state = acc.HostWatch().state()
        self.assertEqual({"load1": 7.5, "foreignUnity": 1, "foreignPlayer": 1, "foreignBlender": 1, "foreignFfmpeg": 2}, state)

    def test_an_unreadable_load_average_is_reported_not_judged(self):
        # gt Ek 4: ps read, sysctl not - the load is reported only (C-00 06.10), so the host is still quiet
        rows = lambda me: [(me, 1, "python3")]
        ps = self.ps(rows)

        def run(argv, **kw):
            if argv[0] == "sysctl":
                raise OSError("sysctl: not permitted")
            return ps(argv, **kw)
        with mock.patch.object(acc.subprocess, "run", side_effect=run):
            state = acc.HostWatch().state()
        self.assertEqual({"load1": None, "foreignUnity": 0, "foreignPlayer": 0, "foreignBlender": 0, "foreignFfmpeg": 0}, state)
        self.assertTrue(acc.HostWatch.quiet(state))

    def test_any_foreign_heavy_process_during_the_session_makes_it_dirty(self):
        # gt Ek 4: the core of C-00's criterion - each of the four kinds, alone, during a session that started quiet
        free = {"load1": 27.0, "foreignUnity": 0, "foreignPlayer": 0, "foreignBlender": 0, "foreignFfmpeg": 0}
        for key, name in (("foreignUnity", "Unity 1"), ("foreignPlayer", "Player 1"), ("foreignBlender", "Blender 1"),
                          ("foreignFfmpeg", "ffmpeg 1")):
            with self.subTest(heavy=key):
                record = acc.host_record({"id": "r2-S1", "gates": ["S1"]}, free, True, 0.0,
                                         [dict(free, t=0.0), dict(free, t=5.0, **{key: 1}), dict(free, t=10.0)])
                self.assertTrue(record["dirty"])
                self.assertEqual(1, record[key + "Max"])
                self.assertIn(name, record["dirtyWhy"][0])
        clean = acc.host_record({"id": "r2-S1", "gates": ["S1"]}, free, True, 0.0, [dict(free, t=0.0, load1=54.0)])
        self.assertEqual((False, 54.0), (clean["dirty"], clean["load1Max"]))   # load alone never

    def test_the_wait_ends_quiet_or_after_wait_max(self):
        free = {"foreignUnity": 0, "foreignPlayer": 0, "foreignBlender": 0, "foreignFfmpeg": 0}
        states = iter([dict(free, load1=2.0, foreignUnity=1), dict(free, load1=2.0, foreignFfmpeg=1), dict(free, load1=54.0)])
        watch = acc.HostWatch(wait_max=1800.0, sleep=lambda s: None)
        watch.state = lambda: next(states)
        self.assertEqual((dict(free, load1=54.0), True, 30.0), watch.wait_quiet())   # C-00 06.10: load1 54 alone is quiet
        for key in acc.HOST_HEAVY:
            with self.subTest(heavy=key):
                self.assertFalse(watch.quiet(dict(free, load1=0.5, **{key: 1})))
        self.assertFalse(watch.quiet(dict(free, foreignBlender=None)))
        watch = acc.HostWatch(wait_max=30.0, sleep=lambda s: None)
        watch.state = lambda: dict(free, load1=1.0, foreignPlayer=1)
        self.assertEqual((dict(free, load1=1.0, foreignPlayer=1), False, 30.0), watch.wait_quiet())

    def test_an_unreadable_host_is_recorded_never_quiet_and_never_a_crash(self):
        # gt 06.10 KP-11: a failing ps / sysctl used to raise out of r2q, or end the sampler thread silently
        watch = acc.HostWatch(wait_max=30.0, sleep=lambda s: None)
        with mock.patch.object(acc.subprocess, "run", side_effect=OSError("ps: not permitted")):
            start, calm, waited = watch.wait_quiet()
        self.assertEqual((False, 30.0, "OSError: ps: not permitted"), (calm, waited, start["error"]))
        free = {"foreignUnity": 0, "foreignPlayer": 0, "foreignBlender": 0, "foreignFfmpeg": 0}
        calls = iter([dict(free, load1=1.0), ValueError("bad loadavg")] + [dict(free, load1=2.0)] * 100)

        def state():
            value = next(calls)
            if isinstance(value, Exception):
                raise value
            return value
        watch = acc.HostWatch(interval=0.01)
        watch.state = state
        watch.start()
        time.sleep(0.1)
        samples = watch.stop()
        self.assertGreaterEqual(len(samples), 3)   # the sampler went on after the error
        self.assertEqual("ValueError: bad loadavg", samples[1]["error"])
        step = {"id": "r2-S3", "gates": ["S3"]}
        record = acc.host_record(step, start, calm, waited, samples)
        self.assertTrue(record["dirty"])
        self.assertEqual((1, 2.0), (record["unreadableSamples"], record["load1Max"]))
        self.assertEqual(["start not quiet after 30 s: OSError: ps: not permitted",
                          f"host unreadable in 1 of {len(samples)} samples"], record["dirtyWhy"])

    def test_a_session_without_a_host_sample_is_not_clean(self):
        # gt 06.10 KP-8: a quiet start with no sample during the session is unwatched, not quiet
        start = {"load1": 1.0, "foreignUnity": 0, "foreignPlayer": 0, "foreignBlender": 0, "foreignFfmpeg": 0}
        record = acc.host_record({"id": "r2-S1", "gates": ["S1"]}, start, True, 0.0, [])
        self.assertEqual((True, ["no host sample was taken during the session"], 1.0),
                         (record["dirty"], record["dirtyWhy"], record["load1Max"]))

    def test_the_sampler_runs_until_stopped(self):
        watch = acc.HostWatch(interval=0.01)
        watch.state = lambda: {"load1": 1.0, "foreignUnity": 0, "foreignPlayer": 0, "foreignBlender": 0, "foreignFfmpeg": 0}
        watch.start()
        time.sleep(0.1)
        samples = watch.stop()
        self.assertGreaterEqual(len(samples), 2)
        self.assertEqual(samples, watch.stop())   # stopping twice is harmless


class R2StageTests(Base):
    """WP-091: the owners' measurement sessions run with DD_R2_MEASURE_DIR / DD_R2_COMMIT; quiet ones only in r2q."""
    STEPS = {False: [{"id": "r2-L1", "suite": "playmode", "filter": "LocomotionR2Tests", "quiet": False, "gates": ["L1", "L2"]},
                     {"id": "r2-U1", "suite": "editmode", "filter": "UiTextInventoryR2", "quiet": False, "gates": ["U1"]}],
             True: [{"id": "r2-S3", "suite": "playmode", "filter": "AudioR2Dense40", "quiet": True, "gates": ["S3"]}]}

    @staticmethod
    def no_unset(argv):
        """the session argv without its leading `-u NAME` pairs (owner switches cleared, tested on their own)"""
        out, i = [argv[0]], 1
        while i < len(argv) and argv[i] == "-u":
            i += 2
        return out + argv[i:]

    def planned_r2(self, stages, round_name):
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: self.STEPS[quiet]):
            chain = self.dry(stages, round_name=round_name)
        return [self.no_unset(argv) for s, argv in chain.planned if s in ("r2", "r2q") and Path(argv[0]).name == "env"]

    def test_r2_runs_each_session_with_the_measure_env_and_its_filter(self):
        runs = self.planned_r2(["r2"], "R2")
        self.assertEqual(2, len(runs))
        measure_dir = str(self.evidence.resolve() / "r2")
        self.assertEqual(["DD_R2_MEASURE_DIR=" + measure_dir, "DD_R2_COMMIT=" + FINAL], runs[0][1:3])
        self.assertEqual(("playmode", "LocomotionR2Tests"), (runs[0][4], self.opt(runs[0], "--filter")))
        self.assertEqual(str(self.evidence.resolve() / "r2" / "r2-L1"), self.opt(runs[0], "--out"))
        self.assertEqual("editmode", runs[1][4])

    def test_owner_switches_follow_the_measure_env(self):
        steps = {False: [], True: [{"id": "r2-S1", "suite": "playmode", "filter": "Kit", "quiet": True, "gates": ["S1", "S2"],
                                    "env": {"DD_KIT_VARIANTS": "A", "DD_KIT_WEAPONS": "all"}}]}
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: steps[quiet]):
            chain = self.dry(["r2q"], round_name="R2")
        run = self.no_unset([argv for s, argv in chain.planned if s == "r2q" and Path(argv[0]).name == "env"][0])
        self.assertEqual(["DD_KIT_VARIANTS=A", "DD_KIT_WEAPONS=all"], run[3:5])
        self.assertEqual("test.sh", Path(run[5]).name)

    def test_the_kproj_placeholder_becomes_the_dd_paths_root(self):
        steps = {False: [], True: [{"id": "r2-S1", "suite": "playmode", "filter": "Kit", "quiet": True, "gates": ["S1"],
                                    "env": {"DD_AP027_DIR": "{KPROJ}/asset-production/AP-027"}}]}
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: steps[quiet]):
            chain = self.dry(["r2q"], round_name="R2")
        run = self.no_unset([argv for s, argv in chain.planned if s == "r2q" and Path(argv[0]).name == "env"][0])
        self.assertEqual("DD_AP027_DIR=" + str(acc.dd_paths.KPROJ) + "/asset-production/AP-027", run[3])

    def test_each_session_starts_with_every_other_owner_switch_unset(self):
        # gt 06.10 B6: an exported DD_HOLD_IK must not reach the H1 session; a session keeps only the switches it declares
        steps = {False: [{"id": "r2-H1", "suite": "playmode", "filter": "H1", "quiet": False, "gates": ["H1"]}],
                 True: [{"id": "r2-S1", "suite": "playmode", "filter": "Kit", "quiet": True, "gates": ["S1"],
                         "env": {"DD_KIT_WEAPONS": "all"}}]}
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: steps[quiet]):
            chain = self.dry(["r2", "r2q"], round_name="R2")
        h1, s1 = [argv for s, argv in chain.planned if Path(argv[0]).name == "env"]
        unset = lambda argv: {argv[i + 1] for i, a in enumerate(argv) if a == "-u"}
        self.assertTrue({"DD_HOLD_IK", "DD_EDC_WEAPONS", "DD_KIT_WEAPONS", "DD_KIT_VARIANTS", "DD_AP027_DIR"} <= unset(h1), h1)
        self.assertNotIn("DD_KIT_WEAPONS", unset(s1))
        self.assertIn("DD_HOLD_IK", unset(s1))
        self.assertIn("DD_KIT_WEAPONS=all", s1)

    def test_an_r2_final_with_a_lint_error_starts_nothing(self):
        tools = FakeTools()
        error = r2_gates_lint_finding("ERROR", "KEY-MISSING", "U1")
        chain = make_chain(self.evidence, ["a6"], tools=tools, round_name="R2", lint=lambda repo: [error])
        self.assertEqual(3, chain.run())
        self.assertEqual([], tools.calls)
        self.assertFalse(self.evidence.exists())
        self.assertTrue(any("r2_gates_lint ERROR 1" in line and "KEY-MISSING U1" in line for line in chain.lines), chain.lines)

    def test_the_lint_warnings_are_kept_beside_the_evidence_and_an_r1_round_skips_the_lint(self):
        warn = r2_gates_lint_finding("WARN", "NO-WRITER", "L1")
        make_chain(self.evidence, ["a6"], round_name="R2", lint=lambda repo: [warn]).run()
        kept = json.loads((self.evidence / "r2-gates-lint.json").read_text())
        self.assertEqual(({"ERROR": 0, "WARN": 1}, FINAL), (kept["counts"], kept["commit"]))
        self.assertIn("R2 lint: ERROR 0 · WARN 1: NO-WRITER L1", (self.evidence / "accept.log").read_text())
        called = []
        make_chain(Path(self.tmp.name) / "r1-round", ["a6"], round_name="R1", lint=lambda repo: called.append(1) or []).run()
        self.assertEqual([], called)

    def test_p5_comes_from_the_chain_bot_sessions_over_the_r1_evidence(self):
        r1 = Path(tempfile.mkdtemp())
        for root, seconds in ((self.evidence, 1.30), (r1, 1.22)):
            d = Path(root) / "a2" / "gameplay-tp" / "ui-data"
            d.mkdir(parents=True, exist_ok=True)
            (d / "load-timing.json").write_text(json.dumps({"Version": 1, "Scenes": ["Selection", "Run"], "Seconds": [1.2, seconds]}))
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            chain = make_chain(self.evidence, ["r2"], round_name="R2", r1_evidence=r1, r1_commit="b" * 40)
            chain.run()   # a chain of RECORDED stages only has no PASS verdict to exit 0 on; the record is what counts
        self.assertEqual("RECORDED", chain.verdicts["r2"])
        p5 = json.loads((self.evidence / "r2" / "r2-P5.json").read_text())
        self.assertEqual(("P5", FINAL, "b" * 40), (p5["gate"], p5["commit"], p5["baselineCommit"]))
        self.assertEqual(round(1.30 / 1.22, 4), p5["values"]["ratio"])

    def test_without_an_r1_evidence_p5_is_left_blocked_and_named(self):
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: []):
            chain = make_chain(self.evidence, ["r2"], round_name="R2")
            chain.run()
        self.assertEqual("RECORDED", chain.verdicts["r2"])
        self.assertFalse((self.evidence / "r2" / "r2-P5.json").exists())
        self.assertIn("P5 BLOCKED", (self.evidence / "accept.log").read_text())

    def test_quiet_sessions_only_in_r2q(self):
        self.assertEqual(["AudioR2Dense40"], [self.opt(a, "--filter") for a in self.planned_r2(["r2q"], "R2")])

    def quiet_run(self, host, stages=("r2q",), resume=False, tools=None):
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: self.STEPS[quiet]):
            chain = make_chain(self.evidence, list(stages), tools=tools or FakeTools(), round_name="R2", host=host, resume=resume)
            chain.run()
        return chain

    def test_a_quiet_session_waits_for_a_quiet_host_and_is_sampled_while_it_runs(self):
        # KADRO game-test-engineer SHOULD-6 / S-08 06.10: r2q's quiet is measured and recorded, not assumed
        host, tools = FakeHost(), FakeTools()
        self.quiet_run(host, tools=tools)
        self.assertEqual(["wait", "start", "stop"], host.events)
        record = json.loads((self.evidence / "r2" / "r2-S3" / "host.json").read_text())
        self.assertEqual(("r2-S3", ["S3"], True, False, [], 1, 27.0),
                         (record["step"], record["gates"], record["quietAtStart"], record["dirty"], record["dirtyWhy"],
                          record["samples"], record["load1Max"]))   # load1 27 is reported, never judged (C-00 06.10)
        self.assertEqual("t\tload1\tforeignUnity\tforeignPlayer\tforeignBlender\tforeignFfmpeg",
                         (self.evidence / "r2" / "r2-S3" / "host-samples.tsv").read_text().splitlines()[0])
        bound = json.loads((self.evidence / "stages" / "r2q.json").read_text())["artifactSha256"]
        self.assertIn("../r2/r2-S3/host.json", bound)
        self.assertIn("../r2/r2-S3/host-samples.tsv", bound)
        self.assertIn("HOST r2q-r2-S3: quiet at start", (self.evidence / "accept.log").read_text())

    def test_a_busy_host_is_recorded_kirli_and_the_session_still_runs(self):
        busy = {"load1": 12.0, "foreignUnity": 1, "foreignPlayer": 0, "foreignBlender": 0, "foreignFfmpeg": 0}
        host = FakeHost(start=busy, quiet=False, waited=1800.0, samples=[dict(busy, t=0.0), dict(busy, foreignPlayer=1, t=5.0)])
        tools = FakeTools()
        self.quiet_run(host, tools=tools)
        self.assertTrue(any("AudioR2Dense40" in map(str, c) for c in tools.calls))   # never skipped: the owner's gates decide
        record = json.loads((self.evidence / "r2" / "r2-S3" / "host.json").read_text())
        self.assertEqual((False, True, 1, 12.0), (record["quietAtStart"], record["dirty"], record["foreignPlayerMax"], record["load1Max"]))
        self.assertEqual(["start not quiet after 1800 s: foreign Unity 1, Player 0, Blender 0, ffmpeg 0",
                          "a foreign heavy process ran during the session (max foreign Unity 1, Player 1, Blender 0, ffmpeg 0)"],
                         record["dirtyWhy"])
        self.assertIn("KİRLİ r2q-r2-S3: start not quiet", (self.evidence / "accept.log").read_text())

    def test_the_non_quiet_stage_never_waits(self):
        host = FakeHost()
        self.quiet_run(host, stages=("r2",))
        self.assertEqual([], host.events)
        self.assertFalse(list((self.evidence / "r2").rglob("host.json")))

    def test_a_dry_run_only_plans_the_host_check(self):
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: self.STEPS[quiet]):
            chain = self.dry(["r2q"], round_name="R2")
        self.assertTrue(any("DRY r2q: host check before r2q-r2-S3 (no foreign Unity / Player / Blender / ffmpeg, ≤ 1800 s)" in line
                            for line in chain.lines), chain.lines)

    def test_a_reused_session_is_not_watched_again_and_keeps_its_host_record(self):
        self.quiet_run(FakeHost())
        again = FakeHost()
        self.quiet_run(again, resume=True)
        self.assertEqual([], again.events)
        bound = json.loads((self.evidence / "stages" / "r2q.json").read_text())["artifactSha256"]
        self.assertIn("../r2/r2-S3/host.json", bound)

    def test_a_unit_is_reused_only_on_resume(self):
        # gt 06.10 KP-9: without --resume a complete unit record is never reused (nor its host record left unwatched)
        self.quiet_run(FakeHost())
        record = self.evidence / "stages" / "units" / "r2q-r2-S3.json"
        self.assertTrue(record.is_file())
        self.assertIsNotNone(make_chain(self.evidence, ["r2q"], round_name="R2", resume=True).reusable_unit(record))
        self.assertIsNone(make_chain(self.evidence, ["r2q"], round_name="R2").reusable_unit(record))

    def test_the_manifest_binds_each_quiet_gates_host_record(self):
        put_json(self.evidence / "r2" / "r2-S1.json", {"gate": "S1"})
        put_json(self.evidence / "r2" / "r2-S1" / "host.json", {"dirty": False})
        put_json(self.evidence / "r2" / "r2-L1.json", {"gate": "L1"})
        section = acc.r2_section(self.evidence)
        self.assertEqual("r2/r2-S1/host.json", section["gates"]["S1"]["host"]["path"])
        self.assertNotIn("host", section["gates"]["L1"])
        self.assertNotIn("S2", section["gates"])   # a host record alone binds nothing

    def test_an_r1_round_runs_no_measurement_session(self):
        self.assertEqual([], self.planned_r2(["r2", "r2q"], "R1"))

    def test_playmode_session_is_player_scope_guarded(self):
        with mock.patch.object(acc.r2_gates, "measurement_steps", side_effect=lambda c, quiet: self.STEPS[quiet]):
            chain = self.dry(["r2"], round_name="R2")
        scopes = [argv[-1] for s, argv in chain.planned if s == "r2" and Path(argv[0]).name == "userdata-guard.sh"]
        self.assertEqual(["player", "player", "editor", "editor"], scopes)


class StopTests(Base):
    def prepared(self, stages=("a6", "a0", "a1")):
        tools = FakeTools()
        self.assertEqual(0, make_chain(self.evidence, stages, tools=tools).run())
        return tools

    def test_bot_guard_stop_stops_the_whole_chain(self):
        tools = self.prepared(("a6",))
        tools.script["gameplay-tp"] = [7]
        chain = make_chain(self.evidence, ["a2", "a3", "a5", "a4", "a8"], tools=tools)
        self.assertEqual(7, chain.run())
        scripts = [Path(c[1]).name if c[0] == sys.executable else Path(c[0]).name for c in tools.calls]
        self.assertNotIn("wp078_perf.py", scripts)
        self.assertNotIn("wp078_report.py", scripts)
        self.assertFalse(any("--kind" in c and c[c.index("--kind") + 1] in ("flag", "stability") for c in tools.calls))
        log = (self.evidence / "accept.log").read_text()
        self.assertIn("STOP a2", log)
        self.assertIn("report to C-00", log)

    def test_playmode_guard_is_player_scope_snapshot_and_restore(self):
        # C-00 30.09 lock order player → unity: the real guard calls, not only the dry-run plan, carry scope player
        tools, guard = FakeTools(), FakeGuard()
        self.prepared(("a6",))
        chain = make_chain(self.evidence, ["a1"], tools=tools, guard=guard)
        chain.run()
        alone = [("snapshot", "a1-" + i, "player") for i, _ in acc.ci_plan()[1]]
        self.assertEqual([("snapshot", "a1-editmode", "editor"), ("close", "a1-editmode", "editor"),
                          ("snapshot", "a1-playmode", "player"), ("close", "a1-playmode", "player")]
                         + [x for snap in alone for x in (snap, ("close",) + snap[1:])], guard.scopes)

    def test_editor_snapshot_error_is_stop_and_nothing_runs(self):
        tools, guard = FakeTools(), FakeGuard(snapshot_codes=[12])
        chain = make_chain(self.evidence, ["a6", "a1"], tools=tools, guard=guard)
        self.assertEqual(7, chain.run())
        self.assertEqual([], tools.calls)
        self.assertEqual([("snapshot", "build-dev")], guard.calls)

    def test_editor_restore_verdict_failure_is_stop_after_restore(self):
        tools = FakeTools()
        guard = FakeGuard(close=dict(OK_GUARD, code=7, why="touched=1 keys=[] (a non-plist file changed)", verified=True))
        chain = make_chain(self.evidence, ["a1", "a2"], tools=tools, guard=guard)
        self.assertEqual(7, chain.run())
        self.assertEqual([("snapshot", "a1-editmode"), ("close", "a1-editmode")], guard.calls)
        self.assertEqual(1, len(tools.calls))
        record = json.loads((self.evidence / "stages" / "units" / "a1-editmode.json").read_text())
        self.assertEqual("failed", record["status"])

    def test_snapshot_wait_persisting_stops_after_bounded_tries(self):
        tools, guard = FakeTools(), FakeGuard(snapshot_codes=[30, 30, 30])
        chain = make_chain(self.evidence, ["a1"], tools=tools, guard=guard, wait_tries=3)
        self.assertEqual(30, chain.run())
        self.assertEqual([], tools.calls)
        self.assertEqual(3, sum(1 for c in guard.calls if c[0] == "snapshot"))
        self.assertIn("persisted after 3 tries", (self.evidence / "accept.log").read_text())

    def test_bot_wait_then_success_continues_and_keeps_the_blocked_attempt(self):
        tools = self.prepared(("a6",))
        tools.script["gameplay-topdown"] = [30]
        chain = make_chain(self.evidence, ["a2"], tools=tools, resume=True)
        self.assertEqual(0, chain.run())
        self.assertTrue((self.evidence / "a2" / "gameplay-topdown.wait-1" / "attempt.json").is_file())
        self.assertEqual("complete", json.loads((self.evidence / "a2" / "gameplay-topdown" / "attempt.json").read_text())["status"])

    def test_bot_wait_75_persisting_stops(self):
        tools = self.prepared(("a6",))
        tools.script["stability-61"] = [75, 75]
        chain = make_chain(self.evidence, ["a5", "a4"], tools=tools, wait_tries=2)
        self.assertEqual(75, chain.run())
        self.assertFalse(any(Path(c[1]).name == "wp078_perf.py" for c in tools.calls if c[0] == sys.executable))

    def test_failed_gameplay_does_not_stop_later_stages(self):
        tools = self.prepared(("a6", "a1"))
        tools.script["gameplay-fp"] = [1]
        chain = make_chain(self.evidence, ["a2", "a3"], tools=tools)
        self.assertEqual(1, chain.run())
        self.assertEqual("FAIL", json.loads((self.evidence / "stages" / "a2.json").read_text())["verdict"])
        self.assertEqual("PASS", json.loads((self.evidence / "stages" / "a3.json").read_text())["verdict"])

    # ---- G4 D2 review MUST-1: NOT restored (65) is a STOP, never a wait ---------------------------------------------
    def test_bot_not_restored_65_stops_at_once_and_the_marker_stays(self):
        tools = self.prepared(("a6",))
        tools.script["gameplay-topdown"] = [65, 0]
        chain = make_chain(self.evidence, ["a2", "a3"], tools=tools)
        self.assertEqual(65, chain.run())
        runs = [c for c in tools.calls if "--out" in c and Path(c[c.index("--out") + 1]).name == "gameplay-topdown"]
        self.assertEqual(1, len(runs))                                            # not retried
        self.assertTrue((self.evidence / "a2" / "gameplay-topdown" / "ug" / guard_receipt.MARKER).is_file())
        self.assertEqual([], list((self.evidence / "a2").glob("gameplay-topdown.*")))   # not moved aside
        self.assertIn("NOT restored", (self.evidence / "accept.log").read_text())

    def test_perf_not_restored_65_stops_before_the_report_and_b9(self):
        tools = self.prepared(("a6",))
        tools.script["perf:perf"] = [65]
        chain = make_chain(self.evidence, ["a4"], tools=tools)
        self.assertEqual(65, chain.run())
        scripts = [Path(c[1]).name for c in tools.calls if c[0] == sys.executable]
        self.assertEqual(1, scripts.count("wp078_perf.py"))
        self.assertNotIn("wp078_perf_report.py", scripts)

    def test_unresolved_marker_anywhere_stops_before_the_next_unit(self):
        tools = self.prepared(("a6",))
        marker = self.evidence / "b9" / "pose-x" / "ug" / "ug-perf-sinematik-tp-1" / guard_receipt.MARKER
        put(marker, "NOT restored\n")
        before = len(tools.calls)
        self.assertEqual(65, make_chain(self.evidence, ["a2"], tools=tools).run())
        self.assertEqual(before, len(tools.calls))                               # nothing started
        self.assertIn("unrestored guard window", (self.evidence / "accept.log").read_text())
        put(marker.parent / guard_receipt.MARKER_RESOLVED, "C-00\n")             # only C-00 writes this
        self.assertEqual(0, make_chain(self.evidence, ["a2"], tools=tools, resume=True).run())

    def test_move_aside_refuses_a_dir_with_an_unresolved_marker(self):
        chain = make_chain(self.evidence, ["a2"])
        out = self.evidence / "a2" / "gameplay-tp"
        put(out / "ug" / guard_receipt.MARKER, "NOT restored\n")
        with self.assertRaises(acc.Stop) as caught:
            chain.move_aside(out, "wait")
        self.assertEqual(65, caught.exception.code)
        self.assertTrue(out.is_dir())

    def test_bot_stages_blocked_without_a_dev_build(self):
        tools = FakeTools()
        chain = make_chain(self.evidence, ["a2", "a4"], tools=tools)
        self.assertEqual(1, chain.run())
        self.assertEqual([], tools.calls)
        self.assertFalse((self.evidence / "stages" / "a2.json").exists())
        self.assertIn("BLOCKED a2", (self.evidence / "accept.log").read_text())


class SigtermTests(Base):
    def test_sigterm_during_suite_restores_and_exits_143(self):
        tools, guard = FakeTools(), FakeGuard()

        def runner(argv, log_path):
            if Path(argv[0]).name == "test.sh":
                raise acc.Terminated()
            return tools(argv, log_path)
        chain = make_chain(self.evidence, ["a1", "a2"], tools=runner, guard=guard)
        self.assertEqual(143, chain.run())
        self.assertEqual([("snapshot", "a1-editmode"), ("close", "a1-editmode")], guard.calls)
        self.assertIn("SIGTERM", (self.evidence / "accept.log").read_text())

    def test_sigterm_is_forwarded_to_the_running_child(self):
        chain = make_chain(self.evidence, ["a0"])
        previous = signal.signal(signal.SIGTERM, chain.on_sigterm)
        timer = threading.Timer(0.5, os.kill, (os.getpid(), signal.SIGTERM))
        try:
            started = time.monotonic()
            timer.start()
            with self.assertRaises(acc.Terminated):
                chain._spawn(["sleep", "30"], self.evidence / "logs" / "sleep.out")
            self.assertLess(time.monotonic() - started, 10)
        finally:
            timer.cancel()
            signal.signal(signal.SIGTERM, previous)
        self.assertIsNone(chain.child)


class ManifestTests(Base):
    def full_run(self, tools=None, stages=acc.STAGE_ORDER, **kw):
        tools = tools or FakeTools()
        chain = make_chain(self.evidence, stages, tools=tools, **kw)
        code = chain.run()
        return code, tools

    def report(self):
        reports = sorted(self.evidence.glob("report-*/wp078-report.json"))
        self.assertTrue(reports)
        return json.loads(reports[-1].read_text())

    def test_round_r2_binds_measurement_artifacts_into_the_manifest(self):
        # WP-091: --round R2 adds the r2-gates.json step; a present measurement is bound with its hash, the rest stay absent
        stages = [s for s in acc.STAGE_ORDER if s != "a8"]
        self.full_run(stages=stages)
        put_json(self.evidence / "r2" / "r2-L1.json", {"schema": "dd-r2-measure-v1", "gate": "L1", "commit": FINAL,
                                                     "values": {"avg": 0.02, "p95": 0.05}})
        self.assertNotEqual(0, make_chain(self.evidence, ["a8"], tools=FakeTools(), resume=True, round_name="R2").run())
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        self.assertEqual("R2", manifest["round"])
        self.assertEqual(["L1"], list(manifest["checklist"]["R2"]["gates"]))
        self.assertTrue(manifest["checklist"]["R2"]["xml"])
        report = self.report()
        rows = {g["id"]: g["status"] for g in report["checklist"]["R2"]["gates"]}
        self.assertEqual("PASS", rows["L1"])
        self.assertEqual("BLOCKED", rows["S1"])

    def test_b9_pose_windows_do_not_make_the_a4_root_stray(self):
        # regression (WP-091): every B9 pose root is a perf root outside <E>/a4/perf, and the A4 report's single-root check
        # (K8) walked the whole evidence tree — a chain run with --b9-poses BLOCKED its own A4. Now declared, it passes.
        self.full_run(b9_poses=B9PoseTests.POSES)
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        self.assertEqual([{"role": "b9-pose", "path": "b9/" + p["name"], "start": p["start"]} for p in B9PoseTests.POSES],
                         manifest["checklist"]["A4"]["secondaryRoots"])   # from the B9 record, with the start each pose ran
        report = self.report()
        self.assertEqual([], [f for f in report["failures"] if f["step"] == "A4"])
        self.assertTrue(report["pass"], report["failures"])

    def test_a_b9_folder_the_b9_record_does_not_hold_stays_a_stray_root(self):
        # KADRO test-engineer MUST-2 (b): declared from stages/b9.json, never from a folder listing
        self.full_run(b9_poses=B9PoseTests.POSES)
        shutil.copytree(self.evidence / "b9" / "plaza-n", self.evidence / "b9" / "spawn")
        self.assertNotEqual(0, make_chain(self.evidence, ["a8"], tools=FakeTools(), resume=True).run())
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        self.assertNotIn("b9/spawn", [r["path"] for r in manifest["checklist"]["A4"]["secondaryRoots"]])
        self.assertTrue(any(f["step"] == "A4" and "b9/spawn" in f["reason"] for f in self.report()["failures"]))

    def test_a_recorded_baseline_root_that_is_gone_is_not_declared(self):
        # mutant M5: the manifest declares a4/perf-r1 only when it is there
        self.full_run(round_name="R2", r1_evidence=r1_evidence(self.tmp.name), r1_commit=R1)
        shutil.move(str(self.evidence / "a4" / "perf-r1"), str(Path(self.tmp.name) / "perf-r1-away"))
        manifest = acc.assemble_manifest(self.evidence, FINAL, ASSETS, "R2")
        # the A4 baseline is not declared; z3/perf-r1 (also role r1-baseline, from the z3 record) may well be, now that
        # r2_z3.py is in the tree and the chain runs the z3 stage
        self.assertNotIn("a4/perf-r1", [r["path"] for r in manifest["checklist"]["A4"].get("secondaryRoots", [])])

    def test_r2_round_p2_alternation_reaches_the_report(self):
        self.full_run(round_name="R2", r1_evidence=r1_evidence(self.tmp.name), r1_commit=R1, b9_poses=B9PoseTests.POSES)
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        self.assertEqual({"role": "r1-baseline", "path": "a4/perf-r1", "commit": R1, "binarySha256": sha(b"r1probe")},
                         manifest["checklist"]["A4"]["secondaryRoots"][0])
        self.assertIn("P2", manifest["checklist"]["R2"]["gates"])
        report = self.report()
        self.assertEqual([], [f for f in report["failures"] if f["step"] == "A4"])
        rows = {g["id"]: g for g in report["checklist"]["R2"]["gates"]}
        self.assertEqual("REPORT", rows["P2"]["status"])
        self.assertEqual({"flaggedCells": 0, "worstDeltaMs": 0.0, "worstDeltaPct": 0.0}, rows["P2"]["values"])   # the same fixture both sides
        p2 = json.loads((self.evidence / "r2" / "r2-P2.json").read_text())
        self.assertEqual(("dd-r2-measure-v1", "P2", FINAL), (p2["schema"], p2["gate"], p2["commit"]))

    def test_round_r1_manifest_has_no_r2_step(self):
        self.full_run()
        self.assertNotIn("round", json.loads((self.evidence / "manifest.json").read_text()))

    def test_r2_isolated_sessions_carry_what_the_core_excludes(self):
        # R2 (C-00 01.10): the core XML no longer holds the DEC-077 readability case; its own `a3` session does, and the
        # report still closes the readability row — through the A3 record, not a fullname list
        code, _ = self.full_run()
        dec077 = next(i for i in RF.DOMAIN_IDS["readability"] if "DEC077" in i)
        core = (self.evidence / "a1" / "playmode" / "playmode.xml").read_text()
        self.assertNotIn(dec077, core)
        isolated = [d for d in (self.evidence / "a3").iterdir() if d.name.startswith("a3-isolated-") and "DEC077" in d.name]
        self.assertEqual(1, len(isolated))
        self.assertIn(dec077, (isolated[0] / "playmode.xml").read_text())
        self.assertTrue(self.report()["pass"])

    def test_r2_missing_alone_session_blocks_a1(self):
        code, _ = self.full_run()
        step_id = acc.ci_plan()[1][0][0]
        (self.evidence / "a1" / step_id / "playmode.xml").unlink()
        self.assertEqual(1, make_chain(self.evidence, ["a8"], tools=FakeTools(), resume=True).run())
        reports = sorted(self.evidence.glob("report-*/wp078-report.json"))
        report = json.loads(reports[-1].read_text())
        self.assertFalse(report["pass"])
        self.assertTrue(any(f["step"] == "A1" for f in report["failures"]), report["failures"])

    def test_full_chain_manifest_is_accepted_by_the_report(self):
        code, _ = self.full_run()
        report = self.report()
        self.assertEqual(0, code, report["failures"])
        self.assertTrue(report["pass"], report["failures"])
        self.assertEqual({"A" + str(i) for i in range(9)}, set(report["checklist"]))
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        self.assertEqual((FINAL, True, ASSETS), (manifest["commit"], manifest["final"], manifest["assetsTree"]))
        self.assertEqual(sha(b"probe"), manifest["buildHashes"]["probe"])
        refs = []

        def collect(value):
            if isinstance(value, dict):
                if "path" in value and "artifactSha256" in value:
                    refs.append(value)
                for item in value.values():
                    collect(item)
            elif isinstance(value, list):
                for item in value:
                    collect(item)
        collect(manifest)
        self.assertGreater(len(refs), 100)
        for item in refs:
            self.assertFalse(Path(item["path"]).is_absolute())
            self.assertEqual(sha((self.evidence / item["path"]).read_bytes()), item["artifactSha256"])
        a5 = manifest["checklist"]["A5"]
        self.assertEqual(["stability-26", "stability-61"], sorted(Path(r["summary"]["path"]).parent.name for r in a5["runs"]))
        self.assertEqual(9, len(a5["reportRuns"]))
        self.assertEqual("probe", manifest["checklist"]["A4"]["buildRole"])
        self.assertEqual({"notApplicable": "topdown-fixed-camera"},
                         next(r for r in manifest["checklist"]["A2"]["routeRuns"] if "route-topdown" in r["summary"]["path"])["frames"]["horizon"])
        flags = manifest["checklist"]["A3"]["checks"]["aiFlags"]["flagRuns"]
        self.assertEqual(sorted((f, v) for f in wp078_report.AI_FLAGS for v in (0, 1)), sorted((r["flag"], r["value"]) for r in flags))
        self.assertTrue(manifest["checklist"]["A7"]["byteEqual"])
        self.assertIn("UNKNOWN", {row.get("owner", "UNKNOWN") for row in manifest["checklist"]["A3"]["checks"].values()})
        log = (self.evidence / "accept.log").read_text().splitlines()
        self.assertTrue(all(line[:4].isdigit() and line[19] == "Z" for line in log))

    def test_perf_attempts_without_guard_leave_a4_blocked(self):
        # a window whose attempt.json has no guard receipt (wp064_run alone): A4 BLOCKED, never PASS, nothing invented
        code, _ = self.full_run(FakeTools(perf_guard=False, perf_ug_dirs=True))
        report = self.report()
        self.assertEqual(1, code)
        self.assertEqual("BLOCKED", report["checklist"]["A4"]["status"], report["failures"])
        self.assertEqual({"A4", "A8"}, {name for name, row in report["checklist"].items() if row["status"] != "PASS"}, report["failures"])

    def test_perf_with_guard_receipts_and_ug_named_folders_passes_a4(self):
        # wp078_perf writes the guard receipt into each window's attempt.json and names its guard folders ug-<cell>
        self.full_run(FakeTools(perf_guard=True, perf_ug_dirs=True))
        report = self.report()
        self.assertEqual("PASS", report["checklist"]["A4"]["status"], report["failures"])

    def test_missing_stage_stays_absent_and_blocked(self):
        code, _ = self.full_run(stages=[s for s in acc.STAGE_ORDER if s != "a5"])
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        self.assertNotIn("A5", manifest["checklist"])
        self.assertNotIn("byteEqual", manifest["checklist"]["A7"])
        self.assertNotIn("status", manifest["checklist"]["A7"])
        report = self.report()
        self.assertEqual(1, code)
        self.assertEqual("BLOCKED", report["checklist"]["A5"]["status"])
        self.assertEqual("BLOCKED", report["checklist"]["A7"]["status"])

    def test_route_frame_not_captured_is_absent_not_substituted(self):
        root = self.evidence.resolve()
        run = root / "a2" / "route-tp"
        put_json(run / "attempt.json", {"buildRole": "dev", "exitCode": 0, "guard": dict(OK_GUARD, snapshotExit=0)})
        put(run / "frames" / "route-courtyard.png", png(1))
        put_json(run / "bot-summary.json", {"routeFrames": [
            {"key": "courtyard", "status": "captured", "path": "frames/route-courtyard.png"},
            {"key": "kairo", "status": "blocked", "path": ""},
            {"key": "corridor", "status": "captured", "path": "../../outside.png"}]})
        entry = acc._route_run(root, run)
        self.assertEqual(["courtyard"], sorted(entry["frames"]))
        self.assertEqual({"snapshotExit", "restoreExit", "verified", "outOfScopeChanged"}, set(entry["guard"]))

    def test_resume_skips_verified_stages_and_reassembles(self):
        code, tools = self.full_run()
        self.assertEqual(0, code)
        before = len(tools.calls)
        chain = make_chain(self.evidence, acc.STAGE_ORDER, tools=tools, resume=True)
        self.assertEqual(0, chain.run())
        rerun = [Path(c[1]).name if c[0] == sys.executable else Path(c[0]).name for c in tools.calls[before:]]
        self.assertEqual(["wp078_report.py"], rerun)
        self.assertTrue((self.evidence / "manifest.json.prev-1").is_file())
        self.assertEqual(2, len(list(self.evidence.glob("report-*"))))

    def test_resume_reruns_a_stage_whose_evidence_changed(self):
        code, tools = self.full_run(stages=("a6", "a0", "a1"))
        self.assertEqual(0, code)
        (self.evidence / "a0" / "library-provenance.json").write_text("{}")
        before = len(tools.calls)
        self.assertEqual(0, make_chain(self.evidence, ["a0", "a1"], tools=tools, resume=True).run())
        rerun = [Path(c[1]).name if c[0] == sys.executable else Path(c[0]).name for c in tools.calls[before:]]
        self.assertEqual(["library_provenance.py"], rerun)


if __name__ == "__main__":
    unittest.main()
