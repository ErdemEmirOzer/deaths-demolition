"""WP-078 (C-00 30.09 M-16) · tools/qa/wp078_accept.py: the R1 acceptance chain.

Fake git, fake guard and fake tools only — no Unity, no Player, no build, no real userdata-guard. The fake tools write
the files the real ones write (build.sh, library_provenance.py, test.sh, wp078_bot.py, wp078_perf.py), so the manifest
the chain assembles is checked by the real wp078_report.generate_report."""
import argparse
import hashlib
import importlib.util
import json
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
import uuid
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]   # tools/qa
sys.path.insert(0, str(HERE))
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
        self.snapshot_codes, self.close_receipt, self.calls = list(snapshot_codes), close or dict(OK_GUARD), []

    def path(self):
        return "/main/tools/userdata-guard.sh"

    def snapshot(self, ug):
        self.calls.append(("snapshot", ug.name))
        ug.mkdir(parents=True, exist_ok=True)
        return self.snapshot_codes.pop(0) if self.snapshot_codes else 0

    def close(self, ug):
        self.calls.append(("close", ug.name))
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

    def tool_test_sh(self, argv, _):
        mode, out = argv[1], Path(argv[3])
        part = ".EditMode." if mode == "editmode" else ".PlayMode."
        names = sorted({n for ids in RF.DOMAIN_IDS.values() for n in ids if part in n})
        if mode == "editmode":
            names.append("DD.Prototype.Tests.EditMode.ProjectIntegrityTests.Assets_Guid_Unique")
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
        shutil.copytree(perf_fixture() / "perf", root, dirs_exist_ok=True)
        for attempt_path in root.glob("perf-*/attempt.json"):
            data = json.loads(attempt_path.read_text())
            if not self.perf_guard:
                data.pop("guard", None)             # what wp064_run really writes: no guard receipt in attempt.json
            attempt_path.write_text(json.dumps(data))
            if self.perf_ug_dirs:
                (root / "ug" / attempt_path.parent.name).mkdir(parents=True, exist_ok=True)   # wp078_perf's guard dirs
        return 0

    def tool_wp078_perf_report_py(self, argv, _):
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


def make_chain(evidence, stages=acc.STAGE_ORDER, tools=None, guard=None, git=None, **kw):
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

    def test_a1_suites_snapshot_test_restore_scope_editor_never_player(self):
        chain = self.dry(["a1"], editmode_filter="ProjectIntegrityTests|ArsenalV3Tests")
        seq = [(Path(a[0]).name, a[1]) for _, a in chain.planned]
        self.assertEqual([("userdata-guard.sh", "snapshot"), ("test.sh", "editmode"), ("userdata-guard.sh", "restore"),
                          ("userdata-guard.sh", "snapshot"), ("test.sh", "playmode"), ("userdata-guard.sh", "restore")], seq)
        self.assertFalse(any("player" in a for _, argv in chain.planned for a in argv if a.startswith("--scope") or a == "player"))
        edit = chain.planned[1][1]
        self.assertEqual("ProjectIntegrityTests|ArsenalV3Tests", self.opt(edit, "--filter"))
        self.assertNotIn("--filter", chain.planned[4][1])

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

    def test_real_perf_attempts_without_guard_leave_a4_blocked(self):
        # wp064_run's attempt.json carries no guard receipt and wp078_perf keeps its guard folders under <root>/ug/<cell>:
        # the report must say BLOCKED for A4, never PASS, and nothing is invented to fill it.
        code, _ = self.full_run(FakeTools(perf_guard=False, perf_ug_dirs=True))
        report = self.report()
        self.assertEqual(1, code)
        self.assertEqual("BLOCKED", report["checklist"]["A4"]["status"], report["failures"])
        self.assertEqual({"A4", "A8"}, {name for name, row in report["checklist"].items() if row["status"] != "PASS"}, report["failures"])

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
