using System;
using System.Collections.Generic;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · T1 contrast v3 (direction §12): every kit component DECLARES the
    /// foreground / background pairs it puts on screen, and the declarations are measured in
    /// both palettes (Default, colour-blind safe) by UiComponentsV3Tests. A component class
    /// in Kit/Components without a declaration fails the test (beyansız bileşen FAIL); a
    /// component that draws no text or boundary of its own declares an empty set on purpose.
    /// Thresholds: 4.5 text, 3.0 large text / interface boundaries (WCAG 1.4.11). Disabled
    /// states are exempt (WCAG) but the kit keeps the audited disabled pair anyway.
    ///
    /// USAGE: add the pairs here in the same commit as the component that draws them.
    /// DON'T: declare a pair the component does not draw to make a number pass, or leave a
    /// drawn pair out because it fails - fix the token or the usage.
    /// ACCESSIBILITY: this table is the accessibility contract of the kit.
    /// </summary>
    public static class UiKitContrast
    {
        public const float Text = 4.5f;
        public const float Boundary = 3f;

        /// <summary>A cue the AD specified as subtle, where another part of the component carries the boundary (the measured ratio is in the description).</summary>
        public const float Decorative = 1f;

        private static UiContrastCase C(string what, UiColor fg, UiColor bg, float required, float fgAlpha = 1f)
        {
            return new UiContrastCase(what, fg, bg, required, null, fgAlpha);
        }

        private static UiContrastCase Over(string what, UiColor fg, UiColor layer, UiColor bg, float required)
        {
            return new UiContrastCase(what, fg, bg, required, layer);
        }

        /// <summary>
        /// WP-098A (KIT-KILAVUZU §4.4) · a brush pair as drawn: <paramref name="fg"/> (× <paramref name="fgAlpha"/>) on the ink
        /// (SurfaceInset at <paramref name="ink"/>, a <see cref="UiBrushTokens"/> level or a scrim ⊕ wash) over the canonical
        /// white world (Cream100, the top of the grey ramp UiTranslucentTokensTests walks) - the worst case for a dark ground.
        /// </summary>
        private static UiContrastCase OnInk(string what, UiColor fg, float ink, float required, float fgAlpha = 1f)
        {
            return new UiContrastCase(what, fg, UiColor.TextPrimary, required, UiColor.SurfaceInset, fgAlpha, ink);
        }

        private const float Window = UiBrushTokens.ScrimWindow;
        private static readonly float PaneWash = UiBrushTokens.Combined(UiBrushTokens.ScrimPane, UiBrushTokens.WashCore);
        private static readonly float WallpaperWash = UiBrushTokens.Combined(UiBrushTokens.ScrimWallpaper, UiBrushTokens.WashCore);

        /// <summary>Declared pairs by component type.</summary>
        public static readonly Dictionary<Type, UiContrastCase[]> Declared = new Dictionary<Type, UiContrastCase[]>
        {
            {
                typeof(UiSwitch), new[]
                {
                    C("Switch label on the row", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Switch word ON", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Switch word OFF", UiColor.TextSecondary, UiColor.SurfaceBase, Text),
                    C("Switch knob ink edge on the ON track", UiColor.SurfacePanel, UiColor.StateSuccess, Boundary),
                    C("Switch knob core on its ink edge", UiColor.TextPrimary, UiColor.SurfacePanel, Boundary),
                    C("Switch knob on the OFF track", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("Switch track rim on the row", UiColor.BorderStrong, UiColor.SurfaceBase, Boundary),
                    // WP-098A P0b · C15 Brush row: the state is the pill's shape (filled / hollow) and the word, never its colour.
                    C("Brush switch label on the ink", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("Brush switch word ON", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("Brush switch word OFF / disabled", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                    C("Brush switch pill ON (filled cream)", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("Brush switch pill OFF (hollow outline) / disabled", UiColor.TextSecondary, UiColor.SurfaceInset, Boundary),
                }
            },
            {
                typeof(UiStepper), new[]
                {
                    C("Stepper label", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Stepper value", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Stepper chevron", UiColor.TextSecondary, UiColor.SurfaceBase, Boundary),
                    // WP-098A P0b · C16 Brush value cycle.
                    C("Brush cycle label / value on the ink", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("Brush cycle label / value, disabled", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                    C("Brush cycle arrow at rest", UiColor.TextSecondary, UiColor.SurfaceInset, Boundary),
                    C("Brush cycle arrow under the pointer", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("Brush cycle end arrow at OpacityDisabled (an end, not an action - the word carries the value)", UiColor.TextSecondary, UiColor.SurfaceInset, Decorative, UiTokens.OpacityDisabled),
                }
            },
            { typeof(UiStepperArrow), new UiContrastCase[0] }, // pointer target only, draws nothing
            {
                typeof(UiChips), new[]
                {
                    C("Neutral chip", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                    C("NEW chip (ink on sakura)", UiColor.SurfaceBase, UiColor.AccentSakura, Text),
                    C("Value chip", UiColor.TextPrimary, UiColor.SurfaceRaised, Text),
                    C("Selected chip (ink on cream, the shipped SELECTED badge)", UiColor.SurfaceBase, UiColor.TextPrimary, Text),
                    C("State chip, status (NET / DAZED / PULLED)", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("State chip, warning", UiColor.StateWarning, UiColor.SurfaceInset, Text),
                    C("State chip, danger", UiColor.StateDanger, UiColor.SurfaceInset, Text),
                    C("State chip rim, status, on a card", UiColor.TextPrimary, UiColor.SurfacePanel, Boundary, UiChips.RimAlpha),
                    C("State chip rim, warning, on a card", UiColor.StateWarning, UiColor.SurfacePanel, Boundary, UiChips.RimAlpha),
                    C("State chip rim, danger, on a card", UiColor.StateDanger, UiColor.SurfacePanel, Boundary, UiChips.RimAlpha),
                }
            },
            {
                typeof(UiNotices), new[]
                {
                    C("Notice text", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("Notice bar, warning", UiColor.StateWarning, UiColor.SurfaceInset, Boundary),
                    C("Notice bar, error", UiColor.StateDanger, UiColor.SurfaceInset, Boundary),
                    C("Notice bar, info", UiColor.TextSecondary, UiColor.SurfaceInset, Boundary),
                    C("Empty-state sentence", UiColor.TextSecondary, UiColor.SurfacePanel, Text),
                    C("Empty-state glyph", UiColor.TextMuted, UiColor.SurfacePanel, Boundary),
                }
            },
            {
                typeof(UiModal), new[]
                {
                    C("Modal title", UiColor.TextPrimary, UiColor.SurfacePanel, Text),
                    C("Modal body", UiColor.TextSecondary, UiColor.SurfacePanel, Text),
                    C("Modal frame on the card", UiColor.BorderStrong, UiColor.SurfacePanel, Boundary),
                    // WP-098A C31: no card - the title, the TextPrimary body and the C11 words stand on the Confirm ink.
                    OnInk("Brush confirm title / body / action word on the Confirm ink (0.985; 13.32:1)", UiColor.TextPrimary, UiBrushTokens.ScrimConfirm, Text),
                    OnInk("Brush confirm action glyph on the Confirm ink", UiColor.TextPrimary, UiBrushTokens.ScrimConfirm, Boundary),
                }
            },
            {
                typeof(UiPips), new[]
                {
                    C("Filled pip", UiColor.TextPrimary, UiColor.SurfacePanel, Boundary),
                    C("Empty pip ring", UiColor.TextMuted, UiColor.SurfacePanel, Boundary),
                    C("Ghost pip (the tier a buy adds)", UiColor.TextPrimary, UiColor.SurfacePanel, Boundary, UiPips.GhostAlpha),
                }
            },
            {
                typeof(UiCounter), new[]
                {
                    C("Counter", UiColor.TextPrimary, UiColor.SurfacePanel, Text),
                    C("Currency counter", UiColor.StateGold, UiColor.SurfacePanel, Text),
                    // WP-094 (AD SHOULD-5, 08.10): the HUD gold counter and its "+N" carry the HUD ink outline (UiHudInk, HudInk
                    // α 0.98) and no panel - measured on that ink over the brightest world (03's cloud reflection) and the darkest.
                    Over("HUD currency counter / its +N on the ink outline over a white world", UiColor.StateGold, UiColor.HudInk, UiColor.TextPrimary, Text),
                    Over("HUD currency counter / its +N on the ink outline over a black world", UiColor.StateGold, UiColor.HudInk, UiColor.SurfaceInset, Text),
                    C("Fraction maximum", UiColor.TextSecondary, UiColor.SurfacePanel, Text),
                }
            },
            {
                typeof(UiRing), new[]
                {
                    C("Reload ring (Focus400, never gold)", UiColor.BarReload, UiColor.SurfaceBase, Boundary),
                    C("Special ring", UiColor.BarSpecial, UiColor.SurfaceBase, Boundary),
                }
            },
            {
                typeof(UiHeroTiles), new[]
                {
                    C("Hero number", UiColor.TextPrimary, UiColor.SurfacePanel, Text),
                    C("Hero caption", UiColor.TextSecondary, UiColor.SurfacePanel, Text),
                }
            },
            {
                typeof(UiSlider), new[]
                {
                    C("Slider label", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Slider value", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Slider fill on the rail", UiColor.TextPrimary, UiColor.BarTrack, Boundary),
                    C("Slider rail rim on the page (1.72:1 - extent cue; the bead and the fill carry the boundary)", UiColor.BorderHairline, UiColor.SurfaceBase, Decorative),
                    C("Slider detent tick on the rail", UiColor.BorderStrong, UiColor.SurfaceInset, Boundary),
                    C("Slider bead core on its ink edge", UiColor.TextPrimary, UiColor.SurfacePanel, Boundary),
                    C("Slider focus ring", UiColor.BorderFocus, UiColor.SurfaceBase, Boundary),
                    // WP-098A P0b · C14 Brush row on the window ink (≥ 0.945 combined over the world: FircaZeminleri_MetinDogrusalIsikta).
                    C("Brush slider label / value on the ink", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("Brush slider label / value, disabled", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                    C("Brush slider cream fill on the ink", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("Brush slider head core on its ink edge", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("Brush slider rail, the line tone (3.70:1 on ink - extent cue; the fill and the head carry the value)", UiColor.TextMuted, UiColor.SurfaceInset, Decorative, UiBrushTokens.LineAlpha),
                }
            },
            {
                typeof(UiScrollView), new[]
                {
                    C("Scroll grip on its rail", UiColor.BorderStrong, UiColor.SurfaceInset, Boundary),
                    C("Scroll grip on the pane (the rail's ends)", UiColor.BorderStrong, UiColor.SurfacePanel, Boundary),
                    C("Scroll rail on the pane (extent cue; the grip carries the position)", UiColor.SurfaceInset, UiColor.SurfacePanel, Decorative),
                    // WP-098A P0b · C35 Brush: cream grip (G15 - never gold), the rail in the line tone.
                    C("Brush scroll grip on the ink", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("Brush scroll rail, the line tone (extent cue; the grip carries the position)", UiColor.TextMuted, UiColor.SurfaceInset, Decorative, UiBrushTokens.LineAlpha),
                }
            },
            {
                typeof(UiCard), new[]
                {
                    C("Card title / text", UiColor.TextPrimary, UiColor.SurfacePanel, Text),
                    C("Card supporting text", UiColor.TextSecondary, UiColor.SurfacePanel, Text),
                    C("Card text on the lifted fill", UiColor.TextSecondary, UiColor.SurfaceRaised, Text),
                    C("Card bevel sheen (2.29:1 in linear light - the Elev cue; the frame carries the edge)", UiColor.SheenRim, UiColor.SurfacePanel, Decorative),
                }
            },
            {
                // WP-098A C7 (S-09 S4, global): cream arms on their 2 px ink halo - the halo rims them on every side, so the arms
                // meet ink whatever the world (16.40:1); the halo itself against a dark page is the arms' frame, not a boundary.
                typeof(UiFocusBrackets), new[]
                {
                    C("Focus brackets (cream) on their ink halo", UiColor.TextPrimary, UiColor.BorderFocusHalo, Boundary),
                    C("Focus brackets on the page", UiColor.TextPrimary, UiColor.SurfaceBase, Boundary),
                    C("Focus brackets on a card", UiColor.TextPrimary, UiColor.SurfacePanel, Boundary),
                    OnInk("Focus brackets on a brush window", UiColor.TextPrimary, Window, Boundary),
                }
            },
            {
                typeof(UiButton), new[]
                {
                    C("Ghost label", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    Over("Hold label over the HoldFill sweep", UiColor.TextPrimary, UiColor.HoldFill, UiColor.SurfaceBase, Text),
                    C("Disabled reason caption", UiColor.TextSecondary, UiColor.SurfaceRaised, Text),
                    C("Icon glyph", UiColor.TextPrimary, UiColor.SurfaceBase, Boundary),
                    C("Secondary / Nav rest sheen (2.43:1 in linear light; 1 px lacquer cue, the border carries the edge)", UiColor.SheenRim, UiColor.SurfaceBase, Decorative),
                    // WP-098A Brush (C10 / C11 / C17): no surface - the word and glyph stand on the window's ink.
                    OnInk("Brush word on a window (MenuBody, TextPrimary)", UiColor.TextPrimary, Window, Text),
                    OnInk("Brush word, another segment / disabled (TextSecondary)", UiColor.TextSecondary, Window, Text),
                    OnInk("Brush word in a pane's wash", UiColor.TextSecondary, PaneWash, Text),
                    OnInk("Brush glyph on a window", UiColor.TextPrimary, Window, Boundary),
                    C("Brush disabled hairline (line tone on ink, 3.71:1; the word carries the state)", UiColor.TextMuted, UiColor.SurfaceInset, Decorative, UiBrushTokens.LineAlpha),
                }
            },
            { typeof(UiButtons), new UiContrastCase[0] }, // factory for UiButton (declared above)
            {
                // WP-081 ikon v2 B5b: an O1 glyph's material only adds light - max(lit, tint) per channel (UiIconMaterialTests
                // T1, T3) - so the four flat glyph pairs stay its floor. Its outer layers sit outside the ink: the glow below
                // (a halo, not a boundary); the cold lob (#293B46, UiMaterialSpec.IconLobColour) is no token yet - AD D7.
                typeof(UiIconButton), new[]
                {
                    C("Icon glyph, plain button", UiColor.TextPrimary, UiColor.SurfaceBase, Boundary),
                    C("Icon glyph, chosen tab", UiColor.TextPrimary, UiColor.SurfaceRaised, Boundary),
                    C("Icon glyph, other tab", UiColor.TextSecondary, UiColor.SurfaceBase, Boundary),
                    C("Icon glyph, unavailable", UiColor.TextSecondary, UiColor.SurfaceRaised, Boundary),
                    C("Tab word, chosen", UiColor.TextPrimary, UiColor.SurfaceRaised, Text),
                    C("Tab word, other", UiColor.TextSecondary, UiColor.SurfaceBase, Text),
                    C("Tab word, unavailable", UiColor.TextSecondary, UiColor.SurfaceRaised, Text),
                    C("Chosen bar under the word", UiColor.TextPrimary, UiColor.SurfaceRaised, Boundary),
                    C("Icon glow at its hover / focus peak (linear α 0.017 = AP-028's sRGB 0.08; a halo - the glyph carries the boundary)",
                        UiColor.TextPrimary, UiColor.SurfaceBase, Decorative, UiMaterialSpec.IconGlowAlpha),
                    // WP-098A P0b · C12 Brush tab: frameless, the choice is the group's C8 + the tone (no fill, no bar).
                    C("Brush tab glyph, chosen", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("Brush tab glyph, other / unavailable", UiColor.TextSecondary, UiColor.SurfaceInset, Boundary),
                    C("Brush tab word, chosen (G2 interim)", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("Brush tab word, other (G2 interim)", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                }
            },
            { typeof(UiIconTokens), new UiContrastCase[0] }, // numbers only
            { typeof(UiIconControls), new UiContrastCase[0] }, // factory for UiIconButton (declared above)
            {
                typeof(UiTooltip), new[]
                {
                    C("Tooltip word on its plate", UiColor.TextPrimary, UiColor.SurfaceRaised, Text),
                    C("Tooltip edge on the page", UiColor.BorderStrong, UiColor.SurfaceBase, Boundary),
                    // WP-098A P0b · C27 Brush (the default): the ink cloud at TooltipWash 0.95 (FircaZeminleri: "Tooltip").
                    C("Brush tooltip word on its ink cloud", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                }
            },
            {
                typeof(UiBar), new[]
                {
                    C("Health fill on the track", UiColor.BarHealth, UiColor.BarTrack, Boundary),
                    C("Lost-value ghost on the track", UiColor.TextSecondary, UiColor.BarTrack, Boundary, UiBar.GhostAlpha),
                    C("Regen track below the bar", UiColor.TextSecondary, UiColor.SurfaceBase, Boundary, UiBar.RegenAlpha),
                }
            },
            { typeof(UiKitContrast), new UiContrastCase[0] },

            // ---------------------------------------------------------------- WP-098A brush kit (KIT-KILAVUZU §4.4)
            {
                // WP-098A P0b · what the facade draws itself (UiBrushKit.Controls.cs): the C18 list row's trailing glyph and
                // small text, the C21 counter's caption, number and rule. The rest is declared by the component it returns.
                typeof(UiBrushKit), new[]
                {
                    C("List row name on the ink", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("List row name, disabled", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                    C("List row trailing small text", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                    C("List row status glyph (check / lock)", UiColor.TextPrimary, UiColor.SurfaceInset, Boundary),
                    C("List row status glyph, disabled", UiColor.TextSecondary, UiColor.SurfaceInset, Boundary),
                    C("Big counter caption", UiColor.TextSecondary, UiColor.SurfaceInset, Text),
                    C("Big counter number", UiColor.TextPrimary, UiColor.SurfaceInset, Text),
                    C("Big counter, currency (StateGold - the gold reserve)", UiColor.StateGold, UiColor.SurfaceInset, Text),
                    C("Big counter rule, the line tone (separator, no boundary role)", UiColor.TextMuted, UiColor.SurfaceInset, Decorative, UiBrushTokens.LineAlpha),
                }
            },
            {
                // Text may stand on the Window and Confirm ink alone; on a Pane / Wallpaper scrim only inside the C2 wash.
                typeof(UiBrushPane), new[]
                {
                    OnInk("TextPrimary on the Window ink (0.95; 9.27:1)", UiColor.TextPrimary, Window, Text),
                    OnInk("TextSecondary on the Window ink (5.10:1)", UiColor.TextSecondary, Window, Text),
                    OnInk("Currency gold on the Window ink (4.95:1)", UiColor.StateGold, Window, Text),
                    OnInk("TextPrimary in a pane's wash (Pane ⊕ WashCore 0.958)", UiColor.TextPrimary, PaneWash, Text),
                    OnInk("TextSecondary in a pane's wash (5.48:1)", UiColor.TextSecondary, PaneWash, Text),
                    OnInk("Currency gold in a pane's wash", UiColor.StateGold, PaneWash, Text),
                    OnInk("TextPrimary in a wallpaper's wash (Wallpaper ⊕ WashCore 0.957)", UiColor.TextPrimary, WallpaperWash, Text),
                    OnInk("TextSecondary in a wallpaper's wash (5.40:1)", UiColor.TextSecondary, WallpaperWash, Text),
                    OnInk("Currency gold in a wallpaper's wash (5.24:1)", UiColor.StateGold, WallpaperWash, Text),
                }
            },
            {
                typeof(UiBrushMark), new[]
                {
                    C("C8 vermilion core on its opaque ink bed (3.53:1; bedless 1.99)", UiColor.AccentPrimary, UiColor.SurfaceInset, Boundary),
                    C("Title stroke / divider / edge: the line tone on ink (3.71:1; decoration - shape, never state)", UiColor.TextMuted, UiColor.SurfaceInset, Decorative, UiBrushTokens.LineAlpha),
                    OnInk("The line tone on the Window ink (2.31:1; decoration)", UiColor.TextMuted, Window, Decorative, UiBrushTokens.LineAlpha),
                }
            },
            {
                // The choice is shape (the one C8) + luminance: the chosen word / glyph TextPrimary, the others TextSecondary.
                typeof(UiBrushChoiceGroup), new[]
                {
                    OnInk("Chosen item word on the Window ink", UiColor.TextPrimary, Window, Text),
                    OnInk("Other item word on the Window ink (5.10:1)", UiColor.TextSecondary, Window, Text),
                    OnInk("Other item word in a pane's wash", UiColor.TextSecondary, PaneWash, Text),
                    OnInk("Chosen tab glyph on the Window ink", UiColor.TextPrimary, Window, Boundary),
                    OnInk("Other tab glyph on the Window ink", UiColor.TextSecondary, Window, Boundary),
                }
            },
        };
    }
}
