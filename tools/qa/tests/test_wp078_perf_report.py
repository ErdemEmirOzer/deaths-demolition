"""WP-078 §4: tools/qa/wp078_perf_report.py judges a tier × mode matrix against perf-targets.json (no best-of)."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wp078_perf_report as report  # noqa: E402

TARGETS = {
    "source": "test",
    "stallFractionMax": 0.01,
    "modes": ["topdown", "fp"],
    "tiers": {
        "sinematik": {"meanMs": 33.3, "p99FilteredMs": 40.0, "role": "gate", "minWindows": 3, "probeValue": "Sinematik"},
        "yuksek": {"meanMs": 16.6, "p99FilteredMs": 20.0, "role": "gate", "minWindows": 3, "probeValue": "Yuksek"},
    },
    "probeKeys": {"file": "04-window.json", "tier": "qualityTier", "mode": "cameraMode", "mean": "frameMeanMs",
                  "p99Filtered": "frameP99FilteredMs", "stallsExcluded": "framesExcludedAsStalls", "frames": "frameCount"},
}


def window(root, name, tier, mode, mean, p99=18.0, status="complete", exit_code=0, stalls=0):
    d = root / name
    d.mkdir()
    (d / "attempt.json").write_text(json.dumps({"status": status, "exitCode": exit_code}))
    (d / "04-window.json").write_text(json.dumps({"qualityTier": tier, "cameraMode": mode, "frameMeanMs": mean,
                                                  "frameP99FilteredMs": p99, "framesExcludedAsStalls": stalls, "frameCount": 3600}))


def fill(root, tier_key, tier, mode_key, mode, means, p99=18.0):
    for i, mean in enumerate(means, 1):
        window(root, f"perf-{tier_key}-{mode_key}-{i}", tier, mode, mean, p99)


class Wp078PerfReportTests(unittest.TestCase):
    def matrix(self, build):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            build(root)
            return report.report(root, TARGETS)

    def full(self, root, yuksek_fp=(12.0, 12.5, 13.0)):
        fill(root, "sinematik", "Sinematik", "topdown", "TopDown", [30.0, 31.0, 32.0], p99=38.0)
        fill(root, "sinematik", "Sinematik", "fp", "FirstPerson", [25.0, 26.0, 27.0], p99=35.0)
        fill(root, "yuksek", "Yuksek", "topdown", "TopDown", [15.0, 16.0, 16.5])
        fill(root, "yuksek", "Yuksek", "fp", "FirstPerson", list(yuksek_fp))

    def test_every_cell_within_limits_passes(self):
        self.assertTrue(self.matrix(self.full)["pass"])

    def test_one_slow_window_fails_the_cell_no_best_of(self):
        result = self.matrix(lambda r: self.full(r, yuksek_fp=(12.0, 12.5, 16.7)))
        self.assertFalse(result["pass"])
        cell = next(c for c in result["cells"] if c["tier"] == "yuksek" and c["mode"] == "fp")
        self.assertFalse(cell["pass"])
        self.assertEqual(3, cell["counted"])

    def test_wrong_tier_on_the_probe_does_not_count(self):
        def build(root):
            self.full(root)
            (root / "perf-yuksek-fp-3" / "04-window.json").write_text(json.dumps(
                {"qualityTier": "Sinematik", "cameraMode": "FirstPerson", "frameMeanMs": 12.0, "frameP99FilteredMs": 18.0,
                 "framesExcludedAsStalls": 0, "frameCount": 3600}))
        cell = next(c for c in self.matrix(build)["cells"] if c["tier"] == "yuksek" and c["mode"] == "fp")
        self.assertEqual(2, cell["counted"])
        self.assertFalse(cell["pass"], "2 < minWindows 3")

    def test_host_discarded_attempt_does_not_count(self):
        def build(root):
            self.full(root)
            window(root, "perf-yuksek-topdown-4", "Yuksek", "TopDown", 40.0, status="preflight-failed", exit_code=75)
        cell = next(c for c in self.matrix(build)["cells"] if c["tier"] == "yuksek" and c["mode"] == "topdown")
        self.assertEqual(3, cell["counted"])
        self.assertTrue(cell["pass"])

    def test_stall_exclusion_over_one_percent_fails(self):
        def build(root):
            self.full(root)
            window(root, "perf-sinematik-fp-4", "Sinematik", "FirstPerson", 20.0, p99=30.0, stalls=40)
        cell = next(c for c in self.matrix(build)["cells"] if c["tier"] == "sinematik" and c["mode"] == "fp")
        self.assertFalse(cell["pass"])

    def test_missing_cell_fails_the_gate(self):
        def build(root):
            fill(root, "yuksek", "Yuksek", "topdown", "TopDown", [15.0, 16.0, 16.5])
        self.assertFalse(self.matrix(build)["pass"])


if __name__ == "__main__":
    unittest.main()
