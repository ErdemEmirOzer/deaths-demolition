"""WP-091 P5: Selection → Run load time from the bot sessions' load-timing.json, R2 over R1. Files only."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import r2_load_time as lt  # noqa: E402

A, B = "a" * 40, "b" * 40


def session(root, stage, name, scenes, seconds):
    d = Path(root) / stage / name / "ui-data"
    d.mkdir(parents=True)
    (d / "load-timing.json").write_text(json.dumps({"Version": 1, "Scenes": scenes, "Seconds": seconds}))


class SampleTests(unittest.TestCase):
    def test_only_the_run_entry_of_a2_and_a3_bot_sessions_counts(self):
        root = tempfile.mkdtemp()
        session(root, "a2", "gameplay-tp", ["Selection", "Run"], [1.23, 1.40])
        session(root, "a3", "flag-ddaiv3-0", ["Run", "Selection"], [1.50, 1.20])
        session(root, "a5", "soak-1", ["Selection", "Run"], [1.0, 9.0])          # not a chain bot stage
        session(root, "a2", "route-fp", ["Selection"], [1.2])                     # never reached Run
        (Path(root) / "a2" / "broken" / "ui-data").mkdir(parents=True)
        (Path(root) / "a2" / "broken" / "ui-data" / "load-timing.json").write_text("{not json")
        self.assertEqual([("a2/gameplay-tp", 1.40), ("a3/flag-ddaiv3-0", 1.50)], lt.samples(root))


class RawTests(unittest.TestCase):
    def test_raw_seconds_are_r2_only_and_minus_one_or_absent_is_no_sample(self):
        r2, r1 = tempfile.mkdtemp(), tempfile.mkdtemp()
        for name, raw in (("gameplay-tp", [0.1, 0.42]), ("gameplay-fp", [0.1, -1]), ("route-tp", None)):
            d = Path(r2) / "a2" / name / "ui-data"
            d.mkdir(parents=True)
            data = {"Version": 1, "Scenes": ["Selection", "Run"], "Seconds": [1.2, 1.3]}
            if raw is not None:
                data["RawSeconds"] = raw
            (d / "load-timing.json").write_text(json.dumps(data))
        session(r1, "a2", "gameplay-tp", ["Selection", "Run"], [1.2, 1.22])     # R1 FINAL: no RawSeconds at all
        data, _, _ = lt.artifact(r2, r1, A, B)
        self.assertEqual((0.42, 1), (data["values"]["r2RawMedianSeconds"], data["values"]["r2RawSamples"]))
        self.assertEqual(round(1.3 / 1.22, 4), data["values"]["ratio"])          # the ratio stays Seconds over Seconds
        self.assertEqual({"a2/gameplay-tp": 0.42}, data["sessions"]["r2Raw"])

    def test_without_raw_samples_the_field_is_null(self):
        self.assertIsNone(lt.compare([("x", 1.3)], [("p", 1.2)])["r2RawMedianSeconds"])


class CompareTests(unittest.TestCase):
    def test_ratio_of_medians_and_the_floor_flag(self):
        v = lt.compare([("x", 1.30), ("y", 1.50), ("z", 1.40)], [("p", 1.218), ("q", 1.222)])
        self.assertEqual(round(1.40 / 1.22, 4), v["ratio"])
        self.assertEqual((3, 2), (v["r2Samples"], v["r1Samples"]))
        self.assertTrue(v["floorLimited"])     # R1 sat on the 1.2 s curtain floor
        self.assertFalse(lt.compare([("x", 2.0)], [("p", 1.9)])["floorLimited"])

    def test_a_side_without_samples_writes_nothing(self):
        self.assertIsNone(lt.compare([], [("p", 1.2)]))
        self.assertIsNone(lt.compare([("x", 1.2)], []))

    def test_the_artifact_is_a_p5_measurement_on_the_final_with_its_baseline_named(self):
        r2, r1 = tempfile.mkdtemp(), tempfile.mkdtemp()
        session(r2, "a2", "gameplay-tp", ["Selection", "Run"], [1.2, 1.3])
        session(r1, "a2", "gameplay-tp", ["Selection", "Run"], [1.2, 1.2])
        data, _, _ = lt.artifact(r2, r1, A, B)
        self.assertEqual(("dd-r2-measure-v1", "P5", A, B), (data["schema"], data["gate"], data["commit"], data["baselineCommit"]))
        self.assertIn("ratio", data["values"])
        self.assertEqual({"a2/gameplay-tp": 1.3}, data["sessions"]["r2"])


if __name__ == "__main__":
    unittest.main()
