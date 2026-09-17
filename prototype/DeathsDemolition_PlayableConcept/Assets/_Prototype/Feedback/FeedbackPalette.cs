using DD.Prototype.UI;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// WP-047D · the feedback layer's readout colours resolved through S-09's <see cref="UiTokens"/> palette (WP-022G
    /// colour-blind safe option): damage numbers and HUD hit markers take the state tokens, so the critical / kill /
    /// armour distinction survives a red–green deficit. Blood is excluded by directive (DEC-077 band). World VFX
    /// tints (Kagura amber, Ofuda cream, Tide blue) stay as authored — they differ in luminance, not only hue.
    /// </summary>
    public static class FeedbackPalette
    {
        private static readonly Color ArmorDefault = new Color(0.68f, 0.78f, 0.90f, 1f);

        /// <summary>Critical hit readouts: the warning token (gold; amber on the colour-blind palette).</summary>
        public static Color Critical => UiTokens.Get(UiColor.StateWarning);

        /// <summary>Kill readouts: the danger token (coral on both palettes — luminance-separated from the warning token).</summary>
        public static Color Kill => UiTokens.Get(UiColor.StateDanger);

        /// <summary>Armour readouts: steel blue; wisteria on the colour-blind palette (off the red–gold axis entirely).</summary>
        public static Color Armor => UiTokens.Palette == UiPalette.ColorBlindSafe ? UiTokens.Get(UiColor.AccentWisteria) : ArmorDefault;

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
