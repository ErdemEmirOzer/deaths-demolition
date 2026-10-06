"""WP-091 C-00 B-09 (06.10) · tools/qa/aggregate.py — the M3 closure aggregate (WP-026, schema dd-qa-report-v1).

Real use: regression.sh step 7 `python3 "$AGG" "$OUT" --window "$WINDOW" --perf-runs "$PERF_RUNS"` (exit 0 PASS / 1 FAIL
/ 2 usage), and inside the RC rc_aggregate.m3_report_only() runs it with the same argv, then requires
`exit == (0 if report["pass"] else 1)` and qa_contract(report, "aggregate") (string fields, boolean pass, unique ids).
The tests drive the public CLI against fixture folders shaped like a regression.sh <out-dir>; one consumer test runs the
output through rc_aggregate's own contract functions. Hermetic: temp folders only, every DD_* variable is removed from the
child environment (DD_EXTERNAL_*_XML would otherwise redirect the tool to real evidence), no network, no Unity.

Known defects are written as @unittest.expectedFailure with the reason; the tool itself is not changed here.
"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

QA = Path(__file__).resolve().parents[1]
SCRIPT = QA / "aggregate.py"
ARMOR = "ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet"
SPAWN_IDS = ("spawnmix.violations", "spawnmix.simulated", "spawnmix.notes")
BOT_IDS = ("bot.alloc.method", "bot.alloc.perFrame", "bot.rounds", "bot.determinism")
READ_ROUNDS = (1, 4, 8, 10, 12)
ULTRA_ID = "readability.r12.tierEnvContrast.Ultra"
REPORT_KEYS = {"schema", "package", "driver", "utc", "buildInfo", "machine", "window", "load1Before", "load1After",
               "seed", "pass", "summary", "gates"}
GATE_KEYS = {"id", "pass", "value", "threshold", "note", "source"}
OWN_GATES = ("editmode.", "perf.", "readability.armorForm.", "readability.ultraEvidence", "readability.driver")
WINDOW_TAIL = " runs, perf-clean rule (Blender/other Unity < 50 %)"
MB = 1048576


def readability_ids(rounds=READ_ROUNDS, ultra=True):
    """The ids QaReadabilityGate.cs writes (report.Add("readability.r" + round + ".playerEnvContrast.skin" + s, …))."""
    ids = []
    for r in rounds:
        ids += ["readability.r%d.occlusion" % r, "readability.r%d.zombieCrowdContrast" % r]
        for s in (0, 1, 2):
            ids += ["readability.r%d.playerEnvContrast.skin%d" % (r, s), "readability.r%d.playerZombieContrast.skin%d" % (r, s)]
    return ids + ([ULTRA_ID] if ultra else [])


def child_gate(gid, ok=True, source="fixture.json"):
    return {"id": gid, "pass": ok, "value": "0.210", "threshold": ">= 0.06", "note": "fixture", "source": source}


def nunit(cases, total=None, passed=None, failed=None, skipped=None, inconclusive=0):
    """cases: (fullname, result, extra attribute text, categories). Counts default to the cases' own tally."""
    tally = lambda r: sum(1 for case in cases if case[1] == r)  # noqa: E731
    total = len(cases) if total is None else total
    passed = tally("Passed") if passed is None else passed
    failed = tally("Failed") if failed is None else failed
    skipped = tally("Skipped") if skipped is None else skipped
    body = []
    for fullname, result, attrs, categories in cases:
        props = "".join('<property name="Category" value="%s"/>' % c for c in categories)
        body.append('<test-case fullname="%s" result="%s" %s>%s</test-case>'
                    % (fullname, result, attrs, "<properties>%s</properties>" % props if props else ""))
    return ('<test-run total="%d" passed="%d" failed="%d" skipped="%d" inconclusive="%d">'
            '<test-suite type="TestFixture" fullname="DD.Prototype.Qa.Tests.EditMode.Fixture">%s</test-suite></test-run>'
            % (total, passed, failed, skipped, inconclusive, "".join(body)))


def green_cases(n=5):
    return [("DD.Prototype.Qa.Tests.EditMode.Fixture.Case%d" % i, "Passed", "", ()) for i in range(n)]


def window(**overrides):
    w = {"pass": True, "note": "window ok", "frameMeanMs": 6.5, "frameP99Ms": 9.5, "frameP99FilteredMs": 9.0,
         "framesExcludedAsStalls": 1, "frameMaxMs": 41.0, "frameCount": 3600, "drawCallsAvg": 120.0,
         "trianglesAvg": 1500000.0, "textureMemoryMax": 512 * MB, "meshMemoryMax": 128 * MB,
         "renderTextureMemoryMax": 64 * MB, "gcAllocatedInFrameAvg": 0.0, "gen0CollectionsInWindow": 0}
    if "pass_" in overrides:  # `pass` is a keyword
        overrides["pass"] = overrides.pop("pass_")
    w.update(overrides)
    return w


class Fixture:
    """A regression.sh <out-dir>."""

    def __init__(self, root):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)

    def text(self, relative, value):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")
        return path

    def json(self, relative, value):
        return self.text(relative, json.dumps(value, ensure_ascii=False))

    def remove(self, relative):
        (self.root / relative).unlink()

    def editmode(self, xml=None):
        return self.text("editmode-qa.xml", xml if xml is not None else nunit(green_cases()))

    def armor(self, results=("Passed",), relative="playmode-armor.xml"):
        cases = [("DD.Art.Tests.PlayMode.%s" % ARMOR, r, "", ()) for r in results]
        cases.append(("DD.Art.Tests.PlayMode.OtherTests.Unrelated", "Failed", "", ()))
        return self.text(relative, nunit(cases))

    def child(self, folder, driver, ids, failed=(), summary=None, source="fixture.json"):
        report = {"schema": "dd-qa-report-v1", "driver": driver, "pass": not failed,
                  "gates": [child_gate(g, g not in failed, source) for g in ids]}
        if summary is not None:
            report["summary"] = summary
        return self.json(folder + "/qa-report.json", report)

    def bot_summary(self, **overrides):
        summary = {"runMode": "normal", "seed": 26, "roundsRequested": 8, "roundsCompleted": 8, "totalKills": 120,
                   "deaths": 0, "spawnSequenceHash": "a1b2", "outcomeHash": "c3d4", "meanFrameMs": 6.5}
        summary.update(overrides)
        return self.json("bot/bot-summary.json", summary)

    def perf(self, i, **overrides):
        return self.json("perf-%d/04-window.json" % i, window(**overrides))

    def hud(self, i, cost=0.2, ok=True, delta=0.1):
        return self.json("perf-%d/04h-hud.json" % i, {"pass": ok, "hudCostMs": cost, "hudFrameDeltaMs": delta})

    def green(self, perf_runs=3, ultra=True):
        self.json("build-info.json", {"mode": "dev", "commit": "abc123", "describe": "wp-091-1-gabc", "branch": "main",
                                      "utc": "2026-10-06T00:00:00Z", "unity": "2022.3.22f1", "sizeBytes": 1})
        self.editmode()
        self.child("spawn-mix", "SpawnMix", SPAWN_IDS, summary="1000 spawns x rounds 1-10, violations 0")
        self.child("bot", "QaRunBot", BOT_IDS)
        self.bot_summary()
        for i in range(1, perf_runs + 1):
            self.perf(i)
            self.hud(i)
            self.text("perf-%d/load-before.txt" % i, "10:00  up 3 days,  2:01, 2 users, load averages: 2.31 2.10 1.95\n")
            self.text("perf-%d/load-after.txt" % i, "10:01  up 3 days,  2:02, 2 users, load averages: 2.40 2.12 1.96\n")
        self.child("readability", "QaReadabilityGate", readability_ids(ultra=ultra))
        self.armor()
        return self


def hermetic_env(**extra):
    env = {key: value for key, value in os.environ.items() if not key.startswith("DD_")}
    env.update(PYTHONDONTWRITEBYTECODE="1", PYTHONIOENCODING="utf-8")
    env.update(extra)
    return env


class AggregateTestCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory(prefix="dd-b09-aggregate-")
        self.addCleanup(self._tmp.cleanup)
        self.base = Path(self._tmp.name)
        self.fx = Fixture(self.base / "m3")
        self._cases = 0

    def fresh(self):
        self._cases += 1
        return Fixture(self.base / ("case-%d" % self._cases))

    def run_agg(self, *args, fx=None, out=None, **env):
        out = Path(out) if out is not None else (fx or self.fx).root
        proc = subprocess.run([sys.executable, str(SCRIPT), str(out), *args], capture_output=True, text=True,
                              encoding="utf-8", env=hermetic_env(**env), cwd=str(self.base), timeout=120)
        json_path, txt_path = out / "qa-report.json", out / "qa-report.txt"
        report = json.loads(json_path.read_text(encoding="utf-8")) if json_path.exists() else None
        text = txt_path.read_text(encoding="utf-8") if txt_path.exists() else None
        if report is not None and text is not None and proc.returncode in (0, 1) and "Traceback" not in proc.stderr:
            self.assert_contract(proc, report, text)
        return proc, report, text

    def assert_contract(self, proc, report, text):
        """What regression.sh and rc_aggregate.m3_report_only rely on, checked on every run."""
        self.assertEqual(REPORT_KEYS, set(report))
        self.assertEqual("dd-qa-report-v1", report["schema"])
        self.assertEqual("aggregate", report["driver"])
        self.assertIs(type(report["pass"]), bool)
        self.assertRegex(report["utc"], r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
        self.assertTrue(report["gates"])
        self.assertEqual(report["pass"], all(g["pass"] for g in report["gates"]))
        self.assertEqual(0 if report["pass"] else 1, proc.returncode, proc.stdout + proc.stderr)
        for g in report["gates"]:
            if g["id"].startswith(OWN_GATES) or g["value"] == "missing":
                self.assertEqual(GATE_KEYS, set(g), g)
                self.assertIs(type(g["pass"]), bool, g)
                for key in ("value", "threshold", "note", "source"):
                    self.assertIsInstance(g[key], str, g)
        lines = text.splitlines()
        self.assertTrue(text.endswith("\n"))
        self.assertLessEqual(len(lines), 20)
        self.assertTrue(lines[0].startswith("M3 closure %s · %s · build " % ("PASS" if report["pass"] else "FAIL", report["utc"])), lines[0])
        self.assertEqual("window: %s · load1 %s → %s" % (report["window"], report["load1Before"], report["load1After"]), lines[1])
        for line in lines[2:]:
            self.assertLessEqual(len(line), 220)
        self.assertEqual(text, proc.stdout)

    @staticmethod
    def gates(report):
        return {g["id"]: g for g in report["gates"]}

    def assertGate(self, report, gid, ok, value=None):
        gates = self.gates(report)
        self.assertIn(gid, gates, sorted(gates))
        self.assertIs(ok, gates[gid]["pass"], gates[gid])
        if value is not None:
            self.assertEqual(value, gates[gid]["value"], gates[gid])
        return gates[gid]


class CliTests(AggregateTestCase):
    def test_ArgumanYok_KullanimMesaji_Cikis2(self):
        proc = subprocess.run([sys.executable, str(SCRIPT)], capture_output=True, text=True, env=hermetic_env(),
                              cwd=str(self.base), timeout=60)
        self.assertEqual(2, proc.returncode)
        self.assertEqual("usage: aggregate.py <out-dir> [--window 60] [--perf-runs 3]\n", proc.stdout)
        self.assertEqual([self.fx.root], list(self.base.iterdir()))
        self.assertEqual([], list(self.fx.root.iterdir()))

    def test_Varsayilan_60sn_3Kosu(self):
        self.fx.green()
        proc, report, _ = self.run_agg()
        self.assertEqual(0, proc.returncode)
        self.assertEqual("60 s x 3" + WINDOW_TAIL, report["window"])
        self.assertIn("perf.run3.cleanP99Ms", self.gates(report))
        self.assertNotIn("perf.run4", self.gates(report))

    def test_PencereVePerfKosuArgumanlari_Uygulanir(self):
        self.fx.green(perf_runs=2)
        self.fx.perf(3, frameMeanMs=99.0, frameP99FilteredMs=99.0)  # a third run on disk must not be read with --perf-runs 2
        proc, report, _ = self.run_agg("--perf-runs", "2", "--window", "45")
        self.assertEqual(0, proc.returncode, proc.stdout)
        self.assertEqual("45 s x 2" + WINDOW_TAIL, report["window"])
        self.assertFalse([g for g in self.gates(report) if g.startswith("perf.run3")])
        self.assertEqual("each of 2 runs <= 8.0 ms (DEC-088)", self.gates(report)["perf.meanMs"]["threshold"])

    def test_BozukArguman_PassVermez(self):
        for args in (["--window"], ["--perf-runs"], ["--window", "abc"], ["--perf-runs", "x"]):
            with self.subTest(args=args):
                fx = self.fresh().green()
                proc, report, _ = self.run_agg(*args, fx=fx)
                self.assertNotEqual(0, proc.returncode)
                self.assertIsNone(report)
                self.assertNotIn("PASS", proc.stdout)

    def test_OlmayanCiktiKlasoru_PassVermez(self):
        missing = self.base / "does-not-exist"
        proc, report, _ = self.run_agg(out=missing)
        self.assertNotEqual(0, proc.returncode)
        self.assertIsNone(report)
        self.assertFalse(missing.exists())

    def test_HATA_PerfKosu0_PerfKanitsizPass(self):   # AG-2 fixed (S-10 06.10)
        # DEFECT AG-2 (low/medium, fail-open): `--perf-runs 0` (or negative) skips the perf loop, so no perf gate is
        # written at all and the closure PASSes without one perf window. README (WP-044) says steps that did not run
        # appear as `missing`; regression.sh forwards --perf-runs unchecked (only rc.sh rejects < 3 with 64).
        self.fx.green()
        proc, report, _ = self.run_agg("--perf-runs", "0")
        self.assertNotEqual(0, proc.returncode)
        self.assertTrue(any(g.startswith("perf.") for g in self.gates(report)))
        # the cause, not only the outcome (C-00 06.10 N2): the closure names the missing perf window
        gate = self.assertGate(report, "perf.runs", False, "0")
        self.assertEqual("--perf-runs below 1: the closure has no perf window", gate["note"])


class GreenAndContractTests(AggregateTestCase):
    def test_TamYesil_Pass_Cikis0(self):
        self.fx.green()
        proc, report, text = self.run_agg("--window", "60", "--perf-runs", "3")
        self.assertEqual(0, proc.returncode, text)
        self.assertTrue(report["pass"])
        self.assertTrue(text.startswith("M3 closure PASS · "))

    def test_RaporAlanlari(self):
        self.fx.green()
        _, report, _ = self.run_agg()
        self.assertEqual("WP-026", report["package"])
        self.assertEqual(os.uname().machine + " " + os.uname().sysname, report["machine"])
        self.assertEqual(26, report["seed"])
        self.assertEqual('{"mode":"dev","commit":"abc123","describe":"wp-091-1-gabc","branch":"main",'
                         '"utc":"2026-10-06T00:00:00Z","unity":"2022.3.22f1"}', report["buildInfo"])
        self.assertEqual(("2.31", "2.40"), (report["load1Before"], report["load1After"]))
        notes = report["summary"].split(" | ")
        self.assertEqual("spawnmix: 1000 spawns x rounds 1-10, violations 0", notes[0])
        self.assertEqual("bot: rounds 8/8 kills 120 deaths 0 spawnHash a1b2 outcomeHash c3d4 meanFrame 6.50 ms", notes[1])
        self.assertEqual("perf: draw 120 tris 1.50 M tex 512 MB mesh 128 MB rt 64 MB gc/frame 0 B gen0 0 load1 "
                         "[('2.31', '2.40'), ('2.31', '2.40'), ('2.31', '2.40')]", notes[2])
        self.assertEqual(3, len(notes))

    def test_PaketOrtamDegiskeni(self):
        self.fx.green()
        _, report, _ = self.run_agg(DD_QA_PACKAGE="WP-061")
        self.assertEqual("WP-061", report["package"])

    def test_KapiKumesi_AdimBasinaBeklenenKimlikler(self):
        self.fx.green()
        _, report, _ = self.run_agg()
        expected = {"editmode.qa", *SPAWN_IDS, *BOT_IDS, *readability_ids(), "readability.armorForm.dec077",
                    "perf.meanMs", "perf.cleanP99Ms", "perf.externalPresentContention", "perf.hudCostMs"}
        expected |= {"perf.run%d.%s" % (i, k) for i in (1, 2, 3) for k in ("integrity", "metrics", "meanMs", "cleanP99Ms")}
        ids = [g["id"] for g in report["gates"]]
        self.assertEqual(expected, set(ids))
        self.assertEqual(len(ids), len(set(ids)))

    def test_TuketiciSozlesmesi_RcAggregate(self):
        """Consumer side (rc.sh m3-regression): rc_aggregate.qa_contract + m3_required accept the producer's report."""
        spec = importlib.util.spec_from_file_location("dd_b09_rc_aggregate", QA / "rc_aggregate.py")
        rc = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(rc)
        self.fx.green()
        proc, report, _ = self.run_agg("--window", "60", "--perf-runs", "3", DD_REQUIRE_ULTRA_EVIDENCE="1",
                                       DD_QA_PACKAGE="WP-061")
        self.assertEqual(0, proc.returncode)
        gates = rc.qa_contract(report, "aggregate")
        self.assertIsNotNone(gates, "rc_aggregate.qa_contract rejected aggregate.py output")
        self.assertLessEqual(rc.m3_required(3), set(gates))
        self.assertTrue(any(key.endswith("tierEnvContrast.Ultra") for key in gates))
        # the failing side of the same contract: exit 1 and pass False agree, so m3_report_only's equality holds
        self.fx.child("bot", "QaRunBot", BOT_IDS, failed=("bot.rounds",))
        proc, report, _ = self.run_agg("--window", "60", "--perf-runs", "3", DD_REQUIRE_ULTRA_EVIDENCE="1")
        self.assertEqual((1, False), (proc.returncode, report["pass"]))
        self.assertIsNotNone(rc.qa_contract(report, "aggregate"))


class EmptyOutTests(AggregateTestCase):
    def test_BosKlasor_HerAdimEksik_Fail(self):
        proc, report, text = self.run_agg("--perf-runs", "1")
        self.assertEqual(1, proc.returncode)
        self.assertEqual(["editmode.qa", "spawnmix.violations", "bot.rounds", "perf.run1", "readability.driver",
                          "readability.armorForm.dec077"], [g["id"] for g in report["gates"]])
        self.assertFalse(any(g["pass"] for g in report["gates"]))
        self.assertEqual(("n/a", "", "", 0, ""), (report["buildInfo"], report["load1Before"], report["load1After"],
                                                  report["seed"], report["summary"]))
        self.assertEqual([
            "window: 60 s x 1" + WINDOW_TAIL + " · load1  → ",
            "FAIL editmode.qa = missing (editmode-qa.xml) — step skipped or crashed",
            "FAIL spawnmix.violations = missing (== 0) — spawn-mix/qa-report.json not written",
            "FAIL bot.rounds = missing — bot/qa-report.json not written",
            "FAIL perf.run1 = missing — 04-window.json absent",
            "FAIL readability.driver = missing — readability/qa-report.json not written",
            "FAIL readability.armorForm.dec077 = not run (ArmorTests.Okunabilirlik) — quick mode skips it; full closure runs it",
        ], text.splitlines()[1:])
        self.assertEqual("M3 closure FAIL · %s · build n/a" % report["utc"], text.splitlines()[0])


class EditModeTests(AggregateTestCase):
    def setUp(self):
        super().setUp()
        self.fx.green()

    def run_em(self, xml, **env):
        self.fx.editmode(xml)
        _, report, _ = self.run_agg(**env)
        return self.gates(report)["editmode.qa"], report

    def test_TumuYesil_Pass(self):
        gate, _ = self.run_em(nunit(green_cases(5)))
        self.assertEqual((True, "5/5", "all green, unexpected skips 0", "QA tests or supplied full EditMode suite"),
                         (gate["pass"], gate["value"], gate["threshold"], gate["note"]))
        self.assertEqual(str(self.fx.root / "editmode-qa.xml"), gate["source"])

    def test_BirBasarisiz_Fail(self):
        cases = green_cases(4) + [("X.Fixture.Broken", "Failed", "", ())]
        gate, report = self.run_em(nunit(cases))
        self.assertEqual((False, "4/5"), (gate["pass"], gate["value"]))
        self.assertFalse(report["pass"])

    def test_SifirTest_Fail(self):
        gate, _ = self.run_em(nunit([]))
        self.assertEqual((False, "0/0"), (gate["pass"], gate["value"]))

    def test_Inconclusive_Fail(self):
        gate, _ = self.run_em(nunit(green_cases(5), inconclusive=1))
        self.assertFalse(gate["pass"])

    def test_ExplicitDiagnosticsAtlamasi_IzinliPass(self):
        cases = green_cases(4) + [("X.Fixture.Probe", "Skipped", 'label="Explicit" runstate="Explicit"', ("Diagnostics",))]
        gate, _ = self.run_em(nunit(cases))
        self.assertEqual((True, "4/5 +1 diagnostics"), (gate["pass"], gate["value"]))

    def test_IgnoreAtlamasi_BeklenmedikFail(self):
        cases = green_cases(4) + [("X.Fixture.OptIn", "Skipped", 'label="Ignored" runstate="Runnable"', ("Diagnostics",))]
        gate, _ = self.run_em(nunit(cases))
        self.assertFalse(gate["pass"])
        self.assertEqual("QA tests or supplied full EditMode suite; unexpected skips: X.Fixture.OptIn [Ignored]", gate["note"])

    def test_IgnoreAtlamasi_KokSayaciSifirOlsaDa_Fail(self):
        # defence in depth: the per-case skip rule holds even when the run's skipped counter does not count the case
        cases = green_cases(4) + [("X.Fixture.OptIn", "Skipped", 'label="Ignored"', ())]
        gate, _ = self.run_em(nunit(cases, skipped=0))
        self.assertFalse(gate["pass"])
        self.assertTrue(gate["note"].endswith("; unexpected skips: X.Fixture.OptIn [Ignored]"), gate["note"])

    def test_KokAtlamaSayisiUyusmaz_Fail(self):
        cases = green_cases(4) + [("X.Fixture.Probe", "Skipped", 'label="Explicit"', ("Diagnostics",))]
        gate, _ = self.run_em(nunit(cases, skipped=2))
        self.assertFalse(gate["pass"])

    def test_BozukXml_Eksik(self):
        for xml in ("<test-run total='3'", "not xml at all", '<test-run total="many" passed="1"/>'):
            with self.subTest(xml=xml):
                gate, _ = self.run_em(xml)
                self.assertEqual((False, "missing", "step skipped or crashed"), (gate["pass"], gate["value"], gate["note"]))

    def test_DisXmlOrtamDegiskeni_Oncelikli(self):
        external = self.fresh().text("full-editmode/editmode.xml", nunit(green_cases(2) + [("X.Broken", "Failed", "", ())]))
        gate, _ = self.run_em(nunit(green_cases(5)), DD_EXTERNAL_EDITMODE_XML=str(external))
        self.assertEqual((False, "2/3", str(external)), (gate["pass"], gate["value"], gate["source"]))


class ChildReportTests(AggregateTestCase):
    def setUp(self):
        super().setUp()
        self.fx.green()

    def test_CocukKapilar_KaynakOnekiyleAktarilir(self):
        self.fx.child("spawn-mix", "SpawnMix", SPAWN_IDS, source="spawn-mix.json", summary="s")
        self.fx.child("bot", "QaRunBot", BOT_IDS, source="")
        _, report, _ = self.run_agg()
        gates = self.gates(report)
        self.assertEqual("spawn-mix/spawn-mix.json", gates["spawnmix.violations"]["source"])
        self.assertEqual("bot/qa-report.json", gates["bot.rounds"]["source"])
        self.assertEqual("readability/fixture.json", gates[ULTRA_ID]["source"])
        self.assertEqual(child_gate("bot.rounds", True, "")["threshold"], gates["bot.rounds"]["threshold"])

    def test_KaynakBosVeyaYok_VarsayilanQaReport(self):
        for folder, gid in (("spawn-mix", "spawnmix.simulated"), ("bot", "bot.alloc.method"), ("readability", "readability.r1.occlusion")):
            for variant in ("empty", "absent"):
                with self.subTest(child=folder, source=variant):
                    fx = self.fresh().green()
                    report = json.loads((fx.root / folder / "qa-report.json").read_text(encoding="utf-8"))
                    for g in report["gates"]:
                        if variant == "empty":
                            g["source"] = ""
                        else:
                            del g["source"]
                    fx.json(folder + "/qa-report.json", report)
                    _, out, _ = self.run_agg(fx=fx)
                    self.assertEqual(folder + "/qa-report.json", self.gates(out)[gid]["source"])

    def test_CocukKapisiKirmizi_GenelFail(self):
        for folder, driver, ids, failing in (("spawn-mix", "SpawnMix", SPAWN_IDS, "spawnmix.violations"),
                                             ("bot", "QaRunBot", BOT_IDS, "bot.determinism"),
                                             ("readability", "QaReadabilityGate", readability_ids(), "readability.r8.occlusion")):
            with self.subTest(child=folder):
                fx = self.fresh().green()
                fx.child(folder, driver, ids, failed=(failing,))
                proc, report, _ = self.run_agg(fx=fx)
                self.assertEqual(1, proc.returncode)
                self.assertEqual([failing], [g["id"] for g in report["gates"] if not g["pass"]])

    def test_CocukRaporEksikBosVeyaBozuk_EksikKapisi(self):
        cases = (("spawn-mix", "spawnmix.violations", "spawn-mix/"), ("bot", "bot.rounds", "bot/"),
                 ("readability", "readability.driver", "readability/"))
        for folder, gid, source in cases:
            for variant in ("absent", "empty-gates", "truncated", "empty-list"):
                with self.subTest(child=folder, variant=variant):
                    fx = self.fresh().green()
                    if variant == "absent":
                        fx.remove(folder + "/qa-report.json")
                    elif variant == "empty-gates":
                        fx.json(folder + "/qa-report.json", {"schema": "dd-qa-report-v1", "pass": True, "gates": []})
                    elif variant == "truncated":
                        fx.text(folder + "/qa-report.json", '{"schema": "dd-qa-report-v1", "gates": [')
                    else:
                        fx.json(folder + "/qa-report.json", [])
                    proc, report, _ = self.run_agg(fx=fx)
                    self.assertEqual(1, proc.returncode)
                    gate = self.assertGate(report, gid, False, "missing")
                    self.assertEqual(source, gate["source"])

    def test_SpawnOzeti_160KarakterNot(self):
        self.fx.child("spawn-mix", "SpawnMix", SPAWN_IDS, summary="ş" * 300)
        _, report, _ = self.run_agg()
        self.assertEqual("spawnmix: " + "ş" * 160, report["summary"].split(" | ")[0])

    def test_BotOzetiYok_NotYokTohum0(self):
        self.fx.remove("bot/bot-summary.json")
        _, report, _ = self.run_agg()
        self.assertEqual(0, report["seed"])
        self.assertFalse(any(n.startswith("bot: ") for n in report["summary"].split(" | ")))
        self.assertGate(report, "bot.rounds", True)

    def test_BotOzetiVarRaporYok_BotKapisiEksik(self):
        self.fx.remove("bot/qa-report.json")
        _, report, _ = self.run_agg()
        self.assertGate(report, "bot.rounds", False, "missing")
        self.assertIn("bot: rounds 8/8", report["summary"])

    def test_BotOzetiAlanlariEksik_NoneVe0(self):
        self.fx.json("bot/bot-summary.json", {"seed": 61})
        _, report, _ = self.run_agg()
        self.assertEqual(61, report["seed"])
        self.assertIn("bot: rounds None/None kills None deaths None spawnHash None outcomeHash None meanFrame 0.00 ms",
                      report["summary"])

    def test_Unicode_NotVeOzetKorunur(self):
        report = json.loads((self.fx.root / "spawn-mix/qa-report.json").read_text(encoding="utf-8"))
        report["summary"] = "ölçüm: 1000 spawn — ihlal 0 ✓"
        report["gates"][0]["note"] = "zırh kısıtı ığüşöç"
        report["gates"][0]["pass"] = False
        self.fx.json("spawn-mix/qa-report.json", report)
        _, out, text = self.run_agg("--perf-runs", "1")
        self.assertEqual("zırh kısıtı ığüşöç", self.gates(out)["spawnmix.violations"]["note"])
        self.assertIn("spawnmix: ölçüm: 1000 spawn — ihlal 0 ✓", out["summary"])
        self.assertIn("FAIL spawnmix.violations = 0.210 (>= 0.06) — zırh kısıtı ığüşöç", text)


class PerfTests(AggregateTestCase):
    def setUp(self):
        super().setUp()
        self.fx.green()

    def test_YesilDegerlerVeBicim(self):
        _, report, _ = self.run_agg()
        self.assertGate(report, "perf.run1.meanMs", True, "6.50")
        self.assertGate(report, "perf.run1.cleanP99Ms", True, "9.00")
        self.assertGate(report, "perf.run1.metrics", True, "present")
        self.assertGate(report, "perf.run1.integrity", True, "window ok")
        self.assertGate(report, "perf.meanMs", True, "avg 6.50; runs 6.50, 6.50, 6.50")
        self.assertGate(report, "perf.cleanP99Ms", True, "avg 9.00; runs 9.00, 9.00, 9.00; raw p99 9.50, 9.50, 9.50")
        self.assertGate(report, "perf.externalPresentContention", True, "stalls>33.3ms 3 / max 41.0 ms")
        self.assertGate(report, "perf.hudCostMs", True, "0.200 (runs 0.200, 0.200, 0.200; frame delta +0.100, +0.100, +0.100)")
        gate = self.gates(report)["perf.run2.meanMs"]
        self.assertEqual(("<= 8.0 ms", "independent DEC-088 window", "perf-2/04-window.json"),
                         (gate["threshold"], gate["note"], gate["source"]))

    def test_Sinirlar_Ortalama8_Temizp99_10_Dahil(self):
        for i in (1, 2, 3):
            self.fx.perf(i, frameMeanMs=8.0, frameP99FilteredMs=10.0)
        proc, report, _ = self.run_agg()
        self.assertEqual(0, proc.returncode)
        self.assertGate(report, "perf.run1.meanMs", True, "8.00")
        self.assertGate(report, "perf.run1.cleanP99Ms", True, "10.00")

    def test_SinirUstu_TekKosu_Fail(self):
        for field, value, run_gate, total_gate in (("frameMeanMs", 8.001, "perf.run2.meanMs", "perf.meanMs"),
                                                   ("frameP99FilteredMs", 10.001, "perf.run2.cleanP99Ms", "perf.cleanP99Ms")):
            with self.subTest(field=field):
                fx = self.fresh().green()
                fx.perf(2, **{field: value})
                proc, report, _ = self.run_agg(fx=fx)
                self.assertEqual(1, proc.returncode)
                self.assertGate(report, run_gate, False)
                self.assertGate(report, total_gate, False)
                self.assertEqual(sorted([run_gate, total_gate]), sorted(g["id"] for g in report["gates"] if not g["pass"]))

    def test_Ortalama_ButceUstuKosuyuGizlemez(self):
        self.fx.perf(1, frameMeanMs=7.0, frameP99FilteredMs=9.0)
        self.fx.perf(2, frameMeanMs=8.5, frameP99FilteredMs=10.5)
        self.fx.perf(3, frameMeanMs=7.0, frameP99FilteredMs=9.0)
        _, report, _ = self.run_agg()
        self.assertGate(report, "perf.meanMs", False, "avg 7.50; runs 7.00, 8.50, 7.00")
        self.assertGate(report, "perf.cleanP99Ms", False, "avg 9.50; runs 9.00, 10.50, 9.00; raw p99 9.50, 9.50, 9.50")

    def test_HamP99_KapiDegil(self):
        for i in (1, 2, 3):
            self.fx.perf(i, frameP99Ms=25.0)
        proc, report, _ = self.run_agg()
        self.assertEqual(0, proc.returncode)
        self.assertIn("raw p99 25.00, 25.00, 25.00", self.gates(report)["perf.cleanP99Ms"]["value"])

    def test_EksikKosu_ToplamKapiFail(self):
        self.fx.remove("perf-2/04-window.json")
        _, report, _ = self.run_agg()
        gate = self.assertGate(report, "perf.run2", False, "missing")
        self.assertEqual(("04-window.json absent", "perf-2/"), (gate["note"], gate["source"]))
        self.assertFalse([g for g in self.gates(report) if g.startswith("perf.run2.")])
        self.assertGate(report, "perf.meanMs", False, "avg 6.50; runs 6.50, 6.50")
        self.assertGate(report, "perf.cleanP99Ms", False)

    def test_BozukPencereJson_Eksik(self):
        for content in ('{"frameMeanMs": 6.5', "", "[]", "null", "{}"):
            with self.subTest(content=content):
                fx = self.fresh().green()
                fx.text("perf-1/04-window.json", content)
                _, report, _ = self.run_agg(fx=fx)
                self.assertGate(report, "perf.run1", False, "missing")

    def test_GecersizMetrik_FailKapali(self):
        variants = (("NaN", float("nan")), ("Infinity", float("inf")), ("zero", 0), ("negative", -1.0),
                    ("bool", True), ("string", "6.5"), ("null", None))
        for label, value in variants:
            for field in ("frameMeanMs", "frameP99Ms", "frameP99FilteredMs"):
                with self.subTest(field=field, value=label):
                    fx = self.fresh().green()
                    fx.perf(1, **{field: value})
                    proc, report, _ = self.run_agg(fx=fx)
                    self.assertEqual(1, proc.returncode)
                    self.assertGate(report, "perf.run1.metrics", False, "missing/non-finite")
                    # one invalid metric invalidates the run: both budget gates of that run are red
                    self.assertGate(report, "perf.run1.meanMs", False)
                    self.assertGate(report, "perf.run1.cleanP99Ms", False)
                    if field == "frameMeanMs":
                        self.assertGate(report, "perf.run1.meanMs", False, "missing/non-positive")
                        self.assertGate(report, "perf.meanMs", False)
                    if field == "frameP99FilteredMs":
                        self.assertGate(report, "perf.run1.cleanP99Ms", False, "missing/non-positive")
                        self.assertGate(report, "perf.cleanP99Ms", False)

    def test_EksikMetrikAlani_FailKapali(self):
        for field in ("frameMeanMs", "frameP99Ms", "frameP99FilteredMs"):
            with self.subTest(field=field):
                fx = self.fresh().green()
                w = window()
                del w[field]
                fx.json("perf-3/04-window.json", w)
                proc, report, _ = self.run_agg(fx=fx)
                self.assertEqual(1, proc.returncode)
                self.assertGate(report, "perf.run3.metrics", False)

    def test_Butunluk_ProbePassYokVeyaFalse_Fail(self):
        long_note = "probe stage 04 failed: crowd floor 31 < 40 while forcing target; window discarded at t=41 s"
        self.fx.perf(1, pass_=None)
        w = window(note=long_note)
        w["pass"] = False
        self.fx.json("perf-2/04-window.json", w)
        w = window()
        del w["pass"]
        del w["note"]
        self.fx.json("perf-3/04-window.json", w)
        _, report, _ = self.run_agg()
        self.assertGate(report, "perf.run1.integrity", False, "window ok")
        self.assertGate(report, "perf.run2.integrity", False, long_note[:60])
        self.assertGate(report, "perf.run3.integrity", False, "")
        self.assertGate(report, "perf.run2.meanMs", True)

    def test_SunumCekismesi_YalnizRapor(self):
        for i in (1, 2, 3):
            self.fx.perf(i, framesExcludedAsStalls=40, frameMaxMs=250.0 if i == 2 else 50.0)
        proc, report, _ = self.run_agg()
        self.assertEqual(0, proc.returncode)
        gate = self.assertGate(report, "perf.externalPresentContention", True, "stalls>33.3ms 120 / max 250.0 ms")
        self.assertEqual("report only", gate["threshold"])

    def test_Hud_Sinir035Dahil_Ustu_Fail(self):
        for cost, ok in ((0.35, True), (0.3501, False)):
            with self.subTest(cost=cost):
                fx = self.fresh().green()
                for i in (1, 2, 3):
                    fx.hud(i, cost=cost, delta=-0.05)
                _, report, _ = self.run_agg(fx=fx)
                gate = self.assertGate(report, "perf.hudCostMs", ok)
                self.assertEqual("%.3f (runs %.3f, %.3f, %.3f; frame delta -0.050, -0.050, -0.050)" % ((cost,) * 4), gate["value"])
                self.assertEqual("<= 0.35 ms UI CPU + GPU (WP-068 HUD budget)", gate["threshold"])

    def test_Hud_YokVeyaOlcumBasarisiz_Fail(self):
        for variant in ("absent", "measurement-failed"):
            with self.subTest(variant=variant):
                fx = self.fresh().green()
                for i in (1, 2, 3):
                    if variant == "absent":
                        fx.remove("perf-%d/04h-hud.json" % i)
                    else:
                        fx.hud(i, ok=False)
                _, report, _ = self.run_agg(fx=fx)
                gate = self.assertGate(report, "perf.hudCostMs", False, "missing")
                self.assertEqual("04h-hud.json absent or its measurement failed", gate["note"])

    def test_Hud_TekKosuBasarisiz_OrtalamaGizlemez(self):
        # S-10 06.10 (C-00's B-09 residual risk): two good runs must not average away the one whose measurement failed
        for i, ok in ((1, True), (2, False), (3, True)):
            self.fx.hud(i, ok=ok)
        proc, report, _ = self.run_agg()
        self.assertEqual(1, proc.returncode)
        gate = self.assertGate(report, "perf.hudCostMs", False, "measurement failed in run(s) 2")

    def test_CocukKapi_PassAlaniYoksa_NotluFail(self):
        report = json.loads((self.fx.green().root / "bot/qa-report.json").read_text(encoding="utf-8"))
        for value in ("true", 1, None):
            with self.subTest(value=value):
                report["gates"][0]["pass"] = value
                self.fx.json("bot/qa-report.json", report)
                proc, out, _ = self.run_agg()
                self.assertEqual(1, proc.returncode)
                gate = next(g for g in out["gates"] if g["id"] == report["gates"][0]["id"])
                self.assertIs(False, gate["pass"])
                self.assertIn("counted as FAIL", gate["note"])

    def test_PerfPenceresiYok_ToplamPerfKapisiYazilmaz(self):
        for i in (1, 2, 3):
            self.fx.remove("perf-%d/04-window.json" % i)
        _, report, _ = self.run_agg()
        ids = self.gates(report)
        self.assertEqual(["perf.run1", "perf.run2", "perf.run3"], sorted(g for g in ids if g.startswith("perf.")))
        self.assertFalse(any(n.startswith("perf: ") for n in report["summary"].split(" | ")))

    def test_Load1_MacVeLinuxBicimi(self):
        self.fx.text("perf-1/load-before.txt", "load averages: 3.75 2.10 1.95\n")
        self.fx.text("perf-3/load-after.txt", " 10:00:01 up 1 day, load average: 0.52, 0.58, 0.59\n")
        self.fx.remove("perf-2/load-before.txt")
        self.fx.text("perf-2/load-after.txt", "uptime: command not found\n")
        _, report, _ = self.run_agg()
        self.assertEqual(("3.75", "0.52"), (report["load1Before"], report["load1After"]))
        self.assertIn("load1 [('3.75', '2.40'), ('', ''), ('2.31', '0.52')]", report["summary"])


class ArmorAndUltraTests(AggregateTestCase):
    def setUp(self):
        super().setUp()
        self.fx.green()

    def test_ZirhTesti_YesilKirmiziYok(self):
        for results, ok, value in ((("Passed",), True, "1 matching test(s)"), (("Failed",), False, "1 matching test(s)"),
                                   (("Passed", "Failed"), False, "2 matching test(s)"), ((), False, "0 matching test(s)")):
            with self.subTest(results=results):
                fx = self.fresh().green()
                fx.armor(results)
                _, report, _ = self.run_agg(fx=fx)
                gate = self.assertGate(report, "readability.armorForm.dec077", ok, value)
                self.assertEqual(str(fx.root / "playmode-armor.xml"), gate["source"])

    def test_ZirhTesti_NameNiteligiyleDeEslesir(self):
        self.fx.text("playmode-armor.xml", '<test-run total="1" passed="1"><test-case name="%s" result="Passed"/></test-run>' % ARMOR)
        _, report, _ = self.run_agg()
        self.assertGate(report, "readability.armorForm.dec077", True, "1 matching test(s)")

    def test_ZirhTesti_YokVeyaBozuk_KosulmadiFail(self):
        for variant in ("absent", "corrupt"):
            with self.subTest(variant=variant):
                fx = self.fresh().green()
                if variant == "absent":
                    fx.remove("playmode-armor.xml")
                else:
                    fx.text("playmode-armor.xml", "<test-run")
                _, report, _ = self.run_agg(fx=fx)
                gate = self.assertGate(report, "readability.armorForm.dec077", False, "not run")
                self.assertEqual("playmode-armor.xml", gate["source"])

    def test_ZirhTesti_DisXmlOrtamDegiskeni(self):
        external = self.fresh().armor(("Failed",), relative="full-playmode/playmode.xml")
        _, report, _ = self.run_agg(DD_EXTERNAL_PLAYMODE_XML=str(external))
        gate = self.assertGate(report, "readability.armorForm.dec077", False)
        self.assertEqual(str(external), gate["source"])

    def test_UltraKaniti_YalnizOrtamDegiskeni1Iken(self):
        for value in (None, "0", "true"):
            with self.subTest(env=value):
                env = {} if value is None else {"DD_REQUIRE_ULTRA_EVIDENCE": value}
                _, report, _ = self.run_agg(**env)
                self.assertNotIn("readability.ultraEvidence", self.gates(report))

    def test_UltraKaniti_VarYokKirmizi(self):
        for ultra_present, ultra_ok, ok, value in ((True, True, True, "1 measured gate(s)"), (True, False, False, "1 measured gate(s)"),
                                                   (False, None, False, "0 measured gate(s)")):
            with self.subTest(present=ultra_present, ultra_pass=ultra_ok):
                fx = self.fresh().green(ultra=ultra_present)
                if ultra_present and not ultra_ok:
                    fx.child("readability", "QaReadabilityGate", readability_ids(), failed=(ULTRA_ID,))
                _, report, _ = self.run_agg(fx=fx, DD_REQUIRE_ULTRA_EVIDENCE="1")
                gate = self.assertGate(report, "readability.ultraEvidence", ok, value)
                self.assertEqual("readability/qa-report.json", gate["source"])


class BuildInfoTests(AggregateTestCase):
    def setUp(self):
        super().setUp()
        self.fx.green()

    def test_YalnizBilinenAnahtarlar_Sirali(self):
        self.fx.json("build-info.json", {"gitCommit": "c0ffee", "version": "0.4.0", "token": "never-copied", "mode": "release"})
        _, report, text = self.run_agg()
        self.assertEqual('{"mode":"release","version":"0.4.0","gitCommit":"c0ffee"}', report["buildInfo"])
        self.assertIn(' · build {"mode":"release","version":"0.4.0","gitCommit":"c0ffee"}', text.splitlines()[0])

    def test_BilinenAnahtarYok_TumNesne(self):
        self.fx.json("build-info.json", {"result": "Succeeded", "totalErrors": 0})
        _, report, _ = self.run_agg()
        self.assertEqual('{"result":"Succeeded","totalErrors":0}', report["buildInfo"])

    def test_YedekBuildDevRaporu(self):
        self.fx.remove("build-info.json")
        self.fx.json("build-dev/build-report.json", {"mode": "dev", "commit": "fallback"})
        _, report, _ = self.run_agg()
        self.assertEqual('{"mode":"dev","commit":"fallback"}', report["buildInfo"])

    def test_YokBosVeyaBozuk_NA(self):
        for content in (None, "{}", "{bad json"):
            with self.subTest(content=content):
                fx = self.fresh().green()
                if content is None:
                    fx.remove("build-info.json")
                else:
                    fx.text("build-info.json", content)
                _, report, _ = self.run_agg(fx=fx)
                self.assertEqual("n/a", report["buildInfo"])

    def test_Uzun_240KarakteraKesilir_Baslik80(self):
        self.fx.json("build-info.json", {"describe": "d" * 500})
        _, report, text = self.run_agg()
        self.assertEqual(240, len(report["buildInfo"]))
        self.assertTrue(text.splitlines()[0].endswith(" · build " + report["buildInfo"][:80]))


class TextSummaryTests(AggregateTestCase):
    def test_TamKosu_16KapiSonrasiKatlanir_20Satir(self):
        self.fx.green()
        _, report, text = self.run_agg(DD_REQUIRE_ULTRA_EVIDENCE="1")
        lines = text.splitlines()
        self.assertEqual(20, len(lines))
        shown = [line for line in lines[2:] if line.startswith(("PASS ", "FAIL "))]
        self.assertEqual(16, len(shown))
        self.assertEqual("PASS editmode.qa = 5/5 (all green, unexpected skips 0) — QA tests or supplied full EditMode suite", shown[0])
        self.assertEqual("… +%d gates in qa-report.json" % (len(report["gates"]) - 16), lines[18])
        self.assertEqual("spawnmix: 1000 spawns x rounds 1-10, violations 0", lines[19])  # the 2nd note falls past line 20

    def test_EnFazlaIkiNot(self):
        # three notes (spawnmix, bot, perf) under the 16-gate cap: only the first two reach the text
        self.fx.child("spawn-mix", "SpawnMix", ("spawnmix.violations",), summary="mix ok")
        self.fx.bot_summary()
        self.fx.perf(1)
        _, report, text = self.run_agg("--perf-runs", "1")
        self.assertEqual(3, len(report["summary"].split(" | ")))
        lines = text.splitlines()
        self.assertEqual(2 + len(report["gates"]) + 2, len(lines))
        self.assertEqual(["spawnmix: mix ok", "bot: rounds 8/8 kills 120 deaths 0 spawnHash a1b2 outcomeHash c3d4 meanFrame 6.50 ms"],
                         lines[-2:])
        self.assertFalse([line for line in lines if line.startswith("perf: ")])

    def test_SatirBicimi_EsikVeNot(self):
        fx = self.fx
        fx.child("spawn-mix", "SpawnMix", SPAWN_IDS)
        report = json.loads((fx.root / "spawn-mix/qa-report.json").read_text(encoding="utf-8"))
        report["gates"][0].update(threshold="", note="")
        report["gates"][1].update(value="v" * 300)
        fx.json("spawn-mix/qa-report.json", report)
        _, _, text = self.run_agg("--perf-runs", "1")
        lines = text.splitlines()
        self.assertIn("PASS spawnmix.violations = 0.210", lines)
        long_line = [line for line in lines if line.startswith("PASS spawnmix.simulated = ")][0]
        self.assertEqual(220, len(long_line))
        self.assertIn("PASS spawnmix.notes = 0.210 (>= 0.06) — fixture", lines)

    def test_KisiBasinaOkunabilirlik_Katlanir_BasarisizGorunur(self):
        # The folding rule on the ids QaReadabilityGate writes (S-10 06.10, AG-3): passing readability.r*.player*Contrast
        # lines are hidden, failing ones and every other readability line stay visible; the JSON keeps all of them.
        report = {"schema": "dd-qa-report-v1", "driver": "QaReadabilityGate", "pass": False, "gates": [
            child_gate("readability.r1.occlusion"),
            child_gate("readability.r1.playerEnvContrast.skin0"),
            child_gate("readability.r1.playerZombieContrast.skin2"),
            child_gate("readability.r1.playerZombieContrast.skin1", ok=False),
        ]}
        self.fx.json("readability/qa-report.json", report)
        _, out, text = self.run_agg("--perf-runs", "1")
        self.assertIn("PASS readability.r1.occlusion = 0.210 (>= 0.06) — fixture", text)
        self.assertIn("FAIL readability.r1.playerZombieContrast.skin1 = 0.210 (>= 0.06) — fixture", text)
        self.assertNotIn("playerEnvContrast.skin0", text)
        self.assertNotIn("playerZombieContrast.skin2", text)
        self.assertIn("readability.r1.playerEnvContrast.skin0", self.gates(out))  # folded in the text only, kept in JSON

    def test_HATA_KisiBasinaOkunabilirlik_GuncelKimliklerKatlanmaz(self):   # AG-3 fixed (S-10 06.10)
        # DEFECT AG-3 (low, text summary only): the fold matches ".playerEnvDeltaV" / ".playerZombieDeltaV", but
        # QaReadabilityGate.cs has written "readability.rN.playerEnvContrast.skinS" / ".playerZombieContrast.skinS" since dc32ecf1
        # (the names rc_aggregate.m3_required also expects). The fold is dead code: passing per-skin lines take the
        # 16 summary slots. JSON is unaffected.
        self.fx.child("readability", "QaReadabilityGate", readability_ids(rounds=(1,), ultra=False))
        _, _, text = self.run_agg("--perf-runs", "1")
        self.assertIn("PASS readability.r1.occlusion", text)
        self.assertNotIn("playerEnvContrast.skin0", text)


class MalformedInputTests(AggregateTestCase):
    def test_TipiBozukGirdi_PassVermez(self):
        """Whatever the tool does with a schema-violating file (gate or crash), it never reports PASS."""
        def perf_list(fx):
            fx.text("perf-1/04-window.json", "[1, 2]")

        def perf_note_null(fx):
            fx.perf(1, note=None)

        def perf_stalls_null(fx):
            fx.perf(2, framesExcludedAsStalls=None)

        def perf_texture_null(fx):
            fx.perf(3, textureMemoryMax=None)

        def bot_mean_null(fx):
            fx.bot_summary(meanFrameMs=None)

        def child_gate_without_pass(fx):
            report = json.loads((fx.root / "bot/qa-report.json").read_text(encoding="utf-8"))
            del report["gates"][0]["pass"]
            fx.json("bot/qa-report.json", report)

        def child_gate_without_id(fx):
            report = json.loads((fx.root / "bot/qa-report.json").read_text(encoding="utf-8"))
            report["gates"][0]["pass"] = False
            del report["gates"][0]["id"]
            fx.json("bot/qa-report.json", report)

        def child_gates_not_list(fx):
            fx.json("readability/qa-report.json", {"gates": "readability.r1.occlusion"})

        def child_is_string(fx):
            fx.json("spawn-mix/qa-report.json", "PASS")

        def hud_cost_string(fx):
            fx.hud(1, cost="0.1")

        for mutate in (perf_list, perf_note_null, perf_stalls_null, perf_texture_null, bot_mean_null,
                       child_gate_without_pass, child_gate_without_id, child_gates_not_list, child_is_string, hud_cost_string):
            with self.subTest(case=mutate.__name__):
                fx = self.fresh().green()
                mutate(fx)
                proc, report, _ = self.run_agg(fx=fx)
                self.assertNotEqual(0, proc.returncode, proc.stdout)
                self.assertTrue(report is None or report["pass"] is False)
                self.assertNotIn("M3 closure PASS", proc.stdout)

    def test_HATA_CocukKapiPassMetin_YesilSayilir(self):   # AG-1 fixed (S-10 06.10)
        # DEFECT AG-1 (low/medium, fail-open): child gates are copied as-is and judged by truthiness
        # (`all(g["pass"] for g in gates)`), so a child gate whose pass is the string "false" counts as green and
        # regression.sh exits 0. rc_aggregate.contract_items refuses exactly this ("never coerce a purported PASS to
        # boolean"), so the RC path fails closed; the standalone M3 closure does not.
        report = json.loads((self.fx.green().root / "spawn-mix/qa-report.json").read_text(encoding="utf-8"))
        report["gates"][0]["pass"] = "false"
        self.fx.json("spawn-mix/qa-report.json", report)
        proc, out, text = self.run_agg()
        self.assertEqual(1, proc.returncode)
        self.assertFalse(out["pass"])
        # the cause (C-00 06.10 N2): that child gate itself is FAIL, with the reason, in JSON and in the text
        gate = next(g for g in out["gates"] if g["id"] == report["gates"][0]["id"])
        self.assertIs(False, gate["pass"])
        self.assertIn("non-boolean pass 'false' counted as FAIL", gate["note"])
        self.assertIn("FAIL " + gate["id"], text)


if __name__ == "__main__":
    unittest.main()
