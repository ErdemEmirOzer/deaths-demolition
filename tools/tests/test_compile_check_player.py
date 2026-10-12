#!/usr/bin/env python3
"""WP-099 R4 (C-00 12.10) · tools/compile-check.sh --player: the argument contract and the player-log verdict (no Unity,
nothing taken). The Unity side — DdBuild.CompilePlayerScripts compiling, catching an unguarded dev-only file, the out-of-date
first pass after a file was added or deleted — is proven by real runs (K/wp-099/evidence/cc-player-1210/,
cc-player-2phase-1210/). usage: python3 tools/tests/test_compile_check_player.py"""
import subprocess, tempfile, unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / "compile-check.sh"
DD = "[DdBuild] player scripts mode=release-probe target=StandaloneOSX assemblies={n} out=/tmp/x — {v}"
TUNDRA = "*** Tundra requires additional run (0.46 seconds), 2 items updated, 377 evaluated"
RECOMPILE = "[ScriptCompilation] Requested script compilation because: Recompiling all scripts before compiling player scripts because Auto Refresh is disabled."
E_NEW = "Assets/_Prototype/Core/SpawnVisibility.cs(20,79): error CS0103: The name 'CcTwoPhaseProbe' does not exist in the current context"
E_GONE = "error CS2001: Source file '/ci/prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Core/CcTwoPhaseProbe.cs' could not be found."
E_QA = "Assets/_Prototype/Diagnostics/Qa/QaTreeBot.cs(38,58): error CS0103: The name 'QaArgs' does not exist in the current context"
COMPILED, FAILED = DD.format(n=27, v="compiled"), DD.format(n=0, v="FAILED (see the error CS lines above)")


class CompileCheckPlayerArgsTests(unittest.TestCase):
    def run_cc(self, *args):
        return subprocess.run(["zsh", str(TOOL), *args], capture_output=True, text=True, timeout=60)

    def test_GecersizMod_YaDaEksikDeger_64(self):
        for args in (["--player", "bogus"], ["--player"], ["--player", "Release"]):
            with self.subTest(args=args):
                r = self.run_cc(*args)
                self.assertEqual(64, r.returncode, r.stderr); self.assertIn("--player dev|release|release-probe", r.stderr)

    def test_LeaseStatusIlePlayerBirlikte_64(self):
        self.assertEqual(64, self.run_cc("--lease-status", "--player", "release").returncode)

    def test_PlayerLog_ModsuzRefliYaDaDosyasiz_64(self):
        with tempfile.TemporaryDirectory() as d:
            log = Path(d, "p.log"); log.write_text(COMPILED + "\n")
            for args in (["--player-log", str(log)], ["main", "--player", "release-probe", "--player-log", str(log)],
                         ["--player", "release-probe", "--player-log", str(Path(d, "yok.log"))],
                         ["--lease-status", "--player-log", str(log)]):
                with self.subTest(args=args):
                    self.assertEqual(64, self.run_cc(*args).returncode)

    def test_KaynakMetni_PlayerDerlemeSatiriOlmadanYesilYok(self):
        src = TOOL.read_text()
        self.assertIn("DdBuild.CompilePlayerScripts", src)
        self.assertIn("player $mode NOT checked", src, "a ref without the method is never a green")
        self.assertNotIn("[^\\r]", src, "grep reads [^\\r] as 'not \\ or r' and cuts every error message at its first r")

    def test_KaynakMetni_PlayerDerlemesiIkinciUnityOturumunda(self):
        # the Editor compile launches without the method; the player compile in its own launch with its own log, and its
        # verdict comes from cc_player_verdict on that log (Editor and player errors never mixed)
        src = TOOL.read_text()
        launches = [l for l in src.splitlines() if l.lstrip().startswith("dd_unity_watched ")]
        self.assertEqual(2, len(launches), launches)
        self.assertNotIn("-executeMethod", launches[0])
        i = src.index(launches[1]); tail = src[i:i + 300]
        self.assertIn("DdBuild.CompilePlayerScripts", tail)
        self.assertIn('-logFile "$PLOG"', tail)
        self.assertIn('PLOG="$OUT/compile-player-$PLAYER.log"', src)
        self.assertIn('cc_player_verdict "$PLAYER" "$PLOG" "$PX"', src)


class PlayerLogVerdictTests(unittest.TestCase):
    """C-00 12.10: the first player compile after the script set changed runs on the Library's out-of-date build graph and
    logs CS errors (a new file missing, a deleted one still listed), then "Tundra requires additional run" and the real pass.
    Real-log replays: K/wp-099/evidence/cc-player-2phase-1210/verdict-replay.txt."""

    def verdict(self, *lines):
        with tempfile.TemporaryDirectory() as d:
            log = Path(d, "compile-player-release-probe.log"); log.write_text("\n".join(lines) + "\n")
            return subprocess.run(["zsh", str(TOOL), "--player", "release-probe", "--player-log", str(log)],
                                  capture_output=True, text=True, timeout=60)

    def test_EskiGrafikIlkGecisHatasi_SonraDerlendi_Yesil(self):   # main 250b40aa / 0b60e5d9, scratch X1: a NEW file
        r = self.verdict(RECOMPILE, E_NEW, TUNDRA, "Starting: bee_backend …", COMPILED)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn("1 CS lines of the first pass on the out-of-date build graph ignored", r.stdout)
        self.assertIn("RESULT: log player release-probe compiles", r.stdout)

    def test_SilinenDosya_EskiGrafikCS2001_SonraDerlendi_Yesil(self):   # scratch X1 → main: a file and its reference deleted
        r = self.verdict(RECOMPILE, E_GONE, TUNDRA, E_GONE, TUNDRA, COMPILED)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn("2 CS lines of the first pass", r.stdout)

    def test_GercekHata_SonGecisteTekrarlar_Kirmizi(self):   # 12.10-8 negative: QaTreeBot's dev guard removed
        r = self.verdict(RECOMPILE, E_QA, TUNDRA, E_QA, E_QA, E_QA, FAILED)
        self.assertEqual(2, r.returncode, r.stdout + r.stderr)
        self.assertIn("QaTreeBot.cs(38,58): error CS0103", r.stdout)
        self.assertIn("does NOT compile (player release-probe) (1 errors;", r.stdout)

    def test_SilinenDosya_ReferansKaldi_AssetsOneksizHataBileKirmizi(self):   # the deleted-file false green, closed
        r = self.verdict(RECOMPILE, TUNDRA, E_GONE, FAILED)
        self.assertEqual(2, r.returncode, r.stdout + r.stderr)
        self.assertIn("error CS2001", r.stdout)

    def test_DerlendiDiyor_AmaSonGecisteHataVar_Celiskili3(self):
        for err in (E_NEW, E_GONE):   # an error without the Assets/ prefix (CS2001) counts as much as one with it
            with self.subTest(err=err[:30]):
                r = self.verdict(RECOMPILE, TUNDRA, err, COMPILED)
                self.assertEqual(3, r.returncode, r.stdout + r.stderr)
                self.assertIn("contradictory, tell C-00", r.stdout)

    def test_TundraSatiriYok_TemizDerleme_Yesil_HataliDerleme_Kirmizi(self):
        self.assertEqual(0, self.verdict(RECOMPILE, COMPILED).returncode)
        self.assertEqual(2, self.verdict(RECOMPILE, E_QA, FAILED).returncode)
        self.assertEqual(3, self.verdict(RECOMPILE, E_QA, COMPILED).returncode)

    def test_DdBuildSatiriYok_Kontrol_Edilmedi3(self):
        r = self.verdict(RECOMPILE, TUNDRA)
        self.assertEqual(3, r.returncode); self.assertIn("NOT checked", r.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)
