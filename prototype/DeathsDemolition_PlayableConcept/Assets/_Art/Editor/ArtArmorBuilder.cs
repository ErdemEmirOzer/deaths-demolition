using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-017 · AP-008 armour → Unity (ADR-017-1 companion). Reads
    /// <c>Assets/_Art/Characters/Zombie/AP008-armor-manifest.json</c> and the 18 armour FBX files
    /// (Import Contract v1.1: every file carries the ZombieRig + anchors and its meshes are
    /// skinned to the 56 bone names), and writes:
    ///
    ///   Textures/Derived/T_zombie_armor_MetallicSmoothness.png  (ORM → R metal, G AO, A smoothness)
    ///   Textures/Derived/T_zombie_armor_DetailMask.png          (Mask.R lacquer → alpha; AP-014 tints lacquer only)
    ///   M_zombie_armor{,_Special,_Elite,_Ultra}.mat              one atlas material + three version tints
    ///   ZombieArmorLibrary.asset                                 6 variants × 3 fits × (Present / Damaged / Broken) × 4 LODs
    ///
    /// Every library entry references the imported FBX meshes directly: main plate + sub-mesh
    /// (kote / suneate) per (variant, state, fit, LOD), each with its own bone-name order.
    /// (A merged single-mesh variant was built and measured: 96 merged meshes = 23.7 MB of YAML
    /// in the library asset — rejected for repo hygiene; two renderers per slot instead.)
    /// Idempotent: re-running rewrites the same entries (asset GUIDs kept).
    /// </summary>
    public static class ArtArmorBuilder
    {
        private const string LogPrefix = "[ArtArmor] ";

        public const string ArmorFolder = ArtCharacterContract.ZombieFolder + "/Armor";
        public const string ManifestJson = ArtCharacterContract.ZombieFolder + "/AP008-armor-manifest.json";
        public const string TextureRole = "zombie_armor";
        public const string Textures = ArtCharacterPrefabBuilder.ZombieTextures;
        public const string Derived = ArtCharacterPrefabBuilder.ZombieDerived;
        public const string BaseColorMap = Textures + "/T_zombie_armor_BaseColor.png";
        public const string NormalMap = Textures + "/T_zombie_armor_Normal.png";
        public const string PackedMap = Textures + "/T_zombie_armor_Packed.png";
        public const string MaskMap = Textures + "/T_zombie_armor_Mask.png";
        public const string MetallicSmoothnessMap = Derived + "/T_zombie_armor_MetallicSmoothness.png";
        public const string DetailMaskMap = Derived + "/T_zombie_armor_DetailMask.png";
        public const string MaterialPath = ArtCharacterContract.ZombieFolder + "/M_zombie_armor.mat";
        public const string LibraryPath = ArtCharacterContract.ZombieFolder + "/ZombieArmorLibrary.asset";
        public const int ExpectedFbxCount = 18;
        public static readonly string[] Fits = { "A", "B", "C" };
        public static readonly string[] States = { ZombieArmorLibrary.StatePresent, ZombieArmorLibrary.StateDamaged, ZombieArmorLibrary.StateBroken };

        /// <summary>Mask.R (lacquer) → detail-mask alpha; RGB kept for inspection.</summary>
        public static readonly Func<Color32, Color32> LacquerMaskToDetailMask = p => new Color32(p.r, p.g, p.b, p.r);
        public const string LacquerDetailMaskAlgorithm = "DetailMask v2 (WP-017 armour): out.RGB = src.RGB, out.A = src.R (lacquer) — URP _DetailMask gates the version tint to the lacquer only (AP-014 hook); 8-bit RGBA PNG";

        public static string TierMaterialPath(int tier)
        {
            return ArtCharacterContract.ZombieFolder + "/M_zombie_armor" + ArtCharacterPrefabBuilder.TierSuffix[Mathf.Clamp(tier, 0, ArtCharacterPrefabBuilder.TierSuffix.Length - 1)] + ".mat";
        }

        public static bool IsArmorModel(string assetPath)
        {
            return assetPath != null && assetPath.StartsWith(ArmorFolder + "/", StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================== textures

        public static void BuildTextures()
        {
            ArtTextureRepack.BuildDerived(PackedMap, MetallicSmoothnessMap, Derived + "/T_zombie_armor_MetallicSmoothness.provenance.json",
                ArtTextureRepack.OrmToMetallicSmoothnessAo, ArtTextureRepack.OrmAlgorithm);
            ArtTextureRepack.BuildDerived(MaskMap, DetailMaskMap, Derived + "/T_zombie_armor_DetailMask.provenance.json",
                LacquerMaskToDetailMask, LacquerDetailMaskAlgorithm);
        }

        // ================================================================= materials

        public static void BuildMaterials()
        {
            Shader lit = ArtMaterialBuilder.ResolveLitShader();
            var baseMap = Load<Texture2D>(BaseColorMap);
            var normal = Load<Texture2D>(NormalMap);
            var mask = Load<Texture2D>(MetallicSmoothnessMap);
            var detail = Load<Texture2D>(DetailMaskMap);
            for (int tier = 0; tier < ArtCharacterPrefabBuilder.TierSuffix.Length; tier++)
            {
                string path = TierMaterialPath(tier);
                Material m = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(path, lit, Path.GetFileNameWithoutExtension(path));
                ArtMaterialBuilder.ApplyCommonPublic(m);
                ArtCharacterPrefabBuilder.ApplyCharacterMapsPublic(m, baseMap, normal, mask);
                if (tier > 0)
                {
                    m.SetTexture("_DetailMask", detail);
                    m.SetFloat("_DetailAlbedoMapScale", 1f);
                    m.SetFloat("_DetailNormalMapScale", 0f);
                    m.EnableKeyword("_DETAIL_MULX2");
                }
                else
                {
                    m.SetTexture("_DetailMask", null);
                    m.SetTexture("_DetailAlbedoMap", null);
                    m.DisableKeyword("_DETAIL_MULX2");
                }

                EditorUtility.SetDirty(m);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "materials: M_zombie_armor ×4 (version tints through Mask.R lacquer)");
        }

        // =================================================================== library

        public sealed class ManifestPiece
        {
            public string family, slot, variant, state, fbx, anchor, anchorBone, pattern;
            public List<string> subMeshes = new List<string>();
            public Dictionary<string, string> sharedPatterns = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public static List<ManifestPiece> ReadManifest(out Dictionary<string, object> doc, out byte[] bytes)
        {
            if (!File.Exists(ManifestJson)) throw new FileNotFoundException(LogPrefix + "AP008-armor-manifest.json missing", ManifestJson);
            bytes = File.ReadAllBytes(ManifestJson);
            doc = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "manifest");
            var pieces = new List<ManifestPiece>();
            foreach (object o in ArtJson.AsList(doc["families"], "families"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "piece");
                var p = new ManifestPiece
                {
                    family = ArtJson.AsString(r["family"]),
                    slot = ArtJson.AsString(r["slot"]),
                    variant = ArtJson.AsString(r["variant"]),
                    state = ArtJson.AsString(r["state"]),
                    fbx = ArtJson.AsString(r["fbx"]),
                    anchor = ArtJson.AsString(r["anchor"]),
                    anchorBone = ArtJson.AsString(r["anchor_bone"]),
                    pattern = ArtJson.AsString(r["object_name_pattern"])
                };
                foreach (object s in ArtJson.AsList(r["sub_meshes"], "sub_meshes")) p.subMeshes.Add(ArtJson.AsString(s));
                if (r.ContainsKey("shared_sub_meshes"))
                {
                    foreach (KeyValuePair<string, object> kv in ArtJson.AsObject(r["shared_sub_meshes"], "shared_sub_meshes"))
                    {
                        string text = ArtJson.AsString(kv.Value);
                        int cut = text.IndexOf(" (", StringComparison.Ordinal);
                        p.sharedPatterns[kv.Key] = cut > 0 ? text.Substring(0, cut) : text;
                    }
                }

                pieces.Add(p);
            }

            return pieces;
        }

        /// <summary>Excluded AP-007 module ids per variant id from <c>rules.excluded_modules_by_piece</c> ("Leg/L1+L2" keys apply to every listed variant).</summary>
        public static Dictionary<string, List<string>> ReadExclusions(Dictionary<string, object> doc)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, object> rules = ArtJson.AsObject(doc["rules"], "rules");
            foreach (KeyValuePair<string, object> kv in ArtJson.AsObject(rules["excluded_modules_by_piece"], "excluded_modules_by_piece"))
            {
                string key = kv.Key;
                int slash = key.IndexOf('/');
                string variants = slash >= 0 ? key.Substring(slash + 1) : key;
                var ids = new List<string>();
                foreach (object id in ArtJson.AsList(kv.Value, key)) ids.Add(ArtJson.AsString(id));
                foreach (string v in variants.Split('+'))
                {
                    if (!result.TryGetValue(v, out List<string> list)) result[v] = list = new List<string>();
                    foreach (string id in ids) if (!list.Contains(id)) list.Add(id);
                }
            }

            return result;
        }

        /// <summary>Object names of one (piece, fit, lod): the main sub-mesh from the pattern, shared subs from their own patterns.</summary>
        public static List<string> ObjectNames(ManifestPiece p, string fit, int lod)
        {
            var names = new List<string>(p.subMeshes.Count);
            foreach (string sub in p.subMeshes)
            {
                string pattern = p.sharedPatterns.TryGetValue(sub, out string shared) ? shared : p.pattern;
                names.Add(pattern.Replace("<sub>", sub).Replace("<fit>", fit).Replace("<n>", lod.ToString()));
            }

            return names;
        }

        /// <summary>Finds an object by name; the Broken files name their remnants without the manifest's "_any" token (AP-008 export deviation, reported).</summary>
        public static Transform FindObject(GameObject model, string name, out string resolved)
        {
            Transform t = FindDeep(model.transform, name);
            resolved = name;
            if (t == null && name.Contains("_any_"))
            {
                resolved = name.Replace("_any_", "_");
                t = FindDeep(model.transform, resolved);
            }

            return t;
        }

        public static ZombieArmorLibrary BuildLibrary()
        {
            List<ManifestPiece> pieces = ReadManifest(out Dictionary<string, object> doc, out byte[] bytes);
            Dictionary<string, List<string>> exclusions = ReadExclusions(doc);

            var library = AssetDatabase.LoadAssetAtPath<ZombieArmorLibrary>(LibraryPath);
            bool created = library == null;
            if (created)
            {
                library = ScriptableObject.CreateInstance<ZombieArmorLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            // Drop any merged meshes of an earlier build (the library references FBX meshes only).
            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(LibraryPath))
            {
                if (sub is Mesh) UnityEngine.Object.DestroyImmediate(sub, true);
            }

            library.schema = ZombieArmorLibrary.Schema;
            library.package = ArtJson.AsString(doc["package"]);
            library.version = ArtJson.AsString(doc["schema"]);
            library.sourceSha256 = ArtTextureRepack.Sha256Hex(bytes);
            library.fits = Fits;

            // WP-020 · AP-008 v002 `broken_remnants`: the Broken state is per FAMILY — both variants' Broken files
            // carry the same `<family>_any` objects, so every variant references the family's first file (one
            // remnant mesh set per family in the build; the second file stays on disk, unreferenced).
            var canonicalBroken = new Dictionary<string, string>(StringComparer.Ordinal);
            if (doc.ContainsKey("broken_remnants"))
            {
                Dictionary<string, object> br = ArtJson.AsObject(doc["broken_remnants"], "broken_remnants");
                foreach (KeyValuePair<string, object> kv in ArtJson.AsObject(br["files_per_family"], "files_per_family"))
                {
                    List<object> files = ArtJson.AsList(kv.Value, kv.Key);
                    if (files.Count > 0) canonicalBroken[kv.Key] = ArtJson.AsString(files[0]);
                }
            }

            var variants = new Dictionary<string, ZombieArmorLibrary.Variant>(StringComparer.Ordinal);
            var order = new List<string>();
            int referenced = 0, deviations = 0, sharedBroken = 0;
            var models = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (ManifestPiece p in pieces)
            {
                if (p.state == ZombieArmorLibrary.StateBroken && canonicalBroken.TryGetValue(p.family, out string canonical) && Path.GetFileName(p.fbx) != canonical)
                {
                    p.fbx = canonical;
                    sharedBroken++;
                }

                if (!variants.TryGetValue(p.variant, out ZombieArmorLibrary.Variant v))
                {
                    v = new ZombieArmorLibrary.Variant
                    {
                        id = p.variant,
                        family = p.family,
                        slot = ParseSlot(p.slot),
                        anchor = p.anchor,
                        excludedModules = exclusions.TryGetValue(p.variant, out List<string> ex) ? ex.ToArray() : Array.Empty<string>(),
                        fits = new ZombieArmorLibrary.Fit[Fits.Length]
                    };
                    for (int f = 0; f < Fits.Length; f++) v.fits[f] = new ZombieArmorLibrary.Fit { fit = Fits[f] };
                    variants[p.variant] = v;
                    order.Add(p.variant);
                }

                string fbxPath = ArmorFolder + "/" + Path.GetFileName(p.fbx);
                if (!models.TryGetValue(fbxPath, out GameObject model))
                {
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                    if (model == null) throw new FileNotFoundException(LogPrefix + "armour FBX missing", fbxPath);
                    models[fbxPath] = model;
                }

                for (int f = 0; f < Fits.Length; f++)
                {
                    ZombieArmorLibrary.Fit fit = v.fits[f];
                    for (int lod = 0; lod < ZombieArmorLibrary.LodCount; lod++)
                    {
                        var parts = new List<SkinnedMeshRenderer>();
                        foreach (string name in ObjectNames(p, Fits[f], lod))
                        {
                            Transform t = FindObject(model, name, out string resolved);
                            if (t == null) throw new InvalidOperationException(LogPrefix + fbxPath + " has no object '" + name + "' (" + p.variant + " " + p.state + " " + Fits[f] + " LOD" + lod + ")");
                            if (resolved != name) deviations++;
                            var smr = t.GetComponent<SkinnedMeshRenderer>();
                            if (smr == null || smr.sharedMesh == null) throw new InvalidOperationException(LogPrefix + resolved + " in " + fbxPath + " is not a skinned mesh");
                            parts.Add(smr);
                        }

                        if (parts.Count > ZombieArmorLibrary.PartCount) throw new InvalidOperationException(LogPrefix + p.variant + " " + p.state + ": " + parts.Count + " sub-meshes (library carries " + ZombieArmorLibrary.PartCount + ")");
                        ZombieArmorLibrary.StateMeshes target = fit.Of(ParseState(p.state));
                        if (target == null) throw new FormatException(LogPrefix + "unknown state '" + p.state + "' in the manifest");
                        target.main[lod] = MeshRefOf(parts[0]);
                        target.sub[lod] = parts.Count > 1 ? MeshRefOf(parts[1]) : null;
                        referenced += parts.Count;
                        if (p.state == ZombieArmorLibrary.StatePresent && lod == 0) v.trianglesLod0 = Mathf.Max(v.trianglesLod0, target.TrianglesLod0);
                    }
                }
            }

            library.variants = new ZombieArmorLibrary.Variant[order.Count];
            for (int i = 0; i < order.Count; i++) library.variants[i] = variants[order[i]];

            // Materials + tint sub-textures (4×4 flat colour, inside the library asset).
            library.tierMaterials = new Material[ArtCharacterPrefabBuilder.TierSuffix.Length];
            for (int tier = 0; tier < library.tierMaterials.Length; tier++)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(TierMaterialPath(tier));
                if (m == null) throw new InvalidOperationException(LogPrefix + "build the armour materials first: " + TierMaterialPath(tier));
                library.tierMaterials[tier] = m;
                if (tier > 0)
                {
                    Texture2D tint = ArtCharacterPrefabBuilder.TintTexturePublic(library, LibraryPath, "T_zombie_armor_tint" + ArtCharacterPrefabBuilder.TierSuffix[tier], ArtCharacterPrefabBuilder.TierDetailTint[tier]);
                    m.SetTexture("_DetailAlbedoMap", tint);
                    EditorUtility.SetDirty(m);
                }
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "armour library " + (created ? "created" : "updated") + ": " + library.variants.Length + " variants, " + referenced + " mesh references (FBX), name deviations " + deviations + " (Broken '_any' token), shared Broken files " + sharedBroken + ", sha " + library.sourceSha256.Substring(0, 12) + "…");
            return library;
        }

        public static ArmorVisualState ParseState(string text)
        {
            switch (text)
            {
                case ZombieArmorLibrary.StatePresent: return ArmorVisualState.Present;
                case ZombieArmorLibrary.StateDamaged: return ArmorVisualState.Damaged;
                case ZombieArmorLibrary.StateBroken: return ArmorVisualState.Broken;
                default: return ArmorVisualState.Absent;
            }
        }

        public static ArmorSlot ParseSlot(string text)
        {
            string name = text != null && text.StartsWith("ArmorSlot.", StringComparison.Ordinal) ? text.Substring("ArmorSlot.".Length) : text;
            return Enum.TryParse(name, out ArmorSlot slot) ? slot : ArmorSlot.None;
        }

        private static ZombieModuleLibrary.MeshRef MeshRefOf(SkinnedMeshRenderer smr)
        {
            var names = new string[smr.bones.Length];
            for (int i = 0; i < names.Length; i++) names[i] = smr.bones[i] != null ? smr.bones[i].name : string.Empty;
            return new ZombieModuleLibrary.MeshRef
            {
                mesh = smr.sharedMesh,
                boneNames = names,
                rootBone = smr.rootBone != null ? smr.rootBone.name : "Hips",
                triangles = (int)(smr.sharedMesh.GetIndexCount(0) / 3)
            };
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = FindDeep(root.GetChild(i), name);
                if (t != null) return t;
            }

            return null;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(LogPrefix + typeof(T).Name + " missing: " + path, path);
            return asset;
        }
    }
}
