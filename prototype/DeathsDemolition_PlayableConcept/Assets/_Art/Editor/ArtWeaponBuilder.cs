using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Art;
using DD.Prototype.Weapons;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-042B (AP-013 classic weapons) · builds the classic weapon visual library from the
    /// imported package (<c>Assets/_Art/Weapons/Classic</c>):
    ///   1. derived MetallicSmoothness+AO from the packed MRA of the weapons atlas (AP-013 packs
    ///      R metallic / G roughness / B AO — not the AP ORM order, hence its own map);
    ///   2. <c>M_weapons</c> (URP Lit; BaseColor / Normal / MetallicSmoothness);
    ///   3. <c>Resources/DD_ClassicWeapons.asset</c>: per weapon the imported FBX root (LOD0–2
    ///      objects <c>AP013_&lt;key&gt;_LOD&lt;n&gt;</c> + ADR-042-1 sockets) and its tri counts,
    ///      after validating the sockets against <see cref="WeaponSocketContract"/>.
    /// Idempotent. Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtWeaponBuilder.BuildAll</c>.
    /// </summary>
    public static class ArtWeaponBuilder
    {
        public const string LogPrefix = "[ArtWeaponBuilder] ";
        public const string Folder = ArtPipelinePaths.ArtRoot + "/Weapons/Classic";
        public const string Textures = Folder + "/Textures";
        public const string Derived = Folder + "/Derived";
        public const string BaseColorMap = Textures + "/T_weapons_BaseColor.png";
        public const string NormalMap = Textures + "/T_weapons_Normal.png";
        public const string PackedMap = Textures + "/T_weapons_Packed.png";
        public const string MetallicSmoothnessMap = Derived + "/T_weapons_MetallicSmoothness.png";
        public const string MaterialPath = Folder + "/M_weapons.mat";
        public const string ManifestJson = Folder + "/AP013-manifest.json";
        public const string LibraryPath = ArtPipelinePaths.ArtRoot + "/Resources/" + ClassicWeaponVisualLibrary.ResourcePath + ".asset";
        public const string ShaderName = "Universal Render Pipeline/Lit";

        /// <summary>AP-013 MRA → URP mask: out.R = src.R (metallic), out.G = src.B (occlusion), out.B = 0, out.A = 255 − src.G (smoothness).</summary>
        public static readonly Func<Color32, Color32> MraToMetallicSmoothnessAo = p => new Color32(p.r, p.b, 0, (byte)(255 - p.g));
        public const string MraAlgorithm = "MetallicSmoothnessAO-MRA v1 (WP-042B): out.R = src.R (metallic), out.G = src.B (occlusion, URP _OcclusionMap G), out.B = 0, out.A = 255 - src.G (smoothness); 8-bit RGBA PNG, no resampling";

        private static readonly (ClassicWeaponId id, string key, string file)[] Weapons =
        {
            (ClassicWeaponId.AssaultRifle, "A", "AP013_A_rifle_base.fbx"),
            (ClassicWeaponId.AutoSmg, "B", "AP013_B_carbine_base.fbx"),
            (ClassicWeaponId.CombatShotgun, "C", "AP013_C_shotgun_base.fbx"),
        };

        /// <summary>Every FBX this package ships (inventory gate).</summary>
        public static IEnumerable<string> ModelFiles()
        {
            foreach (var w in Weapons) yield return Folder + "/" + w.file;
        }

        /// <summary>Every PNG this package ships (inventory gate): atlas BaseColor / Normal / Packed(MRA) + derived MetallicSmoothness.</summary>
        public static IEnumerable<string> TextureFiles()
        {
            yield return BaseColorMap;
            yield return NormalMap;
            yield return PackedMap;
            yield return MetallicSmoothnessMap;
        }

        [MenuItem("DD/Art/Run Classic Weapons (WP-042B)", priority = 307)]
        public static void BuildAll()
        {
            ReimportModels();
            BuildTextures();
            Material material = BuildMaterial();
            ClassicWeaponVisualLibrary library = BuildLibrary(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "done: " + library.entries.Length + " weapons, library " + LibraryPath);
        }

        /// <summary>The Weapons/Classic axis rule (no bake) entered the contract with this package: re-import where the importer differs.</summary>
        public static void ReimportModels()
        {
            foreach (string path in ModelFiles())
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new FileNotFoundException(LogPrefix + "model importer missing", path);
                if (ArtImportContract.ValidateModel(importer).Count > 0)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    Debug.Log(LogPrefix + "re-imported " + path);
                }
            }
        }

        public static void BuildTextures()
        {
            if (!Directory.Exists(Derived)) Directory.CreateDirectory(Derived);
            ArtTextureRepack.BuildDerived(PackedMap, MetallicSmoothnessMap, Derived + "/T_weapons_MetallicSmoothness.provenance.json",
                MraToMetallicSmoothnessAo, MraAlgorithm);
            AssetDatabase.Refresh();
            // The `weapons` role row (Weapon class) entered the contract with this package: re-apply where the import differs.
            ArtPipelineMenu.ReimportContractTextures();
        }

        public static Material BuildMaterial()
        {
            Shader lit = Shader.Find(ShaderName);
            if (lit == null) throw new InvalidOperationException(LogPrefix + "shader missing: " + ShaderName);
            Material m = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath, lit, "M_weapons");
            ArtCharacterPrefabBuilder.ApplyCharacterMapsPublic(m, Load<Texture2D>(BaseColorMap), Load<Texture2D>(NormalMap), Load<Texture2D>(MetallicSmoothnessMap));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        public static ClassicWeaponVisualLibrary BuildLibrary(Material material)
        {
            var library = AssetDatabase.LoadAssetAtPath<ClassicWeaponVisualLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ClassicWeaponVisualLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.schema = ClassicWeaponVisualLibrary.Schema;
            library.package = "AP-013";
            library.material = material;

            var entries = new List<ClassicWeaponVisualLibrary.Entry>();
            foreach ((ClassicWeaponId id, string key, string file) in Weapons)
            {
                string path = Folder + "/" + file;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new FileNotFoundException(LogPrefix + "FBX missing", path);
                var e = new ClassicWeaponVisualLibrary.Entry { id = id, rosterKey = key, model = model };
                for (int lod = 0; lod < ClassicWeaponVisualLibrary.LodCount; lod++)
                {
                    Transform t = ArtArmorBuilder.FindDeep(model.transform, "AP013_" + key + "_" + LodName(key) + "_LOD" + lod);
                    if (t == null) throw new InvalidOperationException(LogPrefix + path + " has no LOD" + lod + " object");
                    var filter = t.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException(LogPrefix + path + " LOD" + lod + " has no mesh");
                    e.lodTriangles[lod] = filter.sharedMesh.triangles.Length / 3;
                }

                ClassicWeaponDefinition definition = WeaponRoster.Find(id);
                WeaponTypeClass type = definition != null ? definition.TypeClass : WeaponTypeClass.Rifle;
                List<string> faults = WeaponSocketContract.Validate(model.transform, type);
                if (faults.Count > 0) Debug.LogWarning(LogPrefix + key + " socket contract: " + string.Join("; ", faults));
                entries.Add(e);
                Debug.Log(LogPrefix + key + " tris " + string.Join("/", e.lodTriangles) + (faults.Count == 0 ? " sockets ok" : " sockets FAULT"));
            }

            ArtAp021WeaponBuilder.AppendClassics(entries);   // WP-069 B4d: a rebuild keeps the AP-021 classics
            library.entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        /// <summary>The AP-013 object stem per key (A rifle / B carbine / C shotgun).</summary>
        public static string LodName(string key)
        {
            switch (key)
            {
                case "A": return "rifle";
                case "B": return "carbine";
                default: return "shotgun";
            }
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(LogPrefix + typeof(T).Name + " missing", path);
            return asset;
        }
    }
}
