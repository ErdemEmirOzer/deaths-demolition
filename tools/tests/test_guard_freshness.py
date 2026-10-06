#!/usr/bin/env python3
"""WP-089 (C-00 05.10) · tools/guard_freshness.py and its two callers (dd_guard_canon_check, guard_receipt.canon_refusal):
a real git repo whose main carries tools/{userdata-guard.sh, userdata_attrib.py, userdata_guard_io.py, dd-env.sh,
slot.sh} and a real detached driver worktree under a fixture HOME at the canonical path. Fresh → 0; guard, attrib or
ledger io differs (edited, or main moved ahead) → 64; dd-env/slot differ → warning only; driver not a git checkout →
warning only.
usage: python3 tools/tests/test_guard_freshness.py"""
import os, subprocess, sys, tempfile, unittest
from pathlib import Path
from unittest import mock

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS)); sys.path.insert(0, str(TOOLS / "qa"))
import guard_freshness as gf  # noqa: E402
import guard_receipt as gr  # noqa: E402

CANON_REL = "Dev/KADRO/context/projects/deaths-demolition/wp-079/driver"


class GuardFreshnessTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-guard-fresh-")
        self.root = Path(self.tmp.name).resolve()
        self.repo = self.root / "repo"
        (self.repo / "tools").mkdir(parents=True)
        for name, text in (("userdata-guard.sh", "#!/bin/zsh\nexit 0\n"), ("userdata_attrib.py", "# attrib\n"),
                           ("userdata_guard_io.py", "# ledger io\n"), ("slot.sh", "#!/bin/bash\nexit 0\n"),
                           ("dd-env.sh", (TOOLS / "dd-env.sh").read_text())):
            (self.repo / "tools" / name).write_text(text)
        (self.repo / "tools" / "userdata-guard.sh").chmod(0o755)
        self.git("init", "-q", "-b", "main"); self.git("add", "."); self.git("commit", "-q", "-m", "base")
        self.home = self.root / "home"
        self.driver = self.home / CANON_REL
        self.driver.parent.mkdir(parents=True)
        self.git("worktree", "add", "-q", "--detach", str(self.driver), "main")
        self.guard = self.driver / "tools" / "userdata-guard.sh"

    def tearDown(self):
        self.tmp.cleanup()

    def git(self, *args, cwd=None):
        env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t")
        return subprocess.run(["git", "-C", str(cwd or self.repo), *args], check=True, capture_output=True, text=True, env=env).stdout

    def advance_main(self, name, text):
        (self.repo / "tools" / name).write_text(text)
        self.git("commit", "-qam", "main moves: " + name)

    def cli(self):
        return subprocess.run([sys.executable, str(TOOLS / "guard_freshness.py"), str(self.guard)], capture_output=True, text=True)

    def test_Taze_0(self):
        self.assertEqual(("fresh", ""), gf.freshness(self.guard))
        r = self.cli()
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertEqual("", r.stderr)

    def test_GuardYerelDuzenlendi_64(self):
        self.guard.write_text("#!/bin/zsh\n# local edit\nexit 0\n")
        state, msg = gf.freshness(self.guard)
        self.assertEqual("stale", state)
        self.assertIn("userdata-guard.sh", msg)
        self.assertIn("checkout -q --detach main", msg)
        self.assertEqual(64, self.cli().returncode)

    def test_MainIlerledi_GuardAttribVeyaDefterIo_64(self):
        for name in ("userdata-guard.sh", "userdata_attrib.py", "userdata_guard_io.py"):
            with self.subTest(name=name):
                self.advance_main(name, "# newer %s\n" % name)
                self.assertEqual("stale", gf.freshness(self.guard)[0])
                self.git("checkout", "-q", "--detach", "main", cwd=self.driver)   # C-00 syncs the driver
                self.assertEqual("fresh", gf.freshness(self.guard)[0])

    def test_DdEnvSlotFarki_YalnizUyari(self):
        self.advance_main("slot.sh", "#!/bin/bash\n# newer\nexit 0\n")
        state, msg = gf.freshness(self.guard)
        self.assertEqual("aux", state)
        r = self.cli()
        self.assertEqual(0, r.returncode)
        self.assertIn("warning", r.stderr)
        self.assertIn("slot.sh", r.stderr)

    def test_GitDegil_YalnizUyari(self):
        other = self.root / "plain/tools"; other.mkdir(parents=True)
        g = other / "userdata-guard.sh"; g.write_text("x"); g.chmod(0o755)
        self.assertEqual("unknown", gf.freshness(g)[0])
        r = subprocess.run([sys.executable, str(TOOLS / "guard_freshness.py"), str(g)], capture_output=True, text=True)
        self.assertEqual(0, r.returncode)
        self.assertIn("not checked", r.stderr)

    def test_DdGuardCanonCheck_Eskiyse64(self):
        def run():
            env = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
            env["HOME"] = str(self.home)
            return subprocess.run(["zsh", "-c", 'source "$1"; dd_guard_canon_check', "zsh", str(TOOLS / "dd-env.sh")],
                                  env=env, capture_output=True, text=True)
        r = run()
        self.assertEqual(0, r.returncode, r.stderr)
        self.guard.write_text("#!/bin/zsh\n# drift\nexit 0\n")
        r = run()
        self.assertEqual(64, r.returncode, r.stderr)
        self.assertIn("differs from main", r.stderr)

    def test_GuardReceiptCanonRefusal_Eskiyse(self):
        with mock.patch.object(gr, "GUARD", str(self.guard)), mock.patch.dict(os.environ, {}, clear=False):
            os.environ.pop("DD_GUARD_ROOT", None)
            self.assertIsNone(gr.canon_refusal())
            self.guard.write_text("#!/bin/zsh\n# drift\nexit 0\n")
            self.assertIn("differs from main", gr.canon_refusal())


if __name__ == "__main__":
    unittest.main(verbosity=2)
