#!/usr/bin/env python3
"""WP-112 (C-00 12.10) · tools/app-display-name.sh + its call in tools/build.sh. A fake bundle (a copy of /usr/bin/true named
DDFixtureApp — no real guard's Player pattern matches it), ad-hoc signed like Unity's player; no Unity, no Player.
The naming itself (Dock, menu bar and window title follow CFBundleName) is measured: K/wp-112/evidence/s01-appname/.
usage: python3 tools/tests/test_app_display_name.py"""
import plistlib, shutil, subprocess, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
TOOL = TOOLS / "app-display-name.sh"
BUNDLE_ID = "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"


def fake_app(root):
    app = Path(root, "DeathsDemolition.app"); (app / "Contents" / "MacOS").mkdir(parents=True)
    shutil.copy("/usr/bin/true", app / "Contents" / "MacOS" / "DDFixtureApp")
    plist = {"CFBundleName": "Deaths Demolition - Playable Concept", "CFBundleIdentifier": BUNDLE_ID,
             "CFBundleExecutable": "DDFixtureApp", "CFBundlePackageType": "APPL", "CFBundleShortVersionString": "1.0"}
    (app / "Contents" / "Info.plist").write_bytes(plistlib.dumps(plist))
    subprocess.run(["codesign", "--force", "--sign", "-", str(app)], check=True, capture_output=True)
    return app


def verify(app):
    return subprocess.run(["codesign", "--verify", "--deep", "--strict", str(app)], capture_output=True).returncode


class AppDisplayNameTests(unittest.TestCase):
    def test_IkiAdDegisir_KimlikVeCalistirilabilirKalir_ImzaGecerli(self):
        with tempfile.TemporaryDirectory() as d:
            app = fake_app(d)
            r = subprocess.run(["zsh", str(TOOL), str(app), "Deaths Demolition"], capture_output=True, text=True, timeout=60)
            self.assertEqual(0, r.returncode, r.stdout + r.stderr)
            p = plistlib.loads((app / "Contents" / "Info.plist").read_bytes())
            self.assertEqual("Deaths Demolition", p["CFBundleName"]); self.assertEqual("Deaths Demolition", p["CFBundleDisplayName"])
            self.assertEqual(BUNDLE_ID, p["CFBundleIdentifier"], "the bundle id keys the user's PlayerPrefs plist")
            self.assertEqual("DDFixtureApp", p["CFBundleExecutable"], "the executable (productName) stays")
            self.assertEqual(0, verify(app), "re-signed: the Info.plist is part of the ad-hoc seal")
            # idempotent: a second run (CFBundleDisplayName already there) is fine
            self.assertEqual(0, subprocess.run(["zsh", str(TOOL), str(app), "Deaths Demolition"], capture_output=True).returncode)

    def test_ImzasizPlistDegisikligi_DogrulamayiBozar(self):   # why the tool re-signs (positive control)
        with tempfile.TemporaryDirectory() as d:
            app = fake_app(d); pl = app / "Contents" / "Info.plist"
            p = plistlib.loads(pl.read_bytes()); p["CFBundleName"] = "Deaths Demolition"; pl.write_bytes(plistlib.dumps(p))
            self.assertNotEqual(0, verify(app))

    def test_Kullanim_64(self):
        with tempfile.TemporaryDirectory() as d:
            app = fake_app(d)
            for args in ([], [str(app)], [str(app), ""], [d, "Deaths Demolition"]):
                with self.subTest(args=args):
                    self.assertEqual(64, subprocess.run(["zsh", str(TOOL), *args], capture_output=True).returncode)

    def test_BuildSh_YalnizMacReleaseManifesttenOnce(self):
        src = (TOOLS / "build.sh").read_text()
        i = src.index('app-display-name.sh"')
        self.assertIn('[ "$MODE" = release ] && [ "$TARGET" = mac ]', src[i - 300:i])
        self.assertLess(i, src.index('app_manifest.py" write'), "named before the manifest is written")
        self.assertIn("BUILD_OK=0", src[i:i + 300], "a failed rename is no release")
        self.assertIn('"displayName":', src)


if __name__ == "__main__":
    unittest.main(verbosity=2)
