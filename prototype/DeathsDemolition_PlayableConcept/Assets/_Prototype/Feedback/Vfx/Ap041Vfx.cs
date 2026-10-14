namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-101 (R4, AP-041) · the five top-down muzzle families of the AP-041 v001 set (manifest family muzzle_&lt;name&gt;).</summary>
    public enum Ap041MuzzleClass
    {
        None = -1,
        Pistol = 0,
        Rifle = 1,
        Lmg = 2,
        Sniper = 3,
        Pellet = 4
    }

    /// <summary>
    /// WP-101 (R4, AP-041) · Which AP-041 row a shot, an impact or a hole draws. Pure tables over <see cref="VfxIds"/> (no
    /// catalog access, nothing allocated): <see cref="VfxDressing"/> asks the catalog whether the row is there and falls back to
    /// the AP-010 row when it is not, so a checkout without the AP-041 import plays exactly what it played before.
    ///
    /// Class map (C-00 11.10): AssaultRifle → rifle, Smg → pistol (the short, tight flash), Lmg → lmg, Sniper → sniper,
    /// Shotgun → pellet; the Launcher keeps the AP-010 sheet and its backblast. Surfaces follow the AP-010 grouping of
    /// <c>ImpactIdsFor</c>: wood / foliage / paper → wood, dirt / water → earth, metal → metal, flesh → flesh, the rest → stone.
    /// Holes: stone / wood / metal cycle their four variants; earth keeps the AP-010 mark (AP-041 has none), scorch stays AP-010.
    /// </summary>
    public static class Ap041Vfx
    {
        public const int MuzzleVariants = 3;
        public const int DecalVariants = 4;
        public const int DecalSets = 3;   // stone, wood, metal
        /// <summary>WP-101 (S-07): an AP-041 hole sheet is the whole impact mark (hole + spall + cracks, 0.28 m in the manifest); at the
        /// calibre hole size (0.06 / 0.09 / 0.14 m) its hole shrank to ≈ 2 px at the TD camera (≈ 71 px/m). Drawn × this over the
        /// calibre size: the hole itself stays near calibre, the spall round it reads. AD-tuned.</summary>
        public const float DecalSpallScale = 2.2f;
        /// <summary>The whole AP-041 mark (manifest world size): a heavy round's spalled hole never draws past it (review: 0.14 × 2.2 = 0.31).</summary>
        public const float DecalSpallMax = .28f;

        /// <summary>The AP-041 hole size for a calibre hole: the spall round it, capped at the sheet's own size.</summary>
        public static float SpalledHole(float hole) => System.Math.Min(hole * DecalSpallScale, DecalSpallMax);

        /// <summary>AD r1 M6: metal does not spall - its hole sheet draws at most × <see cref="DecalSpallScaleMetal"/>.</summary>
        public const float DecalSpallScaleMetal = 1.4f;

        /// <summary>The AP-041 hole size for a calibre hole on a decal set (0 stone, 1 wood, 2 metal).</summary>
        public static float SpalledHole(float hole, int set) =>
            set == 2 ? System.Math.Min(hole * DecalSpallScaleMetal, DecalSpallMax) : SpalledHole(hole);

        /// <summary>Whether a decal row id is an AP-041 hole sheet.</summary>
        public static bool IsAp041Decal(string id) => id != null && id.StartsWith("decal_ap041", System.StringComparison.Ordinal);

        /// <summary>The AP-041 muzzle family of a weapon class; <see cref="Ap041MuzzleClass.None"/> for the launcher.</summary>
        public static Ap041MuzzleClass MuzzleClassOf(VfxWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case VfxWeaponClass.AssaultRifle: return Ap041MuzzleClass.Rifle;
                case VfxWeaponClass.Smg: return Ap041MuzzleClass.Pistol;
                case VfxWeaponClass.Lmg: return Ap041MuzzleClass.Lmg;
                case VfxWeaponClass.Sniper: return Ap041MuzzleClass.Sniper;
                case VfxWeaponClass.Shotgun: return Ap041MuzzleClass.Pellet;
                default: return Ap041MuzzleClass.None;
            }
        }

        /// <summary>TD flash row id of a class and variant (0..2, wrapped); null for <see cref="Ap041MuzzleClass.None"/>.</summary>
        public static string MuzzleId(Ap041MuzzleClass muzzleClass, int variant)
        {
            if (muzzleClass == Ap041MuzzleClass.None) return null;
            int v = variant % MuzzleVariants;
            if (v < 0) v += MuzzleVariants;
            return VfxIds.Ap041MuzzleTd[(int)muzzleClass][v];
        }

        /// <summary>Impact sheet id of a surface (same grouping as the AP-010 table).</summary>
        public static string ImpactIdFor(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Wood:
                case SurfaceKind.Foliage:
                case SurfaceKind.Paper:
                    return VfxIds.Ap041ImpactWood;
                case SurfaceKind.Dirt:
                case SurfaceKind.Water:
                    return VfxIds.Ap041ImpactEarth;
                case SurfaceKind.Metal:
                    return VfxIds.Ap041ImpactMetal;
                case SurfaceKind.Flesh:
                    return VfxIds.Ap041ImpactFlesh;
                default:
                    return VfxIds.Ap041ImpactStone;
            }
        }

        /// <summary>
        /// The hole set (0 stone, 1 wood, 2 metal) for a surface that leaves a mark in the AP-010 table, or -1: dirt keeps the
        /// AP-010 earth mark, and surfaces without a mark (foliage, paper, water, flesh) stay without one.
        /// </summary>
        public static int DecalSetOf(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Wood: return 1;
                case SurfaceKind.Metal: return 2;
                case SurfaceKind.Foliage:
                case SurfaceKind.Paper:
                case SurfaceKind.Dirt:
                case SurfaceKind.Water:
                case SurfaceKind.Flesh:
                    return -1;
                default: return 0;
            }
        }

        /// <summary>Hole row id of a set and variant (0..3, wrapped); null for set -1.</summary>
        public static string DecalId(int set, int variant)
        {
            if (set < 0 || set >= DecalSets) return null;
            int v = variant % DecalVariants;
            if (v < 0) v += DecalVariants;
            return VfxIds.Ap041Decals[set][v];
        }

        /// <summary>True for an AP-041 TD flash row (not the smoke).</summary>
        public static bool IsMuzzleFlashId(string id)
        {
            return id != null && id.StartsWith(VfxIds.Ap041MuzzlePrefix, System.StringComparison.Ordinal) && !string.Equals(id, VfxIds.Ap041MuzzleSmoke, System.StringComparison.Ordinal);
        }
    }
}
