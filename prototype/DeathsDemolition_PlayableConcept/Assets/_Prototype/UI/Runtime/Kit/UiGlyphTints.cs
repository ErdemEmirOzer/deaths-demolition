using DD.Prototype.Core.Config;

namespace DD.Prototype.UI
{
    /// <summary>
    /// M4-R1 N19 · the one place that names the glyph identity tints (UiTokens v4; council/r1b/N19-DIRECTION.md). A glyph's
    /// colour says what it is - the lineage of a classic weapon, the Special it belongs to - never how it is doing: LOW,
    /// EMPTY, reload and locked are carried by words, rings and alpha, and the tint stays. The reserve registry keeps the
    /// Glyph* tokens to this file, so every consumer asks here.
    /// </summary>
    public static class UiGlyphTints
    {
        /// <summary>A locked / unaffordable glyph keeps its tint at this alpha (no grey swap: colour is identity).</summary>
        public const float LockedAlpha = 0.55f;

        /// <summary>E11 LOW / EMPTY: the weapon glyph steps back so the amber word reads first.</summary>
        public const float LowAlpha = 0.70f;

        /// <summary>The Nami lineage: ar_2, smg_2, sg_2, lmg_2, rl_1. Everything else classic is Iwa.</summary>
        public static bool IsNami(ClassicWeaponId id)
        {
            return id == ClassicWeaponId.Ar2 || id == ClassicWeaponId.Smg2 || id == ClassicWeaponId.Sg2
                || id == ClassicWeaponId.Lmg2 || id == ClassicWeaponId.Rl1;
        }

        /// <summary>A classic weapon's glyph tint: Iwa terracotta or Nami celadon.</summary>
        public static UiColor Weapon(ClassicWeaponId id)
        {
            return IsNami(id) ? UiColor.GlyphNami : UiColor.GlyphIwa;
        }

        /// <summary>Raiju (chain lightning): its glyph, its Electrocuted stamp, its tree links.</summary>
        public static UiColor ChainLightning
        {
            get { return UiColor.GlyphArc; }
        }

        /// <summary>Namekuji (gel): its glyph, the Sliding / Downed stamps, the player's SLICK hint, its tree links.</summary>
        public static UiColor Gel
        {
            get { return UiColor.GlyphGel; }
        }
    }
}
