import json
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SOAK_GATE = ROOT / "qa" / "soak_gate.py"
AGGREGATE = ROOT / "qa" / "aggregate.py"
WP064_GATE = ROOT / "qa" / "wp064_gate.py"
WP064_RUN = ROOT / "qa" / "wp064_run.py"


def load_wp064_gate():
    spec = importlib.util.spec_from_file_location("wp064_gate", WP064_GATE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run_python(script, *args):
    env = dict(os.environ)
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    return subprocess.run(
        [sys.executable, str(script), *map(str, args)],
        text=True,
        capture_output=True,
        env=env,
    )


def soak_summary():
    return {
        "seed": 61,
        "roundsRequested": 12,
        "roundsCompleted": 12,
        "deaths": 0,
        "pass": True,
        "rounds": [
            {"round": number, "completed": True, "outcome": "completed"}
            for number in range(1, 13)
        ],
    }


class SoakGateTests(unittest.TestCase):
    def run_gate(self, summary):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "summary.json"
            path.write_text(json.dumps(summary), encoding="utf-8")
            return run_python(SOAK_GATE, path, "--rounds", "12", "--seed", "61")

    def test_valid_exact_seeded_soak_passes(self):
        result = self.run_gate(soak_summary())
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_wrong_seed_fails(self):
        summary = soak_summary()
        summary["seed"] = 62
        self.assertNotEqual(0, self.run_gate(summary).returncode)

    def test_out_of_order_rounds_fail(self):
        summary = soak_summary()
        summary["rounds"][8], summary["rounds"][9] = summary["rounds"][9], summary["rounds"][8]
        self.assertNotEqual(0, self.run_gate(summary).returncode)

    def test_death_and_generic_pass_false_each_fail(self):
        for mutation in ("death", "pass"):
            summary = soak_summary()
            if mutation == "death":
                summary["deaths"] = 1
            else:
                summary["pass"] = False
            with self.subTest(mutation=mutation):
                self.assertNotEqual(0, self.run_gate(summary).returncode)

    def test_string_completed_does_not_count_as_true(self):
        summary = soak_summary()
        summary["rounds"][0]["completed"] = "false"
        self.assertNotEqual(0, self.run_gate(summary).returncode)


class PerformanceAggregationTests(unittest.TestCase):
    def test_frame_csv_counts_headerless_and_headered_files(self):
        gate = load_wp064_gate()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            headerless = root / "headerless.csv"
            headered = root / "headered.csv"
            headerless.write_text("7.1\n7.2\n7.3\n", encoding="utf-8")
            headered.write_text("frame_ms\n7.1\n7.2\n7.3\n", encoding="utf-8")
            self.assertEqual(3, gate.csv_rows(headerless))
            self.assertEqual(3, gate.csv_rows(headered))

    def test_missing_metrics_fail_closed_even_when_stage_claims_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index in range(1, 4):
                attempt = root / f"perf-{index}"
                attempt.mkdir()
                (attempt / "04-window.json").write_text(json.dumps({
                    "pass": True,
                    "note": "missing metrics",
                    "frameCount": 4000,
                }), encoding="utf-8")
            run_python(AGGREGATE, root, "--window", "60", "--perf-runs", "3")
            report = json.loads((root / "qa-report.json").read_text(encoding="utf-8"))
            gates = {gate["id"]: gate for gate in report["gates"]}
            self.assertFalse(gates["perf.run1.metrics"]["pass"])
            self.assertFalse(gates["perf.run1.meanMs"]["pass"])
            self.assertFalse(gates["perf.run1.cleanP99Ms"]["pass"])

    def test_negative_metric_sentinel_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index in range(1, 4):
                attempt = root / f"perf-{index}"
                attempt.mkdir()
                (attempt / "04-window.json").write_text(json.dumps({
                    "pass": True,
                    "note": "negative sentinel",
                    "frameMeanMs": -1,
                    "frameP99Ms": -1,
                    "frameP99FilteredMs": -1,
                    "frameCount": 4000,
                }), encoding="utf-8")
            run_python(AGGREGATE, root, "--window", "60", "--perf-runs", "3")
            report = json.loads((root / "qa-report.json").read_text(encoding="utf-8"))
            gates = {gate["id"]: gate for gate in report["gates"]}
            self.assertFalse(gates["perf.run1.metrics"]["pass"])
            self.assertFalse(gates["perf.meanMs"]["pass"])

    def test_one_failed_window_cannot_be_hidden_by_average(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, p99 in enumerate((9.0, 11.0, 9.0), 1):
                attempt = root / f"perf-{index}"
                attempt.mkdir()
                (attempt / "04-window.json").write_text(json.dumps({
                    "pass": True,
                    "note": "synthetic",
                    "frameMeanMs": 7.0,
                    "frameP99Ms": p99,
                    "frameP99FilteredMs": p99,
                    "frameCount": 4000,
                }), encoding="utf-8")
            result = run_python(AGGREGATE, root, "--window", "60", "--perf-runs", "3")
            self.assertNotEqual(0, result.returncode)
            report = json.loads((root / "qa-report.json").read_text(encoding="utf-8"))
            gates = {gate["id"]: gate for gate in report["gates"]}
            self.assertFalse(gates["perf.run2.cleanP99Ms"]["pass"])
            self.assertFalse(gates["perf.cleanP99Ms"]["pass"])


class Wp064StrictGateTests(unittest.TestCase):
    def test_numeric_fields_reject_bool_string_nan_and_infinity(self):
        gate = load_wp064_gate()
        for value in (True, "7.0", float("nan"), float("inf"), -1):
            with self.subTest(value=value):
                self.assertFalse(gate.finite_number(value, 0.0, 10.0))
        self.assertTrue(gate.finite_number(7.0, 0.0, 10.0))

    def test_gpu_gate_requires_real_finite_frame_timing_samples(self):
        gate = load_wp064_gate()
        valid = {
            "pass": True,
            "gpuSampledFrames": 120,
            "gpuFrameAvgMs": 4.5,
            "gpuFrameMaxMs": 6.0,
            "gpuMeasurementSource": "FrameTimingManager",
        }
        self.assertTrue(gate.valid_gpu_stage(valid))
        for key, value in (("gpuSampledFrames", 0), ("gpuFrameAvgMs", -1),
                           ("gpuFrameMaxMs", float("nan")), ("gpuMeasurementSource", "unavailable")):
            invalid = dict(valid)
            invalid[key] = value
            with self.subTest(key=key):
                self.assertFalse(gate.valid_gpu_stage(invalid))

    def test_release_cap_never_applies_to_development_build(self):
        gate = load_wp064_gate()
        self.assertTrue(gate.valid_release_footprint({
            "mode": "release", "capApplicable": True, "capExceeded": False, "appMB": 545.6,
        }))
        self.assertFalse(gate.valid_release_footprint({
            "mode": "dev", "capApplicable": False, "capExceeded": False, "appMB": 664.5,
        }))
        self.assertFalse(gate.valid_release_footprint({
            "mode": "release", "capApplicable": True, "capExceeded": True, "appMB": 550.1,
        }))

    def test_malformed_root_fails_closed_without_traceback(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "build-dev").mkdir()
            (root / "build-dev" / "report.json").write_text("{broken", encoding="utf-8")
            result = run_python(WP064_GATE, root, "--commit", "0" * 40, "--repo", root)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertNotIn("Traceback", result.stderr)
            report = json.loads((root / "wp064-report.json").read_text(encoding="utf-8"))
            self.assertFalse(report["pass"])

    def test_runner_rejects_malformed_sha_and_nonfinite_numeric_input(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            common = ("perf", "--bin", root / "missing", "--out", root / "attempt")
            bad_sha = run_python(WP064_RUN, *common, "--commit", "abc")
            self.assertEqual(2, bad_sha.returncode)
            self.assertIn("full lowercase 40-hex", bad_sha.stderr)
            bad_number = run_python(WP064_RUN, *common, "--commit", "0" * 40, "--load-max", "nan")
            self.assertEqual(2, bad_number.returncode)
            self.assertIn("finite and positive", bad_number.stderr)

    def test_attempt_provenance_rejects_mixed_commit_and_stale_artifact(self):
        gate = load_wp064_gate()
        with tempfile.TemporaryDirectory() as directory:
            attempt = Path(directory) / "perf-1"
            attempt.mkdir()
            command = attempt / "command.json"
            metric = attempt / "04-window.json"
            command.write_text(json.dumps(["player", "-ddPilotScene", "Run"]), encoding="utf-8")
            metric.write_text(json.dumps({"frameMeanMs": 7.0}), encoding="utf-8")
            commit = "a" * 40
            provenance = {
                "schema": "dd-wp064-attempt-v1",
                "attemptId": "attempt-a",
                "kind": "perf",
                "executionCommit": commit,
                "status": "complete",
                "exitCode": 0,
                "binarySha256": "binary-a",
                "commandSha256": gate.sha256(command),
                "startedEpoch": 1.0,
                "completedEpoch": 2.0,
                "artifactSha256": {
                    "command.json": gate.sha256(command),
                    "04-window.json": gate.sha256(metric),
                },
            }
            (attempt / "attempt.json").write_text(json.dumps(provenance), encoding="utf-8")

            checks = []
            gate.validate_attempt(attempt, "perf", commit, "binary-a", ["command.json", "04-window.json"],
                                  lambda name, passed, value: checks.append((name, passed, value)))
            self.assertTrue(all(passed for _, passed, _ in checks), checks)

            metric.write_text(json.dumps({"frameMeanMs": 8.5}), encoding="utf-8")
            checks = []
            gate.validate_attempt(attempt, "perf", "b" * 40, "binary-a", ["command.json", "04-window.json"],
                                  lambda name, passed, value: checks.append((name, passed, value)))
            failed = {name for name, passed, _ in checks if not passed}
            self.assertIn("perf-1.provenanceCommit", failed)
            self.assertIn("perf-1.artifacts", failed)


def load_wp064_run():
    spec = importlib.util.spec_from_file_location("wp064_run", WP064_RUN)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def build_report(commit="a1b2c3d4e5f6", describe="wp-064-a1b2c3d", mode="release"):
    return {"mode": mode, "result": "Succeeded", "totalErrors": 0,
            "stamp": {"stamped": True, "commit": commit, "describe": describe, "mode": mode}}


class Wp064GateV2Tests(unittest.TestCase):
    """S-10 pre-review findings 1–6 (WP-064 r2)."""

    FULL = "a1b2c3d4e5f6" + "0" * 28

    def test_finding1_empty_unknown_or_dirty_stamp_fails(self):
        gate = load_wp064_gate()
        self.assertTrue(gate.editor_build_matches(build_report(), "release", self.FULL))
        for commit, describe in (("", "x"), ("unknown", "x"), ("a1b2c3d4e5f6", "wp-064-dirty"),
                                 ("a1b2c3d4e5f6", ""), ("a1b2c3d4e5f6", "unknown"), ("A1B2C3D", "x")):
            with self.subTest(commit=commit, describe=describe):
                self.assertFalse(gate.editor_build_matches(build_report(commit, describe), "release", self.FULL))

    def test_finding2_library_provenance_must_match_the_commit_tree(self):
        gate = load_wp064_gate()
        good = {"schema": "dd-wp064-library-provenance-v1", "phase": "post-build", "head": self.FULL, "porcelain": "",
                "lfsPointersOnDisk": 0, "lfsMissing": 0, "assetsTree": "tree-a",
                "library": {"artifactDbBytes": 1024, "artifactDbMtime": 1.7e9}}
        checks = []
        gate.library_provenance_matches(good, self.FULL, "tree-a", lambda n, p, v: checks.append((n, p)), "library.dev")
        self.assertTrue(all(p for _, p in checks), checks)
        for key, value in (("assetsTree", "tree-b"), ("porcelain", " M Assets/x.unity"), ("lfsPointersOnDisk", 3),
                           ("head", "b" * 40), ("phase", "post-hoc")):
            bad = dict(good, **{key: value})
            checks = []
            gate.library_provenance_matches(bad, self.FULL, "tree-a", lambda n, p, v: checks.append((n, p)), "library.dev")
            with self.subTest(key=key):
                self.assertFalse(all(p for _, p in checks))

    def test_finding3_controller_processes_are_not_exempt(self):
        run = load_wp064_run()
        for args in ("/Users/x/.codex/bin/codex exec", "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT"):
            row = {"pid": 999999, "ppid": 1, "cpu": 90.0, "args": args}
            with self.subTest(args=args):
                self.assertFalse(run.controller_or_presentation(row))
                self.assertTrue(run.heavy(row, None, {}))
        self.assertTrue(run.controller_or_presentation({"args": "/System/Library/PrivateFrameworks/SkyLight.framework/Resources/WindowServer -daemon"}))

    def test_finding4_missing_or_empty_log_fails(self):
        gate = load_wp064_gate()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            empty = root / "empty.log"
            empty.write_text("", encoding="utf-8")
            hits = gate.log_failures([root / "missing.log", empty])
            self.assertEqual(2, len(hits), hits)

    def test_finding5_metal_import_and_lfs_patterns_and_exact_allowlist(self):
        gate = load_wp064_gate()
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "player.log"
            log.write_text("\n".join((
                "Shader Hidden/ProbeVolume/VoxelizeScene is not supported: GPU does not support conservative rasterization",
                "all good",
            )), encoding="utf-8")
            self.assertEqual([], gate.log_failures([log]))
            for line in ("Metal: Error creating pipeline state", "Shader DD/GroundBlend is not supported on this GPU",
                         "Could not create texture from Assets/x.png", "File could not be read", "Unable to import Assets/x.fbx",
                         "version https://git-lfs.github.com/spec/v1"):
                log.write_text(line + "\n", encoding="utf-8")
                with self.subTest(line=line):
                    self.assertEqual(1, len(gate.log_failures([log])))

    def test_finding5_lfs_pointer_inside_player_data_fails(self):
        gate = load_wp064_gate()
        with tempfile.TemporaryDirectory() as directory:
            build = Path(directory)
            data = build / "Game.app" / "Contents" / "Resources" / "Data"
            data.mkdir(parents=True)
            (data / "sharedassets0.assets").write_bytes(b"\x00" * 1000 + b"UnityFS")
            hits, scanned = gate.lfs_pointers_in_build(build)
            self.assertEqual(([], 1), (hits, scanned))
            (data / "resources.assets").write_bytes(b"\x00" * 5 + gate.LFS_POINTER_MAGIC + b"\noid sha256:00\n")
            hits, _ = gate.lfs_pointers_in_build(build)
            self.assertEqual(1, len(hits), hits)
            empty = Path(directory) / "nobuild"
            empty.mkdir()
            self.assertTrue(gate.lfs_pointers_in_build(empty)[0])

    def test_finding6_stall_exclusion_bounded_and_rederived(self):
        gate = load_wp064_gate()
        frames = [7.0] * 990 + [40.0] * 10          # 1.0 % stalls
        ok, _ = gate.stall_exclusion({"framesExcludedAsStalls": 10, "frameCount": 1000}, frames)
        self.assertTrue(ok)
        frames2 = [7.0] * 980 + [40.0] * 20         # 2.0 % stalls
        self.assertFalse(gate.stall_exclusion({"framesExcludedAsStalls": 20, "frameCount": 1000}, frames2)[0])
        self.assertFalse(gate.stall_exclusion({"framesExcludedAsStalls": 3, "frameCount": 1000}, frames)[0],
                         "the probe's count must equal the CSV's frames above 33.33 ms")

    def test_finding6_ledger_discards_only_for_host_reasons(self):
        gate = load_wp064_gate()
        rows = [
            {"dir": "perf-1", "status": "preflight-failed"},
            {"dir": "perf-2", "status": "complete"},
            {"dir": "perf-3", "status": "failed", "hostClean": False, "timedOut": False},
            {"dir": "perf-4", "status": "complete"},
            {"dir": "perf-5", "status": "complete"},
        ]
        complete, discarded, unexplained = gate.classify_perf_ledger(rows)
        self.assertEqual((3, 2, 0), (len(complete), len(discarded), len(unexplained)))
        for bad in ({"dir": "perf-6", "status": "failed", "hostClean": True, "timedOut": False},
                    {"dir": "perf-6", "status": "failed", "hostClean": False, "timedOut": True},
                    {"dir": "perf-6", "status": "running"}):
            with self.subTest(bad=bad):
                self.assertEqual(1, len(gate.classify_perf_ledger(rows + [bad])[2]))


if __name__ == "__main__":
    unittest.main()


SOAK_COMMIT = "58be2c28b7505bc17eb70aab3407e8110397723e"


def stability_summary(seed, mode="stability", completed=12):
    rounds = [{"round": r, "completed": r <= completed, "outcome": "completed" if r <= completed else "run-ended:PlayerDeath",
               "memMonoUsedMB": 60.0 + 0.5 * r, "memTotalAllocatedMB": 900.0 + 2.0 * r, "memRssMB": -1.0}
              for r in range(1, 13)]
    stability = mode == "stability"
    return {"seed": seed, "roundsRequested": 12, "roundsCompleted": completed, "deaths": 0 if completed == 12 else 1,
            "pass": False if stability else completed == 12, "runMode": mode, "stabilityPass": stability,
            "artifactWriteFailed": False, "deathRound": 0 if completed == 12 else completed + 1, "rounds": rounds}


def write_external_run(root, name, summary, commit=SOAK_COMMIT, armed_seed=None, quiet=3, restore="USERDATA_RESTORED_OK"):
    """One S-10 soak.sh attempt: <root>/build-<name> (dev build of the commit), <root>/<name>.wait.log, <root>/<name>/."""
    build = root / ("build-" + name)
    macos = build / "DeathsDemolition.app" / "Contents" / "MacOS"
    macos.mkdir(parents=True)
    binary = macos / "Deaths Demolition - Playable Concept"
    binary.write_bytes(b"player-" + name.encode())
    os.utime(binary, (1_000_000, 1_000_000))
    (build / "report.json").write_text(json.dumps({"mode": "dev", "ok": True, "head": commit}), encoding="utf-8")
    (build / "build-report.json").write_text(json.dumps({"mode": "dev", "result": "Succeeded", "totalErrors": 0, "stamp": {
        "stamped": True, "commit": commit[:12], "describe": "wp-064b-bot-v2", "mode": "dev"}}), encoding="utf-8")
    lines = [f"2026-09-24T10:0{i}:00Z busy=0 slots=0 load=3.2 quiet={i}" for i in range(1, quiet + 1)]
    (root / (name + ".wait.log")).write_text("\n".join(lines + [f"2026-09-24T10:04:00Z START build+soak {name} seed={summary['seed']}"]) + "\n", encoding="utf-8")
    run = root / name
    run.mkdir()
    (run / "bot-summary.json").write_text(json.dumps(summary), encoding="utf-8")
    seed = summary["seed"] if armed_seed is None else armed_seed
    (run / "player.log").write_text(f"[QaRunBot] armed rounds=12 seed={seed} roundTimeout=420s out={run}\n", encoding="utf-8")
    for item, text in (("exit.txt", "exit=0\n"), ("qa-report.json", "{}"), ("events.txt", "round 1\n"),
                       ("userdata-restore.txt", restore + "\n"), ("userdata-inventory.diff", "")):
        (run / item).write_text(text, encoding="utf-8")


class Dec091ClosureSoakTests(unittest.TestCase):
    """DEC-091 (user 2026-09-24) in the closure gate: stability = two seeds x 12/12 with the invulnerable-by-design bot on
    their own dev builds of the soak commit; >= 5 real-bot runs are required as a report, their distribution is not gated."""

    def soak_checks(self, soak_dirs="soak-61,soak-26", seeds="61,26", report_dirs=("r1", "r2", "r3", "r4", "r5"), mutate=None, soak_dirs_arg=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "perf"
            soak_root = Path(directory) / "s10"
            root.mkdir()
            soak_root.mkdir()
            runs = {"soak-61": stability_summary(61), "soak-26": stability_summary(26)}
            for index, name in enumerate(report_dirs):
                runs[name] = stability_summary((61, 26)[index % 2], mode="normal", completed=(3, 5, 8, 11, 7, 4)[index % 6])
            kwargs = {}
            if mutate:
                mutate(runs, kwargs)
            for name, summary in runs.items():
                write_external_run(soak_root, name, summary, **kwargs.get(name, {}))
            run_python(WP064_GATE, root, "--commit", "a" * 40, "--repo", root, "--soak-root", soak_root, "--soak-commit", SOAK_COMMIT,
                       "--soak-dirs", soak_dirs_arg or soak_dirs, "--seeds", seeds, "--report-dirs", ",".join(report_dirs))
            report = json.loads((root / "wp064-report.json").read_text(encoding="utf-8"))
        self.assertEqual(SOAK_COMMIT, report["soakCommits"]["soak-61"])
        return {item["name"]: item for item in report["checks"] if item["name"].startswith(("soak.", "report."))}

    def failed(self, checks):
        return sorted(name for name, item in checks.items() if not item["pass"])

    def test_two_seed_stability_and_five_real_runs_pass_the_soak_section(self):
        checks = self.soak_checks()
        self.assertEqual([], self.failed(checks))
        self.assertIn("soak.soak-26.stabilityGate", checks)
        self.assertIn("runs=5", checks["soak.realBotReport"]["value"])

    def test_one_seed_or_the_same_seed_twice_fails(self):
        self.assertIn("soak.stabilitySeeds", self.failed(self.soak_checks(soak_dirs="soak-61", seeds="61")))
        self.assertIn("soak.stabilitySeeds", self.failed(self.soak_checks(seeds="61,61")))

    def test_real_bot_run_in_a_stability_slot_fails(self):
        def mutate(runs, kwargs):
            runs["soak-26"] = stability_summary(26, mode="normal")
        failed = self.failed(self.soak_checks(mutate=mutate))
        self.assertIn("soak.soak-26.modeSeparated", failed)
        self.assertIn("soak.soak-26.stabilityGate", failed)

    def test_real_bot_report_needs_five_runs_but_not_their_rounds(self):
        self.assertIn("soak.realBotReport", self.failed(self.soak_checks(report_dirs=("r1", "r2", "r3", "r4"))))
        checks = self.soak_checks(report_dirs=("r1", "r2", "r3", "r4", "r5", "r6"))
        self.assertEqual([], self.failed(checks))   # deaths at rounds 4..12 are reported, not gated

    def test_stability_run_as_report_is_not_counted(self):
        def mutate(runs, kwargs):
            runs["r5"] = stability_summary(61)
        failed = self.failed(self.soak_checks(mutate=mutate))
        self.assertIn("report.r5.realBot", failed)
        self.assertIn("soak.realBotReport", failed)

    def test_soak_provenance_host_seed_and_user_data_fail_closed(self):
        cases = {
            "soak.soak-61.build": {"commit": "b" * 40},
            "soak.soak-61.host": {"quiet": 1},
            "soak.soak-61.command": {"armed_seed": 26},
            "soak.soak-61.userData": {"restore": "USERDATA_RESTORE_MISMATCH"},
        }
        for name, override in cases.items():
            with self.subTest(check=name):
                failed = self.failed(self.soak_checks(mutate=lambda runs, kwargs, o=override: kwargs.update({"soak-61": o})))
                self.assertIn(name, failed)

    def test_each_soak_dir_may_name_its_own_commit(self):
        other = "c" * 40
        def mutate(runs, kwargs):
            kwargs["soak-26"] = {"commit": other}
        # soak-26 ran on another commit's build (a world fix after the first seed): named per dir it passes...
        self.assertEqual([], self.failed(self.soak_checks(mutate=mutate, soak_dirs_arg="soak-61,soak-26@" + other)))
        # ...unnamed, its build is checked against the default soak commit and fails
        self.assertIn("soak.soak-26.build", self.failed(self.soak_checks(mutate=mutate)))


class ReleaseModeLabelTests(unittest.TestCase):
    """build-report.sh labels the mode with target / compression (WP-066W / 066C): the cap follows the base mode (r3 fix)."""

    def test_labelled_release_footprints_are_release_and_dev_never(self):
        gate = load_wp064_gate()
        for mode in ("release", "release-lz4hc", "release-win64-lz4hc"):
            self.assertTrue(gate.valid_release_footprint({"mode": mode, "capApplicable": True, "capExceeded": False, "appMB": 324.8}), mode)
        for mode in ("dev", "dev-lz4hc", "", None):
            self.assertFalse(gate.valid_release_footprint({"mode": mode, "capApplicable": True, "capExceeded": False, "appMB": 324.8}), mode)

    def test_build_report_applies_the_cap_to_a_compressed_release_only(self):
        for mode, applies in (("release", True), ("dev", False)):
            with tempfile.TemporaryDirectory() as directory:
                build = Path(directory)
                (build / "X.app").mkdir()
                (build / "build.log").write_text("Build Report\nComplete build size 300.0 mb\n", encoding="utf-8")
                (build / "build-report.json").write_text(json.dumps({"mode": mode, "compression": "lz4hc", "stamp": {"describe": "x"}}), encoding="utf-8")
                result = subprocess.run([str(ROOT / "build-report.sh"), str(build), "--cap", "550"], capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stderr)
                footprint = json.loads((build / "footprint.json").read_text(encoding="utf-8"))
                self.assertEqual(mode + "-lz4hc", footprint["mode"])
                self.assertIs(applies, footprint["capApplicable"], mode)

