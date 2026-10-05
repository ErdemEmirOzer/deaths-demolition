#!/usr/bin/env python3
"""Codex-audit M-08 / K-02 (C-00 30.09) · tools/integrate.sh on throwaway fixture repos (tempdir, real git, optional
fake git wrapper first on PATH). Never touches the real repository: the script is copied into the tempdir, run with
--repo <fixture> --no-post, HOME/DD_* point into the tempdir. usage: python3 tools/tests/test_integrate_gates.py"""
import os, shutil, subprocess, sys, tempfile, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
REAL_GIT = shutil.which("git")
GOOD_SUBJ = "WP-999 K2: fixture change (C-00 30.09)"
GOOD_BODY = "Fixture body.\n\nTest: fixture 1/1 (no Unity).\n"

# Forwards everything to the real git. FAKE_MODE=status-fail: once any `cherry-pick` was seen, `status --porcelain*`
# fails (128). FAKE_MODE=restore-noop: `restore` does nothing, so the conflicted path stays dirty.
FAKE_GIT = r'''#!/bin/sh
sub=""; skip=0
for a in "$@"; do
  if [ "$skip" = 1 ]; then skip=0; continue; fi
  case "$a" in -C|-c) skip=1 ;; -*) ;; *) sub="$a"; break ;; esac
done
[ "$sub" = cherry-pick ] && : > "$FAKE_MARK"
if [ "$FAKE_MODE" = status-fail ] && [ "$sub" = status ] && [ -f "$FAKE_MARK" ]; then
  for a in "$@"; do case "$a" in --porcelain*) echo "fake git: simulated status failure" >&2; exit 128 ;; esac; done
fi
if [ "$FAKE_MODE" = restore-noop ] && [ "$sub" = restore ]; then exit 0; fi
exec "$REAL_GIT" "$@"
'''


class EntegreKapiTestleri(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-int-gates-")
        self.root = Path(self.tmp.name)
        tools = self.root / "tools"; tools.mkdir()
        for name in ("integrate.sh", "dd-env.sh"):
            shutil.copy2(TOOLS / name, tools / name)
        self.script = tools / "integrate.sh"
        self.repo = self.root / "repo"; self.repo.mkdir()
        (self.root / "home").mkdir(); (self.root / "bin").mkdir(); (self.root / "tmp").mkdir()
        self.env = {
            "PATH": os.environ["PATH"], "HOME": str(self.root / "home"), "TMPDIR": str(self.root / "tmp"),
            "LANG": "en_US.UTF-8", "LC_ALL": "en_US.UTF-8", "GIT_CONFIG_NOSYSTEM": "1",
            "GIT_AUTHOR_NAME": "fixture", "GIT_AUTHOR_EMAIL": "fixture@example.invalid",
            "GIT_COMMITTER_NAME": "fixture", "GIT_COMMITTER_EMAIL": "fixture@example.invalid",
            "DD_REPO": str(self.repo), "DD_BUILD_ROOT": str(self.root / "builds"), "REAL_GIT": REAL_GIT,
            "FAKE_MARK": str(self.root / "cherry-pick-seen"),
        }
        self.git("init", "-q", "-b", "main")
        (self.repo / "tools").mkdir()
        (self.repo / "tools" / "a.txt").write_text("base\n")
        self.git("add", "tools/a.txt"); self.git("commit", "-q", "-m", "base")

    def tearDown(self):
        self.tmp.cleanup()

    def git(self, *args):
        r = subprocess.run([REAL_GIT, "-C", str(self.repo), *args], capture_output=True, text=True, env=self.env)
        self.assertEqual(r.returncode, 0, f"git {args}: {r.stderr}")
        return r.stdout

    def source(self, subject=GOOD_SUBJ, body=GOOD_BODY, content="feat\n", conflict=False):
        """Branch `feat` with one commit on tools/a.txt; with conflict=True main moves the same line too."""
        self.git("checkout", "-q", "-b", "feat")
        (self.repo / "tools" / "a.txt").write_text(content)
        self.git("commit", "-q", "-am", subject, "-m", body)
        self.git("checkout", "-q", "main")
        if conflict:
            (self.repo / "tools" / "a.txt").write_text("main\n")
            self.git("commit", "-q", "-am", "main moves")
        return self.git("rev-parse", "main").strip()

    def run_int(self, *extra, fake=None):
        env = dict(self.env)
        if fake:
            git = self.root / "bin" / "git"; git.write_text(FAKE_GIT); git.chmod(0o755)
            env["PATH"] = str(self.root / "bin") + os.pathsep + env["PATH"]; env["FAKE_MODE"] = fake
        return subprocess.run(["zsh", str(self.script), "feat", "--tag", "fixture-tag", "--repo", str(self.repo),
                               "--no-post", *extra], capture_output=True, text=True, env=env, cwd=str(self.root))

    def status(self):
        return subprocess.run([REAL_GIT, "-C", str(self.repo), "status", "--porcelain"], capture_output=True,
                              text=True, env=self.env).stdout

    # ---------------------------------------------------------------- M-08 rollback
    def test_Rollback_GercekCatisma_Cikis4TemizAgac(self):
        head = self.source(conflict=True)
        r = self.run_int("--apply")
        out = r.stdout + r.stderr
        self.assertEqual(r.returncode, 4, out)
        self.assertIn("CONFLICT in tools/a.txt", out)
        self.assertIn("aborted: 0 dirty paths remain", out)
        self.assertEqual(self.status(), "")
        self.assertEqual(self.git("rev-parse", "main").strip(), head)
        self.assertEqual((self.repo / "tools" / "a.txt").read_text(), "main\n")

    def test_Rollback_StatusCherryPickSonrasiBasarisiz_Cikis7(self):
        head = self.source(conflict=True)
        r = self.run_int("--apply", fake="status-fail")
        out = r.stdout + r.stderr
        self.assertEqual(r.returncode, 7, out)
        self.assertIn("ROLLBACK UNVERIFIED", out)
        self.assertNotIn("aborted: 0 dirty paths", out)
        self.assertTrue((self.root / "cherry-pick-seen").exists())
        self.assertEqual(self.git("rev-parse", "main").strip(), head)
        self.assertNotEqual(self.status(), "")   # the working tree really is not clean — exit 4 would have lied

    def test_Rollback_RestoreEtkisiz_KirliYolKalir_Cikis7(self):
        head = self.source(conflict=True)
        r = self.run_int("--apply", fake="restore-noop")
        out = r.stdout + r.stderr
        self.assertEqual(r.returncode, 7, out)
        self.assertIn("aborted: 1 dirty paths remain", out)
        self.assertIn("WARN: rollback left paths", out)
        self.assertEqual(self.git("rev-parse", "main").strip(), head)
        self.assertIn("tools/a.txt", self.status())

    def test_Rollback_TumCagiranlar_AbortPickVeya7(self):
        text = self.script.read_text(encoding="utf-8")
        self.assertNotIn("abort_pick; exit 4", text)
        code = [l for l in text.splitlines() if not l.lstrip().startswith("#")]
        self.assertEqual(sum("abort_pick || exit 7; exit 4" in l for l in code), 6)   # 03/04+00/01/conflict/verify/ids/commit
        self.assertIn("7 rollback unverified / dirty", text)

    # ---------------------------------------------------------------- gate 4 secrets (C-00 05.10: case)
    # Key-shaped strings are built at run time, so this file's own added lines never trip the gate it tests.
    AKIA_REAL = "AK" + "IA" + "Q3EGRT5YHB2KLM7N"                     # realistic, no EXAMPLE
    AKIA_DOC = "AK" + "IA" + "IOSFODNN7" + "EXAMPLE"                 # AWS's documented example key: excluded
    S02_NAME = "YalnizIleri_TekrarYokSayilir_Sonraki" + "AtlananlariIndirir"   # S-02 179f533c (joined at run time)

    def test_GizliKapisi_TurkceTestAdi_YanlisAlarmYok(self):
        r, out = self.dry(content="public void %s() { }\n" % self.S02_NAME)
        self.assertEqual(r.returncode, 0, out)
        self.assertNotIn("GATE FAIL", out)
        self.assertIn("RESULT: gate passed", out)

    def test_GizliKapisi_GercekAkiaAnahtari_Cikis3(self):
        r, out = self.dry(content='aws = "%s"\n' % self.AKIA_REAL)
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: secret-like content in added lines", out)
        self.assertIn(self.AKIA_REAL, out)

    def test_GizliKapisi_KucukHarfAkia_AnahtarSayilmaz(self):
        r, out = self.dry(content="x = %s\n" % self.AKIA_REAL.lower())
        self.assertEqual(r.returncode, 0, out)

    def test_GizliKapisi_AtamaKalibi_BuyukKucukHarfDuyarsizKalir(self):
        r, out = self.dry(content='Pass' + 'word = "' + 'hunter2hunter2' + '"\n')
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("secret-like content", out)

    def test_GizliKapisi_AwsOrnekAnahtari_Dislanir(self):
        r, out = self.dry(content='doc = "%s"\n' % self.AKIA_DOC)
        self.assertEqual(r.returncode, 0, out)

    # ---------------------------------------------------------------- K-02 commit-message gate
    def dry(self, *extra, **kw):
        self.source(**kw)
        r = self.run_int(*extra)
        return r, r.stdout + r.stderr

    def test_MesajKapisi_IyiKonuVeTestSatiri_Gecer(self):
        r, out = self.dry()
        self.assertEqual(r.returncode, 0, out)
        self.assertIn("RESULT: gate passed", out)
        self.assertNotIn("GATE FAIL", out)

    def test_MesajKapisi_GovdeEditModeAnar_Gecer(self):
        r, out = self.dry(body="Ran the suite.\n\nEditMode 12/12 green.\n")
        self.assertEqual(r.returncode, 0, out)

    def test_MesajKapisi_C00TarihiYok_Cikis3(self):
        r, out = self.dry(subject="WP-999 K2: fixture change")
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: commit subject", out)

    def test_MesajKapisi_ConventionalKonu_Cikis3(self):
        r, out = self.dry(subject="fix(tools): fixture change (C-00 30.09)")
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: commit subject", out)

    def test_MesajKapisi_TestSatiriYok_Cikis3(self):
        r, out = self.dry(body="Fixture body without any verification line.\n")
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: commit body has no test line", out)
        self.assertNotIn("GATE FAIL: commit subject", out)

    def test_MesajKapisi_SubjectExempt_GecerVeGerekceYazar(self):
        reason = "C-00 revert of b2be2562 (hotfix)"
        r, out = self.dry("--subject-exempt", reason, subject="Revert \"fix(tools): x\"", body="no test line\n")
        self.assertEqual(r.returncode, 0, out)
        result = [l for l in out.splitlines() if l.startswith("RESULT:")]
        self.assertEqual(len(result), 1, out)
        self.assertIn("gate passed", result[0])
        self.assertIn("EXEMPT", result[0]); self.assertIn(reason, result[0])

    def test_MesajKapisi_SubjectExemptBosGerekce_Cikis64(self):
        r, out = self.dry("--subject-exempt", "  ")
        self.assertEqual(r.returncode, 64, out)


if __name__ == "__main__":
    unittest.main(verbosity=2)
