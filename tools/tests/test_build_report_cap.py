#!/usr/bin/env python3
"""WP-079G K7 · tools/build-report.sh cap check in bytes against Unity's BuildReport totalSize (DEC-093: 5 GB = 5 000 000 000 bytes).
Synthetic build dirs only (no Unity). usage: python3 tools/tests/test_build_report_cap.py"""
import json, subprocess, tempfile, unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / "build-report.sh"
LOG = """Build Report
Uncompressed usage by category (Percentages based on user generated assets only):
Textures               1.2 gb	 90.7%
Meshes                 102.0 mb	 7.2%
Total User Assets      1.4 gb	 100.0%
Complete build size    1.1 gb
Used Assets and files from the Resources folder, sorted by uncompressed size:
 21.3 mb	 1.9% Assets/_Art/Textures/Source/T_ap023_trim_Normal.png
 5.3 mb	 0.5% Assets/_Art/Scenes/World_V6_Greybox/Lightmap-0_comp_light.exr

"""


class BuildReportCapTests(unittest.TestCase):
    def build_dir(self, total_size=None, mode="release"):
        d = Path(self.tmp.name) / f"b{len(list(Path(self.tmp.name).iterdir()))}"
        d.mkdir()
        (d / "build.log").write_text(LOG)
        report = {"result": "Succeeded", "mode": mode, "compression": "lz4hc"}
        if total_size is not None:
            report["totalSize"] = total_size
        (d / "build-report.json").write_text(json.dumps(report, indent=2))
        return d

    def run_tool(self, d, *args):
        r = subprocess.run(["zsh", str(TOOL), str(d), *args], capture_output=True, text=True)
        return r, (json.loads((d / "footprint.json").read_text()) if (d / "footprint.json").exists() else None)

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-build-report-cap-")

    def tearDown(self):
        self.tmp.cleanup()

    def test_Kap_Varsayilan5e9Bayt_TotalSizeKapiDegeri(self):
        r, fp = self.run_tool(self.build_dir(5_000_000_000))
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual((fp["capBytes"], fp["gateBytes"], fp["gateSource"]), (5_000_000_000, 5_000_000_000, "buildReport.totalSize"))
        self.assertFalse(fp["capExceeded"])
        self.assertAlmostEqual(fp["capMB"], 4768.37, places=2)
        self.assertIn("gateBytes=5000000000 capBytes=5000000000 ok", r.stdout)

    def test_Kap_BirBaytFazla_UyariAmaCikis0(self):
        r, fp = self.run_tool(self.build_dir(5_000_000_001))
        self.assertEqual(r.returncode, 0)
        self.assertTrue(fp["capExceeded"])
        self.assertIn("WARN cap exceeded", r.stdout)

    def test_Kap_EskiMiBAyarlari_VeCapBytes_Gecerli(self):
        _, fp = self.run_tool(self.build_dir(5_300_000_000), "--cap", "5120")
        self.assertEqual(fp["capBytes"], 5120 * 1048576)
        self.assertFalse(fp["capExceeded"])
        _, fp = self.run_tool(self.build_dir(1_181_216_746), "--cap-bytes", "1181216745")
        self.assertTrue(fp["capExceeded"])
        r, _ = self.run_tool(self.build_dir(1), "--cap-bytes", "5e9")
        self.assertEqual(r.returncode, 64)

    def test_Kap_TotalSizeYoksa_UygulamaDiskBoyutu_DevdeUygulanmaz(self):
        d = self.build_dir(None)
        (d / "DeathsDemolition.app").mkdir()
        (d / "DeathsDemolition.app" / "x.bin").write_bytes(b"x" * 8192)
        _, fp = self.run_tool(d)
        self.assertEqual(fp["gateSource"], "app-du")
        self.assertGreater(fp["gateBytes"], 0)
        _, fp = self.run_tool(self.build_dir(6_000_000_000, mode="dev"))
        self.assertFalse(fp["capApplicable"])
        self.assertFalse(fp["capExceeded"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
