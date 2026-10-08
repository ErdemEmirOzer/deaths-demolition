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
                  ("Z3", "bonesMax"): 74, ("Z3b", "zombieSetPass"): 24, ("S1", "below400ShareMin"): 0.20,
                  # C-00 07.10 (S-10 proposal): every enabled weapon of the roster measured in the kit, or S1 / S2 cannot pass
                  ("S1", "weaponsMissing"): 0, ("S2", "weaponsMissing"): 0,
                  ("S4", "hallRt60MaxS"): 2.28, ("S4", "openRt60MinS"): 0.56,   # C-00 05.10 S4 v2: the tail's RT60 (spec ±20 %)
                  ("U1", "u1_default"): 0.40, ("U1", "u1_same_content"): 0.40, ("H4b", "leakDegMax"): 1.0,   # U1: C-00 05.10, two measures
                  ("H4", "yawRollExcessDegMax"): 1.0, ("H4a", "armOffscreenFrames"): 0, ("H4a", "armInCameraFrames"): 0,
                  ("S5", "poolMax"): 3,   # C-00 05.10: concurrent casing voices (was 8, which mixed in the airborne queue)
                  # C-00 05.10: Z3b from the fixed-mix R1 (16 / 567 / 88): +8, x1.45, +10 — the old 14/450/88 came from the
                  # director-mixed R1 6/310/78
                  ("Z3b", "zombieDraws"): 822, ("Z3b", "frameSetPass"): 98, ("Z3b", "sharedTextureGpuMB"): 96,
                  ("Z3b", "uniqueTexturesPerZombie"): 0,
                  # DEC-077 (user 16.09), asserted from C-00 06.10
                  ("Z1", "silhouetteChange"): 0.30, ("Z1", "deltaVBandMean"): 0.12, ("Z1", "deltaVBandMin"): 0.08,
                  # C-00 06.10 record (S-06 B2e AD MUST-2 / MUST-3)
                  ("H1b", "palmToWeaponMaxDeg"): 35.0, ("H1b", "fingertipGapMaxCm"): 1.5,   # palm: C-00 06.10, 35 PROVISIONAL
                  ("H1b", "palmGapMaxCm"): 1.5,   # C-00 06.10 H1b record (a)
                  ("H1c", "elbowMaxDeg"): 165.0, ("H1c", "reachMarginMinCm"): 1.0,   # C-00 06.10 (S-06 t-b2f4-off)
                  ("H6", "visibleShareMin"): 0.25, ("H6", "muzzleInsideSilhouetteFrames"): 0}
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
        self.assertEqual({"suite": "playmode", "filter": "DD.Art.Tests.PlayMode.AnimationTests.FpKollar_Kadraj_KolPayi10_KameraMesafesi15_ElSilahta", "quiet": False},
                         gate("H4a")["measuredBy"])   # S-06's writer, WP-083 R2 6711f57f (FP arms on)
        # C-00 06.10: H4a also requires its writer to pass (a failed or missing case is FAIL / BLOCKED, as H4)
        self.assertEqual([gate("H4a")["measuredBy"]["filter"]], gate("H4a")["tests"])

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
        self.assertEqual("DD.Prototype.Tests.PlayMode.UiOverflowMatrixTests.U2_Tasma_TrEn_Metin1ve125_MacUi12",
                         gate("U2")["measuredBy"]["filter"])

    def test_s03s_zombie_measurements_each_run_in_their_own_full_name_session(self):
        # [Explicit]: one full name per session — the only selection S-02 verified (05.10)
        steps = {s["id"]: s for s in r2.measurement_steps(r2.load_contract(CONTRACT), quiet=False)}
        for gid, name in (("Z2", "Z2_TierCiftleri_Ayrisir"), ("Z3b", "Z3b_40Zombi_SetPassPayi_DokuButcesi"),
                          ("Z4", "Z4_HerAile_YuzGozDis")):
            self.assertEqual("DD.Prototype.Tests.PlayMode.ZombieR2Tests." + name, steps["r2-" + gid]["filter"])
            self.assertEqual([gid], steps["r2-" + gid]["gates"])
        # C-00 05.10: the gate is the fixed tier mix 22/10/6/2; the round-30 worst mix is reported, never gates
        self.assertEqual(["zombieSetPassWorst", "zombieDrawsWorst", "sharedTextureGpuMBWorst"], gate("Z3b")["report"])

    def test_h1_reports_the_support_elbow_until_the_hero_v2_import(self):
        # C-00 05.10 (WP-082 hero v2 reach pre-study): a report key, not a check, until the import makes it <= 165
        self.assertEqual(["elbowMaxDeg", "holdOn"], gate("H1")["report"])
        self.assertEqual(["maxCm"], [c["key"] for c in gate("H1")["checks"]])
        row = r2.evaluate_gate(gate("H1"), FINAL, measure=measure("H1", maxCm=0.4, elbowMaxDeg=171.0))
        self.assertEqual("PASS", row["status"])
        self.assertEqual(171.0, row["values"]["elbowMaxDeg"])

    def test_h1_is_measured_on_the_default_player_path_in_its_own_full_name_session(self):
        # C-00 05.10 decision B: the gate is the default player path, so the session carries no switch (DD_HOLD_IK is the
        # owner's A/B evidence, never the gate value); [Explicit] → the full name (BEKLEYEN §18 EXPLICIT-UNRUN)
        steps = {s["id"]: s for s in r2.measurement_steps(r2.load_contract(CONTRACT), quiet=False)}
        self.assertEqual("DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus",
                         steps["r2-H1"]["filter"])
        self.assertEqual(("playmode", {}, ["H1"]), (steps["r2-H1"]["suite"], steps["r2-H1"]["env"], steps["r2-H1"]["gates"]))

    def test_u1_gates_both_measures_and_names_the_one_that_fails(self):
        # C-00 05.10 (BEKLEYEN §11, S-09 8ba256ec): the default path and the same content as R1 both gate (>= 0.40); the
        # smaller of the two, lettersTotalReduction, is reported
        self.assertEqual(["u1_default", "u1_same_content", "lettersPerScreenReductionMin", "unnamedSelectables"],
                         [c["key"] for c in gate("U1")["checks"]])
        self.assertEqual(["lettersTotalReduction"], gate("U1")["report"])
        dec109 = measure("U1", u1_default=0.4222, u1_same_content=0.1306, lettersTotalReduction=0.1306,
                         lettersPerScreenReductionMin=0.0, unnamedSelectables=0)   # S-09's 05.10 r2-U1.json (r2-u1s)
        row = r2.evaluate_gate(gate("U1"), FINAL, measure=dec109)
        self.assertEqual("FAIL", row["status"])
        self.assertIn("u1_same_content 0.1306 >= 0.4 is false", row["reason"])
        self.assertIn("lettersPerScreenReductionMin 0.0 >= 0.25 is false", row["reason"])
        self.assertNotIn("u1_default", row["reason"])
        both = dict(dec109, values=dict(dec109["values"], u1_same_content=0.41, lettersTotalReduction=0.41,
                                        lettersPerScreenReductionMin=0.25))
        self.assertEqual("PASS", r2.evaluate_gate(gate("U1"), FINAL, measure=both)["status"])
        # the single-number artifact main's test wrote before 8ba256ec carries neither measure: BLOCKED by name
        old = measure("U1", lettersTotalReduction=0.131, lettersPerScreenReductionMin=0.0, unnamedSelectables=0)
        row = r2.evaluate_gate(gate("U1"), FINAL, measure=old)
        self.assertEqual("BLOCKED", row["status"])
        self.assertIn("u1_default, u1_same_content", row["reason"])

    def test_h1b_is_measured_by_s06s_writer_and_h6_waits_for_its_own(self):
        # C-00 06.10: the rows came first; measuredBy lands after S-06's writer (WP-083 B2f) - a measuredBy nobody writes is a
        # lint ERROR. H6's writer has not come yet.
        # C-00 06.10 H1b record (b): the wrist and wrap keys are report-only; thetaSwitchCount's target is a note
        self.assertEqual(["palmToWeaponMaxDeg", "fingertipGapMaxCm", "palmGapMaxCm"], [c["key"] for c in gate("H1b")["checks"]])
        for gid, report in (("H1b", ["weaponsMeasured", "framesMeasured", "wristClampShare", "wristMaxDeg", "thetaSwitchCount"]),
                            ("H6", ["weaponsMeasured", "secondWorstShare"])):
            self.assertEqual(("S-06", "gate", report), (gate(gid)["owner"], gate(gid)["kind"], gate(gid)["report"]))
        self.assertEqual({"suite": "playmode", "filter": "DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1b_SolEl_Kavrama_"
                          "TumKlasikSilahlar_DurusYuruyus", "quiet": False}, gate("H1b")["measuredBy"])
        # S-06's WP-083 R2 writers (f114d24c H2 + H6, c001a5e1 L4 + L5), each run by full name in the r2 stage
        for gid, name in (("H2", "R2HeroGovdeTests.H2_SilahGovdeGomulmesi_TumKlasikSilahlar_DurusYuruyus"),
                          ("H6", "R2HeroGovdeTests.H6_TpSilahGorunurlugu_TumKlasikSilahlar_DurusYuruyus"),
                          ("L4", "R2LokomotionIkincilTests.L4_IkincilHareket_DurusSonrasiOturma"),
                          ("L5", "R2LokomotionIkincilTests.L5_Kalabalik_FazSenkronuCiftleri")):
            self.assertEqual({"suite": "playmode", "filter": "DD.Prototype.Tests.PlayMode." + name, "quiet": False},
                             gate(gid)["measuredBy"], gid)
        # ca1d743d: one locomotion course writes L1, L1t, L2 and L3 - one r2 session for the four
        course = "DD.Prototype.Tests.PlayMode.R2LokomotionOlcumTests.L1_L3_Lokomotion_8Yon_KalkisDurusDonus"
        steps = {s["id"]: s for s in r2.measurement_steps(r2.load_contract(CONTRACT), quiet=False)}
        self.assertEqual((course, ["L1", "L1t", "L2", "L3"]), (steps["r2-L1"]["filter"], steps["r2-L1"]["gates"]))
        row = r2.evaluate_gate(gate("H6"), FINAL, measure=measure("H6", visibleShareMin=0.21, muzzleInsideSilhouetteFrames=0,
                                                                  weaponsMeasured=10, secondWorstShare=0.33))
        self.assertEqual("FAIL", row["status"])
        self.assertIn("visibleShareMin 0.21 >= 0.25 is false", row["reason"])

    def test_h1b_judges_each_grasp_check_and_never_its_report_keys(self):
        # C-00 06.10 H1b record (a)/(b): three checks, each failing on its own; the wrist and wrap keys never fail it
        good = dict(palmToWeaponMaxDeg=28.0, fingertipGapMaxCm=0.8, palmGapMaxCm=0.9, weaponsMeasured=10, framesMeasured=480,
                    wristClampShare=0.4, wristMaxDeg=70.0, thetaSwitchCount=6)
        self.assertEqual("PASS", r2.evaluate_gate(gate("H1b"), FINAL, measure=measure("H1b", **good))["status"])
        for key, value, reason in (("palmToWeaponMaxDeg", 36.0, "palmToWeaponMaxDeg 36.0 <= 35.0 is false"),
                                   ("fingertipGapMaxCm", 1.6, "fingertipGapMaxCm 1.6 <= 1.5 is false"),
                                   ("palmGapMaxCm", 1.6, "palmGapMaxCm 1.6 <= 1.5 is false")):
            with self.subTest(key=key):
                row = r2.evaluate_gate(gate("H1b"), FINAL, measure=measure("H1b", **dict(good, **{key: value})))
                self.assertEqual("FAIL", row["status"])
                self.assertIn(reason, row["reason"])

    def test_h1c_gates_the_support_arm_reach_and_waits_for_s06s_writer(self):
        # C-00 06.10 (S-06 t-b2f4-off): H1 keeps reporting the elbow, H1c gates it with the reach margin
        self.assertEqual(["elbowMaxDeg", "holdOn"], gate("H1")["report"])
        self.assertEqual(("S-06", "gate"), (gate("H1c")["owner"], gate("H1c")["kind"]))
        self.assertEqual(["elbowMaxDeg", "reachMarginMinCm"], [c["key"] for c in gate("H1c")["checks"]])
        self.assertEqual({"suite": "playmode", "filter": "DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1c_SolKol_Erisim_"
                          "TumKlasikSilahlar_DurusYuruyus", "quiet": False}, gate("H1c")["measuredBy"])   # WP-083 B2f writer
        # one arm geometry (29.0 + 25.5 cm, S-02's reach pre-study): 161 deg leaves 0.7 cm - the elbow passes, the reach fails
        row = r2.evaluate_gate(gate("H1c"), FINAL, measure=measure("H1c", elbowMaxDeg=161.0, reachMarginMinCm=0.7))
        self.assertEqual("FAIL", row["status"])
        self.assertIn("reachMarginMinCm 0.7 >= 1.0 is false", row["reason"])
        self.assertNotIn("elbowMaxDeg", row["reason"])
        ok = measure("H1c", elbowMaxDeg=144.0, reachMarginMinCm=2.6)
        self.assertEqual("PASS", r2.evaluate_gate(gate("H1c"), FINAL, measure=ok)["status"])

    def test_s5_airborne_queue_is_reported_never_gated(self):
        self.assertEqual(["queueMax"], gate("S5")["report"])
        self.assertNotIn("queueMax", [c["key"] for c in gate("S5")["checks"]])

    def test_the_contract_binds_s08s_kit_session_in_the_quiet_window_with_its_switches(self):
        data = r2.load_contract(CONTRACT)
        kit = [s for s in r2.measurement_steps(data, quiet=True) if s["id"] == "r2-S1"]
        self.assertEqual(1, len(kit))
        self.assertEqual(["S1", "S2"], kit[0]["gates"])
        edc = [s for s in r2.measurement_steps(data, quiet=True) if s["id"] == "r2-S4"]   # S-08 f53062f2, all 10 weapons
        self.assertEqual(("DD.Prototype.Tests.PlayMode.AudioSliceTests.MekanKuyrugu_EDC_Olcum", {}), (edc[0]["filter"], edc[0]["env"]))
        # A = today's game (the S1/S2 gates), C = the AP-027 v4 prototype (S1 report below400ShareMinV4, C-00 05.10)
        self.assertEqual({"DD_AP027_DIR": "{KPROJ}/asset-production/AP-027/exports/v001-prototip",
                          "DD_KIT_VARIANTS": "AC", "DD_KIT_WEAPONS": "all"}, kit[0]["env"])
        self.assertEqual("/k/asset-production/AP-027/exports/v001-prototip",
                         r2.expand_env(kit[0]["env"], "/k")["DD_AP027_DIR"])
        self.assertEqual(["below400ShareMinV4", "weaponsMeasured", "rosterEnabled"], gate("S1")["report"])
        self.assertEqual(["weaponsMeasured", "rosterEnabled"], gate("S2")["report"])   # C-00 07.10: coverage, reported
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
                    {"S1": {"suite": "playmode", "filter": "x", "env": {"DD_KIT_WEAPONS": 1}}},  # string values only
                    {"S1": {"suite": "playmode", "filter": "x", "env": {"DD_AP027_DIR": "{HOME}/x"}}}):  # only {KPROJ}
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
        # a gate judged on its required test alone (Z1 was one until C-00 06.10 gave it the DEC-077 checks)
        g = {"id": "ZT", "area": "zombie", "kind": "gate", "owner": "S-03", "title": "tests-only", "tests": list(gate("Z1")["tests"])}
        self.assertEqual("BLOCKED", r2.evaluate_gate(g, FINAL, cases=None)["status"])
        self.assertEqual("BLOCKED", r2.evaluate_gate(g, FINAL, cases={})["status"])
        self.assertEqual("FAIL", r2.evaluate_gate(g, FINAL, cases={g["tests"][0]: "Failed"})["status"])
        self.assertEqual("PASS", r2.evaluate_gate(g, FINAL, cases={g["tests"][0]: "Passed"})["status"])

    def test_z1_needs_the_dec077_numbers_and_the_passed_test(self):
        # C-00 06.10: DEC-077 (user 16.09) is asserted, not only reported; S-03 measured 0.265 / 0.019 / 0.000 on v002
        g = gate("Z1")
        ok = {g["tests"][0]: "Passed"}
        self.assertEqual("BLOCKED", r2.evaluate_gate(g, FINAL, cases=ok)["status"])
        low = measure("Z1", silhouetteChange=0.265, deltaVBandMean=0.019, deltaVBandMin=0.0)
        self.assertEqual("FAIL", r2.evaluate_gate(g, FINAL, measure=low, cases=ok)["status"])
        good = measure("Z1", silhouetteChange=0.31, deltaVBandMean=0.13, deltaVBandMin=0.09)
        self.assertEqual("PASS", r2.evaluate_gate(g, FINAL, measure=good, cases=ok)["status"])

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

    def test_a_measurement_that_counted_nothing_is_blocked(self):
        # S-10 06.10 (§15): an S4 run that measured no weapon wrote min = float.MaxValue, max = 0 — inside every check
        empty = dict(measure("S4", openRt60MinS=3.4028234663852886e38, openRt60MaxS=0.0, corridorRt60MinS=3.4028234663852886e38,
                             corridorRt60MaxS=0.0, hallRt60MinS=3.4028234663852886e38, hallRt60MaxS=0.0, orderViolations=0,
                             openEdc30MsMin=0.0, corridorEdc30MsMin=0.0, hallEdc30MsMin=0.0), samples=0)
        self.assertEqual("PASS", r2.evaluate_gate(gate("S4"), FINAL, measure=dict(empty, samples=24))["status"])   # what the judge saw before
        for samples in (0, -1, None, "24", float("nan"), True, "absent"):
            with self.subTest(samples=samples):
                art = dict(empty, samples=samples) if samples != "absent" else {k: v for k, v in empty.items() if k != "samples"}
                row = r2.evaluate_gate(gate("S4"), FINAL, measure=art)
                if samples == "absent":   # no samples field: judged as before (every writer today sets one); null is BLOCKED
                    self.assertEqual("PASS", row["status"])
                else:
                    self.assertEqual("BLOCKED", row["status"])
                    self.assertIn("counted no samples", row["reason"])

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


class ScopeModeTests(unittest.TestCase):
    """C-00 06.10 (user: top down only, tests too): a gate names the camera modes it is measured in; a run that covers
    none of them does not measure it and judges it OUT_OF_SCOPE — never FAIL or BLOCKED, never in the step status."""

    def test_the_contract_names_the_fp_and_tp_gates_and_the_multi_mode_ones(self):
        data = r2.load_contract(CONTRACT)
        self.assertEqual({"H1b": ["tp"], "H1c": ["tp"], "H4": ["fp"], "H4a": ["fp"], "H4b": ["fp"], "H6": ["tp"],
                          "Z3": ["topdown", "tp"], "S3": ["topdown", "tp", "fp"]},
                         {g["id"]: g["modes"] for g in data["gates"] if "modes" in g})

    def test_modes_must_be_a_non_empty_list_of_distinct_camera_modes(self):
        for bad in ([], ["td"], ["tp", "tp"], "tp"):
            data = json.loads(CONTRACT)
            data["gates"][0]["modes"] = bad
            with self.subTest(modes=bad), self.assertRaises(r2.ContractError):
                r2.load_contract(json.dumps(data))

    def test_a_topdown_run_measures_no_fp_or_tp_only_gate(self):
        contract = r2.load_contract(CONTRACT)
        every = {g for s in r2.measurement_steps(contract, False) for g in s["gates"]}
        td = {g for s in r2.measurement_steps(contract, False, ("topdown",)) for g in s["gates"]}
        self.assertEqual({"H1b", "H1c", "H4", "H4a", "H4b", "H6"}, every - td)
        self.assertIn("Z3", td)   # multi-mode: its topdown cell still runs
        self.assertEqual(every, {g for s in r2.measurement_steps(contract, False, ("topdown", "tp", "fp")) for g in s["gates"]})

    def test_an_out_of_scope_gate_is_out_of_scope_whatever_was_bound(self):
        for artifact in (None, measure("H6", visibleShareMin=0.1, muzzleInsideSilhouetteFrames=9)):
            row = r2.evaluate_gate(gate("H6"), FINAL, measure=artifact, cases={}, modes=("topdown",))
            self.assertEqual(r2.OUT_OF_SCOPE, row["status"])
            self.assertIn(r2.SCOPE_REASON, row["reason"])
        self.assertEqual("BLOCKED", r2.evaluate_gate(gate("H6"), FINAL)["status"])   # every mode: as before

    def test_a_multi_mode_gate_is_judged_on_its_modes_in_scope(self):
        row = r2.evaluate_gate(gate("Z3"), FINAL, modes=("topdown",))
        self.assertEqual((["topdown"], ["tp"], "BLOCKED"), (row["modes"], row["outOfScopeModes"], row["status"]))

    def test_out_of_scope_rows_do_not_count_in_the_step_status(self):
        rows = [{"kind": "gate", "status": "PASS"}, {"kind": "gate", "status": r2.OUT_OF_SCOPE}]
        self.assertEqual("PASS", r2.summarize(rows))


class MinSamplesTests(unittest.TestCase):
    """C-00 07.10: a coverage floor for gates judged on a worst value or a count (S1 min share, S2 counts) — top down,
    ten classics: S1 66 shots (6 automatics × shots 2–10, rl_1 / sg_1 / sg_2 / sr_1 × 3), S2 151 planned shots."""

    def test_the_contract_pins_the_sound_floors(self):
        self.assertEqual((66, 151), (gate("S1")["minSamples"], gate("S2")["minSamples"]))

    def test_min_samples_is_a_whole_number_above_zero_on_a_gate_with_checks(self):
        # gt SHOULD-1 on 27a16ab4: the judge applies the floor to checks only — a report entry (L4), a tests-only gate and
        # null were accepted and never applied
        tests_only = {"checks": [], "report": [], "tests": ["DD.Prototype.Tests.PlayMode.AudioSliceTests.SilahKiti_GercekRpm_OyunIci_Kayit"]}
        for gid, bad, edit in (("S1", 0, {}), ("S1", -1, {}), ("S1", 6.5, {}), ("S1", True, {}), ("S1", "66", {}),
                               ("S1", None, {}), ("H5", 3, {}), ("L4", 1000, {}), ("S1", 66, tests_only)):
            data = json.loads(CONTRACT)
            g = next(g for g in data["gates"] if g["id"] == gid)
            g.update(edit)
            g["minSamples"] = bad
            with self.subTest(gate=gid, value=bad, edit=sorted(edit)), self.assertRaises(r2.ContractError) as cm:
                r2.load_contract(json.dumps(data))
            self.assertIn("minSamples", str(cm.exception))

    def test_below_the_floor_is_blocked_at_or_above_it_is_judged(self):
        s1 = gate("S1")
        cover = dict(weaponsMissing=0, weaponsMeasured=10, rosterEnabled=10)   # C-00 07.10: S1 / S2 coverage keys
        art = dict(measure("S1", below400ShareMin=0.25, below400ShareMinV4=0.3, **cover))
        for samples, status in ((65, "BLOCKED"), (66, "PASS"), (132, "PASS")):
            with self.subTest(samples=samples):
                row = r2.evaluate_gate(s1, FINAL, measure=dict(art, samples=samples), cases={t: "Passed" for t in s1.get("tests", [])})
                self.assertEqual(status, row["status"], row["reason"])
                if status == "BLOCKED":
                    self.assertIn("kapsama eksik", row["reason"])
        self.assertIn("kapsama bilinmiyor", r2.evaluate_gate(s1, FINAL, measure=art, cases={t: "Passed" for t in s1.get("tests", [])})["reason"])
        failing = dict(measure("S1", below400ShareMin=0.1, below400ShareMinV4=0.3, **cover), samples=10)
        low = r2.evaluate_gate(s1, FINAL, measure=failing, cases={t: "Passed" for t in s1.get("tests", [])})
        self.assertEqual("FAIL", low["status"])   # more samples only lower a minimum: a fail on few samples stands
        # C-00 07.10: a weapon of the roster without a take fails S1 / S2 even over the shot floor
        for gid, values in (("S1", dict(below400ShareMin=0.25)),
                            ("S2", dict(fireVoiceDeficit=0, transients=0, loopStarts=0, rejectedNotStolen=0))):
            short = dict(measure(gid, **values, weaponsMissing=1, weaponsMeasured=9, rosterEnabled=10), samples=200)
            row = r2.evaluate_gate(gate(gid), FINAL, measure=short, cases={t: "Passed" for t in gate(gid).get("tests", [])})
            self.assertEqual("FAIL", row["status"], (gid, row["reason"]))
