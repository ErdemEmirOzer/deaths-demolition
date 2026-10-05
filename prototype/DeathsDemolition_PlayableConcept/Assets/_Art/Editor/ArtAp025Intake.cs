using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-084 (S-03, C-00 05.10) · the AP-025 v002+ delivery as the character builders read it. tools/art/ap025_intake.py stages an
    /// export into Characters/Zombie and writes AP025-intake-manifest.json; its "builder" section (copied from
    /// tools/art/ap025-intake.json — data, not code) tells the builders the body object names, the face modules, the fit-C FBX
    /// overrides (H2 heavy body: Modules/C_H2, Tier/C_H2) and the O1 skin-atlas maps. Without the manifest every member answers
    /// the AP-007 layout, so the builders behave exactly as before.
    /// </summary>
    public static class ArtAp025Intake
    {
        public const string ManifestPath = ArtCharacterContract.ZombieFolder + "/AP025-intake-manifest.json";
        public const string ContractPath = ArtCharacterContract.ZombieFolder + "/AP025-material-contract.json";
        public const string FaceKind = "face";

        public struct Face
        {
            public string id, name, objectPattern, fbxPattern;
            public string Object(int lod) => objectPattern.Replace("{lod}", lod.ToString());
            public string Fbx(int lod) => ArtCharacterContract.ZombieFolder + "/" + fbxPattern.Replace("{lod}", lod.ToString());
        }

        private static Dictionary<string, object> s_builder;
        private static string s_builderStamp;

        public static bool Active => File.Exists(ManifestPath);

        private static Dictionary<string, object> Builder
        {
            get
            {
                if (!Active) return null;
                string text = File.ReadAllText(ManifestPath, Encoding.UTF8);
                if (s_builder == null || s_builderStamp != text)
                {
                    Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(text), "AP025 intake manifest");
                    s_builder = ArtJson.AsObject(ArtJson.Require(doc, "builder"), "builder");
                    s_builderStamp = text;
                }

                return s_builder;
            }
        }

        /// <summary>The body object name of a fit / LOD inside zombie_body_&lt;fit&gt;.fbx.</summary>
        public static string BodyObject(string fit, int lod)
        {
            Dictionary<string, object> b = Builder;
            string pattern = b != null && b.ContainsKey("bodyObject") ? ArtJson.AsString(b["bodyObject"]) : "zombie_body_{fit}_LOD{lod}";
            return pattern.Replace("{fit}", fit).Replace("{lod}", lod.ToString());
        }

        /// <summary>The FBX a module's fit is read from: the fit override (H2 garments) when the manifest names one, else the module's own FBX.</summary>
        public static string ModuleFbx(string id, string fit, string defaultPath)
        {
            string over = Override(fit, "modulesFbx");
            return over != null ? ArtCharacterContract.ZombieFolder + "/" + over.Replace("{id}", id) : defaultPath;
        }

        /// <summary>The FBX an AP-014 tier part's fit is read from (H2 tier parts for fit C), else the part's own FBX.</summary>
        public static string TierFbx(string defaultPath, string fit)
        {
            string over = Override(fit, "tierFbx");
            return over != null ? ArtCharacterContract.ZombieFolder + "/" + over.Replace("{name}", Path.GetFileNameWithoutExtension(defaultPath)) : defaultPath;
        }

        private static string Override(string fit, string key)
        {
            Dictionary<string, object> b = Builder;
            if (b == null || !b.ContainsKey("fitOverrides")) return null;
            Dictionary<string, object> fits = ArtJson.AsObject(b["fitOverrides"], "fitOverrides");
            if (!fits.ContainsKey(fit)) return null;
            Dictionary<string, object> f = ArtJson.AsObject(fits[fit], "fitOverrides." + fit);
            return f.ContainsKey(key) ? ArtJson.AsString(f[key]) : null;
        }

        public static List<Face> Faces()
        {
            var faces = new List<Face>();
            Dictionary<string, object> b = Builder;
            if (b == null || !b.ContainsKey("faces")) return faces;
            foreach (object o in ArtJson.AsList(b["faces"], "faces"))
            {
                Dictionary<string, object> f = ArtJson.AsObject(o, "face");
                faces.Add(new Face
                {
                    id = ArtJson.AsString(f["id"]), name = ArtJson.AsString(f["name"]),
                    objectPattern = ArtJson.AsString(f["object"]), fbxPattern = ArtJson.AsString(f["fbx"])
                });
            }

            return faces;
        }

        private static Dictionary<string, object> SkinAtlas => Builder != null && Builder.ContainsKey("skinAtlasMaterial") ? ArtJson.AsObject(Builder["skinAtlasMaterial"], "skinAtlasMaterial") : null;

        /// <summary>True when the O1 replacement atlas is staged (ORM map present): the old body Packed / Mask / derived maps are retired.</summary>
        public static bool HasO1Atlas => SkinAtlas != null && File.Exists(ArtCharacterContract.ZombieFolder + "/" + ArtJson.AsString(SkinAtlas["orm"]));

        public static Texture2D Orm => HasO1Atlas ? AssetDatabase.LoadAssetAtPath<Texture2D>(ArtCharacterContract.ZombieFolder + "/" + ArtJson.AsString(SkinAtlas["orm"])) : null;

        public static Texture2DArray MaskArray => SkinAtlas != null ? AssetDatabase.LoadAssetAtPath<Texture2DArray>(ArtCharacterContract.ZombieFolder + "/" + ArtJson.AsString(SkinAtlas["maskArray"])) : null;

        private static Vector4 Vec(string key, Vector4 fallback)
        {
            Dictionary<string, object> s = SkinAtlas;
            if (s == null || !s.ContainsKey(key)) return fallback;
            List<object> l = ArtJson.AsList(s[key], key);
            return new Vector4(ArtJson.AsFloat(l[0]), ArtJson.AsFloat(l[1]), ArtJson.AsFloat(l[2]), ArtJson.AsFloat(l[3]));
        }

        private static Color ContractColor(Dictionary<string, object> palette, string key)
        {
            List<object> c = ArtJson.AsList(ArtJson.Require(palette, key), key);
            return new Color(ArtJson.AsFloat(c[0]), ArtJson.AsFloat(c[1]), ArtJson.AsFloat(c[2]), 1f);   // sRGB colour; Unity linearises Color properties
        }

        /// <summary>
        /// Turn on the AP-025 mask path of a DD/Character/Tier Lit material (shared, no per-zombie clone): the 9-slice mask array, the
        /// mask domain (skin atlas = left half, cloth atlas = right half), the contract palette, and on the skin atlas the single ORM map
        /// plus the version tint's cloth coverage as the sash / rope UV rectangle (the old body TierMask is retired).
        /// </summary>
        public static void ConfigureMasks(Material m, bool skinAtlas)
        {
            Texture2DArray masks = MaskArray;
            if (masks == null) throw new InvalidOperationException("[AP025] mask array missing: " + ArtJson.AsString(SkinAtlas["maskArray"]) + " (import it first)");
            m.SetTexture("_ZombieMaskArray", masks);
            m.SetVector("_ZombieMaskDomain", skinAtlas ? Vec("atlasDomain", new Vector4(0.5f, 1f, 0f, 0f)) : Vec("clothDomain", new Vector4(0.5f, 1f, 0.5f, 0f)));
            m.SetFloat("_ZombieMaskMaxLod", 9f);
            m.SetFloat("_ZombieSkinEnable", skinAtlas ? 1f : 0f);
            m.SetFloat("_ZombieUseOrm", skinAtlas ? 1f : 0f);
            m.SetTexture("_ZombieORM", skinAtlas ? Orm : null);
            m.SetVector("_ZombieTierClothRect", skinAtlas ? Vec("tierClothRectOnSkinAtlas", new Vector4(0.875f, 0.125f, 1f, 0.25f)) : new Vector4(0f, 0f, 1f, 1f));
            if (File.Exists(ContractPath))
            {
                Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ContractPath, Encoding.UTF8)), "material contract");
                Dictionary<string, object> palette = ArtJson.AsObject(ArtJson.Require(doc, "palette_srgb"), "palette_srgb");
                m.SetColor("_ZombieSkinTint0", ContractColor(palette, "skin01"));
                m.SetColor("_ZombieSkinTint1", ContractColor(palette, "skin02"));
                m.SetColor("_ZombieSkinTint2", ContractColor(palette, "skin03"));
                m.SetColor("_ZombieSkinRef", ContractColor(palette, "skin02"));   // Tref #8C9292 — the tone the atlas was painted in
                m.SetColor("_ZombieDirtColor", ContractColor(palette, "dirt"));
                m.SetColor("_ZombieBloodColor", ContractColor(palette, "blood"));
            }

            m.SetFloat("_ZombieMasksOn", 1f);
            m.EnableKeyword("_ZOMBIE_MASKS");
            if (skinAtlas)
            {
                // the ORM path replaces the URP metallic / occlusion maps: unbind them so no old body map stays resident
                m.SetTexture("_MetallicGlossMap", null);
                m.SetTexture("_OcclusionMap", null);
                m.SetTexture("_TierMask", null);
                m.DisableKeyword("_METALLICSPECGLOSSMAP");
                m.DisableKeyword("_OCCLUSIONMAP");
            }

            EditorUtility.SetDirty(m);
        }
    }
}
