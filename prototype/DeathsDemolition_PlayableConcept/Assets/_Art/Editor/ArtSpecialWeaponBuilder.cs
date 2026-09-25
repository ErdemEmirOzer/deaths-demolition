using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Art;
using DD.Prototype.Weapons;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-037 (AP-014 Special Weapons) · builds the Special Weapon visual library from the
    /// imported package:
    ///   1. derived MetallicSmoothness+AO from the packed ORM of the SW atlas;
    ///   2. <c>M_sw_atlas</c> (URP Lit; BaseColor / Normal / MetallicSmoothness);
    ///   3. <c>Resources/DD_SpecialWeapons.asset</c>: per weapon pickup + hand LOD0–2 meshes
    ///      (rigid, MeshFilter objects <c>sw_&lt;weapon&gt;_&lt;pose&gt;_LOD&lt;n&gt;</c>) and the
    ///      four AP-014 use sheets with their grid / timing from the VFX manifest.
    /// Idempotent. Tri counts are recorded; the budget verdict is reported, not enforced
    /// (AP-014 manifest: pickup LOD1/2 over the 600/200 budget — P-05 item).
    /// </summary>
    public static class ArtSpecialWeaponBuilder
    {
        public const string LogPrefix = "[ArtSpecialWeaponBuilder] ";
        public const string Folder = ArtPipelinePaths.ArtRoot + "/Weapons/Special";
        public const string Textures = Folder + "/Textures";
        public const string Derived = Folder + "/Derived";
        public const string BaseColorMap = Textures + "/T_sw_atlas_BaseColor.png";
        public const string NormalMap = Textures + "/T_sw_atlas_Normal.png";
        public const string PackedMap = Textures + "/T_sw_atlas_Packed.png";
        public const string MetallicSmoothnessMap = Derived + "/T_sw_atlas_MetallicSmoothness.png";
        public const string MaterialPath = Folder + "/M_sw_atlas.mat";
        public const string ManifestJson = Folder + "/AP014-sw-manifest.json";
        public const string VfxManifestJson = Folder + "/AP014-sw-vfx-manifest.json";
        public const string VfxTextures = ArtPipelinePaths.ArtRoot + "/VFX/Textures";
        public const string LibraryPath = ArtPipelinePaths.ArtRoot + "/Resources/" + SpecialWeaponVisualLibrary.ResourcePath + ".asset";
        public const string ShaderName = "Universal Render Pipeline/Lit";

        // WP-069 B4 (S-02): the two DEC-094 Specials wear the old AP-014 bell / loom meshes until AP-021 ships sw_arc / sw_gel.
        private static readonly (SpecialWeaponId id, string key)[] Weapons =
        {
            (SpecialWeaponId.Raiju, "kagura_bell"),
            (SpecialWeaponId.Namekuji, "ofuda_loom"),
        };

        // WP-069 B4: the Tide Jewel left the game; its FBX pair (WP-059C · AP-014D) stays in the inventory until the art owner removes it.
        private static readonly string[] RetiredKeys = { "tide_jewel" };

        /// <summary>Every FBX this package ships (inventory gate): pickup + hand per authored weapon.</summary>
        public static IEnumerable<string> ModelFiles()
        {
            foreach ((SpecialWeaponId _, string key) in Weapons)
            {
                yield return Folder + "/sw_" + key + "_pickup.fbx";
                yield return Folder + "/sw_" + key + "_hand.fbx";
            }

            foreach (string key in RetiredKeys)
            {
                yield return Folder + "/sw_" + key + "_pickup.fbx";
                yield return Folder + "/sw_" + key + "_hand.fbx";
            }
        }

        /// <summary>Every PNG this package ships (inventory gate): the 1K atlas set + derived mask + the four use sheets.</summary>
        public static IEnumerable<string> TextureFiles()
        {
            yield return BaseColorMap;
            yield return NormalMap;
            yield return PackedMap;
            yield return Textures + "/T_sw_atlas_Mask.png";
            yield return MetallicSmoothnessMap;
            foreach (var s in Sheets) yield return VfxTextures + "/" + s.file;
        }

        // WP-073 B6 asset slice (C-00 25.09): the AP-014 Kagura / Ofuda use sheets left with the old Specials — Raiju / Namekuji
        // draw procedurally (ArcSystem / GelSystem). The sheet table stays as the library's (empty) contract.
        private static readonly (string usage, string file, int cols, int rows, float fps, float duration, bool loop, float world)[] Sheets =
            Array.Empty<(string, string, int, int, float, float, bool, float)>();

        [MenuItem("DD/Art/Run Special Weapons (WP-037)", priority = 306)]
        public static void BuildAll()
        {
            BuildTextures();
            BuildMaterial();
            BuildLibrary();
            Debug.Log(LogPrefix + "done");
        }

        public static void BuildTextures()
        {
            if (!Directory.Exists(Derived)) Directory.CreateDirectory(Derived);
            ArtTextureRepack.BuildDerived(PackedMap, MetallicSmoothnessMap, Derived + "/T_sw_atlas_MetallicSmoothness.provenance.json",
                ArtTextureRepack.OrmToMetallicSmoothnessAo, ArtTextureRepack.OrmAlgorithm);
            AssetDatabase.Refresh();
            // The sw_atlas role row and the Sheet role entered the contract with this package; a map imported
            // before that carries the Prop size, so the contract is re-applied where it differs.
            ArtPipelineMenu.ReimportContractTextures();
        }

        public static Material BuildMaterial()
        {
            Shader lit = Shader.Find(ShaderName);
            if (lit == null) throw new InvalidOperationException(LogPrefix + "shader missing: " + ShaderName);
            Material m = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath, lit, "M_sw_atlas");
            ArtCharacterPrefabBuilder.ApplyCharacterMapsPublic(m, Load<Texture2D>(BaseColorMap), Load<Texture2D>(NormalMap), Load<Texture2D>(MetallicSmoothnessMap));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        public static SpecialWeaponVisualLibrary BuildLibrary()
        {
            string dir = Path.GetDirectoryName(LibraryPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) throw new DirectoryNotFoundException(LogPrefix + "Resources folder missing: " + dir);
            var library = AssetDatabase.LoadAssetAtPath<SpecialWeaponVisualLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SpecialWeaponVisualLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.schema = SpecialWeaponVisualLibrary.Schema;
            library.package = "AP-014";
            library.material = Load<Material>(MaterialPath);

            var entries = new List<SpecialWeaponVisualLibrary.Entry>();
            foreach ((SpecialWeaponId id, string key) in Weapons)
            {
                var e = new SpecialWeaponVisualLibrary.Entry { id = id };
                FillLods(Folder + "/sw_" + key + "_pickup.fbx", "sw_" + key + "_pickup_LOD", e.pickupLods, e.pickupTriangles);
                FillLods(Folder + "/sw_" + key + "_hand.fbx", "sw_" + key + "_hand_LOD", e.handLods, e.handTriangles);
                ArtAp021WeaponBuilder.ApplyModel(e);   // WP-069 B4c: a rebuild keeps the AP-021 model (it wins over these meshes)
                entries.Add(e);
                Debug.Log(LogPrefix + key + " pickup tris " + string.Join("/", e.pickupTriangles) + " hand tris " + string.Join("/", e.handTriangles));
            }

            library.entries = entries.ToArray();

            var sheets = new List<SpecialWeaponVisualLibrary.Sheet>();
            foreach (var s in Sheets)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(VfxTextures + "/" + s.file);
                if (tex == null) throw new FileNotFoundException(LogPrefix + "VFX sheet missing", s.file);
                sheets.Add(new SpecialWeaponVisualLibrary.Sheet
                {
                    usage = s.usage, texture = tex, columns = s.cols, rows = s.rows, fps = s.fps,
                    durationSeconds = s.duration, loop = s.loop, worldSizeMetres = s.world
                });
            }

            library.sheets = sheets.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static void FillLods(string fbxPath, string prefix, Mesh[] lods, int[] triangles)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new FileNotFoundException(LogPrefix + "FBX missing", fbxPath);
            for (int lod = 0; lod < SpecialWeaponVisualLibrary.LodCount; lod++)
            {
                Transform t = ArtArmorBuilder.FindDeep(model.transform, prefix + lod);
                if (t == null) throw new InvalidOperationException(LogPrefix + fbxPath + " has no object '" + prefix + lod + "'");
                var filter = t.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    mesh = smr != null ? smr.sharedMesh : null;
                }

                if (mesh == null) throw new InvalidOperationException(LogPrefix + prefix + lod + " in " + fbxPath + " has no mesh");
                lods[lod] = mesh;
                triangles[lod] = mesh.triangles.Length / 3;
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
