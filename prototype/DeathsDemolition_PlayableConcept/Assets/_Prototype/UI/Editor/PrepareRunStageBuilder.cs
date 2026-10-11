using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DD.Prototype.Rendering;
using DD.Prototype.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// R4 WP-106 P1 · imports AP-040 v001's Prepare Run set and builds the prefab <see cref="PrepareRunStage"/> loads
    /// (<see cref="PrepareRunStage.StageResource"/>). Batch-safe:
    /// <c>Unity -batchmode -quit -projectPath … -executeMethod DD.Prototype.UI.EditorTools.PrepareRunStageBuilder.BuildCli
    /// -ddAp040Export &lt;K&gt;/asset-production/AP-040/exports/v001</c>.
    ///
    ///   1. With <see cref="SourceFlag"/> the eleven files are copied from the export into <see cref="Folder"/> after their SHA-256
    ///      matches the export's SHA256SUMS (without the flag the files must already be there).
    ///   2. Import settings from AP040-integration-v001.json: models scale 1 with file units, normals imported, Mikktspace
    ///      tangents, no animation / cameras / lights / colliders, not readable; BaseColor and Emission sRGB, Normal a normal
    ///      map (OpenGL Y+, not flipped), MaskMap linear with its alpha (smoothness); 2048 clamp, mips, high-quality compression.
    ///   3. Three URP Lit materials on the shared atlas (MaskMap: R metallic, G occlusion, A smoothness): M_AP040_PR_Shared,
    ///      M_AP040_PR_GlowWarm (key lantern, 2800 K) and M_AP040_PR_GlowCool (fill lantern, 6500 K); the FBX material slots
    ///      M_AP040_Shared / M_AP040_Glow are remapped onto them per model.
    ///   4. PR_Stage: the rotatable pedestal at the origin, the backdrop at z −1.3, the lanterns at (∓1.05, 0, −0.4) - the
    ///      json's placements - unpacked; the *_COL proxies, the LOD1 / LOD2 meshes and every LODGroup removed (a close-up
    ///      shows LOD0); every object on <see cref="PrepareRunStage.StageLayer"/>; SOCKET_Weapon checked under SOCKET_Turntable
    ///      at 0.94 m. Saved to <see cref="PrefabPath"/>.
    /// Throws on any failure (the batch run exits non-zero); logs one summary line on success.
    /// </summary>
    public static class PrepareRunStageBuilder
    {
        public const string Folder = "Assets/_Prototype/Art/AP040/PrepareRun";
        public const string ModelsFolder = Folder + "/Models";
        public const string TexturesFolder = Folder + "/Textures";
        public const string MaterialsFolder = Folder + "/Materials";
        public const string ResourcesFolder = Folder + "/Resources/PrepareRun";
        public const string PrefabPath = ResourcesFolder + "/PR_Stage.prefab";

        public const string SourceFlag = "-ddAp040Export";
        private const string LogPrefix = "[UI] Prepare Run stage builder: ";

        public const string PedestalModel = "AP040_display_pedestal_rotatable_V01.fbx";
        public const string BackdropModel = "AP040_backdrop_panel_V01.fbx";
        public const string KeyLanternModel = "AP040_stage_lantern_V01.fbx";
        public const string FillLanternModel = "AP040_stage_lantern_V02.fbx";

        public const string BaseColorTexture = "T_AP040_Shared_BaseColor.png";
        public const string NormalTexture = "T_AP040_Shared_Normal.png";
        public const string MaskMapTexture = "T_AP040_Shared_MaskMap.png";
        public const string EmissionTexture = "T_AP040_Shared_Emission.png";

        public const string SharedMaterial = "M_AP040_PR_Shared";
        public const string GlowWarmMaterial = "M_AP040_PR_GlowWarm";
        public const string GlowCoolMaterial = "M_AP040_PR_GlowCool";

        /// <summary>The FBX's own material slot names (every AP-040 model).</summary>
        public const string SourceSharedSlot = "M_AP040_Shared";
        public const string SourceGlowSlot = "M_AP040_Glow";

        /// <summary>The lanterns' paper glow: linear HDR multiplier on the temperature colour (the look review tunes it).</summary>
        public const float GlowIntensity = 2.0f;

        /// <summary>SOCKET_Weapon's height over the pedestal's base (preview/AP040_PrepareRun_v001.json), with its tolerance.</summary>
        public const float WeaponSocketHeight = 0.94f;
        public const float SocketTolerance = 0.005f;

        /// <summary>export-relative source → folder-relative destination (the export's SHA256SUMS names the sources).</summary>
        private static readonly string[,] s_files =
        {
            { "fbx/" + PedestalModel, "Models/" + PedestalModel },
            { "fbx/" + BackdropModel, "Models/" + BackdropModel },
            { "fbx/" + KeyLanternModel, "Models/" + KeyLanternModel },
            { "fbx/" + FillLanternModel, "Models/" + FillLanternModel },
            { "textures/" + BaseColorTexture, "Textures/" + BaseColorTexture },
            { "textures/" + NormalTexture, "Textures/" + NormalTexture },
            { "textures/" + MaskMapTexture, "Textures/" + MaskMapTexture },
            { "textures/" + EmissionTexture, "Textures/" + EmissionTexture },
            { "preview/AP040_PrepareRun_v001.json", "AP040_PrepareRun_v001.json" },
            { "preview/AP040_rotatable_pedestal_v001.json", "AP040_rotatable_pedestal_v001.json" },
            { "AP040-LICENSES.csv", "AP040-LICENSES.csv" }
        };

        [MenuItem("Tools/Deaths Demolition/UI/Build Prepare Run Stage (AP-040)", false, 130)]
        public static void BuildFromMenu()
        {
            Build(null);
        }

        /// <summary>Batch entry (see the class remarks); <see cref="SourceFlag"/> &lt;dir&gt; optional.</summary>
        public static void BuildCli()
        {
            Build(Arg(SourceFlag));
        }

        /// <summary>Copies (when <paramref name="exportRoot"/> is given), imports and builds; throws on any failure.</summary>
        public static void Build(string exportRoot)
        {
            if (!string.IsNullOrEmpty(exportRoot))
            {
                CopyFromExport(exportRoot);
            }

            for (int i = 0; i < s_files.GetLength(0); i++)
            {
                string path = Folder + "/" + s_files[i, 1];
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException(LogPrefix + path + " missing - run with " + SourceFlag + " <AP-040 exports/v001>.");
                }
            }

            ConfigureTextures();
            Material shared = LitMaterial(SharedMaterial, null, Color.black);
            Material warm = LitMaterial(GlowWarmMaterial, Load<Texture2D>(TexturesFolder + "/" + EmissionTexture), Glow(2800f));
            Material cool = LitMaterial(GlowCoolMaterial, Load<Texture2D>(TexturesFolder + "/" + EmissionTexture), Glow(6500f));
            AssetDatabase.SaveAssets();

            ConfigureModel(PedestalModel, shared, shared);
            ConfigureModel(BackdropModel, shared, shared);
            ConfigureModel(KeyLanternModel, shared, warm);
            ConfigureModel(FillLanternModel, shared, cool);

            int removed = BuildPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + PrefabPath + " built (4 models, 3 materials, " + removed + " proxy / LOD1-2 objects removed).");
        }

        // ------------------------------------------------------------------ 1 · copy

        private static void CopyFromExport(string exportRoot)
        {
            string sums = Path.Combine(exportRoot, "SHA256SUMS");
            if (!File.Exists(sums))
            {
                throw new InvalidOperationException(LogPrefix + "no SHA256SUMS in " + exportRoot);
            }

            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(sums))
            {
                string[] parts = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    expected[parts[1].Trim().TrimStart('*')] = parts[0].Trim().ToLowerInvariant();
                }
            }

            for (int i = 0; i < s_files.GetLength(0); i++)
            {
                string source = Path.Combine(exportRoot, s_files[i, 0]);
                if (!File.Exists(source))
                {
                    throw new InvalidOperationException(LogPrefix + "missing in the export: " + s_files[i, 0]);
                }

                string sha = Sha256(source);
                if (!expected.TryGetValue(s_files[i, 0], out string want) || want != sha)
                {
                    throw new InvalidOperationException(LogPrefix + s_files[i, 0] + " SHA-256 " + sha + " does not match the export's SHA256SUMS.");
                }

                string destination = Folder + "/" + s_files[i, 1];
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (File.Exists(destination) && Sha256(destination) == sha)
                {
                    continue;
                }

                File.Copy(source, destination, true);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        // ------------------------------------------------------------------ 2 · import settings

        private static void ConfigureTextures()
        {
            ConfigureTexture(BaseColorTexture, TextureImporterType.Default, true);
            ConfigureTexture(NormalTexture, TextureImporterType.NormalMap, false);
            ConfigureTexture(MaskMapTexture, TextureImporterType.Default, false);
            ConfigureTexture(EmissionTexture, TextureImporterType.Default, true);
        }

        private static void ConfigureTexture(string file, TextureImporterType type, bool srgb)
        {
            string path = TexturesFolder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(LogPrefix + path + " is not an imported texture.");
            }

            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;   // the MaskMap's smoothness lives in alpha
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Clamp;                     // the 8 × 8 atlas: never sample across a cell
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }

        private static void ConfigureModel(string file, Material shared, Material glow)
        {
            string path = ModelsFolder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(LogPrefix + path + " is not an imported model.");
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), SourceSharedSlot), shared);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), SourceGlowSlot), glow);
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ 3 · materials

        private static Color Glow(float kelvin)
        {
            return Mathf.CorrelatedColorTemperatureToRGB(kelvin) * GlowIntensity;
        }

        /// <summary>A URP Lit material on the shared atlas (created or updated in place, its GUID kept).</summary>
        private static Material LitMaterial(string name, Texture2D emission, Color emissionColor)
        {
            Directory.CreateDirectory(MaterialsFolder);
            string path = MaterialsFolder + "/" + name + ".mat";
            Shader lit = PrototypeShaders.Lit;   // the shader library, never a name lookup (ShaderLibraryTests)
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                material = new Material(lit) { name = name };
            }
            else
            {
                material.shader = lit;
            }

            Texture2D baseMap = Load<Texture2D>(TexturesFolder + "/" + BaseColorTexture);
            Texture2D normal = Load<Texture2D>(TexturesFolder + "/" + NormalTexture);
            Texture2D mask = Load<Texture2D>(TexturesFolder + "/" + MaskMapTexture);

            material.SetFloat("_WorkflowMode", 1f);   // metallic
            material.SetFloat("_Surface", 0f);        // opaque (the atlas' alpha is not coverage)
            material.SetTexture("_BaseMap", baseMap);
            material.SetTexture("_MainTex", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);

            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");

            material.SetTexture("_MetallicGlossMap", mask);   // R metallic, A smoothness
            material.SetFloat("_Smoothness", 1f);             // with a map: the scale on its alpha
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

            material.SetTexture("_OcclusionMap", mask);       // G occlusion
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_OCCLUSIONMAP");

            if (emission != null)
            {
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", emissionColor);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;   // realtime only: nothing is baked on the stage
            }
            else
            {
                material.SetTexture("_EmissionMap", null);
                material.SetColor("_EmissionColor", Color.black);
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        // ------------------------------------------------------------------ 4 · the prefab

        private static int BuildPrefab()
        {
            Directory.CreateDirectory(ResourcesFolder);
            var root = new GameObject("PR_Stage");
            try
            {
                int removed = 0;
                removed += Place(root.transform, PedestalModel, "PR_Pedestal", Vector3.zero);
                removed += Place(root.transform, BackdropModel, "PR_Backdrop", new Vector3(0f, 0f, -1.3f));
                removed += Place(root.transform, KeyLanternModel, "PR_KeyLantern", new Vector3(-1.05f, 0f, -0.4f));
                removed += Place(root.transform, FillLanternModel, "PR_FillLantern", new Vector3(1.05f, 0f, -0.4f));

                Transform turntable = FindDeep(root.transform, PrepareRunStage.TurntableSocket);
                Transform socket = FindDeep(root.transform, PrepareRunStage.WeaponSocket);
                if (turntable == null || socket == null || !socket.IsChildOf(turntable))
                {
                    throw new InvalidOperationException(LogPrefix + PrepareRunStage.WeaponSocket + " under " + PrepareRunStage.TurntableSocket + " not found in " + PedestalModel + ".");
                }

                float height = root.transform.InverseTransformPoint(socket.position).y;
                if (Mathf.Abs(height - WeaponSocketHeight) > SocketTolerance)
                {
                    throw new InvalidOperationException(LogPrefix + PrepareRunStage.WeaponSocket + " at " + height.ToString("F3") + " m, the json says " + WeaponSocketHeight + " m.");
                }

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = PrepareRunStage.StageLayer;
                }

                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException(LogPrefix + PrefabPath + " could not be saved.");
                }

                return removed;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>One model under the stage, unpacked, its proxies, LOD1 / LOD2 meshes and LODGroups removed; returns the objects removed.</summary>
        private static int Place(Transform parent, string file, string name, Vector3 position)
        {
            var model = Load<GameObject>(ModelsFolder + "/" + file);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>(true))
            {
                Object.DestroyImmediate(group);
            }

            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            int removed = 0;
            var doomed = new List<GameObject>();
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (t != instance.transform && (t.name.EndsWith("_COL", StringComparison.Ordinal) || AuthoredWeaponModel.LodIndexOf(t.name) > 0))
                {
                    doomed.Add(t.gameObject);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                if (doomed[i] != null)
                {
                    Object.DestroyImmediate(doomed[i]);
                    removed++;
                }
            }

            return removed;
        }

        // ------------------------------------------------------------------ helpers

        private static T Load<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(LogPrefix + path + " did not load as " + typeof(T).Name + ".");
            }

            return asset;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
