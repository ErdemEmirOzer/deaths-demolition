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
                }
            },
            {
                typeof(UiStepper), new[]
                {
                    C("Stepper label", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Stepper value", UiColor.TextPrimary, UiColor.SurfaceBase, Text),
                    C("Stepper chevron", UiColor.TextSecondary, UiColor.SurfaceBase, Boundary),
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
                }
            },
            {
                typeof(UiScrollView), new[]
                {
                    C("Scroll grip on its rail", UiColor.BorderStrong, UiColor.SurfaceInset, Boundary),
                    C("Scroll grip on the pane (the rail's ends)", UiColor.BorderStrong, UiColor.SurfacePanel, Boundary),
                    C("Scroll rail on the pane (extent cue; the grip carries the position)", UiColor.SurfaceInset, UiColor.SurfacePanel, Decorative),
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
                typeof(UiFocusBrackets), new[]
                {
                    C("Focus brackets on the page", UiColor.BorderFocus, UiColor.SurfaceBase, Boundary),
                    C("Focus brackets on a card", UiColor.BorderFocus, UiColor.SurfacePanel, Boundary),
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
                }
            },
            { typeof(UiButtons), new UiContrastCase[0] }, // factory for UiButton (declared above)
            {
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
                }
            },
            { typeof(UiIconTokens), new UiContrastCase[0] }, // numbers only
            { typeof(UiIconControls), new UiContrastCase[0] }, // factory for UiIconButton (declared above)
            {
                typeof(UiTooltip), new[]
                {
                    C("Tooltip word on its plate", UiColor.TextPrimary, UiColor.SurfaceRaised, Text),
                    C("Tooltip edge on the page", UiColor.BorderStrong, UiColor.SurfaceBase, Boundary),
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
        };
    }
}
