#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DD.Prototype.Diagnostics
{
    /// <summary>WP-099 (S-01 td-capture): what one <see cref="TdCaptureAgent"/> run does.</summary>
    public enum TdCaptureMode
    {
        Stills = 0,
        Video = 1,
        /// <summary>Round 3 (C-00 08.10): real-time video from the first frame — loading screen, main menu (logo intro), into Fighting.</summary>
        Boot = 2
    }

    /// <summary>
    /// WP-099 · the td-capture command line, parsed without Unity (EditMode-testable). Inactive (null, no error) unless
    /// <c>-ddTdCapture &lt;absolute dir&gt;</c> is present; every other malformed value is a hard error — a capture run that
    /// silently fell back to a default would produce evidence of a different thing than the script asked for.
    /// </summary>
    public sealed class TdCaptureOptions
    {
        public const string OutFlag = "-ddTdCapture";
        public const string ModeFlag = "-ddTdCaptureMode";
        public const string SeedFlag = "-ddTdSeed";
        public const string PosesFlag = "-ddTdPoses";
        public const string HoldSpawnsFlag = "-ddTdHoldSpawns";
        public const string SettleFlag = "-ddTdSettleSeconds";
        public const string VideoSecondsFlag = "-ddTdVideoSeconds";
        public const string VideoFpsFlag = "-ddTdVideoFps";
        public const string WarmupFlag = "-ddTdWarmupSeconds";
        /// <summary>Optional: video frames wider than this are scaled down on the GPU before readback (0 = native backbuffer). Default 1920.</summary>
        public const string VideoMaxWidthFlag = "-ddTdVideoMaxWidth";
        /// <summary>Optional: <c>auto|0|1</c> — row flip of the readback (auto = measured against a CaptureScreenshotAsTexture reference frame).</summary>
        public const string VideoFlipFlag = "-ddTdVideoFlip";
        /// <summary>Optional: <c>WxH</c> windowed Screen.SetResolution before anything is captured (both modes). Absent: the window is left alone.</summary>
        public const string ResolutionFlag = "-ddTdResolution";
        /// <summary>Stills: unscaled seconds to wait after the run is playable before the first pose (the ROUND 1 intro). Default 5.</summary>
        public const string IntroFlag = "-ddTdIntroSeconds";
        /// <summary>Stills: the bounded wait for a <c>crowd40</c> pose's bodies to arrive (≥ ceiling − 2 alive within 15 m). Default 20.</summary>
        public const string CrowdWaitFlag = "-ddTdCrowdWaitSeconds";
        /// <summary>Round 3: <c>on|off</c> (default on) — off disables every UI canvas in the Run scene for the capture (stills / video / boot's run part).</summary>
        public const string HudFlag = "-ddTdHud";
        /// <summary>Round 3, boot mode: seconds the main menu is shown (logo intro) before the agent goes Selection → Run. Default 4.</summary>
        public const string BootMenuFlag = "-ddTdBootMenuSeconds";

        /// <summary>Round 3 (gt SHOULD-1): ALL of these must carry a value, otherwise the agent refuses (error.json, exit 2).</summary>
        public static readonly string[] SandboxFlags = { "-ddProfilePath", "-ddUiDataRoot", "-ddTelemetryDir" };

        public const int MinVideoSeconds = 10, MaxVideoSeconds = 120, DefaultVideoSeconds = 45;
        public const int MinVideoFps = 1, MaxVideoFps = 60, DefaultVideoFps = 30;
        public const float DefaultSettleSeconds = 2f, DefaultWarmupSeconds = 3f, DefaultIntroSeconds = 5f, DefaultCrowdWaitSeconds = 20f, DefaultBootMenuSeconds = 4f;
        public const int DefaultVideoMaxWidth = 1920;

        public string OutDir;
        public TdCaptureMode Mode;
        public bool HasSeed;
        public int Seed;
        public string PosesPath;
        public bool HoldSpawns;
        public float SettleSeconds = DefaultSettleSeconds;
        public int VideoSeconds = DefaultVideoSeconds;
        public int VideoFps = DefaultVideoFps;
        public float WarmupSeconds = DefaultWarmupSeconds;
        public float IntroSeconds = DefaultIntroSeconds;
        public float CrowdWaitSeconds = DefaultCrowdWaitSeconds;
        public float BootMenuSeconds = DefaultBootMenuSeconds;
        public bool HudOff;
        public int VideoMaxWidth = DefaultVideoMaxWidth;
        /// <summary>-1 auto (measured), 0 never, 1 always.</summary>
        public int VideoFlip = -1;
        public int ResolutionWidth, ResolutionHeight;   // 0 = not requested

        /// <summary>
        /// True when <paramref name="args"/> carries <c>-ddTdCapture &lt;value&gt;</c>. Scans only: no allocation (the release-safety
        /// guard tested by TdCaptureLogicTests; the agent itself is not compiled into release Players at all).
        /// </summary>
        public static bool IsRequested(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], OutFlag, StringComparison.Ordinal) && !string.IsNullOrEmpty(args[i + 1])
                    && !args[i + 1].StartsWith("-dd", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The value after <paramref name="flag"/> (last occurrence wins), or null.</summary>
        public static string Arg(string[] args, string flag)
        {
            string found = null;
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal)) found = args[i + 1];
            }

            return found;
        }

        /// <summary>BeforeSceneLoad seed read: true with the seed when -ddTdCapture is armed and -ddTdSeed is a valid int.</summary>
        public static bool TryReadSeed(string[] args, out int seed)
        {
            seed = 0;
            if (!IsRequested(args)) return false;
            string s = Arg(args, SeedFlag);
            return s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
        }

        /// <summary>
        /// Round 3 (gt SHOULD-1): the sandbox rule is AND — <c>-ddProfilePath</c>, <c>-ddUiDataRoot</c> and <c>-ddTelemetryDir</c>
        /// must all be present with a value (not another flag). One or two of them is a refusal: a capture run plays a run, and a
        /// single redirected store still lets the others write into the player's own data.
        /// </summary>
        public static bool CheckSandbox(string[] args, out string error)
        {
            error = null;
            var missing = new List<string>(3);
            foreach (string flag in SandboxFlags)
            {
                string v = Arg(args, flag);
                if (string.IsNullOrEmpty(v) || v.StartsWith("-", StringComparison.Ordinal)) missing.Add(flag);
            }

            if (missing.Count == 0) return true;
            error = "refused: no full sandbox — missing " + string.Join(", ", missing) + " (all of " + string.Join(", ", SandboxFlags) + " are required)";
            return false;
        }

        /// <summary>
        /// Null + null error: not requested (inactive). Null + error: requested but malformed (the agent writes error.json and
        /// quits 2). Otherwise the options, with mode defaults applied (HoldSpawns: stills 1, video 0).
        /// </summary>
        public static TdCaptureOptions Parse(string[] args, out string error)
        {
            error = null;
            if (!IsRequested(args)) return null;

            var o = new TdCaptureOptions { OutDir = Arg(args, OutFlag) };
            if (!Path.IsPathRooted(o.OutDir))
            {
                error = OutFlag + " '" + o.OutDir + "' is not an absolute directory";
                return null;
            }

            string mode = Arg(args, ModeFlag);
            if (mode == "stills") o.Mode = TdCaptureMode.Stills;
            else if (mode == "video") o.Mode = TdCaptureMode.Video;
            else if (mode == "boot") o.Mode = TdCaptureMode.Boot;
            else
            {
                error = ModeFlag + " must be 'stills', 'video' or 'boot' (got " + (mode == null ? "nothing" : "'" + mode + "'") + ")";
                return null;
            }

            string seed = Arg(args, SeedFlag);
            if (seed != null)
            {
                if (!int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out o.Seed))
                {
                    error = SeedFlag + " '" + seed + "' is not an int";
                    return null;
                }

                o.HasSeed = true;
            }

            o.HoldSpawns = o.Mode == TdCaptureMode.Stills;
            string hold = Arg(args, HoldSpawnsFlag);
            if (hold != null)
            {
                if (hold == "1") o.HoldSpawns = true;
                else if (hold == "0") o.HoldSpawns = false;
                else { error = HoldSpawnsFlag + " must be 0 or 1 (got '" + hold + "')"; return null; }
            }

            if (!TryFloat(args, SettleFlag, 0f, 60f, ref o.SettleSeconds, out error)) return null;
            if (!TryFloat(args, WarmupFlag, 0f, 600f, ref o.WarmupSeconds, out error)) return null;
            if (!TryFloat(args, IntroFlag, 0f, 60f, ref o.IntroSeconds, out error)) return null;
            if (!TryFloat(args, CrowdWaitFlag, 1f, 120f, ref o.CrowdWaitSeconds, out error)) return null;
            if (!TryFloat(args, BootMenuFlag, 0f, 60f, ref o.BootMenuSeconds, out error)) return null;

            string hud = Arg(args, HudFlag);
            if (hud != null)
            {
                if (hud == "off") o.HudOff = true;
                else if (hud == "on") o.HudOff = false;
                else { error = HudFlag + " must be on or off (got '" + hud + "')"; return null; }
            }
            if (!TryInt(args, VideoSecondsFlag, MinVideoSeconds, MaxVideoSeconds, ref o.VideoSeconds, out error)) return null;
            if (!TryInt(args, VideoFpsFlag, MinVideoFps, MaxVideoFps, ref o.VideoFps, out error)) return null;
            if (!TryInt(args, VideoMaxWidthFlag, 0, 8192, ref o.VideoMaxWidth, out error)) return null;
            if (o.VideoMaxWidth > 0 && o.VideoMaxWidth < 320)
            {
                error = VideoMaxWidthFlag + " must be 0 (native) or 320..8192";
                return null;
            }

            string flip = Arg(args, VideoFlipFlag);
            if (flip != null)
            {
                if (flip == "auto") o.VideoFlip = -1;
                else if (flip == "0") o.VideoFlip = 0;
                else if (flip == "1") o.VideoFlip = 1;
                else { error = VideoFlipFlag + " must be auto, 0 or 1 (got '" + flip + "')"; return null; }
            }

            string res = Arg(args, ResolutionFlag);
            if (res != null)
            {
                string[] p = res.ToLowerInvariant().Split('x');
                int w, h;
                if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out w) ||
                    !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out h) || w < 320 || h < 240 || w > 8192 || h > 8192)
                {
                    error = ResolutionFlag + " '" + res + "' is not WxH (>= 320x240)";
                    return null;
                }

                o.ResolutionWidth = w;
                o.ResolutionHeight = h;
            }

            if (o.Mode == TdCaptureMode.Stills)
            {
                o.PosesPath = Arg(args, PosesFlag);
                if (string.IsNullOrEmpty(o.PosesPath))
                {
                    error = "stills mode needs " + PosesFlag + " <file> (lines 'key x z')";
                    return null;
                }
            }

            return o;
        }

        private static bool TryFloat(string[] args, string flag, float min, float max, ref float value, out string error)
        {
            error = null;
            string s = Arg(args, flag);
            if (s == null) return true;
            float v;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || float.IsNaN(v) || v < min || v > max)
            {
                error = flag + " '" + s + "' must be a number in " + min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            value = v;
            return true;
        }

        private static bool TryInt(string[] args, string flag, int min, int max, ref int value, out string error)
        {
            error = null;
            string s = Arg(args, flag);
            if (s == null) return true;
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < min || v > max)
            {
                error = flag + " '" + s + "' must be an int in " + min + ".." + max;
                return false;
            }

            value = v;
            return true;
        }
    }

    /// <summary>
    /// WP-099 · one requested still: the key names the PNG (<c>nn-key.png</c>), (X, Z) is the world point the player stands near;
    /// <see cref="Crowd"/> (the optional 4th column <c>crowd40</c>) releases the spawn hold and forces the safety-ceiling crowd
    /// around the player before the capture.
    /// </summary>
    public struct TdPose
    {
        public string Key;
        public float X, Z;
        public bool Crowd;
        public int Line;   // 1-based line in the pose file (error messages, poses.json)
    }

    /// <summary>
    /// WP-099 · the pose file: one <c>key x z [crowd40]</c> per line (whitespace-separated), <c>#</c> starts a comment (whole
    /// line or trailing), blank lines are ignored. Keys are <c>[a-z0-9_-]+</c> (≤ 48 chars) and unique; x / z are
    /// invariant-culture finite numbers; the only flag is <see cref="CrowdFlag"/>. A crowd leaves 40 bodies on the map, so
    /// crowd poses come last: a plain pose after one is an error (every plain pose starts from a held, empty field).
    /// 1..<see cref="MaxPoses"/> poses (the PNG index is two digits). Any malformed line fails the whole file with its line
    /// number — a half-read pose list would make the before/after sets disagree silently.
    /// </summary>
    public static class TdPoseFile
    {
        public const int MaxPoses = 99;
        public const int MaxKeyLength = 48;
        public const string CrowdFlag = "crowd40";

        public static bool TryParse(string text, out List<TdPose> poses, out string error)
        {
            poses = new List<TdPose>();
            error = null;
            if (text == null)
            {
                error = "pose file is empty";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            string crowdKey = null;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim().TrimStart('﻿');
                if (line.Length == 0) continue;

                string[] tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                int n = i + 1;
                if (tok.Length != 3 && tok.Length != 4)
                {
                    error = "line " + n + ": expected 'key x z [" + CrowdFlag + "]', got " + tok.Length + " field(s): '" + line + "'";
                    return false;
                }

                bool crowd = tok.Length == 4;
                if (crowd && !string.Equals(tok[3], CrowdFlag, StringComparison.Ordinal))
                {
                    error = "line " + n + ": unknown flag '" + tok[3] + "' (the only flag is " + CrowdFlag + ")";
                    return false;
                }

                if (!IsValidKey(tok[0]))
                {
                    error = "line " + n + ": key '" + tok[0] + "' must match [a-z0-9_-]+ (max " + MaxKeyLength + ")";
                    return false;
                }

                float x, z;
                if (!TryFinite(tok[1], out x) || !TryFinite(tok[2], out z))
                {
                    error = "line " + n + ": x / z must be finite numbers (invariant culture), got '" + tok[1] + "' '" + tok[2] + "'";
                    return false;
                }

                if (!seen.Add(tok[0]))
                {
                    error = "line " + n + ": duplicate key '" + tok[0] + "'";
                    return false;
                }

                if (poses.Count >= MaxPoses)
                {
                    error = "line " + n + ": more than " + MaxPoses + " poses";
                    return false;
                }

                if (!crowd && crowdKey != null)
                {
                    error = "line " + n + ": pose '" + tok[0] + "' follows the " + CrowdFlag + " pose '" + crowdKey + "'; " + CrowdFlag + " poses must come last";
                    return false;
                }

                if (crowd) crowdKey = tok[0];
                poses.Add(new TdPose { Key = tok[0], X = x, Z = z, Crowd = crowd, Line = n });
            }

            if (poses.Count == 0)
            {
                error = "pose file has no poses";
                return false;
            }

            return true;
        }

        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }

            return true;
        }

        /// <summary>The still's file name: <c>nn-key.png</c>, nn = 1-based index, two digits.</summary>
        public static string StillFileName(int index0, string key)
        {
            return (index0 + 1).ToString("00", CultureInfo.InvariantCulture) + "-" + key + ".png";
        }

        private static bool TryFinite(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }

    /// <summary>
    /// WP-099 · the real-time video sample grid. Slot k covers [k/fps, (k+1)/fps) seconds after t0; a rendered frame whose
    /// grab time falls in a slot not yet taken takes it. Frames that land in an already-taken slot are not grabbed (the game
    /// runs faster than the video fps); slots no frame landed in are <see cref="MissedSlots"/> (the game ran slower); a due
    /// slot that could not be grabbed because the readback / encode pipeline was full is <see cref="BusySkipped"/> — the
    /// pipeline never queues without bound. The recorded timestamp is the grab time, not the slot time (the muxer lays the
    /// frames on its own constant grid).
    /// </summary>
    public sealed class TdFrameSchedule
    {
        public readonly int Fps;
        public long NextSlot { get; private set; }
        public long MissedSlots { get; private set; }
        public long BusySkipped { get; private set; }
        public long Taken { get; private set; }

        /// <summary>Slots skipped for any reason.</summary>
        public long Skipped => MissedSlots + BusySkipped;

        public TdFrameSchedule(int fps)
        {
            if (fps < 1) throw new ArgumentOutOfRangeException(nameof(fps));
            Fps = fps;
        }

        /// <summary>The slot a grab time falls in (−1 before t0).</summary>
        public long SlotOf(double t)
        {
            if (t < 0d || double.IsNaN(t)) return -1;
            return (long)Math.Floor(t * Fps + 1e-9);
        }

        /// <summary>
        /// Called once per rendered frame with the grab time. True: grab this frame for <paramref name="slot"/>.
        /// False: not due (slot already taken / before t0), or due but <paramref name="busy"/> (counted in BusySkipped).
        /// </summary>
        public bool TryTake(double t, bool busy, out long slot)
        {
            slot = SlotOf(t);
            if (slot < 0 || slot < NextSlot) return false;
            MissedSlots += slot - NextSlot;
            NextSlot = slot + 1;
            if (busy)
            {
                BusySkipped++;
                return false;
            }

            Taken++;
            return true;
        }

        public static double EffectiveFps(long frames, double seconds)
        {
            return seconds > 1e-6 ? frames / seconds : 0d;
        }
    }

    /// <summary>
    /// WP-099 · readback orientation check. The ScreenCapture.CaptureScreenshotIntoRenderTexture readback is upside down on
    /// some graphics APIs; instead of trusting SystemInfo, the agent grabs one calibration frame both ways (the known-good
    /// CaptureScreenshotAsTexture and the RT readback, same frame) and compares horizontal luma bands. Both buffers are in
    /// texture row order (row 0 = first row in memory); the band profile is size-independent, so a Retina-scaled or
    /// GPU-downscaled readback still compares.
    /// </summary>
    public static class TdOrientation
    {
        public const int Bands = 16;

        /// <summary>Mean luma (0..255) per horizontal band of an RGBA32 buffer, band 0 = memory rows 0..h/bands.</summary>
        public static float[] BandLuma(byte[] rgba, int width, int height, int bands)
        {
            if (rgba == null || width <= 0 || height <= 0 || bands <= 0 || rgba.Length < width * height * 4)
                throw new ArgumentException("buffer smaller than width*height*4");
            var sum = new double[bands];
            var count = new long[bands];
            int step = Math.Max(1, width / 64);   // 64 samples per row is plenty for a band profile
            for (int y = 0; y < height; y++)
            {
                int b = (int)((long)y * bands / height);
                int row = y * width * 4;
                for (int x = 0; x < width; x += step)
                {
                    int o = row + x * 4;
                    sum[b] += 0.2126 * rgba[o] + 0.7152 * rgba[o + 1] + 0.0722 * rgba[o + 2];
                    count[b]++;
                }
            }

            var result = new float[bands];
            for (int b = 0; b < bands; b++) result[b] = count[b] > 0 ? (float)(sum[b] / count[b]) : 0f;
            return result;
        }

        /// <summary>
        /// 0: the grab matches the reference as-is; 1: it matches flipped; −1: ambiguous (a near-uniform frame, or neither
        /// orientation clearly better: the better error must be under half the worse, and the profile must vary by ≥ 4 luma).
        /// </summary>
        public static int ChooseFlip(float[] reference, float[] grab, out double errStraight, out double errFlipped)
        {
            errStraight = errFlipped = 0d;
            if (reference == null || grab == null || reference.Length != grab.Length || reference.Length == 0) return -1;
            int n = reference.Length;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                errStraight += Math.Abs(reference[i] - grab[i]);
                errFlipped += Math.Abs(reference[i] - grab[n - 1 - i]);
                min = Math.Min(min, reference[i]);
                max = Math.Max(max, reference[i]);
            }

            errStraight /= n;
            errFlipped /= n;
            if (max - min < 4f) return -1;
            if (errStraight < 0.5 * errFlipped) return 0;
            if (errFlipped < 0.5 * errStraight) return 1;
            return -1;
        }

        public static float Mean(float[] bands)
        {
            if (bands == null || bands.Length == 0) return 0f;
            double s = 0d;
            for (int i = 0; i < bands.Length; i++) s += bands[i];
            return (float)(s / bands.Length);
        }

        /// <summary>Reverses the row order of an RGBA32 buffer in place (<paramref name="scratch"/> ≥ one row).</summary>
        public static void FlipRows(byte[] rgba, int width, int height, byte[] scratch)
        {
            int stride = width * 4;
            for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
            {
                Buffer.BlockCopy(rgba, top * stride, scratch, 0, stride);
                Buffer.BlockCopy(rgba, bottom * stride, rgba, top * stride, stride);
                Buffer.BlockCopy(scratch, 0, rgba, bottom * stride, stride);
            }
        }
    }

    /// <summary>WP-099 · minimal invariant-culture JSON writing (the artefacts are read by python / swift, never by JsonUtility).</summary>
    public static class TdJson
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }

        public static string Num(double v, int decimals = 4)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public static string Bool(bool v) => v ? "true" : "false";
    }
}
#endif
