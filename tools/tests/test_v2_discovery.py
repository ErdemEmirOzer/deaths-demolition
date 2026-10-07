#!/usr/bin/env python3
"""C-00 07.10: the guard v2 suite under discovery / `python3 -m unittest` is ONE skipped placeholder that names how to run it,
never 139 failures; run as a file it still runs (and only runs) sandboxed. usage: python3 tools/tests/test_v2_discovery.py"""
import os, subprocess, sys, unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent


class V2DiscoveryTests(unittest.TestCase):
    def run_py(self, *args):
        env = {k: v for k, v in os.environ.items() if k != "DD_GUARD_V2_SANDBOXED"}
        return subprocess.run([sys.executable, *args], cwd=HERE, env=dict(env, PYTHONDONTWRITEBYTECODE="1"),
                              capture_output=True, text=True, timeout=120)

    def test_ModulOlarakYukleme_TekAtlananYerTutucu_NasilKosulacagiYazar(self):
        r = self.run_py("-m", "unittest", "-v", "test_userdata_guard_v2")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn("Ran 1 test", r.stderr)
        self.assertIn("OK (skipped=1)", r.stderr)
        self.assertIn("python3 tools/tests/test_userdata_guard_v2.py", r.stderr)
        self.assertIn("sandbox-exec", r.stderr)

    def test_Discover_HerTestDegil_TekYerTutucu(self):
        r = self.run_py("-m", "unittest", "discover", "-s", str(HERE), "-p", "test_userdata_guard_v2.py")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn("Ran 1 test", r.stderr)

    def test_AdiyleCagrilanTest_HalaSetupUyarisindaDurur(self):
        """a test named explicitly is not routed through load_tests: it still stops at its own SANDBOX_FLAG assertion."""
        r = self.run_py("-m", "unittest", "test_userdata_guard_v2.LedgerTests.test_Ledger_IcIceAtaPenceresi_Cikis0")
        self.assertNotEqual(0, r.returncode)
        self.assertIn("run this file directly", r.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
