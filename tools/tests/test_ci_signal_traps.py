#!/usr/bin/env python3
"""Codex-audit Y1 / G4 Q10 (C-00 30.09) · tools/main-ci.sh + tools/compile-check.sh (+ tools/test.sh) on INT/TERM/HUP:
exit 130/143/129 through the one EXIT cleanup (guard restore once, player slot + lease released); a child that survives
TERM for DD_CI_CHILD_GRACE s leaves the user-data window open (no restore, slot + lease kept, "tell C-00").
Fully fake: copied tools in a tempdir, fake git repo + detached CI worktree, fake slot.sh / userdata-guard.sh / test.sh /
Unity, HOME inside the tempdir. No Unity, no Player, no real slot dir, no real user data.
Codex-audit M-12 (C-00 30.09): the scripts run from a DRIVER that is a detached git worktree of the fixture repo whose `main`
carries these tools (the same general code path as the real driver — no test-only bypass, H11); CiDriverGuardTests covers
the driver check (M-12), dd_project_open (M-06), the editor-scope guard lock wait, main-ci's 75 retry into a new guard
directory and dd_slot_* exit codes (M-07).
usage: python3 tools/tests/test_ci_signal_traps.py"""
import json, os, shutil, signal, subprocess, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
COPY = ["main-ci.sh", "compile-check.sh", "dd-env.sh", "ci_summary.py", "ci-owners.tsv", "ci-signatures.tsv", "ci-suites.txt"]
PRODUCT = "Deaths Demolition - Playable Concept"

FAKE_SLOT = """#!/bin/zsh
echo "$1 $2 $3" >> "{log}/slot-calls.txt"; echo "SLOT $2#1 -> $3"
"""
# Like the real guard: one snapshot per directory (64 when <dir>/snap exists). FAKE_GUARD_75_ONCE: the first snapshot
# leaves a partial <dir>/snap and returns 75 (live data kept changing), later ones succeed; FAKE_GUARD_ALWAYS_75: every one.
FAKE_GUARD = """#!/bin/zsh
cmd=$1 dir=$2; echo "$cmd" >> "{log}/guard-calls.txt"; echo "$cmd $dir" >> "{log}/guard-dirs.txt"
if [ "$cmd" = snapshot ]; then
  [ -e "$dir/snap" ] && {{ echo "$dir/snap exists — one snapshot per directory" >&2; exit 64; }}
  mkdir -p "$dir/snap"
  [ -n "${{FAKE_GUARD_ALWAYS_75:-}}" ] && {{ echo busy; exit 75; }}
  if [ -n "${{FAKE_GUARD_75_ONCE:-}}" ] && [ ! -e "{log}/guard-75-done" ]; then : > "{log}/guard-75-done"; echo busy; exit 75; fi
  echo x > "$dir/before-editor.txt"; echo "snapshot scope=editor ok"
else
  if [ -n "${{FAKE_GUARD_SLOW:-}}" ]; then echo $$ > "{log}/guard-restore.pid"; sleep "$FAKE_GUARD_SLOW"; fi
  cp "$dir/before-editor.txt" "$dir/after-run-editor.txt"; echo restore-done >> "{log}/guard-calls.txt"
  echo "restore scope=editor changed=0 verified=1"
fi
"""
# FAKE_MODE: sleep (dies on TERM) · ignore (ignores TERM) · orphan (dies, but leaves a grandchild that ignores TERM) · pass
#            · stray (passes, but leaves a detached "Unity" with -projectPath on the CI checkout — not in main-ci's tree)
FAKE_TEST_SH = """#!/bin/zsh
mode=$1; shift; out=""; while [ $# -gt 0 ]; do [ "$1" = --out ] && out=$2; shift; done
echo $$ > "{log}/child-$mode.pid"; env > "{log}/env-$mode.txt"
case "$FAKE_MODE" in
  pass|stray) mkdir -p "$out"; echo '<test-run><test-case name="A" fullname="F.A" result="Passed" duration="0.1"/></test-run>' > "$out/$mode.xml"
    if [ "$FAKE_MODE" = stray ] && [ "$mode" = editmode ]; then
      "$FAKE_STRAY_UNITY" -projectPath "$FAKE_STRAY_PROJECT" -batchmode </dev/null >/dev/null 2>&1 &!
      for i in {{1..50}}; do [ -s "{log}/stray-unity.pid" ] && break; sleep 0.1; done
    fi
    exit 0 ;;
  ignore) trap '' TERM; exec sleep 60 ;;
  orphan) ( trap '' TERM; exec sleep 60 ) & echo $! > "{log}/grandchild.pid"; exec sleep 60 ;;
  *) exec sleep 60 ;;
esac
"""
# FAKE_UNITY_MODE: pass (compiles) · sleep (dies on TERM) · ignore (ignores TERM)
FAKE_UNITY = """#!/bin/zsh
log=""; while [ $# -gt 0 ]; do [ "$1" = -logFile ] && log=$2; shift; done
echo "CompileScripts: fake" > "$log"; echo $$ > "{log}/unity.pid"
case "$FAKE_UNITY_MODE" in pass) exit 0 ;; ignore) trap '' TERM; exec sleep 60 ;; *) exec sleep 60 ;; esac
"""


def write_exec(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)
    path.chmod(0o755)


def alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    st = subprocess.run(["ps", "-o", "stat=", "-p", str(pid)], capture_output=True, text=True).stdout.strip()
    return bool(st) and not st.startswith("Z")


# C-00 30.09 disk gate: fixtures must not depend on the real machine's free disk / swap — df and sysctl report plenty.
def plenty_bin(root):
    d = Path(root) / "plenty-bin"
    d.mkdir(exist_ok=True)
    (d / "df").write_text("#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho '/dev/fixture 999999999 1 999999999'\n")
    (d / "sysctl").write_text("#!/bin/sh\n[ \"$2\" = vm.swapusage ] && { echo 'total = 1.00M  used = 0.00M  free = 1.00M'; exit 0; }\nexec /usr/sbin/sysctl \"$@\"\n")
    for f in ("df", "sysctl"):
        (d / f).chmod(0o755)
    return str(d)


class CiFixture(unittest.TestCase):
    """Shared fixture (no tests of its own)."""
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-ci-signal-")
        self.root = Path(self.tmp.name).resolve()
        self.log = self.root / "log"; self.log.mkdir()
        self.tools = None   # set by make_repo: <driver>/tools, a detached worktree of the fixture repo at main
        write_exec(self.root / "fake-unity", FAKE_UNITY.format(log=self.log))
        self.home = self.root / "home"
        pd = self.home / "Library/Application Support/Deaths Demolition" / PRODUCT
        pd.mkdir(parents=True); (pd / "profile.json").write_text("{}")
        self.repo = self.root / "repo"; self.ci = self.root / "ci"; self.out = self.root / "out"; self.driver = self.root / "driver"
        self.tmpdir = self.root / "t"; self.tmpdir.mkdir()
        self.pids = []; self.procs = []

    def tearDown(self):
        for pid in self.pids + [int(p.read_text()) for p in self.log.glob("*.pid") if p.read_text().strip()]:
            if alive(pid):
                os.kill(pid, signal.SIGKILL)  # only fixture processes this test started
        for p in self.procs:
            p.wait(timeout=10)
        self.tmp.cleanup()

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        e.update(HOME=str(self.home), TMPDIR=str(self.tmpdir), DD_REPO=str(self.repo), DD_UNITY=str(self.root / "fake-unity"),
                 DD_STALL_TICK="1", DD_CI_CHILD_GRACE="10", GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t",
                 GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t", GIT_CONFIG_NOSYSTEM="1")
        e["PATH"] = plenty_bin(self.root) + ":" + e.get("PATH", "/usr/bin:/bin")
        e.update(extra)
        return e

    def git(self, *args, cwd=None):
        return subprocess.run(["git", *args], cwd=cwd or self.repo, env=self.env(), check=True, capture_output=True,
                              text=True).stdout.strip()

    def make_repo(self, real_test_sh=False):
        """Fake main repo whose `main` carries the tools under test (copied scripts + fake slot.sh / userdata-guard.sh and
        the CI checkout's test.sh), a detached CI worktree and a detached DRIVER worktree at main (Codex-audit M-12): the
        scripts run from <driver>/tools. real_test_sh: the checkout carries the REAL test.sh/dd-env.sh."""
        self.repo.mkdir()
        self.git("init", "-q", "-b", "main")
        (self.repo / "prototype/DeathsDemolition_PlayableConcept").mkdir(parents=True)
        (self.repo / "prototype/DeathsDemolition_PlayableConcept/.keep").write_text("")
        # Codex-audit M-10: the C# of the commit under test decides what main-ci scrubs (dd_scrub_code_env)
        (self.repo / "prototype/DeathsDemolition_PlayableConcept/Assets").mkdir()
        (self.repo / "prototype/DeathsDemolition_PlayableConcept/Assets/Probe.cs").write_text(
            'class P { string a = System.Environment.GetEnvironmentVariable("DD_X");\n'
            '  string b = System.Environment.GetEnvironmentVariable("HOME");\n'
            '  string c = System.Environment.GetEnvironmentVariable("DD_WP076_PROBE"); }\n')
        tools = self.repo / "tools"; tools.mkdir()
        for name in COPY:
            shutil.copy2(TOOLS / name, tools / name)
        write_exec(tools / "slot.sh", FAKE_SLOT.format(log=self.log))
        write_exec(tools / "userdata-guard.sh", FAKE_GUARD.format(log=self.log))
        if real_test_sh:
            for name in ("test.sh", "dd-env.sh", "test-durations.py"):
                shutil.copy2(TOOLS / name, tools / name)
        else:
            write_exec(tools / "test.sh", FAKE_TEST_SH.format(log=self.log))
        self.git("add", ".")
        self.git("commit", "-q", "-m", "fixture")
        self.sha = self.git("rev-parse", "HEAD")
        self.git("worktree", "add", "-q", "--detach", str(self.ci), self.sha)
        self.git("worktree", "add", "-q", "--detach", str(self.driver), "main")
        self.tools = self.driver / "tools"
        (self.ci / "prototype/DeathsDemolition_PlayableConcept/Library").mkdir()  # no seed search

    def start(self, argv, **env):
        p = subprocess.Popen(["zsh", *argv], env=self.env(**env), stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True, start_new_session=True)
        self.pids.append(p.pid)
        return p

    def wait_file(self, path, seconds=30):
        end = time.time() + seconds
        while time.time() < end:
            if path.exists() and path.read_text().strip():
                return int(path.read_text())
            time.sleep(0.1)
        self.fail(f"{path.name} did not appear")

    def finish(self, p, seconds=60):
        out, err = p.communicate(timeout=seconds)
        return p.returncode, out, err

    def lines(self, name):
        f = self.log / name
        return f.read_text().splitlines() if f.exists() else []

    def leases(self):
        return list(self.root.glob(".dd-ci-*.lease"))

    def main_ci(self, mode="sleep", real_test_sh=False, playmode=False, prepare=None, **env):
        self.make_repo(real_test_sh)
        if prepare:
            prepare()
        return self.start([str(self.tools / "main-ci.sh"), self.sha, "--out-root", str(self.out), "--ci-dir", str(self.ci),
                           *([] if playmode else ["--skip-playmode"])], FAKE_MODE=mode, **env)

    def compile_check(self, unity_mode, **env):
        free_kib = int(subprocess.run(["df", "-k", str(self.root)], capture_output=True, text=True).stdout.splitlines()[1].split()[3])
        if free_kib < 15728640 + 1048576:
            self.skipTest("compile-check needs ≥ 15 GiB free on the fixture disk")
        self.make_repo()
        return self.start([str(self.tools / "compile-check.sh"), self.sha, "--ci-dir", str(self.ci), "--out", str(self.out)],
                          FAKE_UNITY_MODE=unity_mode, **env)



class CiSignalTrapTests(CiFixture):
    # --- main-ci -------------------------------------------------------------------------------------------------------

    def assert_main_ci_signal(self, sig, code):
        p = self.main_ci()
        child = self.wait_file(self.log / "child-editmode.pid"); time.sleep(0.3)
        os.kill(p.pid, sig)
        rc, out, err = self.finish(p)
        self.assertEqual(rc, code, out + err)
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot", "restore", "restore-done"])
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "player"], ["release", "player"]])
        self.assertEqual(self.leases(), [])
        self.assertFalse(alive(child))
        self.assertIn(f"SIG{signal.Signals(sig).name[3:]} — exit {code} through cleanup", err)
        self.assertNotIn("window left open", out + err)

    def test_MainCi_SIGINT_Cikis130TekRestoreSlotVeLeaseBirakilir(self):
        self.assert_main_ci_signal(signal.SIGINT, 130)

    def test_MainCi_SIGTERM_Cikis143TekRestoreSlotVeLeaseBirakilir(self):
        self.assert_main_ci_signal(signal.SIGTERM, 143)

    def test_MainCi_SIGHUP_Cikis129TekRestoreSlotVeLeaseBirakilir(self):
        self.assert_main_ci_signal(signal.SIGHUP, 129)

    def assert_window_left_open(self, p, survivor_file):
        survivor = self.wait_file(self.log / survivor_file); time.sleep(0.3)
        os.kill(p.pid, signal.SIGTERM)
        rc, out, err = self.finish(p)
        self.assertEqual(rc, 143, out + err)
        self.assertIn(f"user-data window left open: child {survivor} still alive, tell C-00", err)
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot"])  # restore NOT called
        self.assertEqual([l.split()[0] for l in self.lines("slot-calls.txt")], ["acquire"])  # player slot not released
        self.assertEqual(len(self.leases()), 1)  # lease kept: the survivor still uses the checkout
        self.assertTrue(alive(survivor))

    def test_MainCi_CocukTERMeDirenir_Cikis143RestoreYokPencereAcikSatiri(self):
        self.assert_window_left_open(self.main_ci("ignore", DD_CI_CHILD_GRACE="2"), "child-editmode.pid")

    def test_MainCi_TestShOlurTorunYasar_RestoreYokPencereAcik(self):
        self.assert_window_left_open(self.main_ci("orphan", DD_CI_CHILD_GRACE="2"), "grandchild.pid")

    def test_MainCi_GercekTestSh_TERMUnityyeIletilir_Cikis143TekRestore(self):
        p = self.main_ci(real_test_sh=True, FAKE_UNITY_MODE="sleep")
        unity = self.wait_file(self.log / "unity.pid"); time.sleep(0.5)
        os.kill(p.pid, signal.SIGTERM)
        rc, out, err = self.finish(p)
        self.assertEqual(rc, 143, out + err)
        self.assertFalse(alive(unity))
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot", "restore", "restore-done"])
        calls = [l.split()[:2] for l in self.lines("slot-calls.txt")]
        self.assertEqual(sorted(calls), sorted([["acquire", "player"], ["acquire", "unity"], ["release", "unity"], ["release", "player"]]))
        self.assertEqual(self.leases(), [])

    def test_MainCi_RestoreSirasindaIkinciSinyal_GuardKesilmez_RestoreTamamlanir(self):
        # Y1b / judge MUST-1: INT/TERM/HUP are ignored while the guard restores, and the guard child inherits that.
        p = self.main_ci(FAKE_GUARD_SLOW="3")
        self.wait_file(self.log / "child-editmode.pid"); time.sleep(0.3)
        os.kill(p.pid, signal.SIGTERM)
        guard = self.wait_file(self.log / "guard-restore.pid")
        os.kill(guard, signal.SIGTERM); os.kill(p.pid, signal.SIGINT)   # a second Ctrl-C / group signal mid-restore
        rc, out, err = self.finish(p)
        self.assertEqual(rc, 143, out + err)
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot", "restore", "restore-done"])
        self.assertEqual(self.leases(), [])

    def test_MainCi_ProjeHalaAcikUnity_RestoreYok_PencereAcik_SlotVeLeaseTutulur(self):
        # Y1b / judge MUST-2: a Unity outside main-ci's tree still has the CI checkout open → nothing is written.
        stray = self.root / "Unity.app/Contents/MacOS/Unity"
        write_exec(stray, "#!/bin/zsh\necho $$ > \"%s/stray-unity.pid\"\nsleep 60\n:\n" % self.log)
        p = self.main_ci("stray", playmode=True, FAKE_STRAY_UNITY=str(stray),
                         FAKE_STRAY_PROJECT=str(self.ci / "prototype/DeathsDemolition_PlayableConcept"))
        rc, out, err = self.finish(p)
        self.wait_file(self.log / "stray-unity.pid")
        self.assertEqual(rc, 3, out + err)
        self.assertIn("a Unity still has", out + err)
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot"])
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "player"]])
        self.assertEqual(len(self.leases()), 1)

    def test_MainCi_NormalYol_Cikis0OzetYesilTekRestore(self):
        p = self.main_ci("pass", playmode=True)  # green needs both XMLs
        rc, out, err = self.finish(p)
        self.assertEqual(rc, 0, out + err)
        self.assertIn("GREEN · edit 1/1 · play 1/1", out)
        self.assertTrue((self.log / "child-playmode.pid").exists())
        summary = json.loads((self.out / self.sha / "summary.json").read_text())
        # Codex-audit M-12: the driver's HEAD and tools tree are recorded (driver = detached worktree at main = self.sha)
        self.assertEqual(summary["driverHead"], self.sha)
        self.assertEqual(summary["toolsTree"], self.git("rev-parse", "main:tools"))
        self.assertIn(summary["toolsTree"], (self.out / self.sha / "summary.md").read_text())
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot", "restore", "restore-done"])
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "player"], ["release", "player"]])
        self.assertEqual(self.leases(), [])
        self.assertNotIn("through cleanup", err)

    # --- Codex-audit M-10: env scrub right before the tests -------------------------------------------------------------
    def assert_scrubbed(self, extra, cleared):
        rc, out, err = self.finish(self.main_ci("pass", playmode=True, DD_KEEP_ME="k", **extra))
        self.assertEqual(rc, 0, out + err)
        for mode in ("editmode", "playmode"):
            seen = dict(l.split("=", 1) for l in self.lines(f"env-{mode}.txt") if "=" in l)
            for name in extra:
                self.assertNotIn(name, seen, f"{mode} child still sees {name}")
            self.assertEqual(str(self.home), seen["HOME"])       # allow-listed although the C# reads it
            self.assertEqual("k", seen["DD_KEEP_ME"])            # not read by the C#: untouched
        summary = json.loads((self.out / self.sha / "summary.json").read_text())
        self.assertEqual(summary["envScrubbed"], cleared)
        md = (self.out / self.sha / "summary.md").read_text()
        self.assertIn("Ortam temizliği (env-scrub, C# okuduğu değişkenler): " + (", ".join(cleared) or "temizlenecek yok"), md)
        timeline = (self.out / self.sha / "timeline.txt").read_text()
        if cleared:
            self.assertIn(f"env-scrub: cleared {len(cleared)} ({','.join(cleared)}) from {self.ci}", timeline)
        else:
            self.assertIn("env-scrub: nothing to clear (3 names read by C#)", timeline)

    def test_M10_DD_XIleBaslatilir_TestCocuguGormez_HomeGorur_OzetteDD_X(self):
        self.assert_scrubbed({"DD_X": "1"}, ["DD_X"])

    def test_M10_GercekAdDD_WP076_PROBE_KodOkudugu_Temizlenir(self):
        self.assert_scrubbed({"DD_X": "1", "DD_WP076_PROBE": "1"}, ["DD_WP076_PROBE", "DD_X"])

    def test_M10_HicbiriAyarliDegil_OzetteBosListe(self):
        self.assert_scrubbed({}, [])

    def test_M10_GitGrepHatasi_Cikis3_TestKosmaz_GuardSlotLeaseKapanir(self):
        real_git = shutil.which("git")
        wrap = self.root / "grep-fails-bin"; wrap.mkdir()
        write_exec(wrap / "git", '#!/bin/sh\nfor a in "$@"; do [ "$a" = grep ] && { echo "fatal: fixture git grep failure" >&2; exit 128; }; done\n'
                                 'exec "%s" "$@"\n' % real_git)
        rc, out, err = self.finish(self.main_ci("pass", DD_X="1", PATH=f"{wrap}:{self.env()['PATH']}"))
        self.assertEqual(rc, 3, out + err)
        self.assertIn("env-scrub: git grep failed in", err)
        self.assertFalse((self.log / "child-editmode.pid").exists())   # never run unscrubbed
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot", "restore", "restore-done"])
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "player"], ["release", "player"]])
        self.assertEqual(self.leases(), [])

    # --- compile-check -------------------------------------------------------------------------------------------------
    def assert_compile_signal(self, sig, code):
        p = self.compile_check("sleep")
        unity = self.wait_file(self.log / "unity.pid"); time.sleep(0.3)
        os.kill(p.pid, sig)
        rc, out, err = self.finish(p)
        self.assertEqual(rc, code, out + err)
        self.assertFalse(alive(unity))
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "unity"], ["release", "unity"]])
        self.assertEqual(self.leases(), [])
        self.assertNotIn("RESULT:", out)

    def test_CompileCheck_SIGTERM_Cikis143SlotVeLeaseBirakilir(self):
        self.assert_compile_signal(signal.SIGTERM, 143)

    def test_CompileCheck_SIGINT_Cikis130SlotVeLeaseBirakilir(self):
        self.assert_compile_signal(signal.SIGINT, 130)

    def test_CompileCheck_UnityTERMeDirenir_Cikis143SlotVeLeaseTutulur(self):
        p = self.compile_check("ignore", DD_CI_CHILD_GRACE="2")
        unity = self.wait_file(self.log / "unity.pid"); time.sleep(0.3)
        os.kill(p.pid, signal.SIGTERM)
        rc, out, err = self.finish(p)
        self.assertEqual(rc, 143, out + err)
        self.assertIn(f"Unity child {unity} still alive, tell C-00", err)
        self.assertEqual([l.split()[0] for l in self.lines("slot-calls.txt")], ["acquire"])
        self.assertEqual(len(self.leases()), 1)

    def test_CompileCheck_NormalYol_Cikis0Derlenir(self):
        rc, out, err = self.finish(self.compile_check("pass"))
        self.assertEqual(rc, 0, out + err)
        self.assertIn("compiles (errorsCS 0)", out)
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "unity"], ["release", "unity"]])
        self.assertEqual(self.leases(), [])


class CiDriverGuardTests(CiFixture):
    """Codex-audit M-12 / M-06 / M-07 (C-00 30.09)."""
    # --- M-12: driver = the git worktree holding main-ci.sh: clean, detached, tools tree == main's ----------------------
    def assert_driver_refused(self, prepare, why):
        rc, out, err = self.finish(self.main_ci("pass", prepare=prepare))
        self.assertEqual(rc, 3, out + err)
        self.assertIn(f"main-ci: driver {self.driver} refused — {why}", err)
        self.assertIn(f"fix: git -C '{self.driver}' checkout -q --detach main", err)
        self.assertEqual(self.lines("guard-calls.txt"), [])     # nothing taken: no guard, no slot, no lease, no test
        self.assertEqual(self.lines("slot-calls.txt"), [])
        self.assertEqual(self.leases(), [])
        self.assertFalse((self.log / "child-editmode.pid").exists())

    def test_M12_SurucuKirli_Cikis3HicbirSeyAlinmaz(self):
        self.assert_driver_refused(
            lambda: (self.driver / "prototype/DeathsDemolition_PlayableConcept/.keep").write_text("dirty"), "worktree not clean")

    def test_M12_SurucuDaldaDetachedDegil_Cikis3(self):
        self.assert_driver_refused(lambda: self.git("switch", "-q", "-c", "driver-side", cwd=self.driver),
                                   "HEAD is on branch driver-side, not detached")

    def test_M12_SurucuToolsMainToolsDegil_Cikis3(self):
        def main_moves_on():   # main gets a new tools tree; the driver stays clean + detached on the old commit
            (self.repo / "tools/extra.txt").write_text("new on main")
            self.git("add", "tools/extra.txt"); self.git("commit", "-q", "-m", "main moves")
        self.assert_driver_refused(main_moves_on, "tools tree")
        self.assertNotEqual(self.git("rev-parse", "main:tools"), self.git("rev-parse", "HEAD:tools", cwd=self.driver))

    # --- M-07(b): 75 → retry into a NEW guard directory; the restore uses that directory ------------------------------
    def test_M07_Snapshot75BirKez_YeniDizinleDevam_OnuRestoreEder(self):
        rc, out, err = self.finish(self.main_ci("pass", playmode=True, FAKE_GUARD_75_ONCE="1"))
        self.assertEqual(rc, 0, out + err)
        ug = self.out / self.sha / "userdata-guard"
        self.assertEqual(self.lines("guard-dirs.txt"), [f"snapshot {ug}", f"snapshot {ug}.try2", f"restore {ug}.try2"])
        self.assertIn("snapshot75 (try 1/3", out)
        self.assertTrue((Path(f"{ug}.try2") / "after-run-editor.txt").exists())
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "player"], ["release", "player"]])
        self.assertEqual(self.leases(), [])

    def test_M07_Snapshot75Surer_UcDenemeUcDizin_75TestYokRestoreYok(self):
        rc, out, err = self.finish(self.main_ci("pass", FAKE_GUARD_ALWAYS_75="1"))
        self.assertEqual(rc, 75, out + err)
        ug = self.out / self.sha / "userdata-guard"
        self.assertEqual(self.lines("guard-dirs.txt"), [f"snapshot {ug}", f"snapshot {ug}.try2", f"snapshot {ug}.try3"])
        self.assertIn("userdata snapshot exit 75 (try 3/3", out)
        self.assertFalse((self.log / "child-editmode.pid").exists())
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "player"], ["release", "player"]])
        self.assertEqual(self.leases(), [])

    # --- M-06: dd_project_open (dd-env.sh, shared by main-ci + compile-check) -----------------------------------------
    def env_tools(self, slot_body=None):
        tools = self.root / "envtools"; tools.mkdir(exist_ok=True)
        shutil.copy2(TOOLS / "dd-env.sh", tools / "dd-env.sh")
        if slot_body is not None:
            write_exec(tools / "slot.sh", slot_body)
        return tools

    def zsh_env(self, script, *args):
        r = subprocess.run(["zsh", "-c", 'DD_TIMELINE=""; source "$1"; shift; ' + script, "zsh",
                            str(self.env_tools() / "dd-env.sh"), *args], env=self.env(), capture_output=True, text=True, timeout=60)
        return r.returncode, r.stdout, r.stderr

    def background(self, argv):
        p = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                             start_new_session=True)
        self.pids.append(p.pid); self.procs.append(p)
        return p

    def fake_unity_process(self, *argv):
        exe = self.root / "Hub/Editor/Unity.app/Contents/MacOS/Unity"
        write_exec(exe, '#!/bin/zsh\necho $$ > "%s/fake-hub-unity.pid"\nsleep 60\n:\n' % self.log)
        p = self.background([str(exe), *argv])
        self.wait_file(self.log / "fake-hub-unity.pid")
        return p

    def test_M06_HubKucukHarfProjectpath_AcikSayilir(self):
        project = self.root / "hub-project"; project.mkdir()
        self.fake_unity_process("-projectpath", str(project), "-useHub", "-hubIPC")
        rc, out, err = self.zsh_env('dd_project_open "$1"', str(project))
        self.assertEqual(rc, 0, err)

    def test_M06_VarolmayanYabanciYol_AtlanirLoglanir_BizimkiAcikDegil(self):
        project = self.root / "our-project"; project.mkdir()
        gone = self.root / "removed-worktree/prototype/DeathsDemolition_PlayableConcept"
        self.fake_unity_process("-projectPath", str(gone), "-batchmode")
        rc, out, err = self.zsh_env('dd_project_open "$1"', str(project))
        self.assertEqual(rc, 1, err)
        self.assertIn(f"dd_project_open: skipped a Unity projectPath that does not exist: {gone}", err)

    def test_M06_GoreceliProjectPath_Mesgul2(self):
        # M-12 review SHOULD-1: a relative -projectPath cannot be matched → busy (2), never "not open".
        project = self.root / "our-project"; project.mkdir()
        self.fake_unity_process("-projectPath", "prototype/DeathsDemolition_PlayableConcept", "-batchmode")
        rc, out, err = self.zsh_env('dd_project_open "$1"', str(project))
        self.assertEqual(rc, 2, err)
        self.assertIn("has a relative projectPath", err)

    def test_M06_BeklenmeyenHata_AcikDegilDemez_Mesgul2(self):
        # M-12 review MUST-1: an unexpected error (PermissionError from exists() under an unreadable dir) → 2, not 1.
        project = self.root / "our-project"; project.mkdir()
        locked = self.root / "locked"; locked.mkdir()
        self.fake_unity_process("-projectPath", str(locked / "inner/project"), "-batchmode")
        locked.chmod(0o000)
        try:
            rc, out, err = self.zsh_env('dd_project_open "$1"', str(project))
        finally:
            locked.chmod(0o755)
        self.assertEqual(rc, 2, err)
        self.assertIn("treating the checkout as busy", err)

    # --- M-07(c): dd_slot_acquire / dd_slot_release return slot.sh's exit code -----------------------------------------
    def test_M07_DdSlotAcquireRelease_SlotShCikisKodunuDondurur(self):
        self.env_tools('#!/bin/zsh\n[ "$1" = acquire ] && { echo "SLOT refused"; exit 75; }\nexit 5\n')
        rc, out, err = self.zsh_env('dd_slot_acquire player; print "acquire=$?"; dd_slot_release player; print "release=$?"')
        self.assertIn("acquire=75", out, err)
        self.assertIn("release=5", out, err)
        self.assertIn("slot: acquire player failed (slot.sh exit 75)", out)
        self.env_tools('#!/bin/zsh\necho "SLOT ok"; exit 0\n')
        rc, out, err = self.zsh_env('dd_slot_acquire unity; print "acquire=$?"; dd_slot_release unity; print "release=$?"')
        self.assertIn("acquire=0", out, err)
        self.assertIn("release=0", out, err)
        self.assertNotIn("failed", out + err, "M-12 code review SHOULD: success must not log 'release … failed' (empty DD_TIMELINE)")

    def test_M07_OncekiBasarisiz75Denemesi_YanlisBeklemeyeYolAcmaz_BasariliPencere64(self):
        # M-12 code review MUST: a failed 75 try leaves before.txt but no players.log → not an open window; a SUCCESSFUL
        # earlier snapshot (players.log) without after.txt → 64, nothing taken.
        self.make_repo()
        ug = self.out / self.sha / "userdata-guard.try2"; ug.mkdir(parents=True)
        (ug / "before.txt").write_text("x\n")
        rc, out, err = self.finish(self.start([str(self.tools / "main-ci.sh"), self.sha, "--out-root", str(self.out),
                                              "--ci-dir", str(self.ci), "--skip-playmode"], FAKE_MODE="pass"))
        self.assertNotIn("unrestored guard window", out + err)
        (ug / "players.log").write_text("")
        self.git("worktree", "remove", "--force", str(self.ci))   # a second run on the same fixture needs a fresh CI checkout
        self.git("worktree", "add", "-q", "--detach", str(self.ci), self.sha)
        (self.ci / "prototype/DeathsDemolition_PlayableConcept/Library").mkdir()
        rc, out, err = self.finish(self.start([str(self.tools / "main-ci.sh"), self.sha, "--out-root", str(self.out),
                                              "--ci-dir", str(self.ci), "--skip-playmode"], FAKE_MODE="pass"))
        self.assertEqual(rc, 64, out + err)
        self.assertIn("unrestored guard window", out + err)

    # --- M-07(a): userdata-guard snapshot lock wait follows the scope -------------------------------------------------
    def run_guard(self, target, scope):
        gtools = self.root / "gtools"
        if not gtools.exists():
            gtools.mkdir()
            for name in ("userdata-guard.sh", "dd-env.sh", "userdata_attrib.py"):
                shutil.copy2(TOOLS / name, gtools / name)
            write_exec(gtools / "slot.sh", FAKE_SLOT.format(log=self.log))
        env = self.env(DD_GUARD_ROOT=str(self.root / "groot"), DD_GUARD_WAIT="1",
                       DD_GUARD_PLAYER_RE="dd-fixture-no-such-player-7f3c")   # never matches a real Player
        env.pop("DD_TIMELINE", None)
        r = subprocess.run(["zsh", str(gtools / "userdata-guard.sh"), "snapshot", str(target), "--scope", scope],
                           env=env, capture_output=True, text=True, timeout=120)
        (target / ".sampling").unlink(missing_ok=True)   # ends the guard's Player sampler loop (≤ one 5 s tick) —
        end = time.time() + 15                           # wait for it so the tempdir cleanup does not race its log
        while time.time() < end and subprocess.run(["pgrep", "-f", f"userdata-guard.sh snapshot {target} "],
                                                   capture_output=True).returncode == 0:
            time.sleep(0.5)
        return r.returncode, r.stdout + r.stderr

    def test_M07_GuardEditorKapsami_D2KilidiniBeklemez_PlayerKapsami75(self):
        d2 = self.root / "groot/AS/com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"; d2.mkdir(parents=True)
        (self.root / "groot/AS/Deaths Demolition" / PRODUCT).mkdir(parents=True)
        holder = self.root / "dd-fixture-lock-holder"
        write_exec(holder, "#!/bin/zsh\nsleep 60\n:\n")
        h = self.background([str(holder)]); time.sleep(0.3)
        (d2 / "prototype-profile.json.lock").write_text(f"pid={h.pid}\nname={holder}\n")   # lock_holder's format
        rc, text = self.run_guard(self.root / "ug-editor", "editor")
        self.assertEqual(rc, 0, text)
        self.assertNotIn("profile lock held", (self.root / "ug-editor/timeline.txt").read_text())
        rc, text = self.run_guard(self.root / "ug-player", "player")
        self.assertEqual(rc, 75, text)
        self.assertIn(f"profile lock still held by live pid {h.pid}", (self.root / "ug-player/timeline.txt").read_text())


if __name__ == "__main__":
    unittest.main(verbosity=2)
