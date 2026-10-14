#!/usr/bin/env python3
"""Audit M-10 (C-00 30.09) · dd_scrub_code_env (tools/dd-env.sh) and the release path of tools/build.sh.

The scrub list is not fixed: it is every GetEnvironmentVariable("NAME") literal in the C# of the tree under test; only HOME
is kept. Fully fake: a git repo in a tempdir whose .cs reads DD_X / HOME / DD_UNSET_Y, a fake Unity that writes the build
report and dumps the environment it sees, HOME inside the tempdir, df/sysctl that report plenty (disk gate), no slot.sh
(dd_slot_acquire skips it). No Unity, no Player, no real slot dir, no real user data.
main-ci.sh end to end: tools/tests/test_ci_signal_traps.py · rc.sh: tools/tests/test_rc_stage_guard.py.
usage: python3 tools/tests/test_env_scrub.py"""
import json, os, shutil, subprocess, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
CS = """using System;
static class Probe {
    static string A() => Environment.GetEnvironmentVariable("DD_X");
    static string B() => Environment.GetEnvironmentVariable("HOME");
    static string C() => System.Environment.GetEnvironmentVariable("DD_UNSET_Y");
}
"""
# The fake editor (never named like a Player: .../Contents/MacOS/(Deaths Demolition|DeathsDemolition)) plays DdBuild.Build:
# log, build-report.json "Succeeded", the output .app folder, and the environment it was started with.
FAKE_UNITY = """#!/bin/zsh
log="" out="" rep=""
while [ $# -gt 0 ]; do case "$1" in -logFile) log=$2 ;; -ddOut) out=$2 ;; -ddReport) rep=$2 ;; esac; shift; done
env > "{dump}"
echo "fake build" > "$log"; mkdir -p "$out/Contents"; echo x > "$out/Contents/Info.plist"
print -r -- '{{"result": "Succeeded", "describe": "fixture"}}' > "$rep"
"""


def plenty_bin(root):
    d = Path(root) / "plenty-bin"
    d.mkdir(exist_ok=True)
    (d / "df").write_text("#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho '/dev/fixture 999999999 1 999999999'\n")
    (d / "sysctl").write_text("#!/bin/sh\n[ \"$2\" = vm.swapusage ] && { echo 'total = 1.00M  used = 0.00M  free = 1.00M'; exit 0; }\nexec /usr/sbin/sysctl \"$@\"\n")
    for f in ("df", "sysctl"):
        (d / f).chmod(0o755)
    return str(d)


class EnvScrubFixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-env-scrub-")
        self.root = Path(self.tmp.name).resolve()
        self.home = self.root / "home"; self.home.mkdir()
        self.repo = self.root / "repo"
        (self.repo / "tools").mkdir(parents=True)
        for name in ("dd-env.sh", "build.sh", "app_manifest.py"):
            shutil.copy2(TOOLS / name, self.repo / "tools" / name)
        cs = self.repo / "prototype/DeathsDemolition_PlayableConcept/Assets/Probe.cs"
        cs.parent.mkdir(parents=True); cs.write_text(CS)
        self.git("init", "-q", "-b", "main"); self.git("add", "."); self.git("commit", "-q", "-m", "fixture")

    def tearDown(self):
        self.tmp.cleanup()

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        e.update(HOME=str(self.home), TMPDIR=str(self.root), DD_REPO=str(self.repo), GIT_CONFIG_NOSYSTEM="1",
                 GIT_CEILING_DIRECTORIES=str(self.root),
                 GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t")
        e["PATH"] = plenty_bin(self.root) + ":" + e.get("PATH", "/usr/bin:/bin")
        e.update(extra)
        return e

    def git(self, *args):
        subprocess.run(["git", *args], cwd=self.repo, env=self.env(), check=True, capture_output=True)

    def scrub(self, tree, **extra):
        """Runs dd_scrub_code_env in zsh; returns (rc, stdout, stderr, what the shell's child sees afterwards)."""
        script = ('DD_TIMELINE=""; source "$1"; dd_scrub_code_env "$2"; rc=$?; print -r -- "rc=$rc"; '
                  'print -r -- "scrubbed=[$DD_ENV_SCRUBBED] names=$DD_ENV_SCRUB_NAMES json=$(dd_env_scrubbed_json)"; '
                  'env | grep -E "^(DD_X|DD_UNSET_Y|DD_KEEP|HOME)=" | LC_ALL=C sort')
        r = subprocess.run(["zsh", "-c", script, "zsh", str(self.repo / "tools/dd-env.sh"), str(tree)],
                           env=self.env(**extra), capture_output=True, text=True, timeout=60)
        return r.returncode, r.stdout, r.stderr


class DdScrubCodeEnvTests(EnvScrubFixture):
    def test_KodunOkuduguDegisken_Temizlenir_HomeKalir_Raporlanir(self):
        rc, out, err = self.scrub(self.repo, DD_X="1", DD_KEEP="k")
        self.assertIn("rc=0", out, err)
        self.assertIn('scrubbed=[DD_X] names=3 json=["DD_X"]', out)
        self.assertIn(f"env-scrub: cleared 1 (DD_X) from {self.repo}", out)
        self.assertNotIn("DD_X=", out)                          # the child no longer sees it
        self.assertIn(f"HOME={self.home}", out)                  # allow-listed: kept even though the C# reads it
        self.assertIn("DD_KEEP=k", out)                          # not read by the C#: untouched

    def test_KodunOkuduguAmaAyarliOlmayan_Raporlanmaz(self):
        rc, out, err = self.scrub(self.repo)
        self.assertIn("rc=0", out, err)
        self.assertIn("scrubbed=[] names=3 json=[]", out)
        self.assertIn("env-scrub: nothing to clear (3 names read by C#)", out)
        self.assertNotIn("DD_UNSET_Y", out)

    def test_YeniOkumaKoda_Eklenince_ListeKendiligindenGenisler(self):
        cs = self.repo / "prototype/DeathsDemolition_PlayableConcept/Assets/Probe.cs"
        cs.write_text(CS.replace('"DD_UNSET_Y"', '"DD_UNSET_Y"); static string D() => Environment.GetEnvironmentVariable("DD_NEW"'))
        rc, out, err = self.scrub(self.repo, DD_NEW="1", DD_X="1")
        self.assertIn("scrubbed=[DD_NEW DD_X] names=4", out, err)

    def test_SabitUzerindenOkunanDDDegiskeni_DeTemizlenir(self):
        # C-00 30.09 follow-up: names read through a constant (const string K = "DD_Z"; GetEnvironmentVariable(K)) count too.
        cs = self.repo / "prototype/DeathsDemolition_PlayableConcept/Assets/ConstProbe.cs"
        cs.write_text('using System; static class C { const string K = "DD_CONST_Z"; static string R() => Environment.GetEnvironmentVariable(K); }\n')
        self.git("add", "."); self.git("commit", "-q", "-m", "const probe")
        rc, out, err = self.scrub(self.repo, DD_CONST_Z="1")
        self.assertIn("rc=0", out, err)
        self.assertIn("DD_CONST_Z", out.split("scrubbed=[", 1)[1].split("]", 1)[0])

    def test_GitGrepHatasi_Cikis3_TemizlenmedenKosulmaz(self):
        rc, out, err = self.scrub(self.root / "not-a-repo", DD_X="1")
        self.assertIn("rc=3", out, err)
        self.assertIn("env-scrub: git grep failed", err)
        self.assertIn("not run unscrubbed (3)", err)

    def test_IzinListesi_OrtamdanGenisletilemez(self):
        rc, out, err = self.scrub(self.repo, DD_X="1", DD_ENV_SCRUB_ALLOW="HOME DD_X")
        self.assertIn("scrubbed=[DD_X]", out, err)

    def test_KaynaktaSabitListeYok(self):
        for rel in ("dd-env.sh", "main-ci.sh", "build.sh", "qa/rc.sh"):
            text = (TOOLS / rel).read_text()
            self.assertNotIn("DD_WP076", text, rel)
            self.assertNotIn("test-diag-args", text, rel)
        self.assertEqual(1, (TOOLS / "dd-env.sh").read_text().count('DD_ENV_SCRUB_ALLOW="HOME"'))


class BuildReleaseScrubTests(EnvScrubFixture):
    def build(self, mode, **extra):
        dump = self.root / f"unity-env-{mode}.txt"
        unity = self.root / "fake-unity"
        unity.write_text(FAKE_UNITY.format(dump=dump)); unity.chmod(0o755)
        out = self.root / f"out-{mode}"
        r = subprocess.run(["zsh", str(self.repo / "tools/build.sh"), mode, "--out", str(out)],
                           env=self.env(DD_UNITY=str(unity), DD_STALL_TICK="1", **extra),
                           capture_output=True, text=True, timeout=120)
        seen = dict(l.split("=", 1) for l in dump.read_text().splitlines() if "=" in l) if dump.exists() else None
        return r, out, seen

    def test_ReleaseBuild_UnityDD_XGormez_HomeGorur_RaporaYazilir(self):
        for mode in ("release", "release-probe"):
            with self.subTest(mode=mode):
                r, out, seen = self.build(mode, DD_X="1")
                self.assertEqual(0, r.returncode, r.stdout + r.stderr)
                self.assertNotIn("DD_X", seen)
                self.assertEqual(str(self.home), seen["HOME"])
                report = json.loads((out / "report.json").read_text())
                self.assertEqual(["DD_X"], report["envScrubbed"])
                self.assertTrue(report["ok"])
                self.assertIn(f"env-scrub: cleared 1 (DD_X) from {self.repo}", (out / "timeline.txt").read_text())

    def test_DevBuild_Degismez_DegiskenGecer_RaporAnahtariYok(self):
        r, out, seen = self.build("dev", DD_X="1")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual("1", seen["DD_X"])
        self.assertNotIn("envScrubbed", json.loads((out / "report.json").read_text()))
        self.assertNotIn("env-scrub:", (out / "timeline.txt").read_text())

    def test_ReleaseBuild_GitGrepHatasi_Cikis3_UnityBaslamaz(self):
        shutil.rmtree(self.repo / ".git")
        r, out, seen = self.build("release", DD_X="1")
        self.assertEqual(3, r.returncode, r.stdout + r.stderr)
        self.assertIsNone(seen)
        self.assertFalse((out / "report.json").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
