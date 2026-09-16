using System;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-036 (ADR-036-1) · The AP-014 tier silhouette parts as data: one part = the meshes of
    /// (fit A/B/C × LOD 0–3) skinned to the shared ZombieRig bones (rigid where AP-014 notes),
    /// hung from an AP-007 anchor (anchor_torso / socket_back). Written by
    /// <c>ArtTierBuilder</c> from <c>AP014-manifest.json</c> + the imported FBX; read by
    /// <see cref="TierSilhouetteSlot"/> through <see cref="EnemyVersionApplier"/>.
    ///
    /// Materials: the parts atlas (<see cref="partMaterials"/>, one per version) and the
    /// version tint materials for the body atlas (<see cref="bodyMaterials"/>) and the armour
    /// atlas (<see cref="armorMaterials"/>) on <c>DD/Character/Tier Lit</c>: lerp(base, tint,
    /// Mask.G·k) on the cloth, lerp(base, tint, Mask.R·k) on the lacquer with the lacquer floor
    /// (AP-014 tint table). Standard (index 0) carries the untinted materials.
    /// </summary>
    public sealed class ZombieTierLibrary : ScriptableObject
    {
        public const string Schema = "dd-zombie-tier-v1";
        public const int LodCount = ZombieModuleLibrary.LodCount;
        public const int MaxPartsPerTier = 2;   // Elite: sode + backframe

        [Serializable]
        public sealed class Fit
        {
            public string fit;                                                       // A | B | C
            public ZombieModuleLibrary.MeshRef[] lods = new ZombieModuleLibrary.MeshRef[LodCount];
            public int TrianglesLod0 => lods != null && lods.Length > 0 && lods[0] != null ? lods[0].triangles : 0;
        }

        [Serializable]
        public sealed class Part
        {
            public string id;                // special_collar, elite_sode, elite_backframe, ultra_yoke
            public EnemyTier tier;
            public string anchor;            // anchor_torso | socket_back
            public string[] skinBones = Array.Empty<string>();
            public Fit[] fits = Array.Empty<Fit>();
            public int trianglesLod0;        // worst fit
            public int materialIndex;        // 0 = parts atlas material of the tier, 1 = armour atlas material (elite_sode uses the AP-008 atlas)

            public Fit FitOf(string fitId)
            {
                if (fits == null) return null;
                for (int i = 0; i < fits.Length; i++) if (fits[i] != null && fits[i].fit == fitId) return fits[i];
                return fits.Length > 0 ? fits[0] : null;
            }
        }

        [Serializable]
        public sealed class TintEntry
        {
            public EnemyTier tier;
            public Color cloth = Color.white;
            public float clothK;
            public Color lacquer = Color.white;
            public float lacquerK;
            public float lacquerFloor;      // value floor under the lacquer mask (AP-014: 0.35–0.40)
        }

        public string schema = Schema;
        public string package = "AP-014";
        public string version;
        public string sourceSha256;
        public string[] fits = { "A", "B", "C" };
        public Part[] parts = Array.Empty<Part>();
        public TintEntry[] tints = Array.Empty<TintEntry>();
        public Material[] partMaterials = new Material[4];    // parts atlas per version
        public Material[] bodyMaterials = new Material[4];    // AP-007 atlas per version (Tier Lit)
        public Material[] armorMaterials = new Material[4];   // AP-008 atlas per version (Tier Lit)

        public bool IsReady => parts != null && parts.Length > 0 && partMaterials != null && partMaterials.Length == 4 && partMaterials[0] != null;

        /// <summary>Parts of the tier (0–2), in library order. Allocation-free fill of <paramref name="buffer"/>.</summary>
        public int PartsOf(EnemyTier tier, Part[] buffer)
        {
            int n = 0;
            if (parts == null || buffer == null) return 0;
            for (int i = 0; i < parts.Length && n < buffer.Length; i++)
            {
                if (parts[i] != null && parts[i].tier == tier) buffer[n++] = parts[i];
            }

            return n;
        }

        public TintEntry TintOf(EnemyTier tier)
        {
            if (tints == null) return null;
            for (int i = 0; i < tints.Length; i++) if (tints[i] != null && tints[i].tier == tier) return tints[i];
            return null;
        }

        public Material PartMaterial(EnemyTier tier, int materialIndex)
        {
            Material[] set = materialIndex == 1 ? armorMaterials : partMaterials;
            int t = (int)tier;
            return set != null && t >= 0 && t < set.Length ? set[t] : null;
        }

        public Material BodyMaterial(EnemyTier tier) => bodyMaterials != null && (int)tier < bodyMaterials.Length ? bodyMaterials[(int)tier] : null;
        public Material ArmorMaterial(EnemyTier tier) => armorMaterials != null && (int)tier < armorMaterials.Length ? armorMaterials[(int)tier] : null;

        private static ZombieTierLibrary s_active;
        private static bool s_probed;
        public const string ResourcesPath = "DD_ZombieTier";

        /// <summary>The Resources asset (null when the art pipeline has not built it); cached.</summary>
        public static ZombieTierLibrary Active
        {
            get
            {
                if (!s_probed) { s_active = Resources.Load<ZombieTierLibrary>(ResourcesPath); s_probed = true; }
                return s_active;
            }
        }

        public static void SetActive(ZombieTierLibrary library) { s_active = library; s_probed = true; }
        public static void ResetStatics() { s_active = null; s_probed = false; }
    }
}
