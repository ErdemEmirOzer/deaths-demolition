#!/usr/bin/env python3
"""Codex-audit M-02(e) (C-00 30.09) · tools/userdata-guard.sh argv is exact: `snapshot|restore <dir>` or `… <dir> --scope <s>`.
Every other shape exits 64 before anything is touched (the <dir> is not even created). Only malformed argv is run here —
a well-formed call would take the real snapshot. usage: python3 tools/tests/test_userdata_guard_argv.py"""
import subprocess, tempfile, unittest
from pathlib import Path

GUARD = Path(__file__).resolve().parents[1] / "userdata-guard.sh"


class GuardArgvTests(unittest.TestCase):
    def run_guard(self, *args):
        with tempfile.TemporaryDirectory(prefix="dd-guard-argv-") as tmp:
            target = Path(tmp) / "ug"
            argv = [a.replace("{DIR}", str(target)) for a in args]
            r = subprocess.run(["zsh", str(GUARD), *argv], capture_output=True, text=True, timeout=30)
            return r.returncode, r.stderr, target.exists()

    def assertUsage(self, *args):
        code, err, created = self.run_guard(*args)
        self.assertEqual(code, 64, f"{args}: {err}")
        self.assertIn("usage: userdata-guard.sh", err)
        self.assertFalse(created, f"{args}: <dir> must not be created on a usage error")

    def test_Argv_EsittirliScope_64_HicbirSeyeDokunmaz(self):
        self.assertUsage("snapshot", "{DIR}", "--scope=player")
        self.assertUsage("restore", "{DIR}", "--scope=editor")

    def test_Argv_FazladanVeyaEksikArguman_64(self):
        self.assertUsage("snapshot", "{DIR}", "--scope", "player", "extra")
        self.assertUsage("restore", "{DIR}", "player")
        self.assertUsage("snapshot", "{DIR}", "--scope")
        self.assertUsage("snapshot")
        self.assertUsage()

    def test_Argv_YanlisKomutVeyaKapsam_64(self):
        self.assertUsage("backup", "{DIR}")
        self.assertUsage("snapshot", "{DIR}", "--scope", "everything")
        self.assertUsage("snapshot", "{DIR}", "--scop", "player")


if __name__ == "__main__":
    unittest.main(verbosity=2)
