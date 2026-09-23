using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Spacing scale. The enum VALUE is the pixel amount at the 1920x1080 reference
    /// resolution, so an off-scale number cannot be typed at a call site: every
    /// padding, gap and offset in this module is a member of this enum.
    ///
    /// 8pt base with a single 4pt half-step for optical corrections inside a control.
    /// </summary>
    public enum UiSpace
    {
        None = 0,
        XS = 4,
        S = 8,
        M = 12,
        L = 16,
        XL = 24,
        XXL = 32,
        XXXL = 48,
        Section = 64,
        Page = 96
    }

    /// <summary>
    /// Type scale. Minor-third-ish ratio, every step snapped to an even pixel so text
    /// baselines keep landing on the 4pt sub-grid.
    ///
    /// Sizes are for the 1920x1080 reference; the CanvasScaler handles everything else.
    /// </summary>
    /// <summary>WP-022G: which colour set UiTokens.Get answers from.</summary>
    public enum UiPalette
    {
        Default = 0,
        ColorBlindSafe = 1
    }

    public enum UiType
    {
        /// <summary>56 bold. The game title, once per screen at most.</summary>
        Display,

        /// <summary>34 bold. Screen title.</summary>
        H1,

        /// <summary>24 bold. Card / section title.</summary>
        H2,

        /// <summary>20 bold. Sub-section.</summary>
        H3,

        /// <summary>18 regular. Default reading size.</summary>
        Body,

        /// <summary>18 bold.</summary>
        BodyStrong,

        /// <summary>15 bold, written upper case by the caller. Field labels, tags.</summary>
        Label,

        /// <summary>14 regular (WP-022C, AD SHOULD-3/6: 13 was under the DEC-077 comfort floor). Footnotes, hints, captions.</summary>
        Caption,

        /// <summary>44 bold. The one number a screen is about.</summary>
        NumericXL,

        /// <summary>32 bold. Primary HUD readouts.</summary>
        NumericL,

        /// <summary>22 bold. Secondary HUD readouts.</summary>
        NumericM
    }

    /// <summary>
    /// Semantic colour tokens. NOTHING in this module writes a literal colour: the only
    /// <c>new Color(...)</c> calls in Assets/_Prototype/UI live in this file, inside the
    /// primitive palette below. Screens ask for a role, never for a hex value.
    ///
    /// Three layers, in the order a token may depend on another:
    ///   primitive (Sumi900, Cream100, Vermilion600 ...)  - private, in this file
    ///   semantic  (SurfacePanel, TextPrimary, StateDanger ...) - this enum
    ///   component (button fills, bar fills) - also this enum, suffixed by its use
    /// </summary>
    public enum UiColor
    {
        Transparent,

        // -------------------------------------------------------------- surfaces
        /// <summary>Full-screen dimmer placed over live gameplay.</summary>
        ScrimOverlay,

        /// <summary>Screen background.</summary>
        SurfaceBase,

        /// <summary>Card / panel background on a screen.</summary>
        SurfacePanel,

        /// <summary>Panel that must read as lifted off the one behind it.</summary>
        SurfaceRaised,

        /// <summary>Recessed well: bar tracks, list backings.</summary>
        SurfaceInset,

        /// <summary>
        /// HUD panel backing. Deliberately near-opaque (alpha 0.92): a translucent HUD
        /// cannot guarantee a text contrast ratio, because the ratio would depend on
        /// whatever the camera happens to be pointing at.
        /// </summary>
        HudInk,

        // --------------------------------------------------------------- borders
        /// <summary>Decorative separator. Not load bearing, exempt from the 3:1 rule.</summary>
        BorderHairline,

        /// <summary>Boundary of an interactive control. Measured 4.04:1 on SurfaceBase.</summary>
        BorderStrong,

        /// <summary>Focus indicator. Always drawn with BorderFocusHalo behind it.</summary>
        BorderFocus,

        /// <summary>Dark halo drawn under the focus ring so the ring works on any backdrop.</summary>
        BorderFocusHalo,

        // ------------------------------------------------------------------ text
        /// <summary>Default text. 13.85:1 on SurfacePanel.</summary>
        TextPrimary,

        /// <summary>Supporting text. 7.62:1 on SurfacePanel.</summary>
        TextSecondary,

        /// <summary>De-emphasised text. 5.33:1 on SurfacePanel - still above 4.5.</summary>
        TextMuted,

        /// <summary>Text placed on AccentPrimary. 4.64:1 on that fill.</summary>
        TextOnAccent,

        /// <summary>Accent-coloured text. 5.91:1 on SurfacePanel.</summary>
        TextAccent,

        // ---------------------------------------------------------------- accent
        /// <summary>
        /// Primary interactive fill. 3.30:1 against SurfaceBase - and only 2.99:1 against
        /// SurfacePanel, i.e. BELOW the 3:1 boundary floor. An accent shape placed on a
        /// CARD must sit on a SurfaceInset well instead (3.53:1); the upgrade screen's
        /// action shelf exists for exactly that reason. Second rule this palette needs a
        /// person to remember, after TextMuted on SurfaceRaised.
        /// </summary>
        AccentPrimary,

        /// <summary>Pressed fill. Darker than normal, so label contrast only improves.</summary>
        AccentPrimaryPressed,

        /// <summary>Sakura pink. Highlight for new content.</summary>
        AccentSakura,

        /// <summary>Wisteria purple. Secondary highlight.</summary>
        AccentWisteria,

        /// <summary>Moss green. Health, positive quantities.</summary>
        AccentMoss,

        // ------------------------------------------------------------- damage feed
        /// <summary>
        /// Directional screen-edge flash raised when the PLAYER is hit. Deep vermilion,
        /// deliberately NOT StateDanger: the Special Weapon final-seconds warning already
        /// owns StateDanger on the top and bottom edges, and two red edge treatments that
        /// share a hue would be one red edge treatment as far as the player is concerned.
        /// The two are separated on four axes - hue, depth, which edges they can use, and
        /// above all time: this one is transient, that one is continuous.
        /// </summary>
        DamageEdge,

        // ----------------------------------------------------------------- state
        StateDanger,
        StateWarning,
        StateSuccess,
        StateGold,

        // ------------------------------------------------------------ components
        BarTrack,
        BarHealth,
        BarHealthLow,
        BarXp,
        BarReload,
        BarSpecial,
        // --------------------------------------------- WP-068 token v3 (append-only)
        ShadowKey,
        SheenRim,
        InkStroke,
        GoldLeaf,
        SilverLeaf
    }

    /// <summary>
    /// The single source of truth for every measurable visual constant in the prototype
    /// UI: colour, type size, radius, stroke, opacity, motion duration and touch target.
    ///
    /// CONTRAST BUDGET - measured, not asserted. Every ratio below was computed with the
    /// WCAG 2.1 relative-luminance formula (see <see cref="UiContrast"/>, which recomputes
    /// them at runtime so the numbers in these comments cannot silently rot):
    ///
    ///   TextPrimary   on SurfacePanel  13.85:1     TextPrimary on AccentPrimary  4.64:1
    ///   TextSecondary on SurfacePanel   7.62:1     TextAccent  on SurfacePanel   5.91:1
    ///   TextMuted     on SurfacePanel   5.33:1     StateGold   on SurfacePanel   7.40:1
    ///   StateDanger   on SurfacePanel   5.44:1     AccentSakura on SurfacePanel  7.75:1
    ///   TextPrimary   on HudInk over a WHITE world  13.50:1  (worst case composite)
    ///   BorderStrong  on SurfaceBase    4.04:1     AccentPrimary on SurfaceBase  3.30:1
    ///   BarHealth     on BarTrack       3.51:1     BarXp on BarTrack             9.17:1
    ///   BorderFocus   on AccentPrimary  3.86:1     BorderFocus on SurfacePanel  11.51:1
    ///
    /// Text targets 4.5:1, non-text interface boundaries target 3:1.
    ///
    /// NOTHING HERE IS A PRODUCT DECISION. These are prototype art-direction values that
    /// implement the request's stated palette (moss green, stone gray, dark wood, pale
    /// cream, pink and purple accents, limited vermilion). They occupy no pending
    /// planning decision line.
    /// </summary>
    public static class UiTokens
    {
        // ================================================================== primitives
        // The only literal colours in Assets/_Prototype/UI.

        private static readonly Color Sumi900 = Hex(0x12, 0x10, 0x0F); // ink / dark wood
        private static readonly Color Sumi800 = Hex(0x1A, 0x18, 0x16);
        private static readonly Color Sumi700 = Hex(0x23, 0x21, 0x20);
        private static readonly Color Stone600 = Hex(0x34, 0x32, 0x2F);
        private static readonly Color Stone500 = Hex(0x4A, 0x47, 0x44);
        private static readonly Color Stone350 = Hex(0x9A, 0x94, 0x8C);
        private static readonly Color Cream300 = Hex(0xB9, 0xB2, 0xA6);
        private static readonly Color Cream100 = Hex(0xF3, 0xEE, 0xE3); // pale cream

        private static readonly Color Vermilion700 = Hex(0x9E, 0x2A, 0x21);
        private static readonly Color Vermilion600 = Hex(0xC4, 0x36, 0x2B); // limited vermilion
        private static readonly Color Vermilion300 = Hex(0xE8, 0x80, 0x6F);

        private static readonly Color Sakura400 = Hex(0xE8, 0xA0, 0xB8);
        private static readonly Color Wisteria400 = Hex(0xA9, 0x9B, 0xD4);
        private static readonly Color Moss500 = Hex(0x59, 0x71, 0x4A);
        private static readonly Color Moss400 = Hex(0x74, 0x8F, 0x60);

        private static readonly Color Gold400 = Hex(0xD9, 0xA9, 0x3C);
        private static readonly Color Coral400 = Hex(0xEE, 0x70, 0x63);
        private static readonly Color Jade400 = Hex(0x8F, 0xC0, 0x79);

        // WP-022G colour-blind safe palette: the green family (Moss / Jade: health, earned,
        // UNLOCKED) moves to a blue that deuteranopes and protanopes separate from the
        // vermilion / coral danger family; low health keeps coral but the healthy bar is
        // blue, so the two ends of the bar differ in hue AND luminance. Blood (VFX) is
        // untouched by directive. Every pair is in the UiContrast table under both palettes.
        // WP-068 token v3 (direction §2.1): indigo-leaning blues replace the generic sky
        // pair (AD v006 SHOULD-3), and warning moves off the currency hex (C5).
        private static readonly Color Indigo400 = Hex(0x7F, 0xA6, 0xD9);
        private static readonly Color Indigo500 = Hex(0x5F, 0x8C, 0xC4);
        private static readonly Color Amber300 = Hex(0xF7, 0xC7, 0x66);
        private static readonly Color Focus400 = Hex(0xF2, 0xD9, 0x8C);

        private static Color Hex(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        // =================================================================== semantic

        /// <summary>Resolve a semantic token. This is the ONLY way screens obtain a colour.</summary>
        /// <summary>WP-022G: the active palette (UiSettings.ColorPalette); Get() answers from it.</summary>
        public static UiPalette Palette = UiPalette.Default;

        /// <summary>WP-022G: reading-text multiplier (Body / BodyStrong / Label / Caption), 1.0–1.25.</summary>
        public static float TextScale = 1f;

        /// <summary>WP-022G: 1, or UiSettings.ReducedFlashScale when "reduce flashes" is on; the HUD's damage-edge and final-seconds peaks multiply by it.</summary>
        public static float FlashScale = 1f;

        /// <summary>
        /// WP-068 user decision U4 as ONE line. true (council recommendation): warning is
        /// Amber300 #F7C766 in both palettes, ΔL* 10.8 from the currency gold it used to
        /// share (#D9A93C). false (AD MUST 2, the "no" path): warning keeps no hue at all -
        /// LOW / EMPTY / listening / SUSPENDED read as cream text with their word and shape -
        /// so warning text never stands on a panel in the currency hex either way.
        /// </summary>
        public static bool DistinctWarningHue = true;

        public static Color Get(UiColor token)
        {
            if (Palette == UiPalette.ColorBlindSafe)
            {
                switch (token)
                {
                    case UiColor.StateSuccess: return Indigo400;
                    case UiColor.BarHealth: return Indigo500;
                }
            }

            switch (token)
            {
                case UiColor.Transparent: return new Color(0f, 0f, 0f, 0f);

                case UiColor.ScrimOverlay: return WithAlpha(Sumi900, 0.86f);
                case UiColor.SurfaceBase: return Sumi800;
                case UiColor.SurfacePanel: return Sumi700;
                case UiColor.SurfaceRaised: return Stone600;
                case UiColor.SurfaceInset: return Sumi900;
                case UiColor.HudInk: return WithAlpha(Sumi900, 0.92f);

                case UiColor.BorderHairline: return WithAlpha(Stone500, 0.85f);
                case UiColor.BorderStrong: return WithAlpha(Cream100, 0.45f);
                case UiColor.BorderFocus: return Focus400;
                case UiColor.BorderFocusHalo: return Sumi900;

                case UiColor.TextPrimary: return Cream100;
                case UiColor.TextSecondary: return Cream300;
                case UiColor.TextMuted: return Stone350;
                case UiColor.TextOnAccent: return Cream100;
                case UiColor.TextAccent: return Vermilion300;

                case UiColor.AccentPrimary: return Vermilion600;
                case UiColor.AccentPrimaryPressed: return Vermilion700;
                case UiColor.AccentSakura: return Sakura400;
                case UiColor.AccentWisteria: return Wisteria400;
                case UiColor.AccentMoss: return Moss500;

                case UiColor.DamageEdge: return Vermilion600;

                case UiColor.StateDanger: return Coral400;
                case UiColor.StateWarning: return DistinctWarningHue ? Amber300 : Cream100;
                case UiColor.StateSuccess: return Jade400;
                case UiColor.StateGold: return Gold400;

                case UiColor.BarTrack: return Sumi900;
                case UiColor.BarHealth: return Moss400;
                case UiColor.BarHealthLow: return Coral400;
                case UiColor.BarXp: return Sakura400;
                case UiColor.BarReload: return Focus400;
                case UiColor.BarSpecial: return Gold400;

                // WP-068 token v3 (direction §2.1): depth and material colours.
                case UiColor.ShadowKey: return Sumi900; // alpha comes from UiElevationSpec
                case UiColor.SheenRim: return WithAlpha(Cream100, UiMaterialSpec.SheenRimAlpha);
                case UiColor.InkStroke: return WithAlpha(Sumi900, 0.90f);
                case UiColor.GoldLeaf: return Gold400;   // reserve: currency / price / Special only
                case UiColor.SilverLeaf: return Cream100; // ceremony: medals, NEW BEST, level

                default: return Cream100;
            }
        }

        // ================================================================ typography

        /// <summary>Font size in reference pixels.</summary>
        public static int FontSize(UiType type)
        {
            int size = BaseFontSize(type);
            switch (type)
            {
                case UiType.Body:
                case UiType.BodyStrong:
                case UiType.Label:
                case UiType.Caption:
                    return Mathf.RoundToInt(size * Mathf.Clamp(TextScale, 1f, 1.25f));
                default:
                    return size;
            }
        }

        private static int BaseFontSize(UiType type)
        {
            switch (type)
            {
                case UiType.Display: return 56;
                case UiType.H1: return 34;
                case UiType.H2: return 24;
                case UiType.H3: return 20;
                case UiType.Body: return 18;
                case UiType.BodyStrong: return 18;
                case UiType.Label: return 15;
                case UiType.Caption: return 14;
                case UiType.NumericXL: return 44;
                case UiType.NumericL: return 32;
                case UiType.NumericM: return 22;
                default: return 18;
            }
        }

        public static FontStyle Weight(UiType type)
        {
            switch (type)
            {
                case UiType.Body:
                case UiType.Caption:
                    return FontStyle.Normal;
                default:
                    return FontStyle.Bold;
            }
        }

        /// <summary>
        /// Line-height multiplier. Long-form text gets more air; numeric readouts are set
        /// tight so a two-line value block still reads as one object.
        /// </summary>
        public static float LineSpacing(UiType type)
        {
            switch (type)
            {
                case UiType.Body:
                case UiType.BodyStrong:
                case UiType.Caption:
                    return 1.35f;
                case UiType.NumericXL:
                case UiType.NumericL:
                case UiType.NumericM:
                case UiType.Display:
                    return 1f;
                default:
                    return 1.2f;
            }
        }

        /// <summary>
        /// Maximum comfortable measure for a paragraph, in reference pixels. Roughly
        /// 66 characters at Body size. Layouts clamp text blocks to this.
        /// </summary>
        public const int MaxMeasure = 720;

        // ==================================================================== shape

        public const int RadiusCard = 14;
        public const int RadiusControl = 10;
        public const int RadiusChip = 8;
        public const int RadiusPill = 999;

        public const int StrokeHairline = 1;
        public const int StrokeControl = 2;
        public const int StrokeFocusRing = 3;
        public const int StrokeFocusHalo = 3;

        /// <summary>Vermilion tick that marks a heading. The controlled "Japanese" detail.</summary>
        public const int HeadingTickWidth = 4;
        public const int HeadingTickHeight = 26;

        // ============================================================ touch targets

        /// <summary>Hard floor. Nothing interactive may be smaller than this.</summary>
        public const int TargetMin = 24;

        /// <summary>What every button in this module actually uses.</summary>
        public const int TargetPreferred = 48;

        /// <summary>Square control (stepper, close, tab). Still above 44.</summary>
        public const int TargetCompact = 44;

        /// <summary>Minimum clear space between two adjacent targets.</summary>
        public const UiSpace TargetGap = UiSpace.S;

        // =================================================================== motion
        // All durations are unscaled: menus and the pause screen run at timeScale 0.

        public const float MotionInstant = 0.08f;
        public const float MotionFast = 0.14f;
        public const float MotionBase = 0.22f;
        public const float MotionScreen = 0.28f;

        /// <summary>Period of the attention pulse used by warnings. Slow enough not to nag.</summary>
        public const float PulsePeriod = 0.9f;

        // ================================================================== opacity

        /// <summary>
        /// REQ-057. A classic weapon suspended by an active Special Weapon is drawn at
        /// this opacity - clearly de-emphasised, never blank. 0.62 is not a taste value:
        /// it is the lowest alpha at which TextPrimary composited over HudInk still
        /// measures 5.55:1, i.e. still above the 4.5:1 text floor.
        /// </summary>
        public const float OpacitySuspended = 0.62f;

        public const float OpacityDisabled = 0.55f;
        public const float OpacityScrimUnderDialog = 0.72f;

        /// <summary>
        /// Cream wash laid over a control while the pointer is on it. Low enough that it
        /// cannot move any audited pair below its threshold: it lightens both the fill
        /// and nothing else, and every button's label is measured against the fill it
        /// actually has, not against the wash.
        /// </summary>
        public const float OpacityHoverWash = 0.08f;

        /// <summary>Wash removed on press; the press is carried by the darker fill token instead.</summary>
        public const float OpacityPressWash = 0f;

        // ====================================================== player damage feed
        // REQ-045 / REQ-104. Every number below is PROTOTYPE-ONLY and carries its own
        // attribute; none of them occupies an open planning line. Two of them are
        // DERIVED rather than chosen, and the derivation is written next to the value.
        //
        // WHAT THE INDICATOR MAY NOT DO, and why nothing here can make it do so:
        //   * it cannot take input   - the Images are raycastTarget = false
        //   * it cannot stop time    - no timeScale is written anywhere in the UI module
        //   * it cannot cover play   - the deepest it ever reaches is DamageEdgeDepth,
        //                              i.e. the outer 8.9% of the reference height

        /// <summary>
        /// Peak alpha of a damage edge at the screen boundary, at full strength.
        ///
        /// DERIVED, not chosen. An alpha-blended overlay approaches the colour behind it,
        /// so the binding case is the darkest world the camera can show: measured against
        /// SurfaceInset standing in for a black world, DamageEdge composited at 0.85
        /// reaches 2.85:1, at 0.90 it reaches 3.06:1 and at 0.92 it reaches 3.15:1. 0.92
        /// is therefore the first value on the 0.02 grid with real headroom over the 3:1
        /// non-text floor. Both bounding worlds are audit cases in UiContrast.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Peak alpha of the directional damage edge. Derived from the 3:1 contrast floor " +
            "against the darkest world the camera can show, not chosen by eye.", true)]
        public const float DamageEdgePeakAlpha = 0.92f;

        /// <summary>
        /// How far a damage edge reaches inward, in reference pixels.
        ///
        /// 96 of 1080 is 8.9% of the height, i.e. HALF the reach of the Special Weapon
        /// final-seconds veil (which owns 18% of the top and bottom). That ordering is
        /// deliberate: the transient signal must never be the one that eats more of the
        /// play area than the continuous one.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Inward reach of the damage edge in reference pixels. Half the final-seconds veil.", true)]
        public const float DamageEdgeDepth = 150f; // AD v005 MUST-2: 14 % of 1080 - the ramp reaches 0 well outside the panels

        /// <summary>
        /// AD v005 MUST-2: a hit that leaves the player at low health raises its edges to
        /// this fraction of <see cref="DamageEdgePeakAlpha"/> (0.45 / 0.92) instead of to
        /// full strength, so the health / weapon panels never sit inside saturated red.
        /// </summary>
        public const float LowHealthEdgeStrength = 0.49f;

        /// <summary>
        /// Seconds a damage edge is held at full amplitude before it starts to fall.
        ///
        /// 0.06 s is between three and four frames at 60 fps and seven at 120: enough that
        /// a hit landing on the frame the player blinks is still on screen on the next one,
        /// short enough that it reads as an impact rather than as a state.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Hold time at peak before the damage edge decays. Roughly four frames at 60 fps.", true)]
        public const float DamageEdgeHoldSeconds = 0.06f;

        /// <summary>
        /// Seconds the damage edge takes to fall from its peak to nothing after the hold.
        /// Total on-screen life of one hit is Hold + Decay = 0.48 s.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Decay time of the damage edge after the hold. Total life of one hit is 0.48 s.", true)]
        public const float DamageEdgeDecaySeconds = 0.42f;

        /// <summary>
        /// Damage as a fraction of maximum health that produces a full-strength edge.
        /// Same 0.25 as PlayerDamageFeedback's camera bump, on purpose: the edge, the bump
        /// and the flinch are three views of one event and must not disagree about how
        /// big that event was.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Damage fraction of max health that reads as a full-strength edge. Matches the camera bump.", true)]
        public const float DamageEdgeFullDamageFraction = 0.25f;

        /// <summary>
        /// Floor on edge strength. A one-point graze still has to answer "from where?",
        /// which is the whole job of this element; scaling it to invisibility would be a
        /// readability bug dressed as restraint.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Lowest strength any hit may produce, so a graze still shows a direction.", true)]
        public const float DamageEdgeMinStrength = 0.35f;

        /// <summary>
        /// Weight given to ALL FOUR edges when the hit carries no direction at all
        /// (PlayerDamageInfo.HitDirection is zero because the attacker gave no position).
        /// Below 1 so an omnidirectional flash can never be mistaken for four real hits.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Per-edge weight for a hit with no direction. Below 1 so it cannot read as four hits.", true)]
        public const float DamageEdgeOmniWeight = 0.55f;

        /// <summary>
        /// Health fraction at or below which the run is "low". ONE number for the whole
        /// HUD: the health bar colour, the health readout colour and the damage edge all
        /// read it here, so the bar cannot go red on a different frame from the edge.
        /// </summary>
        [PrototypeOnly(PendingDecisions.HealthRegen,
            "Low-health threshold. Placed by the HUD task; hoisted here so the bar, the readout " +
            "and the damage edge cannot drift apart.", true)]
        public const float LowHealthFraction = 0.30f;

        // ================================================================== layout

        /// <summary>Canvas reference resolution.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>Distance from the screen edge to any HUD or screen content.</summary>
        public const UiSpace SafeInset = UiSpace.XXL;

        /// <summary>
        /// Widest the page ever gets, so ultrawide does not stretch cards: exactly the
        /// 16:9 reference page (1920 minus two Page margins), i.e. 16:9 layouts are
        /// untouched and 21:9 gains side margin instead of width (WP-022C, AD SHOULD-2).
        /// </summary>
        public const int ContentMaxWidth = 1728;

        // ============================================================ canvas layers

        public const int SortingHud = UiLayer.Hud;
        public const int SortingScreen = UiLayer.Screen;
        public const int SortingPause = UiLayer.Pause;
        public const int SortingDialog = UiLayer.Modal;
    }
}
