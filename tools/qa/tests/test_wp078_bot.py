"""WP-078 · tools/qa/wp078_bot.py: the command it builds and the files it binds (no Player, no guard in tests)."""
import argparse
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wp078_bot as bot  # noqa: E402


def args(**over):
    base = dict(kind="gameplay", mode="tp", seed=61, rounds=5, tier="sinematik", extra_arg=[])
    base.update(over)
    return argparse.Namespace(**base)


class Wp078BotTests(unittest.TestCase):
    def test_gameplay_command_is_sandboxed_and_names_tier_and_mode(self):
        out = Path("/x/run")
        c = bot.build_command(Path("/b/Player"), out, args())
        self.assertIn("-ddProfilePath", c)
        self.assertIn(str(out / "telemetry"), c)
        self.assertEqual("sinematik", c[c.index("-ddQualityTier") + 1])
        self.assertEqual("tp", c[c.index("-ddCameraMode") + 1])
        self.assertNotIn("-ddQaBotStability", c)
        self.assertNotIn("-ddQaBotRouteTour", c)
        self.assertEqual(str(out / "player.log"), c[-1])

    def test_route_and_stability_kinds_add_their_flag(self):
        out = Path("/x/run")
        self.assertEqual(str(out / "frames"), bot.build_command(Path("/b/P"), out, args(kind="route"))[
            bot.build_command(Path("/b/P"), out, args(kind="route")).index("-ddQaBotRouteTour") + 1])
        self.assertIn("-ddQaBotStability", bot.build_command(Path("/b/P"), out, args(kind="stability")))

    def test_extra_args_pass_through_before_the_log(self):
        c = bot.build_command(Path("/b/P"), Path("/x"), args(extra_arg=["-ddAiV3", "0"]))
        self.assertEqual(["-ddAiV3", "0"], c[c.index("-ddAiV3"):c.index("-ddAiV3") + 2])
        self.assertEqual("-logFile", c[-2])

    def test_route_frames_are_hashed_relative_and_guard_files_are_not(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            (out / "frames").mkdir()
            (out / "frames" / "route-kairo.png").write_bytes(b"\x89PNG")
            (out / "bot-summary.json").write_text("{}")
            (out / "ug").mkdir()
            (out / "ug" / "restore.txt").write_text("x")
            (out / "profile-sandbox").mkdir()
            (out / "profile-sandbox" / "prototype-profile.json").write_text("{}")
            (out / "attempt.json").write_text("{}")
            hashes = bot.hash_outputs(out)
        self.assertIn("frames/route-kairo.png", hashes)
        self.assertIn("bot-summary.json", hashes)
        self.assertFalse(any(k.startswith(("ug/", "profile-sandbox/")) or k == "attempt.json" for k in hashes))


if __name__ == "__main__":
    unittest.main()
