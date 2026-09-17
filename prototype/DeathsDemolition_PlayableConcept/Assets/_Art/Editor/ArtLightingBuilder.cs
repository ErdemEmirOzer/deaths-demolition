using System;
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
    /// WP-012 · Closes ADR-011-4: one overcast lighting base for every scene (DEC-068).
    ///
    ///   Assets/_Art/Lighting/DD_Overcast_v1.asset      the Global Volume profile (post-fx values
    ///                                                  are WorldAtmosphere.PostFx, written by
    ///                                                  WorldAtmosphere.ApplyPostFx — the same code
    ///                                                  path the Run scene used for its own profile)
    ///   Assets/_Art/Resources/DD_Sky_Overcast_v1.mat   the sky (ArtSkyBuilder gradient)
    ///
    /// <see cref="AlignRunScene"/> points Run.unity at both: the WorldBootstrap profile
    /// reference and the scene's skybox. The procedural sky the Run scene used to build at
    /// runtime read as a sunset at a horizontal camera (WP-011 finding); WorldAtmosphere now
    /// loads the shared sky by name and only falls back to the procedural one when the
    /// asset is absent, so the Player and the Editor agree.
    /// </summary>
    public static class ArtLightingBuilder
    {
        public const string RunScenePath = "Assets/_Prototype/Scenes/Run.unity";
        private const string LogPrefix = "[ArtLighting] ";

        public static void BuildAll()
        {
            BuildOvercastProfile();
            ArtSkyBuilder.Build();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Creates or refreshes the shared profile with the Run scene's post-fx values. GUID stable.</summary>
        public static VolumeProfile BuildOvercastProfile()
        {
            string path = ArtLibraryPaths.OvercastVolumeProfile;
            EnsureFolder(ArtLibraryPaths.LightingFolder);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            bool created = profile == null;
            if (created)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // Same values as Assets/Settings/DD_GlobalVolumeProfile.asset (WorldAtmosphere.PostFx),
            // written through the same method, so the two profiles cannot drift.
            WorldAtmosphere.ApplyPostFx(profile);
            // WP-029 (ADR-029-1): the look recipe as a user LUT on top of the WP-013 grade — the
            // Lightroom recipe was applied to frames rendered WITH that grade, so nothing is
            // applied twice. Absent LUT (before the user picked a recipe) = no Color Lookup override.
            ApplyLookLut(profile);
            // profile.Add only creates the instance; without AddObjectToAsset the component is
            // not serialised (same rule PrototypeProjectSetup follows for the Run profile).
            for (int i = 0; i < profile.components.Count; i++)
            {
                VolumeComponent c = profile.components[i];
                // IsSubAsset is false for hidden sub-assets; the asset path is the reliable test.
                if (c != null && AssetDatabase.GetAssetPath(c) != path)
                {
                    c.name = c.GetType().Name;
                    c.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "Overcast profile " + (created ? "created" : "updated") + ": " + path + " (" +
                      profile.components.Count + " components)");
            return profile;
        }

        /// <summary>WP-029 · Color Lookup override: DD_Look_v1.png at full contribution when it exists, otherwise the override is removed.</summary>
        public static bool ApplyLookLut(VolumeProfile profile)
        {
            var lut = AssetDatabase.LoadAssetAtPath<Texture2D>(LutBuilder.Look);
            if (!profile.TryGet(out UnityEngine.Rendering.Universal.ColorLookup lookup))
            {
                if (lut == null) return false;
                lookup = profile.Add<UnityEngine.Rendering.Universal.ColorLookup>();
            }

            lookup.texture.overrideState = true;
            lookup.texture.value = lut;
            lookup.contribution.overrideState = true;
            lookup.contribution.value = lut != null ? 1f : 0f;
            lookup.active = lut != null;
            EditorUtility.SetDirty(lookup);
            Debug.Log(LogPrefix + "Color Lookup " + (lut != null ? "= " + LutBuilder.Look + " (contribution 1)" : "inactive (no DD_Look_v1.png yet)"));
            return lut != null;
        }

        /// <summary>
        /// Opens Run.unity, assigns the shared profile to its WorldBootstrap and the shared
        /// sky to its RenderSettings, saves. Nothing else in the scene is touched; the
        /// procedural world itself is built at runtime by WorldBootstrap as before.
        /// </summary>
        public static void AlignRunScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "AlignRunScene cannot run in Play mode.");
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtLibraryPaths.OvercastVolumeProfile);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(ArtLibraryPaths.SkyMaterial);
            if (profile == null || sky == null)
            {
                throw new InvalidOperationException(LogPrefix + "Build the lighting assets first (profile " + (profile != null) +
                                                    ", sky " + (sky != null) + ").");
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new InvalidOperationException(LogPrefix + "Cancelled: the open scene has unsaved changes.");
            }

            Scene scene = EditorSceneManager.OpenScene(RunScenePath, OpenSceneMode.Single);
            WorldBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<WorldBootstrap>(true);
            if (bootstrap == null)
            {
                throw new InvalidOperationException(LogPrefix + "Run.unity has no WorldBootstrap.");
            }

            var so = new SerializedObject(bootstrap);
            SerializedProperty property = so.FindProperty("_volumeProfile");
            if (property == null)
            {
                throw new InvalidOperationException(LogPrefix + "WorldBootstrap has no _volumeProfile field.");
            }

            bool changed = property.objectReferenceValue != profile;
            property.objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();

            Material before = RenderSettings.skybox;
            if (before != sky)
            {
                RenderSettings.skybox = sky;
                changed = true;
                // The old sky was a material object embedded in the scene file; drop it so
                // the saved scene does not keep an orphan.
                if (before != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(before)))
                {
                    UnityEngine.Object.DestroyImmediate(before);
                }
            }

            // Every Volume already in the scene (the editor-time one) follows the bootstrap.
            foreach (Volume volume in UnityEngine.Object.FindObjectsOfType<Volume>(true))
            {
                if (volume.sharedProfile != profile)
                {
                    volume.sharedProfile = profile;
                    changed = true;
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, RunScenePath))
                {
                    throw new InvalidOperationException(LogPrefix + "Could not save " + RunScenePath);
                }
            }

            Debug.Log(LogPrefix + "Run.unity " + (changed ? "aligned" : "already aligned") + ": profile " +
                      ArtLibraryPaths.OvercastVolumeProfile + ", sky " + ArtLibraryPaths.SkyMaterial);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
