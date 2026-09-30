#!/usr/bin/env python3
"""C-00 30.09 · dd_disk_gate (tools/dd-env.sh): the shared Unity/Player resource gate — free disk ≥ 15 GiB and used swap
≤ 12 GiB, else 75 + one reason line; wired into compile-check, test.sh, build.sh and player-run.sh.
Fake df / sysctl on PATH (no real disk state involved). usage: python3 tools/tests/test_disk_gate.py"""
import subprocess, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]


class DiskGateTests(unittest.TestCase):
    def gate(self, free_kib, swap_used, path="/"):
        with tempfile.TemporaryDirectory(prefix="dd-disk-gate-") as tmp:
            fake = Path(tmp)
            (fake / "df").write_text(f"#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho '/dev/x 999 1 {free_kib}'\n")
            (fake / "sysctl").write_text(f"#!/bin/sh\necho 'total = 16384.00M  used = {swap_used}  free = 1.00M  (encrypted)'\n")
            for f in ("df", "sysctl"):
                (fake / f).chmod(0o755)
            r = subprocess.run(["zsh", "-c", f'PATH="{fake}:$PATH"; DD_TIMELINE=""; source "$1"; dd_disk_gate "$2"', "zsh",
                                str(TOOLS / "dd-env.sh"), path], capture_output=True, text=True, timeout=30)
            return r.returncode, r.stdout + r.stderr

    def test_DiskKapisi_YeterliDiskVeSwap_0(self):
        rc, out = self.gate(15728640, "12288.00M")
        self.assertEqual(rc, 0, out)
        self.assertIn("disk-gate: ok freeKiB=15728640 swapUsedMB=12288", out)

    def test_DiskKapisi_15GiBAlti_75TekSatirNeden(self):
        rc, out = self.gate(15728639, "100.00M")
        self.assertEqual(rc, 75, out)
        self.assertIn("free disk 15728639 KiB < 15728640 (15 GiB)", out)

    def test_DiskKapisi_Swap12GiBUstu_75(self):
        rc, out = self.gate(99999999, "12289.50M")
        self.assertEqual(rc, 75, out)
        self.assertIn("used swap 12289 MB > 12288 (12 GiB)", out)

    def test_DiskKapisi_OkunamayanDegerler_75(self):
        rc, out = self.gate("n/a", "100.00M")
        self.assertEqual(rc, 75, out)
        rc, out = self.gate(99999999, "garbage")
        self.assertEqual(rc, 75, out)

    def test_DiskKapisi_TumGirisNoktalarindaTekKaynak(self):
        for name in ("test.sh", "build.sh", "player-run.sh", "compile-check.sh", "slot.sh"):
            with self.subTest(script=name):
                self.assertIn("dd_disk_gate", (TOOLS / name).read_text())
        self.assertNotIn("15728640", (TOOLS / "compile-check.sh").read_text(), "threshold lives only in dd-env.sh")


if __name__ == "__main__":
    unittest.main(verbosity=2)
