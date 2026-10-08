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
CODESIGN = ('#!/bin/sh\ncase "$1" in -dv) echo "Signature=adhoc" >&2; exit 0;; esac\n'
            'for a in "$@"; do case "$a" in *.tmp.*) [ -n "${FAKE_SIGN_FAIL_COPY:-}" ] && exit 1;; esac; done\n'
            'exit ${FAKE_SIGN_RC:-0}\n')


class ReleasePublishTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-release-publish-")
        root = Path(self.tmp.name)
        self.root = root
        self.src, self.dest_root, self.bin = root / "build", root / "DD-BUILDS", root / "bin"
        app = self.src / "DeathsDemolition.app" / "Contents" / "MacOS"
        app.mkdir(parents=True)
        (app / "Deaths Demolition - Playable Concept").write_text("player")
        (self.src / "DeathsDemolition_BurstDebugInformation_DoNotShip").mkdir()
        (self.src / "userdata-backup").mkdir(); (self.src / "profile-sandbox").mkdir()   # smoke leftovers: never shipped
        (self.src / "e1-mainmenu-1280x720.png").write_bytes(b"png")
        for n in ("build-report.json", "build.log", "footprint.md", "timeline.txt", "smoke-report.json"):
            (self.src / n).write_text("x")
        subprocess.run(["python3", str(TOOLS / "app_manifest.py"), "write", str(self.src), "DeathsDemolition.app"],
                       check=True, capture_output=True)
        self.record()
        self.dest_root.mkdir()
        self.bin.mkdir(); (self.bin / "codesign").write_text(CODESIGN); (self.bin / "codesign").chmod(0o755)

    def tearDown(self):
        self.tmp.cleanup()

    def record(self, smoke="ok", errors=0, gate=1_227_990_323, mode="release", ok=True, head="313595996d888d6f", raw=None):
        import hashlib
        sha = hashlib.sha256((self.src / "app-manifest.sha256").read_bytes()).hexdigest()
        (self.src / "report.json").write_text(raw if raw is not None else json.dumps(
            {"mode": mode, "ok": ok, "head": head, "appManifestSha256": sha, "build": {"errorsCS": errors, "stamp": "wp-099-x"},
             "smoke": {"status": smoke}}))
        (self.src / "footprint.json").write_text(json.dumps({"gateBytes": gate, "capBytes": 5_000_000_000}))

    def run_pub(self, *extra, root=None, **env):
        e = dict(os.environ, PATH="%s:%s" % (self.bin, os.environ["PATH"]), **env)
        return subprocess.run(["zsh", str(TOOLS / "release-publish.sh"), str(self.src), "--dest-root", str(root or self.dest_root),
                               *extra], capture_output=True, text=True, env=e, timeout=120)

    def fresh_root(self, name):
        r = self.root / name; r.mkdir(); return r

    def test_HepsiTamam_KopyalarDogrularSmokeArtigiYok(self):
        r = self.run_pub()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        d = self.dest_root / "31359599-mac"
        self.assertTrue((d / "DeathsDemolition.app" / "Contents" / "MacOS" / "Deaths Demolition - Playable Concept").is_file())
        self.assertTrue((d / "e1-mainmenu-1280x720.png").is_file()); self.assertTrue((d / "report.json").is_file())
        self.assertFalse((d / "userdata-backup").exists()); self.assertFalse((d / "profile-sandbox").exists())
        self.assertIn("app-manifest ok", (d / "publish-verify.txt").read_text()); self.assertIn("codesign ok", (d / "publish-verify.txt").read_text())
        self.assertIn("signature adhoc", r.stdout); self.assertIn("stamp wp-099-x", r.stdout)
        self.assertEqual([], list(self.dest_root.glob("*.tmp.*")))

    def test_HedefVar_Cikis64_UzerineYazmaz(self):
        self.assertEqual(0, self.run_pub().returncode)
        r = self.run_pub()
        self.assertEqual(64, r.returncode, r.stdout + r.stderr); self.assertIn("never overwritten", r.stderr)

    def test_KuruKosu_HicbirSeyKopyalanmaz(self):
        r = self.run_pub("--dry-run")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr); self.assertEqual([], list(self.dest_root.iterdir()))

    def test_KontrolBasarisiz_Cikis1_HicbirSeyKopyalanmaz(self):
        cases = {"smoke status=failed": dict(smoke="failed"), "errorsCS=2": dict(errors=2),
                 "gateBytes=6000000000": dict(gate=6_000_000_000), "mode=dev": dict(mode="dev"), "ok=False": dict(ok=False),
                 # gt MUST-1 (08.10): a record the check cannot even read refuses — it used to publish to "<root>/-mac"
                 "report.json is not an object": dict(raw="[]"), "build is not an object": dict(raw=json.dumps(
                     {"mode": "release", "ok": True, "head": "313595996d888d6f", "build": "x", "smoke": {"status": "ok"}})),
                 "is not a git sha": dict(head="xx/../../evil"), "report.json unreadable": dict(raw="{not json")}
        for n, (why, rec) in enumerate(cases.items()):
            with self.subTest(why=why):
                self.record(**rec); root = self.fresh_root("r%d" % n)
                r = self.run_pub(root=root)
                self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn(why, r.stderr)
                self.assertEqual([], list(root.iterdir()))
        self.record()

    def test_ManifestYenidenYazildi_RaporlaUyusmaz_Cikis1(self):
        """gt S-1 (08.10): the .app changed and the manifest rewritten after build.sh — report.json's sha no longer matches."""
        exe = self.src / "DeathsDemolition.app" / "Contents" / "MacOS" / "Deaths Demolition - Playable Concept"
        exe.write_text("changed")
        subprocess.run(["python3", str(TOOLS / "app_manifest.py"), "write", str(self.src), "DeathsDemolition.app"],
                       check=True, capture_output=True)
        r = self.run_pub()
        self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn("!= report.json appManifestSha256", r.stderr)

    def test_HedefKokYok_Cikis64_Yaratmaz(self):
        r = self.run_pub(root=self.root / "nope")
        self.assertEqual(64, r.returncode, r.stdout + r.stderr); self.assertFalse((self.root / "nope").exists())

    def test_KopyaninImzasiTutmaz_Cikis2_HedefYok(self):
        """gt S-5 (08.10): the copy itself is signature-checked before it is named."""
        r = self.run_pub(FAKE_SIGN_FAIL_COPY="1")
        self.assertEqual(2, r.returncode, r.stdout + r.stderr); self.assertFalse((self.dest_root / "31359599-mac").exists())

    def test_ManifestUyusmaz_Cikis1(self):
        (self.src / "DeathsDemolition.app" / "Contents" / "MacOS" / "Deaths Demolition - Playable Concept").write_text("changed")
        r = self.run_pub()
        self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn("app_manifest.py verify", r.stderr)

    def test_ImzaDogrulanmaz_Cikis1(self):
        r = self.run_pub(FAKE_SIGN_RC="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn("codesign --verify", r.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
