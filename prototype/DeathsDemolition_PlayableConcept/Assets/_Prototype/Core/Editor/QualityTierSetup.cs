using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// WP-075 C1 · Quality tiers v2 (planning/work-packages/QUALITY-TIERS-V2.md): three QualitySettings levels — Performance,
    /// High, Cinematic — each with its own URP asset. High keeps today's look: the "Ultra" level it replaces (lodBias 2) and
    /// DD_URP_PipelineAsset unchanged; only its texture streaming budget moves 160 → 1024 MB (ADR-015-1 v3). Performance =
    /// render scale 0.85, MSAA off, hard shadows 45 m / 2 cascades, a renderer copy without SSAO, lodBias 1, 512 MB.
    /// Cinematic (DEC-100, the default on every platform) = MSAA 4×, 4096 soft (high) shadows 120 m / 4 cascades, lodBias 3,
    /// 3072 MB, its own renderer copy with SSAO 2.0 / 0.40 m, depth-normals, high blur and samples. Idempotent:
    /// <c>-executeMethod DD.Prototype.Core.EditorTools.QualityTierSetup.ApplyCli</c>.
    /// </summary>
    public static class QualityTierSetup
    {
        public const string HighPipelinePath = "Assets/Settings/DD_URP_PipelineAsset.asset";
        public const string PerformancePipelinePath = "Assets/Settings/DD_URP_Performance.asset";
        public const string CinematicPipelinePath = "Assets/Settings/DD_URP_Cinematic.asset";
        public const string HighRendererPath = "Assets/Settings/DD_URP_Renderer.asset";
        public const string PerformanceRendererPath = "Assets/Settings/DD_URP_Renderer_Performance.asset";
        public const string CinematicRendererPath = "Assets/Settings/DD_URP_Renderer_Cinematic.asset";
        /// <summary>Cinematic SSAO (QUALITY-TIERS-V2 r2 §4.1): intensity, radius (m).</summary>
        public const float CinematicSsaoIntensity = 2.0f, CinematicSsaoRadius = 0.40f;
        public const string SsaoFeatureName = "ScreenSpaceAmbientOcclusion";
        private const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";

        public static void ApplyCli()
        {
            Apply();
            Debug.Log("[QualityTiers] levels " + string.Join(", ", QualitySettings.names) + "; default " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
        }

        public static void Apply()
        {
            if (!File.Exists(PerformanceRendererPath)) AssetDatabase.CopyAsset(HighRendererPath, PerformanceRendererPath);
            if (!File.Exists(PerformancePipelinePath)) AssetDatabase.CopyAsset(HighPipelinePath, PerformancePipelinePath);
            if (!File.Exists(CinematicPipelinePath)) AssetDatabase.CopyAsset(HighPipelinePath, CinematicPipelinePath);
            if (!File.Exists(CinematicRendererPath)) AssetDatabase.CopyAsset(HighRendererPath, CinematicRendererPath);

            var performanceRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PerformanceRendererPath);
            foreach (ScriptableRendererFeature feature in performanceRenderer.rendererFeatures)
                if (feature != null && feature.name == SsaoFeatureName) { feature.SetActive(false); EditorUtility.SetDirty(feature); }
            EditorUtility.SetDirty(performanceRenderer);

            var performance = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PerformancePipelinePath);
            var so = new SerializedObject(performance);
            so.FindProperty("m_RenderScale").floatValue = 0.85f;
            so.FindProperty("m_MSAA").intValue = (int)MsaaQuality.Disabled;
            so.FindProperty("m_ShadowDistance").floatValue = QualityTiers.ShadowDistanceCap[(int)QualityTier.Performans];
            so.FindProperty("m_ShadowCascadeCount").intValue = QualityTiers.ShadowCascadeCap[(int)QualityTier.Performans];
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            SerializedProperty renderers = so.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = performanceRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(performance);

            var cinematic = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(CinematicPipelinePath);
            so = new SerializedObject(cinematic);
            so.FindProperty("m_MSAA").intValue = (int)MsaaQuality._4x;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.FindProperty("m_ShadowDistance").floatValue = QualityTiers.ShadowDistanceCap[(int)QualityTier.Sinematik];
            so.FindProperty("m_ShadowCascadeCount").intValue = QualityTiers.ShadowCascadeCap[(int)QualityTier.Sinematik];
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            SetEnum(so.FindProperty("m_SoftShadowQuality"), "High");
            // C-00 30.09 decision A (S-07 WP-073 B3): Cinematic alone supports additional-light shadows — the explosion light's; the
            // muzzle light stays shadowless on every tier. High (PrototypeProjectSetup) and Performance keep them off.
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            var cinematicRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CinematicRendererPath);
            SerializedProperty cinematicRenderers = so.FindProperty("m_RendererDataList");
            cinematicRenderers.arraySize = 1;
            cinematicRenderers.GetArrayElementAtIndex(0).objectReferenceValue = cinematicRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cinematic);
            foreach (ScriptableRendererFeature feature in cinematicRenderer.rendererFeatures)
            {
                if (feature == null || feature.name != SsaoFeatureName) continue;
                var ssao = new SerializedObject(feature);
                ssao.FindProperty("m_Settings.Intensity").floatValue = CinematicSsaoIntensity;
                ssao.FindProperty("m_Settings.Radius").floatValue = CinematicSsaoRadius;
                ssao.FindProperty("m_Settings.Downsample").boolValue = false;
                SetEnum(ssao.FindProperty("m_Settings.Source"), "DepthNormals");
                SetEnum(ssao.FindProperty("m_Settings.NormalSamples"), "High");
                SetEnum(ssao.FindProperty("m_Settings.Samples"), "High");
                SetEnum(ssao.FindProperty("m_Settings.BlurQuality"), "High");
                ssao.ApplyModifiedPropertiesWithoutUndo();
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
            }

            EditorUtility.SetDirty(cinematicRenderer);

            var high = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HighPipelinePath);
            ApplyLevels(new Object[] { performance, high, cinematic });
            AssetDatabase.SaveAssets();
        }

        private static void ApplyLevels(Object[] pipelines)
        {
            Object settings = AssetDatabase.LoadAllAssetsAtPath(QualitySettingsPath)[0];
            var so = new SerializedObject(settings);
            SerializedProperty levels = so.FindProperty("m_QualitySettings");
            // Template = today's default ("Ultra") or, on a re-run, the High level.
            int template = levels.arraySize - 1;
            for (int i = 0; i < levels.arraySize; i++)
            {
                string name = levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (name == "Ultra" || name == QualityTiers.LevelNames[(int)QualityTier.Yuksek]) template = i;
            }

            for (int i = levels.arraySize - 1; i >= 0; i--)
                if (i != template) levels.DeleteArrayElementAtIndex(i);
            while (levels.arraySize < QualityTiers.LevelCount) levels.InsertArrayElementAtIndex(0);
            for (int t = 0; t < QualityTiers.LevelCount; t++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(t);
                level.FindPropertyRelative("name").stringValue = QualityTiers.LevelNames[t];
                level.FindPropertyRelative("lodBias").floatValue = QualityTiers.LodBiasBase[t];
                level.FindPropertyRelative("streamingMipmapsMemoryBudget").floatValue = QualityTiers.StreamingBudgetMb[t];
                level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipelines[t];
                // The level's MSAA mirrors its URP asset (Unity re-syncs it whenever the level is active, so a mismatch dirties the file).
                int samples = ((UniversalRenderPipelineAsset)pipelines[t]).msaaSampleCount;
                level.FindPropertyRelative("antiAliasing").intValue = samples > 1 ? samples : 0;
            }

            so.FindProperty("m_CurrentQuality").intValue = (int)QualityTiers.DefaultTier;
            SerializedProperty defaults = so.FindProperty("m_PerPlatformDefaultQuality");
            if (defaults != null && defaults.isArray)
                for (int i = 0; i < defaults.arraySize; i++)
                {
                    SerializedProperty second = defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                    if (second != null) second.intValue = (int)QualityTiers.DefaultTier;
                }

            so.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.SetQualityLevel((int)QualityTiers.DefaultTier, false);
        }

        /// <summary>Sets a serialized enum by its name (URP keeps these enums internal).</summary>
        private static void SetEnum(SerializedProperty property, string name)
        {
            if (property == null) throw new System.InvalidOperationException("serialized enum missing: " + name);
            int index = System.Array.IndexOf(property.enumNames, name);
            if (index < 0) throw new System.InvalidOperationException(property.propertyPath + " has no value " + name);
            property.enumValueIndex = index;
        }
    }
}
