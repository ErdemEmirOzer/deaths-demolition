"""WP-091 (C-00 05.10): tools/qa/r2-gates.json + r2_gates.py — the R2 threshold contract and its gate evaluator."""
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import r2_gates as r2  # noqa: E402

CONTRACT = (Path(__file__).resolve().parents[1] / "r2-gates.json").read_text(encoding="utf-8")
FINAL = "b" * 40


def gate(gid):
    return next(g for g in r2.load_contract(CONTRACT)["gates"] if g["id"] == gid)


def measure(gid, **values):
    return {"schema": "dd-r2-measure-v1", "gate": gid, "commit": FINAL, "values": values}


class ContractTests(unittest.TestCase):
    def test_the_approved_contract_loads_and_covers_every_area(self):
        data = r2.load_contract(CONTRACT)
        ids = [g["id"] for g in data["gates"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual({"hero", "locomotion", "zombie", "sound", "perf", "ui"}, {g["area"] for g in data["gates"]})
        for gid in ("H1", "H4", "H4a", "H4b", "L1", "L1t", "L3", "Z3", "Z3b", "S1", "S4", "U1", "U3"):
            self.assertIn(gid, ids)

    def test_approved_thresholds_are_the_numbers_in_the_draft(self):
        # a changed threshold must be a reviewed edit of this file: pin the C-00 05.10 values
        pinned = {("H1", "maxCm"): 1.0, ("L1", "avg"): 0.05, ("L1t", "avg"): 0.10, ("L3", "limbDegPerFrameMax"): 20.0,
                  ("Z3", "bonesMax"): 74, ("Z3b", "zombieSetPass"): 14, ("S1", "below400ShareMin"): 0.20,
                  ("S4", "hallMsMax"): 2000, ("U1", "lettersTotalReduction"): 0.40, ("H4b", "leakDegMax"): 1.0,
                  ("H4", "yawRollExcessDegMax"): 1.0, ("H4a", "armOffscreenFrames"): 0, ("H4a", "armInCameraFrames"): 0,
                  ("S5", "poolMax"): 3}   # C-00 05.10: concurrent casing voices (was 8, which mixed in the airborne queue)
        for (gid, key), limit in pinned.items():
            check = next(c for c in gate(gid)["checks"] if c["key"] == key)
            self.assertEqual(limit, check["limit"], (gid, key))

    def test_invalid_contracts_are_refused(self):
        base = json.loads(CONTRACT)
        cases = []
        dup = json.loads(CONTRACT); dup["gates"].append(dict(dup["gates"][0])); cases.append(dup)
        bad_op = json.loads(CONTRACT); bad_op["gates"][0]["checks"][0]["op"] = "<"; cases.append(bad_op)
        report_with_check = json.loads(CONTRACT)
        next(g for g in report_with_check["gates"] if g["kind"] == "report")["checks"] = [{"key": "x", "op": "<=", "limit": 1}]
        cases.append(report_with_check)
        empty_gate = json.loads(CONTRACT); empty_gate["gates"].append({"id": "X9", "kind": "gate", "owner": "S-10"}); cases.append(empty_gate)
        for data in cases:
            with self.assertRaises(r2.ContractError):
                r2.load_contract(json.dumps(data))
        self.assertTrue(base["gates"])


class MeasuredByTests(unittest.TestCase):
    def test_the_contract_runs_s02s_h4_and_h4b_sessions_outside_the_quiet_window(self):
        steps = r2.measurement_steps(r2.load_contract(CONTRACT), quiet=False)
        self.assertEqual({"r2-H4": ["H4"], "r2-H4b": ["H4b"]}, {s["id"]: s["gates"] for s in steps if s["id"] in ("r2-H4", "r2-H4b")})
        self.assertEqual("S-06", gate("H4a")["owner"])
        self.assertNotIn("measuredBy", gate("H4a"))   # S-06 binds its FpKollar measurement when it lands

    def contract(self, **by_gate):
        data = json.loads(CONTRACT)
        for g in data["gates"]:
            g.pop("measuredBy", None)   # synthetic sessions only, independent of what the contract binds today
            if g["id"] in by_gate:
                g["measuredBy"] = by_gate[g["id"]]
        return json.dumps(data)

    def test_steps_group_gates_sharing_one_session_and_split_by_quiet(self):
        loco = {"suite": "playmode", "filter": "LocomotionR2Tests", "quiet": False}
        text = self.contract(L1=loco, L1t=loco, L2=loco, S3={"suite": "playmode", "filter": "AudioR2Dense40", "quiet": True})
        data = r2.load_contract(text)
        normal, quiet = r2.measurement_steps(data, False), r2.measurement_steps(data, True)
        self.assertEqual([{"id": "r2-L1", "suite": "playmode", "filter": "LocomotionR2Tests", "env": {}, "quiet": False,
                           "gates": ["L1", "L1t", "L2"]}], normal)
        self.assertEqual(["S3"], quiet[0]["gates"])

    def test_a_null_value_is_blocked_and_a_note_is_ignored(self):
        # S-02 05.10: R2Olcum writes null for a key whose measurement has not landed (H3 contactDeviationFramesMax
        # until S-06 B2c), with a top-level "note"
        measure = {"schema": "dd-r2-measure-v1", "gate": "H3", "commit": "a" * 40, "note": "contact from S-06 B2c",
                   "values": {"missingEvents": 0, "contactDeviationFramesMax": None, "floatingMagazineFrames": 0}}
        row = r2.evaluate_gate(gate("H3"), "a" * 40, measure=measure)
        self.assertEqual("BLOCKED", row["status"])
        self.assertIn("contactDeviationFramesMax", row["reason"])
        measure["values"]["contactDeviationFramesMax"] = 1
        self.assertEqual("PASS", r2.evaluate_gate(gate("H3"), "a" * 40, measure=measure)["status"])

    def test_h3_belongs_to_s02_and_p5_shows_its_floor(self):
        self.assertEqual("S-02", gate("H3")["owner"])     # C-00 05.10: the magazine state machine is S-02's
        self.assertEqual("DD.Prototype.Tests.PlayMode.WeaponReloadMagazineTests.H3_SarjorluSilahlar_VuruslarTam_HavadaKareYok",
                         gate("H3")["measuredBy"]["filter"])
        self.assertIn("floorLimited", gate("P5")["report"])

    def test_s5_airborne_queue_is_reported_never_gated(self):
        self.assertEqual(["queueMax"], gate("S5")["report"])
        self.assertNotIn("queueMax", [c["key"] for c in gate("S5")["checks"]])

    def test_the_contract_binds_s08s_kit_session_in_the_quiet_window_with_its_switches(self):
        data = r2.load_contract(CONTRACT)
        kit = [s for s in r2.measurement_steps(data, quiet=True) if s["id"] == "r2-S1"]
        self.assertEqual(1, len(kit))
        self.assertEqual(["S1", "S2"], kit[0]["gates"])
        self.assertEqual({"DD_KIT_VARIANTS": "A", "DD_KIT_WEAPONS": "all"}, kit[0]["env"])
        normal = {s["id"]: s for s in r2.measurement_steps(data, quiet=False)}
        self.assertEqual("editmode", normal["r2-U3"]["suite"])
        self.assertEqual("DD.Prototype.Tests.PlayMode.UiTextInventoryTests.U1_GorunurHarf_AnaMenuPanellerVeSecim_EnTr",
                         normal["r2-U1"]["filter"])   # [Explicit]: the full name, never a class regex
        self.assertIn("r2-S5", normal)

    def test_the_same_test_under_other_switches_is_another_session(self):
        a = {"suite": "playmode", "filter": "Kit", "quiet": False, "env": {"DD_KIT_WEAPONS": "all"}}
        b = {"suite": "playmode", "filter": "Kit", "quiet": False, "env": {"DD_KIT_WEAPONS": "smg_1"}}
        steps = r2.measurement_steps(r2.load_contract(self.contract(S1=a, S2=b, S5=dict(a))), False)
        self.assertEqual([["S1", "S5"], ["S2"]], [s["gates"] for s in steps])

    def test_measured_by_is_validated(self):
        for bad in ({"H5": {"suite": "playmode", "filter": "x"}},                 # a human gate is not measured
                    {"L1": {"suite": "batch", "filter": "x"}},                    # unknown suite
                    {"L1": {"suite": "playmode", "filter": ""}},                  # empty filter
                    {"L1": {"suite": "playmode", "filter": "x", "quiet": "yes"}},  # quiet must be a boolean
                    {"S1": {"suite": "playmode", "filter": "x", "env": {"DD_R2_COMMIT": "0"}}},  # the chain's own variables
                    {"S1": {"suite": "playmode", "filter": "x", "env": {"kit": "all"}}},          # UPPER_CASE names only
                    {"S1": {"suite": "playmode", "filter": "x", "env": {"DD_KIT_WEAPONS": 1}}}):  # string values only
            with self.subTest(bad=bad):
                with self.assertRaises(r2.ContractError):
                    r2.load_contract(self.contract(**bad))


class EvaluateTests(unittest.TestCase):
    def test_measured_within_limits_passes(self):
        row = r2.evaluate_gate(gate("L1"), FINAL, measure=measure("L1", avg=0.031, p95=0.074))
        self.assertEqual(("PASS", {"avg": 0.031, "p95": 0.074}), (row["status"], row["values"]))

    def test_one_value_over_its_limit_fails_and_says_which(self):
        row = r2.evaluate_gate(gate("L1"), FINAL, measure=measure("L1", avg=0.031, p95=0.12))
        self.assertEqual("FAIL", row["status"])
        self.assertIn("p95 0.12 <= 0.1 is false", row["reason"])

    def test_no_artifact_or_missing_value_is_blocked(self):
        self.assertEqual("BLOCKED", r2.evaluate_gate(gate("S1"), FINAL)["status"])
        self.assertEqual("BLOCKED", r2.evaluate_gate(gate("L1"), FINAL, measure=measure("L1", avg=0.03))["status"])

    def test_other_commit_or_wrong_gate_artifact_fails(self):
        other = dict(measure("L1", avg=0.01, p95=0.02), commit="c" * 40)
        self.assertEqual("FAIL", r2.evaluate_gate(gate("L1"), FINAL, measure=other)["status"])
        self.assertEqual("FAIL", r2.evaluate_gate(gate("L1"), FINAL, measure=measure("L2", avg=0.01, p95=0.02))["status"])

    def test_required_test_must_have_passed(self):
        g = gate("Z1")
        self.assertEqual("BLOCKED", r2.evaluate_gate(g, FINAL, cases=None)["status"])
        self.assertEqual("BLOCKED", r2.evaluate_gate(g, FINAL, cases={})["status"])
        self.assertEqual("FAIL", r2.evaluate_gate(g, FINAL, cases={g["tests"][0]: "Failed"})["status"])
        self.assertEqual("PASS", r2.evaluate_gate(g, FINAL, cases={g["tests"][0]: "Passed"})["status"])

    def test_human_review_verdicts(self):
        g = gate("H5")
        review = {"schema": "dd-r2-review-v1", "gate": "H5", "commit": FINAL, "reviewer": "AD", "verdict": "KABUL", "must": []}
        self.assertEqual("BLOCKED", r2.evaluate_gate(g, FINAL)["status"])
        self.assertEqual("PASS", r2.evaluate_gate(g, FINAL, review=review)["status"])
        self.assertEqual("FAIL", r2.evaluate_gate(g, FINAL, review=dict(review, verdict="RED"))["status"])
        corrections = dict(review, verdict="KABUL_DÜZELTMELERLE", must=[{"id": "MUST-1", "closed": False}])
        self.assertEqual("FAIL", r2.evaluate_gate(g, FINAL, review=corrections)["status"])
        corrections["must"][0]["closed"] = True
        self.assertEqual("PASS", r2.evaluate_gate(g, FINAL, review=corrections)["status"])

    def test_report_entries_never_fail(self):
        row = r2.evaluate_gate(gate("P2"), FINAL, measure=measure("P2", flaggedCells=3, worstDeltaMs=4.0, worstDeltaPct=0.3))
        self.assertEqual("REPORT", row["status"])
        self.assertEqual("PASS", r2.summarize([row]))

    def test_summary_fail_beats_blocked(self):
        rows = [r2.evaluate_gate(gate("S1"), FINAL), r2.evaluate_gate(gate("L1"), FINAL, measure=measure("L1", avg=1, p95=1))]
        self.assertEqual("FAIL", r2.summarize(rows))
        self.assertEqual("BLOCKED", r2.summarize(rows[:1]))


if __name__ == "__main__":
    unittest.main()
