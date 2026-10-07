using System.Collections.Generic;
using System.IO;
using DD.Prototype.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-099 (S-01 td-capture) · the pure logic of TdCaptureAgent: the command line, the pose file, the real-time frame
    /// schedule, the readback orientation check, and the guard that the agent does nothing without <c>-ddTdCapture</c>.
    /// No Player, no scene, no file writes (the default pose file in tools/ is only read).
    /// </summary>
    [TestFixture]
    [Category("WP099")]
    public sealed class TdCaptureLogicTests
    {
        private static readonly string[] NoFlagArgv = { "DeathsDemolition", "-ddDirectToRun", "-ddCameraMode", "topdown", "-ddTdCaptureMode", "stills", "-logFile", "run.log" };
        private static readonly string[] DanglingFlagArgv = { "DeathsDemolition", "-ddTdCaptureMode", "video", "-ddTdCapture" };
        private static byte[] s_bait;

        private static string[] Argv(params string[] tail)
        {
            var list = new List<string> { "DeathsDemolition" };
            list.AddRange(tail);
            return list.ToArray();
        }

        // ---------------------------------------------------------------- inactive guard

        [Test]
        public void Agent_WithoutFlag_StartsNothing()
        {
            Assert.IsFalse(TdCaptureAgent.TryStart(NoFlagArgv));
            Assert.IsFalse(TdCaptureAgent.TryStart(DanglingFlagArgv), "-ddTdCapture as the last argument has no directory");
            Assert.IsFalse(TdCaptureAgent.TryStart(Argv("-ddTdCapture", "-ddTdCaptureMode", "video")), "a flag is not a directory");
            Assert.IsFalse(TdCaptureAgent.TryStart(new string[0]));
            Assert.IsNull(GameObject.Find("TdCaptureAgent"));
            Assert.IsNull(TdCaptureOptions.Parse(NoFlagArgv, out string error));
            Assert.IsNull(error, "inactive is not an error");
        }

        [Test]
        public void Agent_WithoutFlag_DoesNotAllocate_PositiveControlDoes()
        {
            TdCaptureAgent.TryStart(NoFlagArgv);   // warm-up: JIT / static init stays out of the measurement
            Assert.That((TestDelegate)(() => { s_bait = new byte[64 * 1024]; }), UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "positive control: the measurement must be able to see an allocation");
            Assert.That((TestDelegate)(() => { TdCaptureAgent.TryStart(NoFlagArgv); }), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That((TestDelegate)(() => { TdCaptureAgent.TryStart(DanglingFlagArgv); }), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Seed_IsReadOnlyWhenArmed()
        {
            Assert.IsFalse(TdCaptureOptions.TryReadSeed(Argv("-ddTdSeed", "7"), out _), "no -ddTdCapture: the seed flag alone does nothing");
            Assert.IsTrue(TdCaptureOptions.TryReadSeed(Argv("-ddTdCapture", "/tmp/x", "-ddTdSeed", "-12"), out int seed));
            Assert.AreEqual(-12, seed);
            Assert.IsFalse(TdCaptureOptions.TryReadSeed(Argv("-ddTdCapture", "/tmp/x", "-ddTdSeed", "x"), out _));
        }

        // ---------------------------------------------------------------- options

        [Test]
        public void Options_StillsDefaults()
        {
            TdCaptureOptions o = TdCaptureOptions.Parse(Argv("-ddTdCapture", "/tmp/td", "-ddTdCaptureMode", "stills", "-ddTdPoses", "/tmp/p.txt", "-ddTdSeed", "26"), out string error);
            Assert.IsNull(error);
            Assert.IsNotNull(o);
            Assert.AreEqual(TdCaptureMode.Stills, o.Mode);
            Assert.AreEqual("/tmp/td", o.OutDir);
            Assert.AreEqual("/tmp/p.txt", o.PosesPath);
            Assert.IsTrue(o.HoldSpawns, "stills hold spawns by default");
            Assert.AreEqual(2f, o.SettleSeconds);
            Assert.AreEqual(5f, o.IntroSeconds, "the ROUND 1 intro is waited out before the first still");
            Assert.AreEqual(20f, o.CrowdWaitSeconds);
            Assert.IsTrue(o.HasSeed);
            Assert.AreEqual(26, o.Seed);
            Assert.AreEqual(0, o.ResolutionWidth);
        }

        [Test]
        public void Options_VideoDefaultsAndRanges()
        {
            TdCaptureOptions o = TdCaptureOptions.Parse(Argv("-ddTdCapture", "/tmp/td", "-ddTdCaptureMode", "video"), out string error);
            Assert.IsNull(error);
            Assert.AreEqual(TdCaptureMode.Video, o.Mode);
            Assert.IsFalse(o.HoldSpawns, "video plays the real spawns by default");
            Assert.AreEqual(45, o.VideoSeconds);
            Assert.AreEqual(30, o.VideoFps);
            Assert.AreEqual(3f, o.WarmupSeconds);
            Assert.AreEqual(1920, o.VideoMaxWidth);
            Assert.AreEqual(-1, o.VideoFlip);
            Assert.IsFalse(o.HasSeed);

            Assert.AreEqual(10, Parse("-ddTdVideoSeconds", "10").VideoSeconds);
            Assert.AreEqual(120, Parse("-ddTdVideoSeconds", "120").VideoSeconds);
            Assert.AreEqual(60, Parse("-ddTdVideoFps", "60").VideoFps);
            Assert.AreEqual(1, Parse("-ddTdVideoFlip", "1").VideoFlip);
            Assert.AreEqual(0, Parse("-ddTdVideoMaxWidth", "0").VideoMaxWidth);
            TdCaptureOptions r = Parse("-ddTdResolution", "1920x1080");
            Assert.AreEqual(1920, r.ResolutionWidth);
            Assert.AreEqual(1080, r.ResolutionHeight);
            Assert.IsTrue(Parse("-ddTdHoldSpawns", "1").HoldSpawns);
        }

        private static TdCaptureOptions Parse(string flag, string value)
        {
            TdCaptureOptions o = TdCaptureOptions.Parse(Argv("-ddTdCapture", "/tmp/td", "-ddTdCaptureMode", "video", flag, value), out string error);
            Assert.IsNull(error, flag + " " + value);
            return o;
        }

        [TestCase("-ddTdVideoSeconds", "9")]
        [TestCase("-ddTdVideoSeconds", "121")]
        [TestCase("-ddTdVideoSeconds", "45.5")]
        [TestCase("-ddTdVideoFps", "0")]
        [TestCase("-ddTdVideoFps", "61")]
        [TestCase("-ddTdWarmupSeconds", "-1")]
        [TestCase("-ddTdSettleSeconds", "abc")]
        [TestCase("-ddTdHoldSpawns", "2")]
        [TestCase("-ddTdSeed", "1.5")]
        [TestCase("-ddTdVideoFlip", "yes")]
        [TestCase("-ddTdVideoMaxWidth", "100")]
        [TestCase("-ddTdResolution", "1920")]
        [TestCase("-ddTdResolution", "100x100")]
        [TestCase("-ddTdIntroSeconds", "61")]
        [TestCase("-ddTdCrowdWaitSeconds", "0")]
        [TestCase("-ddTdCrowdWaitSeconds", "121")]
        public void Options_MalformedValue_IsAnError(string flag, string value)
        {
            TdCaptureOptions o = TdCaptureOptions.Parse(Argv("-ddTdCapture", "/tmp/td", "-ddTdCaptureMode", "video", flag, value), out string error);
            Assert.IsNull(o);
            Assert.IsNotNull(error);
            StringAssert.Contains(flag, error);
        }

        [Test]
        public void Options_StructuralErrors()
        {
            string error;
            Assert.IsNull(TdCaptureOptions.Parse(Argv("-ddTdCapture", "relative/dir", "-ddTdCaptureMode", "video"), out error));
            StringAssert.Contains("absolute", error);
            Assert.IsNull(TdCaptureOptions.Parse(Argv("-ddTdCapture", "/tmp/td"), out error));
            StringAssert.Contains("-ddTdCaptureMode", error);
            Assert.IsNull(TdCaptureOptions.Parse(Argv("-ddTdCapture", "/tmp/td", "-ddTdCaptureMode", "movie"), out error));
            StringAssert.Contains("movie", error);
            Assert.IsNull(TdCaptureOptions.Parse(Argv("-ddTdCapture", "/tmp/td", "-ddTdCaptureMode", "stills"), out error));
            StringAssert.Contains("-ddTdPoses", error);
        }

        // ---------------------------------------------------------------- pose file

        private const string EightPoses =
            "# WP-099 test poses\n" +
            "spawn_courtyard 0 -3     # WorldLayout.PlayerStart\n" +
            "\n" +
            "lantern-axis\t0\t11\r\n" +
            "   chumon 0 19\n" +
            "kairo -33 15\n" +
            "# a comment line between poses\n" +
            "bridge_c8 4 -28.75\n" +
            "corner_sw -48 -48\n" +
            "sakura_court 31 1\n" +
            "karesansui 19.5 -22.25\n" +
            "   # trailing comment-only line\n";

        [Test]
        public void Poses_ValidFile_EightPosesInOrder()
        {
            Assert.IsTrue(TdPoseFile.TryParse(EightPoses, out List<TdPose> poses, out string error), error);
            Assert.AreEqual(8, poses.Count);
            Assert.AreEqual("spawn_courtyard", poses[0].Key);
            Assert.AreEqual(0f, poses[0].X);
            Assert.AreEqual(-3f, poses[0].Z);
            Assert.AreEqual(2, poses[0].Line, "1-based line of the file");
            Assert.AreEqual("lantern-axis", poses[1].Key, "tabs and CRLF are whitespace");
            Assert.AreEqual(11f, poses[1].Z);
            Assert.AreEqual(-28.75f, poses[4].Z);
            Assert.AreEqual(19.5f, poses[7].X);
            Assert.AreEqual(-22.25f, poses[7].Z);
            foreach (TdPose p in poses) Assert.IsFalse(p.Crowd, p.Key + ": no flag column, no crowd");
        }

        [Test]
        public void Poses_Crowd40FlagColumn_LastPosesOnly()
        {
            const string text = "plaza 0 -3\npath 20 1  # a plain pose\ncrowd_a 0 -3 crowd40\ncrowd_b 4 0\tcrowd40   # crowds may follow crowds\n";
            Assert.IsTrue(TdPoseFile.TryParse(text, out List<TdPose> poses, out string error), error);
            Assert.AreEqual(4, poses.Count);
            Assert.IsFalse(poses[0].Crowd);
            Assert.IsFalse(poses[1].Crowd);
            Assert.IsTrue(poses[2].Crowd);
            Assert.IsTrue(poses[3].Crowd);
            Assert.AreEqual(4f, poses[3].X);
        }

        [TestCase("a 0 0 crowd\n", "unknown flag")]
        [TestCase("a 0 0 CROWD40\n", "unknown flag")]
        [TestCase("a 0 0 crowd30\n", "unknown flag")]
        [TestCase("a 0 0 crowd40 extra\n", "line 1")]
        [TestCase("a 0 0 crowd40\nb 1 1\n", "must come last")]
        [TestCase("a 0 0\nc 0 0 crowd40\n# comment\n\nb 1 1\n", "line 5")]
        public void Poses_BadFlagOrPlainAfterCrowd_IsAnError(string text, string expected)
        {
            Assert.IsFalse(TdPoseFile.TryParse(text, out _, out string error));
            StringAssert.Contains(expected, error);
        }

        [TestCase("a 1\n", "line 1")]
        [TestCase("a 1 2 3\n", "line 1")]
        [TestCase("ok 0 0\nBad 1 2\n", "line 2")]
        [TestCase("ok 0 0\nsp ace 1 2\n", "line 2")]
        [TestCase("ok 0 0\nk.dot 1 2\n", "line 2")]
        [TestCase("a x 2\n", "line 1")]
        [TestCase("a 1 NaN\n", "line 1")]
        [TestCase("a 1 Infinity\n", "line 1")]
        [TestCase("a 1,5 2\n", "line 1")]
        [TestCase("a 0 0\n\nb 1 1\na 2 2\n", "duplicate")]
        [TestCase("", "no poses")]
        [TestCase("# only comments\n\n   \n", "no poses")]
        public void Poses_Malformed_IsAnErrorWithLine(string text, string expected)
        {
            Assert.IsFalse(TdPoseFile.TryParse(text, out _, out string error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void Poses_MoreThanEightAllowed_UpToNinetyNine()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 99; i++) sb.Append("p").Append(i).Append(' ').Append(i).Append(" 0\n");
            Assert.IsTrue(TdPoseFile.TryParse(sb.ToString(), out List<TdPose> poses, out string error), error);
            Assert.AreEqual(99, poses.Count);
            sb.Append("p99 1 1\n");
            Assert.IsFalse(TdPoseFile.TryParse(sb.ToString(), out _, out error));
            StringAssert.Contains("99", error);
        }

        [Test]
        public void Poses_StillFileName_TwoDigitOneBased()
        {
            Assert.AreEqual("01-spawn_courtyard.png", TdPoseFile.StillFileName(0, "spawn_courtyard"));
            Assert.AreEqual("10-kairo.png", TdPoseFile.StillFileName(9, "kairo"));
            Assert.IsFalse(TdPoseFile.IsValidKey(new string('a', TdPoseFile.MaxKeyLength + 1)));
            Assert.IsTrue(TdPoseFile.IsValidKey("a-b_9"));
        }

        [Test]
        public void Poses_DefaultFileInTools_ParsesToEight()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "tools", "td-capture-poses.txt"));
            Assert.IsTrue(File.Exists(path), "missing " + path);
            Assert.IsTrue(TdPoseFile.TryParse(File.ReadAllText(path), out List<TdPose> poses, out string error), error);
            Assert.AreEqual(8, poses.Count, "the default TD set is eight poses");
            Assert.IsTrue(poses[7].Crowd, "C-00 07.10: the 40-crowd pose is the last of the set");
            Assert.AreEqual(1, poses.FindAll(p => p.Crowd).Count, "exactly one crowd40 pose");
            foreach (TdPose p in poses)
            {
                Assert.LessOrEqual(Mathf.Abs(p.X), 55f, p.Key + " outside the v6 ±55 m ring");
                Assert.LessOrEqual(Mathf.Abs(p.Z), 55f, p.Key + " outside the v6 ±55 m ring");
            }
        }

        // ---------------------------------------------------------------- frame schedule

        [Test]
        public void Schedule_SlotMath()
        {
            var s = new TdFrameSchedule(30);
            Assert.IsFalse(s.TryTake(-0.001, false, out _), "before t0");
            Assert.IsTrue(s.TryTake(0.0, false, out long slot));
            Assert.AreEqual(0, slot);
            Assert.IsFalse(s.TryTake(0.016, false, out _), "a 60 Hz frame inside the taken slot 0 is not grabbed");
            Assert.IsTrue(s.TryTake(1.0 / 30.0, false, out slot), "exact slot boundary belongs to the next slot");
            Assert.AreEqual(1, slot);
            Assert.AreEqual(0, s.MissedSlots);
            Assert.AreEqual(2, s.Taken);
            Assert.AreEqual(1, s.SlotOf(0.05));
            Assert.AreEqual(30, s.SlotOf(1.0));
        }

        [Test]
        public void Schedule_SlowFramesCountMissedSlots()
        {
            var s = new TdFrameSchedule(30);
            Assert.IsTrue(s.TryTake(0.0, false, out _));
            Assert.IsTrue(s.TryTake(0.2, false, out long slot));   // a 200 ms hitch: slots 1..5 never had a frame
            Assert.AreEqual(6, slot);
            Assert.AreEqual(5, s.MissedSlots);
            Assert.AreEqual(5, s.Skipped);
            Assert.AreEqual(7, s.NextSlot);
        }

        [Test]
        public void Schedule_BusyPipelineSkipsInsteadOfQueueing()
        {
            var s = new TdFrameSchedule(30);
            Assert.IsTrue(s.TryTake(0.0, false, out _));
            Assert.IsFalse(s.TryTake(0.034, true, out long slot), "due but the pipeline is full: skipped");
            Assert.AreEqual(1, slot);
            Assert.AreEqual(1, s.BusySkipped);
            Assert.IsFalse(s.TryTake(0.05, false, out _), "the skipped slot is consumed, not retried");
            Assert.IsTrue(s.TryTake(0.067, false, out slot));
            Assert.AreEqual(2, slot);
            Assert.AreEqual(1, s.Skipped);
            Assert.AreEqual(2, s.Taken);
            Assert.AreEqual(0, s.MissedSlots);
        }

        [Test]
        public void Schedule_FullMinuteAtSixtyHz_TakesEverySecondFrame()
        {
            var s = new TdFrameSchedule(30);
            int taken = 0;
            for (int f = 0; f < 3600; f++)
            {
                if (s.TryTake(f / 60.0, false, out _)) taken++;
            }

            Assert.AreEqual(1800, taken);
            Assert.AreEqual(0, s.Skipped);
            Assert.AreEqual(30.0, TdFrameSchedule.EffectiveFps(1800, 60.0), 1e-9);
            Assert.AreEqual(0.0, TdFrameSchedule.EffectiveFps(10, 0.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TdFrameSchedule(0));
        }

        // ---------------------------------------------------------------- orientation

        private static byte[] Gradient(int w, int h, bool reversed)
        {
            var b = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                byte v = (byte)(20 + 200 * (reversed ? h - 1 - y : y) / (h - 1));
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    b[o] = v; b[o + 1] = v; b[o + 2] = v; b[o + 3] = 255;
                }
            }

            return b;
        }

        [Test]
        public void Orientation_DetectsStraightFlippedAndAmbiguous()
        {
            float[] reference = TdOrientation.BandLuma(Gradient(64, 64, false), 64, 64, TdOrientation.Bands);
            Assert.AreEqual(0, TdOrientation.ChooseFlip(reference, TdOrientation.BandLuma(Gradient(32, 32, false), 32, 32, TdOrientation.Bands), out _, out _),
                "same profile at another size (Retina / downscale) is straight");
            Assert.AreEqual(1, TdOrientation.ChooseFlip(reference, TdOrientation.BandLuma(Gradient(32, 32, true), 32, 32, TdOrientation.Bands), out double straight, out double flipped));
            Assert.Greater(straight, flipped);

            var flat = new byte[16 * 16 * 4];
            for (int i = 0; i < flat.Length; i++) flat[i] = 128;
            float[] flatBands = TdOrientation.BandLuma(flat, 16, 16, TdOrientation.Bands);
            Assert.AreEqual(-1, TdOrientation.ChooseFlip(flatBands, flatBands, out _, out _), "a uniform frame cannot tell");
            Assert.AreEqual(-1, TdOrientation.ChooseFlip(reference, new float[3], out _, out _), "band count mismatch");
        }

        [Test]
        public void Orientation_FlipRowsTwiceIsIdentity()
        {
            byte[] a = Gradient(5, 7, false);
            byte[] copy = (byte[])a.Clone();
            var scratch = new byte[5 * 4];
            TdOrientation.FlipRows(a, 5, 7, scratch);
            CollectionAssert.AreEqual(Gradient(5, 7, true), a);
            TdOrientation.FlipRows(a, 5, 7, scratch);
            CollectionAssert.AreEqual(copy, a);
        }

        [Test]
        public void Json_EscapesAndFormatsInvariant()
        {
            Assert.AreEqual("\"a\\\"b\\\\c\\n\\t\"", TdJson.Str("a\"b\\c\n\t"));
            Assert.AreEqual("null", TdJson.Str(null));
            Assert.AreEqual("\"\\u0001\"", TdJson.Str("\u0001"));
            Assert.AreEqual("-28.750", TdJson.Num(-28.75, 3));
            Assert.AreEqual("null", TdJson.Num(double.NaN));
        }
    }
}
