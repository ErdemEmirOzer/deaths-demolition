#!/usr/bin/env python3
"""G4 D8 window queue (WP-089, C-00 05.10; reviews 06.10) — unit level: userdata_guard_io.ledger_begin / ledger_update /
ledger_move on a TEMP ledger (L = <tmp>/ledger), called directly: deterministic, no poll phases, no guard process, no
defaults, nothing under ~/Library or ~/.dd-userdata-guard. Real processes stand in for owners and waiters (the real `ps`
is needed, so this file is not run under sandbox-exec like test_userdata_guard_v2.py, whose process-level LedgerTests
cover the same rules end to end).
usage: python3 tools/tests/test_guard_d8_queue.py"""
import json, os, subprocess, sys, tempfile, time, unittest
from unittest import mock
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


    # -- next slice (C-00 06.10): dirty + retry, a dead snapshotting guard, one ps table, siblings (gt NIT-5) --------
    def test_Birim_DirtyRetrySameDir_SahipCanli_Bekler75(self):
        """a dirty window whose restore ended retry=same-dir (BUSY / LOCKED / INTERRUPTED) and whose owner lives on is
        restored again with the same DIR: the queue waits for it (75), never 65."""
        self.put_record(self.rec("B", self.foreign(), state="dirty", retry="same-dir"))
        code, msg = self.begin("N")
        self.assertEqual(75, code, msg)
        self.assertIn("queue position 1 — ahead: /x/B", msg); self.assertIn("dirty", msg)

    def test_Birim_DirtyRetry_SahipOluYaDaRetryYok_65(self):
        for name, owner, retry in (("olu", None, "same-dir"), ("retry-yok", "live", "none"), ("eski-kayit", "live", None)):
            with self.subTest(case=name):
                L = self.L / name
                rec = self.rec("B", self.foreign() if owner else self.dead(), state="dirty")
                if retry:
                    rec["retry"] = retry
                self.io.ledger_write(str(L / "windows" / "B.json"), rec)
                code, msg = self.io.ledger_begin(str(L), self.rec("N", self.me, state="snapshotting"), self.po, self.po)
                self.assertEqual(65, code, msg)

    def test_Birim_DirtyRetrySameDir_IcIce_65(self):
        """our own ancestor's dirty window: waiting for it would wait for ourselves — still 65."""
        self.put_record(self.rec("A", self.me, state="dirty", retry="same-dir"))
        self.assertEqual(65, self.begin("N")[0])

    # -- the next slice's review (backend 06.10, d8-next-review-backend-0610.md) ------------------------------------------
    def utc_ago(self, seconds):
        return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() - seconds))

    def test_Birim_DirtyRetry_GraceDolunca65_EnvIleAyarlanir(self):
        """MUST-1: the wait for a re-restore is bounded — DD_GUARD_DIRTY_GRACE (600 s) after R12."""
        self.put_record(self.rec("B", self.foreign(), state="dirty", retry="same-dir", exit=75, utcUpdated=self.utc_ago(900)))
        self.assertEqual(65, self.begin("N")[0])
        with mock.patch.dict(os.environ, {"DD_GUARD_DIRTY_GRACE": "1200"}):
            self.assertEqual(75, self.begin("N2")[0])

    def test_Birim_DirtyRetry_Exit30_Beklenmez_65(self):
        """SHOULD-3: a 30 (STOP, tell C-00) also carries retry=same-dir when an area stayed BUSY — nobody retries it."""
        self.put_record(self.rec("B", self.foreign(), state="dirty", retry="same-dir", exit=30, utcUpdated=self.utc_ago(5)))
        self.assertEqual(65, self.begin("N")[0])

    def test_Birim_MarkMissing_RetryNoneYazar_Bekleyen65(self):
        """MUST-2: SNAPSHOT_MISSING can never be restored — mark-missing clears retry=same-dir."""
        rec = self.rec("B", self.foreign(), state="dirty", retry="same-dir", exit=75, utcUpdated=self.utc_ago(5))
        rec["dir"] = os.path.realpath(str(self.L.parent / "ugB"))
        self.put_record(rec)
        self.assertEqual(75, self.begin("N")[0])
        self.assertEqual(0, self.io.main(["ledger-mark-missing", str(self.L), rec["dir"], "no before.txt"]))
        self.assertEqual("none", json.loads((self.L / "windows" / "B.json").read_text())["retry"])
        self.assertEqual(65, self.begin("N")[0])

    def test_Birim_IcIce_AtaDirtyRetryBaskaAlanda_YabanciBilet_OndeSayilmaz(self):
        """MUST-3: the ancestor's dirty window that will be restored again holds its areas too (N1 for dirty)."""
        f = self.foreign()
        r = self.rec("A", self.me, areas=("player",), state="dirty", retry="same-dir", exit=75, utcUpdated=self.utc_ago(5))
        r["areas"] = {"player": "dirty"}
        self.put_record(r)
        self.put_ticket("T", f, f, areas=("editor", "player"))
        code, msg = self.begin("N", areas=("editor",))
        self.assertEqual(0, code, msg)

    def test_Birim_SnapshottingGuardOlu_BeforeTxtVar_AcikSayilir_Bekler(self):
        """SHOULD-1: committed (before.txt) but the 'open' update was lost — the window is open, its run goes on."""
        d = self.L.parent / "ugB"; d.mkdir(); (d / "before.txt").write_text("x")
        rec = self.rec("B", self.foreign(), state="snapshotting", guard=self.dead()); rec["dir"] = str(d)
        self.put_record(rec)
        self.assertEqual(75, self.begin("N")[0])

    def test_Birim_DirtyReleasedAlan_BloklamayaDevamEder_65(self):
        """gt B-1 (06.10): a RELEASED area of a dirty window stays blocked — its owner may restore the DIR again in a wider
        scope and would write the run's bytes into a window opened there meanwhile (user data)."""
        r = self.rec("B", self.foreign(), areas=("editor", "player"), state="dirty")
        r["areas"] = {"editor": "dirty", "player": "released"}
        self.put_record(r)
        self.assertEqual(65, self.begin("P", areas=("player",))[0])

    def test_Birim_DirtyRetry_BozukZamanDamgasi_GraceGecmisSayilir_65(self):
        """gt N-7: an unreadable time stamp never makes the wait unbounded."""
        self.put_record(self.rec("B", self.foreign(), state="dirty", retry="same-dir", exit=75, utc="?", utcUpdated="bozuk"))
        self.assertEqual(65, self.begin("N")[0])

    def test_Birim_DirtyYalnizDirtyAlanlariBloklar(self):
        """SHOULD-2: editor finalized + player dirty — an editor snapshot is not blocked, a player one is (65, no retry)."""
        r = self.rec("B", self.foreign(), areas=("editor", "player"), state="dirty")
        r["areas"] = {"editor": "finalized", "player": "dirty"}
        self.put_record(r)
        self.assertEqual(0, self.begin("E", areas=("editor",))[0])
        self.assertEqual(65, self.begin("P", areas=("player",))[0])

    def test_Birim_SnapshottingGuardOlu_SahipCanli_65(self):
        """backend SHOULD-4: the guard died after the grant (SIGKILL), its owner lives — abandoned, not a wait."""
        rec = self.rec("B", self.foreign(), state="snapshotting", guard=self.dead())
        self.put_record(rec)
        code, msg = self.begin("N")
        self.assertEqual(65, code, msg)
        self.assertIn("its snapshotting guard pid %s is gone" % rec["guard"]["pid"], msg)

    def test_Birim_SnapshottingGuardCanli_Bekler75(self):
        f = self.foreign()
        self.put_record(self.rec("B", f, state="snapshotting", guard=self.foreign()))
        self.assertEqual(75, self.begin("N")[0])

    def test_Birim_MarkMissing_SnapshottingGuardOlu_DirtyYapilir(self):
        rec = self.rec("B", self.foreign(), state="snapshotting", guard=self.dead())
        rec["dir"] = os.path.realpath(str(self.L.parent / "ugB"))
        self.put_record(rec)
        rc = self.io.main(["ledger-mark-missing", str(self.L), rec["dir"], "no before.txt"])
        self.assertEqual(0, rc)
        self.assertEqual("dirty", json.loads((self.L / "windows" / "B.json").read_text())["state"])

    def test_Birim_MarkMissing_SnapshottingGuardCanli_Reddedilir(self):
        rec = self.rec("B", self.foreign(), state="snapshotting", guard=self.foreign())
        rec["dir"] = os.path.realpath(str(self.L.parent / "ugB"))
        self.put_record(rec)
        self.assertEqual(2, self.io.main(["ledger-mark-missing", str(self.L), rec["dir"], "no before.txt"]))

    def test_Birim_TekPsTablosu_BiletVeKayitSayisindanBagimsiz(self):
        """reviews SHOULD-6: one `ps -axo` per call; the per-pid ps is only for the new ticket's waiter (its start time)."""
        f = [self.foreign() for _ in range(6)]
        self.put_record(self.rec("B", f[0]))
        for i, who in enumerate(f[1:]):
            self.put_ticket("T%d" % i, who, who)
        real = subprocess.run
        calls = []

        def counting(argv, *a, **k):
            if argv and argv[0] == "ps":
                calls.append(" ".join(argv[:2]))
            return real(argv, *a, **k)
        with mock.patch.object(self.io.subprocess, "run", side_effect=counting):
            code, _ = self.begin("N")
        self.assertEqual(75, code)
        self.assertEqual(1, sum(c.startswith("ps -axo") for c in calls), calls)
        self.assertLessEqual(len(calls), 2, calls)   # the table + lstart(waiter) for N's ticket

    def test_Birim_KardesBilet_NIT5_IcIceKardesOneGecebilir_KilitlenmeYok(self):
        """gt NIT-5 (C-00 06.10: a note, tested): A (this process) holds a window; two of its children C1 (older ticket)
        and C2 snapshot nested. C1's ticket overlaps A's held area, so C2 may pass it (FIFO between siblings is not kept);
        C1 then waits for C2's window like any foreign one — no deadlock, never two windows over one area at once."""
        c1, c2 = self.foreign(), self.foreign()   # children of this process: A's descendants
        self.put_record(self.rec("A", self.me))
        self.put_record(self.rec("F", self.foreign(), areas=("player",)))   # C1 waits for a foreign player window
        code1, msg1 = self.io.ledger_begin(str(self.L), self.rec("C1", c1, areas=("editor", "player"), state="snapshotting"),
                                           c1["pid"], c1["pid"])
        self.assertEqual(75, code1, msg1)
        code2, msg2 = self.io.ledger_begin(str(self.L), self.rec("C2", c2, state="snapshotting"), c2["pid"], c2["pid"])
        self.assertEqual(0, code2, msg2)   # passed C1 (sibling under A): the documented reversal
        (self.L / "windows" / "F.json").rename(self.L / "F-closed.json")   # F goes away
        code1, msg1 = self.io.ledger_begin(str(self.L), self.rec("C1", c1, areas=("editor", "player"), state="snapshotting"),
                                           c1["pid"], c1["pid"])
        self.assertEqual(75, code1, msg1)   # C1 now waits for C2's window (foreign to C1) — it will close
        self.assertIn("ahead: /x/C2", msg1)


    def test_Birim_MarkMissing_SnapshottingSahipOlu_DirtyYapilir(self):
        """C-00 06.10: `alive` was shadowed in main() — UnboundLocalError on every snapshotting record before."""
        rec = self.rec("B", self.dead(), state="snapshotting")
        rec["dir"] = os.path.realpath(str(self.L.parent / "ugB"))
        self.put_record(rec)
        self.assertEqual(0, self.io.main(["ledger-mark-missing", str(self.L), rec["dir"], "no before.txt"]))
        self.assertEqual("dirty", json.loads((self.L / "windows" / "B.json").read_text())["state"])

    def test_Birim_MarkMissing_SnapshottingSahipCanli_2(self):
        rec = self.rec("B", self.foreign(), state="snapshotting")
        rec["dir"] = os.path.realpath(str(self.L.parent / "ugB"))
        self.put_record(rec)
        self.assertEqual(2, self.io.main(["ledger-mark-missing", str(self.L), rec["dir"], "no before.txt"]))
        self.assertEqual("snapshotting", json.loads((self.L / "windows" / "B.json").read_text())["state"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
