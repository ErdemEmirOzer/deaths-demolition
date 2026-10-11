#!/usr/bin/env python3
"""C-00 12.10 (WP-104 B1a, profile v6) · tools/player-run.sh gives every Player launch a profile sandbox: without
-ddProfilePath the wrapper adds a fresh one; a caller's own path is kept as it is. A fake Player binary records its argv;
fake df / sysctl on PATH pass the disk gate; no slot.sh in the fixture (dd_slot_acquire skips). usage: python3 tools/tests/test_player_run_sandbox.py"""
import json, os, shutil, subprocess, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
PRODUCT = "Deaths Demolition - Playable Concept"


class PlayerRunSandboxTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-player-run-")
        root = Path(self.tmp.name).resolve(); self.root = root
        tools = root / "repo" / "tools"; tools.mkdir(parents=True)
        for name in ("player-run.sh", "dd-env.sh"):
            shutil.copy2(TOOLS / name, tools / name)
        self.script = tools / "player-run.sh"
        self.app = root / "DeathsDemolition.app"; macos = self.app / "Contents" / "MacOS"; macos.mkdir(parents=True)
        self.argv = root / "argv.json"
        player = macos / PRODUCT
        player.write_text("#!/usr/bin/env python3\nimport json, sys\njson.dump(sys.argv[1:], open(%r, 'w'))\n" % str(self.argv))
        player.chmod(0o755)
        fake = root / "fakebin"; fake.mkdir()
        (fake / "df").write_text("#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho '/dev/x 999 1 99999999'\n")
        (fake / "sysctl").write_text("#!/bin/sh\necho 'total = 16384.00M  used = 100.00M  free = 1.00M  (encrypted)'\n")
        for f in ("df", "sysctl"):
            (fake / f).chmod(0o755)
        self.env = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        self.env.update(PATH="%s:%s" % (fake, os.environ.get("PATH", "")), TMPDIR=str(root), HOME=str(root / "home"))

    def tearDown(self):
        self.tmp.cleanup()

    def run_pr(self, *args):
        r = subprocess.run(["zsh", str(self.script), str(self.app), *args], capture_output=True, text=True, env=self.env, timeout=60)
        return r, (json.loads(self.argv.read_text()) if self.argv.exists() else None)

    def test_ProfilYolsuzBaslatma_TaziSandboxAlir(self):
        r, argv = self.run_pr("-screen-fullscreen", "0", "-ddTelemetryDir", str(self.root / "t"))
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual(1, argv.count("-ddProfilePath"))
        path = Path(argv[argv.index("-ddProfilePath") + 1])
        self.assertEqual("prototype-profile.json", path.name); self.assertTrue(path.parent.is_dir())
        self.assertTrue(str(path).startswith(str(self.root)), "under TMPDIR, never the real Application Support")
        self.assertIn("no -ddProfilePath — sandbox", r.stdout)

    def test_CagiraninYolu_Korunur(self):
        own = str(self.root / "mine" / "p.json")
        r, argv = self.run_pr("-ddProfilePath", own, "-ddTelemetryDir", str(self.root / "t"))
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual(["-ddProfilePath", own], argv[:2]); self.assertEqual(1, argv.count("-ddProfilePath"))
        self.assertNotIn("sandbox", r.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)
