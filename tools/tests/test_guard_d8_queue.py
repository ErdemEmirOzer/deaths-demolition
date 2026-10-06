#!/usr/bin/env python3
"""G4 D8 window queue (WP-089, C-00 05.10; reviews 06.10) — unit level: userdata_guard_io.ledger_begin / ledger_update /
ledger_move on a TEMP ledger (L = <tmp>/ledger), called directly: deterministic, no poll phases, no guard process, no
defaults, nothing under ~/Library or ~/.dd-userdata-guard. Real processes stand in for owners and waiters (the real `ps`
is needed, so this file is not run under sandbox-exec like test_userdata_guard_v2.py, whose process-level LedgerTests
cover the same rules end to end).
usage: python3 tools/tests/test_guard_d8_queue.py"""
import json, os, subprocess, sys, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import userdata_guard_io  # noqa: E402


class KuyrukBirimTests(unittest.TestCase):
    """G4 D8 queue reviews (06.10): ledger_begin called directly on a temp ledger — deterministic (no poll phases), with
    real processes: this test process = the snapshot's caller (po), a child `sleep` = a live FOREIGN process (not an
    ancestor of po), a reaped child = a dead pid."""

    def setUp(self):
        self.io = userdata_guard_io
        self.tmpd = tempfile.TemporaryDirectory(prefix="dd-queue-unit-")
        self.L = Path(self.tmpd.name) / "ledger"
        self.procs = []
        self.po = os.getpid()
        self.me = {"pid": self.po, "lstart": self.io.lstart(self.po)}

    def tearDown(self):
        for p in self.procs:
            p.kill(); p.wait()
        self.tmpd.cleanup()

    def foreign(self):
        p = subprocess.Popen(["sleep", "300"]); self.procs.append(p)
        return {"pid": p.pid, "lstart": self.io.lstart(p.pid)}

    def dead(self):
        p = subprocess.Popen(["true"]); p.wait()
        return {"pid": p.pid, "lstart": ""}

    def rec(self, wid, owner, areas=("editor",), state="open", **extra):
        r = {"id": wid, "dir": "/x/" + wid, "scope": "editor", "windowAreas": list(areas), "owner": owner, "state": state,
             "utc": self.io.utc_now()}
        r.update(extra)
        return r

    def put_record(self, r):
        self.io.ledger_write(str(self.L / "windows" / (r["id"] + ".json")), r)

    def put_ticket(self, wid, owner, waiter, areas=("editor",), ns=None):
        t = {"id": wid, "dir": "/x/" + wid, "owner": owner, "waiter": waiter, "windowAreas": list(areas),
             "utc": self.io.utc_now()}
        self.io.ledger_write(str(self.L / "queue" / ("%d-%s.json" % (ns or time.time_ns(), wid))), t)

    def begin(self, wid, areas=("editor",), owner=None, waiter=None):
        return self.io.ledger_begin(str(self.L), self.rec(wid, owner or self.me, areas, state="snapshotting"), self.po,
                                    waiter if waiter is not None else self.po)

    def tickets(self):
        return sorted(p.name for p in (self.L / "queue").glob("*.json"))

    def done(self):
        return sorted(p.name for p in (self.L / "queue" / "done").glob("*.json"))

    def test_Birim_EskiOrtusenBilet_EngelYokken_YeniGeleniBekletir(self):
        f = self.foreign()
        self.put_ticket("T1", f, f)
        code, msg = self.begin("N")
        self.assertEqual(75, code, msg)
        self.assertIn("queue position 2 — ahead: /x/T1", msg)
        self.assertEqual(2, len(self.tickets()))

    def test_Birim_OrtusmeyenBilet_Bekletmez(self):
        f = self.foreign()
        self.put_ticket("T1", f, f, areas=("player",))
        self.assertEqual(0, self.begin("N", areas=("editor",))[0])

    def test_Birim_IcIce_AtaninAlanindakiYabanciBilet_OndeSayilmaz_Cikis0(self):
        """MUST-1: the ancestor's window is nested for us; the foreign ticket waits for it — not ahead of us."""
        f = self.foreign()
        self.put_record(self.rec("A", self.me))
        self.put_ticket("Q", f, f)
        code, msg = self.begin("N")
        self.assertEqual(0, code, msg)
        self.assertEqual(["*"], ["*" for n in self.tickets() if "-Q.json" in n])   # Q keeps its place

    def test_Birim_IcIce_AtaPenceresiBaskaAlanda_OAlandakiYabanciBilet_OndeSayilmaz(self):
        """gt 06.10 N1: the ancestor holds a PLAYER window, a foreign all-scope ticket waits for it, the nested snapshot wants
        the editor area only — that ticket waits for our ancestor, which waits for us: not ahead (0, at once)."""
        f = self.foreign()
        self.put_record(self.rec("A", self.me, areas=("player",)))
        self.put_ticket("T", f, f, areas=("editor", "player"))
        code, msg = self.begin("N", areas=("editor",))
        self.assertEqual(0, code, msg)
        self.assertTrue(any(n.endswith("-T.json") for n in self.tickets()))   # T keeps its place

    def test_Birim_WindowsIcindeClosedKayit_Atlanir_Cikis0(self):
        """MUST-2: a closed record whose move to closed/ is pending is finished, never a 65."""
        self.put_record(self.rec("C", self.foreign(), state="closed"))
        self.assertEqual(0, self.begin("N")[0])

    def test_Birim_R12Closed_AyniKilitteTasinir_LedgerCloseNoOp(self):
        self.put_record(self.rec("C", self.me, state="restoring"))
        r = self.io.ledger_update(str(self.L), "C", {"state": "auto", "areas": {"editor": "finalized"}})
        self.assertEqual("closed", r["state"])
        self.assertFalse((self.L / "windows" / "C.json").exists())
        self.assertTrue((self.L / "closed" / "C.json").exists())
        self.assertTrue(self.io.ledger_move(str(self.L), "C", "closed"))   # the guard's later ledger-close: done, not a failure

    def test_Birim_BosLstartliOluBekleyici_Budanir_DoneKlasorune(self):
        """MUST-3: a ticket whose waiter's start time was never known is still reaped once that pid is dead (kill -0)."""
        d = self.dead()
        self.put_ticket("T1", d, d)
        self.assertEqual(0, self.begin("N")[0])
        self.assertEqual([], self.tickets())
        self.assertTrue(any(n.endswith("-T1.reaped.json") for n in self.done()), self.done())

    def test_Birim_GeciciBosPs_CanliBekleyiciyiOldurmez_K3(self):
        f = self.foreign()
        self.put_ticket("T1", f, {"pid": f["pid"], "lstart": ""})   # start time unknown, pid alive → kept (K3)
        self.assertEqual(75, self.begin("N")[0])
        self.assertTrue(any(n.endswith("-T1.json") for n in self.tickets()))

    def test_Birim_KuyruktayikenSahipOldu_65PencereYok_BiletLeft(self):
        """MUST-4: no window is opened for a caller that died while it waited."""
        f = self.foreign()
        self.put_record(self.rec("B", f))
        self.assertEqual(75, self.begin("N")[0])   # queued behind B
        code, msg = self.begin("N", owner=self.dead())   # the same snapshot's next poll: its caller is gone
        self.assertEqual(65, code, msg)
        self.assertIn("is gone — no window opened", msg)
        self.assertFalse((self.L / "windows" / "N.json").exists())
        self.assertEqual([], self.tickets())
        self.assertTrue(any(n.endswith("-N.left.json") for n in self.done()), self.done())

    def zombie(self):
        """a child that exited and is NOT reaped yet (Popen without wait): kill -0 still answers for it."""
        p = subprocess.Popen(["sleep", "0.2"]); who = {"pid": p.pid, "lstart": self.io.lstart(p.pid)}
        end = time.time() + 10
        while time.time() < end and not self.io.proc_state(p.pid)[0].startswith("Z"):
            time.sleep(0.05)
        self.procs.append(p)   # tearDown reaps it
        self.assertTrue(self.io.proc_state(p.pid)[0].startswith("Z"), "fixture: no zombie")
        return who

    def test_Birim_ZombiCagiran_Olu_65PencereYok(self):
        """technical-writer 06.10 (8z): a caller that exited but is not reaped yet is gone — no window for it."""
        z = self.zombie()
        os.kill(z["pid"], 0)   # kill -0 still answers: the reason this needs the ps state
        self.assertTrue(self.io.gone(z))
        code, msg = self.begin("N", owner=z)
        self.assertEqual(65, code, msg)
        self.assertFalse((self.L / "windows" / "N.json").exists())

    def test_Birim_BiletSirasi_SaatGeriGitse_Monoton(self):
        f = self.foreign()
        future = time.time_ns() + 10 ** 12
        self.put_ticket("T1", f, f, ns=future)
        self.assertEqual(75, self.begin("N")[0])
        ns = sorted(int(n.split("-", 1)[0]) for n in self.tickets())
        self.assertEqual(future, ns[0]); self.assertGreater(ns[1], future)

    def test_Birim_BicimDisiKuyrukDosyasi_Cokmez(self):
        (self.L / "queue").mkdir(parents=True)
        (self.L / "queue" / "not-a-ticket.json").write_text("{}")
        (self.L / "queue" / "123-T.json").write_text("{broken")
        self.assertEqual(0, self.begin("N")[0])
        self.assertTrue((self.L / "queue" / "not-a-ticket.json").exists())   # never touched
        self.assertTrue(any(n.endswith("-T.unreadable.json") for n in self.done()), self.done())

    def test_Birim_Tahmin_TipikSureAsildi_Overdue(self):
        old = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() - 3600))
        for i in range(3):   # typical: 10 min
            c = self.rec("c%d" % i, self.me, state="closed", utc=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() - 7200)),
                         utcUpdated=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() - 6600)))
            self.io.ledger_write(str(self.L / "closed" / ("c%d.json" % i)), c)
        self.put_record(self.rec("B", self.foreign(), utc=old))
        code, msg = self.begin("N")
        self.assertEqual(75, code)
        self.assertIn("estimate overdue: open 60 min, typical 10 min", msg)

    def test_Birim_Tahmin_KapaliPencereYok_Unknown(self):
        self.put_record(self.rec("B", self.foreign()))
        self.assertIn("estimate unknown (no closed window over these areas yet)", self.begin("N")[1])

    def test_Birim_Verilince_UtcYenilenir_QueuedUtcVeGuardYazilir(self):
        f = self.foreign()
        self.put_record(self.rec("B", f))
        self.assertEqual(75, self.begin("N")[0])
        self.assertTrue(self.io.ledger_move(str(self.L), "B", "closed"))   # B is restored and closed
        code, msg = self.begin("N")
        self.assertEqual(0, code, msg)
        r = json.loads((self.L / "windows" / "N.json").read_text())
        self.assertIn("queuedUtc", r); self.assertEqual(self.po, r["guard"]["pid"])
        self.assertGreaterEqual(r["utc"], r["queuedUtc"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
