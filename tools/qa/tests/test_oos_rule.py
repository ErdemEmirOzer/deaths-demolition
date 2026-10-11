"""WP-089 (C-00 05.10, decision (b)) · qa/oos_rule.py: an editor-stage guard 32 passes only when the change is in the
player area alone, every writer is a Player of another guarded window (same pid + start, owned=1) and that window's
restore is verified=1. Fixture folders only."""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import oos_rule  # noqa: E402

START = "Mon Oct  5 15:00:00 2026"
P1 = "/root/PR/com.x.plist"


class OosRuleTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-oos-")
        self.root = Path(self.tmp.name)
        self.ug = self.root / "rc" / "stages" / "full-editmode" / "ug"; self.ug.mkdir(parents=True)
        self.owner = self.root / "wp-078" / "evidence" / "ugp"; self.owner.mkdir(parents=True)

    def tearDown(self):
        self.tmp.cleanup()

    def window(self, rows=("player  %s  %s (launch-counter keys excluded)" % ("a" * 64, P1),), attributed=True,
               own_start=START, owner_owned=True, owner_start=START, restore="verified=1"):
        (self.ug / "window.json").write_text("{}")
        (self.ug / "out-of-scope-changed.txt").write_text("\n".join(rows) + "\n")
        (self.ug / "attribution.txt").write_text(
            "%s\tmtime 15:01:00Z\t%s\n" % (P1, "pid 4242 /tmp/run.log" if attributed else "no Player alive (Editor, or between samples)"))
        (self.ug / "players-lineage.tsv").write_text("2026-10-05T12:01:00Z\t4242\t1\t%s\t0\n" % own_start)
        (self.owner / "window.json").write_text("{}")
        (self.owner / "players-lineage.tsv").write_text("2026-10-05T12:01:00Z\t4242\t900\t%s\t%d\n" % (owner_start, owner_owned))
        if restore is not None:
            (self.owner / "restore.txt").write_text(
                "2026-10-05T12:05:00Z userdata-guard restore scope=player touched=1 %s extraKeys=0\nexit=0\n" % restore)

    def decide(self, wait=0.0):
        return oos_rule.decide(self.ug, [self.root], wait=wait, sleep=lambda s: None)

    def test_UcKosulTutar_Gecer(self):
        self.window()
        ok, why = self.decide()
        self.assertTrue(ok, why); self.assertIn("pid 4242 → %s" % self.owner, why)

    def test_Kosul1_EditorAlaniDegisti_Kalir(self):
        self.window(rows=("player  %s  %s" % ("a" * 64, P1), "editor  MISSING  /root/AS/x/ui-settings.json"))
        ok, why = self.decide(); self.assertFalse(ok); self.assertIn("condition 1", why)

    def test_Kosul2_Atfedilmemis_Kalir(self):
        self.window(attributed=False)
        ok, why = self.decide(); self.assertFalse(ok); self.assertIn("not attributed", why)

    def test_Kosul2_YazarGuardliPencereDegil_Kalir(self):
        self.window(owner_owned=False)
        ok, why = self.decide(); self.assertFalse(ok); self.assertIn("belongs to no guarded window", why)

    def test_Kosul2_PidYenidenKullanilmis_Kalir(self):
        self.window(owner_start="Mon Oct  5 09:00:00 2026")
        ok, why = self.decide(); self.assertFalse(ok); self.assertIn("belongs to no guarded window", why)

    def test_Kosul3_SahipVerified0_Kalir(self):
        self.window(restore="verified=0")
        ok, why = self.decide(); self.assertFalse(ok); self.assertIn("not verified=1", why)

    def test_Kosul3_SahipHenuzRestoreEtmedi_BekleSonraKalir(self):
        self.window(restore=None)
        sleeps, t = [], [0.0]
        ok, why = oos_rule.decide(self.ug, [self.root], wait=30, sleep=lambda s: (sleeps.append(s), t.__setitem__(0, t[0] + s)),
                                  clock=lambda: t[0])
        self.assertFalse(ok); self.assertIn("not restored yet", why); self.assertEqual([10, 10, 10], sleeps)

    def test_RetryKopyasi_N_Okunur(self):
        self.window()
        (self.ug / "out-of-scope-changed.txt.2").write_text("editor  MISSING  /root/AS/x/ui-settings.json\n")
        ok, why = self.decide(); self.assertFalse(ok); self.assertIn("condition 1", why)

    def test_EskiGuardSatiri_SonundaLiteralNewline_BireBirKopya_Kabul(self):
        """C-00 05.10 MUST: K/wp-083/evidence/ug-b2f6-ppm (guard 52b7a00f..) — the row ends in a literal $'\\n' and no
        newline; the writer pid 62577 is S-09's cine1 window, verified=1. Byte copies of those files."""
        start = "Mon Oct 5 16:10:46 2026"
        (self.ug / "window.json").write_text("{}")
        (self.ug / "out-of-scope-changed.txt").write_text(
            "player  7659d6828c0a1914bd89c3a7f6af3b9ad06078cffa37bf0cec771c5cb225ca06  /Users/Erdem/Library/Preferences/"
            "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept.plist (launch-counter keys excluded)$'\\n'")
        (self.ug / "attribution.txt").write_text(
            "/Users/Erdem/Library/Preferences/com.Deaths-Demolition.Deaths-Demolition---Playable-Concept.plist\tmtime 13:11:26Z"
            "\tpid 62577 /Users/Erdem/Dev/KADRO/context/projects/deaths-demolition/wp-068s9/evidence/wp081/cine1/21x9/cine.log\n")
        (self.ug / "players-lineage.tsv").write_text("2026-10-05T13:10:51Z\t62577\t62478\t%s\t0\n" % start)
        (self.owner / "window.json").write_text("{}")
        (self.owner / "players-lineage.tsv").write_text("2026-10-05T13:10:51Z\t62577\t62478\t%s\t1\n" % start)
        (self.owner / "restore.txt").write_text("2026-10-05T13:14:00Z userdata-guard restore scope=player touched=1 verified=1 "
                                                "retry=none class=OK\nexit=0\n")
        ok, why = self.decide()
        self.assertTrue(ok, why); self.assertIn("pid 62577", why)
        self.assertEqual(1, len(oos_rule.oos_rows((self.ug / "out-of-scope-changed.txt").read_text())))

    def test_DefterKaydi_KokDisindakiSahipPencereyiBulur(self):
        """G4 D8: the owner window is found through a ledger record (it lies outside every walk root)."""
        self.window()
        elsewhere = self.root / "outside"; elsewhere.mkdir()
        self.owner.rename(elsewhere / "ugp"); self.owner = elsewhere / "ugp"
        ledger = self.root / "ledger"; (ledger / "closed").mkdir(parents=True)
        (ledger / "closed" / "w1.json").write_text('{"id": "w1", "dir": "%s"}' % self.owner)
        ok, why = oos_rule.decide(self.ug, [self.root / "rc"], wait=0, sleep=lambda s: None)
        self.assertFalse(ok, why)                                      # the walk alone does not see it
        ok, why = oos_rule.decide(self.ug, [self.root / "rc"], wait=0, sleep=lambda s: None, ledgers=[ledger])
        self.assertTrue(ok, why)

    # --- 2b (WP-099, C-00 08.10): no Player alive at the change, its mtime inside another player window's span ----------
    def span_case(self, mtime="12:05:00", other_scope="player", other_restore="verified=1", other_end="12:05:01",
                  other_start="12:03:00"):
        """own editor window 12:00 → 12:10; another window (other_scope) 12:03 → other_end; the plist changed at mtime with
        no Player alive (C-00 08.10: S-03 before4 — S-01's player window restored the plist inside it, 21:10:08Z)."""
        import json as _json
        (self.ug / "window.json").write_text(_json.dumps({"scope": "editor", "utc": "2026-10-05T12:00:00Z"}))
        (self.ug / "out-of-scope-changed.txt").write_text("player  %s  %s (launch-counter keys excluded)\n" % ("a" * 64, P1))
        (self.ug / "attribution.txt").write_text("%s\tmtime %sZ\tno Player alive (Editor, or between samples)\n" % (P1, mtime))
        (self.ug / "players-lineage.tsv").write_text("")
        (self.ug / "restore.txt").write_text("2026-10-05T12:10:00Z userdata-guard restore scope=editor touched=0 verified=1\nexit=32\n")
        (self.owner / "window.json").write_text(_json.dumps({"scope": other_scope, "windowAreas": [other_scope],
                                                              "utc": "2026-10-05T%sZ" % other_start}))
        (self.owner / "restore.txt").write_text(
            "2026-10-05T%sZ userdata-guard restore scope=%s touched=1 %s extraKeys=0\nexit=0\n" % (other_end, other_scope, other_restore))

    def test_2b_CanliPlayerYok_MtimeBaskaPlayerPenceresinde_UyariylaGecer(self):
        self.span_case()
        ok, why = self.decide()
        self.assertTrue(ok, why); self.assertIn("WARNING (2b", why); self.assertIn(str(self.owner), why)

    def test_2b_PozitifKontrol_MtimePencereDisinda_Kalir(self):
        self.span_case(mtime="12:08:00")   # the other window ended 12:05:01: nobody guarded wrote this
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("lies in no other guarded player window", why)

    def test_2b_PencereVerified0_Kalir(self):
        self.span_case(other_restore="verified=0")
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("not verified=1", why)

    def test_2b_PencereEditorKapsamli_Sayilmaz_Kalir(self):
        self.span_case(other_scope="editor")   # only a player-scope window runs a Player / restores the player area
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("lies in no other guarded player window", why)

    def test_2b_SinirlarTam_BirSaniyeDisariKalir(self):
        """gt S-2 (08.10): SPAN_SLACK 0 — the other window's own seconds hold, one second outside does not."""
        for mtime, ok_want in (("12:03:00", True), ("12:05:01", True), ("12:02:59", False), ("12:05:02", False)):
            with self.subTest(mtime=mtime):
                self.span_case(mtime=mtime)
                ok, why = self.decide(); self.assertEqual(ok_want, ok, why)

    def test_2b_DigerPencereHenuzAcik_BeklerSonraGecer(self):
        """gt S-7 (08.10): the writer window is still open (no restore summary yet) — condition 3 waits for it; once it
        restores verified=1 the change passes with the warning."""
        self.span_case(mtime="12:05:00")
        (self.owner / "restore.txt").unlink()
        restored = []
        def sleep(_):
            if not restored:
                (self.owner / "restore.txt").write_text(
                    "2026-10-05T12:06:00Z userdata-guard restore scope=player touched=1 verified=1 extraKeys=0\nexit=0\n")
                restored.append(1)
        ok, why = oos_rule.decide(self.ug, [self.root], wait=60, sleep=sleep)
        self.assertTrue(ok, why); self.assertIn("WARNING (2b", why); self.assertEqual([1], restored)

    def test_2b_DigerPencereAcikKalir_Kalir(self):
        self.span_case(mtime="12:05:00")
        (self.owner / "restore.txt").unlink()
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("not restored yet", why)

    def test_2b_MtimeKendiPenceresiDisinda_Kalir(self):
        # gt S-1 (08.10): the other window DOES hold 11:50 (11:40 → 11:55) — only this window's own span (12:00 →) refuses
        self.span_case(mtime="11:50:00", other_start="11:40:00", other_end="11:55:00")
        ok, why = self.decide(); self.assertFalse(ok, why)

    def test_2b_MtimeDigerPencereAcilmadanOnce_Kalir(self):
        # gt S-1 (08.10): the other window opened at 12:06, after the change at 12:05 — its start bound refuses
        self.span_case(mtime="12:05:00", other_start="12:06:00", other_end="12:08:00")
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("lies in no other guarded player window", why)

    # --- R4 (C-00 11.10, S-07 wp101-once-v2): windows that will never restore are no writers; a restored one wins ---------
    def other(self, name, start, end=None, verified="verified=1", marker=False, resolved=False, owner=None):
        """another player-scope window under the walk root: window.json utc start (+ receipt.owner), a restore summary at
        end (None = none), the NOT-RESTORED marker (+ .resolved)."""
        import json as _json
        w = self.root / "wp-092" / "perf-td" / name / "ug"; w.mkdir(parents=True)
        doc = {"scope": "player", "windowAreas": ["player"], "utc": "2026-10-05T%sZ" % start}
        if owner:
            doc["receipt"] = {"owner": {"pid": owner[0], "lstart": owner[1]}}
        (w / "window.json").write_text(_json.dumps(doc))
        if end is not None:
            (w / "restore.txt").write_text("2026-10-05T%sZ userdata-guard restore scope=player touched=1 %s extraKeys=0\nexit=0\n" % (end, verified))
        if marker:
            (w / "userdata-NOT-RESTORED.txt").write_text("NOT restored\n")
        if resolved:
            (w / "userdata-NOT-RESTORED.resolved").write_text("C-00\n")
        return w

    def test_2b_IsaretliTerkPencere_AdayDegil_GercekVerifiedYazar(self):
        self.span_case(mtime="12:05:00")   # the real writer: self.owner 12:03 → 12:05:01, verified=1
        stale = self.other("18c8be11", "12:01:00", marker=True, resolved=True)   # opened before, never restored
        ok, why = self.decide()
        self.assertTrue(ok, why); self.assertIn(str(self.owner), why); self.assertNotIn(str(stale), why)

    def test_2b_YalnizIsaretliTerkPencere_Kalir_YazarYok(self):
        self.span_case(mtime="12:05:00", other_start="12:06:00", other_end="12:08:00")   # the real window does not hold it
        self.other("18c8be11", "12:01:00", marker=True, resolved=True)
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("lies in no other guarded player window", why)

    def test_2b_SahibiOlmusAcikPencere_AdayDegil_CanliSahipBeklenir(self):
        import os as _os, subprocess as _sp
        self.span_case(mtime="12:05:00", other_start="12:06:00", other_end="12:08:00")
        gone = _sp.Popen(["true"]); gone.wait()
        dead = self.other("dead-owner", "12:01:00", owner=(gone.pid, "Mon Oct  5 15:01:00 2026"))
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("lies in no other guarded player window", why)
        lstart = _sp.run(["ps", "-o", "lstart=", "-p", str(_os.getpid())], capture_output=True, text=True,
                         env=dict({k: v for k, v in _os.environ.items() if k != "TZ"}, LC_ALL="C")).stdout.strip()
        import json as _json
        doc = _json.loads((dead / "window.json").read_text()); doc["receipt"]["owner"] = {"pid": _os.getpid(), "lstart": lstart}
        (dead / "window.json").write_text(_json.dumps(doc))   # its owner is alive: still open, the writer, waited for
        ok, why = self.decide(); self.assertFalse(ok, why); self.assertIn("not restored yet", why); self.assertIn(str(dead), why)

    def test_2b_DefterdeKapaliRestoresizPencere_AdayDegil(self):
        self.span_case(mtime="12:05:00", other_start="12:06:00", other_end="12:08:00")
        closed = self.other("closed-no-summary", "12:01:00")
        ledger = self.root / "ledger"; (ledger / "closed").mkdir(parents=True)
        (ledger / "closed" / "c1.json").write_text('{"id": "c1", "dir": "%s"}' % closed)
        ok, why = oos_rule.decide(self.ug, [self.root], wait=0, sleep=lambda s: None, ledgers=[ledger])
        self.assertFalse(ok, why); self.assertIn("lies in no other guarded player window", why)
        ok, why = oos_rule.decide(self.ug, [self.root], wait=0, sleep=lambda s: None, ledgers=[])   # without the ledger: open
        self.assertFalse(ok, why); self.assertIn("not restored yet", why)

    def test_2b_AcikVeRestoreEdenPencere_RestoreEdenTercih(self):
        self.span_case(mtime="12:05:00")   # self.owner restored verified 12:03 → 12:05:01
        self.other("open-unknown-owner", "12:02:00")   # no restore, owner unknown: open
        ok, why = self.decide(); self.assertTrue(ok, why); self.assertIn(str(self.owner), why)

    def test_SatirYok_Kalir(self):
        self.window(rows=())
        (self.ug / "out-of-scope-changed.txt").write_text("")
        ok, why = self.decide(); self.assertFalse(ok)


if __name__ == "__main__":
    unittest.main()
