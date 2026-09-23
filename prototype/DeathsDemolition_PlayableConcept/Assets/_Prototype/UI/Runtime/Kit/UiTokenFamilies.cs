using UnityEngine;

namespace DD.Prototype.UI
{
    // WP-068 token v3 (UI v3 direction §2). Colour stays in UiTokens.cs; this file holds the
    // other families so every screen, the motion core (slice 2), the Fx shader (S-07, via
    // the exported UiTokens.hlsl) and the style guide read the same numbers. Values are
    // the council's; changing one is a direction change, not a tweak.

    /// <summary>Three elevations (AD triad). Elev0 = flat HUD / wells, Elev1 = page panel, Elev2 = floating (hover, modal, tooltip, curtain card).</summary>
    public enum UiElevation
    {
        Elev0 = 0,
        Elev1 = 1,
        Elev2 = 2
    }

    public static class UiElevationSpec
    {
        /// <summary>Soft shadow radius in reference px (one 9-slice Gaussian sprite; uGUI Shadow/Outline are banned).</summary>
        public static float ShadowRadius(UiElevation e)
        {
            switch (e)
            {
                case UiElevation.Elev1: return 8f;
                case UiElevation.Elev2: return 24f;
                default: return 0f;
            }
        }

        /// <summary>Shadow alpha over <see cref="UiColor.ShadowKey"/>. Elev0 casts none - a halo under a HUD panel darkens the approach band (DEC-077).</summary>
        public static float ShadowAlpha(UiElevation e)
        {
            switch (e)
            {
                case UiElevation.Elev1: return 0.35f;
                case UiElevation.Elev2: return 0.55f;
                default: return 0f;
            }
        }

        /// <summary>Elev2 sits over a scrim.</summary>
        public static bool UsesScrim(UiElevation e)
        {
            return e == UiElevation.Elev2;
        }
    }

    /// <summary>The four materials (sumi, urushi, kinpaku, washi) plus ginpaku for ceremony.</summary>
    public enum UiMaterial
    {
        Ink = 0,
        Lacquer = 1,
        Washi = 2,
        GoldLeaf = 3,
        SilverLeaf = 4
    }

    public static class UiMaterialSpec
    {
        /// <summary>Sheen over a surface (area): ≤ 0.05, or TextMuted drops under 4.5:1 (measured: 0.06 → 4.56, 0.10 → 4.00).</summary>
        public const float SheenFieldAlphaMax = 0.05f;

        /// <summary>The 1 px top rim is an edge, outside the text envelope.</summary>
        public const float SheenRimAlpha = 0.10f;

        public const float WashiAlphaMin = 0.04f;
        public const float WashiAlphaMax = 0.08f;

        /// <summary>Kinpaku / ginpaku crackle tile under the gold chip and the Special ring (and ceremony for ginpaku).</summary>
        public const float LeafTileAlpha = 0.35f;

        /// <summary>A leaf glint may add at most this much brightness.</summary>
        public const float GlintMaxBrightness = 0.25f;

        /// <summary>Glint: 600 ms, at least 1 s apart per element, ≤ 2 per frame, × FlashScale.</summary>
        public const float GlintMinIntervalSeconds = 1f;
        public const int GlintMaxPerFrame = 2;
    }

    public static class UiRadius
    {
        public const int Small = 4;
        public const int Chip = 8;
        public const int Control = 10;
        public const int Card = 14;
        public const int Pill = 999;
    }

    public static class UiStroke
    {
        public const int Hairline = 1;
        public const int Rim = 2;
        public const int Focus = 3;
    }

    /// <summary>Component sizes in reference px. Text-bearing ones are floors: the real height is face metric × TextScale (UiFactory.ScaledBand).</summary>
    public static class UiSize
    {
        public const int RowChip = 22;
        public const int CardChip = 28;
        public const int Toggle = 40;
        public const int HudChip = 40;
        public const int Compact = 44;
        public const int Target = 48;
        public const int Cta = 56;

        /// <summary>Hit-target floor (icon button 44², slider handle hit area 44).</summary>
        public const int MinTarget = 44;
    }

    /// <summary>Page grid (direction §2.3, §2.7).</summary>
    public static class UiGrid
    {
        public const int Columns = 12;
        public const int Gutter = 24;
        public const int PageMargin = 96;

        /// <summary>
        /// The narrowest page any layout must fit: CanvasScaler Expand at UI scale 1.2
        /// gives a 1600-wide canvas (1920 / 1.2), minus two page margins.
        /// </summary>
        public const int BindingPageWidth = 1408;

        /// <summary>Column width for a page width: the page minus 11 gutters (264), over 12 columns (95.3 px at 1408, 122 px at 1728).</summary>
        public static float ColumnWidth(float pageWidth)
        {
            return (pageWidth - 264f) / Columns;
        }
    }

    /// <summary>Six durations (ms). Exit = one step shorter than its entry; nothing decorative is longer than Reveal.</summary>
    public enum UiDuration
    {
        Instant = 80,
        Fast = 140,
        Base = 220,
        Screen = 280,
        Emphasis = 400,
        Reveal = 600
    }

    /// <summary>Every motion preset declares one; the motion core applies the Reduced Motion / flash policy by class, not at call sites.</summary>
    public enum UiMotionClass
    {
        /// <summary>Translate, scale, stagger, parallax, drift, glint, curtain slide. RM: end state at t = 0; fades 0 ms.</summary>
        Decor = 0,

        /// <summary>Ghost, trail, count-up, "+N". RM: final value; a ghost shows statically 350 ms, then goes.</summary>
        Delta = 1,

        /// <summary>Reload bar, Special ring, countdown, recovery track, hold fill, projection-bound markers. RM NEVER changes it (Linear).</summary>
        Data = 2,

        /// <summary>The two existing pulses (final-seconds band, Results NEW). RM: held at full alpha.</summary>
        Attention = 3,

        /// <summary>Damage edge, glint, spark, veil. Plays; peak × FlashScale and through the FlashGate.</summary>
        Flash = 4
    }

    public static class UiMotionTokens
    {
        public static float Seconds(UiDuration d)
        {
            return (int)d / 1000f;
        }

        /// <summary>Row stagger (≤ 6 items; the 7th+ share the 6th's delay), card stagger, beat (Results / cue spacing).</summary>
        public const float StaggerRow = 0.04f;
        public const int StaggerRowMaxItems = 6;
        public const float StaggerCard = 0.06f;
        public const float StaggerBeat = 0.12f;

        public const float TrailHold = 0.35f;
        public const float TooltipDelayPointer = 0.35f;
        public const float TooltipDelayFocus = 0.5f;

        /// <summary>Whole banner envelope (in + hold + out).</summary>
        public const float BannerHold = 2.4f;

        /// <summary>One hold-to-confirm token (RESET ALL BINDINGS; REROLL if the user says so - U5).</summary>
        public const float HoldConfirm = 0.5f;

        public const float MaxDecorativeSeconds = 0.6f;
        public const float MaxLoopHz = 1.1f;

        /// <summary>A text-bearing element never pops past this (checked at TextScale 1.25).</summary>
        public const float TextPopMaxScale = 1.06f;

        /// <summary>Stamp: 1.12 → 1.00 InCubic over 90 ms, then a 40 ms hold (130 total). Never below 1.0.</summary>
        public const float StampPeakScale = 1.12f;
        public const float StampInSeconds = 0.09f;
        public const float StampHoldSeconds = 0.04f;

        /// <summary>Follow half-lives for value-bound motion (reticle spread, parallax, ghost).</summary>
        public const float FollowShort = 0.05f;
        public const float FollowMid = 0.12f;
        public const float FollowLong = 0.20f;

        /// <summary>FlashGate (WCAG 2.3.1): above this area (px² at 1080p) ≤ 3 onsets per second globally, ≤ 2 per element; excess onsets merge.</summary>
        public const float FlashGateAreaPx = 43000f;
        public const int FlashGateOnsetsPerSecond = 3;
        public const int FlashGateOnsetsPerElement = 2;

        /// <summary>DeniedNudge: ±4 px, 2 cycles, 160 ms; off under RM; never in the HUD play band.</summary>
        public const float DeniedNudgePx = 4f;
    }

    /// <summary>
    /// The named curves (direction §2.5). No overshoot anywhere: every curve maps [0,1] to
    /// [0,1] monotonically. Stamp is a two-phase scale, not a curve (see <see cref="StampScale"/>).
    /// The same control points are exported to UiTokens.hlsl for the Fx shader.
    /// </summary>
    public enum UiEase
    {
        Linear = 0,
        Settle = 1,
        SettleLong = 2,
        Exit = 3,
        Move = 4,
        Brush = 5
    }

    public static class UiEaseCurve
    {
        /// <summary>cubic-bezier control points (x1, y1, x2, y2) per <see cref="UiEase"/>.</summary>
        public static Vector4 ControlPoints(UiEase ease)
        {
            switch (ease)
            {
                case UiEase.Settle: return new Vector4(0.33f, 1.00f, 0.68f, 1.00f);
                case UiEase.SettleLong: return new Vector4(0.16f, 1.00f, 0.30f, 1.00f);
                case UiEase.Exit: return new Vector4(0.32f, 0.00f, 0.67f, 0.00f);
                case UiEase.Move: return new Vector4(0.65f, 0.00f, 0.35f, 1.00f);
                case UiEase.Brush: return new Vector4(0.25f, 0.10f, 0.25f, 1.00f);
                default: return new Vector4(0f, 0f, 1f, 1f);
            }
        }

        /// <summary>Eased progress for t in [0,1]. Allocation-free.</summary>
        public static float Evaluate(UiEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            if (ease == UiEase.Linear)
            {
                return t;
            }

            Vector4 p = ControlPoints(ease);
            return CubicBezier(p.x, p.y, p.z, p.w, t);
        }

        /// <summary>CSS cubic-bezier: solve x(u) = t for u (Newton, bisection fallback), return y(u).</summary>
        public static float CubicBezier(float x1, float y1, float x2, float y2, float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            float u = t;
            for (int i = 0; i < 8; i++)
            {
                float x = Bezier(x1, x2, u) - t;
                if (Mathf.Abs(x) < 1e-5f)
                {
                    return Bezier(y1, y2, u);
                }

                float dx = BezierDerivative(x1, x2, u);
                if (Mathf.Abs(dx) < 1e-6f)
                {
                    break;
                }

                u -= x / dx;
            }

            float lo = 0f, hi = 1f;
            u = t;
            for (int i = 0; i < 24; i++)
            {
                float x = Bezier(x1, x2, u);
                if (Mathf.Abs(x - t) < 1e-5f)
                {
                    break;
                }

                if (x < t) lo = u; else hi = u;
                u = (lo + hi) * 0.5f;
            }

            return Bezier(y1, y2, u);
        }

        private static float Bezier(float p1, float p2, float u)
        {
            float v = 1f - u;
            return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
        }

        private static float BezierDerivative(float p1, float p2, float u)
        {
            float v = 1f - u;
            return 3f * v * v * p1 + 6f * v * u * (p2 - p1) + 3f * u * u * (1f - p2);
        }

        /// <summary>Stamp scale at time since contact start: 1.12 → 1.00 InCubic over 90 ms, then 1.00. Never below 1.</summary>
        public static float StampScale(float seconds)
        {
            if (seconds <= 0f) return UiMotionTokens.StampPeakScale;
            if (seconds >= UiMotionTokens.StampInSeconds) return 1f;
            float k = seconds / UiMotionTokens.StampInSeconds;
            float eased = k * k * k;
            return Mathf.Lerp(UiMotionTokens.StampPeakScale, 1f, eased);
        }

        /// <summary>Frame-rate independent follow with a half-life (value-bound motion).</summary>
        public static float Follow(float current, float target, float halfLife, float dt)
        {
            if (halfLife <= 0f)
            {
                return target;
            }

            return target + (current - target) * Mathf.Pow(0.5f, dt / halfLife);
        }
    }

    /// <summary>Sorting layers (direction §2.6). The WP-022 SortingHud/Screen/Pause/Dialog constants stay as aliases.</summary>
    public static class UiLayer
    {
        public const int WorldNumber = 40;
        public const int FrozenFrame = 90;
        public const int WorldMarker = 95;
        public const int Hud = 100;
        public const int HudFeedback = 110;
        public const int HudBanner = 120;
        public const int HudRibbon = 130;
        public const int Screen = 200;
        public const int ScreenOverlay = 250;
        public const int Pause = 300;
        public const int Modal = 320;
        public const int Tooltip = 360;
        public const int Curtain = 400;
        public const int Debug = 1000;
    }
}
