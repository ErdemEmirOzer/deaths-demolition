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
    /// WP-036 (ADR-036-1) · AP-014 tier silhouette parts → Unity (S-03; the S-05 pipeline is not
    /// touched - this builder runs after Run Characters). Steps (batch: <c>-executeMethod
    /// DD.Art.Pipeline.EditorTools.ArtTierBuilder.BuildAll</c>):
    ///   1. derived textures of the tier atlas (MetallicSmoothness+AO from the packed ORM);
    ///   2. materials on <c>DD/Character/Tier Lit</c>: parts atlas ×4, body atlas ×4 (cloth tint
    ///      through AP-007 Mask.G), armour atlas ×4 (lacquer tint through AP-008 Mask.R + lacquer floor)
    ///      - the tint table comes from <c>AP014-manifest.json</c>;
    ///   3. <c>Resources/DD_ZombieTier.asset</c>: 4 parts × 3 fits × 4 LODs of skinned mesh references
    ///      (bone names in bind order) + the tint table + the 12 materials.
    /// The FBX files under <c>Zombie/Tier</c> take the character import contract automatically
    /// (Humanoid avatar from the model; ArtAssetPostprocessor keys on the Characters root).
    /// </summary>
    public static class ArtTierBuilder
    {
        public const string LogPrefix = "[ArtTierBuilder] ";
        public const string TierFolder = ArtCharacterContract.ZombieFolder + "/Tier";
        public const string ManifestJson = ArtCharacterContract.ZombieFolder + "/AP014-manifest.json";
        public const string Textures = ArtCharacterPrefabBuilder.ZombieTextures;
        public const string Derived = ArtCharacterPrefabBuilder.ZombieDerived;
        public const string BaseColorMap = Textures + "/T_zombie_tier_BaseColor.png";
        public const string NormalMap = Textures + "/T_zombie_tier_Normal.png";
        public const string PackedMap = Textures + "/T_zombie_tier_Packed.png";
        public const string MaskMap = Textures + "/T_zombie_tier_Mask.png";
        public const string MetallicSmoothnessMap = Derived + "/T_zombie_tier_MetallicSmoothness.png";
        public const string ShaderName = "DD/Character/Tier Lit";
        public const string LibraryPath = "Assets/_Art/Resources/DD_ZombieTier.asset";
        public static readonly string[] Fits = { "A", "B", "C" };
        public static readonly string[] TierNames = { "Standard", "Special", "Elite", "Ultra" };

        public static string PartMaterialPath(int tier) => ArtCharacterContract.ZombieFolder + "/M_zombie_tier" + ArtCharacterPrefabBuilder.TierSuffix[tier] + ".mat";
        public static string BodyMaterialPath(int tier) => ArtCharacterContract.ZombieFolder + "/M_zombie_atlas_tint" + ArtCharacterPrefabBuilder.TierSuffix[tier] + ".mat";
        public static string ArmorMaterialPath(int tier) => ArtCharacterContract.ZombieFolder + "/M_zombie_armor_tint" + ArtCharacterPrefabBuilder.TierSuffix[tier] + ".mat";

        public static bool IsTierModel(string assetPath) =>
            assetPath != null && assetPath.StartsWith(TierFolder + "/", StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        [MenuItem("DD/Art/Run Tier Parts (WP-036)", priority = 305)]
        public static void BuildAll()
        {
            BuildTextures();
            BuildMaterials();
            ZombieTierLibrary lib = BuildLibrary();
            Debug.Log(LogPrefix + "done: " + lib.parts.Length + " parts, " + lib.tints.Length + " tints, library " + LibraryPath);
        }

        public static void BuildTextures()
        {
            ArtTextureRepack.BuildDerived(PackedMap, MetallicSmoothnessMap, Derived + "/T_zombie_tier_MetallicSmoothness.provenance.json",
                ArtTextureRepack.OrmToMetallicSmoothnessAo, ArtTextureRepack.OrmAlgorithm);
        }

        // ================================================================= manifest

        public sealed class TintRow
        {
            public EnemyTier tier;
            public Color cloth = Color.white; public float clothK;
            public Color lacquer = Color.white; public float lacquerK;
        }

        public sealed class ManifestPart
        {
            public string id, fbx, anchor, pattern, material;
            public EnemyTier tier;
            public List<string> skinBones = new List<string>();
            public List<string> fits = new List<string>();
        }

        public static List<ManifestPart> ReadParts(out Dictionary<string, object> doc, out byte[] bytes)
        {
            if (!File.Exists(ManifestJson)) throw new FileNotFoundException(LogPrefix + "AP014-manifest.json missing", ManifestJson);
            bytes = File.ReadAllBytes(ManifestJson);
            doc = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "manifest");
            var parts = new List<ManifestPart>();
            foreach (object o in ArtJson.AsList(doc["tier_parts"], "tier_parts"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "part");
                var p = new ManifestPart
                {
                    id = ArtJson.AsString(r["part"]),
                    tier = ParseTier(ArtJson.AsString(r["tier"])),
                    fbx = ArtJson.AsString(r["fbx"]),
                    anchor = ArtJson.AsString(r["anchor"]),
                    pattern = ArtJson.AsString(r["object_name_pattern"]),
                    material = r.TryGetValue("material", out object m) ? ArtJson.AsString(m) : "M_Tier_Atlas",
                };
                foreach (object b in ArtJson.AsList(r["skin_bones"], "skin_bones")) p.skinBones.Add(ArtJson.AsString(b));
                foreach (object f in ArtJson.AsList(r["fits"], "fits")) p.fits.Add(ArtJson.AsString(f));
                parts.Add(p);
            }

            return parts;
        }

        public static List<TintRow> ReadTints(Dictionary<string, object> doc)
        {
            var rows = new List<TintRow>();
            Dictionary<string, object> table = ArtJson.AsObject(ArtJson.AsObject(doc["tint"], "tint")["table"], "tint.table");
            for (int t = 0; t < TierNames.Length; t++)
            {
                var row = new TintRow { tier = (EnemyTier)t };
                if (table.TryGetValue(TierNames[t], out object entry))
                {
                    Dictionary<string, object> e = ArtJson.AsObject(entry, TierNames[t]);
                    ReadPair(e, "cloth", ref row.cloth, ref row.clothK);
                    ReadPair(e, "lacquer", ref row.lacquer, ref row.lacquerK);
                }

                rows.Add(row);
            }

            return rows;
        }

        private static void ReadPair(Dictionary<string, object> e, string key, ref Color colour, ref float k)
        {
            if (!e.TryGetValue(key, out object v) || v == null) return;
            List<object> pair = ArtJson.AsList(v, key);
            if (pair.Count < 2) return;
            if (ColorUtility.TryParseHtmlString(ArtJson.AsString(pair[0]), out Color c)) colour = c;
            k = (float)ArtJson.AsDouble(pair[1], key + ".k");
        }

        public static EnemyTier ParseTier(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "special": case "runner": return EnemyTier.Special;
                case "elite": case "guard": return EnemyTier.Elite;
                case "ultra": case "executioner": return EnemyTier.Ultra;
                default: return EnemyTier.Standard;
            }
        }

        // ================================================================= materials

        /// <summary>AP-014 finding: the black lacquer (v ≈ 0.12) is lifted to this value under the lacquer mask before the tint lerp so plum / rust can read.</summary>
        public const float LacquerFloor = 0.38f;

        public static void BuildMaterials()
        {
            Shader tierLit = Shader.Find(ShaderName);
            if (tierLit == null) throw new InvalidOperationException(LogPrefix + "shader missing: " + ShaderName);
            ReadParts(out Dictionary<string, object> doc, out _);
            List<TintRow> tints = ReadTints(doc);

            // parts atlas (AP-014): R lacquer, G cloth
            var partBase = Load<Texture2D>(BaseColorMap); var partNormal = Load<Texture2D>(NormalMap); var partMs = Load<Texture2D>(MetallicSmoothnessMap); var partMask = Load<Texture2D>(MaskMap);
            // body atlas (AP-007): R skin, G cloth → cloth tint only
            var bodyBase = Load<Texture2D>(Textures + "/T_zombie_atlas_BaseColor.png"); var bodyNormal = Load<Texture2D>(Textures + "/T_zombie_atlas_Normal.png");
            bool o1 = ArtAp025Intake.HasO1Atlas;   // WP-084: the O1 atlas retires the body Packed / Mask — ORM + masks instead
            var bodyMs = o1 ? null : Load<Texture2D>(Derived + "/T_zombie_atlas_MetallicSmoothness.png"); var bodyMask = o1 ? null : Load<Texture2D>(Textures + "/T_zombie_atlas_Mask.png");
            // armour atlas (AP-008): R lacquer, G cord → lacquer tint only
            var armBase = Load<Texture2D>(ArtArmorBuilder.BaseColorMap); var armNormal = Load<Texture2D>(ArtArmorBuilder.NormalMap);
            var armMs = Load<Texture2D>(ArtArmorBuilder.MetallicSmoothnessMap); var armMask = Load<Texture2D>(ArtArmorBuilder.MaskMap);

            for (int tier = 0; tier < 4; tier++)
            {
                TintRow row = tints[tier];
                Build(PartMaterialPath(tier), tierLit, partBase, partNormal, partMs, partMask, row, cloth: true, lacquer: true);
                Build(BodyMaterialPath(tier), tierLit, bodyBase, bodyNormal, bodyMs, bodyMask, row, cloth: true, lacquer: false);
                if (o1) ArtAp025Intake.ConfigureMasks(Load<Material>(BodyMaterialPath(tier)), true);
                Build(ArmorMaterialPath(tier), tierLit, armBase, armNormal, armMs, armMask, row, cloth: false, lacquer: true);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "materials: M_zombie_tier ×4, M_zombie_atlas_tint ×4, M_zombie_armor_tint ×4 (" + ShaderName + ")");
        }

        private static void Build(string path, Shader shader, Texture2D baseMap, Texture2D normal, Texture2D ms, Texture2D mask, TintRow row, bool cloth, bool lacquer)
        {
            Material m = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(path, shader, Path.GetFileNameWithoutExtension(path));
            if (m.shader != shader) m.shader = shader;
            ArtMaterialBuilder.ApplyCommonPublic(m);
            ArtCharacterPrefabBuilder.ApplyCharacterMapsPublic(m, baseMap, normal, ms);
            ArtCharacterPrefabBuilder.EnableSpecialEmissionSlot(m);   // WP-076: S-07 shock / gel MPB slot
            m.SetTexture("_DetailMask", null); m.SetTexture("_DetailAlbedoMap", null); m.DisableKeyword("_DETAIL_MULX2"); m.DisableKeyword("_DETAIL_SCALED");
            m.SetTexture("_TierMask", mask);
            float clothK = cloth ? row.clothK : 0f, lacquerK = lacquer ? row.lacquerK : 0f;
            m.SetColor("_TierClothTint", row.cloth); m.SetFloat("_TierClothK", clothK);
            m.SetColor("_TierLacquerTint", row.lacquer); m.SetFloat("_TierLacquerK", lacquerK);
            m.SetFloat("_TierLacquerFloor", lacquerK > 0f ? LacquerFloor : 0f);
            bool on = clothK > 0f || lacquerK > 0f;
            m.SetFloat("_TierTintOn", on ? 1f : 0f);
            if (on) m.EnableKeyword("_TIER_TINT"); else m.DisableKeyword("_TIER_TINT");
            EditorUtility.SetDirty(m);
        }

        // ================================================================== library

        public static ZombieTierLibrary BuildLibrary()
        {
            List<ManifestPart> parts = ReadParts(out Dictionary<string, object> doc, out byte[] bytes);
            List<TintRow> tints = ReadTints(doc);
            string dir = Path.GetDirectoryName(LibraryPath);
            if (!AssetDatabase.IsValidFolder(dir)) throw new DirectoryNotFoundException(LogPrefix + "Resources folder missing: " + dir);
            var library = AssetDatabase.LoadAssetAtPath<ZombieTierLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ZombieTierLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.schema = ZombieTierLibrary.Schema;
            library.package = ArtJson.AsString(doc["package"]);
            library.version = ArtJson.AsString(doc["schema"]);
            library.sourceSha256 = ArtTextureRepack.Sha256Hex(bytes);
            library.fits = Fits;

            var built = new List<ZombieTierLibrary.Part>();
            var models = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            int referenced = 0;
            foreach (ManifestPart p in parts)
            {
                string partFbx = TierFolder + "/" + Path.GetFileName(p.fbx);

                var part = new ZombieTierLibrary.Part
                {
                    id = p.id, tier = p.tier, anchor = p.anchor, skinBones = p.skinBones.ToArray(),
                    materialIndex = p.material == "M_Armor_Atlas" ? 1 : 0,
                    fits = new ZombieTierLibrary.Fit[Fits.Length]
                };
                for (int f = 0; f < Fits.Length; f++)
                {
                    var fit = new ZombieTierLibrary.Fit { fit = Fits[f] };
                    string fbxPath = ArtAp025Intake.TierFbx(partFbx, Fits[f]);   // WP-084: fit C from the H2 tier parts
                    if (!models.TryGetValue(fbxPath, out GameObject model))
                    {
                        model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                        if (model == null) throw new FileNotFoundException(LogPrefix + "tier FBX missing", fbxPath);
                        models[fbxPath] = model;
                    }

                    for (int lod = 0; lod < ZombieTierLibrary.LodCount; lod++)
                    {
                        string name = p.pattern.Replace("<fit>", Fits[f]).Replace("<n>", lod.ToString());
                        Transform t = ArtArmorBuilder.FindDeep(model.transform, name);
                        if (t == null) throw new InvalidOperationException(LogPrefix + fbxPath + " has no object '" + name + "'");
                        var smr = t.GetComponent<SkinnedMeshRenderer>();
                        if (smr == null || smr.sharedMesh == null) throw new InvalidOperationException(LogPrefix + name + " in " + fbxPath + " is not a skinned mesh");
                        fit.lods[lod] = MeshRefOf(smr);
                        referenced++;
                    }

                    part.fits[f] = fit;
                    part.trianglesLod0 = Mathf.Max(part.trianglesLod0, fit.TrianglesLod0);
                }

                built.Add(part);
            }

            library.parts = built.ToArray();
            library.tints = new ZombieTierLibrary.TintEntry[tints.Count];
            for (int i = 0; i < tints.Count; i++)
            {
                library.tints[i] = new ZombieTierLibrary.TintEntry { tier = tints[i].tier, cloth = tints[i].cloth, clothK = tints[i].clothK, lacquer = tints[i].lacquer, lacquerK = tints[i].lacquerK, lacquerFloor = tints[i].lacquerK > 0f ? LacquerFloor : 0f };
            }

            for (int tier = 0; tier < 4; tier++)
            {
                library.partMaterials[tier] = Load<Material>(PartMaterialPath(tier));
                library.bodyMaterials[tier] = Load<Material>(BodyMaterialPath(tier));
                library.armorMaterials[tier] = Load<Material>(ArmorMaterialPath(tier));
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "library: " + built.Count + " parts, " + referenced + " skinned meshes (3 fits × 4 LODs), sha " + library.sourceSha256.Substring(0, 8));
            return library;
        }

        private static ZombieModuleLibrary.MeshRef MeshRefOf(SkinnedMeshRenderer smr)
        {
            var names = new string[smr.bones.Length];
            for (int i = 0; i < names.Length; i++) names[i] = smr.bones[i] != null ? smr.bones[i].name : string.Empty;
            return new ZombieModuleLibrary.MeshRef { mesh = smr.sharedMesh, boneNames = names, rootBone = smr.rootBone != null ? smr.rootBone.name : "Hips", triangles = (int)(smr.sharedMesh.GetIndexCount(0) / 3) };
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(LogPrefix + typeof(T).Name + " missing: " + path, path);
            return asset;
        }
    }
}
