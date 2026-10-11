using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>The AP-035 logo's state (<see cref="UiLogoTimeline"/>).</summary>
    public enum UiLogoPhase
    {
        /// <summary>Not on screen.</summary>
        Hidden = 0,
        /// <summary>The intro (motion-spec frames 1..67) at <see cref="UiLogoTimeline.IntroSpeed"/>.</summary>
        Intro = 1,
        /// <summary>The 4.0 s loop on the canonical frame (ring breath, crack glow, ash, sparks).</summary>
        Loop = 2,
        /// <summary>The loading outro (v001 outro.mp4, 17 frames): hold, the wordmark wiped left to right, the seal under ink.</summary>
        Outro = 3,
        /// <summary>Reduced motion on a loading screen: the canonical frame, eased in over 0.5 s, then still.</summary>
        Static = 4,
        /// <summary>The menu intro's end, or any skip / reduced-motion leave: the whole logo fades out from where it is.</summary>
        FadeOut = 5
    }

    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10, K2/K3/K6/K7) · the logo's timeline, pure C# (no Unity objects: EditMode-testable).
    /// Every state comes from the absolute unscaled clock the caller passes in (<see cref="UiFxGlobals.Now"/>): a phase is
    /// "now minus its start", never a frame counter or an accumulated delta - a long frame jumps the animation to the right
    /// time instead of slowing it (C-00 08.10, the 0-6.9 s low-fps load). The motion-spec numbers (24 fps, 1-based frames,
    /// PTS = (frame - 1) / 24) are tested against <c>UI/Art/logo-intake.json</c>, which the bake writes from the spec.
    /// Rules:
    ///   loading   intro x <see cref="IntroSpeed"/> once -> loop while the load runs -> outro when the curtain opens. The
    ///             load may leave (<see cref="CanLeave"/>) once the canonical frame is reached, the intro is over, or
    ///             <see cref="MaxExtraHoldSeconds"/> after it said it was ready - so the logo adds at most 0.5 s.
    ///   menu      the intro x <see cref="IntroSpeed"/> up to the canonical frame, then a <see cref="MenuFadeSeconds"/> fade:
    ///             <see cref="MenuIntroDuration"/> ≤ 2.0 s; a skip fades from the canonical pose over <see cref="SkipFadeSeconds"/>.
    ///   reduced   loading: <see cref="Static"/> eased in over 0.5 s, leaves at once, fades out over SkipFadeSeconds;
    ///             menu: never plays.
    /// </summary>
    public sealed class UiLogoTimeline
    {
        public const float Fps = 24f;
        public const int IntroFrames = 67;
        public const int LoopFrames = 96;
        public const int OutroFrames = 17;

        /// <summary>Spec seconds (1x): PTS of frame f = (f - 1) / 24.</summary>
        public const float IntroSeconds = IntroFrames / Fps;              // 2.7917
        public const float LoopSeconds = LoopFrames / Fps;                // 4.0
        public const float RevealStartSeconds = (3 - 1) / Fps;            // frame 3
        public const float RevealEndSeconds = (47 - 1) / Fps;             // frame 47 (the UiFx duration: frames 1..47)
        public const float SealFirstSeconds = (39 - 1) / Fps;             // frame 39
        public const float ImpactSeconds = (47 - 1) / Fps;                // frame 47: the seal lands, the BootLogo bell (menu)
        public const float FxEndSeconds = (54 - 1) / Fps;                 // frame 54
        public const float CanonicalSeconds = (55 - 1) / Fps;             // frame 55: the canonical frame holds from here

        /// <summary>K2: the loading intro runs 1.33x (2.10 s); the impact at 1.44 s, the canonical frame at 1.69 s after the screen shows.</summary>
        public const float IntroSpeed = 1.33f;

        /// <summary>K3: the most the logo may add to a load that is ready (outro excluded: it never delays Revealed).</summary>
        public const float MaxExtraHoldSeconds = 0.5f;

        /// <summary>K6: v001 outro.mp4 - 17 frames at 24 fps.</summary>
        public const float OutroSeconds = OutroFrames / Fps;              // 0.7083
        public const float OutroHoldSeconds = (8 - 1) / Fps;              // frames 1..8 hold
        public const float OutroEraseSeconds = (14 - 8) / Fps;            // frames 8..14: the wordmark wipe
        public const float OutroSealInkFrom = (9 - 1) / Fps;              // frame 9: the seal goes under ink
        public const float OutroSealInkSeconds = (12 - 9) / Fps;
        public const float OutroFadeFrom = (10 - 1) / Fps;                // frames 10..16: what is left fades
        public const float OutroFadeSeconds = (16 - 10) / Fps;

        /// <summary>K6: reduced motion - v001 reduced_motion.mp4 fades in over ~0.42 s (frames 1..11); the task: 0.5 s.</summary>
        public const float ReducedFadeInSeconds = 0.5f;

        /// <summary>K7: the menu intro's closing fade and the fade any skip / reduced-motion leave uses.</summary>
        public const float MenuFadeSeconds = 0.3f;
        public const float SkipFadeSeconds = 0.14f;   // UiDuration.Fast

        /// <summary>The menu intro, start to gone: canonical / speed + fade (1.69 + 0.30 = 1.99 s ≤ 2.0, K7).</summary>
        public const float MenuIntroDuration = CanonicalSeconds / IntroSpeed + MenuFadeSeconds;

        // Seal samples, motion-spec seal.scale_samples frames 39..54: the layer's top y (spec px, y down), sx, sy, crack glow.
        public const int SealFirstFrame = 39;
        public const int SealLastFrame = 54;
        internal static readonly float[] SealY = { -610f, -602f, -577f, -534f, -470f, -384f, -270f, -122f, 78f, 124f, 101f, 64f, 69f, 81f, 80f, 78f };
        internal static readonly float[] SealSx = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1.04f, 1.025f, 0.985f, 0.99f, 1.006f, 1.003f, 1f };
        internal static readonly float[] SealSy = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0.92f, 0.96f, 1.025f, 1.015f, 0.994f, 0.997f, 1f };
        internal static readonly float[] SealGlow = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.65f, 0.4f, 0.2f, 0.08f, 0f, 0f, 0f };

        /// <summary>The seal layer (570², spec px): its rest top y and size; the pivot is its bottom centre (rest y 648).</summary>
        public const float SealLayerSize = 570f;
        public const float SealRestTop = 78f;
        public const float SealRestBottom = SealRestTop + SealLayerSize;  // 648

        /// <summary>motion-spec camera_shake: frames 48..50, whole composition, spec px (y down).</summary>
        public const int ShakeFirstFrame = 48;
        internal static readonly float[] ShakeX = { 2f, -1f, 1f };
        internal static readonly float[] ShakeY = { -1f, 1f, 0f };

        /// <summary>Impact (frames 47..54): the closed ink ring and the ink blot (motion-spec closed_ink_ring / ink).</summary>
        public const int ImpactFrame = 47;
        public const float ImpactFrames = 7f;
        public static readonly Vector2 RingSizeStart = new Vector2(510f, 533f);
        public static readonly Vector2 RingSizeEnd = new Vector2(625f, 653f);
        public const float RingPeakAlpha = 0.72f;
        public const float RingBreathPeakAlpha = 0.045f;
        public const float BlotPeakAlpha = 0.35f;
        public const float LoopGlowPeakAlpha = 0.28f;

        public enum Mode
        {
            Loading,
            Menu
        }

        private UiLogoPhase _phase;
        private Mode _mode;
        private bool _reduced;
        private float _introStart;
        private float _speed = IntroSpeed;
        private float _readyAt = -1f;
        private float _phaseStart;
        private float _frozenSpec = CanonicalSeconds;   // the intro's spec time held by an outro / fade
        private float _fadeSeconds;
        private float _fadeFromAlpha = 1f;

        public UiLogoPhase Phase => _phase;
        public Mode CurrentMode => _mode;
        public bool Reduced => _reduced;
        public float IntroStart => _introStart;
        public float Speed => _speed;
        public float ReadyAt => _readyAt;
        public float PhaseStart => _phaseStart;

        /// <summary>Wall seconds of the whole intro at the current speed.</summary>
        public float IntroWallSeconds => IntroSeconds / _speed;

        /// <summary>Start a loading logo at <paramref name="now"/> (reduced: the static frame, eased in).</summary>
        public void BeginLoading(float now, bool reduced, float speed = IntroSpeed)
        {
            _mode = Mode.Loading;
            Begin(now, reduced, speed);
            _phase = reduced ? UiLogoPhase.Static : UiLogoPhase.Intro;
        }

        /// <summary>Start the menu intro (reduced motion: nothing plays - the caller checks <see cref="Phase"/> == Hidden).</summary>
        public void BeginMenu(float now, bool reduced, float speed = IntroSpeed)
        {
            _mode = Mode.Menu;
            Begin(now, reduced, speed);
            _phase = reduced ? UiLogoPhase.Hidden : UiLogoPhase.Intro;
        }

        private void Begin(float now, bool reduced, float speed)
        {
            _reduced = reduced;
            _speed = Mathf.Max(0.1f, speed);
            _introStart = now;
            _phaseStart = now;
            _readyAt = -1f;
            _frozenSpec = CanonicalSeconds;
        }

        /// <summary>The load is ready (loaded and its minimum display shown). The first call counts.</summary>
        public void NotifyReady(float now)
        {
            if (_readyAt < 0f)
            {
                _readyAt = now;
            }
        }

        /// <summary>
        /// K3: the load may activate - not intro (loop, static, hidden), the canonical frame reached (the intro's last
        /// 12 frames are the canonical hold), or <see cref="MaxExtraHoldSeconds"/> past ready.
        /// </summary>
        public bool CanLeave(float now)
        {
            Tick(now);
            if (_phase != UiLogoPhase.Intro)
            {
                return true;
            }

            if (SpecTime(now) >= CanonicalSeconds)
            {
                return true;
            }

            return _readyAt >= 0f && now - _readyAt >= MaxExtraHoldSeconds;
        }

        /// <summary>The intro's spec time (1x seconds) at <paramref name="now"/>, clamped to the intro.</summary>
        public float SpecTime(float now)
        {
            if (_phase == UiLogoPhase.Outro || _phase == UiLogoPhase.FadeOut)
            {
                return _frozenSpec;
            }

            if (_phase == UiLogoPhase.Static || _phase == UiLogoPhase.Loop)
            {
                return IntroSeconds;
            }

            return Mathf.Clamp((now - _introStart) * _speed, 0f, IntroSeconds);
        }

        /// <summary>Seconds into the loop (0..4), from the absolute clock: the loop starts where the intro ends.</summary>
        public float LoopTime(float now)
        {
            float t = now - (_introStart + IntroWallSeconds);
            if (t <= 0f)
            {
                return 0f;
            }

            return t - Mathf.Floor(t / LoopSeconds) * LoopSeconds;
        }

        /// <summary>Seconds into the current phase.</summary>
        public float PhaseSeconds(float now)
        {
            return Mathf.Max(0f, now - _phaseStart);
        }

        /// <summary>Advance the phase from the absolute clock (any step size lands in the right phase).</summary>
        public void Tick(float now)
        {
            switch (_phase)
            {
                case UiLogoPhase.Intro:
                    if (_mode == Mode.Menu)
                    {
                        float canonicalAt = _introStart + CanonicalSeconds / _speed;
                        if (now >= canonicalAt)
                        {
                            StartFade(canonicalAt, CanonicalSeconds, MenuFadeSeconds, 1f);
                            Tick(now);
                        }
                    }
                    else if (now >= _introStart + IntroWallSeconds)
                    {
                        _phase = UiLogoPhase.Loop;
                        _phaseStart = _introStart + IntroWallSeconds;
                    }

                    break;
                case UiLogoPhase.Outro:
                    if (now >= _phaseStart + OutroSeconds)
                    {
                        _phase = UiLogoPhase.Hidden;
                    }

                    break;
                case UiLogoPhase.FadeOut:
                    if (now >= _phaseStart + _fadeSeconds)
                    {
                        _phase = UiLogoPhase.Hidden;
                    }

                    break;
            }
        }

        private void StartFade(float at, float spec, float seconds, float fromAlpha)
        {
            _frozenSpec = spec;
            _phase = UiLogoPhase.FadeOut;
            _phaseStart = at;
            _fadeSeconds = Mathf.Max(0.01f, seconds);
            _fadeFromAlpha = fromAlpha;
        }

        /// <summary>
        /// The curtain opens: the loading outro from here (the intro's pose is held where it was - normally the canonical
        /// frame); reduced motion: a short fade. Ignored when hidden or already leaving.
        /// </summary>
        public void BeginOutro(float now)
        {
            Tick(now);
            if (_phase == UiLogoPhase.Hidden || _phase == UiLogoPhase.Outro || _phase == UiLogoPhase.FadeOut)
            {
                return;
            }

            float alpha = Alpha(now);
            float spec = SpecTime(now);
            if (_reduced)
            {
                StartFade(now, spec, SkipFadeSeconds, alpha);
                return;
            }

            _frozenSpec = spec;
            _phase = UiLogoPhase.Outro;
            _phaseStart = now;
        }

        /// <summary>A skip: the canonical pose, faded out over <see cref="SkipFadeSeconds"/>. Ignored when hidden or leaving.</summary>
        public void Skip(float now)
        {
            Tick(now);
            if (_phase == UiLogoPhase.Hidden || _phase == UiLogoPhase.FadeOut || _phase == UiLogoPhase.Outro)
            {
                return;
            }

            StartFade(now, CanonicalSeconds, SkipFadeSeconds, 1f);
        }

        /// <summary>Gone at once (a new load, an instant open, the error over the boot).</summary>
        public void Hide()
        {
            _phase = UiLogoPhase.Hidden;
        }

        /// <summary>True while the logo draws anything.</summary>
        public bool Visible(float now)
        {
            Tick(now);
            return _phase != UiLogoPhase.Hidden;
        }

        /// <summary>The whole logo's opacity (the root's CanvasGroup).</summary>
        public float Alpha(float now)
        {
            switch (_phase)
            {
                case UiLogoPhase.Hidden:
                    return 0f;
                case UiLogoPhase.Static:
                    return Smooth01(PhaseSeconds(now) / ReducedFadeInSeconds);
                case UiLogoPhase.FadeOut:
                    return _fadeFromAlpha * (1f - Smooth01(PhaseSeconds(now) / _fadeSeconds));
                case UiLogoPhase.Outro:
                    return 1f - Smooth01((PhaseSeconds(now) - OutroFadeFrom) / OutroFadeSeconds);
                default:
                    return 1f;
            }
        }

        // ================================================================ poses (pure functions of spec time)

        /// <summary>1-based spec frame (fractional) at spec time <paramref name="spec"/>.</summary>
        public static float Frame(float spec)
        {
            return spec * Fps + 1f;
        }

        /// <summary>
        /// R4 WP-105 (user note 6, C-00 11.10: the logo sits inside the loading ring, not above it) · the seal layer's top
        /// for a seal that lands <paramref name="landing"/> spec px below its canonical rest (y down): the fall (before the
        /// impact frame) is stretched so it still starts where the spec starts it (off the composition, SealY[0]) and lands
        /// at rest + landing on the impact frame; the bounce after it keeps the spec's own amplitude, offset by landing.
        /// landing 0 returns <paramref name="top"/> unchanged (the menu intro's canonical composition).
        /// </summary>
        public static float LandedTop(float top, float spec, float landing)
        {
            if (landing == 0f)
            {
                return top;
            }

            if (spec < ImpactSeconds)
            {
                float start = SealY[0];
                return start + (top - start) * (SealRestTop + landing - start) / (SealRestTop - start);
            }

            return top + landing;
        }

        /// <summary>
        /// The seal layer at spec time: visible from frame 39, its bottom (the pivot, spec px from the canvas top) and its
        /// scale, linear between the spec's per-frame samples; rest after frame 54.
        /// </summary>
        public static bool SealPose(float spec, out float bottom, out float sx, out float sy, out float glow)
        {
            float f = Frame(spec);
            if (f < SealFirstFrame)
            {
                bottom = SealY[0] + SealLayerSize;
                sx = sy = 1f;
                glow = 0f;
                return false;
            }

            float u = Mathf.Min(f, SealLastFrame) - SealFirstFrame;
            int i = Mathf.Min((int)u, SealY.Length - 2);
            float k = Mathf.Clamp01(u - i);
            float y = Mathf.Lerp(SealY[i], SealY[i + 1], k);
            sx = Mathf.Lerp(SealSx[i], SealSx[i + 1], k);
            sy = Mathf.Lerp(SealSy[i], SealSy[i + 1], k);
            glow = Mathf.Lerp(SealGlow[i], SealGlow[i + 1], k);
            bottom = y + SealLayerSize * sy;
            return true;
        }

        /// <summary>Impact progress 0..1 over frames 47..54, or -1 outside.</summary>
        public static float ImpactT(float spec)
        {
            float u = (Frame(spec) - ImpactFrame) / ImpactFrames;
            return u < 0f || u > 1f ? -1f : u;
        }

        /// <summary>The closed ink ring: 0.72 sin(pi t), its size start -> end over the impact.</summary>
        public static float RingAlpha(float spec, out Vector2 size)
        {
            float t = ImpactT(spec);
            size = Vector2.Lerp(RingSizeStart, RingSizeEnd, Mathf.Clamp01(t));
            return t < 0f ? 0f : RingPeakAlpha * Mathf.Sin(Mathf.PI * t);
        }

        /// <summary>The ink blot: 0.35 sin(pi t)^0.7, scale 0.58 + 0.52 smoothstep(t).</summary>
        public static float BlotAlpha(float spec, out float scale)
        {
            float t = ImpactT(spec);
            scale = 0.58f + 0.52f * Smooth01(Mathf.Clamp01(t));
            return t < 0f ? 0f : BlotPeakAlpha * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.7f);
        }

        /// <summary>Camera shake on frames 48..50 (spec px, y down); zero elsewhere.</summary>
        public static Vector2 Shake(float spec)
        {
            int f = Mathf.FloorToInt(Frame(spec) + 1e-4f) - ShakeFirstFrame;
            return f >= 0 && f < ShakeX.Length ? new Vector2(ShakeX[f], ShakeY[f]) : Vector2.zero;
        }

        /// <summary>The loop's seam envelope sin²(pi t / 4): 0 at both ends, so the loop and the intro's last frame meet.</summary>
        public static float LoopEnvelope(float loopTime)
        {
            float s = Mathf.Sin(Mathf.PI * loopTime / LoopSeconds);
            return s * s;
        }

        public static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
