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
  echo "userdata-guard snapshot → $2 root=real ${FAKE_KG:-kgSource=env} kg=abc"; exit 0; fi
echo "userdata-guard restore scope=$4 touched=0 ${FAKE_VERIFIED:-verified=1}"
exit ${FAKE_RESTORE_RC:-0}
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
        return [l.split()[0] for l in p.read_text().splitlines()] if p.exists() else []

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
        r = self.run_step("--warn-min", "1", step=step, DD_UG_STEP_MINUTE_SECONDS="1", DD_UG_STEP_POLL="1")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr); self.assertTrue(self.ran(), "warned, never stopped")
        tl = Path(str(self.ug) + "-timeline.txt").read_text()
        self.assertIn("FAIRNESS", tl); self.assertIn("with 1 waiting", tl); self.assertIn("FAIRNESS", r.stderr)
        self.assertLessEqual(tl.count("FAIRNESS"), 1, "at most one line per 10 (test) minutes")
        shutil.rmtree(self.ug); (self.root / "step.out").unlink(); Path(str(self.ug) + "-timeline.txt").unlink()
        for p in (self.ledger / "queue").glob("*.json"):
            p.unlink()
        self.run_step("--warn-min", "1", step=step, DD_UG_STEP_MINUTE_SECONDS="1", DD_UG_STEP_POLL="1")
        self.assertNotIn("FAIRNESS", Path(str(self.ug) + "-timeline.txt").read_text(), "nobody waits: no warning")

    def test_Sinyal_AdimDurur_RestoreYineKosar(self):
        p = subprocess.Popen(self.cmd(step=("sleep", "60")), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                             env=self.env(STEP_OUT=str(self.root / "step.out")))
        for _ in range(100):
            if Path(str(self.log) + ".guard").exists() and self.guard() == ["snapshot"]:
                break
            time.sleep(0.1)
        time.sleep(1.0)
        p.send_signal(signal.SIGTERM)
        out, err = p.communicate(timeout=60)
        self.assertEqual(143, p.returncode, out + err)
        self.assertEqual(["snapshot", "restore"], self.guard())


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

    def test_BosDefter_Cikis0(self):
        r = self.run_q()
        self.assertEqual(0, r.returncode); self.assertIn("OPEN WINDOWS (0)", r.stdout); self.assertIn("QUEUE (0 waiting)", r.stdout)
        self.assertEqual(64, self.run_q("--nope").returncode)


if __name__ == "__main__":
    unittest.main(verbosity=2)
