#!/usr/bin/env python3
"""C-00 06.10 (R3; the R2 FINAL's z3 stop): dd_unity_watched (tools/dd-env.sh — test.sh, build.sh and compile-check.sh
launch Unity through it) runs Unity in its log's folder when the log path is absolute, so a mono crash's
mono_crash.* dumps land next to the run's log, never in the caller's cwd (the checkout root: 26 dumps from the H2 run
dirtied the FINAL tree). A fake Unity writes a dump into its cwd. usage: python3 tools/tests/test_unity_cwd.py"""
import os, subprocess, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
FAKE = """#!/bin/sh
echo $$ >> "{pids}"
: > mono_crash.mem.$$.1.blob
log=""; while [ $# -gt 0 ]; do [ "$1" = -logFile ] && log="$2"; shift; done
[ -n "$log" ] && echo "fake unity ran in $(pwd)" >> "$log"
[ -f "{stall}" ] && sleep 30
exit 0
"""


class UnityCwdTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-unity-cwd-")
        self.root = Path(self.tmp.name).resolve()
        self.caller = self.root / "caller"; self.caller.mkdir()
        self.out = self.root / "out"; self.out.mkdir()
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

    def watched(self, log, **env):
        e = dict(os.environ, DD_TIMELINE="", **env)
        r = subprocess.run(["zsh", "-c", 'source "$1"; dd_unity_watched "$2" 0 "$3" -batchmode -logFile "$2"; echo "rc=$?"',
                            "zsh", str(TOOLS / "dd-env.sh"), log, str(self.fake)],
                           cwd=self.caller, env=e, capture_output=True, text=True, timeout=60)
        return r.stdout + r.stderr

    def dumps(self, d):
        return sorted(p.name for p in d.glob("mono_crash.*"))

    def test_MutlakLog_CokmeDokumuLogunYaninda_CagiraninDizinindeDegil(self):
        out = self.watched(str(self.out / "playmode.log"))
        self.assertIn("rc=0", out)
        self.assertEqual(1, len(self.dumps(self.out)), out)
        self.assertEqual([], self.dumps(self.caller))
        self.assertIn("fake unity ran in %s" % self.out, (self.out / "playmode.log").read_text())

    def test_GoreliLog_EskiDavranis_CagiraninDizini(self):
        (self.caller / "rel").mkdir()
        out = self.watched("rel/playmode.log")
        self.assertIn("rc=0", out)
        self.assertEqual(1, len(self.dumps(self.caller)), out)

    def test_Takilma_GercekUnityPidiOldurulur(self):
        """`( cd … && exec … ) &` — $! is the fake Unity itself: the stall watchdog stops it (124 after one retry)."""
        self.stall.write_text("")
        t0 = time.time()
        out = self.watched(str(self.out / "playmode.log"), DD_STALL_SEC="2", DD_STALL_TICK="1")
        self.assertIn("rc=124", out)
        self.assertLess(time.time() - t0, 30)
        pids = [int(x) for x in self.pids.read_text().split()]
        self.assertEqual(2, len(pids))   # the run and its one retry
        for pid in pids:
            with self.assertRaises(ProcessLookupError):
                os.kill(pid, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
