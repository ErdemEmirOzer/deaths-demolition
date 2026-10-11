#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-110 (S-01 mk-capture, C-00 11.10) · the marketing-capture command line, parsed without Unity (EditMode-testable).
    /// Inactive (null, no error) unless <c>-ddMkCapture &lt;absolute dir&gt;</c> is present; every other malformed value is a hard
    /// error (error.json, exit 2) — a capture that silently fell back to a default would film something the script did not ask for.
    /// </summary>
    public sealed class MkCaptureOptions
    {
        public const string OutFlag = "-ddMkCapture";
        /// <summary>Master frame size WxH (default 1080x1920, portrait). 1080x1350 and 1080x1080 are centre crops of it (tools/mk-capture.sh).</summary>
        public const string SizeFlag = "-ddMkSize";
        public const string FpsFlag = "-ddMkFps";
        /// <summary>TopDown vertical field of view for the master, degrees (default 68.6 = the game's 42° as the master's horizontal angle; 0 = the game's own).</summary>
        public const string FovFlag = "-ddMkFov";
        public const string PrerollFlag = "-ddMkPreroll";
        public const string PostrollFlag = "-ddMkPostroll";
        public const string MinClipFlag = "-ddMkMinClipSeconds";
        public const string MaxClipFlag = "-ddMkMaxClipSeconds";
        public const string ClipsFlag = "-ddMkClips";
        public const string SecondsFlag = "-ddMkSeconds";
        public const string ThresholdFlag = "-ddMkThreshold";
        public const string StillsFlag = "-ddMkStills";
        public const string HudFlag = "-ddMkHud";
        public const string WarmupFlag = "-ddMkWarmupSeconds";
        public const string JpegQualityFlag = "-ddMkJpegQuality";
        /// <summary>S-11 §9-1: a MultiKill counts as a marketing beat only from this many bodies (the game's own 3/4 stay). Default 6.</summary>
        public const string MultiKillMinFlag = "-ddMkMultiKillMin";
        /// <summary>S-11 §9-2: <c>Y|O|G</c> — near / middle / wide TopDown distance for the capture session (the angle stays). Default O.</summary>
        public const string CameraFlag = "-ddMkCamera";
        /// <summary>S-11 §9-2: metres the camera leans toward the nearby crowd's centre (look room in the threat's direction; 0 = off). Default 3.</summary>
        public const string LookFlag = "-ddMkLookMetres";
        /// <summary>S-11 §9-5: <c>1</c> — the mixer's Music group is muted for the session (SFX + ambience + UI are recorded). Default 0.</summary>
        public const string MusicOffFlag = "-ddMkMusicOff";
        /// <summary>Preroll ring memory budget in MB (JPEG bytes; the oldest frames go first). Default 640 (gt S4 11.10: with
        /// hud both a 1080×1920 frame pair is ≈ 1.2 MiB, so 640 MB holds ≈ 18 s at 30 fps; hud off ≈ 35 s).</summary>
        public const string RingMbFlag = "-ddMkRingMB";
        /// <summary>gt M1 (11.10): the raw bytes (clip frames + stills) one session may write before it stops ("raw-budget"),
        /// GB. Default 5 — S-11 §9-13 raw ≤ 6 GB; mk-capture.sh muxes and removes them after each segment.</summary>
        public const string RawGbFlag = "-ddMkRawGB";
        /// <summary>dry-1 (11.10): <c>1</c> keeps the hero inside <see cref="MkFraming"/>'s band of the portrait master (default), 0 = off.</summary>
        public const string FrameBandFlag = "-ddMkFrameBand";
        /// <summary>S-11 11.10: <c>1</c> lays the HUD out inside the Reels safe rectangle (<see cref="MkFraming.HudSafe"/>) of the
        /// master instead of its full frame (the bottom ~400 px and the right column sit under Instagram's own UI). Default 0.</summary>
        public const string HudSafeFlag = "-ddMkHudSafe";

        public const int DefaultWidth = 1080, DefaultHeight = 1920, DefaultFps = 30, DefaultClips = 12, DefaultSeconds = 600, DefaultStills = 2;
        public const float DefaultFov = 68.6f, DefaultPreroll = 24f, DefaultPostroll = 8f, DefaultMinClip = 15f, DefaultMaxClip = 36f;
        public const float DefaultThreshold = 10f, DefaultWarmup = 3f, DefaultLook = 3f;
        public const int DefaultJpegQuality = 88, DefaultMultiKillMin = 6, DefaultRingMb = 640;
        public const float DefaultRawGb = 5f;
        /// <summary>Camera distance factor per preset (G = the game's own distance).</summary>
        public static float DistanceScale(char preset) => preset == 'Y' ? 0.6f : preset == 'O' ? 0.8f : 1f;

        public string OutDir;
        public int Width = DefaultWidth, Height = DefaultHeight, Fps = DefaultFps, Clips = DefaultClips, Seconds = DefaultSeconds, Stills = DefaultStills;
        public float Fov = DefaultFov, Preroll = DefaultPreroll, Postroll = DefaultPostroll, MinClip = DefaultMinClip, MaxClip = DefaultMaxClip;
        public float Threshold = DefaultThreshold, Warmup = DefaultWarmup, Look = DefaultLook;
        public int JpegQuality = DefaultJpegQuality, MultiKillMin = DefaultMultiKillMin, RingMb = DefaultRingMb;
        public char Camera = 'O';
        public float RawGb = DefaultRawGb;
        public bool MusicOff;
        public bool FrameBand = true;
        public bool HudSafe;
        /// <summary>The HUD-less stream (game camera only) is written.</summary>
        public bool HudOff = true;
        /// <summary>The HUD stream (UI composited over the game) is written.</summary>
        public bool HudOn;

        /// <summary>True when <paramref name="args"/> carries <c>-ddMkCapture &lt;value&gt;</c>. A scan only: no allocation.</summary>
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

        /// <summary>Null + null error: not requested. Null + error: requested but malformed. Otherwise the options.</summary>
        public static MkCaptureOptions Parse(string[] args, out string error)
        {
            error = null;
            if (!IsRequested(args)) return null;
            var o = new MkCaptureOptions { OutDir = TdCaptureOptions.Arg(args, OutFlag) };
            if (!Path.IsPathRooted(o.OutDir))
            {
                error = OutFlag + " '" + o.OutDir + "' is not an absolute directory";
                return null;
            }

            string size = TdCaptureOptions.Arg(args, SizeFlag);
            if (size != null && !TryParseSize(size, out o.Width, out o.Height))
            {
                error = SizeFlag + " must be WxH, both even, 256..4096 (got '" + size + "')";
                return null;
            }

            if (!Int(args, FpsFlag, 10, 60, ref o.Fps, out error) || !Int(args, ClipsFlag, 1, 60, ref o.Clips, out error) ||
                !Int(args, SecondsFlag, 60, 7200, ref o.Seconds, out error) || !Int(args, StillsFlag, 0, 3, ref o.Stills, out error) ||
                !Int(args, JpegQualityFlag, 60, 100, ref o.JpegQuality, out error) || !Int(args, MultiKillMinFlag, 2, 20, ref o.MultiKillMin, out error) ||
                !Int(args, RingMbFlag, 32, 2048, ref o.RingMb, out error) || !Float(args, LookFlag, 0f, 8f, ref o.Look, out error) ||
                !Float(args, PrerollFlag, 3f, 30f, ref o.Preroll, out error) || !Float(args, PostrollFlag, 3f, 20f, ref o.Postroll, out error) ||
                !Float(args, MinClipFlag, 5f, 60f, ref o.MinClip, out error) || !Float(args, MaxClipFlag, 10f, 90f, ref o.MaxClip, out error) ||
                !Float(args, ThresholdFlag, 1f, 200f, ref o.Threshold, out error) || !Float(args, WarmupFlag, 0f, 60f, ref o.Warmup, out error) ||
                !Float(args, RawGbFlag, 0.2f, 50f, ref o.RawGb, out error))
            {
                return null;
            }

            string fov = TdCaptureOptions.Arg(args, FovFlag);
            if (fov != null)
            {
                float f;
                if (!float.TryParse(fov, NumberStyles.Float, CultureInfo.InvariantCulture, out f) || !(f == 0f || (f >= 20f && f <= 100f)))
                {
                    error = FovFlag + " must be 0 (the game's own) or 20..100 degrees (got '" + fov + "')";
                    return null;
                }

                o.Fov = f;
            }

            if (o.MinClip > o.MaxClip)
            {
                error = MinClipFlag + " (" + o.MinClip + ") > " + MaxClipFlag + " (" + o.MaxClip + ")";
                return null;
            }

            string hud = TdCaptureOptions.Arg(args, HudFlag) ?? "off";
            if (hud == "off") { o.HudOff = true; o.HudOn = false; }
            else if (hud == "on") { o.HudOff = false; o.HudOn = true; }
            else if (hud == "both") { o.HudOff = true; o.HudOn = true; }
            else
            {
                error = HudFlag + " must be off, on or both (got '" + hud + "')";
                return null;
            }

            string cam = TdCaptureOptions.Arg(args, CameraFlag) ?? "O";
            if (cam != "Y" && cam != "O" && cam != "G")
            {
                error = CameraFlag + " must be Y, O or G (got '" + cam + "')";
                return null;
            }

            o.Camera = cam[0];
            string music = TdCaptureOptions.Arg(args, MusicOffFlag) ?? "0";
            if (music != "0" && music != "1")
            {
                error = MusicOffFlag + " must be 0 or 1 (got '" + music + "')";
                return null;
            }

            o.MusicOff = music == "1";
            string band = TdCaptureOptions.Arg(args, FrameBandFlag) ?? "1";
            if (band != "0" && band != "1")
            {
                error = FrameBandFlag + " must be 0 or 1 (got '" + band + "')";
                return null;
            }

            o.FrameBand = band == "1";
            string safe = TdCaptureOptions.Arg(args, HudSafeFlag) ?? "0";
            if (safe != "0" && safe != "1")
            {
                error = HudSafeFlag + " must be 0 or 1 (got '" + safe + "')";
                return null;
            }

            o.HudSafe = safe == "1";
            return o;
        }

        public static bool TryParseSize(string s, out int w, out int h)
        {
            w = h = 0;
            int x = s.IndexOf('x');
            if (x <= 0 || x >= s.Length - 1) return false;
            if (!int.TryParse(s.Substring(0, x), NumberStyles.None, CultureInfo.InvariantCulture, out w) ||
                !int.TryParse(s.Substring(x + 1), NumberStyles.None, CultureInfo.InvariantCulture, out h)) return false;
            return w >= 256 && h >= 256 && w <= 4096 && h <= 4096 && (w & 1) == 0 && (h & 1) == 0;
        }

        private static bool Int(string[] args, string flag, int min, int max, ref int value, out string error)
        {
            error = null;
            string s = TdCaptureOptions.Arg(args, flag);
            if (s == null) return true;
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= min && v <= max)
            {
                value = v;
                return true;
            }

            error = flag + " must be an integer " + min + ".." + max + " (got '" + s + "')";
            return false;
        }

        private static bool Float(string[] args, string flag, float min, float max, ref float value, out string error)
        {
            error = null;
            string s = TdCaptureOptions.Arg(args, flag);
            if (s == null) return true;
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max)
            {
                value = v;
                return true;
            }

            error = flag + " must be a number " + min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture) + " (got '" + s + "')";
            return false;
        }
    }

    /// <summary>What a highlight event was (weights in <see cref="MkWeights"/>).</summary>
    public enum MkEventKind
    {
        Kill = 0,
        MultiKill = 1,
        Explosion = 2,
        Special = 3,
        LowHealth = 4,
        LastZombie = 5,
        /// <summary>An Elite body within 18 m for the first time.</summary>
        EliteNear = 6,
        /// <summary>S-11 §9-1: an Ultra (Executioner) body within 18 m for the first time.</summary>
        UltraNear = 7,
        /// <summary>S-11 §9-1: ≥ 3 rocket-launcher shots within 1 s.</summary>
        RocketBurst = 8
    }

    /// <summary>
    /// WP-110 · how much one event adds to the "something worth filming is happening" heat. Explosions and multi-kills weigh most
    /// (the crowd moments of R4-PLAN §3.10), a lone kill little; Elite / Ultra bodies and the low-health escape add on top.
    /// </summary>
    public static class MkWeights
    {
        public const int KindCount = 9;

        /// <param name="count">MultiKill: bodies in the burst; Kill: chain length; otherwise ignored.</param>
        /// <param name="tier">0 Standard … 3 Ultra (the kill's tier); otherwise 0.</param>
        /// <param name="multiKillMin">S-11 §9-1: a burst below this many bodies weighs like separate kills, not a beat.</param>
        public static float Of(MkEventKind kind, int count, int tier, int multiKillMin = MkCaptureOptions.DefaultMultiKillMin)
        {
            switch (kind)
            {
                case MkEventKind.Kill: return 1f + (tier >= 2 ? 2f : tier == 1 ? 0.5f : 0f) + (count >= 3 ? 0.5f : 0f);
                case MkEventKind.MultiKill: return count >= multiKillMin ? 1.5f * count : 0.5f * Math.Max(2, count);
                case MkEventKind.Explosion: return 3f;
                case MkEventKind.Special: return 3f;
                case MkEventKind.LowHealth: return 5f;
                case MkEventKind.LastZombie: return 2f;
                case MkEventKind.EliteNear: return 4f;
                case MkEventKind.UltraNear: return 8f;
                case MkEventKind.RocketBurst: return 6f;
                default: return 0f;
            }
        }

        /// <summary>
        /// The game's own FeelMultiKill (a pull or chain reaching a new TIER: 3 / 5 / 8 bodies). From
        /// <paramref name="multiKillMin"/> bodies the beat is <see cref="MkBurstBeat"/>'s, counted from the kills, so the game's
        /// event weighs nothing there (S-11 12.10: never twice).
        /// </summary>
        public static float GameMultiKill(int count, int multiKillMin = MkCaptureOptions.DefaultMultiKillMin) =>
            count >= multiKillMin ? 0f : Of(MkEventKind.MultiKill, count, 0, multiKillMin);
    }

    /// <summary>
    /// S-11 12.10 (dry-2 clip 02: the game said "count 3" for a 4-body rocket): FeelMultiKill is raised only when a pull or the
    /// chain reaches a new tier (3 / 5 / 8), so a 6- or 7-body blast never says 6 and the §9-1 beat (from MultiKillMin bodies)
    /// could not start for it. The beat is counted from the kills: a burst is max(GroupKills, ChainKills); when it first reaches
    /// the minimum it weighs <see cref="MkWeights.Of"/>(MultiKill, bodies) = 1.5 × bodies, every further body 1.5 more. The
    /// burst is over when both counts fall below 3 (a new pull outside the chain window), as the game's chain starts over.
    /// </summary>
    public sealed class MkBurstBeat
    {
        public const int EndBelow = 3;
        private readonly int _min;
        private int _credited;

        public MkBurstBeat(int multiKillMin)
        {
            _min = Math.Max(EndBelow, multiKillMin);
        }

        /// <returns>the weight this kill adds to the beat (0: none); <paramref name="burst"/> = the burst's bodies now.</returns>
        public float OnKill(int groupKills, int chainKills, out int burst)
        {
            burst = Math.Max(groupKills, chainKills);
            if (burst < EndBelow) _credited = 0;
            if (burst < _min || burst <= _credited) return 0f;
            float w = _credited == 0 ? MkWeights.Of(MkEventKind.MultiKill, burst, 0, _min) : 1.5f * (burst - _credited);
            _credited = burst;
            return w;
        }
    }

    /// <summary>One clip decision from <see cref="MkHighlightDirector.Tick"/>.</summary>
    public enum MkDecision
    {
        None = 0,
        Start = 1,
        End = 2
    }

    /// <summary>
    /// WP-110 · when to keep a clip, in real seconds since the session's t0 (pure, EditMode-tested). Heat = the summed weights of
    /// the events of the last <see cref="Window"/> seconds. Outside a clip: heat ≥ threshold (and the cool-down since the last clip
    /// over, and clips left) starts one at max(trigger − preroll, oldest buffered frame, last clip end); its planned end is
    /// trigger + postroll, at least start + minClip. Inside: every event of weight ≥ 2 pushes the end to its time + postroll;
    /// never past start + maxClip. End when the clock reaches the planned end. The peak = the moment of the highest heat inside.
    /// </summary>
    public sealed class MkHighlightDirector
    {
        public const float Window = 4f, CoolDown = 2f, ExtendWeight = 2f;

        private readonly float _threshold, _preroll, _postroll, _minClip, _maxClip;
        private readonly int _maxClips;
        private readonly Queue<KeyValuePair<double, float>> _recent = new Queue<KeyValuePair<double, float>>(256);
        private float _heat;

        public bool InClip { get; private set; }
        public double ClipStart { get; private set; }
        public double ClipEnd { get; private set; }
        public double TriggerT { get; private set; }
        public double PeakT { get; private set; }
        public float PeakHeat { get; private set; }
        public int Clips { get; private set; }
        public double LastClipEnd { get; private set; } = double.NegativeInfinity;
        public bool Exhausted => Clips >= _maxClips && !InClip;

        public MkHighlightDirector(float threshold, float preroll, float postroll, float minClip, float maxClip, int maxClips)
        {
            if (minClip > maxClip) throw new ArgumentException("minClip > maxClip");
            _threshold = threshold; _preroll = preroll; _postroll = postroll; _minClip = minClip; _maxClip = maxClip; _maxClips = maxClips;
        }

        /// <summary>The heat at <paramref name="t"/> (events older than the window dropped).</summary>
        public float Heat(double t)
        {
            while (_recent.Count > 0 && _recent.Peek().Key <= t - Window)
            {
                _heat -= _recent.Dequeue().Value;
            }

            if (_recent.Count == 0) _heat = 0f;   // no float residue
            return _heat;
        }

        /// <summary>An event at <paramref name="t"/> (events arrive in time order).</summary>
        public void Add(double t, float weight)
        {
            if (weight <= 0f) return;
            _recent.Enqueue(new KeyValuePair<double, float>(t, weight));
            _heat += weight;
            if (InClip && weight >= ExtendWeight)
            {
                ClipEnd = Math.Min(Math.Max(ClipEnd, t + _postroll), ClipStart + _maxClip);
            }
        }

        /// <summary>Called once per frame. <paramref name="oldestBuffered"/>: the time of the oldest frame still in the preroll ring.</summary>
        public MkDecision Tick(double t, double oldestBuffered)
        {
            float heat = Heat(t);
            if (InClip)
            {
                if (heat > PeakHeat) { PeakHeat = heat; PeakT = t; }
                if (t < ClipEnd) return MkDecision.None;
                InClip = false;
                LastClipEnd = ClipEnd;
                return MkDecision.End;
            }

            if (Clips >= _maxClips || heat < _threshold || t - LastClipEnd < CoolDown) return MkDecision.None;
            InClip = true;
            Clips++;
            TriggerT = t;
            PeakT = t;
            PeakHeat = heat;
            double oldest = double.IsNaN(oldestBuffered) || oldestBuffered > t ? t : oldestBuffered;   // empty ring (+∞): start now
            ClipStart = Math.Max(Math.Max(t - _preroll, oldest), LastClipEnd);
            ClipEnd = Math.Min(Math.Max(t + _postroll, ClipStart + _minClip), ClipStart + _maxClip);
            return MkDecision.Start;
        }

        /// <summary>The session is stopping inside a clip: end it now (the caller keeps it if it is long enough).</summary>
        public void ForceEnd(double t)
        {
            if (!InClip) return;
            InClip = false;
            ClipEnd = Math.Min(ClipEnd, t);
            LastClipEnd = ClipEnd;
        }
    }

    /// <summary>
    /// WP-110 · the preroll: the last N encoded frames (grab time + JPEG bytes), oldest dropped first. Frames are pushed in
    /// completion order, which may differ from grab order by a frame or two; <see cref="TakeFrom"/> returns them by grab index.
    /// </summary>
    public sealed class MkFrameRing
    {
        public struct Frame
        {
            public long Index;
            public double T;
            public byte[] Jpeg;
            /// <summary>The HUD stream's JPEG of the same frame (null without it).</summary>
            public byte[] JpegHud;
        }

        private readonly Frame[] _items;
        private readonly long _budget;
        private int _head, _count;

        /// <param name="budgetBytes">The JPEG bytes held never exceed this (the oldest go first); ≤ 0 = count only.</param>
        public MkFrameRing(int capacity, long budgetBytes = 0)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new Frame[capacity];
            _budget = budgetBytes;
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public long Bytes { get; private set; }
        public long PeakBytes { get; private set; }
        /// <summary>Frames dropped for the byte budget before the count capacity was reached.</summary>
        public long BudgetEvictions { get; private set; }

        private void DropOldest()
        {
            Bytes -= Size(_items[_head]);
            _items[_head] = default;
            _head = (_head + 1) % _items.Length;
            _count--;
        }

        private static long Size(Frame f) => (f.Jpeg != null ? f.Jpeg.Length : 0) + (f.JpegHud != null ? f.JpegHud.Length : 0);

        /// <summary>Grab time of the oldest frame held, +∞ when empty.</summary>
        public double OldestT
        {
            get
            {
                double m = double.PositiveInfinity;
                for (int i = 0; i < _count; i++) m = Math.Min(m, _items[(_head + i) % _items.Length].T);
                return m;
            }
        }

        public void Push(long index, double t, byte[] jpeg, byte[] jpegHud = null)
        {
            var f = new Frame { Index = index, T = t, Jpeg = jpeg, JpegHud = jpegHud };
            if (_count == _items.Length) DropOldest();
            while (_budget > 0 && _count > 0 && Bytes + Size(f) > _budget)
            {
                DropOldest();
                BudgetEvictions++;
            }

            _items[(_head + _count) % _items.Length] = f;
            _count++;
            Bytes += Size(f);
            if (Bytes > PeakBytes) PeakBytes = Bytes;
        }

        /// <summary>Removes and returns every held frame with grab time ≥ <paramref name="t"/>, in grab-index order.</summary>
        public List<Frame> TakeFrom(double t)
        {
            var taken = new List<Frame>(_count);
            var kept = new List<Frame>(_count);
            for (int i = 0; i < _count; i++)
            {
                Frame f = _items[(_head + i) % _items.Length];
                (f.T >= t ? taken : kept).Add(f);
            }

            for (int i = 0; i < _items.Length; i++) _items[i] = default;
            _head = 0;
            _count = 0;
            Bytes = 0;
            foreach (Frame f in kept) Push(f.Index, f.T, f.Jpeg, f.JpegHud);
            taken.Sort((a, b) => a.Index.CompareTo(b.Index));
            return taken;
        }
    }

    /// <summary>S-11 §9-1 · "RocketBurst": true on the shot that makes ≥ <see cref="Shots"/> rocket shots within <see cref="Seconds"/>
    /// (then quiet until the window has emptied, so one burst is one event).</summary>
    public sealed class MkRocketBurst
    {
        public const int Shots = 3;
        public const double Seconds = 1.0;
        private readonly Queue<double> _shots = new Queue<double>(8);
        private double _quietUntil = double.NegativeInfinity;

        public bool Shot(double t)
        {
            while (_shots.Count > 0 && _shots.Peek() <= t - Seconds) _shots.Dequeue();
            _shots.Enqueue(t);
            if (t < _quietUntil || _shots.Count < Shots) return false;
            _quietUntil = t + Seconds;
            _shots.Clear();
            return true;
        }
    }

    /// <summary>
    /// S-11 §9-3 · the HUD over the off-screen master. Screen-space overlay canvases never reach a camera target, so the UI is drawn
    /// a second time by its own camera into a target cleared to black and once more cleared to white; the difference gives each
    /// pixel's coverage (difference matting: over black = C·a, over white = C·a + (1 − a)), exact for the "over" blending uGUI uses.
    /// The project renders in linear space and the targets are sRGB, so the maths runs on linear values (two lookup tables).
    /// Buffers are RGBA32 in the same row order; the result is written into <paramref name="dst"/> (may be <paramref name="game"/>).
    /// </summary>
    public static class MkHudComposite
    {
        private static float[] s_toLinear;
        private static byte[] s_toSrgb;
        private const int SrgbSteps = 4096;

        private static void Tables()
        {
            if (s_toLinear != null) return;
            var lin = new float[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                lin[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            }

            var srgb = new byte[SrgbSteps + 1];
            for (int i = 0; i <= SrgbSteps; i++)
            {
                double l = i / (double)SrgbSteps;
                double c = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
                srgb[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(c * 255)));
            }

            s_toSrgb = srgb;
            s_toLinear = lin;
        }

        /// <summary>Coverage of one pixel from its black / white renders (linear, averaged over RGB, clamped 0..1).</summary>
        public static float Coverage(byte br, byte bg, byte bb, byte wr, byte wg, byte wb)
        {
            Tables();
            float d = (s_toLinear[wr] - s_toLinear[br] + s_toLinear[wg] - s_toLinear[bg] + s_toLinear[wb] - s_toLinear[bb]) / 3f;
            float a = 1f - d;
            return a < 0f ? 0f : a > 1f ? 1f : a;
        }

        public static void Composite(byte[] game, byte[] uiBlack, byte[] uiWhite, byte[] dst)
        {
            Tables();
            float[] L = s_toLinear;
            byte[] S = s_toSrgb;
            int n = Math.Min(Math.Min(game.Length, uiBlack.Length), Math.Min(uiWhite.Length, dst.Length));
            for (int i = 0; i + 3 < n; i += 4)
            {
                float br = L[uiBlack[i]], bg = L[uiBlack[i + 1]], bb = L[uiBlack[i + 2]];
                float a = 1f - (L[uiWhite[i]] - br + L[uiWhite[i + 1]] - bg + L[uiWhite[i + 2]] - bb) / 3f;
                if (a <= 0.001f) { if (!ReferenceEquals(dst, game)) { dst[i] = game[i]; dst[i + 1] = game[i + 1]; dst[i + 2] = game[i + 2]; dst[i + 3] = 255; } continue; }
                if (a > 1f) a = 1f;
                float k = 1f - a;
                dst[i] = S[(int)(Math.Min(1f, L[game[i]] * k + br) * SrgbSteps + 0.5f)];
                dst[i + 1] = S[(int)(Math.Min(1f, L[game[i + 1]] * k + bg) * SrgbSteps + 0.5f)];
                dst[i + 2] = S[(int)(Math.Min(1f, L[game[i + 2]] * k + bb) * SrgbSteps + 0.5f)];
                dst[i + 3] = 255;
            }
        }
    }

    /// <summary>
    /// WP-110 dry-1 (11.10) · keeping the hero in the narrow portrait master. The game's aim look-ahead (≤ 2.5 m), the fire lean
    /// and the capture's look room all shift the TopDown camera off the hero; 9:16 is only 42° wide, so the hero ended at the
    /// left edge. The agent keeps the hero's viewport point inside the band (y from the bottom; the lower part leaves look
    /// room ahead): outside it, the camera moves by exactly the ground-plane distance that brings the hero back to the band's
    /// edge, smoothed. The look room toward the crowd is mostly along the frame's long axis (sideways at most 1 m).
    /// </summary>
    public static class MkFraming
    {
        public const float X0 = 0.3f, X1 = 0.7f, Y0 = 0.25f, Y1 = 0.7f;
        public const float MaxOffset = 8f, LookSideMax = 1f;

        /// <summary>S-11 11.10 · the Reels safe rectangle of a 1080×1920 master, x 0–960 and y 250–1520 from the top, as a camera
        /// viewport rect (origin bottom-left, 0..1): the HUD camera draws its canvases inside it with -ddMkHudSafe 1.</summary>
        public static readonly Rect HudSafe = new Rect(0f, (1920f - 1520f) / 1920f, 960f / 1080f, (1520f - 250f) / 1920f);

        /// <summary>The hero's viewport point clamped into the band; true when it was outside.</summary>
        public static bool BandTarget(float vx, float vy, out float tx, out float ty)
        {
            tx = Mathf.Clamp(vx, X0, X1);
            ty = Mathf.Clamp(vy, Y0, Y1);
            return tx != vx || ty != vy;
        }

        /// <summary>Look room toward <paramref name="crowdCentre"/> (relative to the hero, y ignored): at most <paramref name="metres"/>, sideways at most <see cref="LookSideMax"/>.</summary>
        public static Vector3 Look(Vector3 crowdCentre, float metres)
        {
            crowdCentre.y = 0f;
            Vector3 v = Vector3.ClampMagnitude(crowdCentre, metres);
            v.x = Mathf.Clamp(v.x, -LookSideMax, LookSideMax);
            return v;
        }
    }

    /// <summary>gt M1 (11.10) · the session's raw-byte budget (clip JPEGs + stills), counted from the writer threads.</summary>
    public sealed class MkRawBudget
    {
        private long _used;
        public readonly long Limit;

        public MkRawBudget(float gigabytes)
        {
            Limit = (long)(gigabytes * 1024.0 * 1024.0 * 1024.0);
        }

        public long Used => System.Threading.Interlocked.Read(ref _used);
        public bool Exceeded => Used >= Limit;
        public void Add(long bytes) => System.Threading.Interlocked.Add(ref _used, bytes);
    }

    /// <summary>
    /// gt S3 (11.10) · the session's recent highlight events, so a clip carries the events of its preroll too (its kills, its
    /// events in clip.json) and its trigger kind is the kind that weighed most in the burst that started it — not only what
    /// came after the trigger (dry-2 clip 02 said 5 kills and "Kill" for an 11-kill explosion burst).
    /// </summary>
    public sealed class MkEventLog
    {
        public struct Entry
        {
            public double T;
            public MkEventKind Kind;
            public float Weight;
            public string Detail;
            /// <summary>Kill: the pull's / blast's bodies so far (FeelKill.GroupKills); a MultiKill beat: the burst's bodies.</summary>
            public int Count;
            /// <summary>Kill: the 0.6 s chain (FeelKill.ChainKills).</summary>
            public int Chain;
        }

        private readonly Queue<Entry> _q = new Queue<Entry>(512);
        private readonly double _keepSeconds;

        public MkEventLog(double keepSeconds)
        {
            _keepSeconds = keepSeconds;
        }

        public int Count => _q.Count;

        public void Add(double t, MkEventKind kind, float weight, string detail, int count = 0, int chain = 0)
        {
            _q.Enqueue(new Entry { T = t, Kind = kind, Weight = weight, Detail = detail, Count = count, Chain = chain });
            while (_q.Count > 0 && _q.Peek().T < t - _keepSeconds) _q.Dequeue();
        }

        /// <summary>The kept events with t ≥ <paramref name="from"/>, in time order.</summary>
        public List<Entry> Since(double from)
        {
            var list = new List<Entry>();
            foreach (Entry e in _q) if (e.T >= from) list.Add(e);
            return list;
        }

        /// <summary>The kind with the largest summed weight in [t − window, t] (ties: the later event's kind); null when none.</summary>
        public MkEventKind? TriggerKind(double t, double window)
        {
            var sum = new float[MkWeights.KindCount];
            MkEventKind? best = null;
            foreach (Entry e in _q)
            {
                if (e.T < t - window || e.T > t) continue;
                sum[(int)e.Kind] += e.Weight;
                if (best == null || sum[(int)e.Kind] >= sum[(int)best.Value]) best = e.Kind;
            }

            return best;
        }
    }
}
#endif
