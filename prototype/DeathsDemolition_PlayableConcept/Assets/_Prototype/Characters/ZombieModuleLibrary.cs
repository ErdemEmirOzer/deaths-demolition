using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-016 · The AP-007 modular zombie as data (ADR-016-2): three body fits, nineteen
    /// clothing modules (7 tops, 7 bottoms, 5 heads; H0 bald = no head mesh) with their
    /// colourways, each as four LOD meshes skinned to the shared 56-bone <c>ZombieRig</c>.
    /// Every mesh carries the bone-name order of its own bind (module FBX files list only
    /// the bones they weight), so a renderer can be re-pointed to any zombie's skeleton by
    /// name at spawn. Written by the art pipeline from <c>AP007-modules.json</c> + the
    /// imported FBX files; read by <see cref="ZombieSkinnedVisual"/>.
    ///
    /// Materials: one shared atlas material for the crowd (SRP batching; no per-instance
    /// material, no property block) and one variant per enemy version whose tint reaches
    /// only the cloth channel of the AP-007 mask (URP/Lit detail albedo gated by
    /// <c>_DetailMask</c>.a = cloth) — the version-visual parity WP-010 asked for, without
    /// touching skin (Mask.R) and with no emissive (DEC-067).
    /// </summary>
    public sealed class ZombieModuleLibrary : ScriptableObject
    {
        public const string Schema = "dd-zombie-modules-v1";
        public const string HeadNone = "H0_bald_scalp";
        public const string TopMinoCape = "T7_mino_cape";
        public const string TopBareStraps = "T6_bare_straps";
        public const string BottomFundoshi = "B5_fundoshi";
        public const string KindAccessory = "accessory";          // WP-055B · AP-007C: 0–1 per zombie, Head-bound, worn with any head module
        public const string AccessoryStrawHat = "A1_straw_hat";   // occludes the head module; Standard only; never with a helmet / tier head part / T7
        public const string AccessoryFaceWrap = "A2_face_wrap";
        public const string ColourwayDark = "cw1";
        public const string ColourwayFaded = "cw2";
        public const string NoColourway = "-";
        public const int LodCount = 4;

        [Serializable]
        public sealed class MeshRef
        {
            public Mesh mesh;
            /// <summary>Bone names in the mesh's bind order (SkinnedMeshRenderer.bones of the imported FBX).</summary>
            public string[] boneNames;
            public string rootBone;
            public int triangles;
            /// <summary>
            /// WP-038C · Per-LOD material names when this mesh has fewer sub-meshes than the module's <c>materials</c> list
            /// (AP-007C v003: T8 cw1 is cloth-only, the A1 hat LOD3 drops its rope cord) — the trailing entries of the module
            /// list, i.e. the cloth atlas; null = the module list applies.
            /// </summary>
            public string[] materials;
        }

        [Serializable]
        public sealed class Body
        {
            public string fit;          // A | B | C
            public MeshRef[] lods = new MeshRef[LodCount];
        }

        [Serializable]
        public sealed class Variant
        {
            public string colourway;    // cw1 | cw2 | -
            public string fit;          // A | B | C
            public MeshRef[] lods = new MeshRef[LodCount];
        }

        [Serializable]
        public sealed class Module
        {
            public string id;           // T1_kosode_short …
            public string kind;         // top | bottom | head | accessory (WP-055B)
            public string[] colourways; // ["cw1","cw2"] or ["-"]
            /// <summary>WP-055B · AP-007C: the accessory hides the head module beneath it (A1 straw hat); the importer reads it from the manifest.</summary>
            public bool occludesHead;
            public Variant[] variants;
            /// <summary>WP-020 · AP-007 v002: the module's materials in FBX sub-mesh order (<see cref="MaterialAtlas"/> / <see cref="MaterialCloth"/>); v001-style modules list the atlas only.</summary>
            public string[] materials = { MaterialAtlas };

            public Variant Find(string colourway, string fit)
            {
                if (variants == null) return null;
                for (int i = 0; i < variants.Length; i++)
                {
                    if (variants[i].colourway == colourway && variants[i].fit == fit) return variants[i];
                }

                return null;
            }

            public bool HasColourways => colourways != null && colourways.Length > 0 && colourways[0] != NoColourway;
        }

        public string schema = Schema;
        public string package = "AP-007";
        public string version;
        public string sourceSha256;
        public Body[] bodies;
        public Module[] modules;
        [Tooltip("Shared atlas material (Standard); index = EnemyTier (0 Standard, 1 Special, 2 Elite, 3 Ultra).")]
        public Material[] tierMaterials = new Material[4];
        [Tooltip("WP-020 · AP-007 v002 second atlas (cloth): index = EnemyTier; the whole cloth atlas is version-tintable.")]
        public Material[] tierClothMaterials = new Material[4];
        public string[] fits = { "A", "B", "C" };

        public const string MaterialAtlas = "M_Zombie_Atlas";
        public const string MaterialCloth = "M_Zombie_Cloth";
        [NonSerialized] private Dictionary<string, Material[]> _materialSets;

        public Body FindBody(string fit)
        {
            if (bodies == null) return null;
            for (int i = 0; i < bodies.Length; i++) if (bodies[i].fit == fit) return bodies[i];
            return null;
        }

        public Module FindModule(string id)
        {
            if (modules == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < modules.Length; i++) if (modules[i].id == id) return modules[i];
            return null;
        }

        public Material MaterialFor(int tierIndex)
        {
            return Pick(tierMaterials, tierIndex);
        }

        public Material ClothMaterialFor(int tierIndex)
        {
            Material m = Pick(tierClothMaterials, tierIndex);
            return m != null ? m : MaterialFor(tierIndex);   // v001 libraries: no cloth atlas yet
        }

        private static Material Pick(Material[] set, int tierIndex)
        {
            if (set == null || set.Length == 0) return null;
            if (tierIndex < 0 || tierIndex >= set.Length || set[tierIndex] == null) return set[0];
            return set[tierIndex];
        }

        /// <summary>
        /// WP-020 · The shared-material array of a module's sub-mesh order for one enemy version
        /// (cached per pattern × tier: at most 3 patterns × 4 tiers — no per-spawn allocation after warm-up).
        /// </summary>
        public Material[] MaterialsFor(string[] materialNames, int tierIndex)
        {
            if (materialNames == null || materialNames.Length == 0) materialNames = new[] { MaterialAtlas };
            string key = string.Join("|", materialNames) + "#" + tierIndex;
            if (_materialSets == null) _materialSets = new Dictionary<string, Material[]>(StringComparer.Ordinal);
            if (_materialSets.TryGetValue(key, out Material[] set)) return set;
            set = new Material[materialNames.Length];
            for (int i = 0; i < set.Length; i++) set[i] = materialNames[i] == MaterialCloth ? ClothMaterialFor(tierIndex) : MaterialFor(tierIndex);
            _materialSets[key] = set;
            return set;
        }

        /// <summary>Module ids of one kind, in asset order (stable: the selector indexes into it).</summary>
        public List<string> Ids(string kind)
        {
            var list = new List<string>();
            if (modules == null) return list;
            for (int i = 0; i < modules.Length; i++) if (modules[i].kind == kind) list.Add(modules[i].id);
            return list;
        }
    }

    /// <summary>One zombie's look: what <see cref="ZombieLookSelector"/> decides and <see cref="ZombieSkinnedVisual"/> assembles.</summary>
    [Serializable]
    public struct ZombieLook
    {
        public string fit;
        public string top;
        public string topColourway;
        public string bottom;
        public string bottomColourway;
        public string head;      // ZombieModuleLibrary.HeadNone = bald
        /// <summary>WP-055B · AP-007C accessory id, or null / "" = none (a library without the slot never sets it — WP-016/017 keys unchanged).</summary>
        public string accessory;
        public string accessoryColourway;

        public bool IsValid => !string.IsNullOrEmpty(fit) && !string.IsNullOrEmpty(top) && !string.IsNullOrEmpty(bottom);
        public bool HasAccessory => !string.IsNullOrEmpty(accessory);

        public string Key => fit + "/" + top + ":" + topColourway + "/" + bottom + ":" + bottomColourway + "/" + head + (HasAccessory ? "/" + accessory + ":" + accessoryColourway : "");

        public override string ToString() => Key;
    }

    /// <summary>
    /// WP-016 · Deterministic look from a spawn seed (ADR-016-2). Pure: the same seed and
    /// library order give the same look on every machine, so a spawn sequence is
    /// reproducible and the crowd-ratio rule is testable over N seeds:
    ///   cw2 (faded) ≤ 30 % of colourway picks · T7 mino ≤ 10 % · H0 bald ≤ 25 % ·
    ///   T6 bare straps and B5 fundoshi never together (AP-007 README spawner rule).
    /// The weights below are the prototype's translation of those ceilings into per-spawn
    /// probabilities (expected cw2 ≈ 26 %, T7 ≈ 6 %, H0 ≈ 19 %); the numbers are not a
    /// product decision and AP-014 may re-weight per version.
    /// </summary>
    public static class ZombieLookSelector
    {
        public const float FadedColourwayChance = 0.26f;
        public const float MinoCapeWeight = 0.40f;      // vs 1.0 for every other top (7 tops → 0.40 / 6.40 ≈ 6 %)
        public const float BareStrapsWeight = 0.75f;
        public const float FundoshiWeight = 0.70f;
        public const float BaldWeight = 1.15f;          // vs 1.0 for H1–H5 (1.15 / 6.15 ≈ 19 %)

        // Crowd ceilings (README); asserted by the distribution test over 1000 seeds.
        public const float MaxFadedShare = 0.30f;
        public const float MaxMinoShare = 0.10f;
        public const float MaxBaldShare = 0.25f;

        // WP-055B · AP-007C accessory slot (PROTOTYPE-ONLY; only drawn when the library carries the slot):
        public const float StrawHatWeight = 0.20f;      // vs 1.0 for "none" (+ the wrap): ≈ 15 % of eligible Standards (AD band 15–25 %)
        public const float FaceWrapWeight = 0.15f;
        public const float MinStrawHatShare = 0.15f, MaxStrawHatShare = 0.25f;
        /// <summary>WP-055B · AP-011B: the vivid cw1 share of a round's colourway picks must stay at or above this (identity read) — early rounds.</summary>
        public const float MinVividShare = 0.30f;
        /// <summary>
        /// WP-055C (M3 K4): from round 8 the Ö-5 armour bands (per slot 0.40/0.36/0.30 → 0.55/0.50/0.45, user decision) leave
        /// only ~27 % → ~12 % of the Standards unarmoured, so the shipped table can reach at most ≈ 0.19 (R8) → ≈ 0.09 (R14)
        /// vivid cw1 whatever the selector does (S-10 spawn-mix 0.233 at R8, shipped config 0.187). The identity floor is an
        /// EARLY-round rule; from R8 the share is reported (simulator note), not enforced — the lever would be the armour bands.
        /// </summary>
        public const int LateVividRound = 8;
        public static float MinVividShareFor(int round) => round >= LateVividRound ? 0f : MinVividShare;

        public static ZombieLook Select(ZombieModuleLibrary library, uint seed, bool allowMinoCape = true)
        {
            return Select(library, seed, ZombieLookConstraints.None, allowMinoCape);
        }

        /// <summary>
        /// WP-017 · The seed's look under armour constraints (ADR-017-1): excluded modules draw
        /// with weight 0 and the cloth under a Body / Leg piece is forced to cw2. The random
        /// sequence is the unconstrained one, so with empty constraints this is exactly the
        /// WP-016 look, and a constrained look differs only where a rule bites.
        /// </summary>
        public static ZombieLook Select(ZombieModuleLibrary library, uint seed, in ZombieLookConstraints c, bool allowMinoCape = true)
        {
            var look = new ZombieLook();
            if (library == null || library.modules == null || library.bodies == null || library.bodies.Length == 0)
            {
                return look;
            }

            uint state = seed == 0u ? 0x9E3779B9u : seed;
            look.fit = library.bodies[Next(ref state) % (uint)library.bodies.Length].fit;

            List<string> tops = library.Ids("top");
            List<string> bottoms = library.Ids("bottom");
            List<string> heads = library.Ids("head");

            ZombieLookConstraints cc = c;
            look.top = Pick(tops, ref state, id => cc.ExcludesTop(id) ? 0f : id == ZombieModuleLibrary.TopMinoCape ? (allowMinoCape ? MinoCapeWeight : 0f) : id == ZombieModuleLibrary.TopBareStraps ? BareStrapsWeight : 1f);
            look.topColourway = Colourway(library.FindModule(look.top), ref state, cc.fadedTop);

            bool strapsTop = look.top == ZombieModuleLibrary.TopBareStraps;
            look.bottom = Pick(bottoms, ref state, id => cc.ExcludesBottom(id) ? 0f : id == ZombieModuleLibrary.BottomFundoshi ? (strapsTop ? 0f : FundoshiWeight) : 1f);
            look.bottomColourway = Colourway(library.FindModule(look.bottom), ref state, cc.fadedBottom);

            // H0 is "no head module"; it is an option alongside the five meshes.
            var headOptions = new List<string>(heads.Count + 1) { ZombieModuleLibrary.HeadNone };
            headOptions.AddRange(heads);
            look.head = Pick(headOptions, ref state, id => cc.ExcludesHead(id) ? 0f : id == ZombieModuleLibrary.HeadNone ? BaldWeight : 1f);

            // WP-055B · AP-007C accessory: drawn only when the library has the slot, so a library without it keeps the
            // WP-016/017 sequence bit-for-bit. The straw hat never with a mino cape, a helmet or a tier head part.
            List<string> accessories = library.Ids(ZombieModuleLibrary.KindAccessory);
            if (accessories.Count > 0)
            {
                bool mino = look.top == ZombieModuleLibrary.TopMinoCape;
                var accessoryOptions = new List<string>(accessories.Count + 1) { "" };
                accessoryOptions.AddRange(accessories);
                look.accessory = Pick(accessoryOptions, ref state, id => id == "" ? 1f
                    : id == ZombieModuleLibrary.AccessoryStrawHat ? (cc.noHeadAccessory || mino ? 0f : StrawHatWeight)
                    : id == ZombieModuleLibrary.AccessoryFaceWrap ? FaceWrapWeight : 0.1f);
                look.accessoryColourway = look.HasAccessory ? Colourway(library.FindModule(look.accessory), ref state) : null;
            }
            return look;
        }

        /// <summary>WP-055B: true when the look's accessory hides the head module (A1) — the assembler skips the head mesh.</summary>
        public static bool AccessoryOccludesHead(ZombieModuleLibrary library, in ZombieLook look)
        {
            if (library == null || !look.HasAccessory) return false;
            ZombieModuleLibrary.Module m = library.FindModule(look.accessory);
            return m != null && m.occludesHead;
        }

        /// <summary>The same look with another top when the mino cape must go (body armour Present).</summary>
        public static ZombieLook WithoutMinoCape(ZombieModuleLibrary library, in ZombieLook look, uint seed)
        {
            if (look.top != ZombieModuleLibrary.TopMinoCape) return look;
            ZombieLook alt = Select(library, seed ^ 0x5DEECE66u, false);
            ZombieLook result = look;
            result.top = alt.top;
            result.topColourway = alt.topColourway;
            if (result.top == ZombieModuleLibrary.TopBareStraps && result.bottom == ZombieModuleLibrary.BottomFundoshi)
            {
                result.top = alt.bottom == ZombieModuleLibrary.BottomFundoshi ? "T1_kosode_short" : result.top;
            }

            return result;
        }

        private static string Colourway(ZombieModuleLibrary.Module module, ref uint state, bool forceFaded = false)
        {
            if (module == null || !module.HasColourways) return ZombieModuleLibrary.NoColourway;
            bool faded = Next01(ref state) < FadedColourwayChance;   // the draw is consumed either way (sequence parity)
            return faded || forceFaded ? ZombieModuleLibrary.ColourwayFaded : ZombieModuleLibrary.ColourwayDark;
        }

        private static string Pick(List<string> ids, ref uint state, Func<string, float> weight)
        {
            if (ids == null || ids.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < ids.Count; i++) total += Mathf.Max(0f, weight(ids[i]));
            if (total <= 0f) return ids[0];
            float roll = Next01(ref state) * total;
            for (int i = 0; i < ids.Count; i++)
            {
                float w = Mathf.Max(0f, weight(ids[i]));
                if (w <= 0f) continue;
                if (roll < w) return ids[i];
                roll -= w;
            }

            return ids[ids.Count - 1];
        }

        private static uint Next(ref uint state)
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        private static float Next01(ref uint state)
        {
            return (Next(ref state) >> 8) * (1f / 16777216f);
        }
    }
}
