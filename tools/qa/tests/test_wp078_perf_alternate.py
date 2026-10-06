"""WP-091 (C-00 05.10): the R1/R2 alternating perf runner — R2 and R1 windows interleave per cell in the same quiet
window, each side in its own root under its own commit. Fake windows only — no Player, no guard."""
import argparse
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wp078_perf  # noqa: E402


def run_args(**extra):
    base = dict(dry_run=False, max_discards=1, cooldown=0.0, commit="a" * 40)
    base.update(extra)
    return argparse.Namespace(**base)


class AlternateTests(unittest.TestCase):
    def setUp(self):
        self.r2, self.r1 = Path(tempfile.mkdtemp()), Path(tempfile.mkdtemp())
        self.counted = {}      # (root, cell) -> counted windows
        self.calls = []        # (side, commit, cell, index)
        self.estimates = []    # the quiet-batch estimate each window was given

    def fake_state(self, root, name, mode):
        n = self.counted.get((root, f"{name}-{mode}"), 0)
        return n, n + 1

    def fake_window(self, discard=()):
        def window(args, root, binary, seconds, quiet, note, name, mode, index, estimate=None):
            side = "r2" if root == self.r2 else "r1"
            self.calls.append((side, args.commit, f"{name}-{mode}", index))
            self.estimates.append(estimate)
            if (side, f"{name}-{mode}") not in discard:
                key = (root, f"{name}-{mode}")
                self.counted[key] = self.counted.get(key, 0) + 1
            return None
        return window

    def matrix(self, runs, discard=()):
        args = run_args()
        baseline = ("r1:", run_args(commit="b" * 40), self.r1, Path("/r1/P"))
        window = self.fake_window(discard)
        notes = []

        def kind(root, name, mode, index):   # a fake window writes no attempt.json: what it was, from the fake
            return "discarded" if ("r2" if root == self.r2 else "r1", f"{name}-{mode}") in discard else "complete"
        with mock.patch.object(wp078_perf, "cell_state", side_effect=self.fake_state), \
                mock.patch.object(wp078_perf, "run_window", side_effect=window), \
                mock.patch.object(wp078_perf, "window_kind", side_effect=kind), \
                mock.patch.object(wp078_perf.time, "sleep"):
            code = wp078_perf.run_all(args, runs, self.r2, Path("/r2/P"), 60, None, notes.append, baseline)
        return code, notes

    def test_windows_alternate_abba_per_cell_and_swap_the_start_side_per_cell(self):
        # KADRO game-test-engineer SHOULD-1: R2 R1 R1 R2 R2 R1 in the first cell, R1 R2 … in the next
        code, notes = self.matrix([("yuksek", "tp", 3), ("dusuk", "topdown", 2)])
        self.assertEqual(0, code)
        self.assertEqual([("r2", "a" * 40, "yuksek-tp", 1), ("r1", "b" * 40, "yuksek-tp", 1),
                          ("r1", "b" * 40, "yuksek-tp", 2), ("r2", "a" * 40, "yuksek-tp", 2),
                          ("r2", "a" * 40, "yuksek-tp", 3), ("r1", "b" * 40, "yuksek-tp", 3),
                          ("r1", "b" * 40, "dusuk-topdown", 1), ("r2", "a" * 40, "dusuk-topdown", 1),
                          ("r2", "a" * 40, "dusuk-topdown", 2), ("r1", "b" * 40, "dusuk-topdown", 2)], self.calls)
        # SHOULD-2: the second window of every pair has estimate 0 (the pair was budgeted at its start)
        self.assertEqual([60 + wp078_perf.WINDOW_OVERHEAD_S, 0] * 5, self.estimates)
        self.assertIn("perf-yuksek-tp: 3/3 counted", " ".join(notes))
        self.assertIn("r1:perf-dusuk-topdown: 2/2 counted", " ".join(notes))

    def test_every_pair_is_budgeted_whole_before_its_first_window(self):
        # gt 06.10 N-5 (mutant pair-nobudget): the pair's two windows are asked for at its start, in one budget
        asked = []
        quiet = mock.Mock(before_window=lambda seconds: asked.append(seconds) or "quiet")
        with mock.patch.object(wp078_perf, "cell_state", side_effect=self.fake_state), \
                mock.patch.object(wp078_perf, "run_window", side_effect=self.fake_window()), \
                mock.patch.object(wp078_perf, "window_kind", return_value="complete"), \
                mock.patch.object(wp078_perf.time, "sleep"):
            wp078_perf.run_all(run_args(), [("yuksek", "tp", 2)], self.r2, Path("/r2/P"), 60, quiet, lambda s: None,
                               ("r1:", run_args(commit="b" * 40), self.r1, Path("/r1/P")))
        self.assertEqual([2 * (60 + wp078_perf.WINDOW_OVERHEAD_S)] * 2, asked)   # two pairs, each budgeted once, whole
        self.assertEqual([60 + wp078_perf.WINDOW_OVERHEAD_S, 0] * 2, self.estimates)

    def test_window_kind_is_the_reports_classification_of_the_window_just_run(self):
        cell = self.r1 / "perf-yuksek-tp-4"
        cell.mkdir(parents=True)
        for attempt, kind in (({"status": "complete", "exitCode": 0, "hostClean": True}, "complete"),
                              ({"status": "preflight-failed", "exitCode": 75}, "discarded"),
                              ({"status": "failed", "exitCode": 1, "hostClean": True}, "failed")):
            (cell / "attempt.json").write_text(json.dumps(attempt))
            self.assertEqual(kind, wp078_perf.window_kind(self.r1, "yuksek", "tp", 4))
        self.assertEqual("failed", wp078_perf.window_kind(self.r1, "yuksek", "tp", 5))   # no attempt.json at all

    def test_a_side_already_complete_is_not_rerun_on_resume(self):
        self.counted[(self.r1, "yuksek-tp")] = 2
        self.matrix([("yuksek", "tp", 2)])
        self.assertEqual(["r2", "r2"], [c[0] for c in self.calls])

    def test_discards_on_one_side_block_only_that_side(self):
        code, notes = self.matrix([("yuksek", "tp", 1)], discard={("r1", "yuksek-tp")})
        self.assertEqual(0, code)
        self.assertEqual(["r2", "r1", "r1"], [c[0] for c in self.calls])   # max_discards=1 → one re-run
        self.assertEqual([60 + wp078_perf.WINDOW_OVERHEAD_S, 0, 60 + wp078_perf.WINDOW_OVERHEAD_S], self.estimates)   # a lone re-run is no pair
        joined = " ".join(notes)
        self.assertIn("perf-yuksek-tp: 1/1 counted", joined)
        self.assertIn("r1:perf-yuksek-tp: 0/1 counted after 1 re-runs — BLOCKED", joined)

    def test_a_stop_code_from_any_window_stops_the_whole_matrix(self):
        def window(args, root, *rest):
            self.calls.append(root)
            return 65 if root == self.r1 else None
        with mock.patch.object(wp078_perf, "cell_state", side_effect=self.fake_state), \
                mock.patch.object(wp078_perf, "run_window", side_effect=window), \
                mock.patch.object(wp078_perf.time, "sleep"):
            code = wp078_perf.run_all(run_args(), [("yuksek", "tp", 3)], self.r2, Path("/r2/P"), 60, None, lambda s: None,
                                      ("r1:", run_args(commit="b" * 40), self.r1, Path("/r1/P")))
        self.assertEqual(65, code)
        self.assertEqual([self.r2, self.r1], self.calls)

    def kinds_matrix(self, runs, r1_kinds, **extra):
        """r1_kinds: what each R1 window turns out to be (complete / discarded / failed), in order; FINAL's are complete."""
        kinds = list(r1_kinds)
        args = run_args(**extra)
        baseline = ("r1:", run_args(commit="b" * 40), self.r1, Path("/r1/P"))
        notes, discard = [], set()

        def window(args, root, binary, seconds, quiet, note, name, mode, index, estimate=None):
            side = "r2" if root == self.r2 else "r1"
            self.calls.append((side, args.commit, f"{name}-{mode}", index))
            if side == "r2" or kinds[0] == "complete":
                key = (root, f"{name}-{mode}")
                self.counted[key] = self.counted.get(key, 0) + 1
            return None

        def kind(root, name, mode, index):
            return kinds.pop(0) if root == self.r1 else "complete"
        with mock.patch.object(wp078_perf, "cell_state", side_effect=self.fake_state), \
                mock.patch.object(wp078_perf, "run_window", side_effect=window), \
                mock.patch.object(wp078_perf, "window_kind", side_effect=kind), \
                mock.patch.object(wp078_perf.time, "sleep"):
            code = wp078_perf.run_all(args, runs, self.r2, Path("/r2/P"), 60, None, notes.append, baseline)
        return code, notes

    def test_an_r1_build_that_keeps_failing_stops_its_side_and_final_goes_on(self):
        # KADRO test-engineer NOTE-4: max_discards 6 would run 7 empty R1 windows per cell; three in a row stop the side
        code, notes = self.kinds_matrix([("yuksek", "tp", 3), ("dusuk", "topdown", 2)], ["failed"] * 20, max_discards=6)
        self.assertEqual(0, code)
        self.assertEqual(3, sum(1 for c in self.calls if c[0] == "r1"))
        self.assertEqual(5, sum(1 for c in self.calls if c[0] == "r2"))   # FINAL's 3 + 2 counted windows all ran
        joined = " ".join(notes)
        self.assertIn("3 consecutive R1 windows failed on the build, not the host — the R1 side stops", joined)
        self.assertIn("perf-dusuk-topdown: 2/2 counted", joined)
        self.assertNotIn("r1:perf-dusuk-topdown", joined)

    def test_host_discards_and_a_counted_window_never_add_up_to_a_stop(self):
        kinds = ["failed", "failed", "discarded", "complete", "failed", "failed", "complete", "complete"]
        code, notes = self.kinds_matrix([("yuksek", "tp", 3)], kinds, max_discards=6)
        self.assertEqual(0, code)
        self.assertNotIn("the R1 side stops", " ".join(notes))
        self.assertIn("r1:perf-yuksek-tp: 3/3 counted", " ".join(notes))

    def test_the_stop_threshold_is_the_cli_value(self):
        code, notes = self.kinds_matrix([("yuksek", "tp", 3)], ["failed"] * 20, max_discards=6, baseline_fail_stop=5)
        self.assertEqual(5, sum(1 for c in self.calls if c[0] == "r1"))
        self.assertIn("5 consecutive R1 windows failed", " ".join(notes))

    def test_without_a_baseline_the_runner_is_unchanged(self):
        with mock.patch.object(wp078_perf, "cell_state", side_effect=self.fake_state), \
                mock.patch.object(wp078_perf, "run_window", side_effect=self.fake_window()), \
                mock.patch.object(wp078_perf.time, "sleep"):
            code = wp078_perf.run_all(run_args(), [("yuksek", "tp", 2)], self.r2, Path("/r2/P"), 60, None, lambda s: None)
        self.assertEqual(0, code)
        self.assertEqual(["r2", "r2"], [c[0] for c in self.calls])


class MainWiringTests(unittest.TestCase):
    """gt 06.10 N-5 (mutant quiet-cli-notpassed): the quiet options reach the QuietWindow."""

    def test_the_quiet_options_reach_the_quiet_window(self):
        made = []
        out = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, out, True)
        argv = ["wp078_perf.py", "--bin", "/nowhere/P", "--out", str(out), "--commit", "a" * 40, "--load-max", "4.5",
                "--quiet-minutes", "21", "--quiet-pause", "99", "--quiet-wait-max", "777"]
        with mock.patch.object(sys, "argv", argv), \
                mock.patch.object(wp078_perf, "QuietWindow", side_effect=lambda *a: made.append(a) or mock.Mock()), \
                mock.patch.object(wp078_perf.guard_receipt, "canon_refusal", return_value=None), \
                mock.patch.object(wp078_perf, "resolve_binary", side_effect=Path), \
                mock.patch.object(wp078_perf, "run_all", return_value=0):
            self.assertEqual(0, wp078_perf.main())
        self.assertEqual(1, len(made))
        self.assertEqual((4.5, 21.0, 99.0, 777.0), made[0][2:])


class FailStopWiringTests(unittest.TestCase):
    def test_a_fail_stop_below_one_is_refused(self):
        # gt 06.10 KP-10: 0 would stop the R1 side after its first window, whatever that window was
        out = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, out, True)
        argv = ["wp078_perf.py", "--bin", "/nowhere/P", "--out", str(out), "--commit", "a" * 40, "--baseline-fail-stop", "0"]
        with mock.patch.object(sys, "argv", argv), \
                mock.patch.object(wp078_perf.guard_receipt, "canon_refusal", return_value=None), \
                mock.patch.object(wp078_perf, "run_all", side_effect=AssertionError("must not run")):
            self.assertEqual(64, wp078_perf.main())


class QuietWindowTests(unittest.TestCase):
    """KADRO game-test-engineer SHOULD-2/-4: the batch budget and the bounded quiet wait (no slot.sh, no host)."""

    def window(self, quiet_after, wait_max=30.0):
        notes, checks = [], []
        q = wp078_perf.QuietWindow(notes.append, Path(tempfile.mkdtemp()) / ".hold", 6.0, 25.0, 180.0, wait_max)
        taken, given = [], []

        def host():
            checks.append(1)
            return len(checks) > quiet_after, f"load1 x{len(checks)}"
        q._host_quiet, q._take_slots, q._give_back = host, lambda: taken.append(1), lambda: given.append(1)
        return q, notes, taken, given

    def test_a_host_that_never_quiets_gives_every_slot_back_and_retries(self):
        q, notes, taken, given = self.window(quiet_after=5, wait_max=30.0)   # 30 s = 2 sleeps of 15 s per hold
        with mock.patch.object(wp078_perf.time, "sleep"):
            q.acquire()
        self.assertEqual((2, 1), (len(taken), len(given)))   # held, waited out, gave back, took again
        self.assertIn("every slot given back", " ".join(notes))
        self.assertIsNotNone(q.started)

    def test_the_second_window_of_a_pair_is_never_a_batch_boundary(self):
        q, notes, taken, given = self.window(quiet_after=0)
        with mock.patch.object(wp078_perf.time, "sleep"):
            q.acquire()
            q.started -= 26 * 60          # the 25-minute batch is over (gt 06.10 N-5: at 24 an ignored 0 passed too)
            q.before_window(0)            # inside a pair: still no release
            self.assertEqual(0, len(given))
            q.before_window(200)          # a new window that does not fit: released and taken again
            self.assertEqual((1, 2), (len(given), len(taken)))


if __name__ == "__main__":
    unittest.main()
