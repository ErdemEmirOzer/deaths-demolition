#!/usr/bin/env python3
"""WP-099 (S-01, C-00 08.10) · tools/release-publish.sh on a fake release build dir: the real app_manifest.py, a codesign
shim on PATH (answers --verify by FAKE_SIGN_RC, -dv with "Signature=adhoc"). Nothing outside a temp dir is touched.
usage: python3 tools/tests/test_release_publish.py"""
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
CODESIGN = '#!/bin/sh\ncase "$1" in -dv) echo "Signature=adhoc" >&2; exit 0;; esac\nexit ${FAKE_SIGN_RC:-0}\n'


class ReleasePublishTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-release-publish-")
        root = Path(self.tmp.name)
        self.src, self.dest_root, self.bin = root / "build", root / "DD-BUILDS", root / "bin"
        app = self.src / "DeathsDemolition.app" / "Contents" / "MacOS"
        app.mkdir(parents=True)
        (app / "Deaths Demolition - Playable Concept").write_text("player")
        (self.src / "DeathsDemolition_BurstDebugInformation_DoNotShip").mkdir()
        (self.src / "userdata-backup").mkdir(); (self.src / "profile-sandbox").mkdir()   # smoke leftovers: never shipped
        (self.src / "e1-mainmenu-1280x720.png").write_bytes(b"png")
        for n in ("build-report.json", "build.log", "footprint.md", "timeline.txt", "smoke-report.json"):
            (self.src / n).write_text("x")
        self.record()
        subprocess.run(["python3", str(TOOLS / "app_manifest.py"), "write", str(self.src), "DeathsDemolition.app"],
                       check=True, capture_output=True)
        self.bin.mkdir(); (self.bin / "codesign").write_text(CODESIGN); (self.bin / "codesign").chmod(0o755)

    def tearDown(self):
        self.tmp.cleanup()

    def record(self, smoke="ok", errors=0, gate=1_227_990_323, mode="release"):
        (self.src / "report.json").write_text(json.dumps({"mode": mode, "ok": True, "head": "313595996d888d6f",
                                                          "build": {"errorsCS": errors}, "smoke": {"status": smoke}}))
        (self.src / "footprint.json").write_text(json.dumps({"gateBytes": gate, "capBytes": 5_000_000_000}))

    def run_pub(self, *extra, **env):
        e = dict(os.environ, PATH="%s:%s" % (self.bin, os.environ["PATH"]), **env)
        return subprocess.run(["zsh", str(TOOLS / "release-publish.sh"), str(self.src), "--dest-root", str(self.dest_root),
                               *extra], capture_output=True, text=True, env=e, timeout=120)

    def test_HepsiTamam_KopyalarDogrularSmokeArtigiYok(self):
        r = self.run_pub()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        d = self.dest_root / "31359599-mac"
        self.assertTrue((d / "DeathsDemolition.app" / "Contents" / "MacOS" / "Deaths Demolition - Playable Concept").is_file())
        self.assertTrue((d / "e1-mainmenu-1280x720.png").is_file()); self.assertTrue((d / "report.json").is_file())
        self.assertFalse((d / "userdata-backup").exists()); self.assertFalse((d / "profile-sandbox").exists())
        self.assertIn("app-manifest ok", (d / "publish-verify.txt").read_text())
        self.assertIn("signature adhoc", r.stdout)

    def test_HedefVar_Cikis64_UzerineYazmaz(self):
        self.assertEqual(0, self.run_pub().returncode)
        r = self.run_pub()
        self.assertEqual(64, r.returncode, r.stdout + r.stderr); self.assertIn("never overwritten", r.stderr)

    def test_KuruKosu_HicbirSeyKopyalanmaz(self):
        r = self.run_pub("--dry-run")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr); self.assertFalse(self.dest_root.exists())

    def test_KontrolBasarisiz_Cikis1_HicbirSeyKopyalanmaz(self):
        cases = {"smoke status=failed": dict(smoke="failed"), "errorsCS=2": dict(errors=2),
                 "gateBytes=6000000000": dict(gate=6_000_000_000), "mode=dev": dict(mode="dev")}
        for why, rec in cases.items():
            with self.subTest(why=why):
                self.record(**rec)
                r = self.run_pub()
                self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn(why, r.stderr)
                self.assertFalse(self.dest_root.exists())
        self.record()

    def test_ManifestUyusmaz_Cikis1(self):
        (self.src / "DeathsDemolition.app" / "Contents" / "MacOS" / "Deaths Demolition - Playable Concept").write_text("changed")
        r = self.run_pub()
        self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn("app_manifest.py verify", r.stderr)

    def test_ImzaDogrulanmaz_Cikis1(self):
        r = self.run_pub(FAKE_SIGN_RC="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn("codesign --verify", r.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
