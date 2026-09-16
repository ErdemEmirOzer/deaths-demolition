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
            public string kind;         // top | bottom | head
            public string[] colourways; // ["cw1","cw2"] or ["-"]
            public Variant[] variants;

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
        public string[] fits = { "A", "B", "C" };

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
            if (tierMaterials == null || tierMaterials.Length == 0) return null;
            if (tierIndex < 0 || tierIndex >= tierMaterials.Length || tierMaterials[tierIndex] == null) return tierMaterials[0];
            return tierMaterials[tierIndex];
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

        public bool IsValid => !string.IsNullOrEmpty(fit) && !string.IsNullOrEmpty(top) && !string.IsNullOrEmpty(bottom);

        public string Key => fit + "/" + top + ":" + topColourway + "/" + bottom + ":" + bottomColourway + "/" + head;

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

        public static ZombieLook Select(ZombieModuleLibrary library, uint seed, bool allowMinoCape = true)
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

            look.top = Pick(tops, ref state, id => id == ZombieModuleLibrary.TopMinoCape ? (allowMinoCape ? MinoCapeWeight : 0f) : id == ZombieModuleLibrary.TopBareStraps ? BareStrapsWeight : 1f);
            look.topColourway = Colourway(library.FindModule(look.top), ref state);

            bool strapsTop = look.top == ZombieModuleLibrary.TopBareStraps;
            look.bottom = Pick(bottoms, ref state, id => id == ZombieModuleLibrary.BottomFundoshi ? (strapsTop ? 0f : FundoshiWeight) : 1f);
            look.bottomColourway = Colourway(library.FindModule(look.bottom), ref state);

            // H0 is "no head module"; it is an option alongside the five meshes.
            var headOptions = new List<string>(heads.Count + 1) { ZombieModuleLibrary.HeadNone };
            headOptions.AddRange(heads);
            look.head = Pick(headOptions, ref state, id => id == ZombieModuleLibrary.HeadNone ? BaldWeight : 1f);
            return look;
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

        private static string Colourway(ZombieModuleLibrary.Module module, ref uint state)
        {
            if (module == null || !module.HasColourways) return ZombieModuleLibrary.NoColourway;
            return Next01(ref state) < FadedColourwayChance ? ZombieModuleLibrary.ColourwayFaded : ZombieModuleLibrary.ColourwayDark;
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
