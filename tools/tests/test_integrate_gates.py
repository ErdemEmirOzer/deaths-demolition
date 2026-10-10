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

    # ---------------------------------------------------------------- S-09 / C-00 05.10: default repo = main worktree
    def linked_worktree_script(self):
        """integrate.sh + dd-env.sh inside a linked worktree of the fixture repo (untracked → that worktree is dirty)."""
        wt = self.root / "wt"
        self.git("worktree", "add", "-q", "--detach", str(wt), "main")
        for name in ("integrate.sh", "dd-env.sh"):
            shutil.copy2(TOOLS / name, wt / "tools" / name)
        return wt / "tools" / "integrate.sh"

    def run_default(self, script):
        env = {k: v for k, v in self.env.items() if k != "DD_REPO"}
        return subprocess.run(["zsh", str(script), "feat", "--tag", "fixture-tag", "--no-post", "--verbose"],
                              capture_output=True, text=True, env=env, cwd=str(self.root))

    def test_VarsayilanRepo_KirliWorktreedenCagri_AnaWorktreeDenetlenir(self):
        self.source()
        r = self.run_default(self.linked_worktree_script())
        self.assertIn("repo: the main worktree %s" % self.repo.resolve(), r.stdout, r.stdout + r.stderr)
        self.assertIn("main worktree clean", r.stdout)
        self.assertNotIn("main worktree dirty", r.stdout)

    def test_VarsayilanRepo_AnaKirli_MesajRepoOnerir(self):
        self.source()
        (self.repo / "tools" / "a.txt").write_text("local edit\n")
        r = self.run_default(self.linked_worktree_script())
        self.assertIn("main worktree dirty: %s (not the main checkout? pass --repo <main checkout>)" % self.repo.resolve(),
                      r.stdout, r.stdout + r.stderr)

    def test_AcikRepo_Degismez(self):
        self.source()
        r = self.run_int("--verbose")
        self.assertNotIn("repo: the main worktree", r.stdout)

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

    # ---------------------------------------------------------- gate 4c (C-00 10.10): the acoustic map is baked from its json
    AC_JSON = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Environment/World/V6/acoustic-spaces-v6.json"
    AC_MAP = "prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Audio/Resources/DD_AcousticSpaces.asset"
    J1 = b'{"schema": "acoustic-v6", "spaces": []}\n'
    J2 = b'{"schema": "acoustic-v6", "spaces": [{"id": "R1", "enclosure": "Room"}]}\n'

    @staticmethod
    def h16(b):
        import hashlib
        return hashlib.sha256(b).hexdigest()[:16]

    @staticmethod
    def map_bytes(h):
        return ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_Name: DD_AcousticSpaces\n"
                "  _sourceHash: " + h + "\n  _worldScene: World_V6_Greybox\n  _spaces: []\n").encode()

    def put(self, rel, data):
        f = self.repo / rel
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_bytes(data)

    def ac_source(self, json=None, map_hash=None, main_pair=True, main_stale=False):
        """main: the pair (json J1 + its map; stale = a wrong hash) unless main_pair=False. Branch feat writes the json and /
        or the map when given, else only tools/a.txt."""
        if main_pair:
            self.put(self.AC_JSON, self.J1)
            self.put(self.AC_MAP, self.map_bytes("0" * 16 if main_stale else self.h16(self.J1)))
            self.git("add", self.AC_JSON, self.AC_MAP); self.git("commit", "-q", "-m", "baked pair")
        self.git("checkout", "-q", "-b", "feat")
        paths = []
        if json is not None:
            self.put(self.AC_JSON, json); paths.append(self.AC_JSON)
        if map_hash is not None:
            self.put(self.AC_MAP, self.map_bytes(map_hash)); paths.append(self.AC_MAP)
        if not paths:
            (self.repo / "tools" / "a.txt").write_text("feat\n"); paths.append("tools/a.txt")
        self.git("add", *paths); self.git("commit", "-q", "-m", GOOD_SUBJ, "-m", GOOD_BODY)
        self.git("checkout", "-q", "main")
        r = self.run_int()
        return r, r.stdout + r.stderr

    def test_AkustikKapisi_JsonDegisirHaritaBayat_Cikis3(self):
        """S-05 588a3b74's case: the json changes, the map keeps the old hash → every Unity run re-bakes a tracked file."""
        r, out = self.ac_source(json=self.J2)
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: gate 4c: DD_AcousticSpaces.asset _sourceHash %s != sha256(acoustic-spaces-v6.json)[:16] %s"
                      % (self.h16(self.J1), self.h16(self.J2)), out)

    def test_AkustikKapisi_JsonVeHaritaBirlikte_Gecer(self):
        r, out = self.ac_source(json=self.J2, map_hash=self.h16(self.J2))
        self.assertEqual(r.returncode, 0, out)
        self.assertNotIn("gate 4c", out)

    def test_AkustikKapisi_YalnizHaritaYanlisHash_Cikis3(self):
        r, out = self.ac_source(map_hash="f" * 16)
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: gate 4c", out)

    def test_AkustikKapisi_DokunmayanDal_BayatMaindeReddedilmez(self):
        """a branch cut from an older (stale) main that touches neither file is not refused — the pair is main's business."""
        r, out = self.ac_source(main_stale=True)
        self.assertEqual(r.returncode, 0, out)
        self.assertNotIn("gate 4c", out)

    def test_AkustikKapisi_BomluJson_BomsuzHashlenir(self):
        """File.ReadAllText drops a UTF-8 BOM before Encoding.UTF8.GetBytes: the hash is the BOM-less bytes'."""
        bom = b"\xef\xbb\xbf" + self.J2
        r, out = self.ac_source(json=bom, map_hash=self.h16(self.J2))
        self.assertEqual(r.returncode, 0, out)
        self.assertNotEqual(self.h16(bom), self.h16(self.J2))

    def test_AkustikKapisi_JsonVarHaritaYok_Cikis3(self):
        r, out = self.ac_source(json=self.J2, main_pair=False)
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("acoustic-spaces-v6.json without DD_AcousticSpaces.asset", out)

    def test_AkustikKapisi_GercekVeri_UnityPisirmesiyleAyniHash(self):
        """the real pair: main's json + main's map pass (Unity's own bake, c94625a7); main's json + 3e44f614's stale map fail."""
        real = TOOLS.parent
        def show(ref, rel):
            r = subprocess.run([REAL_GIT, "-C", str(real), "show", "%s:%s" % (ref, rel)], capture_output=True)
            if r.returncode != 0:
                self.skipTest("real repo objects not readable here (%s:%s)" % (ref, rel))
            return r.stdout
        jm, mm, mold = show("c94625a7", self.AC_JSON), show("c94625a7", self.AC_MAP), show("3e44f614", self.AC_MAP)
        for stale, expect in ((False, 0), (True, 3)):
            with self.subTest(stale=stale):
                self.tearDown(); self.setUp()
                self.git("checkout", "-q", "-b", "feat")
                self.put(self.AC_JSON, jm); self.put(self.AC_MAP, mold if stale else mm)
                self.git("add", self.AC_JSON, self.AC_MAP); self.git("commit", "-q", "-m", GOOD_SUBJ, "-m", GOOD_BODY)
                self.git("checkout", "-q", "main")
                r = self.run_int()
                self.assertEqual(r.returncode, expect, r.stdout + r.stderr)

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

    # ---------------------------------------------------------------- G4 D8 next slice (C-00 06.10): WIP commits
    def test_MesajKapisi_WipEtiketliKonu_Cikis3(self):
        r, out = self.dry(subject="WP-999 WIP: fixture change on top of x (C-00 06.10)")
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: WIP commit (subject label", out)

    def test_MesajKapisi_WipGovdeNotForMain_Cikis3(self):
        r, out = self.dry(body="Not for main — loss protection.\n\nTest: unit 3/3\n")
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: WIP commit (a body line", out)

    def test_MesajKapisi_WipGovdeTestWip_Cikis3(self):
        r, out = self.dry(body="Did x.\n\nTest: WIP, not verified\n")
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: WIP commit (a body line", out)

    def test_MesajKapisi_WipKelimesiEtiketDisindaVeCumleIcinde_Gecer(self):
        """main's own history: 'final WIP table' after the label, '(not for main)' / 'not verified' inside a sentence."""
        r, out = self.dry(subject="WP-999 C-00 handoff: the final WIP table (C-00 06.10)",
                          body="Replaces the WIP dee54ffa (not for main).\nOrder not verified: a note.\n\nTest: docs only\n")
        self.assertEqual(r.returncode, 0, out)
        self.assertNotIn("WIP commit", out)

    def test_MesajKapisi_KucukHarfWipEtiketi_Cikis3(self):
        r, out = self.dry(subject="WP-999 wip: fixture change (C-00 06.10)")
        self.assertEqual(r.returncode, 3, out)
        self.assertIn("GATE FAIL: WIP commit (subject label", out)

    def test_MesajKapisi_WipBenzeriKelimeler_YanlisAlarmYok(self):
        """review NIT-1: 'Not for maintenance', 'Not for main-ci', 'Test: wiped …' are no WIP marks."""
        r, out = self.dry(body="Not for maintenance windows.\nNot for main-ci runs.\n\nTest: wiped the cache, 3/3 green\n")
        self.assertEqual(r.returncode, 0, out)
        self.assertNotIn("WIP commit", out)

    def test_MesajKapisi_WipSubjectExempt_Gecer(self):
        r, out = self.dry("--subject-exempt", "C-00: a WIP taken on purpose", subject="WP-999 WIP: x (C-00 06.10)")
        self.assertEqual(r.returncode, 0, out)
        self.assertIn("EXEMPT", [l for l in out.splitlines() if l.startswith("RESULT:")][0])
        self.assertIn("WARNING: the exempted commit is WIP-labelled", out)   # review NIT-3: never silent

    def test_MesajKapisi_SubjectExemptBosGerekce_Cikis64(self):
        r, out = self.dry("--subject-exempt", "  ")
        self.assertEqual(r.returncode, 64, out)


if __name__ == "__main__":
    unittest.main(verbosity=2)
