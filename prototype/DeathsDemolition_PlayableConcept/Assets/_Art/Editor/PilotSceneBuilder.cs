using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Writes the two pilot scenes and registers the playable one.
    ///
    /// WHAT ENDS UP IN AP001_GoldenSlice.unity
    /// ---------------------------------------
    /// The same shape as Run.unity, for the same reason (see RunSceneBuilder): lighting
    /// settings that only exist in a scene file, one directional light, one Global Volume
    /// and one bootstrap GameObject. Not one of the 823 instances is serialized -
    /// <see cref="PilotSceneBootstrap"/> spawns them on Awake from the layout asset, so the
    /// scene stays a few hundred lines and a layout change never touches it.
    ///
    /// The scene is REWRITTEN every time, not repaired. Unlike Run.unity nobody hand-edits
    /// this file: everything in it is derived from assets the pipeline owns, so the honest
    /// idempotent behaviour is "regenerate from scratch and get byte-identical output".
    ///
    /// The kit shelf (AP001_KitShelf.unity) is the opposite: it DOES hold the eighteen
    /// family prefabs as instances, because its whole purpose is to let a human look at
    /// them side by side in the Scene view without pressing Play. It is never in the
    /// build.
    ///
    /// The bootstrap's fields are private and belong to the runtime assembly. They are
    /// written through SerializedObject rather than setters - exactly what the Inspector
    /// does - so the runtime type keeps its fields private and this tool still needs no
    /// API from it beyond the field names.
    ///
    /// Headless:
    ///   Unity -batchmode -quit -projectPath &lt;project&gt; \
    ///     -executeMethod DD.Art.Pipeline.EditorTools.ArtPipelineMenu.RunAll
    /// </summary>
    public static class PilotSceneBuilder
    {
        private const string LogPrefix = "[ArtPipeline] ";
        private const string VolumeProfilePath = "Assets/Settings/DD_GlobalVolumeProfile.asset";
        private const string BootstrapName = "Pilot Bootstrap";
        private const string SunName = "Sun (pilot)";
        private const string VolumeName = "Global Volume";

        /// <summary>Kit shelf grid: six per row, 8 m apart, rows 10 m apart.</summary>
        private const int ShelfColumns = 6;
        private const float ShelfColumnSpacing = 8f;
        private const float ShelfRowSpacing = 10f;

        // Same numbers WorldAtmosphere.ApplyLighting uses for the Run scene. They are private
        // there; duplicated here so the pilot light is the Run light, value for value.
        private const float SunShadowBias = 0.06f;
        private const float SunShadowNormalBias = 0.5f;
        private const float SunShadowNearPlane = 0.2f;

        // Fallback values, used only if WorldAtmosphere.ApplyEnvironment cannot run (the
        // prototype shader library is missing). They mirror the Run scene's RenderSettings.
        private const float SkyAtmosphereThickness = 1.55f;
        private const float SkyExposure = 1.05f;
        private static readonly Color SkyTint = new Color(0.678f, 0.702f, 0.722f);
        private static readonly Color SkyGround = new Color(0.498f, 0.490f, 0.451f);
        private const float ReflectionIntensity = 0.55f;

        // ------------------------------------------------------------- pilot scene

        /// <summary>
        /// Creates or overwrites <see cref="ArtPipelinePaths.PilotScene"/>. Throws if any
        /// asset the bootstrap needs is missing, because a pilot scene with a null layout
        /// is not a pilot scene - and a batch run must exit non-zero for it.
        /// </summary>
        public static void BuildPilotScene()
        {
            GuardEditMode("Build Pilot Scene");
            ConfirmSceneChange();

            // Resolve every input BEFORE touching the open scene, so a missing asset
            // fails without having thrown away whatever the user had open.
            var missing = new List<string>();
            var layout = Load<GoldenSliceLayoutAsset>(ArtPipelinePaths.LayoutAsset, missing);
            var library = Load<ArtFamilyLibrary>(ArtPipelinePaths.FamilyLibraryAsset, missing);
            var earth = Load<Material>(ArtPipelinePaths.Material("earth"), missing);
            var sakuraPurple = Load<Material>(ArtPipelinePaths.Material("sakura_purple"), missing);
            Mesh groundCollider = LoadGroundColliderMesh(missing);
            VolumeProfile profile = Load<VolumeProfile>(VolumeProfilePath, missing);

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + "Cannot build the pilot scene, missing: " +
                                                    string.Join(", ", missing) +
                                                    ". Run Tools/Deaths Demolition/Art Pipeline/Run All (steps 1-4) first.");
            }

            EnsureFolder(AssetFolderOf(ArtPipelinePaths.PilotScene));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ApplyEnvironment();
            CreateSun();
            CreateVolume(profile);

            var go = new GameObject(BootstrapName);
            PilotSceneBootstrap bootstrap = go.AddComponent<PilotSceneBootstrap>();
            AssignBootstrap(bootstrap, layout, library, earth, sakuraPurple, groundCollider);

            Save(scene, ArtPipelinePaths.PilotScene);

            Debug.Log(LogPrefix + "Wrote " + ArtPipelinePaths.PilotScene + " (sun, volume, '" + BootstrapName +
                      "' -> layout " + Count(layout.entries) + " entries, library " + Count(library.items) +
                      " items). The slice is spawned by PilotSceneBootstrap on Awake.");
        }

        // --------------------------------------------------------------- kit shelf

        /// <summary>
        /// Creates or overwrites <see cref="ArtPipelinePaths.KitShelfScene"/> with every
        /// family prefab as a prefab instance on a 6-column grid, in
        /// <see cref="ArtFamilies.All"/> order so a family always lands on the same slot.
        /// </summary>
        public static void BuildKitShelf()
        {
            GuardEditMode("Build Kit Shelf");
            ConfirmSceneChange();

            var library = AssetDatabase.LoadAssetAtPath<ArtFamilyLibrary>(ArtPipelinePaths.FamilyLibraryAsset);
            if (library == null)
            {
                Debug.LogWarning(LogPrefix + "Family library missing at " + ArtPipelinePaths.FamilyLibraryAsset +
                                 "; kit shelf resolves prefabs by path instead.");
            }

            var prefabs = new GameObject[ArtFamilies.All.Length];
            var missing = new List<string>();
            for (int i = 0; i < ArtFamilies.All.Length; i++)
            {
                string family = ArtFamilies.All[i];
                GameObject prefab = library != null ? library.Find(family) : null;
                if (prefab == null)
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPipelinePaths.FamilyPrefab(family));
                }

                if (prefab == null)
                {
                    missing.Add(family);
                }

                prefabs[i] = prefab;
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + "Cannot build the kit shelf, prefab missing for: " +
                                                    string.Join(", ", missing) +
                                                    ". Run Tools/Deaths Demolition/Art Pipeline/3. Build Family Prefabs first.");
            }

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

            EnsureFolder(AssetFolderOf(ArtPipelinePaths.KitShelfScene));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ApplyEnvironment();
            CreateSun();
            if (profile != null)
            {
                CreateVolume(profile);
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                // A prefab INSTANCE, not a copy: the shelf must follow the prefab when the
                // pipeline regenerates it, and the Inspector must show the blue link.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                instance.transform.position = new Vector3(
                    (i % ShelfColumns) * ShelfColumnSpacing,
                    0f,
                    (i / ShelfColumns) * ShelfRowSpacing);
            }

            Save(scene, ArtPipelinePaths.KitShelfScene);

            Debug.Log(LogPrefix + "Wrote " + ArtPipelinePaths.KitShelfScene + " with " + prefabs.Length +
                      " family prefab instances. Not registered in the build settings on purpose.");
        }

        // ---------------------------------------------------------- build settings

        /// <summary>
        /// Appends the pilot scene to the build settings after whatever is already there
        /// (Boot, MainMenu, Selection, Run, Results -> pilot is the sixth). Never reorders
        /// or removes; running it twice is a no-op.
        /// </summary>
        public static void RegisterInBuildSettings()
        {
            string path = ArtPipelinePaths.PilotScene;
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ArtPipelinePaths.KitShelfScene)
                {
                    // Not ours to remove - but it should never have got there.
                    Debug.LogWarning(LogPrefix + "Kit shelf scene is in the build settings at index " + i +
                                     "; it is an editor-only scene and should be removed by hand.");
                }

                if (scenes[i].path != path)
                {
                    continue;
                }

                if (scenes[i].enabled)
                {
                    Debug.Log(LogPrefix + "Pilot scene already registered at build index " + i + ".");
                    return;
                }

                scenes[i] = new EditorBuildSettingsScene(path, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log(LogPrefix + "Pilot scene re-enabled at build index " + i + ".");
                return;
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(LogPrefix + "Cannot register " + path +
                                                    " - the scene file does not exist. Build the pilot scene first.");
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log(LogPrefix + "Registered " + path + " as build scene #" + scenes.Count + " ('" +
                      ArtPipelinePaths.PilotSceneName + "'); the " + (scenes.Count - 1) +
                      " existing entries are untouched.");
        }

        // ------------------------------------------------------------- environment

        /// <summary>
        /// Fog, ambient, reflection and sky, identical to Run.unity. Uses the Run scene's
        /// own applier so the two files cannot drift; falls back to writing the same
        /// values by hand only if the prototype shader library is not loadable.
        /// </summary>
        private static void ApplyEnvironment()
        {
            ApplyRunEnvironment();

            // Pilot-only override (ADR-011-4 candidate): the Run scene's procedural sky reads
            // as a sunset from any horizontal camera; the pilot uses an overcast gradient
            // whose horizon equals the fog colour. Fog, ambient, reflection and sun are the
            // Run values above, untouched.
            RenderSettings.skybox = ArtSkyBuilder.Build();
        }

        private static void ApplyRunEnvironment()
        {
            try
            {
                WorldAtmosphere.ApplyEnvironment();
                return;
            }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning(LogPrefix + "WorldAtmosphere.ApplyEnvironment failed (" + e.Message +
                                 "); writing the Run scene's RenderSettings values directly.");
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = WorldAtmosphere.FogColor;
            RenderSettings.fogStartDistance = WorldAtmosphere.FogStart;
            RenderSettings.fogEndDistance = WorldAtmosphere.FogEnd;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = WorldAtmosphere.AmbientSky;
            RenderSettings.ambientEquatorColor = WorldAtmosphere.AmbientEquator;
            RenderSettings.ambientGroundColor = WorldAtmosphere.AmbientGround;
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = ReflectionIntensity;

            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null)
            {
                Debug.LogWarning(LogPrefix + "Skybox/Procedural not found; the pilot scene has no skybox material.");
                return;
            }

            // Saved INTO the scene file as an embedded object, exactly like Run.unity's
            // "World/OvercastSky" - no separate .mat asset to keep in sync.
            var sky = new Material(skyShader) { name = "World/OvercastSky" };
            sky.SetFloat("_SunDisk", 0f);
            sky.SetFloat("_AtmosphereThickness", SkyAtmosphereThickness);
            sky.SetColor("_SkyTint", SkyTint);
            sky.SetColor("_GroundColor", SkyGround);
            sky.SetFloat("_Exposure", SkyExposure);
            RenderSettings.skybox = sky;
        }

        private static void CreateSun()
        {
            var go = new GameObject(SunName);
            go.transform.rotation = Quaternion.Euler(
                WorldAtmosphere.SunEuler.x, WorldAtmosphere.SunEuler.y, WorldAtmosphere.SunEuler.z);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = WorldAtmosphere.SunColor;
            light.intensity = WorldAtmosphere.SunIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = WorldAtmosphere.SunShadowStrength;
            light.shadowBias = SunShadowBias;
            light.shadowNormalBias = SunShadowNormalBias;
            light.shadowNearPlane = SunShadowNearPlane;

            // Unlike Run.unity (whose light is created at runtime) this one exists in the
            // file, so the scene's sun reference can be saved too.
            RenderSettings.sun = light;
        }

        private static void CreateVolume(VolumeProfile profile)
        {
            var go = new GameObject(VolumeName);
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        // --------------------------------------------------------------- bootstrap

        private static void AssignBootstrap(PilotSceneBootstrap bootstrap, GoldenSliceLayoutAsset layout,
            ArtFamilyLibrary library, Material earth, Material sakuraPurple, Mesh groundCollider)
        {
            var so = new SerializedObject(bootstrap);
            var problems = new List<string>();

            SetReference(so, "_layout", layout, problems);
            SetReference(so, "_library", library, problems);
            SetReference(so, "_earthMaterial", earth, problems);
            SetReference(so, "_sakuraPurpleMaterial", sakuraPurple, problems);
            SetReference(so, "_groundColliderMesh", groundCollider, problems);
            SetBool(so, "_installRunSystems", true, problems);
            SetBool(so, "_buildHud", true, problems);
            SetBool(so, "_bakeNavMesh", true, problems);
            SetBool(so, "_logReport", true, problems);

            so.ApplyModifiedPropertiesWithoutUndo();

            if (problems.Count > 0)
            {
                // A renamed field would otherwise save a bootstrap that silently builds
                // nothing. Loud is the only acceptable failure mode here.
                throw new InvalidOperationException(LogPrefix + "PilotSceneBootstrap serialized fields do not match " +
                                                    "the signature contract: " + string.Join("; ", problems));
            }
        }

        private static void SetReference(SerializedObject so, string field, UnityEngine.Object value, List<string> problems)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                problems.Add("missing field '" + field + "'");
                return;
            }

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                problems.Add("field '" + field + "' is " + property.propertyType + ", expected ObjectReference");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value, List<string> problems)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                problems.Add("missing field '" + field + "'");
                return;
            }

            if (property.propertyType != SerializedPropertyType.Boolean)
            {
                problems.Add("field '" + field + "' is " + property.propertyType + ", expected Boolean");
                return;
            }

            property.boolValue = value;
        }

        // ------------------------------------------------------------------ assets

        private static int Count<T>(List<T> list)
        {
            return list != null ? list.Count : 0;
        }

        private static T Load<T>(string path, List<string> missing) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                missing.Add(path);
            }

            return asset;
        }

        /// <summary>
        /// The Mesh sub-asset of GoldenSlice_Ground_COLLIDER.fbx (one mesh, two triangles,
        /// per the import study). Materials are not imported for contract models, so the
        /// only representations are the mesh and the root GameObject.
        /// </summary>
        private static Mesh LoadGroundColliderMesh(List<string> missing)
        {
            string path = ArtPipelinePaths.GroundColliderFbx;
            UnityEngine.Object[] representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            if (representations != null)
            {
                for (int i = 0; i < representations.Length; i++)
                {
                    var mesh = representations[i] as Mesh;
                    if (mesh != null)
                    {
                        return mesh;
                    }
                }
            }

            var fallback = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (fallback == null)
            {
                missing.Add(path + " (Mesh sub-asset)");
            }

            return fallback;
        }

        /// <summary>Asset-database folder of an asset path. Always forward slashes, whatever the OS.</summary>
        private static string AssetFolderOf(string assetPath)
        {
            int slash = assetPath.LastIndexOf('/');
            return slash < 0 ? string.Empty : assetPath.Substring(0, slash);
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = AssetFolderOf(folder);
            string leaf = folder.Substring(parent.Length == 0 ? 0 : parent.Length + 1);
            EnsureFolder(parent);

            // AssetDatabase, not Directory: the folder needs a .meta and a GUID before a
            // scene can be saved into it in the same call.
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void Save(Scene scene, string path)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException(LogPrefix + "Could not save " + path);
            }
        }

        // ------------------------------------------------------------------ guards

        private static void GuardEditMode(string what)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + what + " cannot run in Play mode.");
            }
        }

        /// <summary>
        /// Interactive only: give the user the chance to save the open scene. Batch mode
        /// has nobody to ask and must never block on a dialog.
        /// </summary>
        private static void ConfirmSceneChange()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new OperationCanceledException(LogPrefix + "Cancelled: the open scene was not saved.");
            }
        }
    }
}
