#!/usr/bin/env python3
"""C-00 06.10 (R3; the R2 FINAL's z3 stop) + gt 07.10: a mono crash of a Unity run must never dirty the checkout.
dd_unity_watched (tools/dd-env.sh — test.sh, build.sh and compile-check.sh launch Unity through it) starts Unity in its
log's folder, and — because the Editor switches its cwd to the project folder, where all 26 of the H2 run's
mono_crash.mem.<pid>.* dumps landed — dd_crash_sweep moves this attempt's dumps from the -projectPath folder into
<log folder>/mono-crash/ after every attempt (normal end, watchdog, stall). The fake Unity behaves like the Editor: it
changes into -projectPath when given and drops a dump into its cwd. usage: python3 tools/tests/test_unity_cwd.py"""
import os, subprocess, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
FAKE = """#!/bin/sh
echo $$ >> "{pids}"
log=""; proj=""
while [ $# -gt 0 ]; do
  [ "$1" = -logFile ] && log="$2"
  [ "$1" = -projectPath ] && proj="$2"
  shift
done
[ -n "$log" ] && echo "fake unity started in $(pwd)" >> "$log"
[ -n "$proj" ] && cd "$proj" && case "$log" in /*) echo "fake unity project $(pwd)" >> "$log" ;; esac
: > mono_crash.mem.$$.1.blob
[ -f "{stall}" ] && sleep 30
exit 0
"""


class UnityCwdTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-unity-cwd-")
        self.root = Path(self.tmp.name).resolve()
        self.caller = self.root / "caller"; self.caller.mkdir()
        self.out = self.root / "out"; self.out.mkdir()
        self.project = self.root / "project"; self.project.mkdir()
        self.pids, self.stall = self.root / "pids.txt", self.root / "stall"
        self.fake = self.root / "Unity"
        self.fake.write_text(FAKE.format(pids=self.pids, stall=self.stall)); self.fake.chmod(0o755)

    def tearDown(self):
        for line in (self.pids.read_text().split() if self.pids.exists() else []):
            try:
                os.kill(int(line), 9)   # only the fake's own pids
            except (ProcessLookupError, ValueError):
                pass
        self.tmp.cleanup()

    def watched(self, log, project=True, project_arg=None, exe=None, **env):
        e = dict(os.environ, DD_TIMELINE="", **env)
        extra = ["-projectPath", project_arg or str(self.project)] if project else []
        r = subprocess.run(["zsh", "-c", 'source "$1"; shift; log=$1; shift; dd_unity_watched "$log" 0 "$@" -logFile "$log"; echo "rc=$?"',
                            "zsh", str(TOOLS / "dd-env.sh"), log, exe or str(self.fake), "-batchmode", *extra],
                           cwd=self.caller, env=e, capture_output=True, text=True, timeout=90)
        return r.stdout + r.stderr

    def dumps(self, d):
        return sorted(p.name for p in d.glob("mono_crash.*")) if d.exists() else []

    def test_ProjeKlasorundekiDokum_LogunYaninaTasinir_CheckoutTemiz(self):
        out = self.watched(str(self.out / "playmode.log"))
        self.assertIn("rc=0", out)
        self.assertEqual([], self.dumps(self.project), out)          # the project (in the checkout) stays clean
        self.assertEqual(1, len(self.dumps(self.out / "mono-crash")), out)
        self.assertEqual([], self.dumps(self.caller))
        self.assertIn("mono_crash dump(s) of pid", out)
        self.assertIn("fake unity started in %s" % self.out, (self.out / "playmode.log").read_text())

    def test_EskiYabanciDokum_ProjedeKalir(self):
        old = self.project / "mono_crash.mem.99999.1.blob"
        old.write_text(""); past = time.time() - 3600; os.utime(old, (past, past))
        self.watched(str(self.out / "playmode.log"))
        self.assertEqual([old.name], self.dumps(self.project))       # not this run's: neither its pid nor written since
        self.assertEqual(1, len(self.dumps(self.out / "mono-crash")))

    def test_GoreliLog_DokumYineLogunYanina(self):
        (self.caller / "rel").mkdir()
        out = self.watched("rel/playmode.log")
        self.assertIn("rc=0", out)
        self.assertEqual([], self.dumps(self.project), out)
        self.assertEqual(1, len(self.dumps(self.caller / "rel" / "mono-crash")), out)

    def test_GoreliProjectPath_CagiraninCwdsineGoreAcilir(self):
        """gt 07.10 SHOULD: a relative -projectPath (a relative DD_REPO) is made absolute against the CALLER's cwd before
        Unity starts in the log's folder — the right project opens and its dump is swept from there."""
        rel = self.caller / "relproj"; rel.mkdir()
        out = self.watched(str(self.out / "playmode.log"), project_arg="relproj")
        self.assertIn("rc=0", out)
        self.assertIn("fake unity project %s" % rel, (self.out / "playmode.log").read_text())
        self.assertEqual([], self.dumps(rel), out)
        self.assertEqual(1, len(self.dumps(self.out / "mono-crash")), out)
        self.assertEqual([], self.dumps(self.out))

    def test_GoreliUnityYolu_CagiraninCwdsineGoreCalisir(self):
        """gt 07.10 SHOULD: a relative executable (a relative DD_UNITY) still starts after the cd into the log's folder."""
        (self.caller / "bin").mkdir()
        exe = self.caller / "bin" / "Unity"
        exe.write_text(self.fake.read_text()); exe.chmod(0o755)
        out = self.watched(str(self.out / "playmode.log"), exe="bin/Unity")
        self.assertIn("rc=0", out)
        self.assertIn("fake unity project %s" % self.project, (self.out / "playmode.log").read_text())
        self.assertEqual(1, len(self.dumps(self.out / "mono-crash")), out)

    def test_ProjeYok_DokumLogKlasorunde(self):
        out = self.watched(str(self.out / "playmode.log"), project=False)
        self.assertIn("rc=0", out)
        self.assertEqual(1, len(self.dumps(self.out)), out)             # started in the log's folder
        self.assertEqual([], self.dumps(self.caller))

    def test_Takilma_GercekPidOldurulur_IkiDenemeninDokumuTasinir(self):
        """`( cd … && exec … ) &` — $! is the fake Unity itself: the stall watchdog stops it (124 after one retry); both
        attempts' dumps are swept."""
        self.stall.write_text("")
        out = self.watched(str(self.out / "playmode.log"), DD_STALL_SEC="2", DD_STALL_TICK="1")
        self.assertIn("rc=124", out)
        pids = [int(x) for x in self.pids.read_text().split()]
        self.assertEqual(2, len(pids))
        for pid in pids:
            with self.assertRaises(ProcessLookupError):
                os.kill(pid, 0)
        self.assertEqual([], self.dumps(self.project), out)
        self.assertEqual(sorted("mono_crash.mem.%d.1.blob" % p for p in pids), self.dumps(self.out / "mono-crash"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
