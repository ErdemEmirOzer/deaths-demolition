using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (KIT-API §2) · the dialect a kit control is drawn in: V3 (framed, WP-068) or Brush (frameless AP-028 O1).
    /// Every existing factory defaults to V3, so a screen changes only by asking for Brush.
    /// </summary>
    public enum UiControlStyle
    {
        V3 = 0,
        Brush = 1
    }

    /// <summary>
    /// WP-098A · the C1 ink-scrim level of a brush window. The alphas are the LINEAR-light equivalents of AP-028's sRGB mock
    /// (KIT-KILAVUZU §0 T1: the project blends UI in linear light, so the mock's 203/255 is 0.95 here, not 0.80).
    /// </summary>
    public enum UiBrushScrim
    {
        None = 0,
        /// <summary>0.72 · menu panes: text stands in the C2 wash, never on the scrim alone.</summary>
        Pane = 1,
        /// <summary>0.95 · full-screen windows (selection, settings, pause, results): text may stand on it.</summary>
        Window = 2,
        /// <summary>0.71 · over a wallpaper (the upgrade tree): text stands in the C2 wash.</summary>
        Wallpaper = 3,
        /// <summary>0.985 · C31 confirm: the question stands on it.</summary>
        Confirm = 4
    }

    /// <summary>WP-098A · whether a pane lays the C2 sumi wash under its content column.</summary>
    public enum UiBrushWash
    {
        None = 0,
        Column = 1
    }

    /// <summary>
    /// WP-098A (KIT-KILAVUZU §2.2–2.5, S-09 07.10) · the brush kit's numbers. Changing one is a design-system change (the
    /// guide first, then this file), not a tweak: the contrast declarations (<see cref="UiKitContrast"/>) and the linear-light
    /// floor test (<c>UiTranslucentTokensTests.FircaZeminleri_MetinDogrusalIsikta</c>) measure these values as drawn.
    /// </summary>
    public static class UiBrushTokens
    {
        // ------------------------------------------------------------------ §2.2 opacity (linear light)

        public const float ScrimPane = 0.72f, ScrimWindow = 0.95f, ScrimWallpaper = 0.71f, ScrimConfirm = 0.985f;

        /// <summary>
        /// C2 core 0.85 · the tooltip cloud 0.95 · the line tone's α on TextMuted (P0 stand-in for UiColor.InkLine) · the C8
        /// bed (vermilion holds 3:1 only on opaque ink) · the least combined ink α any brush text may stand on (TextSecondary
        /// 4.5:1 on a white world in linear light).
        /// </summary>
        public const float WashCore = 0.85f, TooltipWash = 0.95f, LineAlpha = 0.51f, BedAlpha = 1f, TextGroundMin = 0.945f;

        // ------------------------------------------------------------------ §2.4 size (8 px grid, 1920×1080 reference)

        public const int WashReach = 48, ControlHeight = 64, IconButtonWidth = 72, LabelButtonMin = 232;
        public const int TabWidth = 72, TabStep = 88, SegmentWidth = 304;
        public const int RowStep = 64, RowHit = 56, RowStepCompact = 56, RowHitCompact = 48;
        public const int ListWidth = 1008, LabelColumn = 600, ValueColumn = 360;
        public const int SelectionCore = 3, SelectionBed = 3, SelectionInset = 16, SelectionGap = 8;
        public const int BracketArm = 10, BracketStroke = 3, BracketHalo = 2;   // offset stays UiFocusBrackets.Outset (8)
        public const int TitleStrokeMin = 160, TitleStrokeMax = 600, TitleStrokeHeight = 12, TitleRuleGap = 16, EdgeWidth = 8, EdgeOffset = 24;
        public const int SliderRail = 1, SliderFill = 3, SliderHead = 12, TogglePillWidth = 40, TogglePillHeight = 8, ScrollGrip = 4, ScrollGripMin = 40;
        public const int TooltipMaxWidth = 384, TooltipPadX = 16, TooltipPadY = 8, TooltipGap = 16;
        public const int ConfirmMeasure = 1088, ConfirmActionGap = 64, SealSize = 96;

        /// <summary>The per-window trace budget (KIT-KILAVUZU §1): title 1, C8 2, divider 2, edge 1, wash 2; all together ≤ 6.</summary>
        public const int MaxTitle = 1, MaxSelection = 2, MaxDivider = 2, MaxEdge = 1, MaxWash = 2, MaxTraces = 6;

        // ------------------------------------------------------------------ §2.5 motion (existing UiDuration tokens)

        /// <summary>Open: C2, title stroke, edge and the opening C8 ink in (280) · ScrimIn: C1 (140) · Close: content + C1 + C2 (140) · Reselect: C8 redraw (400).</summary>
        public const UiDuration Open = UiDuration.Screen, ScrimIn = UiDuration.Fast, Close = UiDuration.Fast, Reselect = UiDuration.Emphasis;

        /// <summary>The content's PaneIn starts this long after the scrim (<see cref="UiPresets.PaneIn"/>).</summary>
        public const float ContentDelay = 0.06f;

        /// <summary>C1 alpha of a level (None → 0).</summary>
        public static float Scrim(UiBrushScrim level)
        {
            switch (level)
            {
                case UiBrushScrim.Pane: return ScrimPane;
                case UiBrushScrim.Window: return ScrimWindow;
                case UiBrushScrim.Wallpaper: return ScrimWallpaper;
                case UiBrushScrim.Confirm: return ScrimConfirm;
                default: return 0f;
            }
        }

        /// <summary>Two stacked ink layers as one alpha: 1 − (1 − a)(1 − b).</summary>
        public static float Combined(float a, float b)
        {
            return 1f - (1f - Mathf.Clamp01(a)) * (1f - Mathf.Clamp01(b));
        }

        /// <summary>One-line control height at the current text scale: max(hit, UiFactory.LineHeight(type) + 8) - measured from the face, never a constant row (T3).</summary>
        public static float RowHeight(UiType type, float hit)
        {
            return Mathf.Max(hit, UiFactory.LineHeight(type) + (int)UiSpace.S);
        }

        /// <summary>The line colour: TextMuted at <see cref="LineAlpha"/> (P0 stand-in; P1 → UiColor.InkLine). A value type, no allocation.</summary>
        public static Color LineColour()
        {
            Color c = UiTokens.Get(UiColor.TextMuted);
            c.a = LineAlpha;
            return c;
        }
    }
}
