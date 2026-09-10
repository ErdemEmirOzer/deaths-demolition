using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.World.EditorTools
{
    /// <summary>
    /// Creates Assets/_Prototype/Scenes/Run.unity and keeps it correct.
    ///
    /// WHAT ENDS UP IN THE SCENE FILE, AND WHY IT IS SO LITTLE
    /// ------------------------------------------------------
    /// Exactly two things: the scene's lighting settings (fog, ambient, sky - which URP
    /// reads from RenderSettings and which therefore cannot live anywhere else), and one
    /// GameObject carrying <see cref="WorldBootstrap"/>. Not one triangle of the map is
    /// serialized.
    ///
    /// That is a deliberate choice about a project with four agents editing in parallel.
    /// A scene holding the built world would be a hundred thousand lines of YAML that no
    /// two people can merge; this one is about eighty lines and conflicts with nothing.
    /// It also side-steps a real bug: the world's meshes and materials are created in
    /// code, so a saved scene referencing them would save dangling references and load as
    /// a field of pink error shaders.
    ///
    /// Idempotent. Run it on an existing Run.unity and it repairs the bootstrap and the
    /// lighting rather than replacing the scene, so anything another task has added to the
    /// Run scene survives.
    ///
    /// Headless:
    ///   Unity -batchmode -quit -projectPath &lt;project&gt; \
    ///     -executeMethod DD.Prototype.World.EditorTools.RunSceneBuilder.BuildRunScene
    /// </summary>
    public static class RunSceneBuilder
    {
        private const string ScenesFolder = "Assets/_Prototype/Scenes";
        private const string RunScenePath = ScenesFolder + "/Run.unity";
        private const string VolumeProfilePath = "Assets/Settings/DD_GlobalVolumeProfile.asset";
        private const string BootstrapName = "World Bootstrap";

        [MenuItem("Tools/Deaths Demolition/Build Run Scene", priority = 10)]
        public static void BuildRunScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ClearPreview();

            Scene scene;
            bool existed = System.IO.File.Exists(RunScenePath);

            if (existed)
            {
                scene = EditorSceneManager.OpenScene(RunScenePath, OpenSceneMode.Single);
            }
            else
            {
                if (!AssetDatabase.IsValidFolder(ScenesFolder))
                {
                    Debug.LogError("[World] Missing folder " + ScenesFolder +
                                   ". Run Tools/Deaths Demolition/Setup Prototype Project first.");
                    return;
                }

                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            // Fog, ambient and sky are scene data. Writing them here is what makes the
            // request's "fog is a lighting setting, not a Volume component" true in the
            // file rather than only at runtime.
            WorldAtmosphere.ApplyEnvironment();

            WorldBootstrap bootstrap = EnsureBootstrap();
            AssignVolumeProfile(bootstrap);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, RunScenePath);

            if (!saved)
            {
                Debug.LogError("[World] Could not save " + RunScenePath);
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RegisterInBuildSettings();

            Debug.Log("[World] " + (existed ? "Updated " : "Created ") + RunScenePath +
                      ". The map itself is built by WorldBootstrap on Awake - press Play, " +
                      "or use Tools/Deaths Demolition/World/Preview In Scene to look at it.");
        }

        // ------------------------------------------------------------------ preview

        [MenuItem("Tools/Deaths Demolition/World/Preview In Scene", priority = 20)]
        public static void PreviewInScene()
        {
            ClearPreview();

            WorldBuilder.Result result = WorldBuilder.Build(LoadVolumeProfile(), true);
            WorldBuilder.MarkTransient(result.Root);

            if (result.Ok)
            {
                Debug.Log("[World] Preview built.\n" + result.Report);
            }
            else
            {
                Debug.LogWarning("[World] Preview built WITH FAILURES.\n" + result.Report);
            }

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Frame(
                    new Bounds(Vector3.zero, new Vector3(WorldLayout.MapSize, 12f, WorldLayout.MapSize)),
                    false);
            }
        }

        [MenuItem("Tools/Deaths Demolition/World/Clear Preview", priority = 21)]
        public static void ClearPreview()
        {
            WorldBuilder.Clear();
        }

        [MenuItem("Tools/Deaths Demolition/World/Validate Topology", priority = 22)]
        public static void ValidateTopology()
        {
            string report = WorldLayout.ValidateConnectivity(out bool ok);

            if (ok)
            {
                Debug.Log(report);
            }
            else
            {
                Debug.LogError(report);
            }
        }

        // ------------------------------------------------------------------ internals

        private static WorldBootstrap EnsureBootstrap()
        {
            WorldBootstrap existing = Object.FindObjectOfType<WorldBootstrap>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(BootstrapName);
            Undo.RegisterCreatedObjectUndo(go, "Create World Bootstrap");
            return go.AddComponent<WorldBootstrap>();
        }

        private static void AssignVolumeProfile(WorldBootstrap bootstrap)
        {
            VolumeProfile profile = LoadVolumeProfile();
            if (profile == null || bootstrap == null)
            {
                return;
            }

            var so = new SerializedObject(bootstrap);
            SerializedProperty property = so.FindProperty("_volumeProfile");
            if (property != null)
            {
                property.objectReferenceValue = profile;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static VolumeProfile LoadVolumeProfile()
        {
            return AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        }

        /// <summary>
        /// Adds Run to the build settings after Boot, without disturbing whatever the
        /// menu-scene task has already registered.
        /// </summary>
        private static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == RunScenePath)
                {
                    if (!scenes[i].enabled)
                    {
                        scenes[i] = new EditorBuildSettingsScene(RunScenePath, true);
                        EditorBuildSettings.scenes = scenes.ToArray();
                    }

                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(RunScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[World] Registered " + RunScenePath + " in the build settings as '" +
                      PrototypeScenes.Run + "'.");
        }
    }
}
