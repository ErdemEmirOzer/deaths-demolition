#!/usr/bin/env python3
"""WP-079G (C-00 30.09) · tools/cleanup-merged.sh + tools/disk-report.sh on throwaway fixtures (tempdir, real git).
Never touches the real repository or the KADRO root: the scripts are copied into the tempdir and run with DD_REPO=<fixture>,
--root <tempdir root>, HOME / DD_SLOT_BASE inside the tempdir. --apply runs only against these fixtures.
Covers: a registered worktree whose folder is gone (was "bad math expression") → PRUNE row, size 0, no crash; non-standard
folders (worktree-*, perf-*, release-*, rc-*, wp-*/builds, ~/.codex/worktrees) in both reports; the apparent-size / real-gain
column naming (text + JSON); the keep-guard (.dd-keep, recent < 24 h, in use by argv / Unity-like cwd+relative arg /
slot-owner cwd, unknown = fail closed); size helpers robust to a broken du; merged worktrees still REMOVE.
usage: python3 tools/tests/test_cleanup_disk_report.py"""
import json, os, shutil, subprocess, sys, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
COPY = ("cleanup-merged.sh", "disk-report.sh", "dd-env.sh", "apfs_real_gain.py", "cleanup_guard.py")
OLD = time.time() - 3 * 86400
CLONE_NOTE = "apparent size (clones share blocks)"


def make_old(path):
    """Every entry under `path` (and `path` itself) gets an mtime 3 days back."""
    for d, dirs, files in os.walk(path, topdown=False):
        for n in files + dirs:
            os.utime(os.path.join(d, n), (OLD, OLD), follow_symlinks=False)
    os.utime(path, (OLD, OLD))


def filled(path, mib=1, name="data.bin"):
    path.mkdir(parents=True, exist_ok=True)
    (path / name).write_bytes(os.urandom(mib * 1048576))
    return path


class TemizlikRaporTestleri(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-cleanup-report-")
        t = Path(self.tmp.name).resolve()
        self.t = t
        (t / "tools").mkdir()
        for n in COPY:
            shutil.copy2(TOOLS / n, t / "tools" / n)
        self.home, self.root, self.repo, self.bin, self.slots = t / "home", t / "root", t / "repo", t / "bin", t / "slots"
        for d in (self.home, self.root, self.repo, self.bin, self.slots):
            d.mkdir()
        self.procs = []
        self.env = dict(os.environ, HOME=str(self.home), DD_REPO=str(self.repo), DD_SLOT_BASE=str(self.slots),
                        GIT_AUTHOR_NAME="fixture", GIT_AUTHOR_EMAIL="f@x", GIT_COMMITTER_NAME="fixture",
                        GIT_COMMITTER_EMAIL="f@x", GIT_CONFIG_NOSYSTEM="1", DD_TIMELINE="")
        self.git("init", "-q", "-b", "main")
        (self.repo / "a.txt").write_text("a\n")
        self.git("add", "a.txt")
        self.git("commit", "-q", "-m", "init")

    def tearDown(self):
        for p in self.procs:
            p.kill()
            p.wait()
        for d, dirs, _ in os.walk(self.t):
            for n in dirs:
                try:
                    os.chmod(os.path.join(d, n), 0o755)
                except OSError:
                    pass
        self.tmp.cleanup()

    # ---- helpers -------------------------------------------------------------------------------------------------
    def git(self, *a, cwd=None):
        return subprocess.run(["git", *a], cwd=cwd or self.repo, env=self.env, check=True, capture_output=True, text=True).stdout

    def merged_worktree(self, wp):
        path = self.root / wp / "worktree" / "deaths-demolition"
        self.git("worktree", "add", "-q", "-b", f"{wp}-x", str(path), "main")
        (path / f"{wp}.txt").write_text(wp)
        self.git("add", f"{wp}.txt", cwd=path)
        self.git("commit", "-q", "-m", f"{wp.upper()} fixture package commit", cwd=path)
        self.git("merge", "-q", "--ff-only", f"{wp}-x")
        return path

    def run_tool(self, name, *args, env=None):
        r = subprocess.run(["zsh", str(self.t / "tools" / name), *args], env=env or self.env, capture_output=True,
                           text=True, timeout=300)
        return r.returncode, r.stdout, r.stderr

    def cleanup(self, *args, env=None):
        return self.run_tool("cleanup-merged.sh", "--root", str(self.root), *args, env=env)

    def row(self, out, needle):
        rows = [l for l in out.splitlines() if needle in l]
        self.assertTrue(rows, f"no row with {needle!r} in:\n{out}")
        return rows[0]

    def sleeper(self, *argv, cwd=None, exe=None):
        p = subprocess.Popen([exe or sys.executable, *argv], cwd=cwd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.procs.append(p)
        time.sleep(0.5)
        self.assertIsNone(p.poll(), "fixture sleeper must be alive")
        return p

    # ---- 1) prunable worktree ------------------------------------------------------------------------------------
    def test_Temizlik_KayitliWorktreeKlasoruSilinmis_CokmezPrunableSatiriBoyut0(self):
        gone = self.merged_worktree("wp-101")
        shutil.rmtree(gone)
        rc, out, err = self.cleanup("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertNotIn("bad math", out + err)
        line = self.row(out, "prunable (folder missing) → git worktree prune")
        self.assertEqual(line.split()[:3], ["0", "0", "PRUNE"], line)
        self.assertIn("wp-101/worktree/deaths-demolition", line)
        self.assertIn("TOTAL reclaimable (1+2+3): 0 MB", out)
        self.assertIn("wp-101", self.git("worktree", "list"), "dry-run must not prune")

    def test_Temizlik_ApplyFixture_PrunableBudanirBirlesmisWorktreeSilinir(self):
        kept = self.merged_worktree("wp-100")
        gone = self.merged_worktree("wp-101")
        shutil.rmtree(gone)
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("git worktree prune done", out)
        listing = self.git("worktree", "list", "--porcelain")
        self.assertNotIn("wp-101", listing)
        self.assertNotIn("wp-100", listing)
        self.assertFalse(kept.exists())

    def test_Temizlik_BirlesmisWorktree_REMOVEDavranisiDegismez(self):
        self.merged_worktree("wp-100")
        path = self.root / "wp-102" / "worktree" / "deaths-demolition"
        self.git("worktree", "add", "-q", "-b", "wp-102-x", str(path), "main")
        rc, out, err = self.cleanup("--dry-run")
        self.assertEqual(rc, 0, out + err)
        line = self.row(out, "wp-100/worktree/deaths-demolition")
        self.assertIn("REMOVE", line)
        self.assertIn("integrated wp-100-x", line)
        self.assertIn("no package commit of its own yet (wp-102-x", self.row(out, "wp-102/worktree"))

    def test_Temizlik_KilitliPrunable_Tutulur(self):
        gone = self.merged_worktree("wp-103")
        self.git("worktree", "lock", str(gone))
        shutil.rmtree(gone)
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("but locked", self.row(out, "wp-103/worktree"))
        self.assertIn("wp-103", self.git("worktree", "list"))

    # ---- 2) non-standard folders ---------------------------------------------------------------------------------
    def test_Rapor_StandartDisiKlasorler_IkiRapordaGorunurCleanupSilmez(self):
        dirs = [filled(self.root / "worktree-x"), filled(self.root / "wp-200" / "perf-x"),
                filled(self.root / "wp-200" / "release-x"), filled(self.root / "wp-200" / "builds"),
                filled(self.root / "rc-x"), filled(self.home / ".codex" / "worktrees" / "cx")]
        for d in dirs:
            make_old(d)
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        section = out.split("== 5)", 1)[1]
        for label in ("worktree-x", "wp-200/perf-x", "wp-200/release-x", "wp-200/builds", "rc-x", "~/.codex/worktrees/cx"):
            line = self.row(section, label)
            self.assertEqual(line.split()[0], "1", line)
            self.assertIn("report", line)
        for d in dirs:
            self.assertTrue(d.exists(), f"report-only folder deleted: {d}")
        rc, dout, derr = self.run_tool("disk-report.sh", "--root", str(self.root), "--json", str(self.t / "d.json"))
        self.assertEqual(rc, 0, dout + derr)
        for label in ("worktree-x", "wp-200/perf-x", "wp-200/release-x", "wp-200/builds", "rc-x", "~/.codex/worktrees/cx"):
            self.assertIn(label, dout)
        data = json.loads((self.t / "d.json").read_text())
        self.assertEqual(data["otherFoldersMB"], 6)
        self.assertEqual(data["codexWorktreesMB"], 1)
        self.assertEqual(len(data["otherFolders"]), 6)

    def test_Rapor_StandartDisiWorktreeIcindeKayitliWorktree_Bolum1eYonlendirir(self):
        path = self.root / "wp-201" / "worktree-r2" / "deaths-demolition"
        self.git("worktree", "add", "-q", "-b", "wp-201-x", str(path), "main")
        rc, out, err = self.cleanup("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("wp-201/worktree-r2/deaths-demolition", out.split("== 2)")[0])
        self.assertIn("1 registered worktree(s) inside → see 1)", self.row(out.split("== 5)", 1)[1], "wp-201/worktree-r2"))

    # ---- 3) clone-aware size naming -------------------------------------------------------------------------------
    def test_Rapor_KlonSutunAdi_MetinVeJSONTutarli(self):
        rc, out, err = self.cleanup("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertIn(CLONE_NOTE, out)
        self.assertIn("apparentMB", out)
        self.assertIn("realGainMB", out)
        self.assertIn("estimated real gain", self.row(out, "TOTAL reclaimable"))
        rc, dout, derr = self.run_tool("disk-report.sh", "--root", str(self.root), "--json", str(self.t / "d.json"))
        self.assertEqual(rc, 0, dout + derr)
        self.assertIn(CLONE_NOTE, dout)
        data = json.loads((self.t / "d.json").read_text())
        self.assertEqual(data["sizeKind"], CLONE_NOTE)
        for key in ("totalMB", "worktreesMB", "libraryMB", "buildMB", "evidenceMB", "reportsMB", "backupsMB",
                    "assetProductionMB", "gitMB", "lfsMB", "freeMB", "swap", "realGainMethod", "otherFolders"):
            self.assertIn(key, data)

    def test_GercekKazanc_CpcKlonu_0KendiVerisi_TamBoyut(self):
        src = filled(self.t / "src", mib=2)
        clone = self.root / "wp-400" / "build-clone"
        clone.parent.mkdir(parents=True)
        subprocess.run(["cp", "-cR", str(src), str(clone)], check=True)
        own = filled(self.root / "wp-400" / "build-own", mib=2)
        make_old(clone)
        make_old(own)
        rc, out, err = self.cleanup("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(self.row(out, "wp-400/build-clone").split()[:3], ["2", "0", "REMOVE"])
        self.assertEqual(self.row(out, "wp-400/build-own").split()[:3], ["2", "2", "REMOVE"])
        self.assertIn("4 MB apparent size (clones share blocks) · estimated real gain ≥ 2 MB", out)

    def test_GercekKazanc_OkunamayanAgac_NaTahminYok(self):
        d = filled(self.t / "na" / "sub")
        os.chmod(d, 0)
        r = subprocess.run([sys.executable, str(self.t / "tools" / "apfs_real_gain.py"), str(self.t / "na")],
                           capture_output=True, text=True)
        self.assertEqual(r.stdout.split("\t")[0], "n/a", r.stdout)

    # ---- keep-guard -----------------------------------------------------------------------------------------------
    def test_Koruma_DdKeepDosyasi_Tutulur(self):
        d = filled(self.root / "wp-300" / "build-keep")
        (d / ".dd-keep").write_text("")
        make_old(d)
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("keep (.dd-keep)", self.row(out, "wp-300/build-keep"))
        self.assertTrue(d.exists())

    def test_Koruma_UstKlasordeDdKeep_PaketinTumAgaciniKorur(self):
        # a package protects everything below it with one wp-NNN/.dd-keep (e.g. a worktree whose Unity Library is reused)
        d = filled(self.root / "wp-301" / "build-a")
        (self.root / "wp-301" / ".dd-keep").write_text("")
        make_old(d)
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("keep (.dd-keep in ", self.row(out, "wp-301/build-a"))
        self.assertTrue(d.exists())

    def test_Koruma_Son24Saat_TutulurEskiSilinir(self):
        new = filled(self.root / "wp-300" / "build-new")
        old = filled(self.root / "wp-300" / "build-old")
        make_old(new)
        os.utime(new / "data.bin", None)  # one fresh file deep inside is enough
        make_old(old)
        subprocess.run(["touch", "-t", "202001010000", str(old / "data.bin")], check=True)
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("keep (recent: data.bin", self.row(out, "wp-300/build-new"))
        self.assertIn("REMOVE", self.row(out, "wp-300/build-old"))
        self.assertTrue(new.exists())
        self.assertFalse(old.exists())

    def test_Koruma_KomutSatirindaKlasor_KullanimdaTutulur(self):
        d = filled(self.root / "wp-300" / "build-busy")
        make_old(d)
        p = self.sleeper("-c", "import time; time.sleep(120)", "dd-fixture-sleeper", str(d) + "/out.log")
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        self.assertIn(f"keep (in use: pid {p.pid})", self.row(out, "wp-300/build-busy"))
        self.assertTrue(d.exists())

    def test_Koruma_UnityBenzeriSurecCwdGoreliArguman_KullanimdaTutulur(self):
        d = filled(self.root / "wp-300" / "build-rel")
        make_old(d)
        exe = self.bin / "Unity.app" / "Contents" / "MacOS" / "Unity"
        exe.parent.mkdir(parents=True)
        os.symlink("/bin/sh", exe)
        # no -batchmode / -projectPath: invisible to dd_unity_count / dd_project_open of other sessions
        p = self.sleeper("-c", "sleep 120; :", "dd-fixture", "-logFile", "build-rel/editor.log", cwd=d.parent, exe=str(exe))
        rc, out, err = self.cleanup("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertIn(f"keep (in use: pid {p.pid})", self.row(out, "wp-300/build-rel"))

    def test_Koruma_SlotSahibininCalismaDizini_KullanimdaTutulur(self):
        d = filled(self.root / "wp-300" / "build-slot")
        make_old(d)
        p = self.sleeper("-c", "import time; time.sleep(120)", cwd=d)
        make_old(d)
        rc, out, err = self.cleanup("--dry-run")
        self.assertIn("REMOVE", self.row(out, "wp-300/build-slot"), "without a slot lock the cwd alone does not count")
        (self.slots / "unity").mkdir()
        (self.slots / "unity" / f"1.fixture.{p.pid}").write_text("2026-09-30T00:00:00Z fixture\nstart=x\n")
        rc, out, err = self.cleanup("--apply")
        self.assertEqual(rc, 0, out + err)
        self.assertIn(f"keep (in use: pid {p.pid})", self.row(out, "wp-300/build-slot"))
        self.assertTrue(d.exists())

    def test_Koruma_PsCalismaz_BilinmiyorTutulur(self):
        d = filled(self.root / "wp-300" / "build-old")
        make_old(d)
        (self.bin / "ps").write_text("#!/bin/sh\necho 'ps: simulated failure' >&2\nexit 1\n")
        (self.bin / "ps").chmod(0o755)
        env = dict(self.env, PATH=f"{self.bin}:{os.environ['PATH']}")
        rc, out, err = self.cleanup("--apply", env=env)
        self.assertEqual(rc, 0, out + err)
        self.assertIn("keep (unknown: process list unavailable", self.row(out, "wp-300/build-old"))
        self.assertTrue(d.exists())

    # ---- robustness -----------------------------------------------------------------------------------------------
    def test_Boyut_duBozukCikti_0NotluCokmez(self):
        self.merged_worktree("wp-100")
        filled(self.root / "worktree-x")
        (self.bin / "du").write_text("#!/bin/sh\necho 'garbage'\n")
        (self.bin / "du").chmod(0o755)
        env = dict(self.env, PATH=f"{self.bin}:{os.environ['PATH']}")
        for name, args in (("cleanup-merged.sh", ["--root", str(self.root), "--dry-run"]),
                           ("disk-report.sh", ["--root", str(self.root)])):
            rc, out, err = self.run_tool(name, *args, env=env)
            self.assertEqual(rc, 0, name + out + err)
            self.assertNotIn("bad math", out + err)
            self.assertIn("is not a number → 0", err)

    def test_Sozdizimi_zshN_IkiBetik(self):
        for name in ("cleanup-merged.sh", "disk-report.sh"):
            r = subprocess.run(["zsh", "-n", str(TOOLS / name)], capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, name + r.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
