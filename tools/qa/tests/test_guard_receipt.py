"""WP-078 (C-00 30.09 M-14, M-17): the one guard receipt module and the runners' restore-on-every-path contract.
Fake guard and fake launchers only — no Unity, no Player, no real userdata-guard."""
import argparse
import plistlib
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import guard_receipt as gr  # noqa: E402
import wp078_bot  # noqa: E402
import wp078_perf  # noqa: E402


def done(code, out=""):
    return subprocess.CompletedProcess([], code, out, "")


class VerdictTests(unittest.TestCase):
    def setUp(self):
        self.ug = Path(tempfile.mkdtemp())
        (self.ug / "snap" / "PR").mkdir(parents=True)
        with open(self.ug / "snap" / "PR" / "p1.plist", "wb") as h:
            plistlib.dump({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 2, "unity.player_session_count": 3}, h)
        self.after = self.ug / "after.plist"

    def after_keys(self, values):
        with open(self.after, "wb") as h:
            plistlib.dump(values, h)

    def test_window_keys_only_is_accepted(self):
        self.after_keys({"Screenmanager Fullscreen mode": 3, "UnityGraphicsQuality": 2, "unity.player_session_count": 4})
        self.assertTrue(gr.verdict(self.ug, "restore touched=1 verified=1", self.after)[0])

    def test_any_other_key_stops(self):
        self.after_keys({"Screenmanager Fullscreen mode": 3, "UnityGraphicsQuality": 0})
        ok, why = gr.verdict(self.ug, "restore touched=1 verified=1", self.after)
        self.assertFalse(ok)
        self.assertIn("UnityGraphicsQuality", why)

    def test_touched_without_a_plist_change_stops(self):
        # the one behaviour for an empty key list (M-17): a file other than the plist changed → STOP
        self.after_keys({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 2})
        ok, why = gr.verdict(self.ug, "restore touched=1 verified=1", self.after)
        self.assertFalse(ok)
        self.assertIn("non-plist", why)

    def test_unverified_and_out_of_scope_stop(self):
        self.assertFalse(gr.verdict(self.ug, "restore touched=0 verified=0", self.after)[0])
        (self.ug / "out-of-scope-changed.txt").write_text("D1/ui-settings.json\n")
        self.assertFalse(gr.verdict(self.ug, "restore touched=0 verified=1", self.after)[0])

    def test_untouched_verified_passes(self):
        self.assertTrue(gr.verdict(self.ug, "restore touched=0 verified=1", self.after)[0])


class CodeMappingTests(unittest.TestCase):
    def test_snapshot_codes(self):
        self.assertEqual([0, 30, 75, 7, 7, 7], [gr.snapshot_code(c) for c in (0, 30, 75, 31, 64, 1)])

    def test_restore_retries_75_then_succeeds(self):
        ug = Path(tempfile.mkdtemp())
        calls, sleeps = [done(75), done(75), done(0, "restore touched=0 verified=1")], []
        with mock.patch.object(gr, "run_guard", side_effect=calls):
            code, result = gr.restore_with_retry(ug, attempts=5, sleep=sleeps.append)
        self.assertEqual(0, code)
        self.assertEqual(2, len(sleeps))

    def test_restore_75_forever_is_a_stop_with_the_warning(self):
        ug = Path(tempfile.mkdtemp())
        with mock.patch.object(gr, "run_guard", return_value=done(75)):
            code, _ = gr.restore_with_retry(ug, attempts=3, sleep=lambda s: None)
        self.assertEqual(7, code)
        self.assertIn("VERİ GERİ YÜKLENMEDİ", (ug / "restore.txt").read_text(encoding="utf-8"))

    def test_other_restore_errors_stop_without_retry(self):
        ug = Path(tempfile.mkdtemp())
        with mock.patch.object(gr, "run_guard", return_value=done(31)) as guard:
            code, _ = gr.restore_with_retry(ug, attempts=5, sleep=lambda s: None)
        self.assertEqual(7, code)
        self.assertEqual(1, guard.call_count)


def perf_args():
    return argparse.Namespace(dry_run=False, player_wait=0, commit="a" * 40, load_max=6.0)


class PerfRestoreOnEveryPathTests(unittest.TestCase):
    def run_window(self, launch):
        root = Path(tempfile.mkdtemp())
        closed = []
        with mock.patch.object(wp078_perf, "player_up", return_value=False), \
                mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", side_effect=lambda ug, *a, **k: closed.append(ug) or {"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_perf, "launch", side_effect=launch):
            try:
                result = wp078_perf.run_window(perf_args(), root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1)
            except BaseException as error:   # noqa: B902 — the test checks what escapes
                return error, closed
        return result, closed

    def test_keyboard_interrupt_still_restores(self):
        outcome, closed = self.run_window(KeyboardInterrupt)
        self.assertIsInstance(outcome, KeyboardInterrupt)
        self.assertEqual(1, len(closed))

    def test_sigterm_as_systemexit_143_still_restores(self):
        outcome, closed = self.run_window(SystemExit(143))
        self.assertIsInstance(outcome, SystemExit)
        self.assertEqual(143, outcome.code)
        self.assertEqual(1, len(closed))

    def test_normal_window_restores_once(self):
        outcome, closed = self.run_window(lambda cmd: done(0))
        self.assertIsNone(outcome)
        self.assertEqual(1, len(closed))

    def test_snapshot_wait_states_and_errors_launch_nothing(self):
        for snapshot, expected in ((30, 30), (75, 75), (31, 7)):
            root = Path(tempfile.mkdtemp())
            with mock.patch.object(wp078_perf, "player_up", return_value=False), \
                    mock.patch.object(gr, "run_guard", return_value=done(snapshot)), \
                    mock.patch.object(wp078_perf, "launch") as launch:
                code = wp078_perf.run_window(perf_args(), root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1)
            self.assertEqual(expected, code)
            launch.assert_not_called()

    def test_restore_failure_stops_the_matrix(self):
        root = Path(tempfile.mkdtemp())
        with mock.patch.object(wp078_perf, "player_up", return_value=False), \
                mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", return_value={"code": 7, "why": "guard restore exit 31"}), \
                mock.patch.object(wp078_perf, "launch", return_value=done(0)):
            self.assertEqual(7, wp078_perf.run_window(perf_args(), root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1))


class BotRestoreOnEveryPathTests(unittest.TestCase):
    def test_interrupt_during_the_bot_run_still_restores(self):
        out = Path(tempfile.mkdtemp())
        closed = []
        args = argparse.Namespace(timeout=60, quiet=False, load_max=6.0, kind="gameplay", mode="tp")
        with mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", side_effect=lambda ug, *a, **k: closed.append(ug) or {"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_bot, "launch", side_effect=KeyboardInterrupt):
            with self.assertRaises(KeyboardInterrupt):
                wp078_bot.run_guarded(args, out, ["/b/P"], {"status": "starting"})
        self.assertEqual(1, len(closed))


if __name__ == "__main__":
    unittest.main()


class PerfAttemptAnnotationTests(unittest.TestCase):
    """WP-078 A4: wp078_perf adds the window's guard receipt and its evidenced scope to wp064_run's attempt.json."""

    def test_receipt_and_scope_come_from_the_windows_own_evidence(self):
        import json
        cell = Path(tempfile.mkdtemp())
        (cell / "attempt.json").write_text(json.dumps({"status": "complete", "exitCode": 0}))
        (cell / "command.json").write_text(json.dumps(["/b/P", "-ddPilotResolution", "1920x1080", "-ddPilotWarmupSeconds", "10",
                                                       "-ddPilotWindowSeconds", "60"]))
        (cell / "02-force-40.json").write_text(json.dumps({"achievedCount": 40, "forcedTarget": 40}))
        wp078_perf.annotate_attempt(cell, {"code": 0, "why": "touched=0", "restoreExit": 0, "verified": True, "touched": "0",
                                           "outOfScopeChanged": False})
        a = json.loads((cell / "attempt.json").read_text())
        self.assertEqual({"snapshotExit": 0, "restoreExit": 0, "verified": True, "outOfScopeChanged": False},
                         {k: a["guard"][k] for k in ("snapshotExit", "restoreExit", "verified", "outOfScopeChanged")})
        self.assertEqual(([1920, 1080], 40, 10, 60, "probe"),
                         (a["resolution"], a["activeEnemies"], a["warmupSeconds"], a["windowSeconds"], a["buildRole"]))

    def test_missing_sources_leave_fields_out(self):
        import json
        cell = Path(tempfile.mkdtemp())
        (cell / "attempt.json").write_text(json.dumps({"status": "failed", "exitCode": 1}))
        wp078_perf.annotate_attempt(cell, {"code": 7, "why": "x", "restoreExit": 31, "verified": False, "touched": None,
                                           "outOfScopeChanged": False})
        a = json.loads((cell / "attempt.json").read_text())
        self.assertNotIn("activeEnemies", a)
        self.assertNotIn("resolution", a)
        self.assertEqual(31, a["guard"]["restoreExit"])

    def test_guard_folders_are_not_named_like_perf_windows(self):
        import re
        root = Path(tempfile.mkdtemp())
        seen = []
        with mock.patch.object(wp078_perf, "player_up", return_value=False), \
                mock.patch.object(gr, "run_guard", side_effect=lambda cmd, ug, *a: seen.append(Path(ug).name) or done(0)), \
                mock.patch.object(gr, "close", return_value={"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_perf, "launch", return_value=done(0)):
            wp078_perf.run_window(perf_args(), root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1)
        self.assertTrue(seen and not re.fullmatch(r"perf-[a-z]+-[a-z]+-\d+", seen[0]))


class PerfExtraArgTests(unittest.TestCase):
    """S-05 B9 (30.09): caller probe flags reach every window after the fixed tier/mode/sandbox arguments."""

    def test_extra_args_follow_the_fixed_window_args(self):
        args = wp078_perf.window_args("yuksek", "topdown", Path("/c"), ["-ddPilotStart", "-48,-48,45"])
        self.assertEqual(["-ddPilotStart", "-48,-48,45"], args[-2:])
        self.assertEqual("yuksek", args[args.index("-ddQualityTier") + 1])

    def test_run_window_passes_them_in_the_equals_form(self):
        root = Path(tempfile.mkdtemp())
        seen = []
        a = perf_args()
        a.extra_arg = ["-ddPilotStart", "48,48,225"]
        with mock.patch.object(wp078_perf, "player_up", return_value=False), \
                mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", return_value={"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_perf, "launch", side_effect=lambda cmd: seen.append(cmd) or done(0)):
            wp078_perf.run_window(a, root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "topdown", 1)
        self.assertIn("--extra-arg=-ddPilotStart", seen[0])
        self.assertIn("--extra-arg=48,48,225", seen[0])
