"""WP-079: exercise the RC evidence contract through its public CLI."""
import json
import hashlib
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[3]
SCRIPT = REPO / "tools/qa/rc_aggregate.py"
STAGES = (
    "full-editmode", "full-playmode", "release-smoke", "release-library",
    "m3-regression", "soak-stability-61", "soak-stability-26", "wp064-closure",
    "economy-acceptance", "window-modes", "log-scan",
)
EXACT_TESTS = (
    "FlowSmokeTest.Akis_BootMainMenuSelectionRunOlumResultsMainMenu",
    "RoundHudSyncTests.TurIlerleyinceHudRakamiDaIlerler",
    "GoldUpgradeSliceTests.P10_P19_RoundBitisi_Sweep_Ekran_SatinAlma_SonrakiRound",
    "MetaUiSliceTests.Results_SunumOzetleEslesir_UpgradeBirKez_HudSonDegeriKorur",
)


class RcAggregateTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.out = Path(self.directory.name)
        self.head = "a" * 40
        self.assets_tree = "b" * 40
        self.fixture_repo = self.out / "fixture-repo"
        self.fixture_repo.mkdir()
        fake_bin = self.out / "fake-bin"
        fake_bin.mkdir()
        fake_git = fake_bin / "git"
        fake_git.write_text("""#!/usr/bin/env python3
import os, sys
args = sys.argv[1:]
if len(args)<3 or args[:2] != ['-C', os.environ['RC_FIXTURE_REPO']]: sys.exit(99)
args=args[2:]
if args == ['status', '--porcelain'] or args == ['lfs', 'ls-files']: sys.exit(0)
if args == ['rev-parse', 'HEAD']: print('a'*40); sys.exit(0)
if args == ['rev-parse', 'a'*40+':prototype/DeathsDemolition_PlayableConcept/Assets']: print('b'*40); sys.exit(0)
sys.exit(99)
""")
        fake_git.chmod(0o700)
        self.env = dict(os.environ, PYTHONDONTWRITEBYTECODE="1",
                        PATH=str(fake_bin)+os.pathsep+os.environ["PATH"],
                        RC_FIXTURE_REPO=str(self.fixture_repo), DD_REQUIRE_ULTRA_EVIDENCE="1", DD_QA_PACKAGE="WP-061",
                        DD_EXTERNAL_EDITMODE_XML=str(self.out / "full-editmode/editmode.xml"),
                        DD_EXTERNAL_PLAYMODE_XML=str(self.out / "full-playmode/playmode.xml"))
        for stage in STAGES:
            self.write_text("stages/"+stage+"/exit.txt", "0\n")
        self.write_xml("full-editmode/editmode.xml", ("Example.EditMode",))
        armor = "ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet"
        self.write_xml("full-playmode/playmode.xml", (*EXACT_TESTS, armor))
        self.library = {
            "schema": "dd-wp064-library-provenance-v1", "phase": "post-build", "head": self.head,
            "porcelain": "", "lfsPointersOnDisk": 0, "lfsMissing": 0,
            "assetsTree": self.assets_tree, "library": {"artifactDbBytes": 100, "artifactDbMtime": 1000000},
        }
        self.dev_hash = self.build("m3/build-dev", "dev")
        self.build("release", "release")
        self.write_json("window-modes/qa-report.json", {"pass": True})
        self.write_json("log-scan.json", {"pass": True})
        self.bot = self.summary(61, "normal", 5)
        self.write_json("m3/bot/bot-summary.json", self.bot)
        for seed in (61, 26):
            name = f"m3/soak-{seed}"
            self.write_json(name+"/bot-summary.json", self.summary(seed, "stability", 12))
            self.attempt(name, "soak", ["player", "-ddQaBotRounds", "12", "-ddQaBotSeed", str(seed), "-ddQaBotStability"],
                         ["bot-summary.json", "qa-report.json", "events.txt"])
        for index, name in enumerate(("bot", "real-2", "real-3", "real-4", "real-5")):
            if name != "bot":
                self.write_json("m3/"+name+"/bot-summary.json", self.summary((61, 26)[index % 2], "normal", 5+index))
            self.attempt("m3/"+name, "soak", ["player", "-ddQaBotRounds", "12", "-ddQaBotSeed", str((61,26)[index%2])],
                         ["bot-summary.json", "qa-report.json", "events.txt"])
        ledger = []
        for index in range(1, 4):
            name = f"m3/perf-{index}"
            self.write_json(name+"/01-scene-ready.json", {"scene": "Run", "screenWidth": 1920, "screenHeight": 1080,
                            "graphicsDeviceType": "Metal", "rendererCount": 100, "renderersVisible": 80})
            self.write_json(name+"/02-force-40.json", dict.fromkeys(("configuredSafetyCeiling", "ceiling", "forcedTarget", "achievedCount"), 40) | {"pass": True})
            self.write_json(name+"/03-warmup.json", {"warmupSeconds": 10, "pass": True})
            window = dict.fromkeys(("activeMin", "activeMax", "liveBodiesMin", "liveBodiesMax", "crowdMemberMin", "crowdMemberMax"), 40)
            window.update(pass_=True, loadIntegrityFloor=40, loadIntegrityFraction=1.0, frameCount=100,
                          framesExcludedAsStalls=0, frameMeanMs=7.0, frameP99Ms=7.0, frameP99FilteredMs=7.0,
                          frameMaxMs=7.0, drawCallsAvg=120, trianglesAvg=150000)
            window["pass"] = window.pop("pass_")
            self.write_json(name+"/04-window.json", window)
            self.write_json(name+"/04d-gpu.json", {"pass": True, "gpuSampledFrames": 100,
                            "gpuFrameAvgMs": 3, "gpuFrameMaxMs": 4, "gpuMeasurementSource": "FrameTimingManager"})
            self.write_json(name+"/04h-hud.json", {"pass": True, "hudCostMs": 0.1, "hudFrameDeltaMs": 0.1})
            self.write_text(name+"/frame-times-ms.csv", "7.0\n"*100)
            self.attempt(name, "perf", ["player", "-ddPilotScene", "Run", "-ddPilotResolution", "1920x1080",
                         "-ddPilotWarmupSeconds", "10", "-ddPilotWindowSeconds", "60"],
                         ["01-scene-ready.json", "02-force-40.json", "03-warmup.json", "04-window.json", "04d-gpu.json", "frame-times-ms.csv"])
            ledger.append({"attemptId": f"perf-{index}", "dir": f"perf-{index}", "kind": "perf", "status": "complete"})
        self.write_text("m3/attempts-ledger.jsonl", "".join(json.dumps(row)+"\n" for row in ledger))
        self.child_reports()
        self.produce_reports()

    def write_text(self, name, value):
        path = self.out / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")

    def build(self, name, mode):
        binary = name+"/Fixture.app/Contents/MacOS/Fixture"
        self.write_text(binary, "inert synthetic build data " + mode)
        self.write_text(name+"/build.log", "fixture build succeeded\n")
        self.write_json(name+"/report.json", {"mode": mode, "ok": True, "head": self.head})
        self.write_json(name+"/build-report.json", {"mode": mode, "result": "Succeeded", "totalErrors": 0,
                        "stamp": {"stamped": True, "commit": self.head[:12], "describe": "fixture", "mode": mode}})
        self.write_json(name+"/footprint.json", {"mode": mode, "appMB": 1000, "capApplicable": mode=="release", "capExceeded": False})
        self.write_json(name+"/library-provenance.json", self.library)
        return hashlib.sha256((self.out/binary).read_bytes()).hexdigest()

    def summary(self, seed, mode, completed):
        stability = mode == "stability"
        return {"runMode": mode, "seed": seed, "roundsRequested": 12, "roundsCompleted": completed,
                "deaths": 0 if stability else 1, "deathRound": 0 if stability else completed+1,
                "deathKilledBy": "fixture damage", "pass": False, "stabilityPass": stability,
                "artifactWriteFailed": False, "rounds": [{"round": r, "completed": r<=completed,
                "outcome": "completed" if r<=completed else "run-ended:PlayerDeath", "memMonoUsedMB": 60+r*0.5,
                "memTotalAllocatedMB": 900+r*2, "memRssMB": -1} for r in range(1,13)]}

    def attempt(self, name, kind, command, additional):
        self.write_json(name+"/command.json", command)
        self.write_json(name+"/preflight.json", {"pass": True})
        self.write_json(name+"/host-monitor.json", {"pass": True, "timedOut": False})
        self.write_text(name+"/player.log", "fixture Player log without errors\n")
        self.write_text(name+"/exit.txt", "exit=0\n")
        if kind == "soak":
            if not (self.out/name/"qa-report.json").exists():
                self.write_json(name+"/qa-report.json", {"fixture": True})
            self.write_text(name+"/events.txt", "fixture round events\n")
        required = ["command.json", "preflight.json", "host-monitor.json", "player.log", "exit.txt", *additional]
        self.write_json(name+"/attempt.json", {"schema": "dd-wp064-attempt-v1", "kind": kind,
                        "executionCommit": self.head, "status": "complete", "exitCode": 0,
                        "binarySha256": self.dev_hash, "commandSha256": hashlib.sha256((self.out/name/"command.json").read_bytes()).hexdigest(),
                        "startedEpoch": 1000, "completedEpoch": 2000, "attemptId": Path(name).name,
                        "artifactSha256": {item: hashlib.sha256((self.out/name/item).read_bytes()).hexdigest() for item in required}})

    def child_reports(self):
        def gates(ids, failed=()):
            return [{"id": name, "pass": name not in failed, "value": "fixture measured",
                     "threshold": "fixture contract", "note": "synthetic producer input", "source": "fixture.json"} for name in ids]
        self.write_json("m3/spawn-mix/qa-report.json", {"schema": "dd-qa-report-v1", "driver": "SpawnMix", "pass": True,
                        "gates": gates(("spawnmix.violations", "spawnmix.simulated", "spawnmix.notes"))})
        self.write_json("m3/bot/qa-report.json", {"schema": "dd-qa-report-v1", "driver": "QaRunBot", "pass": False,
                        "gates": gates(("bot.alloc.method", "bot.alloc.perFrame", "bot.rounds", "bot.determinism"), ("bot.rounds",))})
        identifiers, rows = [], []
        for round_number in (1,4,8,10,12):
            prefix = f"readability.r{round_number}."
            identifiers.extend(prefix+suffix for suffix in ("occlusion", "zombieCrowdContrast"))
            for skin in (0,1,2):
                identifiers.extend((prefix+f"playerEnvContrast.skin{skin}", prefix+f"playerZombieContrast.skin{skin}"))
            identifiers.append(prefix+"capture.Ultra")
            for tier in ("Special", "Elite", "Ultra"):
                identifiers.extend((prefix+"tierEnvContrast."+tier, prefix+"tierVsStandard."+tier))
            rows.append({"round": round_number, "pass": True, "captureTier": 3,
                         "tierPixels": [1,1,1,1], "tierEnvContrast": [1,1,1,1]})
        self.write_json("m3/readability/readability-summary.json", {"schema": "dd-qa-readability-v1", "pass": True, "rounds": rows})
        self.write_json("m3/readability/qa-report.json", {"schema": "dd-qa-report-v1", "driver": "QaReadabilityGate", "pass": True,
                        "gates": gates(identifiers)})
        self.attempt("m3/bot", "soak", ["player", "-ddQaBotRounds", "12", "-ddQaBotSeed", "61"],
                     ["bot-summary.json", "qa-report.json", "events.txt"])

    def produce_reports(self):
        aggregate = subprocess.run([sys.executable, str(REPO/"tools/qa/aggregate.py"), str(self.out/"m3"), "--window", "60", "--perf-runs", "3"],
                                   capture_output=True, text=True, env=self.env)
        self.assertEqual(1, aggregate.returncode, aggregate.stdout+aggregate.stderr)
        m3 = json.loads((self.out/"m3/qa-report.json").read_text())
        m3.update(rcPass=True, rcOriginalAggregateExit=1, rcReportOnlyGates=["bot.rounds"])
        self.write_json("m3/qa-report.json", m3)
        closure = subprocess.run([sys.executable, str(REPO/"tools/qa/wp064_gate.py"), str(self.out/"m3"),
                                  "--release", str(self.out/"release"), "--commit", self.head, "--repo", str(self.fixture_repo),
                                  "--soak-dirs", "soak-61,soak-26", "--seeds", "61,26", "--report-dirs", "bot,real-2,real-3,real-4,real-5",
                                  "--rounds", "12", "--window", "60"], capture_output=True, text=True, env=self.env)
        self.assertEqual(0, closure.returncode, closure.stdout+closure.stderr)
        self.closure = json.loads((self.out/"m3/wp064-report.json").read_text())

    def write_json(self, name, value):
        path = self.out / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value), encoding="utf-8")

    def write_xml(self, name, cases):
        root = ET.Element("test-run", total=str(len(cases)), passed=str(len(cases)),
                          failed="0", skipped="0", inconclusive="0")
        for case in cases:
            ET.SubElement(root, "test-case", fullname=case, result="Passed")
        path = self.out / name
        path.parent.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(root).write(path, encoding="utf-8")

    def verdict(self, expected, *extra):
        env = self.env
        result = subprocess.run([
            sys.executable, str(SCRIPT), str(self.out), "--repo", str(self.fixture_repo),
            "--rounds", "12", "--window", "60", "--perf-runs", "3", "--start-epoch", "1", *extra,
        ], capture_output=True, text=True, env=env)
        self.assertEqual(0 if expected else 1, result.returncode, result.stdout + result.stderr)
        report = json.loads((self.out / "rc-report.json").read_text(encoding="utf-8"))
        self.assertIs(expected, report["pass"])
        return report

    def write_skip_xml(self, name, passed, skipped):
        # C-00 30.09 Y6: skipped = [(fullname, label, runstate, categories on the case, categories on its fixture)]
        root = ET.Element("test-run", total=str(len(passed) + len(skipped)), passed=str(len(passed)),
                          failed="0", skipped=str(len(skipped)), inconclusive="0")
        for case in passed:
            ET.SubElement(root, "test-case", fullname=case, result="Passed")
        for fullname, label, runstate, case_categories, fixture_categories in skipped:
            fixture = ET.SubElement(root, "test-suite", type="TestFixture", fullname=fullname.rsplit(".", 1)[0])
            if fixture_categories:
                props = ET.SubElement(fixture, "properties")
                for category in fixture_categories:
                    ET.SubElement(props, "property", name="Category", value=category)
            attrs = {"fullname": fullname, "result": "Skipped", "runstate": runstate}
            if label:
                attrs["label"] = label
            node = ET.SubElement(fixture, "test-case", **attrs)
            if case_categories:
                props = ET.SubElement(node, "properties")
                for category in case_categories:
                    ET.SubElement(props, "property", name="Category", value=category)
        path = self.out / name
        path.parent.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(root).write(path, encoding="utf-8")

    def test_y6_ignored_editmode_skip_is_red_and_named(self):
        probe = "DD.AI.Tests.SpawnGateCoverageProbe.V6_KapiKapsami_Arama"
        self.write_skip_xml("full-editmode/editmode.xml", ("Example.EditMode",), [(probe, "Ignored", "Runnable", (), ())])
        report = self.verdict(False)
        self.assertEqual([probe + " [Ignored]"], report["unexpectedEditModeSkips"])

    def test_y6_explicit_diagnostics_skip_is_allowed_on_case_or_fixture(self):
        on_case = "DD.AI.Tests.SpawnGateCoverageProbe.V6_KapiKapsami_Arama"
        on_fixture = "DD.Art.Tests.PlayMode.GpuProbe.Olcum"
        self.write_skip_xml("full-editmode/editmode.xml", ("Example.EditMode",),
                            [(on_case, "Explicit", "Explicit", ("Diagnostics",), ())])
        self.write_skip_xml("full-playmode/playmode.xml", EXACT_TESTS + (
            "ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet",),
            [(on_fixture, "", "Explicit", (), ("Diagnostics",))])
        self.produce_reports()   # the m3 receipts are replayed from these XMLs, so they are produced after them (as in a real RC)
        report = self.verdict(True)
        self.assertEqual(([on_case], []), (report["allowedEditModeSkips"], report["unexpectedEditModeSkips"]))
        self.assertEqual(([on_fixture], []), (report["allowedPlayModeSkips"], report["unexpectedPlayModeSkips"]))

    def test_y6_explicit_without_diagnostics_or_diagnostics_without_explicit_is_red(self):
        for label, runstate, categories in (("Explicit", "Explicit", ("Slow",)), ("Ignored", "Ignored", ("Diagnostics",))):
            with self.subTest(label=label, categories=categories):
                self.write_skip_xml("full-editmode/editmode.xml", ("Example.EditMode",),
                                    [("DD.X.Tests.Probe.Case", label, runstate, categories, ())])
                self.assertEqual(1, len(self.verdict(False)["unexpectedEditModeSkips"]))

    def test_y6_no_name_exception_left_setpass_ignore_is_red(self):
        # C-00 30.09: the SetPassAttributionTests fullname exception is removed; only the Y6 attributes allow a skip
        setpass = "DD.Art.Tests.PlayMode.SetPassAttributionTests.SetPass_Kaynaklari_40ZombiTamZirh"
        self.write_skip_xml("full-playmode/playmode.xml", EXACT_TESTS + (
            "ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet",),
            [(setpass, "Ignored", "Ignored", (), ())])
        report = self.verdict(False)
        self.assertEqual([setpass + " [Ignored]"], report["unexpectedPlayModeSkips"])
        self.assertNotIn("legacyPlayModeSkipExceptions", report)

    def test_y6_setpass_as_explicit_diagnostics_fixture_is_allowed(self):
        setpass = "DD.Art.Tests.PlayMode.SetPassAttributionTests.SetPass_Kaynaklari_40ZombiTamZirh"
        self.write_skip_xml("full-playmode/playmode.xml", EXACT_TESTS + (
            "ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet",),
            [(setpass, "Explicit", "Explicit", (), ("Diagnostics",))])
        self.produce_reports()
        self.assertEqual([setpass], self.verdict(True)["allowedPlayModeSkips"])

    def test_two_stability_runs_and_real_bot_death_pass(self):
        self.verdict(True)

    def test_missing_both_stability_summaries_fails(self):
        for seed in (61, 26):
            (self.out / f"m3/soak-{seed}/bot-summary.json").unlink()
        self.verdict(False)

    def test_single_stability_summary_fails(self):
        (self.out / "m3/soak-26/bot-summary.json").unlink()
        self.verdict(False)

    def test_wrong_stability_seed_fails(self):
        path = "m3/soak-26/bot-summary.json"
        summary = json.loads((self.out / path).read_text())
        summary["seed"] = 27
        self.write_json(path, summary)
        self.verdict(False)

    def test_normal_bot_in_stability_slot_fails(self):
        self.write_json("m3/soak-26/bot-summary.json", self.bot)
        self.verdict(False)

    def test_each_required_stage_nonzero_fails(self):
        for stage in STAGES:
            with self.subTest(stage=stage):
                path = self.out / "stages" / stage / "exit.txt"
                path.write_text("1\n")
                self.verdict(False)
                path.write_text("0\n")

    def test_failed_stage_named_by_rc_fails_even_with_green_stage_exits(self):
        # Y2: a real-bot report run's guard restore or a signal fails the chain outside the eleven stage exits
        report = self.verdict(False, "--failed-stage", "real-3")
        self.assertEqual("real-3", report["failedStage"])

    def test_missing_release_library_receipt_fails(self):
        (self.out / "stages/release-library/exit.txt").unlink()
        self.verdict(False)

    def test_normal_bot_only_even_if_successful_fails(self):
        self.bot.update(roundsCompleted=12, deaths=0)
        self.bot["pass"] = True
        self.write_json("m3/bot/bot-summary.json", self.bot)
        for seed in (61, 26):
            (self.out / f"m3/soak-{seed}/bot-summary.json").unlink()
            (self.out / f"stages/soak-stability-{seed}/exit.txt").unlink()
        self.verdict(False)

    def test_missing_or_malformed_normal_bot_fails(self):
        path = self.out / "m3/bot/bot-summary.json"
        for content in (None, "{", "[]", "null", '{"runMode":"stability"}'):
            with self.subTest(content=content):
                if content is None:
                    path.unlink()
                else:
                    path.write_text(content)
                self.verdict(False)

    def test_missing_or_malformed_closure_fails(self):
        path = self.out / "m3/wp064-report.json"
        for content in (None, "{", "[]", "null"):
            with self.subTest(content=content):
                if content is None:
                    path.unlink()
                else:
                    path.write_text(content)
                self.verdict(False)

    def test_failed_closure_check_fails(self):
        self.closure["checks"][-1]["pass"] = False
        self.write_json("m3/wp064-report.json", self.closure)
        self.verdict(False)

    def test_closure_wrong_execution_commit_fails(self):
        self.closure["executionCommit"] = "0" * 40
        self.write_json("m3/wp064-report.json", self.closure)
        self.verdict(False)

    def test_closure_missing_real_bot_report_fails(self):
        self.closure["realBotReport"] = []
        self.write_json("m3/wp064-report.json", self.closure)
        self.verdict(False)

    def test_rc_flag_does_not_bypass_other_failed_gate(self):
        path = "m3/qa-report.json"
        report = json.loads((self.out / path).read_text())
        report["gates"][1]["pass"] = False
        self.write_json(path, report)
        self.verdict(False)

    def test_missing_required_closure_check_fails(self):
        self.closure["checks"].pop(1)
        self.write_json("m3/wp064-report.json", self.closure)
        self.verdict(False)

    def test_missing_or_malformed_release_library_fails(self):
        path = self.out / "release/library-provenance.json"
        for content in (None, "{", "[]", "null"):
            with self.subTest(content=content):
                if content is None:
                    path.unlink()
                else:
                    path.write_text(content)
                self.verdict(False)

    def test_release_library_wrong_head_fails(self):
        self.library["head"] = "0" * 40
        self.write_json("release/library-provenance.json", self.library)
        self.verdict(False)

    def test_release_library_wrong_schema_or_phase_fails(self):
        for key, wrong in (("schema", "unknown"), ("phase", "pre-build")):
            with self.subTest(key=key):
                library = dict(self.library, **{key: wrong})
                self.write_json("release/library-provenance.json", library)
                self.verdict(False)

    def test_each_release_library_closure_proof_is_required(self):
        original = self.closure["checks"]
        for name in (item["name"] for item in original if item["name"].startswith("library.release.")):
            with self.subTest(check=name):
                self.closure["checks"] = [item for item in original if item["name"] != name]
                self.write_json("m3/wp064-report.json", self.closure)
                self.verdict(False)

    def test_optional_probe_failure_remains_failure(self):
        path = "m3/qa-report.json"
        report = json.loads((self.out / path).read_text())
        report["gates"].append({"id": "perf.hudCostMs", "pass": False, "value": "SKIPPED"})
        self.write_json(path, report)
        self.verdict(False)

    def test_missing_m3_gates_fails(self):
        self.write_json("m3/qa-report.json", {"pass": True, "rcPass": True})
        self.verdict(False)

    def test_truncated_mandatory_child_gates_fail(self):
        for child in ("spawn-mix", "bot", "readability"):
            path = f"m3/{child}/qa-report.json"
            original = json.loads((self.out/path).read_text())
            for gate in original["gates"]:
                with self.subTest(child=child, missing=gate["id"]):
                    report = dict(original, gates=[g for g in original["gates"] if g["id"] != gate["id"]])
                    self.write_json(path, report)
                    self.verdict(False)
            self.write_json(path, original)

    def test_wrong_type_child_reports_fail(self):
        for child in ("spawn-mix", "bot", "readability"):
            path = f"m3/{child}/qa-report.json"
            original = json.loads((self.out/path).read_text())
            for wrong in ([], None, {"schema": "dd-qa-report-v1", "gates": "counterfeit"}):
                with self.subTest(child=child, value=wrong):
                    self.write_json(path, wrong)
                    self.verdict(False)
            self.write_json(path, original)

    def test_duplicate_child_mandatory_gate_fails(self):
        for child in ("spawn-mix", "bot", "readability"):
            path = f"m3/{child}/qa-report.json"
            original = json.loads((self.out/path).read_text())
            report = dict(original, gates=original["gates"]+[original["gates"][0]])
            self.write_json(path, report)
            self.verdict(False)
            self.write_json(path, original)

    def test_counterfeit_child_identity_or_gate_fields_fail(self):
        path = "m3/spawn-mix/qa-report.json"
        original = json.loads((self.out/path).read_text())
        for field, wrong in (("schema", "counterfeit"), ("driver", "QaRunBot"), ("pass", "true")):
            with self.subTest(field=field):
                self.write_json(path, dict(original, **{field: wrong}))
                self.verdict(False)
        for field in ("pass", "value", "threshold", "note", "source"):
            with self.subTest(gate_field=field):
                report = json.loads(json.dumps(original))
                report["gates"][0][field] = 1
                self.write_json(path, report)
                self.verdict(False)

    def test_counterfeit_aggregate_cannot_hide_failed_raw_perf(self):
        path = "m3/perf-2/04-window.json"
        window = json.loads((self.out/path).read_text())
        window["frameMeanMs"] = 20
        self.write_json(path, window)
        self.verdict(False)

    def test_counterfeit_closure_cannot_hide_invalid_attempt_hash(self):
        self.write_text("m3/perf-1/player.log", "changed after provenance receipt\n")
        self.verdict(False)

    def test_truncated_duplicate_and_wrong_type_closure_checks_fail(self):
        original = json.loads(json.dumps(self.closure))
        variants = (original["checks"][:1], original["checks"]+[original["checks"][0]], "counterfeit")
        for checks in variants:
            with self.subTest(checks_type=type(checks).__name__, count=len(checks)):
                self.write_json("m3/wp064-report.json", dict(original, checks=checks))
                self.verdict(False)

    def test_wrong_type_closure_pass_and_check_pass_fail(self):
        report = json.loads(json.dumps(self.closure))
        report["pass"] = "true"
        self.write_json("m3/wp064-report.json", report)
        self.verdict(False)
        report = json.loads(json.dumps(self.closure))
        report["checks"][0]["pass"] = 1
        self.write_json("m3/wp064-report.json", report)
        self.verdict(False)

    def test_readability_summary_round_or_type_corruption_fails(self):
        path = "m3/readability/readability-summary.json"
        original = json.loads((self.out/path).read_text())
        for rounds in (original["rounds"][:-1], original["rounds"]+[original["rounds"][0]], "counterfeit"):
            self.write_json(path, dict(original, rounds=rounds))
            self.verdict(False)

    def test_verify_only_keeps_closure_receipts_immutable(self):
        receipts = [self.out/"m3/wp064-report.json", self.out/"m3/wp064-report.txt"]
        before = [path.read_bytes() for path in receipts]
        result = subprocess.run([sys.executable, str(REPO/"tools/qa/wp064_gate.py"), str(self.out/"m3"),
                                 "--release", str(self.out/"release"), "--commit", self.head, "--repo", str(self.fixture_repo),
                                 "--soak-dirs", "soak-61,soak-26", "--seeds", "61,26", "--report-dirs", "bot,real-2,real-3,real-4,real-5",
                                 "--rounds", "12", "--window", "60", "--verify-only"], capture_output=True, text=True, env=self.env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIs(True, json.loads(result.stdout)["pass"])
        self.assertEqual(before, [path.read_bytes() for path in receipts])

    def test_final_rc_keeps_original_aggregate_and_closure_receipts(self):
        receipts = [self.out/"m3"/name for name in ("qa-report.json", "qa-report.txt", "wp064-report.json", "wp064-report.txt")]
        before = [path.read_bytes() for path in receipts]
        self.verdict(True)
        self.assertEqual(before, [path.read_bytes() for path in receipts])

    def test_missing_source_evidence_cannot_use_claimed_pass_receipts(self):
        for name in ("release/build-report.json", "release/build.log", "m3/perf-1/frame-times-ms.csv",
                     "m3/attempts-ledger.jsonl", "m3/build-dev/library-provenance.json", "m3/perf-1/attempt.json"):
            with self.subTest(missing=name):
                path = self.out/name
                content = path.read_bytes()
                path.unlink()
                self.verdict(False)
                path.write_bytes(content)

    def test_missing_each_aggregate_gate_fails(self):
        path = "m3/qa-report.json"
        original = json.loads((self.out/path).read_text())
        for index, gate in enumerate(original["gates"]):
            with self.subTest(missing=gate["id"]):
                report = dict(original, gates=original["gates"][:index]+original["gates"][index+1:])
                self.write_json(path, report)
                self.verdict(False)

    def test_duplicate_or_string_boolean_aggregate_gate_fails(self):
        path = "m3/qa-report.json"
        original = json.loads((self.out/path).read_text())
        report = dict(original, gates=original["gates"]+[original["gates"][0]])
        self.write_json(path, report)
        self.verdict(False)
        report = json.loads(json.dumps(original))
        report["gates"][0]["pass"] = "true"
        self.write_json(path, report)
        self.verdict(False)

    def test_counterfeit_aggregate_headers_fail(self):
        path = "m3/qa-report.json"
        original = json.loads((self.out/path).read_text())
        for field, wrong in (("schema", "counterfeit"), ("driver", "QaRunBot"), ("pass", True), ("rcPass", "true")):
            with self.subTest(field=field):
                self.write_json(path, dict(original, **{field: wrong}))
                self.verdict(False)

    def test_counterfeit_closure_check_value_fails(self):
        report = json.loads(json.dumps(self.closure))
        report["checks"][0]["value"] = "counterfeit actual proof"
        self.write_json("m3/wp064-report.json", report)
        self.verdict(False)

    def test_real_report_attempt_schema_hash_or_host_corruption_fails(self):
        attempt_path = "m3/real-2/attempt.json"
        original = json.loads((self.out/attempt_path).read_text())
        for field, wrong in (("schema", "counterfeit"), ("commandSha256", "0"*64)):
            with self.subTest(field=field):
                self.write_json(attempt_path, dict(original, **{field: wrong}))
                self.verdict(False)
        self.write_json(attempt_path, original)
        self.write_json("m3/real-2/host-monitor.json", {"pass": False, "timedOut": False})
        host_path = self.out/"m3/real-2/host-monitor.json"
        original["artifactSha256"]["host-monitor.json"] = hashlib.sha256(host_path.read_bytes()).hexdigest()
        self.write_json(attempt_path, original)
        self.verdict(False)

    def test_normal_report_death_exit_three_is_report_only(self):
        name = "m3/real-2"
        self.write_text(name+"/exit.txt", "exit=3\n")
        path = name+"/attempt.json"
        attempt = json.loads((self.out/path).read_text())
        attempt.update(status="failed", exitCode=3)
        attempt["artifactSha256"]["exit.txt"] = hashlib.sha256((self.out/name/"exit.txt").read_bytes()).hexdigest()
        self.write_json(path, attempt)
        self.produce_reports()
        self.verdict(True)

    def test_raw_exit_mismatch_cannot_be_hidden_by_refreshing_artifact_hash(self):
        for name in ("perf-1", "soak-61", "real-2"):
            path = "m3/"+name+"/attempt.json"
            original = json.loads((self.out/path).read_text())
            for raw in ("exit=137\n", "exit=-9\n", "exit=3\n"):
                with self.subTest(attempt=name, raw=raw):
                    self.write_text("m3/"+name+"/exit.txt", raw)
                    attempt = json.loads(json.dumps(original))
                    attempt["artifactSha256"]["exit.txt"] = hashlib.sha256((self.out/"m3"/name/"exit.txt").read_bytes()).hexdigest()
                    self.write_json(path, attempt)
                    self.verdict(False)
            self.write_text("m3/"+name+"/exit.txt", "exit=0\n")
            self.write_json(path, original)

    def test_missing_rc_normal_attempt_cannot_use_legacy_external_build(self):
        self.build("m3/build-real-2", "dev")
        (self.out/"m3/real-2/attempt.json").unlink()
        self.produce_reports()
        self.assertIs(True, self.closure["pass"], "general closure preserves the legacy external-build contract")
        self.verdict(False)

    def test_malformed_attempt_ids_fail_without_producer_traceback(self):
        for name in ("perf-1", "soak-61", "real-2"):
            path = "m3/"+name+"/attempt.json"
            original = json.loads((self.out/path).read_text())
            for bad_id in ([], {}):
                with self.subTest(attempt=name, bad_id=bad_id):
                    self.write_json(path, dict(original, attemptId=bad_id))
                    result = subprocess.run([sys.executable, str(REPO/"tools/qa/wp064_gate.py"), str(self.out/"m3"),
                                             "--release", str(self.out/"release"), "--commit", self.head, "--repo", str(self.fixture_repo),
                                             "--soak-dirs", "soak-61,soak-26", "--seeds", "61,26", "--report-dirs", "bot,real-2,real-3,real-4,real-5",
                                             "--rounds", "12", "--window", "60", "--verify-only"], capture_output=True, text=True, env=self.env)
                    self.assertEqual(1, result.returncode, result.stdout+result.stderr)
                    self.assertIs(False, json.loads(result.stdout)["pass"])
                    self.assertNotIn("Traceback", result.stderr)
                    self.verdict(False)
            self.write_json(path, original)

    def adapter(self, original_exit):
        spec = importlib.util.spec_from_file_location("rc_aggregate_under_test", SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        argv = [str(SCRIPT), str(self.out / "m3"), "--rounds", "12",
                "--window", "60", "--perf-runs", "3"]
        original = subprocess.CompletedProcess(["aggregate.py"], original_exit)
        with mock.patch.object(module.sys, "argv", argv), mock.patch.object(
                module.subprocess, "run", return_value=original) as base_aggregate:
            result = module.m3_report_only()
        base_aggregate.assert_called_once()
        report = json.loads((self.out / "m3/qa-report.json").read_text())
        return result, report

    def test_adapter_preserves_raw_failure_and_accepts_report_only_death(self):
        before = json.loads((self.out / "m3/qa-report.json").read_text())
        result, report = self.adapter(1)
        self.assertEqual(0, result)
        self.assertIs(False, report["pass"])
        self.assertEqual(before["gates"], report["gates"])
        self.assertIs(True, report["rcPass"])
        self.assertEqual(1, report["rcOriginalAggregateExit"])
        self.assertEqual(["bot.rounds"], report["rcReportOnlyGates"])

    def test_adapter_base_error_or_crash_cannot_pass(self):
        for exit_code in (2, -11):
            with self.subTest(exit_code=exit_code):
                result, report = self.adapter(exit_code)
                self.assertNotEqual(0, result)
                self.assertIs(False, report["rcPass"])
                self.assertEqual(exit_code, report["rcOriginalAggregateExit"])
                self.assertIs(False, report["pass"])

    def test_adapter_optional_failed_probe_cannot_pass(self):
        path = "m3/qa-report.json"
        report = json.loads((self.out / path).read_text())
        report["gates"].append({"id": "perf.hudCostMs", "pass": False, "value": "SKIPPED"})
        self.write_json(path, report)
        result, adapted = self.adapter(1)
        self.assertNotEqual(0, result)
        self.assertIs(False, adapted["rcPass"])
        self.assertEqual(report["gates"], adapted["gates"])


if __name__ == "__main__":
    unittest.main()
