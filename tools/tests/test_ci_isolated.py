#!/usr/bin/env python3
"""Codex-audit M-11 (C-00 30.09) · tools/ci_isolated.py: the main-ci core PlayMode -testFilter excludes every test listed
in tools/ci-isolated.txt, each `alone` entry gets a filter matching only that test, `a3` entries get no step, and a
malformed file is refused. Unity's -testFilter is a .NET regex with search semantics against the full test name, and NUnit
runs a test when the filter matches it OR an ancestor (namespace / fixture) — runs_under() models both; Python's `re` and
.NET agree on the constructs used (anchors, escapes, groups, alternation, negative lookahead).
usage: python3 tools/tests/test_ci_isolated.py"""
import re, subprocess, sys, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import ci_isolated as ci  # noqa: E402

ARMOR = "DD.Art.Tests.PlayMode.ArmorTests."
CROWD = ARMOR + "Run_AP005v2_40SkinnedZombi_TamZirh_Binder_5Renderer_TekMateryal_KurallarUyumlu"
DEC077 = ARMOR + "Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet"
ARMOR_KEPT = ARMOR + "ZirhDurumGecisi_HasarYoluyla_Present_Damaged_Broken_OlayAnkrajda_GiysiSabit"
WP073 = "DD.Prototype.Tests.PlayMode.Wp073LauncherVfxTests."
ALLOC = WP073 + "WarmPresentation_DoesNotAllocateOrConsumeGameplayRandom"
KEPT = [ARMOR_KEPT, WP073 + "Flight_UsesGeneration_AfterEarlierLiveIndexEnds", WP073 + "TierSmokeLife_PreservesFlightIdentity",
        "DD.Prototype.Tests.PlayMode.CoreCombatSliceTests.GameFeelOlaylari",
        "DD.Prototype.Tests.PlayMode.RunTreePinActionRegressionTests.QuotaAndReopen_EN",
        "DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Anything"]
NOT_CORE = ["DD.Prototype.Tests.PlayMode.VfxHotPathBisectTests.Idle", "DD.Art.Tests.PlayMode.CharacterTests.Capture",
            "DD.Prototype.Tests.PlayMode.ArmorTestsExtra.X"]


def runs_under(rx, fullname):
    parts = fullname.split(".")
    return any(re.search(rx, ".".join(parts[:i])) for i in range(1, len(parts) + 1))


class RealFilesTests(unittest.TestCase):
    """The committed tools/ci-suites.txt + tools/ci-isolated.txt."""
    def setUp(self):
        self.entries = ci.parse_isolated(TOOLS / "ci-isolated.txt")
        self.suites = ci.read_suites(TOOLS / "ci-suites.txt")
        self.core = ci.core_filter(self.suites, self.entries)

    def test_Kayitlar_IkiA3BirAlone(self):
        self.assertEqual([(w, bool(re.search(p, n))) for (p, w, _), n in zip(self.entries, (CROWD, DEC077, ALLOC))],
                         [("a3", True), ("a3", True), ("alone", True)])

    def test_CekirdekFiltre_IzoleleriDislar_KalanlariKosar(self):
        for name in (CROWD, DEC077, ALLOC):
            self.assertFalse(runs_under(self.core, name), name)
        for name in KEPT:
            self.assertTrue(runs_under(self.core, name), name)
        for name in NOT_CORE:
            self.assertFalse(runs_under(self.core, name), name)

    def test_CekirdekFiltre_FikstureVeIsimAlaninaEslesmez(self):
        # a fixture/namespace match would run ALL its tests (NUnit parent match) and undo the exclusion
        for anc in ("DD", "DD.Art", "DD.Art.Tests.PlayMode", "DD.Art.Tests.PlayMode.ArmorTests",
                    "DD.Prototype.Tests.PlayMode.Wp073LauncherVfxTests"):
            self.assertIsNone(re.search(self.core, anc), anc)

    def test_AloneFiltre_YalnizOTesti(self):
        steps = ci.alone_steps(self.entries)
        self.assertEqual([s for s, _ in steps], ["playmode-alone-1-WarmPresentation_DoesNotAllocateOrConsumeGameplayRandom"])
        rx = steps[0][1]
        self.assertTrue(runs_under(rx, ALLOC))
        for name in (CROWD, DEC077, *KEPT, *NOT_CORE):
            self.assertFalse(runs_under(rx, name), name)
        self.assertNotIn(";", rx)
        self.assertNotIn(";", self.core)

    def test_EskiFiltreyleAyniKume_IzolelerHaric(self):
        old = r"\.(" + "|".join(self.suites) + r")\."
        for name in [CROWD, DEC077, ALLOC, *KEPT, *NOT_CORE]:
            expect = bool(re.search(old, name)) and name not in (CROWD, DEC077, ALLOC)
            self.assertEqual(bool(re.search(self.core, name)), expect, name)

    def test_SuitlerSondakiYorumuAtar(self):
        self.assertIn("ArmorTests", self.suites)
        self.assertTrue(all(re.fullmatch(r"[A-Za-z_]\w*", s) for s in self.suites))


class BuilderTests(unittest.TestCase):
    def write(self, text):
        f = tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8")
        f.write(text); f.close()
        self.addCleanup(Path(f.name).unlink)
        return f.name

    def test_BosListe_EskiFiltre(self):
        self.assertEqual(ci.core_filter(["A", "B"], []), r"\.(A|B)\.")
        self.assertEqual(ci.parse_isolated("/nonexistent/ci-isolated.txt"), [])

    def test_AnkrajsizDesen_AramaSemantigiyleDislar(self):
        rx = ci.core_filter(["F", "G"], [(r"\.F\.Slow", "a3", "why"), (r"G\.Alloc_\w+$", "alone", "why")])
        self.assertIsNone(re.search(rx, "N.F.Slow"))
        self.assertIsNone(re.search(rx, "N.F.SlowTwo"))          # search semantics: a prefix pattern drops both
        self.assertIsNone(re.search(rx, "N.G.Alloc_X"))
        self.assertIsNotNone(re.search(rx, "N.F.Fast"))
        self.assertIsNotNone(re.search(rx, "N.G.Other"))

    def test_Esdegerlik_DislamaYalnizDesenEslesince(self):
        entries = [(r"^N\.F\.a$", "a3", "w"), (r"b\d", "alone", "w"), (r"^N\.G\.c(\(.*\))?$", "alone", "w")]
        rx = ci.core_filter(["F", "G"], entries)
        for name in ["N.F.a", "N.F.aa", "N.F.b1", "N.F.b", "N.G.c", "N.G.c(1)", "N.G.cc", "N.H.a", "N.G.x"]:
            base = bool(re.search(r"\.(F|G)\.", name))
            excluded = any(re.search(p, name) for p, _, _ in entries)
            self.assertEqual(bool(re.search(rx, name)), base and not excluded, name)
        self.assertEqual([s for s, _ in ci.alone_steps(entries)], ["playmode-alone-1-b", "playmode-alone-2-c"])

    def test_BozukSatirlar_Reddedilir(self):
        for text, why in [("^A\\.B$\teverywhere\tw\n", "is not one of"), ("^A\\.B$\ta3\n", "expected"),
                          ("^A\\.B$\ta3\t \n", "expected"), ("^A;B$\ta3\tw\n", "';'"), (".*\ta3\tw\n", "anything"),
                          ("D\talone\tw\n", "anything"), ("(\ta3\tw\n", "not a regex"), ("A B\ta3\tw\n", "whitespace"),
                          ("(?P<n>A)\ta3\tw\n", "Python-only")]:
            with self.assertRaises(ci.Malformed) as cm:
                ci.parse_isolated(self.write(text))
            self.assertIn(why, str(cm.exception), text)

    def test_Suit_AdOlmayanSatirReddedilir(self):
        with self.assertRaises(ci.Malformed):
            ci.read_suites(self.write("Good\nBad.Name\n"))
        with self.assertRaises(ci.Malformed):
            ci.read_suites(self.write("# only comments\n"))

    def test_Cli_CheckCoreAlone(self):
        iso = self.write("# c\n^N\\.F\\.a$\talone\tw\n^N\\.F\\.b$\ta3\tw\n")
        suites = self.write("F   # 1.0 s\n")
        run = lambda *a: subprocess.run([sys.executable, str(TOOLS / "ci_isolated.py"), *a], capture_output=True, text=True)
        self.assertEqual(run("check", iso).returncode, 0)
        r = run("core-filter", iso, suites)
        self.assertEqual(r.stdout.strip(), r"^(?!.*(?:^N\.F\.a$)|.*(?:^N\.F\.b$)).*\.(F)\.")
        self.assertEqual(run("alone", iso).stdout, "playmode-alone-1-a\t(?:^N\\.F\\.a$)\n")
        bad = self.write("x\tnowhere\tw\n")
        r = run("check", bad)
        self.assertEqual(r.returncode, 2)
        self.assertIn("ci-isolated:", r.stderr)
        self.assertEqual(run("bogus").returncode, 64)


if __name__ == "__main__":
    unittest.main(verbosity=2)
