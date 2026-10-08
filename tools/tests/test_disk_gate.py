#!/usr/bin/env python3
"""C-00 30.09 · dd_disk_gate (tools/dd-env.sh): the shared Unity/Player resource gate — free disk ≥ 15 GiB and used swap
≤ 12 GiB (12–16 GiB while memory_pressure shows ≥ 25 % free, WP-099 08.10), else 75 + one reason line; wired into
compile-check, test.sh, build.sh and player-run.sh. Fake df / sysctl / memory_pressure on PATH (no real machine state). usage: python3 tools/tests/test_disk_gate.py"""
import subprocess, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]


class DiskGateTests(unittest.TestCase):
    def gate(self, free_kib, swap_used, path="/", mem_free="10"):
        with tempfile.TemporaryDirectory(prefix="dd-disk-gate-") as tmp:
            fake = Path(tmp); self.asked = False
            (fake / "df").write_text(f"#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho '/dev/x 999 1 {free_kib}'\n")
            (fake / "sysctl").write_text(f"#!/bin/sh\necho 'total = 16384.00M  used = {swap_used}  free = 1.00M  (encrypted)'\n")
            # WP-099 (C-00 08.10): memory_pressure -Q's free %; "missing" = no answer (unreadable). Default 10 % (low).
            mark = f": > '{fake}/memory_pressure-asked'\n"   # gt SHOULD-1 (08.10): the test sees whether the gate asked
            (fake / "memory_pressure").write_text("#!/bin/sh\n" + mark + ("exit 1\n" if mem_free == "missing" else
                                                  "echo 'The system has 17179869184 (1048576 pages with a page size of 16384).'\n"
                                                  f"echo 'System-wide memory free percentage: {mem_free}%'\n"))
            for f in ("df", "sysctl", "memory_pressure"):
                (fake / f).chmod(0o755)
            r = subprocess.run(["zsh", "-c", f'PATH="{fake}:$PATH"; DD_TIMELINE=""; source "$1"; dd_disk_gate "$2"', "zsh",
                                str(TOOLS / "dd-env.sh"), path], capture_output=True, text=True, timeout=30)
            self.asked = (fake / "memory_pressure-asked").exists()
            return r.returncode, r.stdout + r.stderr

    def test_DiskKapisi_YeterliDiskVeSwap_0(self):
        rc, out = self.gate(15728640, "12288.00M")
        self.assertEqual(rc, 0, out)
        self.assertIn("disk-gate: ok freeKiB=15728640 swapUsedMB=12288.00", out)

    def test_DiskKapisi_Swap10GiBUstu12Alti_UyariSatiri_YineBaslar0(self):
        """C-00 06.10: an early WARNING at ≥ 10 GiB used swap, written by dd_log (stdout + timeline); never a stop below 12."""
        for swap, warn in (("10239.99M", False), ("10240.00M", True), ("12288.00M", True)):
            with self.subTest(swap=swap):
                rc, out = self.gate(99999999, swap)
                self.assertEqual(rc, 0, out)
                self.assertEqual(warn, "WARNING disk-gate: used swap %s MB ≥ 10240 (10 GiB)" % swap[:-1] in out, out)
                self.assertIn("disk-gate: ok freeKiB=99999999", out)

    def test_DiskKapisi_15GiBAlti_75TekSatirNeden(self):
        rc, out = self.gate(15728639, "100.00M")
        self.assertEqual(rc, 75, out)
        self.assertIn("free disk 15728639 KiB < 15728640 (15 GiB)", out)

    def test_DiskKapisi_Swap12GiBUstu_75(self):
        rc, out = self.gate(99999999, "12289.50M")
        self.assertEqual(rc, 75, out)
        self.assertIn("used swap 12289.50 MB > 12288 (12 GiB) and memory free 10% < 25%", out)

    def test_DiskKapisi_N8_SwapOndaligi_Sayilir(self):
        """C-00 06.10 (N-8): 12288.50M is over the 12 GiB stop — it was read as 12288 and passed; the decimals count both
        ways (12288.00 / 10239.99 stay below their thresholds, a value without decimals reads as is)."""
        for swap, rc_want in (("12288.01M", 75), ("12288.5M", 75), ("12288.00M", 0), ("12288M", 0), ("12289M", 75),
                              ("0.00M", 0), ("10239.99M", 0)):
            with self.subTest(swap=swap):
                rc, out = self.gate(99999999, swap)
                self.assertEqual(rc_want, rc, out)
        rc, out = self.gate(99999999, "10240.01M")
        self.assertIn("WARNING disk-gate: used swap 10240.01 MB", out)

    def test_SwapKapisi_12Ustu16Alti_BellekBossa_UyariylaBaslar(self):
        """WP-099 (C-00 08.10): used swap alone stopped launches while memory was plentiful — above 12 GiB the gate looks at live memory:
        ≥ 25 % free → a WARNING line with both values, started; < 25 % or no answer → 75 with both values."""
        rc, out = self.gate(99999999, "13000.00M", mem_free="63")
        self.assertEqual(0, rc, out)
        self.assertIn("WARNING disk-gate: used swap 13000.00 MB > 12288 (12 GiB) but memory free 63% ≥ 25% — swap used above 12 GiB, live memory free ≥ 25 %; started anyway", out)
        self.assertIn("disk-gate: ok freeKiB=99999999 swapUsedMB=13000.00 memFreePct=63", out)
        for mem, rc_want in (("25", 0), ("24", 75), ("missing", 75)):
            with self.subTest(mem=mem):
                rc, out = self.gate(99999999, "13000.00M", mem_free=mem)
                self.assertEqual(rc_want, rc, out)
        rc, out = self.gate(99999999, "13000.00M", mem_free="24")
        self.assertIn("used swap 13000.00 MB > 12288 (12 GiB) and memory free 24% < 25% — not started (75)", out)
        rc, out = self.gate(99999999, "13000.00M", mem_free="missing")
        self.assertIn("memory free unknown% < 25%", out)

    def test_SwapKapisi_16GiBUstu_HerDurumda75(self):
        for swap, rc_want in (("16384.00M", 0), ("16384.01M", 75), ("20000.00M", 75)):
            with self.subTest(swap=swap):
                rc, out = self.gate(99999999, swap, mem_free="90")
                self.assertEqual(rc_want, rc, out)
        rc, out = self.gate(99999999, "16384.01M", mem_free="90")
        self.assertIn("used swap 16384.01 MB > 16384 (16 GiB), memory free 90% — not started (75)", out)

    def test_SwapKapisi_12AltindaBellekOkunmaz(self):
        """below 12 GiB the gate never asks memory_pressure — a missing tool changes nothing there."""
        for swap in ("12288.00M", "100.00M"):
            with self.subTest(swap=swap):
                rc, out = self.gate(99999999, swap, mem_free="missing")
                self.assertEqual(0, rc, out); self.assertNotIn("memFreePct", out); self.assertFalse(self.asked)
        self.gate(99999999, "12288.01M", mem_free="63"); self.assertTrue(self.asked)
        self.gate(99999999, "16384.01M", mem_free="63"); self.assertTrue(self.asked)   # C-00 08.10: its line carries both values

    def test_DiskKapisi_OkunamayanDegerler_75(self):
        rc, out = self.gate("n/a", "100.00M")
        self.assertEqual(rc, 75, out)
        rc, out = self.gate(99999999, "garbage", mem_free="44")
        self.assertEqual(rc, 75, out); self.assertIn("used swap unknown, memory free 44% — not started (75)", out)

    def test_DiskKapisi_TumGirisNoktalarindaTekKaynak(self):
        for name in ("test.sh", "build.sh", "player-run.sh", "compile-check.sh", "slot.sh"):
            with self.subTest(script=name):
                self.assertIn("dd_disk_gate", (TOOLS / name).read_text())
        self.assertNotIn("15728640", (TOOLS / "compile-check.sh").read_text(), "threshold lives only in dd-env.sh")


if __name__ == "__main__":
    unittest.main(verbosity=2)
