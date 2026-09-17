using System;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047 C · AP-017 breakable event identities (`asset-production/AP-017/exports/AP017-breakable-event-table-v001.md`):
    /// six break families, each with its residue atlas cell (`breakable_residue` 2×2: TL ceramic / TR wood / BL straw /
    /// BR cloth), a debris sheet and a dust card. The same rows serve S-08 (SFX ids = the event ids). Prefabs arrive with
    /// S-05 WP-038; until then the AP-003B <c>small_breakable</c> family and name keywords map onto the same six ids.
    /// </summary>
    public enum BreakEventId
    {
        BreakCeramic = 0,
        BreakStone = 1,
        BreakWood = 2,
        BreakBamboo = 3,
        BreakPaper = 4,
        BreakStraw = 5
    }

    public readonly struct BreakableVfxProfile
    {
        public BreakableVfxProfile(BreakEventId id, int residueCell, string debrisSheetId, string dustSheetId, int debrisCount, float dustSize)
        {
            Id = id;
            ResidueCell = residueCell;
            DebrisSheetId = debrisSheetId;
            DustSheetId = dustSheetId;
            DebrisCount = debrisCount;
            DustSize = dustSize;
        }

        public BreakEventId Id { get; }
        /// <summary>`breakable_residue` atlas cell: 0 TL ceramic, 1 TR wood, 2 BL straw, 3 BR cloth.</summary>
        public int ResidueCell { get; }
        /// <summary>Sprite sheet for the debris burst (AtlasBurstEffect); empty = the prop's own chip burst only.</summary>
        public string DebrisSheetId { get; }
        /// <summary>Dust / puff card at the break point; empty = none.</summary>
        public string DustSheetId { get; }
        public int DebrisCount { get; }
        public float DustSize { get; }

        /// <summary>The AP-017 row name (SFX id for S-08, `usage_note` id for the manifest).</summary>
        public string EventName => Id.ToString();
    }

    public static class BreakableVfx
    {
        public const int ResidueCeramic = 0, ResidueWood = 1, ResidueStraw = 2, ResidueCloth = 3;

        private static readonly BreakableVfxProfile[] s_table =
        {
            new BreakableVfxProfile(BreakEventId.BreakCeramic, ResidueCeramic, string.Empty, VfxIds.ImpactStone, 0, 0.5f),
            new BreakableVfxProfile(BreakEventId.BreakStone, ResidueCeramic, string.Empty, VfxIds.ImpactStone, 0, 0.7f),
            new BreakableVfxProfile(BreakEventId.BreakWood, ResidueWood, string.Empty, VfxIds.ImpactWood, 0, 0.6f),
            new BreakableVfxProfile(BreakEventId.BreakBamboo, ResidueWood, VfxIds.LeafBurst, VfxIds.ImpactWood, 8, 0.5f),
            new BreakableVfxProfile(BreakEventId.BreakPaper, ResidueCloth, VfxIds.PaperTear, string.Empty, 10, 0f),
            new BreakableVfxProfile(BreakEventId.BreakStraw, ResidueStraw, VfxIds.StrawPuff, VfxIds.StrawPuffCloud, 12, 0.77f)
        };

        public static int Count => s_table.Length;

        public static BreakableVfxProfile Get(BreakEventId id)
        {
            return s_table[(int)id];
        }

        /// <summary>
        /// Prefab / prop name → event id. AP-017 names (`AP017_breakable_<family>`) first, then the AP-003B / AP-005B
        /// keywords. Unknown = BreakWood (crates, the WP-013 default). No allocation: ordinal Contains on the name.
        /// </summary>
        public static BreakEventId EventIdFor(string propName)
        {
            if (string.IsNullOrEmpty(propName)) return BreakEventId.BreakWood;
            if (Has(propName, "lantern_stone") || Has(propName, "stone_lantern") || Has(propName, "tsukubai") || Has(propName, "mossrock")) return BreakEventId.BreakStone;
            if (Has(propName, "lantern_paper") || Has(propName, "shoji") || Has(propName, "paper") || Has(propName, "paravan")) return BreakEventId.BreakPaper;
            if (Has(propName, "planter") || Has(propName, "ceramic") || Has(propName, "pot") || Has(propName, "jar") || Has(propName, "vase") || Has(propName, "small_breakable")) return BreakEventId.BreakCeramic;
            if (Has(propName, "fence") || Has(propName, "bamboo")) return BreakEventId.BreakBamboo;
            if (Has(propName, "straw") || Has(propName, "bale")) return BreakEventId.BreakStraw;
            return BreakEventId.BreakWood;   // bench, sign, crate, barrel, handcart, shed
        }

        private static bool Has(string name, string key)
        {
            return name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
