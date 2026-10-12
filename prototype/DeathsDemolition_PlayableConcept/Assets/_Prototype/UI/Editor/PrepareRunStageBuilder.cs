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
    ///   4. PR_Stage: <see cref="PrepareRunStage.Set"/> - the json's Blender placements through the stage's one conversion
    ///      ([−x, z, −y], AD S1: the warm key lantern V01 at x +1.05, the cool fill V02 at x −1.05, the backdrop at z −1.3) - unpacked; the *_COL proxies, the LOD1 / LOD2 meshes and every LODGroup removed (a close-up
    ///      shows LOD0); every object on <see cref="PrepareRunStage.StageLayer"/>; SOCKET_Weapon checked under SOCKET_Turntable
    ///      at 0.94 m. Saved to <see cref="PrefabPath"/>.
    ///   5. C-00 12.10 (two-cap rest for ar_2 / smg_1): the post shim - the turntable LOD0 mesh's +x post cap block (its
    ///      triangles at or above the cap's foot, normals / tangents / UVs kept, the top centre the origin) saved as
    ///      <see cref="ShimMeshPath"/> and placed once, inactive, under SOCKET_Turntable at that cap's top as
    ///      <see cref="PrepareRunStage.ShimName"/> with the shared material; the stage stretches it per weapon.
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
        public const string ShimMeshPath = Folder + "/Meshes/SM_AP040_PR_PostShim.asset";

        /// <summary>The turntable mesh the shim is cut from, and the cap block's foot over SOCKET_Turntable (0.838 − 0.70 m).</summary>
        public const string TurntableMeshName = "SM_display_pedestal_turntable_V01_LOD0";
        public const float CapFootOverTurntable = 0.138f;

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
        public const float GlowIntensity = 1.4f;   // AD-REVIEW-WP106 M2 (12.10): the lantern faces never outshine the weapon (P90 ≤ 215)

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
                // AD S1: the json's Blender placements through the stage's one conversion (PrepareRunStage.BlenderToUnity, [−x, z, −y]).
                for (int i = 0; i < PrepareRunStage.Set.Length; i++)
                {
                    PrepareRunStage.SetPiece piece = PrepareRunStage.Set[i];
                    removed += Place(root.transform, piece.Model, piece.Name, piece.Position);
                }

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

                BuildShim(root.transform, turntable);

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

        /// <summary>
        /// The post shim: the +x post cap block cut from the turntable's LOD0 mesh into SOCKET_Turntable's frame, re-centred on the cap's
        /// top (x, z the block's centre, y its top), saved as a mesh asset (updated in place, its GUID kept) and placed inactive at the
        /// cap's top with the turntable's own material. Throws when the cap is not where the stage measured it (x ±0.22, top 0.894 m).
        /// </summary>
        private static void BuildShim(Transform root, Transform turntable)
        {
            MeshFilter source = null;
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.name == TurntableMeshName) source = f;
            }

            if (source == null || source.sharedMesh == null)
            {
                throw new InvalidOperationException(LogPrefix + TurntableMeshName + " not found for the post shim.");
            }

            Mesh mesh = source.sharedMesh;
            Matrix4x4 toTurntable = turntable.worldToLocalMatrix * source.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            var local = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                local[i] = toTurntable.MultiplyPoint3x4(vertices[i]);
            }

            var keep = new List<int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                bool cap = true;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 v = local[triangles[i + k]];
                    cap &= v.x > 0.1f && v.y >= CapFootOverTurntable - 0.002f;
                }

                if (cap)
                {
                    keep.Add(triangles[i]);
                    keep.Add(triangles[i + 1]);
                    keep.Add(triangles[i + 2]);
                }
            }

            if (keep.Count == 0)
            {
                throw new InvalidOperationException(LogPrefix + "no post cap triangles above " + CapFootOverTurntable + " m in " + TurntableMeshName + ".");
            }

            var remap = new Dictionary<int, int>();
            var outVertices = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outTangents = new List<Vector4>();
            var outUvs = new List<Vector2>();
            var outTriangles = new List<int>(keep.Count);
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < keep.Count; i++)
            {
                if (!remap.TryGetValue(keep[i], out int index))
                {
                    index = outVertices.Count;
                    remap.Add(keep[i], index);
                    Vector3 v = local[keep[i]];
                    outVertices.Add(v);
                    min = Vector3.Min(min, v);
                    max = Vector3.Max(max, v);
                    if (normals.Length == vertices.Length) outNormals.Add(toTurntable.MultiplyVector(normals[keep[i]]).normalized);
                    if (tangents.Length == vertices.Length)
                    {
                        Vector4 tg = tangents[keep[i]];
                        Vector3 axis = toTurntable.MultiplyVector(new Vector3(tg.x, tg.y, tg.z)).normalized;
                        outTangents.Add(new Vector4(axis.x, axis.y, axis.z, tg.w));
                    }

                    if (uvs.Length == vertices.Length) outUvs.Add(uvs[keep[i]]);
                }

                outTriangles.Add(index);
            }

            var top = new Vector3((min.x + max.x) * 0.5f, max.y, (min.z + max.z) * 0.5f);
            float capTop = PrepareRunStage.PostCapTop - turntable.localPosition.y;
            if (Mathf.Abs(top.x - PrepareRunStage.PostOffset) > 0.005f || Mathf.Abs(top.y - capTop) > 0.002f || Mathf.Abs(top.z) > 0.005f)
            {
                throw new InvalidOperationException(LogPrefix + "the post cap's top is at " + top.ToString("F3") + ", the stage measured (" + PrepareRunStage.PostOffset + ", " + capTop + ", 0).");
            }

            for (int i = 0; i < outVertices.Count; i++)
            {
                outVertices[i] -= top;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ShimMeshPath));
            Mesh shim = AssetDatabase.LoadAssetAtPath<Mesh>(ShimMeshPath);
            bool created = shim == null;
            if (created)
            {
                shim = new Mesh();
            }
            else
            {
                shim.Clear();
            }

            shim.name = "SM_AP040_PR_PostShim";
            shim.SetVertices(outVertices);
            if (outNormals.Count == outVertices.Count) shim.SetNormals(outNormals);
            if (outTangents.Count == outVertices.Count) shim.SetTangents(outTangents);
            if (outUvs.Count == outVertices.Count) shim.SetUVs(0, outUvs);
            shim.SetTriangles(outTriangles, 0);
            shim.RecalculateBounds();
            if (created)
            {
                AssetDatabase.CreateAsset(shim, ShimMeshPath);
            }
            else
            {
                EditorUtility.SetDirty(shim);
            }

            AssetDatabase.SaveAssets();

            var go = new GameObject(PrepareRunStage.ShimName, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(turntable, false);
            go.transform.localPosition = top;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = shim;
            go.GetComponent<MeshRenderer>().sharedMaterial = source.GetComponent<MeshRenderer>().sharedMaterial;
            go.SetActive(false);
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
