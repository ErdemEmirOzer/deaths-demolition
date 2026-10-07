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


GAME_KEY = "DD.Prototype.DirectToRun"   # a key the game itself writes (PrototypeBootFlags) — never an engine key


class VerdictTests(unittest.TestCase):
    def setUp(self):
        self.ug = Path(tempfile.mkdtemp())
        (self.ug / "snap" / "PR").mkdir(parents=True)
        self.before({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 2, GAME_KEY: 0, "unity.player_session_count": 3})
        self.after = self.ug / "after.plist"

    def before(self, values):
        with open(self.ug / "snap" / "PR" / "p1.plist", "wb") as h:
            plistlib.dump(values, h)

    def after_keys(self, values):
        with open(self.after, "wb") as h:
            plistlib.dump(values, h)

    def test_window_keys_only_is_accepted(self):
        self.after_keys({"Screenmanager Fullscreen mode": 3, "UnityGraphicsQuality": 2, GAME_KEY: 0, "unity.player_session_count": 4})
        self.assertTrue(gr.verdict(self.ug, "restore touched=1 verified=1", self.after)[0])

    def test_any_other_key_stops(self):
        self.after_keys({"Screenmanager Fullscreen mode": 3, "UnityGraphicsQuality": 2, GAME_KEY: 1})
        ok, why = gr.verdict(self.ug, "restore touched=1 verified=1", self.after)
        self.assertFalse(ok)
        self.assertIn(GAME_KEY, why)

    def test_quality_level_of_a_tier_probe_is_an_engine_key(self):
        """C-00 07.10 (S-05 Yüksek TD probe): -ddQualityTier yuksek → the Player saved UnityGraphicsQuality 2 → 1; the guard
        restored it (verified=1) — the window is accepted, not STOP 7."""
        self.after_keys({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 1, GAME_KEY: 0})
        self.assertEqual((True, "touched=1 engine window keys only"), gr.verdict(self.ug, "restore touched=1 verified=1", self.after))
        self.assertEqual("UnityGraphicsQuality\n", (self.ug / "prefs-diff.txt").read_text(encoding="utf-8"))

    def test_quality_level_added_by_the_run_is_an_engine_key(self):
        # a profile that never had the key: the run adds it (the guard's exact restore deletes it again, WP-066J)
        self.before({"Screenmanager Fullscreen mode": 1, GAME_KEY: 0})
        self.after_keys({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 1, GAME_KEY: 0})
        self.assertTrue(gr.verdict(self.ug, "restore touched=1 verified=1", self.after)[0])

    def test_quality_level_with_a_game_key_still_stops(self):
        self.after_keys({"Screenmanager Fullscreen mode": 3, "UnityGraphicsQuality": 1, GAME_KEY: 1})
        ok, why = gr.verdict(self.ug, "restore touched=1 verified=1", self.after)
        self.assertFalse(ok)
        self.assertIn(GAME_KEY, why)

    def test_quality_level_needs_a_verified_restore(self):
        self.after_keys({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 1, GAME_KEY: 0})
        self.assertEqual((False, "guard verified!=1"), gr.verdict(self.ug, "restore touched=1 verified=0", self.after))

    def test_engine_keys_are_matched_exactly(self):
        # only the engine's own spellings: a game key that merely contains the name is not one
        for key in ("UnityGraphicsQuality.dd", "DD.UnityGraphicsQuality", "unitygraphicsquality", "UnitySelectMonitor2"):
            self.assertFalse(gr.is_engine_key(key), key)
        for key in ("UnityGraphicsQuality", "UnitySelectMonitor", "Screenmanager Resolution Width"):
            self.assertTrue(gr.is_engine_key(key), key)

    def test_touched_without_a_plist_change_stops(self):
        # the one behaviour for an empty key list (M-17): a file other than the plist changed → STOP
        self.after_keys({"Screenmanager Fullscreen mode": 1, "UnityGraphicsQuality": 2, GAME_KEY: 0})
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

    def test_restore_32_reaches_the_verdict_which_stops_on_the_out_of_scope_change(self):
        """G4 D7: 32 (in scope verified, another area changed) is not retried and goes to verdict() like 0 — which stops
        the acceptance window on out-of-scope changes, exactly as before D7 (when this case exited 0)."""
        ug = Path(tempfile.mkdtemp())
        line = "userdata-guard restore scope=player touched=0 verified=1 retry=none class=OTHER"
        with mock.patch.object(gr, "run_guard", return_value=done(32, line)) as guard:
            code, _ = gr.restore_with_retry(ug, attempts=5, sleep=lambda s: None)
        self.assertEqual((0, 1), (code, guard.call_count))
        (ug / "out-of-scope-changed.txt").write_text("editor  MISSING  /x\n")
        with mock.patch.object(gr, "run_guard", return_value=done(32, line)), mock.patch.object(gr, "export_prefs"):
            receipt = gr.close(ug)
        self.assertEqual((7, "out-of-scope changes", 32, True), (receipt["code"], receipt["why"], receipt["restoreExit"],
                                                                 receipt["outOfScopeChanged"]))

    def test_restore_32_after_a_retry_stops_although_the_first_list_is_empty(self):
        """D7 review SHOULD-1: a retry publishes its list as out-of-scope-changed.txt.N; the summary line decides."""
        ug = Path(tempfile.mkdtemp())
        (ug / "out-of-scope-changed.txt").write_text("")
        (ug / "out-of-scope-changed.txt.2").write_text("editor  MISSING  /x\n")
        line = "userdata-guard restore scope=player touched=0 verified=1 extraKeys=0 outOfScopeChanged=1 retry=none class=OTHER"
        with mock.patch.object(gr, "run_guard", return_value=done(32, line)), mock.patch.object(gr, "export_prefs"):
            receipt = gr.close(ug)
        self.assertEqual((7, "out-of-scope changes", True), (receipt["code"], receipt["why"], receipt["outOfScopeChanged"]))


def perf_args():
    return argparse.Namespace(dry_run=False, player_wait=0, commit="a" * 40, load_max=6.0)


class PerfRestoreOnEveryPathTests(unittest.TestCase):
    def run_window(self, launch):
        root = Path(tempfile.mkdtemp())
        closed = []
        with mock.patch.object(wp078_perf, "players", return_value=[]), \
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
            with mock.patch.object(wp078_perf, "players", return_value=[]), \
                    mock.patch.object(gr, "run_guard", return_value=done(snapshot)), \
                    mock.patch.object(wp078_perf, "launch") as launch:
                code = wp078_perf.run_window(perf_args(), root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1)
            self.assertEqual(expected, code)
            launch.assert_not_called()

    def test_restore_failure_stops_the_matrix(self):
        root = Path(tempfile.mkdtemp())
        with mock.patch.object(wp078_perf, "players", return_value=[]), \
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
                mock.patch.object(wp078_perf, "players", return_value=[]), \
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
        with mock.patch.object(wp078_perf, "players", return_value=[]), \
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
        with mock.patch.object(wp078_perf, "players", return_value=[]), \
                mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", return_value={"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_perf, "launch", side_effect=lambda cmd: seen.append(cmd) or done(0)):
            wp078_perf.run_window(a, root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "topdown", 1)
        self.assertIn("--extra-arg=-ddPilotStart", seen[0])
        self.assertIn("--extra-arg=48,48,225", seen[0])


class BotNotStartedIsAWaitTests(unittest.TestCase):
    """prova 01.10: player-run refusing before the Player starts (disk gate 75 / live Player 30) is a wait, not exit 1."""

    def run_bot(self, player_exit, write_log):
        import json
        out = Path(tempfile.mkdtemp())
        args = argparse.Namespace(timeout=60, quiet=False, load_max=6.0, kind="route", mode="fp")

        class Done:
            def poll(self):
                return player_exit

            def wait(self, timeout=None):
                if write_log:
                    (out / "player.log").write_text("ran")
                return player_exit
        with mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(wp078_perf, "players", return_value=[]), \
                mock.patch.object(gr, "close", return_value={"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_bot, "launch", return_value=Done()):
            code = wp078_bot.run_guarded(args, out, ["/b/P"], {"status": "starting"})
        return code, json.loads((out / "attempt.json").read_text())

    def test_disk_gate_refusal_returns_75_not_started(self):
        code, attempt = self.run_bot(75, write_log=False)
        self.assertEqual((75, "not-started"), (code, attempt["status"]))

    def test_live_player_refusal_returns_30(self):
        self.assertEqual(30, self.run_bot(30, write_log=False)[0])

    def test_a_player_that_ran_and_exited_75_is_a_failed_run(self):
        code, attempt = self.run_bot(75, write_log=True)
        self.assertEqual((1, "failed"), (code, attempt["status"]))


class BotInputLostIsAWaitTests(unittest.TestCase):
    """S-02 01.10 input-loss contract: a run whose bot input never reached the game is UNMEASURED — exit 75 (retried)."""

    def run_bot(self, measurable):
        import json
        out = Path(tempfile.mkdtemp())
        args = argparse.Namespace(timeout=60, quiet=False, load_max=6.0, kind="gameplay", mode="fp")

        class Ran:
            def poll(self):
                return 3

            def wait(self, timeout=None):
                for name in wp078_bot.REQUIRED:
                    (out / name).write_text("{}" if name.endswith(".json") else "x")
                (out / "bot-summary.json").write_text(json.dumps({"inputMeasurable": measurable, "padDisabledFrames": 0,
                                                                  "pointerAfterPadFrames": 1587, "unfocusedFrames": 12, "pass": False}))
                return 3
        # S-10 gt N-2 (C-00 06.10): the restore half asks wp078_perf.players() for live game Players — the machine's real
        # ones without this patch (another session's Player made both tests 65 after a 60 s wait); hermetic: none
        with mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", return_value={"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_perf, "players", return_value=[]), \
                mock.patch.object(wp078_bot, "launch", return_value=Ran()):
            code = wp078_bot.run_guarded(args, out, ["/b/P"], {"status": "starting"})
        return code, json.loads((out / "attempt.json").read_text())

    def test_input_lost_is_unmeasured_75(self):
        code, attempt = self.run_bot(False)
        self.assertEqual((75, "unmeasurable"), (code, attempt["status"]))
        self.assertEqual(1587, attempt["inputLoss"]["pointerAfterPadFrames"])

    def test_measurable_bad_run_stays_a_failure(self):
        code, attempt = self.run_bot(True)
        self.assertEqual((1, "failed"), (code, attempt["status"]))
