using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// One-shot project bootstrap: URP assets, quality/graphics wiring, layers, the
    /// PrototypeConfig asset and the Boot scene.
    ///
    /// Written as an editor routine rather than hand-authored YAML so the assets are
    /// produced by Unity's own serializers and cannot drift from the installed
    /// package version. Idempotent: running it twice does not duplicate anything.
    ///
    /// Run from the menu, or headless:
    ///   Unity -batchmode -quit -projectPath ... -executeMethod
    ///   DD.Prototype.Core.EditorTools.PrototypeProjectSetup.RunSetup
    /// </summary>
    public static class PrototypeProjectSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string PipelinePath = SettingsFolder + "/DD_URP_PipelineAsset.asset";
        private const string RendererPath = SettingsFolder + "/DD_URP_Renderer.asset";
        private const string VolumeProfilePath = SettingsFolder + "/DD_GlobalVolumeProfile.asset";

        private const string ConfigFolder = "Assets/_Prototype/Core/Resources";
        private const string ConfigPath = ConfigFolder + "/PrototypeConfig.asset";

        private const string ScenesFolder = "Assets/_Prototype/Scenes";
        private const string BootScenePath = ScenesFolder + "/Boot.unity";

        private static readonly string[] RequiredLayers =
        {
            PrototypeLayers.PlayerName,
            PrototypeLayers.EnemyName,
            PrototypeLayers.EnemyHitboxName,
            PrototypeLayers.AttackHitboxName,
            PrototypeLayers.ProjectileName,
            PrototypeLayers.PickupName,
            PrototypeLayers.EnvironmentName,
            PrototypeLayers.CameraOccluderName,
            PrototypeLayers.RagdollName
        };

        [MenuItem("Tools/Deaths Demolition/Setup Prototype Project", priority = 0)]
        public static void RunSetup()
        {
            Debug.Log("[Setup] Deaths Demolition prototype project setup starting.");

            EnsureFolders();
            EnsureLayers();
            EnsurePlayerSettings();
            UniversalRenderPipelineAsset pipeline = EnsureUrpAssets();
            AssignPipeline(pipeline);
            EnsureVolumeProfile();
            PrototypeConfig config = EnsureConfigAsset();
            EnsureBootScene(config);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Setup] Deaths Demolition prototype project setup complete.");
        }

        // ------------------------------------------------------------------- folders

        private static void EnsureFolders()
        {
            EnsureFolder(SettingsFolder);
            EnsureFolder(ConfigFolder);
            EnsureFolder(ScenesFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // -------------------------------------------------------------------- layers

        private static void EnsureLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[Setup] TagManager.asset not readable; layers were not created.");
                return;
            }

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null)
            {
                return;
            }

            var existing = new HashSet<string>();
            for (int i = 0; i < layers.arraySize; i++)
            {
                string value = layers.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(value))
                {
                    existing.Add(value);
                }
            }

            int next = 8; // 0-7 are Unity's builtin slots.
            foreach (string name in RequiredLayers)
            {
                if (existing.Contains(name))
                {
                    continue;
                }

                while (next < layers.arraySize &&
                       !string.IsNullOrEmpty(layers.GetArrayElementAtIndex(next).stringValue))
                {
                    next++;
                }

                if (next >= layers.arraySize)
                {
                    Debug.LogWarning($"[Setup] No free user layer slot left for '{name}'.");
                    break;
                }

                layers.GetArrayElementAtIndex(next).stringValue = name;
                existing.Add(name);
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();

            // Collision matrix: keep only the interactions the prototype actually needs.
            int projectile = LayerMask.NameToLayer(PrototypeLayers.ProjectileName);
            int pickup = LayerMask.NameToLayer(PrototypeLayers.PickupName);
            int attackHitbox = LayerMask.NameToLayer(PrototypeLayers.AttackHitboxName);
            int enemyHitbox = LayerMask.NameToLayer(PrototypeLayers.EnemyHitboxName);
            int enemy = LayerMask.NameToLayer(PrototypeLayers.EnemyName);
            int player = LayerMask.NameToLayer(PrototypeLayers.PlayerName);

            if (projectile >= 0)
            {
                Physics.IgnoreLayerCollision(projectile, projectile, true);
                if (pickup >= 0) Physics.IgnoreLayerCollision(projectile, pickup, true);
                if (player >= 0) Physics.IgnoreLayerCollision(projectile, player, true);
            }

            if (pickup >= 0)
            {
                Physics.IgnoreLayerCollision(pickup, pickup, true);
                if (enemy >= 0) Physics.IgnoreLayerCollision(pickup, enemy, true);
                if (enemyHitbox >= 0) Physics.IgnoreLayerCollision(pickup, enemyHitbox, true);
            }

            if (attackHitbox >= 0)
            {
                Physics.IgnoreLayerCollision(attackHitbox, attackHitbox, true);
                if (enemy >= 0) Physics.IgnoreLayerCollision(attackHitbox, enemy, true);
                if (enemyHitbox >= 0) Physics.IgnoreLayerCollision(attackHitbox, enemyHitbox, true);
            }

            if (enemyHitbox >= 0)
            {
                Physics.IgnoreLayerCollision(enemyHitbox, enemyHitbox, true);
                if (enemy >= 0) Physics.IgnoreLayerCollision(enemyHitbox, enemy, true);
                if (player >= 0) Physics.IgnoreLayerCollision(enemyHitbox, player, true);
            }
        }

        // ------------------------------------------------------------ player settings

        private static void EnsurePlayerSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "Deaths Demolition";
            PlayerSettings.productName = "Deaths Demolition - Playable Concept";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
        }

        // ----------------------------------------------------------------- URP assets

        private static UniversalRenderPipelineAsset EnsureUrpAssets()
        {
            var existingPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existingPipeline != null)
            {
                ConfigurePipeline(existingPipeline);
                return existingPipeline;
            }

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
                EditorUtility.SetDirty(rendererData);
            }

            UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            ConfigurePipeline(pipeline);
            return pipeline;
        }

        /// <summary>
        /// PROTOTYPE-ONLY starting quality for 1080p / 60 FPS on a desktop GPU.
        /// Shadow distance is short on purpose: the camera sits ~15 m up and ~13 m back,
        /// so nothing beyond ~45 m ever needs a cast shadow, and a 4-cascade split at
        /// that distance keeps the courtyard crisp without paying for a large atlas.
        /// </summary>
        private static void ConfigurePipeline(UniversalRenderPipelineAsset pipeline)
        {
            var so = new SerializedObject(pipeline);

            SetBool(so, "m_SupportsHDR", true);
            SetEnum(so, "m_MSAA", (int)MsaaQuality._4x);
            SetFloat(so, "m_RenderScale", 1f);

            SetBool(so, "m_RequireDepthTexture", true);   // needed by soft particles / fog work later
            SetBool(so, "m_RequireOpaqueTexture", false);

            SetBool(so, "m_MainLightShadowsSupported", true);
            SetEnum(so, "m_MainLightShadowmapResolution", 2048);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);   // the High asset; Cinematic turns them on in QualityTierSetup (C-00 30.09 A)
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 4);

            SetFloat(so, "m_ShadowDistance", 45f);
            SetInt(so, "m_ShadowCascadeCount", 4);
            SetVector3(so, "m_Cascade4Split", new Vector3(0.08f, 0.22f, 0.48f));
            SetFloat(so, "m_CascadeBorder", 0.15f);
            SetBool(so, "m_SoftShadowsSupported", true);

            SetBool(so, "m_UseSRPBatcher", true);
            SetBool(so, "m_SupportsDynamicBatching", false);
            SetEnum(so, "m_ColorGradingMode", (int)ColorGradingMode.LowDynamicRange);
            SetInt(so, "m_ColorGradingLutSize", 32);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        private static void AssignPipeline(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null)
            {
                return;
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;

            int original = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }

            QualitySettings.SetQualityLevel(original, false);
        }

        // -------------------------------------------------------------- volume profile

        private static void EnsureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null)
            {
                return;
            }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            // Hooks only. Every override is left OFF; the atmosphere task fills them in.
            // Distance fog itself is a lighting setting in URP, not a volume component,
            // so it is set on the world scene rather than here.
            AddVolumeComponent<Tonemapping>(profile);
            AddVolumeComponent<ColorAdjustments>(profile);
            AddVolumeComponent<WhiteBalance>(profile);
            AddVolumeComponent<Vignette>(profile);
            AddVolumeComponent<Bloom>(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// profile.Add only creates the instance. Without AddObjectToAsset the component
        /// is not serialised and the profile ends up holding null references.
        /// </summary>
        private static void AddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.Has<T>())
            {
                return;
            }

            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        // ---------------------------------------------------------------- config asset

        private static PrototypeConfig EnsureConfigAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<PrototypeConfig>(ConfigPath);
            if (config != null)
            {
                return config;
            }

            config = ScriptableObject.CreateInstance<PrototypeConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            EditorUtility.SetDirty(config);
            return config;
        }

        // ------------------------------------------------------------------ boot scene

        private static void EnsureBootScene(PrototypeConfig config)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath) == null)
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var cameraGo = new GameObject("Boot Camera");
                var camera = cameraGo.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1f);
                camera.cullingMask = 0;
                camera.orthographic = true;
                cameraGo.AddComponent<AudioListener>();

                var bootGo = new GameObject("Prototype Boot");
                bootGo.AddComponent<PrototypeBootstrapper>();

                EditorSceneManager.SaveScene(scene, BootScenePath);
            }

            EnsureSceneInBuildSettings(BootScenePath);
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].path == scenePath)
                {
                    return;
                }
            }

            var list = new List<EditorBuildSettingsScene>(current.Length + 1)
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            list.AddRange(current);
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ----------------------------------------------------------- define toggle

        [MenuItem("Tools/Deaths Demolition/Toggle PROTOTYPE_DIRECT_TO_RUN", priority = 20)]
        public static void ToggleDirectToRunDefine()
        {
            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var list = new List<string>(defines.Split(';'));
            list.RemoveAll(string.IsNullOrEmpty);

            if (list.Contains(PrototypeBootFlags.DirectToRunDefine))
            {
                list.Remove(PrototypeBootFlags.DirectToRunDefine);
                Debug.Log("[Setup] PROTOTYPE_DIRECT_TO_RUN removed.");
            }
            else
            {
                list.Add(PrototypeBootFlags.DirectToRunDefine);
                Debug.Log("[Setup] PROTOTYPE_DIRECT_TO_RUN added.");
            }

            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", list));
        }

        // ------------------------------------------------------------- serialized util

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        private static void SetEnum(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }

        private static void SetVector3(SerializedObject so, string name, Vector3 value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.vector3Value = value;
        }
    }
}
