#!/usr/bin/env python3
"""WP-099 R4 (C-00 12.10) · tools/compile-check.sh --player: the argument contract (no Unity, nothing taken). The Unity side —
DdBuild.CompilePlayerScripts compiling and catching an unguarded dev-only file — is proven by real runs
(K/wp-099/evidence/cc-player-1210/). usage: python3 tools/tests/test_compile_check_player.py"""
import subprocess, unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / "compile-check.sh"


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

    def test_KaynakMetni_PlayerDerlemeSatiriOlmadanYesilYok(self):
        src = TOOL.read_text()
        self.assertIn("DdBuild.CompilePlayerScripts", src)
        self.assertIn("player $PLAYER NOT checked", src, "a ref without the method is never a green")


if __name__ == "__main__":
    unittest.main(verbosity=2)
