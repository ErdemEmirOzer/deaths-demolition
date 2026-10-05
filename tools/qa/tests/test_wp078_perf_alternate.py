"""WP-091 (C-00 05.10): the R1/R2 alternating perf runner — R2 and R1 windows interleave per cell in the same quiet
window, each side in its own root under its own commit. Fake windows only — no Player, no guard."""
import argparse
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

    def fake_state(self, root, name, mode):
        n = self.counted.get((root, f"{name}-{mode}"), 0)
        return n, n + 1

    def fake_window(self, discard=()):
        def window(args, root, binary, seconds, quiet, note, name, mode, index):
            side = "r2" if root == self.r2 else "r1"
            self.calls.append((side, args.commit, f"{name}-{mode}", index))
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
        with mock.patch.object(wp078_perf, "cell_state", side_effect=self.fake_state), \
                mock.patch.object(wp078_perf, "run_window", side_effect=window), \
                mock.patch.object(wp078_perf.time, "sleep"):
            code = wp078_perf.run_all(args, runs, self.r2, Path("/r2/P"), 60, None, notes.append, baseline)
        return code, notes

    def test_windows_alternate_r2_then_r1_per_cell_each_under_its_own_commit(self):
        code, notes = self.matrix([("yuksek", "tp", 2), ("dusuk", "topdown", 1)])
        self.assertEqual(0, code)
        self.assertEqual([("r2", "a" * 40, "yuksek-tp", 1), ("r1", "b" * 40, "yuksek-tp", 1),
                          ("r2", "a" * 40, "yuksek-tp", 2), ("r1", "b" * 40, "yuksek-tp", 2),
                          ("r2", "a" * 40, "dusuk-topdown", 1), ("r1", "b" * 40, "dusuk-topdown", 1)], self.calls)
        self.assertIn("perf-yuksek-tp: 2/2 counted", " ".join(notes))
        self.assertIn("r1:perf-yuksek-tp: 2/2 counted", " ".join(notes))

    def test_a_side_already_complete_is_not_rerun_on_resume(self):
        self.counted[(self.r1, "yuksek-tp")] = 2
        self.matrix([("yuksek", "tp", 2)])
        self.assertEqual(["r2", "r2"], [c[0] for c in self.calls])

    def test_discards_on_one_side_block_only_that_side(self):
        code, notes = self.matrix([("yuksek", "tp", 1)], discard={("r1", "yuksek-tp")})
        self.assertEqual(0, code)
        self.assertEqual(["r2", "r1", "r1"], [c[0] for c in self.calls])   # max_discards=1 → one re-run
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

    def test_without_a_baseline_the_runner_is_unchanged(self):
        with mock.patch.object(wp078_perf, "cell_state", side_effect=self.fake_state), \
                mock.patch.object(wp078_perf, "run_window", side_effect=self.fake_window()), \
                mock.patch.object(wp078_perf.time, "sleep"):
            code = wp078_perf.run_all(run_args(), [("yuksek", "tp", 2)], self.r2, Path("/r2/P"), 60, None, lambda s: None)
        self.assertEqual(0, code)
        self.assertEqual(["r2", "r2"], [c[0] for c in self.calls])


if __name__ == "__main__":
    unittest.main()
