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
        /// <summary>
        /// Sheen over a surface (area, the Fx Sheen band): the most Cream100 a field may add under TextMuted on
        /// SurfacePanel at 4.5:1. 12b · D1 (AD 8a carried MUST): re-measured with the pinned linear composite
        /// (UiContrast.Composite, the GPU's blend) the old 0.05 gave 3.25:1 (0.06 → 3.01, 0.10 → 2.33; the sRGB
        /// estimate had read 0.06 → 4.56) - the envelope is 0.014.
        /// </summary>
        public const float SheenFieldAlphaMax = 0.014f;

        /// <summary>The 1 px top rim is an edge, outside the text envelope (text ≥ 8 px from it; 2.29:1 on SurfacePanel in linear light, a Decorative cue).</summary>
        public const float SheenRimAlpha = 0.10f;

        public const float WashiAlphaMin = 0.04f;

        /// <summary>
        /// 12b · D1: washi under TextSecondary on SurfacePanel, the tile's densest fibre (0.73 of the layer α) counted:
        /// 0.08 measured 4.36:1 in linear light, 0.07 keeps 4.5:1 (0.0735 is the edge). TextMuted never stands on washi
        /// (3.05-3.88:1 across the band), as it never stands on SurfaceRaised.
        /// </summary>
        public const float WashiAlphaMax = 0.07f;

        /// <summary>Kinpaku / ginpaku crackle tile under the gold chip and the Special ring (and ceremony for ginpaku).</summary>
        public const float LeafTileAlpha = 0.35f;

        /// <summary>A leaf glint may add at most this much brightness.</summary>
        public const float GlintMaxBrightness = 0.25f;

        /// <summary>Glint: 600 ms, at least 1 s apart per element, ≤ 2 per frame, × FlashScale.</summary>
        public const float GlintMinIntervalSeconds = 1f;
        public const int GlintMaxPerFrame = 2;

        // ------------------------------------------------------------------------------------------------------------------
        // WP-098A (C-00 08.10) · the O1 icon material's AD numbers: AP-034 v002.1 row d, the AD default (AP034-v002-AD §2,
        // ap034_icons_params.json rows.d, AD ACCEPT, MUST 3/3). They replace B5b's D3 / D7 (glow 0.017, cold lob #293B46
        // 0.083 at 0.667 AP: "a retires", AD §1-2). Every value is a LINEAR-blend value - AP-034's Twin composites in linear
        // light, as this pipeline does. s = the size ramp below, on the glyph's physical px. Exported to UiTokens.hlsl.
        // ------------------------------------------------------------------------------------------------------------------

        /// <summary>The size ramp s = sat((P − <see cref="IconRampStartPx"/>) / <see cref="IconRampSpanPx"/>): 0 at 24 physical px, 1 from 40 (Mac UI 1.2: 24 ref = 45 px).</summary>
        public const float IconRampStartPx = 24f;

        public const float IconRampSpanPx = 16f;

        /// <summary>Hover / selected glow α at its peak, at s = 0; + <see cref="IconGlowAlphaRamp"/> · s (0.07 at 24 px, 0.12 from 40 px). At rest 0; × FlashScale.</summary>
        public const float IconGlowAlpha = 0.07f;

        public const float IconGlowAlphaRamp = 0.05f;

        /// <summary>The glow's reach from the edge: 5 G1 px = 6.15 AP, the cell pad's ceiling (direction §1.2: E = 0 up to ~5 G1 px).</summary>
        public const float IconGlowRadiusAp = 5f * 64f / 52f;

        /// <summary>
        /// The lob: ink (Sumi900 = SurfaceInset, the kit's carrier language - C7 halo, C8 bed), not AP-028's #293B46, which is
        /// lighter than the #161D21 ground and lifted instead of shading (AD §1-3; #293B46 stays O1's cold scene token).
        /// </summary>
        public static readonly Color IconLobColour = new Color32(0x12, 0x10, 0x0F, 0xFF);

        public const float IconLobAlpha = 0.45f;

        /// <summary>The lob's offset, AP: (1.6, −2.4) - down-right (y up); 1 px / 1.5 px at 40 px.</summary>
        public static readonly Vector2 IconLobOffsetAp = new Vector2(1.6f, -2.4f);

        /// <summary>The lob's soft edge, ±AP around the shifted glyph's edge (smoothstep(−2.4, 2.4, sd)).</summary>
        public const float IconLobSoftAp = 2.4f;

        /// <summary>The ink darkening's weights: cavity 0.20 · (0.5 + 0.5 s), grain 0.06 · s, dry tail 0.05 · s, bevel shadow 0.10 · (0.5 + 0.5 s).</summary>
        public const float IconCavity = 0.2f;

        public const float IconGrain = 0.06f;
        public const float IconDryTail = 0.05f;
        public const float IconBevelShadow = 0.1f;

        /// <summary>
        /// The darkening's ceiling 0.06 + 0.06 · s: the lit glyph never falls under tint × (1 − ceiling) - 0.94 at 24 px, 0.88
        /// from 40 px (AD §5-2; the B3 ≥ 7:1 guard, AP-034 measured ≥ 10.73:1, body / token ≥ 0.945 / 0.883).
        /// </summary>
        public const float IconCeiling = 0.06f;

        public const float IconCeilingRamp = 0.06f;

        /// <summary>
        /// WP-098A (HAREKET-MENU §3 "Kalkış", C-00 08.10) · the lifted icon's lob: Elev1 → Elev2 - the offset × 2 ((2, −3) → (4, −6))
        /// and the alpha × Elev2 / Elev1 shadow alpha (0.55 / 0.35), at full lift (uv2.w = 1). Exported as DD_UI_ICON_LIFT.
        /// </summary>
        public const float IconLiftOffsetGain = 2f;

        public const float IconLiftLobGain = 0.55f / 0.35f;
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

        /// <summary>WP-094 (HAREKET-HUD §6 O1 exceptions): the DeniedNudge's length, a kit token - the preset no longer writes 0.16 itself.</summary>
        public const float DeniedNudgeSeconds = 0.16f;

        // ---- WP-094 HUD v4 juice (K/wp-094/HAREKET-HUD.md §6, interaction-designer 07.10; C-00 07.10). Lengths stay on the
        //      six steps (no off-scale duration); the px / scale / rate values below are the catalogue's, in one place.

        /// <summary>G1: the coin glyph's punch peak (a non-text glyph; the numeral keeps <see cref="TextPopMaxScale"/>).</summary>
        public const float PunchPeakGlyph = 1.18f;

        /// <summary>G1: at most one gold punch per this many seconds; arrivals in between gather into the next one.</summary>
        public const float PunchMinInterval = 0.12f;

        /// <summary>
        /// G3: gains inside this window read as one "+N". WP-095 feel contract §4.9 (wins over HAREKET-HUD's Emphasis 400): 0.35 s.
        /// </summary>
        public const float PlusMergeWindow = 0.35f;

        /// <summary>A1: shot accents (spark, casing, lean) at most 10 a second - a faster weapon accents every second shot.</summary>
        public const float ShotAccentMinInterval = 0.1f;

        /// <summary>A1: the ammo numeral pops only on a single-fire weapon whose shots are at least this far apart.</summary>
        public const float SlowShotInterval = 0.25f;

        /// <summary>A6: an empty trigger shakes the ring at most once per this many seconds.</summary>
        public const float DryFireMinInterval = 0.25f;

        /// <summary>A1: the ladder leans this far outward while the weapon fires.</summary>
        public const float LadderLeanPx = 2f;

        /// <summary>A4: the refilled magazine slides up this far into place.</summary>
        public const float MagSlidePx = 10f;

        /// <summary>A8: the classic cluster steps down this far while a Special holds it.</summary>
        public const float SuspendSlidePx = 6f;

        /// <summary>H1: the broken-off health chunk falls this far, drifts this far along the fill and turns this much.</summary>
        public const float ChunkDropPx = 14f;
        public const float ChunkDriftPx = 6f;
        public const float ChunkSpinDeg = 8f;

        /// <summary>H1: a lost range narrower than this (px on the stroke) leaves only the trail - no chunk.</summary>
        public const float ChunkMinPx = 4f;

        /// <summary>H3: a hit of at least <see cref="HitKnockMinFraction"/> of max health knocks the health brush this far back (≤ 1 per Fast).</summary>
        public const float HitKnockPx = 3f;
        public const float HitKnockMinFraction = 0.05f;

        /// <summary>H4: one step of at least this share of max health glows (≤ 1 a second), at this peak alpha × FlashScale.</summary>
        public const float HealGlowMinFraction = 0.05f;
        public const float HealGlowAlpha = 0.45f;

        /// <summary>H4: the wet band at the fill's tip while health regenerates (px; data, no loop).</summary>
        public const float HealWetBandPx = 6f;

        /// <summary>H5: the heart's two beats (lub at once, dub <see cref="BeatDubDelay"/> later), × <see cref="BeatCriticalGain"/> under <see cref="BeatCriticalFraction"/>.</summary>
        public const float BeatLub = 1.10f;
        public const float BeatDub = 1.05f;
        public const float BeatCriticalGain = 1.3f;
        public const float BeatCriticalFraction = 0.15f;

        /// <summary>
        /// H5: the dub follows the lub by this much. 08.10 (K/wp-095/HIS-SOZLESMESI.md §4.7 - the heartbeat rumble's double beat
        /// is "+0,18 s"; one clock, so the HUD's beat lands with it): 0.18 s (HAREKET-HUD had 0.28).
        /// </summary>
        public const float BeatDubDelay = 0.18f;

        /// <summary>H5: the stroke rim's alpha between beats and at a beat (the difference × FlashScale).</summary>
        public const float BeatRimRest = 0.35f;
        public const float BeatRimPeak = 0.75f;

        /// <summary>R2: the round's brush reveal waits this long after the banner starts (= Emphasis) - the banner reads first.</summary>
        public const float SealHandoffDelay = 0.4f;

        // ------------------------------------------------------------------------------------------------------------------
        // WP-098A (C-00 08.10; DEC-114, ikon v2 B5c; HAREKET-MENU §3 / §7) · the "Icon" duration family. AP-028 v002 motion.json's
        // own lengths, registered as a family (O1: tokened, not ad hoc) beside the six UiDuration steps; every icon track's
        // transform part ends inside Emphasis (400), the longer tails are light only. DEC-114 approved: IconNotify, IconIdle,
        // IconIdleDelay and UiEase.Sweep; the rest are the family's record (HAREKET-MENU §9 S7).
        // ------------------------------------------------------------------------------------------------------------------

        /// <summary>pressed: 120 ms Sweep, the contact (0.94, +0.5 px24) at <see cref="IconPressContact"/>.</summary>
        public const float IconPress = 0.12f;

        public const float IconPressContact = 0.05f;

        /// <summary>hover / focus (lifted 0 → 1): 180 ms SettleLong to 1.025, −0.25 px24, sheen 1, glow 0.04.</summary>
        public const float IconLift = 0.18f;

        /// <summary>disabled: 160 ms Sweep, flat shading at t0.</summary>
        public const float IconDisable = 0.16f;

        /// <summary>selected (a user's choice): 420 ms SettleLong; the stroke front 0.75 → 1, the transform done at <see cref="IconChooseTransform"/>.</summary>
        public const float IconChoose = 0.42f;

        public const float IconChooseTransform = 0.26f;

        /// <summary>unlock: 560 ms SettleLong, the glyph swap at <see cref="IconUnlockSwap"/>.</summary>
        public const float IconUnlock = 0.56f;

        public const float IconUnlockSwap = 0.13f;

        /// <summary>DEC-114 · notify: 600 ms Sweep (the draft's 640 cut where the last 40 ms move sheen ≤ 0.005).</summary>
        public const float IconNotify = 0.6f;

        /// <summary>DEC-114 · the idle sweep: one 3.6 s sheen pass (≈ 0.28 Hz) on the one focused icon - the single exception to MaxDecorativeSeconds.</summary>
        public const float IconIdle = 3.6f;

        /// <summary>DEC-114 · the idle sweep starts this long after focus arrives ([PrototypeOnly]: the number comes from play-testing).</summary>
        public const float IconIdleDelay = 2.4f;

        /// <summary>settle (an exit event): 120 ms Sweep from the pose on screen to the composite rest; the sheen jumps at t0.</summary>
        public const float IconSettle = 0.12f;
    }

    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU §7, the menu v5 motion language shared with S-04's windows) · the menu's motion numbers
    /// beyond the duration steps. Every one is the motion lead's; changing one is a direction change, not a tweak.
    /// </summary>
    public static class UiMenuMotionTokens
    {
        /// <summary>M8 · a kit text control on lift: −2 px, a whole pixel (no scale - the kit's rule).</summary>
        public const float HoverLiftPx = 2f;

        /// <summary>M7 · a focus held shorter than Instant (80 ms) starts no ink wave (fast travel stays quiet).</summary>
        public const float HoverDwellGate = 0.08f;

        /// <summary>M7 / M8 · the cream ink wave behind a lifted control: α ≤ 0.06 (under the 0.08 hover wash), Ø 56 → 96 px.</summary>
        public const float InkWaveAlpha = 0.06f;

        public const float InkWaveFrom = 56f;
        public const float InkWaveTo = 96f;

        /// <summary>M7 · one wave per control per Emphasis (400 ms) at most; at most three hover traces at once.</summary>
        public const float InkWaveMinInterval = 0.4f;

        public const int MaxHoverTraces = 3;

        /// <summary>M10 · the click splash: Ø 160 cream α 0.14; on PLAY Ø 240 vermilion α 0.5 (a large flash: through the FlashGate).</summary>
        public const float SplashAlpha = 0.14f;

        public const float SplashAlphaHero = 0.5f;
        public const float SplashSize = 160f;
        public const float SplashSizeHero = 240f;

        /// <summary>M10 · the splash's droplets: six, 160 px/s, 400 px/s² gravity, over Base 220.</summary>
        public const int SplashDroplets = 6;

        public const float SplashDropletSpeed = 160f;
        public const float SplashDropletGravity = 400f;
        public const float SplashDropletSize = 8f;

        /// <summary>M10 · one splash per control per Screen (280 ms) at most; two in the pool.</summary>
        public const float SplashMinInterval = 0.28f;

        public const int SplashPool = 2;

        /// <summary>M2 · the AP-035 logo's whole intro at most (the boot seal's 1.6 s hold, the one written exception to MaxDecorativeSeconds).</summary>
        public const float LogoIntroMax = 1.6f;

        /// <summary>M1–M5 · the menu's arrival in total, aligned with the cinematic's 1.2 s opening hold.</summary>
        public const float MenuIntroMax = 1.2f;

        /// <summary>M2 / M3 / M4 · the intro's beats after the reveal (M1): the title, the rail's stagger, the level readout.</summary>
        public const float TitleAt = 0.12f;

        public const float TitleRuleLag = 0.08f;
        public const float RailAt = 0.4f;
        public const float ProgressAt = 0.6f;

        /// <summary>M5 · PLAY's sub-line follows its word by this much.</summary>
        public const float SublineLag = 0.06f;

        /// <summary>M14 / M1 · the full-screen brush: the screen × 1.25 at least (the tilt's corners covered), turned −8°.</summary>
        public const float BrushWipeOverscan = 1.25f;

        public const float BrushWipeTiltDeg = -8f;

        /// <summary>M16 (SHOULD) · the world camera's parallax: ±0.6° yaw, ±0.4° pitch toward the look target.</summary>
        public const float CameraParallaxDeg = 0.6f;

        public const float CameraParallaxPitchDeg = 0.4f;

        /// <summary>M17 · the ambient motes: 16 ash (4–8 px, α ≤ 0.22, 14–24 px/s), 6 leaves (12–18 px, α ≤ 0.35), x ∈ [0.32 W, W].</summary>
        public const int AmbientAsh = 16;

        public const int AmbientLeaf = 6;
        public const float AshSize = 6f;
        public const float AshAlpha = 0.22f;
        public const float AshSpeed = 19f;
        public const float AshPeriod = 14f;
        public const float LeafSize = 15f;
        public const float LeafAlpha = 0.35f;
        public const float LeafSpeed = 24f;
        public const float LeafPeriod = 11f;
        public const float AmbientRegionStart = 0.32f;

        /// <summary>M12 · a second pane change within this window skips the row stagger (fast travel).</summary>
        public const float QuickChangeWindow = 1f;

        /// <summary>M11 / §5 · the world freezes this long after a pane opens (Screen 280): the freeze frame falls outside the pane's motion.</summary>
        public const float FreezeDelay = 0.28f;

        /// <summary>M15 (SHOULD) · the logo glow's breath: 4.0 s, α 0.85 ↔ 1.0 (≤ 0.25 Hz).</summary>
        public const float BreathPeriod = 4f;

        public const float BreathAlpha = 0.075f;
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
        Brush = 5,

        /// <summary>DEC-114 (WP-098A, C-00 08.10) · in_out (.4, 0, .2, 1): a light passing over a glyph - the icon tracks' press / settle / notify / idle.</summary>
        Sweep = 6
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
                case UiEase.Sweep: return new Vector4(0.40f, 0.00f, 0.20f, 1.00f);
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

        /// <summary>The standard easeOutBack overshoot (Penner's 1.70158): about 10 % past the end at t ≈ 0.6.</summary>
        public const float BackOvershoot = 1.70158f;

        /// <summary>
        /// WP-094 (K/wp-095/HIS-SOZLESMESI.md, S-02's feel contract): easeOutBack - 1 + (s + 1)(t − 1)³ + s(t − 1)² - for the
        /// contract's pops only (UiMotion.PopSeconds). It overshoots by design, so it is NOT a <see cref="UiEase"/> member (the
        /// named curves stay monotone and inside [0, 1] - UiTokensV3Tests). Allocation-free.
        /// </summary>
        public static float OutBack(float t, float overshoot = BackOvershoot)
        {
            t = Mathf.Clamp01(t);
            float u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        /// <summary>easeInQuad (t²): the damage-direction exit (HIS-SOZLESMESI §4.6).</summary>
        public static float InQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
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
