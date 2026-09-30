#!/usr/bin/env python3
"""WP-079G D1 · slot.sh K3 (G4 design v3.1 §3 D1, C-00 decisions M5 + 0.3):
(1) clean removes a lock/ticket as stale only when the recorded start AND the current `ps` start are both non-empty and
differ — a transient empty `ps` answer keeps a live lock; (2) DD_SLOT_TIMEOUT (empty/0 = wait forever) → own ticket removed,
`SLOT <kind> TIMEOUT after <n>s` + holders on stderr, exit 75; (3) a waiting acquire killed with INT/TERM drops its ticket.
Isolated: DD_SLOT_BASE in a tempdir, holders/owners are `sleep` processes started here, the transient empty `ps` is a fake
`ps` first on PATH. Never touches the real slot dir. No Unity, no Player.
usage: python3 tools/tests/test_slot_k3.py"""
import os, signal, subprocess, tempfile, time, unittest
from pathlib import Path

SLOT = Path(__file__).resolve().parents[1] / "slot.sh"
# ps that answers nothing for the start-time query (as ps does transiently under load) and is the real ps otherwise.
FAKE_PS = """#!/bin/bash
for a in "$@"; do [ "$a" = lstart= ] && exit 0; done
exec /bin/ps "$@"
"""


def pstart(pid):
    # exactly slot.sh's pstart(): `env -u TZ LC_ALL=C ps -o lstart= -p <pid> | tr -s ' '` in $(…)
    return subprocess.run(["bash", "-c", f"printf %s \"$(env -u TZ LC_ALL=C ps -o lstart= -p {int(pid)} | tr -s ' ')\""],
                          capture_output=True, text=True).stdout


class SlotK3Tests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="slot-k3-")
        self.dir = Path(self.tmp.name)
        self.base = self.dir / "slots"
        self.procs = []
        self.env = {k: v for k, v in os.environ.items() if not k.startswith("DD_SLOT")}
        self.env.update(DD_SLOT_BASE=str(self.base), DD_SLOT_POLL="1")

    def tearDown(self):
        for p in self.procs:
            if p.poll() is None:
                p.kill()
            p.wait()
        self.tmp.cleanup()

    def sleeper(self):
        p = subprocess.Popen(["sleep", "300"])
        self.procs.append(p)
        return p.pid

    def lock(self, kind, name, pid, start, queue=False):
        d = self.base / ("queue/" + kind if queue else kind)
        d.mkdir(parents=True, exist_ok=True)
        f = d / f"{name}.{pid}"
        f.write_text(f"2026-09-30T00:00:00Z holder\nstart={start}\n")
        return f

    def slot(self, *args, env=None, timeout=30):
        return subprocess.run(["bash", str(SLOT), *args], env=env or self.env, capture_output=True, text=True,
                              timeout=timeout)

    def popen(self, *args, env):
        p = subprocess.Popen(["bash", str(SLOT), *args], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True)
        self.procs.append(p)
        return p

    def tickets(self, kind):
        q = self.base / "queue" / kind
        return sorted(q.iterdir()) if q.exists() else []

    def wait_for(self, cond, secs=10):
        end = time.time() + secs
        while time.time() < end:
            if cond():
                return True
            time.sleep(0.05)
        return cond()

    def held_player(self):
        holder = self.sleeper()
        return self.lock("player", "1.holder", holder, pstart(holder))

    # (1) clean
    def test_SlotClean_BosPstart_KilitKalir(self):
        pid = self.sleeper()
        lock = self.lock("player", "1.live", pid, pstart(pid))
        ticket = self.lock("player", "000000000001-000001.live", pid, pstart(pid), queue=True)
        fakebin = self.dir / "fakebin"
        fakebin.mkdir()
        (fakebin / "ps").write_text(FAKE_PS)
        (fakebin / "ps").chmod(0o755)
        env = dict(self.env, PATH=f"{fakebin}:{self.env.get('PATH', '/usr/bin:/bin')}")
        r = self.slot("status", env=env)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertTrue(lock.exists(), "empty ps answer removed a live lock")
        self.assertTrue(ticket.exists(), "empty ps answer removed a live ticket")
        self.assertIn(lock.name, r.stdout)

    def test_SlotClean_OluPidBosPs_KilitYineSilinir(self):
        # D1 review SHOULD-3: a DEAD holder must still be cleaned even while `ps` answers empty (kill -0 goes first).
        pid = self.sleeper(); start = pstart(pid)
        proc = self.procs[-1]; proc.kill(); proc.wait()
        lock = self.lock("player", "1.dead", pid, start)
        ticket = self.lock("player", "000000000001-000001.dead", pid, start, queue=True)
        fakebin = self.dir / "fakebin"
        fakebin.mkdir()
        (fakebin / "ps").write_text(FAKE_PS)
        (fakebin / "ps").chmod(0o755)
        env = dict(self.env, PATH=f"{fakebin}:{self.env.get('PATH', '/usr/bin:/bin')}")
        r = self.slot("status", env=env)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertFalse(lock.exists(), "a dead holder's lock survived an empty ps answer")
        self.assertFalse(ticket.exists(), "a dead waiter's ticket survived an empty ps answer")

    def test_SlotClean_YerelAyarVeSaatDilimi_CanliKilitKalir(self):
        # D1 review MUST-1: a lock written in the fixed C/local format survives a `status` from a tr_TR / UTC shell.
        pid = self.sleeper()
        lock = self.lock("player", "1.live", pid, pstart(pid))
        env = dict(self.env, LANG="tr_TR.UTF-8", LC_ALL="tr_TR.UTF-8", TZ="UTC")
        r = self.slot("status", env=env)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertTrue(lock.exists(), "locale / TZ of the caller removed a live lock")

    def test_SlotClean_EskiBicimYerelAyarliStart_CanliPidKorunur_OluPidSilinir(self):
        # C-00 30.09: the KADRO copy can be synced at any moment — a lock written by an older slot.sh in tr_TR
        # ("Çar 30 Eyl …") cannot be compared with the C-format start: a live pid keeps it, a dead pid is still removed.
        live = self.sleeper()
        lock = self.lock("player", "1.legacy", live, "Çar 30 Eyl 19:03:12 2026")
        r = self.slot("status")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertTrue(lock.exists(), "a live holder's legacy-format lock was removed")
        h = self.slot("holds", "player", str(live))
        self.assertEqual(h.returncode, 0, h.stdout + h.stderr)
        self.assertTrue(h.stdout.startswith("SLOT player held by "))
        proc = self.procs[-1]; proc.kill(); proc.wait()
        r = self.slot("status")
        self.assertFalse(lock.exists(), "a dead holder's legacy lock must still be cleaned")

    def test_SlotClean_FarkliPstart_KilitSilinir(self):
        pid = self.sleeper()
        stale = self.lock("player", "1.reused", pid, "Thu Jan 1 00:00:00 1970")
        ticket = self.lock("player", "000000000001-000001.reused", pid, "Thu Jan 1 00:00:00 1970", queue=True)
        same = self.lock("smoke", "1.live", pid, pstart(pid))
        r = self.slot("status")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertFalse(stale.exists(), "lock of a reused pid (other start time) kept")
        self.assertFalse(ticket.exists(), "ticket of a reused pid (other start time) kept")
        self.assertTrue(same.exists(), "lock with a matching start time removed")

    # (2) DD_SLOT_TIMEOUT
    def test_SlotAcquire_TimeoutTutuluSlot_Cikis75BiletYok(self):
        lock = self.held_player()
        env = dict(self.env, DD_SLOT_OWNER=str(self.sleeper()), DD_SLOT_TIMEOUT="2")
        t0 = time.time()
        r = self.slot("acquire", "player", "k3-timeout", env=env)
        took = time.time() - t0
        self.assertEqual(r.returncode, 75, r.stdout + r.stderr)
        self.assertRegex(r.stderr, r"SLOT player TIMEOUT after [23]s")
        self.assertIn(lock.name, r.stderr, "holders not listed")
        self.assertEqual(self.tickets("player"), [], "own ticket left in the queue")
        self.assertTrue(lock.exists(), "holder's lock touched")
        self.assertLess(took, 6)
        bad = self.slot("acquire", "player", "k3-bad", env=dict(env, DD_SLOT_TIMEOUT="5m"))
        self.assertEqual(bad.returncode, 64, bad.stderr)
        self.assertEqual(self.tickets("player"), [])

    def test_SlotAcquire_TimeoutBos_DavranisAyni(self):
        for value in ("", "0", None):
            with self.subTest(DD_SLOT_TIMEOUT=value):
                lock = self.held_player()
                env = dict(self.env, DD_SLOT_OWNER=str(self.sleeper()))
                if value is not None:
                    env["DD_SLOT_TIMEOUT"] = value
                p = self.popen("acquire", "player", "k3-wait", env=env)
                time.sleep(3)  # well past any 1-2 s timeout: must still be waiting in the FIFO
                self.assertIsNone(p.poll(), "acquire gave up although DD_SLOT_TIMEOUT is empty/0")
                self.assertEqual(len(self.tickets("player")), 1)
                lock.unlink()
                out, err = p.communicate(timeout=10)
                self.assertEqual(p.returncode, 0, out + err)
                self.assertIn("SLOT player#1 -> k3-wait", out)
                self.assertEqual(self.tickets("player"), [])
                mine = list((self.base / "player").glob("1.k3-wait.*"))
                self.assertEqual(len(mine), 1)
                mine[0].unlink()

    # (3) signal while waiting
    def test_SlotAcquire_BeklerkenTerm_BiletSilinir(self):
        for sig, code in ((signal.SIGTERM, 143), (signal.SIGINT, 130), (signal.SIGHUP, 129)):
            with self.subTest(signal=sig.name):
                lock = self.held_player()
                env = dict(self.env, DD_SLOT_OWNER=str(self.sleeper()), DD_SLOT_POLL="20")
                p = self.popen("acquire", "player", "k3-term", env=env)
                self.assertTrue(self.wait_for(lambda: len(self.tickets("player")) == 1), "no ticket written")
                time.sleep(0.5)  # inside the 20 s poll sleep
                t0 = time.time()
                p.send_signal(sig)
                p.communicate(timeout=10)
                self.assertEqual(p.returncode, code)
                self.assertLess(time.time() - t0, 3, "trap deferred until the poll sleep ended")
                self.assertEqual(self.tickets("player"), [], "killed waiter left its ticket")
                self.assertTrue(lock.exists())
                lock.unlink()

    def test_SlotAcquire_Reentrant_TimeoutEtkilemez(self):
        holder = self.sleeper()
        lock = self.lock("player", "1.guard", holder, pstart(holder))
        env = dict(self.env, DD_SLOT_OWNER=str(holder), DD_SLOT_TIMEOUT="1")
        r = self.slot("acquire", "player", "k3-inner", env=env, timeout=10)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("SLOT player reentrant -> k3-inner", r.stdout)
        self.assertNotIn("TIMEOUT", r.stderr)
        self.assertEqual(self.tickets("player"), [])
        self.assertTrue(lock.exists())
        rel = self.slot("release", "player", "k3-inner", env=env)
        self.assertEqual(rel.returncode, 0)
        self.assertTrue(lock.exists(), "release of the inner tag removed the outer lock")


if __name__ == "__main__":
    unittest.main(verbosity=2)
