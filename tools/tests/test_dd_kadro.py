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
# C-00 06.10 (user: no iCloud work on this machine) + gt 06.10 SHOULD: Desktop / Documents as a PATH component — after a
# quote or a slash, or as a join() / joinpath() argument — and the iCloud Drive folders themselves
# gt 07.10 NOTE: any case (APFS ignores it: ~/desktop is ~/Desktop), a bare `cd` / `pushd Desktop`, the glob `Desk*`
OLD_PLACE = re.compile(r"""(["'/]\s*|(?:join|joinpath)\([^)]*?["']|\b(?:cd|pushd)\s+)(Desktop\b|Documents\b|Desk\*)(?!\s*&)"""
                       r"""|Mobile Documents|CloudDocs""", re.I)


def strip_comment(line, suffix):
    """a shell comment starts at a # outside quotes that opens the line or follows a blank — `$#`, `${#x}` and a quoted
    "step #1" are code (gt 07.10 NOTE: `echo "step #1"; ls ~/Desktop` hid the path)."""
    if suffix not in (".sh", ".zsh"):
        return line
    quote = None
    for i, c in enumerate(line):
        if quote:
            if c == quote and (quote == "'" or line[i - 1] != "\\"):
                quote = None
        elif c in "'\"":
            quote = c
        elif c == "#" and (i == 0 or line[i - 1].isspace()):
            return line[:i]
    return line


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


    # -- C-00 06.10 (user: "no iCloud work on this machine"): KADRO and the repo live under ~/Dev; iCloud's Desktop &
    # Documents may hold only symlinks for a while, or nothing — no tools code may depend on them ---------------------
    def test_KadroKokuDevAltinda(self):
        self.assertEqual("Dev/KADRO", dd_paths.KADRO_REL)

    def test_YolDeseni_OrneklerdeDogru(self):
        """gt 06.10 (9a12a6f3, SHOULD): the scan must see every way a tool could spell the old place."""
        hits = ['KADRO_REL = "Desktop/KADRO"', 'DD_ENV="$HOME/Desktop/KADRO/x"', 'x="$HOME"/Desktop/KADRO',
                "os.path.join(HOME, 'Documents', 'Deaths Demolition')", 'Path.home() / "Desktop" / "KADRO"',
                'p = home.joinpath("Desktop")', "p = HOME + '/Documents/x'", 'cd ~/Documents/x',
                "/Users/Erdem/Desktop/KADRO", 'r = "Library/Mobile Documents/com~apple~CloudDocs"',
                'echo "$#"; ls ~/Desktop', 'ls ~/desktop/KADRO', 'cd Desktop', 'pushd Documents >/dev/null',
                'echo "step #1"; ls ~/Desktop', 'ls ~/Desk*', "p = HOME + '/DOCUMENTS'"]
        misses = ['DD_KADRO_REL="Dev/KADRO"', 'echo "the Desktop & Documents folders are off"', 'n=$#',
                  'log "a Desktop app"', 'x = "MyDocumentsTool"', 'echo ok  # was ~/Desktop/KADRO', 'cd "$DD_KADRO"',
                  "echo 'it is # not a comment'", 'cd desktop_tools_x']
        for line in hits:
            self.assertTrue(OLD_PLACE.search(strip_comment(line, ".sh")), line)
        for line in misses:
            self.assertFalse(OLD_PLACE.search(strip_comment(line, ".sh")), line)

    def test_AracKodundaDesktopDocumentsYoluYok(self):
        offenders = []
        for f in sorted(TOOLS.rglob("*")):
            if not f.is_file() or f.suffix not in (".sh", ".py", ".zsh") or f.resolve() == Path(__file__).resolve():
                continue   # (this file spells the old place on purpose: the pattern and its samples)
            for n, line in enumerate(f.read_text(errors="replace").splitlines(), 1):
                if line.lstrip().startswith(("#", "*")):
                    continue
                if OLD_PLACE.search(strip_comment(line, f.suffix)):
                    offenders.append("%s:%d %s" % (f.relative_to(TOOLS), n, line.strip()[:120]))
        self.assertEqual([], offenders, "a Desktop/ or Documents/ path (or iCloud Drive) in the tools' code")

if __name__ == "__main__":
    unittest.main(verbosity=2)
