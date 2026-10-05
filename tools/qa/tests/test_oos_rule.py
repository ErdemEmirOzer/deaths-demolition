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

    def test_SatirYok_Kalir(self):
        self.window(rows=())
        (self.ug / "out-of-scope-changed.txt").write_text("")
        ok, why = self.decide(); self.assertFalse(ok)


if __name__ == "__main__":
    unittest.main()
