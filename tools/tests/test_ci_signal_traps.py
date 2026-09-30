#!/usr/bin/env python3
"""Codex-audit Y1 / G4 Q10 (C-00 30.09) · tools/main-ci.sh + tools/compile-check.sh (+ tools/test.sh) on INT/TERM/HUP:
exit 130/143/129 through the one EXIT cleanup (guard restore once, player slot + lease released); a child that survives
TERM for DD_CI_CHILD_GRACE s leaves the user-data window open (no restore, slot + lease kept, "tell C-00").
Fully fake: copied tools in a tempdir, fake git repo + detached CI worktree, fake slot.sh / userdata-guard.sh / test.sh /
Unity, HOME inside the tempdir. No Unity, no Player, no real slot dir, no real user data.
usage: python3 tools/tests/test_ci_signal_traps.py"""
import os, shutil, signal, subprocess, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
COPY = ["main-ci.sh", "compile-check.sh", "dd-env.sh", "ci_summary.py", "ci-owners.tsv", "ci-signatures.tsv", "ci-suites.txt"]
PRODUCT = "Deaths Demolition - Playable Concept"

FAKE_SLOT = """#!/bin/zsh
echo "$1 $2 $3" >> "{log}/slot-calls.txt"; echo "SLOT $2#1 -> $3"
"""
FAKE_GUARD = """#!/bin/zsh
cmd=$1 dir=$2; echo "$cmd" >> "{log}/guard-calls.txt"
if [ "$cmd" = snapshot ]; then mkdir -p "$dir"; echo x > "$dir/before-editor.txt"; echo "snapshot scope=editor ok"
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
echo $$ > "{log}/child-$mode.pid"
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


class CiSignalTrapTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-ci-signal-")
        self.root = Path(self.tmp.name).resolve()
        self.log = self.root / "log"; self.log.mkdir()
        self.tools = self.root / "tools"; self.tools.mkdir()
        for name in COPY:
            shutil.copy2(TOOLS / name, self.tools / name)
        write_exec(self.tools / "slot.sh", FAKE_SLOT.format(log=self.log))
        write_exec(self.tools / "userdata-guard.sh", FAKE_GUARD.format(log=self.log))
        write_exec(self.root / "fake-unity", FAKE_UNITY.format(log=self.log))
        self.home = self.root / "home"
        pd = self.home / "Library/Application Support/Deaths Demolition" / PRODUCT
        pd.mkdir(parents=True); (pd / "profile.json").write_text("{}")
        self.repo = self.root / "repo"; self.ci = self.root / "ci"; self.out = self.root / "out"
        self.tmpdir = self.root / "t"; self.tmpdir.mkdir()
        self.pids = []

    def tearDown(self):
        for pid in self.pids + [int(p.read_text()) for p in self.log.glob("*.pid") if p.read_text().strip()]:
            if alive(pid):
                os.kill(pid, signal.SIGKILL)  # only fixture processes this test started
        self.tmp.cleanup()

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        e.update(HOME=str(self.home), TMPDIR=str(self.tmpdir), DD_REPO=str(self.repo), DD_UNITY=str(self.root / "fake-unity"),
                 DD_STALL_TICK="1", DD_CI_CHILD_GRACE="10", GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t",
                 GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t", GIT_CONFIG_NOSYSTEM="1")
        e.update(extra)
        return e

    def git(self, *args, cwd=None):
        subprocess.run(["git", *args], cwd=cwd or self.repo, env=self.env(), check=True, capture_output=True)

    def make_repo(self, real_test_sh=False):
        """Fake main repo + detached CI worktree. real_test_sh: the CI checkout carries the REAL test.sh/dd-env.sh."""
        self.repo.mkdir()
        self.git("init", "-q", "-b", "main")
        (self.repo / "prototype/DeathsDemolition_PlayableConcept").mkdir(parents=True)
        (self.repo / "prototype/DeathsDemolition_PlayableConcept/.keep").write_text("")
        ci_tools = self.repo / "tools"; ci_tools.mkdir()
        if real_test_sh:
            for name in ("test.sh", "dd-env.sh", "test-durations.py"):
                shutil.copy2(TOOLS / name, ci_tools / name)
            write_exec(ci_tools / "slot.sh", FAKE_SLOT.format(log=self.log))
        else:
            write_exec(ci_tools / "test.sh", FAKE_TEST_SH.format(log=self.log))
        self.git("add", ".")
        self.git("commit", "-q", "-m", "fixture")
        self.sha = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.repo, capture_output=True, text=True, check=True).stdout.strip()
        self.git("worktree", "add", "-q", "--detach", str(self.ci), self.sha)
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

    # --- main-ci -------------------------------------------------------------------------------------------------------
    def main_ci(self, mode="sleep", real_test_sh=False, playmode=False, **env):
        self.make_repo(real_test_sh)
        return self.start([str(self.tools / "main-ci.sh"), self.sha, "--out-root", str(self.out), "--ci-dir", str(self.ci),
                           *([] if playmode else ["--skip-playmode"])], FAKE_MODE=mode, **env)

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
        self.assertTrue((self.out / self.sha / "summary.json").exists())
        self.assertEqual(self.lines("guard-calls.txt"), ["snapshot", "restore", "restore-done"])
        self.assertEqual([l.split()[:2] for l in self.lines("slot-calls.txt")], [["acquire", "player"], ["release", "player"]])
        self.assertEqual(self.leases(), [])
        self.assertNotIn("through cleanup", err)

    # --- compile-check -------------------------------------------------------------------------------------------------
    def compile_check(self, unity_mode, **env):
        free_kib = int(subprocess.run(["df", "-k", str(self.root)], capture_output=True, text=True).stdout.splitlines()[1].split()[3])
        if free_kib < 15728640 + 1048576:
            self.skipTest("compile-check needs ≥ 15 GiB free on the fixture disk")
        self.make_repo()
        return self.start([str(self.tools / "compile-check.sh"), self.sha, "--ci-dir", str(self.ci), "--out", str(self.out)],
                          FAKE_UNITY_MODE=unity_mode, **env)

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


if __name__ == "__main__":
    unittest.main(verbosity=2)
