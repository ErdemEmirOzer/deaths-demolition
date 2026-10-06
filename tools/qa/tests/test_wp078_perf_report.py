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


class P2CompareTests(unittest.TestCase):
    """WP-091 P2 (C-00 05.10, report only): R2 - R1 per cell; flag only when over +10 % AND +1.0 ms together."""

    def both(self, r1_means, r2_means):
        with tempfile.TemporaryDirectory() as a, tempfile.TemporaryDirectory() as b:
            r1, r2 = Path(a), Path(b)
            fill(r1, "yuksek", "Yuksek", "fp", "FirstPerson", r1_means)
            fill(r2, "yuksek", "Yuksek", "fp", "FirstPerson", r2_means)
            return report.compare(report.report(r2, TARGETS), report.report(r1, TARGETS))

    def cell(self, result):
        return next(c for c in result["cells"] if c["tier"] == "yuksek" and c["mode"] == "fp")

    def test_both_thresholds_together_flag_the_cell(self):
        c = self.cell(self.both([10.0, 10.0, 10.0], [11.5, 11.5, 11.5]))   # +1.5 ms, +15 %
        self.assertTrue(c["flagged"])
        self.assertEqual((1.5, 0.15), (c["deltaMs"], c["deltaPct"]))

    def test_percent_alone_or_ms_alone_does_not_flag(self):
        self.assertFalse(self.cell(self.both([4.0] * 3, [4.8] * 3))["flagged"])     # +20 % but only +0.8 ms
        self.assertFalse(self.cell(self.both([30.0] * 3, [31.5] * 3))["flagged"])   # +1.5 ms but only +5 %

    def test_a_side_without_counted_windows_is_unmeasured_not_fine(self):
        result = self.both([10.0] * 3, [])
        self.assertEqual("unmeasured", self.cell(result)["state"])
        self.assertGreaterEqual(result["unmeasuredCells"], 1)


P2_TARGETS = {
    "source": "test", "resolution": [1920, 1080], "activeEnemies": 40, "warmupSeconds": 10, "windowSeconds": 60,
    "stallMs": 33.33, "stallFractionMax": 0.01, "modes": ["fp"],
    "tiers": {"yuksek": {"meanMs": 16.6, "p99FilteredMs": 20.0, "stallMs": 33.33, "role": "gate", "minWindows": 3, "probeValue": "Yuksek"}},
    "probeKeys": {"file": "04-window.json", "tier": "qualityTier", "mode": "cameraMode", "sweep": "cameraSweep", "mean": "frameMeanMs",
                  "p99Filtered": "frameP99FilteredMs", "stallsExcluded": "framesExcludedAsStalls", "frames": "frameCount"},
}
R2C, R1C, R2SHA, R1SHA = "a" * 40, "b" * 40, "1" * 64, "2" * 64


def sound(root, index, mean, commit, sha, epoch, force=40, binary=None):
    """One window exactly as wp064_run + PilotPerfProbe leave it when everything held (WP-091 P2 integrity)."""
    d = Path(root) / f"perf-yuksek-fp-{index}"
    d.mkdir(parents=True)
    n = -(-60000 // int(mean)) if float(mean).is_integer() else int(60000 / mean) + 1
    put = lambda name, value: (d / name).write_text(json.dumps(value))
    put("attempt.json", {"status": "complete", "exitCode": 0, "hostClean": True, "timedOut": False, "missingArtifacts": [],
                         "executionCommit": commit, "binarySha256": binary or sha, "startedEpoch": epoch})
    put("01-scene-ready.json", {"scene": "Run", "screenWidth": 1920, "screenHeight": 1080})
    put("02-force-40.json", {"pass": True, "configuredSafetyCeiling": 40, "ceiling": 40, "forcedTarget": 40, "achievedCount": force})
    put("03-warmup.json", {"pass": True, "warmupSeconds": 10})
    window = {"pass": True, "qualityTier": "Yuksek", "cameraMode": "FirstPerson", "cameraSweep": True, "windowSeconds": 60,
              "frameMeanMs": mean, "frameP99FilteredMs": mean, "framesExcludedAsStalls": 0, "frameCount": n, "loadIntegrityFraction": 1.0}
    window.update({k: 40 for k in report.LOAD_FIELDS})
    put("04-window.json", window)
    (d / "frame-times-ms.csv").write_text("frameMs\n" + "\n".join(f"{mean:.4f}" for _ in range(n)) + "\n")


class P2IntegrityTests(unittest.TestCase):
    """WP-091 P2 (KADRO game-test-engineer MUST-1, 06.10): every window the comparison uses proves its load, scope and build
    on both sides; a broken window or a cell short of its windows writes no r2-P2.json (P2 BLOCKED by name)."""

    def run_p2(self, r2_means, r1_means, r1_force=None, r2_binary=None):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(lambda: __import__("shutil").rmtree(tmp, ignore_errors=True))
        r2, r1, out = tmp / "perf", tmp / "perf-r1", tmp / "r2" / "r2-P2.json"
        for i, m in enumerate(r2_means, 1):
            sound(r2, i, m, R2C, R2SHA, 1000.0 + 400 * i, binary=(r2_binary or {}).get(i))
        for i, m in enumerate(r1_means, 1):
            sound(r1, i, m, R1C, R1SHA, 1200.0 + 400 * i, force=(r1_force or {}).get(i, 40))
        (tmp / "targets.json").write_text(json.dumps(P2_TARGETS))
        import subprocess
        run = subprocess.run([sys.executable, str(Path(report.__file__)), str(r2), "--targets", str(tmp / "targets.json"),
                              "--baseline", str(r1), "--r2-measure", str(out), "--commit", R2C, "--sha256", R2SHA,
                              "--baseline-commit", R1C, "--baseline-sha256", R1SHA], capture_output=True, text=True)
        compare = json.loads((r2 / "wp078-perf-compare.json").read_text())
        return (json.loads(out.read_text()) if out.is_file() else None), run, compare

    def test_sound_sides_write_p2_with_its_provenance_and_noise(self):
        art, run, _ = self.run_p2([10.0, 10.5, 11.0], [10.0, 10.0, 10.0])
        self.assertIsNotNone(art, run.stderr)
        self.assertEqual(("P2", R2C, R1C, R1SHA, R2SHA), (art["gate"], art["commit"], art["baselineCommit"],
                                                         art["baselineBinarySha256"], art["binarySha256"]))
        self.assertEqual({"flaggedCells": 0, "worstDeltaMs": 0.5, "worstDeltaPct": 0.05}, art["values"])
        cell = art["cells"][0]
        self.assertEqual(([0.0, 0.5, 1.0], 0.5, 200.0), (cell["pairDeltasMs"], cell["pairDeltaMedianMs"], cell["pairGapMaxS"]))
        self.assertEqual(round((0.5 ** 2 / 3) ** 0.5, 4), cell["seDeltaMs"])

    def test_a_baseline_window_under_forty_writes_nothing_and_says_which(self):
        art, run, compare = self.run_p2([10.0] * 3, [10.0] * 3, r1_force={2: 39})
        self.assertIsNone(art)
        self.assertIn("r1 perf-yuksek-fp-2: 02-force-40", run.stderr)
        self.assertEqual(["perf-yuksek-fp-2"], list(compare["invalidWindows"]["r1"]))
        self.assertEqual("unmeasured", compare["cells"][0]["state"])   # 2 sound R1 windows < minWindows 3

    def test_a_window_on_another_build_writes_nothing(self):
        art, run, _ = self.run_p2([10.0] * 3, [10.0] * 3, r2_binary={3: "9" * 64})
        self.assertIsNone(art)
        self.assertIn("r2 perf-yuksek-fp-3: binarySha256", run.stderr)

    def test_a_cell_short_of_its_windows_writes_nothing(self):
        art, run, _ = self.run_p2([10.0] * 3, [10.0] * 2)
        self.assertIsNone(art)
        self.assertIn("unmeasured cell yuksek fp (R2 3, R1 2 sound windows, 3 each needed)", run.stderr)
