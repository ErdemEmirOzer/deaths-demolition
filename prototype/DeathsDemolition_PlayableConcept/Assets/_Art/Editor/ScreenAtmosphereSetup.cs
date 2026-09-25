using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 C4 B + C5 §5.2 · Installs <see cref="ScreenAtmosphereFeature"/> on the High and Cinematic renderers (QUALITY-TIERS-V2 r2
    /// §4.2: Performance has neither screen-space shafts nor height fog, so its renderer stays without it) — the way URP's renderer
    /// editor adds a feature: a sub-asset, the feature list and its local-id map. Idempotent; the shader reference keeps
    /// Hidden/DD/ScreenAtmosphere in builds. Run by GreyboxV6Builder with the dressed world, or
    /// <c>-executeMethod DD.Art.Pipeline.EditorTools.ScreenAtmosphereSetup.ApplyCli</c>.
    /// </summary>
    public static class ScreenAtmosphereSetup
    {
        public const string FeatureName = "DD Screen Atmosphere";
        /// <summary>QualityTierSetup.HighRendererPath / CinematicRendererPath (DD.Prototype.Core.Editor, not referenced here).</summary>
        public static readonly string[] RendererPaths = { "Assets/Settings/DD_URP_Renderer.asset", "Assets/Settings/DD_URP_Renderer_Cinematic.asset" };

        public static void ApplyCli()
        {
            Apply();
            Debug.Log("[ScreenAtmosphere] feature on " + string.Join(", ", RendererPaths));
        }

        public static void Apply()
        {
            Shader shader = Shader.Find(ScreenAtmosphereFeature.ShaderName);
            if (shader == null) throw new InvalidOperationException(ScreenAtmosphereFeature.ShaderName + " missing");
            foreach (string path in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) throw new InvalidOperationException("renderer missing: " + path);
                ScreenAtmosphereFeature feature = null;
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                    if (f is ScreenAtmosphereFeature s) feature = s;
                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<ScreenAtmosphereFeature>();
                    feature.name = FeatureName;
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                    var so = new SerializedObject(data);
                    SerializedProperty features = so.FindProperty("m_RendererFeatures");
                    SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                feature.Shader = shader;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
