"""WP-091 Z3 + P3 (S-05, C-00 06.10): tools/qa/r2_z3.py — render ratios R2/R1 from alternating probe pairs, the static
budget from z3-static.json, the B9 pose cells; the source must be in the recording. Fake evidence trees only."""
import contextlib
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import r2_gates  # noqa: E402
import r2_z3 as z3  # noqa: E402

FINAL, R1 = "f" * 40, "1" * 40
BIN_R2, BIN_R1 = "2" * 64, "3" * 64
REPO = Path(__file__).resolve().parents[3]
CONTRACT = r2_gates.load_contract((Path(__file__).resolve().parents[1] / "r2-gates.json").read_text(encoding="utf-8"))
STATIC = {"characterTextureMaxPx": 2048, "atlasesPerFamilyMax": 2, "mapsPerAtlasMax": 3, "shippedCharacterMB": 312.5,
          "bonesMax": 74, "materialsPerFamilyMax": 4, "materialsPerFamilyCrowdTotal": 8,
          "armorTierAtlasesTotal": 2}   # C-00 06.10 13:30 (S-05 scope): armour + tier atlases, a report key
Z3_TEST = "DD.Art.Tests.EditMode.Z3CharacterBudgetTests.Z3_KarakterButcesi_StatikAnahtarlar_Yazilir"
Z3_GATE = next(g for g in CONTRACT["gates"] if g["id"] == "Z3")
# r2-gates.json Z3 "tests": the static EditMode writer (Z3CharacterBudgetTests) must be Passed in a bound NUnit XML
Z3_CASES = {t: "Passed" for t in Z3_GATE.get("tests", [])}
MODE_NAMES = {"topdown": "TopDown", "tp": "ThirdPerson", "fp": "FirstPerson"}
TIER_NAMES = {"sinematik": "Sinematik", "yuksek": "Yuksek", "performans": "Performans"}


def window(root, tier, mode, index, commit, binary, start="0,-4,0", sampled=None, armor=True, setpass=100.0,
           batches=1000.0, triangles=1e6, draws=2000.0, achieved=40, forced=40, on_screen=12, active_min=40,
           sweep=False, status="complete", exit_code=0, screen=(1920, 1080), armor_note=None, skip=(), frame_mean=25.0):
    """One wp078_perf window as wp064_run + PilotPerfProbe leave it (the fields r2_z3 reads, JsonUtility shapes)."""
    d = Path(root) / f"perf-{tier}-{mode}-{index}"
    d.mkdir(parents=True)
    x, z, yaw = (float(v) for v in start.split(","))
    sx, sz = sampled if sampled is not None else (x, z)
    command = ["/b/Deaths Demolition - Playable Concept", "-screen-fullscreen", "0", "-ddPilotPerfProbe", str(d),
               "-ddPilotScene", "Run", "-ddPilotResolution", "1920x1080", "-ddQualityTier", tier, "-ddCameraMode", mode]
    if mode != "topdown":
        command += ["-ddPilotCameraSweep", "1"]          # wp078_perf.window_args; the probe holds the pose anyway
    command += ["-ddPilotStart", start] + (["-ddArmor", "all"] if armor else []) + ["-logFile", str(d / "player.log")]
    pose = {"set": True, "x": x, "z": z, "yaw": yaw, "sampled": True, "sampledX": sx, "sampledY": 0.05, "sampledZ": sz}
    unset = {"set": False, "x": 0.0, "z": 0.0, "yaw": 0.0, "sampled": False, "sampledX": 0.0, "sampledY": 0.0, "sampledZ": 0.0}
    note = armor_note if armor_note is not None else ("configured/ledger/forced 40/40/40 (pool ignored) in 4.27 s"
                                                       + (" | armor all: 117 pieces fitted" if armor else ""))
    files = {
        "attempt.json": {"schema": "dd-wp064-attempt-v1", "attemptId": f"{commit[:4]}-{tier}-{mode}-{index}", "kind": "perf",
                         "executionCommit": commit, "startedUtc": f"2026-10-06T02:{index:02d}:00Z", "status": status,
                         "exitCode": exit_code, "hostClean": True, "timedOut": False, "missingArtifacts": [],
                         "binarySha256": binary},
        "command.json": command,
        "01-scene-ready.json": {"stage": "01-scene-ready", "pass": True, "screenWidth": screen[0], "screenHeight": screen[1],
                                "start": pose, "achievedCount": 0},
        "02-force-40.json": {"stage": "02-force-40", "pass": achieved == 40, "start": unset, "forcedTarget": forced,
                             "configuredSafetyCeiling": 40, "achievedCount": achieved, "forceTimedOut": False, "note": note},
        "04-window.json": {"stage": "04-window", "pass": True, "start": pose, "cameraMode": MODE_NAMES[mode],
                           "qualityTier": TIER_NAMES[tier], "cameraSweep": sweep, "sweepOffByStart": mode != "topdown",
                           "cameraSweepMeanOnScreen": -1.0, "frameCount": 300, "frameMeanMs": frame_mean,
                           "frameP99FilteredMs": frame_mean * 1.4, "activeMin": active_min, "loadIntegrityFraction": 1.0,
                           "setPassCallsAvg": setpass, "batchesAvg": batches, "trianglesAvg": triangles, "drawCallsAvg": draws},
        "06-readability.json": {"stage": "06-readability", "pass": True, "start": unset, "enemiesChecked": 40,
                                "enemiesOnScreen": on_screen},
    }
    for name, data in files.items():
        if name not in skip:
            (d / name).write_text(json.dumps(data, indent=2), encoding="utf-8")
    return d


def z3_tree(evidence, pairs=5, modes=("topdown", "tp"), r2=None, r1=None):
    """pairs alternating windows per mode; r2 / r1: {(mode, index): window kwargs} overrides."""
    for mode in modes:
        for i in range(1, pairs + 1):
            window(Path(evidence) / "z3/perf", "sinematik", mode, i, FINAL, BIN_R2, **(r2 or {}).get((mode, i), {}))
            window(Path(evidence) / "z3/perf-r1", "sinematik", mode, i, R1, BIN_R1, **(r1 or {}).get((mode, i), {}))


def write_static(evidence, values=None, commit=FINAL, schema="dd-r2-z3-static-v1"):
    path = Path(evidence) / "r2" / "z3-static.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"schema": schema, "commit": commit, "source": "DD.Art.Tests.EditMode.Z3StatikTests.Olc",
                                "values": dict(STATIC) if values is None else values}), encoding="utf-8")
    return path


def strict(path):
    def reject(value):
        raise ValueError(value)
    return json.loads(Path(path).read_text(encoding="utf-8"), parse_constant=reject)


class Base(unittest.TestCase):
    def setUp(self):
        self.evidence = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.evidence, True)

    def main(self, *extra, gates="Z3,P3"):
        out, err = io.StringIO(), io.StringIO()
        argv = ["--evidence", str(self.evidence), "--commit", FINAL, "--r1-commit", R1, "--gates", gates, *extra]
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = z3.main(argv)
        return code, out.getvalue(), err.getvalue()

    def z3_only(self, *extra):
        return self.main(*extra, gates="Z3")

    @property
    def artifact(self):
        return self.evidence / "r2" / "r2-Z3.json"


class ContractTests(unittest.TestCase):
    def test_the_tool_writes_exactly_the_z3_keys_of_the_contract(self):
        ratio, static = z3.z3_keys(CONTRACT)
        gate = next(g for g in CONTRACT["gates"] if g["id"] == "Z3")
        self.assertEqual([c["key"] for c in gate["checks"]] + gate.get("report", []), ratio + static)
        self.assertEqual(["setPassRatio", "batchRatio", "trianglesRatio"], ratio)
        self.assertEqual(sorted(STATIC), sorted(static))
        # C-00 06.10: the crowd material total per body family is reported, never gated; thresholds unchanged
        self.assertEqual(["materialsPerFamilyCrowdTotal", "armorTierAtlasesTotal"], gate["report"])
        # the static writer: Z3's required test is the EditMode test its measuredBy session runs by full name
        self.assertEqual(([Z3_TEST], {"suite": "editmode", "filter": Z3_TEST, "quiet": False}), (gate.get("tests"), gate.get("measuredBy")))
        limits = {c["key"]: c["limit"] for c in gate["checks"]}
        self.assertEqual({"setPassRatio": 1.10, "batchRatio": 1.10, "trianglesRatio": 1.15, "characterTextureMaxPx": 2048,
                          "atlasesPerFamilyMax": 2, "mapsPerAtlasMax": 3, "shippedCharacterMB": 400, "bonesMax": 74,
                          "materialsPerFamilyMax": 4}, limits)

    def test_p3_reports_one_number(self):
        p3 = next(g for g in CONTRACT["gates"] if g["id"] == "P3")
        self.assertEqual(("report", ["cells"], "S-05"), (p3["kind"], p3["report"], p3["owner"]))


class Z3RatioTests(Base):
    def test_five_alternating_pairs_per_mode_and_the_static_file_write_r2_z3(self):
        z3_tree(self.evidence, r2={(m, i): {"setpass": 105.0, "batches": 1080.0, "triangles": 1.1e6} for m in ("topdown", "tp")
                                   for i in range(1, 6)})
        write_static(self.evidence)
        code, out, err = self.z3_only()
        self.assertEqual(0, code, err)
        data = strict(self.artifact)
        self.assertEqual(("dd-r2-measure-v1", "Z3", FINAL, R1), (data["schema"], data["gate"], data["commit"], data["baselineCommit"]))
        self.assertEqual(sum(z3.z3_keys(CONTRACT), []), list(data["values"]))       # exactly the gate's keys, in order
        self.assertEqual((1.05, 1.08, 1.1), (data["values"]["setPassRatio"], data["values"]["batchRatio"], data["values"]["trianglesRatio"]))
        self.assertEqual(74, data["values"]["bonesMax"])
        self.assertEqual(10, data["samples"])
        self.assertEqual("r2/z3-static.json", data["static"]["path"])
        self.assertEqual([0.0, -4.0, 0.0], data["pose"]["requested"])
        row = r2_gates.evaluate_gate(Z3_GATE, FINAL, measure=data, cases=Z3_CASES)
        self.assertEqual("PASS", row["status"], row["reason"])
        if Z3_GATE.get("tests"):   # the static writer's identity: no bound NUnit XML → BLOCKED by name, whatever the values
            blocked = r2_gates.evaluate_gate(Z3_GATE, FINAL, measure=data)
            self.assertEqual(("BLOCKED", "no NUnit XML bound for required test " + Z3_GATE["tests"][0]), (blocked["status"], blocked["reason"]))
        self.assertIn("Z3 written", out)
        self.assertEqual("OK", strict(self.evidence / "r2" / "z3-ratios.json")["status"])

    def test_the_cell_value_is_the_median_of_pair_ratios_not_a_ratio_of_medians(self):
        r2 = [110, 100, 90, 130, 100]
        r1 = [100, 80, 100, 100, 120]     # pair ratios 1.1, 1.25, 0.9, 1.3, 0.8333 → median 1.1; medians 100/100 → 1.0
        z3_tree(self.evidence, r2={("tp", i + 1): {"setpass": float(v)} for i, v in enumerate(r2)},
                r1={("tp", i + 1): {"setpass": float(v)} for i, v in enumerate(r1)})
        ratios = z3.z3_ratios(self.evidence, FINAL, R1, z3.load_targets())
        self.assertEqual("OK", ratios["status"], ratios["problems"])
        tp = next(c for c in ratios["cells"] if c["mode"] == "tp")
        self.assertEqual(1.1, tp["medians"]["setPassRatio"])
        self.assertEqual([1.1, 1.25, 0.9, 1.3, 0.8333], [r["setPassRatio"] for r in tp["pairRows"]])

    def test_the_gate_takes_the_worse_mode_and_fails_on_it(self):
        z3_tree(self.evidence, r2={**{("topdown", i): {"setpass": 102.0} for i in range(1, 6)},
                                   **{("tp", i): {"setpass": 112.0} for i in range(1, 6)}})
        write_static(self.evidence)
        self.assertEqual(0, self.z3_only()[0])
        data = strict(self.artifact)
        self.assertEqual((1.12, "tp"), (data["values"]["setPassRatio"], data["worstMode"]["setPassRatio"]))
        self.assertEqual({"topdown": 1.02, "tp": 1.12}, {c["mode"]: c["medians"]["setPassRatio"] for c in data["cells"]})
        row = r2_gates.evaluate_gate(Z3_GATE, FINAL, measure=data, cases=Z3_CASES)
        self.assertEqual("FAIL", row["status"])
        self.assertIn("setPassRatio 1.12 <= 1.1 is false", row["reason"])

    def test_fewer_than_five_pairs_in_a_cell_is_blocked_and_writes_no_artifact(self):
        z3_tree(self.evidence, pairs=5, modes=("topdown",))
        z3_tree(self.evidence, pairs=4, modes=("tp",))
        write_static(self.evidence)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertFalse(self.artifact.exists())
        self.assertIn("sinematik-tp: 4 pairs", err)
        self.assertEqual("BLOCKED", strict(self.evidence / "r2" / "z3-ratios.json")["status"])

    def test_a_missing_run_is_blocked(self):
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertIn("no z3/perf/", err)

    def test_a_discarded_window_is_skipped_and_the_nth_counted_windows_pair(self):
        z3_tree(self.evidence, modes=("topdown",))
        window(self.evidence / "z3/perf-r1", "sinematik", "tp", 1, R1, BIN_R1)
        window(self.evidence / "z3/perf-r1", "sinematik", "tp", 2, R1, BIN_R1, status="preflight-failed", exit_code=75)
        for i in range(3, 7):
            window(self.evidence / "z3/perf-r1", "sinematik", "tp", i, R1, BIN_R1)
        for i in range(1, 6):
            window(self.evidence / "z3/perf", "sinematik", "tp", i, FINAL, BIN_R2)
        ratios = z3.z3_ratios(self.evidence, FINAL, R1, z3.load_targets())
        self.assertEqual("OK", ratios["status"], ratios["problems"])
        tp = next(c for c in ratios["cells"] if c["mode"] == "tp")
        self.assertEqual([(1, 1), (2, 3), (3, 4), (4, 5), (5, 6)],
                         [(int(r["r2"].rsplit("-", 1)[1]), int(r["r1"].rsplit("-", 1)[1])) for r in tp["pairRows"]])
        self.assertEqual((5, 5, 5), (tp["pairs"], tp["r2Counted"], tp["r1Counted"]))


class SourceInRecordingTests(Base):
    """C-00 06.10: a window whose measured thing is not there FAILS the tool; no r2-Z3.json, every condition named."""

    CASES = {
        "achievedCount": ({"r1": {("tp", 3): {"achieved": 38}}}, "configured/forced/achieved 40/40/38"),
        "forcedTarget": ({"r2": {("tp", 2): {"forced": 39}}}, "configured/forced/achieved 40/39/40"),
        "no body on screen": ({"r2": {("topdown", 1): {"on_screen": 0}}}, "enemiesOnScreen 0"),
        "setpass recorder": ({"r1": {("tp", 1): {"setpass": -1.0}}}, "setPassCallsAvg -1.0"),
        "batches zero": ({"r2": {("tp", 5): {"batches": 0.0}}}, "batchesAvg 0.0"),
        "triangles recorder": ({"r2": {("topdown", 4): {"triangles": -1.0}}}, "trianglesAvg -1.0"),
        "armour flag R1": ({"r1": {("tp", 2): {"armor": False}}}, "no -ddArmor all"),
        "armour not fitted": ({"r2": {("tp", 2): {"armor_note": "configured/ledger/forced 40/40/40"}}}, "no 'armor all: N pieces fitted'"),
        "no armour piece": ({"r1": {("topdown", 3): {"armor_note": "configured/ledger/forced 40/40/40 | armor all: 0 pieces fitted"}}},
                            "with N > 0"),
        "sweep": ({"r2": {("tp", 1): {"sweep": True}}}, "cameraSweep is not false"),
        "load dropped": ({"r1": {("topdown", 2): {"active_min": 39}}}, "activeMin 39 < 40"),
        "pose moved in R1": ({"r1": {("tp", 4): {"sampled": (0.6, -4.0)}}}, "0.600 m from"),
        "pose asked differently": ({"r1": {("tp", 4): {"start": "0,-4,90"}}}, "requested pose [0.0, -4.0, 90.0]"),
        "no readability record": ({"r2": {("tp", 3): {"skip": ("06-readability.json",)}}}, "06-readability.json missing"),
        "no force record": ({"r1": {("tp", 3): {"skip": ("02-force-40.json",)}}}, "02-force-40.json missing"),
        "screen size": ({"r1": {("tp", 1): {"screen": (1280, 720)}}}, "screen sizes differ"),
    }

    def test_each_broken_condition_fails_without_an_artifact(self):
        for name, (overrides, message) in self.CASES.items():
            with self.subTest(name):
                shutil.rmtree(self.evidence)
                self.evidence.mkdir()
                z3_tree(self.evidence, **overrides)
                write_static(self.evidence)
                code, _, err = self.z3_only()
                self.assertEqual(1, code)
                self.assertFalse(self.artifact.exists())
                self.assertIn("FAIL (the source is not in the recording)", err)
                self.assertIn(message, err)
                ratios = strict(self.evidence / "r2" / "z3-ratios.json")
                self.assertEqual(("FAIL", None), (ratios["status"], ratios["values"]))

    def test_a_window_under_another_commit_fails(self):
        z3_tree(self.evidence)
        attempt = self.evidence / "z3/perf-r1/perf-sinematik-tp-2/attempt.json"
        data = json.loads(attempt.read_text())
        data["executionCommit"] = FINAL                   # an R2 window filed under the R1 root
        attempt.write_text(json.dumps(data))
        write_static(self.evidence)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertIn(f"executionCommit {FINAL} is not {R1}", err)

    def test_the_same_build_on_both_sides_fails(self):
        for mode in ("topdown", "tp"):
            for i in range(1, 6):
                window(self.evidence / "z3/perf", "sinematik", mode, i, FINAL, BIN_R2)
                window(self.evidence / "z3/perf-r1", "sinematik", mode, i, R1, BIN_R2)
        write_static(self.evidence)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertIn("two different builds expected", err)

    def test_a_consistent_pose_other_than_z3_start_fails(self):
        for mode in ("topdown", "tp"):                 # both builds agree with each other, but not on the z3 pose
            for i in range(1, 6):
                window(self.evidence / "z3/perf", "sinematik", mode, i, FINAL, BIN_R2, start="48,48,225")
                window(self.evidence / "z3/perf-r1", "sinematik", mode, i, R1, BIN_R1, start="48,48,225")
        write_static(self.evidence)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertIn(f"is not Z3_START {z3.Z3_START}", err)
        self.assertEqual("0,-4,0", z3.Z3_START)

    def test_a_sampled_pose_within_tolerance_passes(self):
        z3_tree(self.evidence, r1={("tp", i): {"sampled": (0.1, -4.05)} for i in range(1, 6)})
        write_static(self.evidence)
        self.assertEqual(0, self.z3_only()[0])
        self.assertEqual(1, self.z3_only("--pose-tolerance", "0.05")[0])


class StaticTests(Base):
    def test_without_the_static_file_r2_z3_is_not_written_but_the_ratios_are(self):
        z3_tree(self.evidence)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertFalse(self.artifact.exists())
        self.assertIn("BLOCKED (static keys)", err)
        self.assertIn("r2/z3-static.json", err)
        ratios = strict(self.evidence / "r2" / "z3-ratios.json")
        self.assertEqual(("dd-r2-z3-ratios-v1", "OK"), (ratios["schema"], ratios["status"]))
        self.assertEqual({"setPassRatio": 1.0, "batchRatio": 1.0, "trianglesRatio": 1.0}, ratios["values"])

    def test_a_bad_static_file_blocks(self):
        bad = {"another commit": dict(commit=R1), "another schema": dict(schema="dd-r2-measure-v1"),
               "a key missing": dict(values={k: v for k, v in STATIC.items() if k != "bonesMax"}),
               "an unknown key": dict(values=dict(STATIC, bonesTotal=900)),
               "a boolean": dict(values=dict(STATIC, bonesMax=True)), "a string": dict(values=dict(STATIC, bonesMax="74")),
               "a sentinel": dict(values=dict(STATIC, bonesMax=-1)), "a zero": dict(values=dict(STATIC, shippedCharacterMB=0))}
        z3_tree(self.evidence)
        for name, kwargs in bad.items():
            with self.subTest(name):
                write_static(self.evidence, **kwargs)
                code, _, err = self.z3_only()
                self.assertEqual(1, code)
                self.assertFalse(self.artifact.exists())
                self.assertIn("BLOCKED (static keys)", err)
        (self.evidence / "r2" / "z3-static.json").write_text('{"schema": "dd-r2-z3-static-v1", "values": {"bonesMax": NaN}}')
        self.assertEqual(1, self.z3_only()[0])

    def test_a_static_value_over_its_limit_is_a_gate_fail_not_a_tool_fail(self):
        z3_tree(self.evidence)
        write_static(self.evidence, values=dict(STATIC, bonesMax=75))
        self.assertEqual(0, self.z3_only()[0])
        row = r2_gates.evaluate_gate(Z3_GATE, FINAL, measure=strict(self.artifact), cases=Z3_CASES)
        self.assertEqual("FAIL", row["status"])
        self.assertIn("bonesMax 75 <= 74 is false", row["reason"])
        self.assertEqual(8, row["values"]["materialsPerFamilyCrowdTotal"])   # the report key rides along


def b9_tree(evidence, poses, windows=3, overrides=None):
    for name, start, tier, modes in poses:
        for mode in modes:
            for i in range(1, windows + 1):
                window(Path(evidence) / "b9" / name, tier, mode, i, FINAL, BIN_R2, start=start, armor=False,
                       setpass=300.0 + i, frame_mean=20.0 + i, **(overrides or {}).get((name, mode, i), {}))


POSES = [("yuksek-sw", "-48,-48,45", "yuksek", ("topdown", "tp", "fp")), ("sin-plazan", "0,-4,0", "sinematik", ("tp",))]


class P3Tests(Base):
    def test_one_cell_per_pose_tier_and_mode_with_medians(self):
        b9_tree(self.evidence, POSES)
        (self.evidence / "b9" / "yuksek-sw" / "ug" / "ug-perf-yuksek-tp-1").mkdir(parents=True)   # guard folders are not cells
        code, out, err = self.main(gates="P3")
        self.assertEqual(0, code, err)
        data = strict(self.evidence / "r2" / "r2-P3.json")
        self.assertEqual(("dd-r2-measure-v1", "P3", FINAL), (data["schema"], data["gate"], data["commit"]))
        self.assertEqual({"cells": 4}, data["values"])                     # plain numbers only; the table is top level
        self.assertEqual(4, len(data["cells"]))
        self.assertEqual(12, data["samples"])
        cell = next(c for c in data["cells"] if (c["pose"], c["mode"]) == ("yuksek-sw", "fp"))
        self.assertEqual((3, 302.0, 22.0, 12, [-48.0, -48.0, 45.0], False),
                         (cell["windows"], cell["setPassCallsAvg"], cell["frameMeanMs"], cell["enemiesOnScreen"], cell["start"], cell["armorAll"]))
        row = r2_gates.evaluate_gate(next(g for g in CONTRACT["gates"] if g["id"] == "P3"), FINAL, measure=data)
        self.assertEqual(("REPORT", {"cells": 4}), (row["status"], row["values"]))
        self.assertIn("P3 written: 4 cells", out)

    def test_the_source_rule_holds_for_the_report_too(self):
        b9_tree(self.evidence, POSES, overrides={("sin-plazan", "tp", 2): {"on_screen": 0}})
        code, _, err = self.main(gates="P3")
        self.assertEqual(1, code)
        self.assertFalse((self.evidence / "r2" / "r2-P3.json").exists())
        self.assertIn("b9/sin-plazan/perf-sinematik-tp-2: 06-readability enemiesOnScreen 0", err)

    def test_one_pose_folder_holds_one_pose(self):
        b9_tree(self.evidence, POSES, overrides={("yuksek-sw", "fp", 1): {"sampled": (-47.0, -48.0)}})
        code, _, err = self.main(gates="P3")
        self.assertEqual(1, code)
        self.assertIn("1.000 m from", err)

    def test_the_b9_list_names_every_cell_and_its_start(self):
        b9_tree(self.evidence, POSES)
        listed = {"source": "test", "poses": [
            {"name": "yuksek-sw", "start": "-48,-48,45", "tiers": "yuksek", "modes": "topdown,tp,fp"},
            {"name": "sin-plazan", "start": "0,-4,0", "tiers": "sinematik", "modes": "tp"},
            {"name": "sin-ne", "start": "48,48,225", "tiers": "sinematik", "modes": "tp"}]}
        path = self.evidence / "b9-poses.json"
        path.write_text(json.dumps(listed))
        code, _, err = self.main("--b9-poses", str(path), gates="P3")
        self.assertEqual(1, code)
        self.assertIn("b9/sin-ne/sinematik-tp: listed in the B9 poses, no cell", err)
        listed["poses"] = listed["poses"][:2]
        listed["poses"][1]["start"] = "0,-4,180"
        path.write_text(json.dumps(listed))
        code, _, err = self.main("--b9-poses", str(path), gates="P3")
        self.assertEqual(1, code)
        self.assertIn("is not the B9 list's [0.0, -4.0, 180.0]", err)
        listed["poses"][1]["start"] = "0,-4,0"
        path.write_text(json.dumps(listed))
        self.assertEqual(0, self.main("--b9-poses", str(path), gates="P3")[0])

    def test_no_b9_run_is_blocked(self):
        code, _, err = self.main(gates="P3")
        self.assertEqual(1, code)
        self.assertIn("no b9/", err)


class HardeningTests(Base):
    """gt 06.10 (K/wp-038m/kadro-0610/gt-z3.md): KB-1 and B-1..B-6."""

    def test_a_crashed_window_fails_its_cell_k8(self):
        # KB-1: wp078_perf re-runs a crashed window at the next index; the crash must not vanish from the record
        z3_tree(self.evidence, r2={("tp", 2): {"status": "failed", "exit_code": 1}, ("tp", 4): {"status": "failed", "exit_code": 1}})
        for i in (6, 7):
            window(self.evidence / "z3/perf", "sinematik", "tp", i, FINAL, BIN_R2)
        write_static(self.evidence)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertFalse(self.artifact.exists())
        self.assertIn("FAIL (the source is not in the recording)", err)
        for i in (2, 4):
            self.assertIn(f"z3/perf/perf-sinematik-tp-{i}: attempt did not complete on a clean host", err)

    def test_a_host_only_discard_is_still_skipped(self):
        z3_tree(self.evidence, r2={("tp", 2): {"status": "preflight-failed", "exit_code": 75}})
        window(self.evidence / "z3/perf", "sinematik", "tp", 6, FINAL, BIN_R2)
        write_static(self.evidence)
        self.assertEqual(0, self.z3_only()[0])

    def test_a_crashed_b9_window_fails_the_report(self):
        b9_tree(self.evidence, POSES[1:], overrides={("sin-plazan", "tp", 2): {"status": "failed", "exit_code": 1}})
        code, _, err = self.main(gates="P3")
        self.assertEqual(1, code)
        self.assertIn("b9/sin-plazan/perf-sinematik-tp-2: attempt did not complete on a clean host", err)

    def test_one_attempt_counts_once(self):
        z3_tree(self.evidence)
        write_static(self.evidence)
        r2 = self.evidence / "z3/perf"
        shutil.copytree(r2 / "perf-sinematik-tp-3", r2 / "perf-sinematik-tp-03")
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertIn("perf-sinematik-tp-03: window index is not 1..n", err)
        self.assertIn("ffff-sinematik-tp-3 is also", err)
        self.assertIn("(one attempt counted twice)", err)
        shutil.rmtree(r2 / "perf-sinematik-tp-03")
        (r2 / "perf-sinematik-tp-8").symlink_to(r2 / "perf-sinematik-tp-3", target_is_directory=True)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertIn("perf-sinematik-tp-8: the same folder as z3/perf/perf-sinematik-tp-3", err)

    def test_the_two_builds_need_two_commits(self):
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(64, z3.main(["--evidence", str(self.evidence), "--commit", FINAL, "--r1-commit", FINAL]))

    def test_thresholds_only_tighten_and_are_recorded(self):
        z3_tree(self.evidence)
        write_static(self.evidence)
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(64, z3.main(["--evidence", str(self.evidence), "--commit", FINAL, "--r1-commit", R1, "--min-pairs", "3"]))
            self.assertEqual(64, z3.main(["--evidence", str(self.evidence), "--commit", FINAL, "--r1-commit", R1,
                                          "--pose-tolerance", "0.5"]))
        self.assertEqual(1, self.z3_only("--min-pairs", "6")[0])          # tighter than the run: BLOCKED, allowed
        self.assertEqual(0, self.z3_only()[0])
        data = strict(self.artifact)
        self.assertEqual((5, 0.25), (data["minPairs"], data["poseToleranceM"]))

    def test_a_stale_artifact_is_set_aside_not_bound(self):
        z3_tree(self.evidence)
        static = write_static(self.evidence)
        self.assertEqual(0, self.z3_only()[0])
        static.unlink()
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertFalse(self.artifact.exists())
        self.assertEqual(1, len(list((self.evidence / "r2").glob("r2-Z3.json.stale-*"))))
        self.assertIn("set aside as r2-Z3.json.stale-", err)


class CliTests(Base):
    def test_both_artifacts_in_one_run(self):
        z3_tree(self.evidence)
        write_static(self.evidence)
        b9_tree(self.evidence, POSES[1:])
        code, out, err = self.main()
        self.assertEqual(0, code, err)
        for name in ("r2-Z3.json", "r2-P3.json", "z3-ratios.json"):
            self.assertTrue((self.evidence / "r2" / name).is_file(), name)

    def test_one_side_missing_still_writes_the_other_and_exits_1(self):
        b9_tree(self.evidence, POSES[1:])
        code, _, err = self.main()
        self.assertEqual(1, code)
        self.assertTrue((self.evidence / "r2" / "r2-P3.json").is_file())
        self.assertFalse(self.artifact.exists())
        self.assertIn("Z3: BLOCKED", err)

    def test_usage(self):
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(64, z3.main(["--evidence", str(self.evidence), "--commit", "abc", "--r1-commit", R1]))
            self.assertEqual(64, z3.main(["--evidence", str(self.evidence), "--commit", FINAL]))              # Z3 needs R1
            self.assertEqual(64, z3.main(["--evidence", str(self.evidence / "nope"), "--commit", FINAL, "--r1-commit", R1]))
            self.assertEqual(64, z3.main(["--evidence", str(self.evidence), "--commit", FINAL, "--r1-commit", R1, "--gates", "Z9"]))
        b9_tree(self.evidence, POSES[1:])
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, z3.main(["--evidence", str(self.evidence), "--commit", FINAL, "--gates", "P3"]))  # P3 needs no R1

    def test_out_dir_and_static_paths(self):
        z3_tree(self.evidence)
        static = write_static(self.evidence)
        moved = self.evidence / "elsewhere" / "static.json"
        moved.parent.mkdir()
        static.rename(moved)
        out_dir = self.evidence / "out"
        code, _, err = self.z3_only("--static", str(moved), "--out-dir", str(out_dir))
        self.assertEqual(0, code, err)
        self.assertEqual("elsewhere/static.json", strict(out_dir / "r2-Z3.json")["static"]["path"])


class LintTests(unittest.TestCase):
    def test_the_prose_makes_z3_and_p3_chain_written_and_agrees_with_the_json(self):
        # r2_gates_lint reads "No owner test" in a "## <gate>" section as chain-made (P5's mechanism); no C# needed, so this
        # holds in a sparse tools/docs worktree too
        import r2_gates_lint as lint
        prose = lint.parse_prose((Path(__file__).resolve().parents[1] / "r2-measure-contract.md").read_text(encoding="utf-8"))
        self.assertTrue({"Z3", "P3"} <= prose["chain_made"])
        findings = lint.lint(CONTRACT, prose, [], [], None)
        mine = sorted((f.level, f.code, f.gate) for f in findings if f.gate in ("Z3", "P3"))
        self.assertIn(("INFO", "CHAIN-MADE", "Z3"), mine)
        self.assertIn(("INFO", "CHAIN-MADE", "P3"), mine)
        # no NO-WRITER, PROSE-KEY or PROSE-LIMIT; with no C# given, the Z3 static writer's test identity (measuredBy / tests) is
        # not checkable here — test_r2_gates_lint's real-repo run checks it against the C# tree
        csharp_only = {"MEASUREDBY-NOTEST", "TEST-MISSING"}
        self.assertEqual([], [m for m in mine if m[0] != "INFO" and not (m[2] == "Z3" and m[1] in csharp_only)])
        self.assertIn("tools/qa/r2_z3.py", prose["paths"])
        self.assertTrue((REPO / "tools/qa/r2_z3.py").is_file())


if __name__ == "__main__":
    unittest.main()


class ModesTests(Base):
    """C-00 06.10 (user: top down only, tests too): wp078_accept --modes reaches r2_z3 --modes; a left-out Z3 mode is
    outOfScope, not too few pairs, and the worst is taken over the measured cells."""

    def test_a_topdown_run_writes_z3_from_the_topdown_cell_and_names_tp_out_of_scope(self):
        z3_tree(self.evidence, modes=("topdown",), r2={("topdown", i): {"setpass": 104.0} for i in range(1, 6)})
        write_static(self.evidence)
        code, out, err = self.z3_only("--modes", "topdown")
        self.assertEqual(0, code, err)
        data = strict(self.artifact)
        self.assertEqual(["topdown"], [c["mode"] for c in data["cells"]])
        self.assertEqual({"modes": ["tp"], "reason": r2_gates.SCOPE_REASON}, data["outOfScope"])
        self.assertEqual(1.04, data["values"]["setPassRatio"])
        self.assertEqual(5, data["samples"])
        self.assertIn("worst of topdown;", out)
        row = r2_gates.evaluate_gate(Z3_GATE, FINAL, measure=data, cases=Z3_CASES, modes=("topdown",))
        self.assertEqual(("PASS", ["topdown"], ["tp"]), (row["status"], row["modes"], row["outOfScopeModes"]), row["reason"])

    def test_without_modes_the_missing_tp_cell_still_blocks(self):
        z3_tree(self.evidence, modes=("topdown",))
        write_static(self.evidence)
        code, _, err = self.z3_only()
        self.assertEqual(1, code)
        self.assertIn("sinematik-tp: 0 pairs", err)

    def test_modes_must_be_z3_modes(self):
        for bad in ("fp", "topdown,topdown", ""):
            with self.subTest(modes=bad):
                self.assertEqual(64, self.z3_only("--modes", bad)[0])

    def test_the_scope_reason_is_the_contracts(self):
        self.assertEqual(r2_gates.SCOPE_REASON, z3.SCOPE_REASON)
