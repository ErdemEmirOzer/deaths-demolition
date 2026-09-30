#!/usr/bin/env python3
"""WP-079H · tools/app_manifest.py on a synthetic player tree (no Unity, no build). usage: python3 tools/tests/test_app_manifest.py"""
import hashlib, os, subprocess, sys, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
TOOL = TOOLS / "app_manifest.py"
APP = "DeathsDemolition.app"


def run(*args, cwd=None):
    return subprocess.run([sys.executable, str(TOOL), *args], capture_output=True, text=True, cwd=cwd)


class AppManifestTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-app-manifest-")
        self.out = Path(self.tmp.name)
        data = self.out / APP / "Contents" / "Resources" / "Data"
        data.mkdir(parents=True)
        (self.out / APP / "Contents" / "Info.plist").write_bytes(b"<plist/>")
        (data / "data.unity3d").write_bytes(os.urandom(4096))
        (data / "boot config").write_text("gfx-enable-gfx-jobs=1\n")   # a space in the name, as Unity ships
        self.man = self.out / "app-manifest.sha256"

    def tearDown(self):
        self.tmp.cleanup()

    def write(self):
        r = run("write", str(self.out), APP)
        self.assertEqual(r.returncode, 0, r.stderr)
        return r.stdout.strip()

    def test_Yaz_ManifestShasumBicimi_SiraliGoreliYollar_OzetDogru(self):
        digest = self.write()
        text = self.man.read_text()
        self.assertEqual(digest, hashlib.sha256(text.encode()).hexdigest())
        rels = [line.split("  ", 1)[1] for line in text.splitlines()]
        self.assertEqual(rels, sorted(rels))
        self.assertEqual(len(rels), 3)
        self.assertTrue(all(r.startswith(APP + "/") for r in rels))

    def test_Dogrula_DegismemisKopya_HemAracHemShasumGecer(self):
        self.write()
        self.assertEqual(run("verify", str(self.out)).returncode, 0)
        sh = subprocess.run(["shasum", "-a", "256", "-c", "app-manifest.sha256"], cwd=self.out, capture_output=True, text=True)
        self.assertEqual(sh.returncode, 0, sh.stdout + sh.stderr)

    def test_Dogrula_DegisenDosya_Kirmizi(self):
        self.write()
        (self.out / APP / "Contents" / "Info.plist").write_bytes(b"<plist>tampered</plist>")
        r = run("verify", str(self.out))
        self.assertEqual(r.returncode, 1)
        self.assertIn("MISMATCH " + APP + "/Contents/Info.plist", r.stdout)

    def test_Dogrula_EksikVeFazlaDosya_Kirmizi(self):
        self.write()
        (self.out / APP / "Contents" / "Resources" / "Data" / "boot config").unlink()
        (self.out / APP / "Contents" / "extra.bin").write_bytes(b"x")
        r = run("verify", str(self.out))
        self.assertEqual(r.returncode, 1)
        self.assertIn("MISMATCH", r.stdout)
        self.assertIn("EXTRA " + APP + "/Contents/extra.bin", r.stdout)

    def test_Yaz_SembolikBag_HedefIcerigiyleOzetlenir_KopukBagHata(self):
        target = self.out / APP / "Contents" / "Info.plist"
        (self.out / APP / "Contents" / "link.plist").symlink_to(target)
        self.write()
        line = [l for l in self.man.read_text().splitlines() if l.endswith("/link.plist")][0]
        self.assertEqual(line.split("  ")[0], hashlib.sha256(target.read_bytes()).hexdigest())
        (self.out / APP / "Contents" / "dangling").symlink_to(self.out / "nowhere")
        self.assertEqual(run("write", str(self.out), APP).returncode, 2)

    def test_Yaz_ShasumunTasiyamadigiAd_VeUygulamaYok_Hata(self):
        (self.out / APP / "Contents" / "bad\\name").write_bytes(b"x")
        self.assertEqual(run("write", str(self.out), APP).returncode, 2)
        self.assertEqual(run("write", str(self.out), "Missing.app").returncode, 2)
        self.assertEqual(run("write").returncode, 64)

    def test_BuildSh_ManifestiSmoketanOnceYazar_RaporaOzetiKoyar(self):
        text = (TOOLS / "build.sh").read_text()
        self.assertIn('app_manifest.py" write "$OUT"', text)
        self.assertIn('"appManifestSha256":', text)
        self.assertLess(text.index("app_manifest.py"), text.index('smoke.sh" "$APP"'))


if __name__ == "__main__":
    unittest.main(verbosity=2)
