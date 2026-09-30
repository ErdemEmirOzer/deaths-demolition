"""WP-078 §4 · tools/qa/wp078_perf_report.py judges a tier × mode matrix against perf-targets.json (no best-of;
C-00 30.09 K8 discard rule, K9 per-tier stallMs recomputed from frame-times-ms.csv)."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wp078_perf_report as report  # noqa: E402

TARGETS = {
    "source": "test",
    "stallMs": 33.33,
    "stallFractionMax": 0.01,
    "modes": ["topdown", "fp"],
    "tiers": {
        "sinematik": {"meanMs": 33.3, "p99FilteredMs": 40.0, "stallMs": 40.0, "role": "gate", "minWindows": 3, "probeValue": "Sinematik"},
        "yuksek": {"meanMs": 16.6, "p99FilteredMs": 20.0, "stallMs": 33.33, "role": "gate", "minWindows": 3, "probeValue": "Yuksek"},
    },
    "probeKeys": {"file": "04-window.json", "tier": "qualityTier", "mode": "cameraMode", "mean": "frameMeanMs",
                  "p99Filtered": "frameP99FilteredMs", "stallsExcluded": "framesExcludedAsStalls", "frames": "frameCount"},
}


def window(root, name, tier, mode, frames, attempt=None):
    d = root / name
    d.mkdir()
    (d / "attempt.json").write_text(json.dumps(attempt or {"status": "complete", "exitCode": 0, "hostClean": True,
                                                           "timedOut": False, "missingArtifacts": []}))
    (d / "04-window.json").write_text(json.dumps({"qualityTier": tier, "cameraMode": mode, "frameMeanMs": sum(frames) / len(frames),
                                                  "frameP99FilteredMs": 0, "framesExcludedAsStalls": 0, "frameCount": len(frames)}))
    (d / "frame-times-ms.csv").write_text("frameMs\n" + "\n".join(f"{v:.3f}" for v in frames) + "\n")


def flat(ms, n=3600):
    return [ms] * n


def fill(root, tier_key, tier, mode_key, mode, means):
    for i, mean in enumerate(means, 1):
        window(root, f"perf-{tier_key}-{mode_key}-{i}", tier, mode, flat(mean))


class Wp078PerfReportTests(unittest.TestCase):
    def matrix(self, build):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            build(root)
            return report.report(root, TARGETS)

    def full(self, root, yuksek_fp=(12.0, 12.5, 13.0)):
        fill(root, "sinematik", "Sinematik", "topdown", "TopDown", [30.0, 31.0, 32.0])
        fill(root, "sinematik", "Sinematik", "fp", "FirstPerson", [25.0, 26.0, 27.0])
        fill(root, "yuksek", "Yuksek", "topdown", "TopDown", [15.0, 16.0, 16.5])
        fill(root, "yuksek", "Yuksek", "fp", "FirstPerson", list(yuksek_fp))

    def cell(self, result, tier, mode):
        return next(c for c in result["cells"] if c["tier"] == tier and c["mode"] == mode)

    def test_every_cell_within_limits_passes(self):
        self.assertTrue(self.matrix(self.full)["pass"])

    def test_one_slow_window_fails_the_cell_no_best_of(self):
        result = self.matrix(lambda r: self.full(r, yuksek_fp=(12.0, 12.5, 16.7)))
        self.assertFalse(result["pass"])
        self.assertFalse(self.cell(result, "yuksek", "fp")["pass"])

    def test_wrong_tier_on_the_probe_fails_the_cell(self):
        def build(root):
            self.full(root)
            window(root, "perf-yuksek-fp-4", "Sinematik", "FirstPerson", flat(12.0))
        c = self.cell(self.matrix(build), "yuksek", "fp")
        self.assertEqual(1, c["failed"])
        self.assertFalse(c["pass"])

    def test_preflight_discard_is_listed_not_counted_not_failed(self):
        def build(root):
            self.full(root)
            window(root, "perf-yuksek-topdown-4", "Yuksek", "TopDown", flat(40.0), {"status": "preflight-failed", "exitCode": 75})
        c = self.cell(self.matrix(build), "yuksek", "topdown")
        self.assertEqual((3, 1, 0), (c["counted"], c["discarded"], c["failed"]))
        self.assertTrue(c["pass"])

    def test_dirty_host_discard_needs_a_working_player(self):
        dirty_ok = {"status": "failed", "exitCode": 0, "hostClean": False, "timedOut": False, "missingArtifacts": []}
        dirty_crash = {"status": "failed", "exitCode": 1, "hostClean": False, "timedOut": False, "missingArtifacts": []}

        def build(root):
            self.full(root)
            window(root, "perf-sinematik-fp-4", "Sinematik", "FirstPerson", flat(20.0), dirty_ok)
            window(root, "perf-sinematik-fp-5", "Sinematik", "FirstPerson", flat(20.0), dirty_crash)
        c = self.cell(self.matrix(build), "sinematik", "fp")
        self.assertEqual((1, 1), (c["discarded"], c["failed"]))
        self.assertFalse(c["pass"], "a Player crash is a FAIL even on a dirty host")

    def test_crash_on_a_clean_host_fails(self):
        def build(root):
            self.full(root)
            window(root, "perf-yuksek-fp-4", "Yuksek", "FirstPerson", flat(12.0),
                   {"status": "failed", "exitCode": 1, "hostClean": True, "timedOut": False, "missingArtifacts": []})
        self.assertFalse(self.cell(self.matrix(build), "yuksek", "fp")["pass"])

    def test_sinematik_stall_threshold_is_forty(self):
        # 1.5 % of frames at 36 ms: stalls for Yüksek's 33.33, not for Sinematik's 40 (C-00 30.09 K9)
        frames = [30.0] * 3546 + [36.0] * 54

        def build(root):
            fill(root, "sinematik", "Sinematik", "topdown", "TopDown", [])
            for i in range(1, 4):
                window(root, f"perf-sinematik-topdown-{i}", "Sinematik", "TopDown", frames)
        c = self.cell(self.matrix(build), "sinematik", "topdown")
        self.assertEqual(3, c["counted"])
        self.assertTrue(c["pass"])
        self.assertEqual(0, c["windows"][0]["stalls"])
        self.assertGreater(c["windows"][0]["over33Fraction"], 0.01)

    def test_raw_p99_over_limit_is_flagged(self):
        frames = [12.0] * 3560 + [25.0] * 40      # 1.1 %: raw p99 25 > 20 while the filtered p99 (stall 33.33) is 25 too
        def build(root):
            for i in range(1, 4):
                window(root, f"perf-yuksek-topdown-{i}", "Yuksek", "TopDown", frames)
        c = self.cell(self.matrix(build), "yuksek", "topdown")
        self.assertTrue(c["rawP99OverLimit"])
        self.assertFalse(c["pass"])

    def test_missing_cell_fails_the_gate(self):
        self.assertFalse(self.matrix(lambda r: fill(r, "yuksek", "Yuksek", "topdown", "TopDown", [15.0, 16.0, 16.5]))["pass"])

    def test_percentile_matches_the_probe(self):
        self.assertEqual(3.0, report.percentile([1.0, 2.0, 3.0, 4.0], 0.75))
        self.assertEqual(4.0, report.percentile([1.0, 2.0, 3.0, 4.0], 0.99))


class PerfTargetsInvariantTests(unittest.TestCase):
    def test_stall_threshold_never_under_the_p99_limit(self):
        targets = json.loads((Path(__file__).resolve().parents[1] / "perf-targets.json").read_text(encoding="utf-8"))
        for name, tier in targets["tiers"].items():
            self.assertIn("stallMs", tier, name)
            self.assertGreaterEqual(tier["stallMs"], tier["p99FilteredMs"], f"{name}: stallMs must be >= p99FilteredMs (K9)")
            self.assertGreaterEqual(tier["minWindows"], 3, name)


if __name__ == "__main__":
    unittest.main()
