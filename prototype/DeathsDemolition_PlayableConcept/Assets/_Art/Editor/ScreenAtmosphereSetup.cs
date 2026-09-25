using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 C4 B + C5 §5.2 (+ DEC-102 VolumetricLightFeature) · Installs <see cref="ScreenAtmosphereFeature"/> and <see cref="VolumetricLightFeature"/> on the High and Cinematic renderers (QUALITY-TIERS-V2 r2
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

        public const string VolumetricFeatureName = "DD Volumetric Light";

        public static void Apply()
        {
            foreach (string path in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) throw new InvalidOperationException("renderer missing: " + path);
                Ensure<ScreenAtmosphereFeature>(data, FeatureName, ScreenAtmosphereFeature.ShaderName, (f, sh) => f.Shader = sh);
                // WP-075 DEC-102: the raymarched volumetric light (High ¼ × 12, Cinematic ½ × 32; Performance's renderer has neither).
                Ensure<VolumetricLightFeature>(data, VolumetricFeatureName, VolumetricLightFeature.ShaderName, (f, sh) => f.Shader = sh);
                EditorUtility.SetDirty(data);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Adds <typeparamref name="T"/> once, the way URP's renderer editor does (sub-asset, feature list, local-id map).</summary>
        private static void Ensure<T>(UniversalRendererData data, string name, string shaderName, Action<T, Shader> bind) where T : ScriptableRendererFeature
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException(shaderName + " missing");
            T feature = null;
            foreach (ScriptableRendererFeature f in data.rendererFeatures)
                if (f is T t) feature = t;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<T>();
                feature.name = name;
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

            bind(feature, shader);
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
        }
    }
}
