using System;
using System.IO;
using DD.Prototype.Feedback.Vfx;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-047E · AP-013C casings (P-06, credit 0): `_Art/VFX/AP013C/AP013_casings.fbx` (casing_A / casing_B / shell_C, origin =
    /// base centre, +Z = case axis, LOD0/LOD1) + the separate 256 set (BaseColor sRGB, Normal, MRA R metallic / G roughness / B AO).
    /// Builds the derived MetallicSmoothness (R = metallic, A = 1 − roughness), the URP Lit material and the
    /// <see cref="CasingLibrary"/> in Resources (`DD_Casings`). Model import: no materials, axis bake (WP-011 lesson).
    /// </summary>
    public static class ArtCasingBuilder
    {
        private const string LogPrefix = "[ArtPipeline/Casings] ";
        public const string Root = ArtPipelinePaths.ArtRoot + "/VFX/AP013C";
        public const string ModelPath = Root + "/AP013_casings.fbx";
        public const string Textures = Root + "/textures";
        public const string BaseColorMap = Textures + "/T_casings_BaseColor.png";
        public const string NormalMap = Textures + "/T_casings_Normal.png";
        public const string MraSource = Textures + "/T_casings_Packed.png";   // MRA (R metallic, G roughness, B AO) under the Packed role
        public const string MetallicSmoothnessMap = Textures + "/T_casings_MetallicSmoothness.png";
        public const string MaterialPath = Root + "/M_AP013_casings.mat";
        public const string LibraryPath = ArtPipelinePaths.ArtRoot + "/VFX/Resources/" + CasingLibrary.ResourceName + ".asset";
        public const int TextureSize = 256;

        /// <summary>Every PNG this set ships (inventory gate).</summary>
        public static System.Collections.Generic.IEnumerable<string> TextureFiles()
        {
            yield return BaseColorMap;
            yield return NormalMap;
            yield return MraSource;
            yield return MetallicSmoothnessMap;
        }

        [MenuItem("DD/Art/Run Casings (WP-047E)", priority = 331)]
        public static void BuildAll()
        {
            BuildTextures();
            ImportModel();
            Material m = BuildMaterial();
            BuildLibrary(m);
            Debug.Log(LogPrefix + "done");
        }

        public static void BuildTextures()
        {
            // Same MRA packing as the AP-013 weapons atlas (WP-042B) → the same derived map recipe; the import contract
            // (roles BaseColor / Normal / Packed / MetallicSmoothness, class Weapon) sets the importers.
            ArtTextureRepack.BuildDerived(MraSource, MetallicSmoothnessMap, Textures + "/T_casings_MetallicSmoothness.provenance.json",
                ArtWeaponBuilder.MraToMetallicSmoothnessAo, ArtWeaponBuilder.MraAlgorithm);
            AssetDatabase.Refresh();
            ArtPipelineMenu.ReimportContractTextures();
        }

        private static bool Set<T>(T current, T wanted, Action<T> apply)
        {
            if (Equals(current, wanted)) return false;
            apply(wanted);
            return true;
        }

        /// <summary>The import contract owns the model settings (axis bake on: the case is a cylinder, a mirrored Z is harmless); re-apply where it differs.</summary>
        public static void ImportModel()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException(LogPrefix + "model importer missing", ModelPath);
            if (ArtImportContract.ValidateModel(importer).Count > 0)
            {
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            }
        }

        public static Material BuildMaterial()
        {
            Shader lit = Shader.Find(ArtWeaponBuilder.ShaderName);
            if (lit == null) throw new InvalidOperationException(LogPrefix + "shader missing: " + ArtWeaponBuilder.ShaderName);
            Material m = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath, lit, "M_AP013_casings");
            ArtCharacterPrefabBuilder.ApplyCharacterMapsPublic(m,
                AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorMap),
                AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMap),
                AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicSmoothnessMap));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        public static CasingLibrary BuildLibrary(Material material)
        {
            var library = AssetDatabase.LoadAssetAtPath<CasingLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CasingLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.CasingA = FindMesh("AP013_casing_A_LOD0");
            library.CasingB = FindMesh("AP013_casing_B_LOD0");
            library.ShellC = FindMesh("AP013_shell_C_LOD0");
            library.Material = material;
            library.LengthA = 0.056f; library.LengthB = 0.029f; library.LengthC = 0.070f;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            if (!library.IsComplete)
            {
                throw new InvalidOperationException(LogPrefix + "casing meshes missing in " + ModelPath + " (A " + (library.CasingA != null) + ", B " + (library.CasingB != null) + ", C " + (library.ShellC != null) + ")");
            }

            Debug.Log(LogPrefix + "library: A " + library.CasingA.triangles.Length / 3 + " tris, B " + library.CasingB.triangles.Length / 3 + ", C " + library.ShellC.triangles.Length / 3 + " → " + LibraryPath);
            return library;
        }

        private static Mesh FindMesh(string objectName)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (o is Mesh mesh && mesh.name == objectName) return mesh;
            }

            // The importer may name the mesh after the object or its mesh data; accept a containing match.
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (o is Mesh mesh && mesh.name.IndexOf(objectName, StringComparison.OrdinalIgnoreCase) >= 0) return mesh;
            }

            return null;
        }
    }
}
