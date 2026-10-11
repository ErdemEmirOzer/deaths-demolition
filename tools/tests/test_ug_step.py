"""C-00 12.10 (queue fairness) · tools/ug-step.sh — one step in its own guard window — and tools/guard-queue.py — the
read-only queue report. Fixture HOME, a fake canonical guard, a fake oos_rule, a fixture ledger; nothing real is touched."""
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import dd_paths  # noqa: E402

FAKE_GUARD = r'''#!/bin/zsh
echo "$1 $2 $4" >> "$FAKE_LOG.guard"
if [ "$1" = snapshot ]; then
  mkdir -p "$2"; [ -n "${FAKE_SNAP_OPEN_RC:-}" ] && { : > "$2/before.txt"; exit "$FAKE_SNAP_OPEN_RC"; }
  [ -n "${FAKE_SNAP_FAIL_RC:-}" ] && exit "$FAKE_SNAP_FAIL_RC"   # failed before any window opened (no before.txt)
  if [ -n "${FAKE_SNAP_COMMIT_SLEEP:-}" ]; then   # the real guard after its commit point: TERM ignored, window open, exit 0
    trap '' INT TERM HUP; : > "$2/before.txt"; echo committing > "$FAKE_LOG.committing"; sleep "$FAKE_SNAP_COMMIT_SLEEP"; fi
  echo "userdata-guard snapshot → $2 root=real ${FAKE_KG:-kgSource=env} kg=abc"; exit 0; fi
# restore: whether the step (its pid file) is still alive, whether a late write landed, then the scripted exit code(s)
if [ -n "${FAKE_STEP_PID:-}" ] && [ -f "$FAKE_STEP_PID" ]; then kill -0 "$(cat "$FAKE_STEP_PID")" 2>/dev/null && echo "stepalive=1" >> "$FAKE_LOG.guard" || echo "stepalive=0" >> "$FAKE_LOG.guard"; fi
[ -n "${FAKE_LATE:-}" ] && { [ -f "$FAKE_LATE" ] && echo "late=1" >> "$FAKE_LOG.guard" || echo "late=0" >> "$FAKE_LOG.guard"; }
rc=${FAKE_RESTORE_RC:-0}
if [ -n "${FAKE_RESTORE_SEQ:-}" ]; then
  n=$(( $(cat "$FAKE_LOG.tries" 2>/dev/null || echo 0) + 1 )); echo $n > "$FAKE_LOG.tries"; rc=${${(s:,:)FAKE_RESTORE_SEQ}[$n]:-0}; fi
[ "$rc" = 75 ] && { echo "userdata-guard restore scope=$4 BUSY — a Player is running (75) retry=same-dir class=BUSY"; exit 75; }
echo "userdata-guard restore scope=$4 touched=0 ${FAKE_VERIFIED:-verified=1}"
exit $rc
'''

FAKE_OOS = r'''#!/usr/bin/env python3
import os, sys
with open(os.environ["FAKE_LOG"] + ".oos", "a") as f:
    f.write(sys.argv[1] + "\n")
sys.exit(int(os.environ.get("FAKE_OOS", "0")))
'''

KG = "Screenmanager Fullscreen mode=1;Screenmanager Resolution Width=1496"


class UgStepTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-ug-step-")
        self.root = Path(self.tmp.name).resolve()
        self.home = self.root / "home"
        self.kproj = self.home / dd_paths.KADRO_REL / "context/projects/deaths-demolition"
        tools = self.root / "repo" / "tools"
        (tools / "qa").mkdir(parents=True)
        for name in ("ug-step.sh", "dd-env.sh", "guard-queue.py", "userdata_guard_io.py"):
            shutil.copy2(TOOLS / name, tools / name)
        self.write(tools / "qa" / "oos_rule.py", FAKE_OOS)
        self.write(self.kproj / "wp-079/driver/tools/userdata-guard.sh", FAKE_GUARD)
        self.script = tools / "ug-step.sh"
        self.log = self.root / "calls"; self.log.write_text("")
        self.ledger = self.root / "ledger"; (self.ledger / "queue").mkdir(parents=True); (self.ledger / "windows").mkdir()
        self.ug = self.root / "ev" / "ug-em"

    def tearDown(self):
        self.tmp.cleanup()

    @staticmethod
    def write(p, text):
        p.parent.mkdir(parents=True, exist_ok=True); p.write_text(text); p.chmod(0o755)

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith(("DD_", "FAKE_"))}
        e.update(HOME=str(self.home), TMPDIR=str(self.root), FAKE_LOG=str(self.log), DD_GUARD_KNOWN_GOOD=KG)
        e.update(extra)
        return {k: v for k, v in e.items() if v is not None}

    def cmd(self, *extra, scope="editor", step=("sh", "-c", "echo ran > \"$STEP_OUT\"; exit ${STEP_RC:-0}")):
        return ["zsh", str(self.script), "--scope", scope, "--ug", str(self.ug), "--ledger", str(self.ledger), *extra, "--", *step]

    def run_step(self, *extra, scope="editor", step=None, **env):
        env.setdefault("STEP_OUT", str(self.root / "step.out"))
        args = self.cmd(*extra, scope=scope) if step is None else self.cmd(*extra, scope=scope, step=step)
        return subprocess.run(args, capture_output=True, text=True, env=self.env(**env), timeout=120)

    def guard(self):
        p = Path(str(self.log) + ".guard")
        return [l.split()[0] for l in p.read_text().splitlines() if l.split()[0] in ("snapshot", "restore")] if p.exists() else []

    def guard_notes(self):
        p = Path(str(self.log) + ".guard")
        return [l for l in p.read_text().splitlines() if "=" in l] if p.exists() else []

    def result(self):
        return json.loads(Path(str(self.ug) + ".result.json").read_text())

    def ran(self):
        return (self.root / "step.out").exists()

    def test_AdimKendiPenceresinde_KomutunCikisKodu(self):
        r = self.run_step()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual(["snapshot", "restore"], self.guard()); self.assertTrue(self.ran())
        self.assertIn("ug-step: done", (Path(str(self.ug) + "-timeline.txt")).read_text())
        shutil.rmtree(self.ug); (self.root / "step.out").unlink()
        self.assertEqual(3, self.run_step(STEP_RC="3").returncode, "the step's own exit code")

    def test_KgEnvYok_ya_da_DizinVar_Cikis64_HicbirSeyAlinmaz(self):
        self.assertEqual(64, self.run_step(DD_GUARD_KNOWN_GOOD=None).returncode)
        self.ug.mkdir(parents=True)
        self.assertEqual(64, self.run_step().returncode, "a fresh window folder per step")
        self.assertEqual([], self.guard()); self.assertFalse(self.ran())
        for bad in (["--warn-min", "0"], ["--scope", "all"]):
            with self.subTest(bad=bad):
                self.assertEqual(64, subprocess.run(["zsh", str(self.script), *bad, "--ug", str(self.root / "x"), "--", "true"],
                                                    capture_output=True, text=True, env=self.env()).returncode)

    def test_SnapshotKgDefault_AdimKosmaz_RestoreKosar_64(self):
        r = self.run_step(FAKE_KG="kgSource=default")
        self.assertEqual(64, r.returncode, r.stdout + r.stderr)
        self.assertEqual(["snapshot", "restore"], self.guard()); self.assertFalse(self.ran())

    def test_SnapshotHataPencereAcik_RestoreKosar_AdimKosmaz(self):
        r = self.run_step(FAKE_SNAP_OPEN_RC="31")
        self.assertEqual(31, r.returncode, r.stdout + r.stderr)
        self.assertEqual(["snapshot", "restore"], self.guard()); self.assertFalse(self.ran())

    def test_Editor32_YalnizOosRuleIle_Player32_Hata(self):
        self.assertEqual(0, self.run_step(FAKE_RESTORE_RC="32", FAKE_OOS="0").returncode, "editor 32 + oos_rule pass → 0")
        shutil.rmtree(self.ug)
        self.assertEqual(32, self.run_step(FAKE_RESTORE_RC="32", FAKE_OOS="1").returncode, "oos_rule refuses → 32")
        shutil.rmtree(self.ug)
        oos = Path(str(self.log) + ".oos"); before = oos.read_text().count("\n")
        self.assertEqual(32, self.run_step(scope="player", FAKE_RESTORE_RC="32").returncode, "player 32 is a failure")
        self.assertEqual(before, oos.read_text().count("\n"), "a player window never asks oos_rule")

    def test_VerifiedYok_31_KomutKoduBastirilir(self):
        r = self.run_step(FAKE_VERIFIED="verified=0", STEP_RC="0")
        self.assertEqual(31, r.returncode, "the guard's failure wins over the step's 0")

    def test_Adalet_EsikAsilincaKuyrukVarsa_UyariSatiri_KapatmaYok(self):
        ticket = {"id": "w2", "dir": "/x/w2", "owner": {"pid": os.getpid()}, "waiter": {"pid": os.getpid()},
                  "windowAreas": ["editor"], "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
        (self.ledger / "queue" / ("%d-w2.json" % time.time_ns())).write_text(json.dumps(ticket))
        step = ("sh", "-c", "sleep 4; echo ran > \"$STEP_OUT\"")
        seams = dict(DD_UG_STEP_MINUTE_SECONDS="1", DD_UG_STEP_POLL="1", DD_UG_STEP_CHECK_SECONDS="1")
        r = self.run_step("--warn-min", "1", step=step, **seams)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr); self.assertTrue(self.ran(), "warned, never stopped")
        tl = Path(str(self.ug) + "-timeline.txt").read_text()
        self.assertIn("FAIRNESS", tl); self.assertIn("with 1 waiting", tl); self.assertIn("FAIRNESS", r.stderr)
        self.assertLessEqual(tl.count("FAIRNESS"), 1, "at most one line per 10 (test) minutes")
        shutil.rmtree(self.ug); (self.root / "step.out").unlink(); Path(str(self.ug) + "-timeline.txt").unlink()
        for p in (self.ledger / "queue").glob("*.json"):
            p.unlink()
        self.run_step("--warn-min", "1", step=step, **seams)
        self.assertNotIn("FAIRNESS", Path(str(self.ug) + "-timeline.txt").read_text(), "nobody waits: no warning")
        # gt S-5: a ticket over the other area, or one whose waiter is dead, is nobody waiting for this window
        dead = subprocess.Popen(["true"]); dead.wait()
        for i, (areas, waiter) in enumerate(((["player"], os.getpid()), (["editor"], dead.pid))):
            t = dict(ticket, id="o%d" % i, windowAreas=areas, waiter={"pid": waiter})
            (self.ledger / "queue" / ("%d-o%d.json" % (time.time_ns(), i))).write_text(json.dumps(t))
        shutil.rmtree(self.ug); (self.root / "step.out").unlink(); Path(str(self.ug) + "-timeline.txt").unlink()
        self.run_step("--warn-min", "1", step=step, **seams)
        self.assertNotIn("FAIRNESS", Path(str(self.ug) + "-timeline.txt").read_text(), "other area / dead waiter: no warning")

    def test_Sinyal_AdimDurur_RestoreYineKosar(self):
        """gt S-4 (gM1): the step is stopped BEFORE the restore — the fake guard records it dead at the restore."""
        pidf = self.root / "step.pid"
        step = ("sh", "-c", "echo $$ > \"$STEP_PID\"; exec sleep 60")
        p = subprocess.Popen(self.cmd(step=step), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                             env=self.env(STEP_OUT=str(self.root / "step.out"), STEP_PID=str(pidf), FAKE_STEP_PID=str(pidf)))
        for _ in range(100):
            if Path(str(self.log) + ".guard").exists() and self.guard() == ["snapshot"]:
                break
            time.sleep(0.1)
        time.sleep(1.0)
        p.send_signal(signal.SIGTERM)
        out, err = p.communicate(timeout=60)
        self.assertEqual(143, p.returncode, out + err)
        self.assertEqual(["snapshot", "restore"], self.guard())
        self.assertIn("stepalive=0", self.guard_notes(), "the step was stopped before the restore")
        self.assertEqual("done", self.result()["restore"])

    def test_M1_SnapshotSonEvresindeSinyal_PencereYineRestoreEdilir(self):
        """gt M-1 (12.10): TERM while the snapshot is past its commit point (it ignores TERM, writes before.txt, exits 0) —
        the window is open although the snapshot never reported back: it is restored, not left to block the queue."""
        p = subprocess.Popen(self.cmd(), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                             env=self.env(STEP_OUT=str(self.root / "step.out"), FAKE_SNAP_COMMIT_SLEEP="3"))
        for _ in range(100):
            if Path(str(self.log) + ".committing").exists():
                break
            time.sleep(0.1)
        p.send_signal(signal.SIGTERM)
        out, err = p.communicate(timeout=60)
        self.assertEqual(143, p.returncode, out + err)
        self.assertEqual(["snapshot", "restore"], self.guard(), "the committed window is restored")
        self.assertFalse(self.ran(), "the step never started")
        self.assertEqual("done", self.result()["restore"])

    def test_gM6_PencereAcilmadanSnapshotHatasi_RestoreYok(self):
        r = self.run_step(FAKE_SNAP_FAIL_RC="65")
        self.assertEqual(65, r.returncode, r.stdout + r.stderr)
        self.assertEqual(["snapshot"], self.guard(), "no window was opened: nothing to restore")
        self.assertEqual("skipped", self.result()["restore"])

    def test_gM5_AdimVeGuardBasarisiz_GuardKoduKazanir(self):
        self.assertEqual(30, self.run_step(STEP_RC="3", FAKE_RESTORE_RC="30").returncode)

    def test_S1_KapaliOkuyucu_SIGPIPE_PencereyiAcikBirakmaz(self):
        """gt S-1a: `ug-step … 2>&1 | head -c 1` — the reader is gone after the first byte; ug-step's own FAIRNESS line in the
        middle of the step (stderr into the closed pipe) must not kill it with the window open."""
        ticket = {"id": "w2", "dir": "/x/w2", "owner": {"pid": os.getppid()}, "waiter": {"pid": os.getppid()},
                  "windowAreas": ["editor"], "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
        (self.ledger / "queue" / ("%d-w2.json" % time.time_ns())).write_text(json.dumps(ticket))
        step = "sh -c 'sleep 5; echo ran > \"$STEP_OUT\"'"
        cmd = " ".join("'%s'" % c for c in self.cmd("--warn-min", "1", step=("true",))[:-1]) + " " + step + " 2>&1 | head -c 1"
        env = self.env(STEP_OUT=str(self.root / "step.out"), DD_UG_STEP_MINUTE_SECONDS="1", DD_UG_STEP_POLL="1", DD_UG_STEP_CHECK_SECONDS="1")
        r = subprocess.run(["zsh", "-c", cmd], capture_output=True, text=True, env=env, timeout=60)
        self.assertIn("FAIRNESS", Path(str(self.ug) + "-timeline.txt").read_text(), "the mid-step write happened")
        self.assertEqual(["snapshot", "restore"], self.guard(), r.stdout + r.stderr)
        self.assertEqual({"rc": 0, "restore": "done"}, {k: self.result()[k] for k in ("rc", "restore")})
        self.assertTrue(self.ran())

    def test_S1_BozukTestDikisi_64_HicbirSeyAlinmaz_EsikOndalikKabul(self):
        for bad in ("0", "x"):
            with self.subTest(bad=bad):
                self.assertEqual(64, self.run_step(DD_UG_STEP_MINUTE_SECONDS=bad).returncode)
        self.assertEqual([], self.guard())
        self.assertEqual(0, self.run_step(DD_GUARD_FAIR_WARN_MIN="25.0").returncode, "25.0 is a number of minutes (gt NOTE-2)")
        shutil.rmtree(self.ug)
        self.assertEqual(0, self.run_step(DD_GUARD_FAIR_WARN_MIN="abc").returncode, "a bad env value falls back to 25, like the guard")

    def test_S2_AdimdanKalanTorun_RestoredanOnceBiter(self):
        """gt S-2: a grandchild the step left behind writes 3 s later — the restore waits for it."""
        late = self.root / "late.txt"
        step = ("sh", "-c", "(sleep 3; echo late > \"%s\") & sleep 1" % late)
        r = self.run_step(step=step, FAKE_LATE=str(late), DD_UG_STEP_POLL="1", DD_UG_STEP_DRAIN_SECONDS="20")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn("late=1", self.guard_notes(), "the late write landed before the restore")

    def test_S3_Restore75AyniDizin_YenidenDenenir(self):
        r = self.run_step(FAKE_RESTORE_SEQ="75,0", DD_UG_STEP_RETRY_SECONDS="1")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual(["snapshot", "restore", "restore"], self.guard())
        self.assertTrue(Path(str(self.ug) + ".restore.1.out").exists(), "the first answer is kept")
        shutil.rmtree(self.ug); Path(str(self.log) + ".tries").unlink(); Path(str(self.log) + ".guard").unlink()
        self.assertEqual(75, self.run_step(FAKE_RESTORE_SEQ="75,75,75,75,0", DD_UG_STEP_RETRY_SECONDS="1").returncode, "4 tries, then 75")
        self.assertEqual(4, self.guard().count("restore"))


class GuardQueueTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-guard-queue-")
        self.L = Path(self.tmp.name) / "ledger"
        for sub in ("windows", "queue", "closed"):
            (self.L / sub).mkdir(parents=True)

    def tearDown(self):
        self.tmp.cleanup()

    def utc(self, minutes_ago):
        return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() - minutes_ago * 60))

    def run_q(self, *extra):
        return subprocess.run([sys.executable, str(TOOLS / "guard-queue.py"), "--ledger", str(self.L), *extra],
                              capture_output=True, text=True, timeout=60)

    def test_AcikPencere_Kuyruk_AdaletBayragi_HicbirSeyYazilmaz(self):
        me = {"pid": os.getpid()}
        (self.L / "windows" / "W1.json").write_text(json.dumps({"id": "W1", "dir": "/k/wp-103/ug", "windowAreas": ["editor"],
                                                                 "state": "open", "utc": self.utc(40), "owner": me}))
        (self.L / "windows" / "W2.json").write_text(json.dumps({"id": "W2", "dir": "/k/wp-110/ug", "windowAreas": ["player"],
                                                                 "state": "open", "utc": self.utc(40), "owner": me}))
        for i, (ago, areas) in enumerate(((6, ["editor"]), (3, ["editor"]))):
            (self.L / "queue" / ("%d-T%d.json" % (1000 + i, i))).write_text(json.dumps(
                {"id": "T%d" % i, "dir": "/k/t%d/ug" % i, "windowAreas": areas, "utc": self.utc(ago), "waiter": me}))
        (self.L / "queue" / "2000-bad.json").write_text("{not json")
        before = sorted(str(p) for p in self.L.rglob("*"))
        r = self.run_q("--json")
        self.assertEqual(0, r.returncode, r.stderr)
        d = json.loads(r.stdout)
        w = {x["id"]: x for x in d["windows"]}
        self.assertTrue(w["W1"]["fairness"], "editor window 40 min, 2 editor runs behind it")
        self.assertEqual(2, w["W1"]["waitingBehind"])
        self.assertFalse(w["W2"]["fairness"], "nobody waits for the player area")
        self.assertEqual(["T0", "T1", "bad"], [q["id"] for q in d["queue"]], "FIFO, an unreadable ticket listed")
        self.assertEqual("alive", d["queue"][0]["waiter"])
        self.assertEqual("3", self.run_q("--waiting").stdout.strip())
        txt = self.run_q().stdout
        self.assertIn("⚠ FAIRNESS: 2 waiting behind it", txt); self.assertIn("QUEUE (3 waiting)", txt)
        self.assertEqual(before, sorted(str(p) for p in self.L.rglob("*")), "read-only: nothing retired or written")

    def test_S5_BekleyenSayisi_AlanCanliVeAltSureclerHaric(self):
        child = subprocess.Popen(["sleep", "30"])
        try:
            dead = subprocess.Popen(["true"]); dead.wait()
            # A: a live waiter outside this process's tree (the test's parent); D: started by this process
            rows = (("A", ["editor"], os.getppid()), ("B", ["player"], os.getppid()), ("C", ["editor"], dead.pid), ("D", ["editor"], child.pid))
            for i, (wid, areas, pid) in enumerate(rows):
                (self.L / "queue" / ("%d-%s.json" % (1000 + i, wid))).write_text(json.dumps(
                    {"id": wid, "dir": "/k/%s" % wid, "windowAreas": areas, "utc": self.utc(1), "waiter": {"pid": pid}}))
            (self.L / "queue" / "2000-E.json").write_text("[]")   # not an object: listed unreadable, no traceback (gt NOTE-4)
            self.assertEqual("5", self.run_q("--waiting").stdout.strip(), "the bare count: every ticket")
            self.assertEqual("2", self.run_q("--waiting", "--areas", "editor").stdout.strip(), "editor, live waiters: A and D")
            self.assertEqual("1", self.run_q("--waiting", "--areas", "editor", "--not-under", str(os.getpid())).stdout.strip(),
                             "D was started by this process (a step's own nested call): not counted")
            r = self.run_q("--json")
            self.assertEqual(0, r.returncode, r.stderr)
            self.assertTrue(json.loads(r.stdout)["queue"][-1].get("unreadable"))
        finally:
            child.kill(); child.wait()

    def test_BosDefter_Cikis0(self):
        r = self.run_q()
        self.assertEqual(0, r.returncode); self.assertIn("OPEN WINDOWS (0)", r.stdout); self.assertIn("QUEUE (0 waiting)", r.stdout)
        self.assertEqual(64, self.run_q("--nope").returncode)


if __name__ == "__main__":
    unittest.main(verbosity=2)
