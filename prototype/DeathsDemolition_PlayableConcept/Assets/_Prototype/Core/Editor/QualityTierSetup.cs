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
    /// Cinematic = MSAA 4×, 4096 shadow map, lodBias 2.5, 2048 MB. Default level High on every platform. Idempotent:
    /// <c>-executeMethod DD.Prototype.Core.EditorTools.QualityTierSetup.ApplyCli</c>.
    /// </summary>
    public static class QualityTierSetup
    {
        public const string HighPipelinePath = "Assets/Settings/DD_URP_PipelineAsset.asset";
        public const string PerformancePipelinePath = "Assets/Settings/DD_URP_Performance.asset";
        public const string CinematicPipelinePath = "Assets/Settings/DD_URP_Cinematic.asset";
        public const string HighRendererPath = "Assets/Settings/DD_URP_Renderer.asset";
        public const string PerformanceRendererPath = "Assets/Settings/DD_URP_Renderer_Performance.asset";
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
            SerializedProperty renderers = so.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = performanceRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(performance);

            var cinematic = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(CinematicPipelinePath);
            so = new SerializedObject(cinematic);
            so.FindProperty("m_MSAA").intValue = (int)MsaaQuality._4x;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cinematic);

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
            }

            so.FindProperty("m_CurrentQuality").intValue = (int)QualityTier.Yuksek;
            SerializedProperty defaults = so.FindProperty("m_PerPlatformDefaultQuality");
            if (defaults != null && defaults.isArray)
                for (int i = 0; i < defaults.arraySize; i++)
                {
                    SerializedProperty second = defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                    if (second != null) second.intValue = (int)QualityTier.Yuksek;
                }

            so.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.SetQualityLevel((int)QualityTier.Yuksek, false);
        }
    }
}
