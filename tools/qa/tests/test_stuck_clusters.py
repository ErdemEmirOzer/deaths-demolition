"""WP-078 K6: tools/qa/stuck_clusters.py — same-spot stall > 5 s from the bot's timed Player log lines."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import stuck_clusters as sc  # noqa: E402


def trace(t, x, z, near=0):
    return f"[QaRunBot] t pos={x:.1f},0.0,{z:.1f} time={t:.1f} mode=Patrol tgt=- los=0 mz=1 eye=1 blk=- fire=0 hp=1.00 near={near} kills=0+0"


def stall(t, x, z, near=0, mode="patrol"):
    return f"[QaRunBot] stall-recovery mode={mode} pos={x:.1f},0.0,{z:.1f} time={t:.1f} near={near} goal=0.0,0.0,0.0"


class StuckClustersTests(unittest.TestCase):
    def test_moving_bot_has_no_cluster(self):
        log = "\n".join(trace(t, t * 2.0, 0.0) for t in range(20))
        result = sc.clusters(log)
        self.assertTrue(result["measurable"])
        self.assertEqual([], result["clusters"])

    def test_wedged_bot_with_stall_event_is_a_cluster(self):
        lines = [trace(t, 0.0, float(t)) for t in range(5)]                       # moving
        lines += [trace(5 + k, 4.0, 4.0 + 0.1 * (k % 2), near=2) for k in range(8)]  # 7 s on one spot
        lines.append(stall(7.0, 4.0, 4.0, near=3))
        lines += [trace(13 + k, 10.0 + k, 4.0) for k in range(3)]
        result = sc.clusters("\n".join(lines))
        self.assertEqual(1, len(result["clusters"]))
        c = result["clusters"][0]
        self.assertGreaterEqual(c["seconds"], 5.0)
        self.assertEqual(3, c["nearMax"])
        self.assertEqual(["patrol"], c["modes"])

    def test_holding_still_without_stall_event_is_not_a_cluster(self):
        log = "\n".join(trace(t, 1.0, 1.0) for t in range(10))
        self.assertEqual([], sc.clusters(log)["clusters"])

    def test_short_stall_under_five_seconds_is_not_a_cluster(self):
        lines = [trace(t, 2.0, 2.0) for t in range(4)] + [stall(2.0, 2.0, 2.0)] + [trace(4 + k, 5.0 + k, 2.0) for k in range(3)]
        self.assertEqual([], sc.clusters("\n".join(lines))["clusters"])

    def test_old_log_without_time_is_not_measurable(self):
        log = "[QaRunBot] stall-recovery mode=combat pos=8.7,0.0,3.9 near=0 goal=-1.0,0.0,-1.2\n" + trace(0, 0, 0)
        result = sc.clusters(log)
        self.assertFalse(result["measurable"])

    def test_empty_log_is_not_measurable(self):
        self.assertFalse(sc.clusters("")["measurable"])


if __name__ == "__main__":
    unittest.main()
