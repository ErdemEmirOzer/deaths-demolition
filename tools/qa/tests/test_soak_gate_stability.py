#!/usr/bin/env python3
"""WP-064B / DEC-091: soak_gate.py stability gate + real-bot report (fail-closed)."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

GATE = Path(__file__).resolve().parents[1] / "soak_gate.py"


def summary(rounds=12, mode="stability", stability_pass=True, mono_step=0.5, alloc_step=2.0, timeout_round=None):
    recs = []
    for r in range(1, rounds + 1):
        done = timeout_round != r
        recs.append({"round": r, "completed": done, "outcome": "completed" if done else "timeout",
                     "memMonoUsedMB": 60.0 + mono_step * r, "memTotalAllocatedMB": 900.0 + alloc_step * r, "memRssMB": -1.0})
    return {"seed": 61, "roundsRequested": rounds, "roundsCompleted": rounds if timeout_round is None else timeout_round - 1,
            "deaths": 0, "pass": False, "runMode": mode, "stabilityPass": stability_pass, "artifactWriteFailed": False, "rounds": recs}


class StabilityGateTests(unittest.TestCase):
    def run_gate(self, data, log_text="[QaRunBot] armed\n"):
        with tempfile.TemporaryDirectory() as tmp:
            s = Path(tmp) / "bot-summary.json"; s.write_text(json.dumps(data))
            log = Path(tmp) / "player.log"; log.write_text(log_text)
            return subprocess.run([sys.executable, str(GATE), str(s), "--rounds", "12", "--seed", "61", "--stability", "--player-log", str(log)],
                                  capture_output=True, text=True)

    def test_clean_stability_run_passes(self):
        r = self.run_gate(summary())
        self.assertEqual(0, r.returncode, r.stdout)

    def test_normal_or_evidence_mode_is_not_a_stability_pass(self):
        for mode in ("normal", "evidence"):
            self.assertNotEqual(0, self.run_gate(summary(mode=mode)).returncode, mode)

    def test_timeout_round_fails(self):
        self.assertNotEqual(0, self.run_gate(summary(timeout_round=7)).returncode)

    def test_error_line_in_player_log_fails(self):
        r = self.run_gate(summary(), "NullReferenceException: Object reference not set\n")
        self.assertNotEqual(0, r.returncode)
        self.assertIn("stability.log errors=1", r.stdout)

    def test_managed_growth_over_threshold_fails(self):
        self.assertNotEqual(0, self.run_gate(summary(mono_step=6.0)).returncode)   # 60 MB over rounds 2..12, 6 MB/round

    def test_native_growth_over_threshold_fails(self):
        self.assertNotEqual(0, self.run_gate(summary(alloc_step=30.0)).returncode)  # 300 MB

    def test_missing_memory_samples_fail(self):
        data = summary()
        for rec in data["rounds"]:
            rec.pop("memMonoUsedMB")
        self.assertNotEqual(0, self.run_gate(data).returncode)

    def test_report_mode_never_gates(self):
        with tempfile.TemporaryDirectory() as tmp:
            paths = []
            for i, done in enumerate((7, 11, 5)):
                p = Path(tmp) / f"s{i}.json"
                p.write_text(json.dumps({"seed": 61, "roundsCompleted": done, "deathRound": done + 1, "deathKilledBy": "Zombie.Standard.Swipe"}))
                paths.append(str(p))
            r = subprocess.run([sys.executable, str(GATE), "--report", *paths], capture_output=True, text=True)
            self.assertEqual(0, r.returncode, r.stdout)
            self.assertIn("median=7", r.stdout)


if __name__ == "__main__":
    unittest.main()
