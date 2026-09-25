"""WP-078 · C-00 25.09: a perf window's user-data verdict — guard verified=1 required; a touched file is accepted only when
it is the player's PlayerPrefs plist and every changed key is Unity's window state (restored exactly by the guard)."""
import plistlib
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wp078_perf as perf  # noqa: E402


class Wp078PerfGuardTests(unittest.TestCase):
    def setUp(self):
        self.ug = Path(tempfile.mkdtemp())
        (self.ug / "snap" / "PR").mkdir(parents=True)
        with open(self.ug / "snap" / "PR" / "p1.plist", "wb") as h:
            plistlib.dump({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 2, "unity.player_session_count": 3}, h)
        self.after = self.ug / "after.plist"

    def write_after(self, values):
        with open(self.after, "wb") as h:
            plistlib.dump(values, h)

    def test_window_keys_only_is_accepted(self):
        self.write_after({"Screenmanager Fullscreen mode": 3, "UnityGraphicsQuality": 2, "unity.player_session_count": 4})
        ok, _ = perf.guard_verdict(self.ug, "restore scope=player touched=1 verified=1", self.after)
        self.assertTrue(ok)

    def test_any_other_key_stops(self):
        self.write_after({"Screenmanager Fullscreen mode": 3, "UnityGraphicsQuality": 0})
        ok, why = perf.guard_verdict(self.ug, "restore scope=player touched=1 verified=1", self.after)
        self.assertFalse(ok)
        self.assertIn("UnityGraphicsQuality", why)

    def test_unverified_restore_stops(self):
        ok, _ = perf.guard_verdict(self.ug, "restore scope=player touched=0 verified=0", self.after)
        self.assertFalse(ok)

    def test_out_of_scope_change_stops(self):
        (self.ug / "out-of-scope-changed.txt").write_text("D1/ui-settings.json\n")
        ok, _ = perf.guard_verdict(self.ug, "restore scope=player touched=0 verified=1", self.after)
        self.assertFalse(ok)

    def test_untouched_verified_passes(self):
        ok, _ = perf.guard_verdict(self.ug, "restore scope=player touched=0 verified=1", self.after)
        self.assertTrue(ok)


if __name__ == "__main__":
    unittest.main()
