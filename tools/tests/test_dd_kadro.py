#!/usr/bin/env python3
"""WP-089 (C-00 05.10, iCloud move A) · the KADRO root is spelled in ONE place per language and nowhere else:
dd-env.sh DD_KADRO_REL (shell), tools/dd_paths.py KADRO_REL (Python) and slot.sh's fallback literal (bash cannot source
dd-env.sh before it has found it) are the same value; no other tools file spells a KADRO path in code. On move day only
these three values change. usage: python3 tools/tests/test_dd_kadro.py"""
import os, re, subprocess, sys, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import dd_paths  # noqa: E402

ALLOWED = {"dd-env.sh", "dd_paths.py", "slot.sh", "README.md"}


class DdKadroTests(unittest.TestCase):
    def test_UcYerAyniDeger(self):
        rel = dd_paths.KADRO_REL
        self.assertIn('DD_KADRO_REL="%s"' % rel, (TOOLS / "dd-env.sh").read_text())
        self.assertIn('$HOME/%s/context/projects/deaths-demolition/wp-079/driver/tools/dd-env.sh' % rel,
                      (TOOLS / "slot.sh").read_text())

    def test_DdEnvDegiskenleri_PythonIleAyni(self):
        with tempfile.TemporaryDirectory() as home:
            env = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
            env.update(HOME=home, DD_KADRO_REL="/elsewhere", DD_KADRO="/elsewhere")   # the environment must not win
            r = subprocess.run(["zsh", "-c", 'source "$1"; print -r -- "$DD_KADRO|$DD_KPROJ|$DD_GUARD_CANON|$DD_BUILD_ROOT"',
                                "zsh", str(TOOLS / "dd-env.sh")], env=env, capture_output=True, text=True, check=True)
            kadro, kproj, canon, builds = r.stdout.strip().split("|")
            self.assertEqual(os.path.join(home, dd_paths.KADRO_REL), kadro)
            self.assertEqual(kproj, kadro + "/context/projects/deaths-demolition")
            self.assertEqual(canon, kproj + "/wp-079/driver/tools/userdata-guard.sh")
            self.assertEqual(builds, kproj + "/builds")
        self.assertEqual(dd_paths.KPROJ, Path.home() / dd_paths.KADRO_REL / "context/projects/deaths-demolition")

    def test_BaskaAracDosyasindaKadroYoluYok(self):
        offenders = []
        for f in sorted(TOOLS.rglob("*")):
            if not f.is_file() or f.suffix not in (".sh", ".py") or "tests" in f.relative_to(TOOLS).parts or f.name in ALLOWED:
                continue
            for n, line in enumerate(f.read_text(errors="replace").splitlines(), 1):
                code = line.split("#", 1)[0] if f.suffix == ".sh" else line
                if line.lstrip().startswith(("#", "*", '"""')):
                    continue
                if re.search(r"KADRO/context|\"KADRO\"|/KADRO\b", code):
                    offenders.append("%s:%d %s" % (f.relative_to(TOOLS), n, line.strip()[:120]))
        self.assertEqual([], offenders, "KADRO spelled outside dd-env.sh / dd_paths.py / slot.sh")


if __name__ == "__main__":
    unittest.main(verbosity=2)
