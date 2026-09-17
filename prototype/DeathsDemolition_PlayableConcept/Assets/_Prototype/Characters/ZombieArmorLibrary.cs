using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-017 · The AP-008 regional armour as data (ADR-017-1 companion): three families
    /// (Helmet = kabuto / jingasa, Body = dō + kote, Leg = haidate + suneate), two variants
    /// each, three visual states (Present / Damaged / Broken remnants), fitted to the three
    /// AP-007 body fits, four LODs. A piece is at most two meshes per (variant, state, fit,
    /// LOD): the main plate (kabuto / dō / haidate) and its sub-mesh (kote sleeves, suneate
    /// shins — none for helmets and remnants), both referencing the imported AP-008 FBX
    /// meshes directly (no merged copies in the repo: a merged-mesh asset measured 23.7 MB of
    /// YAML). A slot is therefore two renderers and a state change two mesh swaps
    /// (<see cref="ArmorVisualBinder"/>). Broken remnants are shared by both variants of a
    /// family (AP-008: cords / straps only).
    ///
    /// One atlas material for the crowd plus one variant per enemy version whose tint reaches
    /// only the lacquer channel (AP-008 Mask.R → URP detail mask alpha), never cords, cloth or
    /// iron (AP-014 hook). The garment exclusion lists come from the AP-008 manifest and drive
    /// <see cref="ZombieLookConstraints"/> at spawn.
    /// </summary>
    public sealed class ZombieArmorLibrary : ScriptableObject
    {
        public const string Schema = "dd-zombie-armor-v1";
        public const int LodCount = ZombieModuleLibrary.LodCount;
        public const string StatePresent = "Present";
        public const string StateDamaged = "Damaged";
        public const string StateBroken = "Broken";

        public const int PartCount = 2;   // main plate, sub-mesh (kote / suneate); index 1 empty for helmets and remnants
        public const int PartMain = 0, PartSub = 1;

        [Serializable]
        public sealed class StateMeshes
        {
            public ZombieModuleLibrary.MeshRef[] main = new ZombieModuleLibrary.MeshRef[LodCount];
            public ZombieModuleLibrary.MeshRef[] sub = new ZombieModuleLibrary.MeshRef[LodCount];

            public ZombieModuleLibrary.MeshRef Part(int part, int lod)
            {
                ZombieModuleLibrary.MeshRef[] lods = part == PartMain ? main : part == PartSub ? sub : null;
                return lods != null && lod >= 0 && lod < lods.Length ? lods[lod] : null;
            }

            public int TrianglesLod0 => (main[0] != null ? main[0].triangles : 0) + (sub[0] != null ? sub[0].triangles : 0);
        }

        [Serializable]
        public sealed class Fit
        {
            public string fit;                                                          // A | B | C
            public StateMeshes present = new StateMeshes();
            public StateMeshes damaged = new StateMeshes();
            public StateMeshes broken = new StateMeshes();

            public StateMeshes Of(ArmorVisualState state)
            {
                switch (state)
                {
                    case ArmorVisualState.Present: return present;
                    case ArmorVisualState.Damaged: return damaged;
                    case ArmorVisualState.Broken: return broken;
                    default: return null;
                }
            }
        }

        [Serializable]
        public sealed class Variant
        {
            public string id;               // K1_jingasa, K2_kabuto, D1_lamellar, D2_plate, L1_haidate_heavy, L2_suneate_heavy
            public string family;           // Helmet | Body | Leg
            public ArmorSlot slot;
            public string anchor;           // anchor_helmet | anchor_torso | anchor_legs
            /// <summary>AP-007 module ids this piece cannot sit over (AP-008 manifest `rules.excluded_modules_by_piece`).</summary>
            public string[] excludedModules = Array.Empty<string>();
            public Fit[] fits;
            public int trianglesLod0;       // worst fit, Present

            public Fit FindFit(string fit)
            {
                if (fits == null) return null;
                for (int i = 0; i < fits.Length; i++) if (fits[i].fit == fit) return fits[i];
                return null;
            }

            public bool Excludes(string moduleId)
            {
                if (excludedModules == null || string.IsNullOrEmpty(moduleId)) return false;
                for (int i = 0; i < excludedModules.Length; i++) if (excludedModules[i] == moduleId) return true;
                return false;
            }
        }

        public string schema = Schema;
        public string package = "AP-008";
        public string version;
        public string sourceSha256;
        public Variant[] variants;
        [Tooltip("Shared atlas material (Standard); index = EnemyTier (0 Standard, 1 Special, 2 Elite, 3 Ultra).")]
        public Material[] tierMaterials = new Material[4];
        public string[] fits = { "A", "B", "C" };

        public Variant Find(string id)
        {
            if (variants == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < variants.Length; i++) if (variants[i].id == id) return variants[i];
            return null;
        }

        /// <summary>Variant ids of one slot, in asset order (stable: the selector indexes into it).</summary>
        public List<string> Ids(ArmorSlot slot)
        {
            var list = new List<string>(2);
            if (variants == null) return list;
            for (int i = 0; i < variants.Length; i++) if (variants[i].slot == slot) list.Add(variants[i].id);
            return list;
        }

        public ZombieModuleLibrary.MeshRef MeshOf(string variantId, string fit, ArmorVisualState state, int part, int lod)
        {
            Variant v = Find(variantId);
            Fit f = v != null ? v.FindFit(fit) : null;
            StateMeshes m = f != null ? f.Of(state) : null;
            return m != null ? m.Part(part, lod) : null;
        }

        public Material MaterialFor(int tierIndex)
        {
            if (tierMaterials == null || tierMaterials.Length == 0) return null;
            if (tierIndex < 0 || tierIndex >= tierMaterials.Length || tierMaterials[tierIndex] == null) return tierMaterials[0];
            return tierMaterials[tierIndex];
        }

        public bool IsReady => variants != null && variants.Length > 0 && tierMaterials != null && tierMaterials.Length > 0 && tierMaterials[0] != null;
    }

    /// <summary>One zombie's armour variants (decided once per spawn; a slot only shows its variant when the gameplay ArmorSet equips it).</summary>
    [Serializable]
    public struct ArmorLook
    {
        public string helmet;
        public string body;
        public string leg;

        public string Of(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return helmet;
                case ArmorSlot.Body: return body;
                case ArmorSlot.Leg: return leg;
                default: return null;
            }
        }

        public string Key => helmet + "/" + body + "/" + leg;
        public override string ToString() => Key;
    }

    /// <summary>
    /// WP-017 · Deterministic armour variant per slot from the spawn seed (independent of the
    /// garment draw so a garment change never re-rolls the armour and vice versa: the seed is
    /// folded with a slot constant). Variants are equally weighted — AP-008 gives no ratio;
    /// AP-014 may re-weight per version.
    /// </summary>
    public static class ArmorLookSelector
    {
        private const uint HelmetSalt = 0x7F4A7C15u, BodySalt = 0x2545F491u, LegSalt = 0x9E3779B9u;

        public static ArmorLook Select(ZombieArmorLibrary library, uint seed)
        {
            var look = new ArmorLook();
            if (library == null || library.variants == null) return look;
            look.helmet = Pick(library.Ids(ArmorSlot.Helmet), seed ^ HelmetSalt);
            look.body = Pick(library.Ids(ArmorSlot.Body), seed ^ BodySalt);
            look.leg = Pick(library.Ids(ArmorSlot.Leg), seed ^ LegSalt);
            return look;
        }

        private static string Pick(List<string> ids, uint state)
        {
            if (ids == null || ids.Count == 0) return null;
            return ids[(int)(Mix(state) % (uint)ids.Count)];
        }

        /// <summary>
        /// Non-linear 32-bit finalizer (lowbias32). The xorshift the garment selector uses is
        /// linear over GF(2): spawn seeds that differ in a few bits gave the same parity on 40
        /// spawns (measured: 2 variant sets in a crowd of 40), so the armour draw hashes instead.
        /// </summary>
        public static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }

    /// <summary>
    /// WP-017 · What the equipped armour forbids or forces on the garment draw (AP-008 manifest
    /// `rules`): excluded module ids per slot plus the readability rule — the cloth under a
    /// Present / Damaged Body or Leg piece is the faded colourway (cw2), the only combination
    /// that reaches the DEC-077 armour contrast in AP-008's measurement. Built by
    /// <see cref="Build"/> from the library and the spawn's armour composition.
    /// </summary>
    public struct ZombieLookConstraints
    {
        public List<string> excludedTops;
        public List<string> excludedBottoms;
        public List<string> excludedHeads;
        public bool fadedTop;
        public bool fadedBottom;
        /// <summary>WP-055B · AP-007C: no head-occluding accessory (A1 straw hat) — any plate is Present / Damaged or the spawn is not a Standard (AP-014 tier head part).</summary>
        public bool noHeadAccessory;

        public bool IsEmpty => (excludedTops == null || excludedTops.Count == 0) && (excludedBottoms == null || excludedBottoms.Count == 0) &&
                               (excludedHeads == null || excludedHeads.Count == 0) && !fadedTop && !fadedBottom && !noHeadAccessory;

        public bool ExcludesTop(string id) => excludedTops != null && excludedTops.Contains(id);
        public bool ExcludesBottom(string id) => excludedBottoms != null && excludedBottoms.Contains(id);
        public bool ExcludesHead(string id) => excludedHeads != null && excludedHeads.Contains(id);

        public static ZombieLookConstraints None => default;

        /// <summary>
        /// Constraints of the armour pieces that are Present / Damaged for this spawn (Broken and Absent constrain nothing).
        /// WP-055B · AP-011B (C-00, DEC-077 readability): ANY armoured spawn wears the faded cloth (cw2) top AND bottom — the
        /// helmet-only spawn included (WP-017 faded only the cloth under a body / leg plate); the Elite / Ultra already read cw2.
        /// The vivid cw1 share of the live crowd is kept ≥ 30 % by the unarmoured draw (SpawnMixSimulator rule).
        /// AP-007C: <paramref name="standardTier"/> false (a tier head part) or a helmet forbids the head-occluding accessory.
        /// </summary>
        public static ZombieLookConstraints Build(ZombieArmorLibrary library, in ArmorLook look, bool helmet, bool body, bool leg, bool standardTier = true)
        {
            var c = new ZombieLookConstraints();
            c.noHeadAccessory = !standardTier || helmet || body || leg;   // C-00: A1 never on an armoured spawn (the manifest names the helmet slot; the simulator rule is "0 A1 + armour")
            if (helmet || body || leg) { c.fadedTop = true; c.fadedBottom = true; }
            if (library == null) return c;
            if (helmet) Add(library.Find(look.helmet), ref c);
            if (body) Add(library.Find(look.body), ref c);
            if (leg) Add(library.Find(look.leg), ref c);
            return c;
        }

        private static void Add(ZombieArmorLibrary.Variant v, ref ZombieLookConstraints c)
        {
            if (v == null || v.excludedModules == null) return;
            for (int i = 0; i < v.excludedModules.Length; i++)
            {
                string id = v.excludedModules[i];
                if (string.IsNullOrEmpty(id)) continue;
                List<string> target = id.StartsWith("T", StringComparison.Ordinal) ? (c.excludedTops ??= new List<string>(4))
                    : id.StartsWith("B", StringComparison.Ordinal) ? (c.excludedBottoms ??= new List<string>(4))
                    : (c.excludedHeads ??= new List<string>(4));
                if (!target.Contains(id)) target.Add(id);
            }
        }
    }
}
