using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-029 · The bake policy (ADR-029-2): World_AP005 owns a Mixed-mode sun (the same
    /// parameters WorldAtmosphere builds at runtime; the runtime reuses it), a light probe
    /// group (route samples, spawn / weapon points, a region grid; two heights), one baked
    /// reflection probe per region plus the pond, and a lighting settings asset (Progressive
    /// GPU, Baked Indirect, AO, non-directional lightmaps, resolution / atlas budgeted to
    /// <see cref="LightmapBudgetMb"/>). Static instances (structure / ground / dressing) carry
    /// ContributeGI (PlacementImporter writes it; <see cref="PrepareWorld"/> also sets it on the
    /// open scene so a bake never depends on a re-import). <see cref="BakeWorld"/> is the
    /// repeatable batch entry (<c>tools/bake-lighting.sh</c>): prepare → Lightmapping.Bake →
    /// save → report (time, lightmap files, bytes).
    ///
    /// Renderer side (<see cref="ConfigureRenderer"/>): the SSAO renderer feature at its low-cost
    /// setting and the main-light shadow distance — the only URP asset fields this package
    /// touches (MSAA 4× → 2× per ADR-025-2 is written here too; HDR / render scale stay).
    /// </summary>
    public static class ArtLightingBake
    {
        private const string LogPrefix = "[ArtLightingBake] ";

        public const string LightingSettingsPath = ArtLibraryPaths.LightingFolder + "/DD_World_v1.lighting";
        public const string BakeReportPath = ArtLibraryPaths.LightingFolder + "/DD_World_v1.bake-report.json";
        public const string ProbeGroupName = "Light Probes (WP-029)";
        public const string ReflectionProbesName = "Reflection Probes (WP-029)";
        public const string UrpAssetPath = "Assets/Settings/DD_URP_PipelineAsset.asset";
        public const string UrpRendererPath = "Assets/Settings/DD_URP_Renderer.asset";
        public const string SsaoTypeName = "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";

        // ------------------------------------------------------------ bake budget (ADR-029-2)
        /// <summary>Texels per metre. First bake at 6 filled ONE 2048 atlas (7 MB) with 1534 overlapping-UV warnings on the small dressing; 12 for the structures/ground, dressing probe-lit (below).</summary>
        public const float LightmapResolution = 16f;   // 12 showed texel steps on the west-corridor wall (bake2); chart margins are computed to survive ½ of this (ArtImportContract.ModelSecondaryUVMinLightmapResolution)
        public const int LightmapMaxSize = 2048;
        public const int LightmapPadding = 4;
        /// <summary>A LOD0 renderer whose surface would take fewer texels than this at <see cref="LightmapResolution"/> is probe-lit instead of charted (bake4: 134 of the 174 remaining overlaps were charts under 800 texels — posts, railings, small panels).</summary>
        public const int MinChartTexels = 1024;
        public const int LightmapBudgetMb = 64;
        public const float AoMaxDistance = 1.0f;
        public const int MaxBounces = 2;
        public const int IndirectSamples = 256;
        public const int DirectSamples = 32;
        public const int EnvironmentSamples = 256;

        // ------------------------------------------------------------ probes
        public const float ProbeRouteStep = 4f;
        public const float ProbeGridStep = 6f;
        public const float ProbeGridHalf = 48f;
        public static readonly float[] ProbeHeights = { 0.6f, 2.0f };
        public const float ProbeGridUpperHeight = 4.5f;
        public const int ReflectionProbeResolution = 128;
        public const float ReflectionProbeHeight = 6f;

        // ------------------------------------------------------------ character fill (WP-038G, M3 closure K2)
        public const string CharacterFillName = "Fill (characters, WP-038G)";
        public static readonly Vector3 CharacterFillEuler = new Vector3(50f, 0f, 0f);   // from the camera side (the rig looks +Z from the south), 50° down
        /// <summary>
        /// vs the 1.15 overcast sun. On the S-10 closure r1 frame the zombie figure (shadow px stripped) sits at luma 0.189 against a
        /// 0.171 ring; the gate wants Michelson ≥ 0.06 on its own mask (0.019 there). 0.45 lifted the crowd only ~5 % (WP-016 test
        /// 0.070 → 0.087); 0.9 targets a ~20 % lift on camera-facing cloth (Michelson ≈ 0.14 on the same frame's mask) — the
        /// world layers are excluded, so the ground keeps its Look B value.
        /// </summary>
        public const float CharacterFillIntensity = 0.9f;
        /// <summary>Rendering layer bit 1: the character fill lights only renderers that carry it (characters); the world renders on bit 0.</summary>
        public const uint CharacterLightLayer = DD.Prototype.Characters.ZombieSkinnedVisual.CharacterRenderingLayers & ~1u;
        public static readonly Color CharacterFillColor = new Color(0.96f, 0.97f, 1.0f);

        /// <summary>Batch entry: renderer / pipeline-asset settings (ConfigureRenderer) plus the character fill on World_AP005 — no re-import, no bake.</summary>
        public static void ConfigureCharacterFillCli()
        {
            ConfigureRenderer();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var root = GameObject.Find(Ap005WorldBootstrap.RootName);
            if (root == null) throw new InvalidOperationException(LogPrefix + "world root missing");
            bool created = ConfigureCharacterFill(root.transform);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log(LogPrefix + "character fill " + (created ? "created" : "updated") + ": " + CharacterFillIntensity + " @ " + CharacterFillEuler);
        }

        /// <summary>Creates / updates the character fill light under the world root: realtime only, no shadows, culling mask = every layer but the world's.</summary>
        public static bool ConfigureCharacterFill(Transform root)
        {
            Light fill = null;
            foreach (Light l in root.GetComponentsInChildren<Light>(true)) if (l.name == CharacterFillName) fill = l;
            bool created = fill == null;
            if (created)
            {
                var go = new GameObject(CharacterFillName);
                go.transform.SetParent(root, false);
                fill = go.AddComponent<Light>();
            }

            fill.type = LightType.Directional;
            fill.transform.rotation = Quaternion.Euler(CharacterFillEuler.x, CharacterFillEuler.y, CharacterFillEuler.z);
            fill.color = CharacterFillColor;
            fill.intensity = CharacterFillIntensity;
            fill.shadows = LightShadows.None;
            fill.lightmapBakeType = LightmapBakeType.Realtime;
            fill.bounceIntensity = 0f;
            // URP ignores Light.cullingMask; the character-only reach is a Rendering Layer (light layers on in the pipeline asset,
            // ConfigureRenderer): the fill carries CharacterLightLayer alone, the character renderers carry it next to the default bit
            // (ArtCharacterPrefabBuilder), the world keeps the default bit only — so the ground stays at its Look B value.
            fill.cullingMask = -1;
            fill.renderingLayerMask = (int)CharacterLightLayer;
            var data = fill.GetComponent<UniversalAdditionalLightData>() ?? fill.gameObject.AddComponent<UniversalAdditionalLightData>();
            data.renderingLayers = CharacterLightLayer;
            data.customShadowLayers = false;
            return created;
        }

        // ------------------------------------------------------------ renderer (WP-029 look)
        public const float ShadowDistance = 60f;          // 45 → 60: the gameplay camera sees ~110 m; palace at 50–60 m grounds
        /// <summary>WP-038G (M3 closure K1): 4 → 2 cascades at 60 m — the top-down camera never needs four splits; each cascade re-draws every caster (~300 draws each).</summary>
        public const int ShadowCascadeCount = 2;
        /// <summary>WP-038G: SSAO normals reconstructed from depth (URP Source = Depth, Medium samples) — the DepthNormals prepass re-drew every opaque renderer (~1 000 draws at 3 120 instances).</summary>
        public const int SsaoSource = 1, SsaoNormalSamples = 1;
        public const float SsaoIntensity = 1.2f;
        public const float SsaoRadius = 0.30f;
        public const float SsaoDirectLightingStrength = 0.25f;
        public const float SsaoFalloff = 80f;

        // ================================================================== renderer

        /// <summary>SSAO feature (low cost: downsample, after opaque, depth-normals, low normal quality, 4 samples, medium blur) + shadow distance.</summary>
        /// <summary>Rendering-layer name of <see cref="CharacterLightLayer"/> in the URP global settings.</summary>
        public const string CharacterLightLayerName = "Characters";

        /// <summary>
        /// URP masks every light's rendering layers with the layers that are NAMED in the global settings
        /// (<c>ToValidRenderingLayers</c>); with the default single "Default" entry, a light on bit 1 reaches nothing.
        /// Names bit 1 "Characters" (the array is trimmed to the bits in use).
        /// </summary>
        public static void ConfigureRenderingLayerNames()
        {
            // UniversalRenderPipelineGlobalSettings is internal in URP 14 — reached through the graphics settings as the public base type.
            RenderPipelineGlobalSettings settings = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            if (settings == null) throw new InvalidOperationException(LogPrefix + "URP global settings missing");
            var so = new SerializedObject(settings);
            SerializedProperty names = so.FindProperty("m_RenderingLayerNames");
            int characterBit = 0;
            for (uint m = CharacterLightLayer; m > 1u; m >>= 1) characterBit++;
            if (names.arraySize < characterBit + 1) names.arraySize = characterBit + 1;
            if (string.IsNullOrEmpty(names.GetArrayElementAtIndex(0).stringValue)) names.GetArrayElementAtIndex(0).stringValue = "Default";
            names.GetArrayElementAtIndex(characterBit).stringValue = CharacterLightLayerName;
            uint valid = 0;
            for (int i = 0; i < names.arraySize; i++) valid |= 1u << i;
            so.FindProperty("m_ValidRenderingLayers").intValue = (int)valid;
            so.ApplyModifiedPropertiesWithoutUndo();   // m_ValidRenderingLayers is the serialized field the getter returns; the name cache is rebuilt on load
            EditorUtility.SetDirty(settings);
        }

        public static void ConfigureRenderer()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            if (renderer == null) throw new FileNotFoundException(LogPrefix + "renderer data missing", UrpRendererPath);
            // URP 14 keeps the SSAO feature type internal; it is created by name (the editor does the same through its menu).
            Type ssaoType = typeof(UniversalRendererData).Assembly.GetType(SsaoTypeName);
            if (ssaoType == null) throw new InvalidOperationException(LogPrefix + "SSAO feature type not found: " + SsaoTypeName);
            ScriptableRendererFeature ssao = null;
            foreach (ScriptableRendererFeature f in renderer.rendererFeatures) if (f != null && f.GetType() == ssaoType) ssao = f;
            var so = new SerializedObject(renderer);
            if (ssao == null)
            {
                ssao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
                ssao.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                SerializedProperty list = so.FindProperty("m_RendererFeatures");
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = ssao;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId);
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var fso = new SerializedObject(ssao);
            SerializedProperty settings = fso.FindProperty("m_Settings");
            var written = new List<string>();
            SetIfPresent(settings, "Downsample", p => p.boolValue = true, written);
            SetIfPresent(settings, "AfterOpaque", p => p.boolValue = true, written);
            SetIfPresent(settings, "Source", p => p.enumValueIndex = SsaoSource, written);          // WP-038G: Depth (1) — no DepthNormals prepass
            SetIfPresent(settings, "NormalSamples", p => p.enumValueIndex = SsaoNormalSamples, written);   // Medium
            SetIfPresent(settings, "Intensity", p => p.floatValue = SsaoIntensity, written);
            SetIfPresent(settings, "DirectLightingStrength", p => p.floatValue = SsaoDirectLightingStrength, written);
            SetIfPresent(settings, "Radius", p => p.floatValue = SsaoRadius, written);
            SetIfPresent(settings, "Samples", p => p.enumValueIndex = 2, written);         // Low (URP 14 enum: High 0, Medium 1, Low 2)
            SetIfPresent(settings, "BlurQuality", p => p.enumValueIndex = 1, written);     // Medium
            SetIfPresent(settings, "Falloff", p => p.floatValue = SsaoFalloff, written);
            SetIfPresent(settings, "AOMethod", p => p.enumValueIndex = 0, written);        // interleaved gradient (URP 14: 0 = SSAO)
            fso.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(true);
            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null) throw new FileNotFoundException(LogPrefix + "pipeline asset missing", UrpAssetPath);
            var aso = new SerializedObject(asset);
            aso.FindProperty("m_ShadowDistance").floatValue = ShadowDistance;
            aso.FindProperty("m_ShadowCascadeCount").intValue = ShadowCascadeCount;
            aso.FindProperty("m_SupportsLightLayers").boolValue = true;   // WP-038G: the character fill reaches characters only (CharacterLightLayer)
            ConfigureRenderingLayerNames();
            aso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "renderer: SSAO feature (" + string.Join(", ", written) + "), shadows " + ShadowDistance + " m / " + ShadowCascadeCount + " cascades, light layers on");
        }

        /// <summary>A/B helper (evidence only): <c>-executeMethod …ArtLightingBake.SetSsaoCli -ddSsao on|off</c> toggles the SSAO feature.</summary>
        /// <summary>
        /// Diagnostic: every charted MeshRenderer of World_AP005 with its estimated texels, the mesh's
        /// lightmap-UV square fill, the baked atlas footprint and the instance role, as CSV (<c>-ddOut &lt;path&gt;</c>;
        /// default <c>Temp/</c>). Used to read the lightmapper's "overlapping UVs" list, which names only the renderer.
        /// </summary>
        public static void ReportChartsCli()
        {
            string[] args = Environment.GetCommandLineArgs();
            string outPath = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ddOut") outPath = args[i + 1];
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(Application.dataPath, "../Temp/DD_World_v1.charts.csv");   // diagnostic, not an asset
            EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            var lines = new List<string> { "instance,role,renderer,texels,area_m2,uvFill,scaleInLightmap,lightmapIndex,receiveGI,contributeGI,bakedTexels,uvSquareTexels" };
            LightmapData[] maps = LightmapSettings.lightmaps;
            foreach (MeshRenderer mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))
            {
                bool contributes = (GameObjectUtility.GetStaticEditorFlags(mr.gameObject) & StaticEditorFlags.ContributeGI) != 0;
                if (!contributes && mr.receiveGI != ReceiveGI.Lightmaps) continue;
                Transform inst = mr.transform;
                RegionTag tag = null;
                while (inst != null && (tag = inst.GetComponent<RegionTag>()) == null) inst = inst.parent;
                var mf = mr.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                int charts = 0;
                if (mesh != null && mesh.uv2 != null && mesh.uv2.Length > 0)
                {
                    // Charts ≈ connected islands in UV2: count triangles whose three uv2 differ from a neighbour is expensive; use a cheap proxy —
                    // the number of distinct uv2 bounding "cells" is not available, so report the UV2 bounding-box fill instead.
                    Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
                    foreach (Vector2 uv in mesh.uv2) { min = Vector2.Min(min, uv); max = Vector2.Max(max, uv); }
                    charts = Mathf.RoundToInt((max.x - min.x) * (max.y - min.y) * 100f);   // % of the UV square the charts span
                }

                float texels = ChartTexels(mr);
                // After a bake: the atlas footprint Unity actually gave the renderer (scale × atlas size).
                float baked = 0f;
                if (mr.lightmapIndex >= 0 && mr.lightmapIndex < maps.Length && maps[mr.lightmapIndex].lightmapColor != null)
                {
                    Texture2D atlas = maps[mr.lightmapIndex].lightmapColor;
                    baked = mr.lightmapScaleOffset.x * atlas.width * mr.lightmapScaleOffset.y * atlas.height;
                }

                lines.Add(string.Join(",",
                    inst != null ? inst.name : mr.transform.root.name,
                    tag != null ? tag.Role : "-",
                    mr.name,
                    texels.ToString("F0", CultureInfo.InvariantCulture),
                    (texels / (LightmapResolution * LightmapResolution)).ToString("F2", CultureInfo.InvariantCulture),
                    charts.ToString(CultureInfo.InvariantCulture),
                    mr.scaleInLightmap.ToString("F2", CultureInfo.InvariantCulture),
                    mr.lightmapIndex.ToString(CultureInfo.InvariantCulture),
                    mr.receiveGI.ToString(),
                    contributes ? "1" : "0",
                    baked.ToString("F0", CultureInfo.InvariantCulture),
                    UvSquareTexels(mr).ToString("F0", CultureInfo.InvariantCulture)));
            }

            File.WriteAllLines(outPath, lines);
            Debug.Log(LogPrefix + "charts report: " + (lines.Count - 1) + " renderers → " + outPath);
        }

        /// <summary>Diagnostic: lists LOD0 renderers whose chart / ContributeGI state disagrees with the size rule (test failure triage).</summary>
        public static void PolicyAuditCli()
        {
            EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            var root = GameObject.Find(Ap005WorldBootstrap.RootName);
            Transform instances = root.transform.Find(Ap005WorldBootstrap.InstancesName);
            int breaks = 0, small = 0, charts = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < instances.childCount; i++)
            {
                Transform t = instances.GetChild(i);
                if ((GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
                var group = t.GetComponentInChildren<LODGroup>(true);
                if (group == null) continue;
                LOD[] lods = group.GetLODs();
                if (lods.Length == 0) continue;
                foreach (Renderer r in lods[0].renderers)
                {
                    var mr = r as MeshRenderer;
                    if (mr == null) continue;
                    float texels = ChartTexels(mr);
                    bool big = texels >= MinChartTexels;
                    bool contributes = (GameObjectUtility.GetStaticEditorFlags(mr.gameObject) & StaticEditorFlags.ContributeGI) != 0;
                    bool chart = contributes && mr.receiveGI == ReceiveGI.Lightmaps;
                    if (chart) charts++; else small++;
                    if (chart != big || contributes != big)
                    {
                        breaks++;
                        if (breaks <= 12) sb.Append(t.name).Append('/').Append(mr.name).Append(" texels=").Append(texels.ToString("F0", CultureInfo.InvariantCulture)).Append(" chart=").Append(chart).Append(" contributes=").Append(contributes).Append(" scale=").Append(mr.transform.lossyScale.ToString("F2")).Append(" mesh=").Append(mr.GetComponent<MeshFilter>() != null && mr.GetComponent<MeshFilter>().sharedMesh != null ? mr.GetComponent<MeshFilter>().sharedMesh.name + "/v" + mr.GetComponent<MeshFilter>().sharedMesh.vertexCount + "/readable=" + mr.GetComponent<MeshFilter>().sharedMesh.isReadable : "-").Append('\n');
                    }
                }
            }

            Debug.Log(LogPrefix + "policy audit: charts " + charts + ", small " + small + ", breaks " + breaks + "\n" + sb);
        }

        public static void SetSsaoCli()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ddSsao") value = args[i + 1];
            bool on = !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            foreach (ScriptableRendererFeature f in renderer.rendererFeatures)
            {
                if (f != null && f.GetType().FullName == SsaoTypeName) { f.SetActive(on); EditorUtility.SetDirty(f); }
            }

            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "SSAO " + (on ? "on" : "off"));
        }

        private static void SetIfPresent(SerializedProperty settings, string field, Action<SerializedProperty> write, List<string> written)
        {
            SerializedProperty p = settings != null ? settings.FindPropertyRelative(field) : null;
            if (p == null) return;
            write(p);
            written.Add(field);
        }

        // ================================================================== prepare

        public sealed class PrepareReport
        {
            public int contributeGI;
            public int probes;
            public int reflectionProbes;
            public bool sunCreated;
            public bool fillCreated;
        }

        /// <summary>Opens World_AP005 and writes the bake set-up into it (idempotent); saves the scene. Returns the report.</summary>
        /// <summary>
        /// ADR-029-2: of a lightmapped instance only the LOD0 renderers take a lightmap chart and
        /// occlude the bake; LOD1+ (and any renderer outside the LOD group) are probe-lit and do not
        /// contribute. bake3 (16 texels/m) listed 1096 renderers with overlapping charts — 328 instances
        /// × three LOD levels, two thirds of them the small LOD1/LOD2 charts. A non-lightmapped instance
        /// (dressing) loses ContributeGI on every child and is probe-lit throughout.
        /// </summary>
        public static void ApplyLightmapLodPolicy(GameObject instance, bool lightmapped)
        {
            var lod0 = new HashSet<Renderer>();
            var group = instance.GetComponentInChildren<LODGroup>(true);
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length > 0) foreach (Renderer r in lods[0].renderers) if (r != null) lod0.Add(r);
            }

            foreach (Transform c in instance.GetComponentsInChildren<Transform>(true))
            {
                var mr = c.GetComponent<MeshRenderer>();
                bool chart = lightmapped && (group == null || lod0.Contains(mr)) && (mr == null || ChartTexels(mr) >= MinChartTexels);
                bool contribute = mr == null ? lightmapped : chart;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(c.gameObject);
                StaticEditorFlags want = contribute ? flags | StaticEditorFlags.ContributeGI : flags & ~StaticEditorFlags.ContributeGI;
                if (want != flags) GameObjectUtility.SetStaticEditorFlags(c.gameObject, want);
                // MeshRenderer.receiveGI's setter is a no-op on a renderer without ContributeGI (the 61 small bamboo
                // fences kept the prefab's "Lightmaps" after their flag was cleared) — write the serialized field instead.
                if (mr != null)
                {
                    var so = new SerializedObject(mr);
                    SerializedProperty rgi = so.FindProperty("m_ReceiveGI");
                    int wantGI = chart ? (int)ReceiveGI.Lightmaps : (int)ReceiveGI.LightProbes;
                    if (rgi != null && rgi.intValue != wantGI) { rgi.intValue = wantGI; so.ApplyModifiedPropertiesWithoutUndo(); }
                }
            }
        }

        /// <summary>Lightmap texels the renderer's mesh would occupy at <see cref="LightmapResolution"/>: world-space surface area × resolution².</summary>
        public static float ChartTexels(MeshRenderer mr)
        {
            var mf = mr.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return 0f;
            Vector3[] v = mesh.vertices;
            int[] tri = mesh.triangles;
            Vector3 scale = mr.transform.lossyScale;
            double area = 0;
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                Vector3 a = Vector3.Scale(v[tri[i]], scale), b = Vector3.Scale(v[tri[i + 1]], scale), d = Vector3.Scale(v[tri[i + 2]], scale);
                area += Vector3.Cross(b - a, d - a).magnitude * 0.5f;
            }

            return (float)(area * LightmapResolution * LightmapResolution);
        }

        /// <summary>
        /// Texels of the whole [0,1] lightmap-UV square at <see cref="LightmapResolution"/>: world area / UV2 area of the
        /// mesh (the density Unity sizes an object's lightmap by), × resolution². Larger than <see cref="ChartTexels"/> by the
        /// packing waste of the charts; 0 without UV2.
        /// </summary>
        public static float UvSquareTexels(MeshRenderer mr)
        {
            var mf = mr.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || mesh.uv2 == null || mesh.uv2.Length != mesh.vertexCount) return 0f;
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv2;
            int[] tri = mesh.triangles;
            Vector3 scale = mr.transform.lossyScale;
            double world = 0, uvArea = 0;
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                Vector3 a = Vector3.Scale(v[tri[i]], scale), b = Vector3.Scale(v[tri[i + 1]], scale), d = Vector3.Scale(v[tri[i + 2]], scale);
                world += Vector3.Cross(b - a, d - a).magnitude * 0.5f;
                Vector2 p = uv[tri[i]], q = uv[tri[i + 1]], r = uv[tri[i + 2]];
                uvArea += Mathf.Abs((q.x - p.x) * (r.y - p.y) - (r.x - p.x) * (q.y - p.y)) * 0.5f;
            }

            return uvArea > 1e-9 ? (float)(world / uvArea * LightmapResolution * LightmapResolution) : 0f;
        }

        public static PrepareReport PrepareWorld()
        {
            Scene scene = EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            Transform root = null;
            foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == Ap005WorldBootstrap.RootName) root = go.transform;
            if (root == null) throw new InvalidOperationException(LogPrefix + "World_AP005 has no " + Ap005WorldBootstrap.RootName);
            var bootstrap = root.GetComponent<Ap005WorldBootstrap>();
            var placement = bootstrap != null ? new SerializedObject(bootstrap).FindProperty("_placement").objectReferenceValue as WorldPlacementAsset : null;
            if (placement == null) placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            var report = new PrepareReport();

            // 1) ContributeGI on the structures and the ground (large surfaces that fill a lightmap chart);
            //    dressing (lanterns, rocks, benches, props) stays static but probe-lit — at 6 texels/m the
            //    first bake reported 1534 objects with overlapping lightmap UVs, all small dressing charts.
            Transform instances = root.Find(Ap005WorldBootstrap.InstancesName);
            for (int i = 0; instances != null && i < instances.childCount; i++)
            {
                Transform t = instances.GetChild(i);
                var tag = t.GetComponent<RegionTag>();
                if (tag == null) continue;
                bool lightmapped = tag.Role == "structure" || tag.Role == "ground";
                bool isStatic = lightmapped || tag.Role == "dressing";
                if (!isStatic) continue;
                ApplyLightmapLodPolicy(t.gameObject, lightmapped);
                if (lightmapped) report.contributeGI++;
            }

            Transform ground = root.Find(PlacementImporter.GroundName);   // WP-014 stand-in; absent once the AP-015P plates are in (WP-038)
            if (ground != null)
            {
                StaticEditorFlags gf = GameObjectUtility.GetStaticEditorFlags(ground.gameObject);
                GameObjectUtility.SetStaticEditorFlags(ground.gameObject, gf | StaticEditorFlags.ContributeGI);
            }

            // 2) The baked sun (Mixed). The runtime finds it by name and does not build its own.
            Light sun = WorldAtmosphere.FindBakedSun();
            if (sun == null)
            {
                var go = new GameObject(WorldAtmosphere.BakedSunName);
                go.transform.SetParent(root, false);
                sun = go.AddComponent<Light>();
                report.sunCreated = true;
            }

            sun.transform.rotation = Quaternion.Euler(WorldAtmosphere.SunEuler.x, WorldAtmosphere.SunEuler.y, WorldAtmosphere.SunEuler.z);
            WorldAtmosphere.ConfigureSun(sun);
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.bounceIntensity = 1f;
            RenderSettings.sun = sun;

            // 2b) WP-038G (M3 closure K2): a realtime character fill from the camera side. Under Look B the zombie cloth and the
            // AP-015P ground share one luma band once cast shadows are stripped from the reading (crowd/surround r1 0.019, r4
            // 0.035 vs the 0.06 gate); the fill lights only the character layers (everything except Environment / CameraOccluder),
            // so the plates keep their value and the characters lift ~25 % on camera-facing surfaces. Not baked, no shadows.
            report.fillCreated = ConfigureCharacterFill(root);

            // 3) Environment (ambient trilight, sky, fog) — the same values the runtime writes, so the bake sees the runtime sky.
            WorldAtmosphere.ApplyEnvironment();

            // 4) Light probes.
            Transform probeRoot = root.Find(ProbeGroupName);
            if (probeRoot == null)
            {
                probeRoot = new GameObject(ProbeGroupName).transform;
                probeRoot.SetParent(root, false);
            }

            LightProbeGroup group = probeRoot.GetComponent<LightProbeGroup>();
            if (group == null) group = probeRoot.gameObject.AddComponent<LightProbeGroup>();   // Unity's fake null defeats ??
            Vector3[] positions = ProbePositions(placement);
            group.probePositions = positions;
            group.dering = true;
            report.probes = positions.Length;

            // 5) Reflection probes: one per region + the pond.
            Transform reflRoot = root.Find(ReflectionProbesName);
            if (reflRoot == null)
            {
                reflRoot = new GameObject(ReflectionProbesName).transform;
                reflRoot.SetParent(root, false);
            }

            foreach (ReflectionProbe old in reflRoot.GetComponentsInChildren<ReflectionProbe>(true)) UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach ((string name, Bounds b) in ReflectionVolumes(placement))
            {
                var go = new GameObject("Reflection " + name);
                go.transform.SetParent(reflRoot, false);
                go.transform.position = b.center;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked;
                probe.resolution = ReflectionProbeResolution;
                probe.hdr = true;
                probe.size = b.size;
                probe.center = Vector3.zero;
                probe.boxProjection = false;
                probe.importance = 1;
                probe.shadowDistance = ShadowDistance;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                report.reflectionProbes++;
            }

            // 6) Lighting settings asset on the scene.
            LightingSettings settings = EnsureLightingSettings();
            Lightmapping.lightingSettings = settings;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ArtLibraryPaths.Ap005WorldScene);
            Debug.Log(LogPrefix + "prepared " + ArtLibraryPaths.Ap005WorldScene + ": ContributeGI " + report.contributeGI + " instances, fill " + (report.fillCreated ? "created" : "updated") + ", sun " + (report.sunCreated ? "created" : "kept") + " (Mixed), probes " + report.probes + ", reflection probes " + report.reflectionProbes + ", settings " + LightingSettingsPath);
            return report;
        }

        public static LightingSettings EnsureLightingSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "DD_World_v1" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapResolution = LightmapResolution;
            settings.lightmapMaxSize = LightmapMaxSize;
            settings.lightmapPadding = LightmapPadding;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.ao = true;
            settings.aoMaxDistance = AoMaxDistance;
            settings.aoExponentDirect = 1f;
            settings.aoExponentIndirect = 1f;
            settings.maxBounces = MaxBounces;
            settings.minBounces = 1;
            settings.indirectSampleCount = IndirectSamples;
            settings.directSampleCount = DirectSamples;
            settings.environmentSampleCount = EnvironmentSamples;
            settings.lightProbeSampleCountMultiplier = 4f;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            settings.prioritizeView = false;
            settings.autoGenerate = false;
            settings.indirectScale = 1f;
            settings.albedoBoost = 1f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        // ------------------------------------------------------------ probe placement

        public static Vector3[] ProbePositions(WorldPlacementAsset placement)
        {
            var set = new HashSet<Vector3Int>();
            var list = new List<Vector3>();
            void Add(Vector3 p)
            {
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 4f), Mathf.RoundToInt(p.y * 4f), Mathf.RoundToInt(p.z * 4f));
                if (set.Add(key)) list.Add(p);
            }

            if (placement != null)
            {
                foreach (WorldPlacementAsset.Route r in placement.routes)
                {
                    float len = Vector3.Distance(r.a, r.b);
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / ProbeRouteStep));
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = Vector3.Lerp(r.a, r.b, (float)i / n);
                        foreach (float h in ProbeHeights) Add(new Vector3(p.x, h, p.z));
                    }
                }

                foreach (WorldPlacementAsset.Point pt in placement.spawnPoints) foreach (float h in ProbeHeights) Add(new Vector3(pt.position.x, h, pt.position.z));
                foreach (WorldPlacementAsset.Point pt in placement.weaponPoints) foreach (float h in ProbeHeights) Add(new Vector3(pt.position.x, h, pt.position.z));
                foreach (float h in ProbeHeights) Add(new Vector3(placement.playerStart.x, h, placement.playerStart.z));
            }

            for (float x = -ProbeGridHalf; x <= ProbeGridHalf + 0.01f; x += ProbeGridStep)
            for (float z = -ProbeGridHalf; z <= ProbeGridHalf + 0.01f; z += ProbeGridStep)
            {
                Add(new Vector3(x, ProbeHeights[0], z));
                Add(new Vector3(x, ProbeHeights[1], z));
                Add(new Vector3(x, ProbeGridUpperHeight, z));
            }

            return list.ToArray();
        }

        public static List<(string name, Bounds bounds)> ReflectionVolumes(WorldPlacementAsset placement)
        {
            var result = new List<(string, Bounds)>();
            if (placement != null)
            {
                foreach (WorldPlacementAsset.Region region in placement.regions)
                {
                    Bounds b = WorldPlacementAsset.RegionBounds(region, 0f, ReflectionProbeHeight * 2f);
                    b.Expand(new Vector3(4f, 0f, 4f));
                    result.Add((region.id, b));
                }

                // The pond: the union of the koi basins.
                bool any = false;
                var pond = new Bounds();
                foreach (WorldPlacementAsset.Instance inst in placement.instances)
                {
                    if (inst.family != "koi_pond_basin") continue;
                    var bb = new Bounds(inst.position, new Vector3(12f, ReflectionProbeHeight * 2f, 8f));
                    if (!any) { pond = bb; any = true; } else pond.Encapsulate(bb);
                }

                if (any) result.Add(("pond", pond));
            }

            if (result.Count == 0) result.Add(("world", new Bounds(Vector3.zero, new Vector3(ProbeGridHalf * 2f, ReflectionProbeHeight * 2f, ProbeGridHalf * 2f))));
            return result;
        }

        // ================================================================== bake

        [Serializable]
        public sealed class BakeReport
        {
            public string utc;
            public string lightmapper;
            public float lightmapResolution;
            public int lightmapMaxSize;
            public int contributeGI;
            public int probes;
            public int reflectionProbes;
            public int uvOverlaps;   // renderers the lightmapper flagged with overlapping charts (0 is the goal, small LOD0 charts may remain)
            public float bakeSeconds;
            public int lightmapCount;
            public long lightmapBytes;
            public int lightmapBudgetMb;
            public bool withinBudget;
            public string[] files;
            public bool ok;
        }

        /// <summary>Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtLightingBake.BakeWorld</c> (no -nographics: the GPU lightmapper needs a device).</summary>
        public static void BakeWorld()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException(LogPrefix + "cannot bake in Play mode");
            ArtPipelineMenu.ReimportContractModels();   // stale lightmap UVs (margin method / resolution) never reach the bake
            PrepareReport prep = PrepareWorld();
            Scene scene = SceneManager.GetActiveScene();
            var sw = Stopwatch.StartNew();
            int uvOverlaps = 0;
            Application.LogCallback onLog = (condition, stack, type) =>
            {
                // "There are N objects in the Scene with overlapping UV's." — the lightmapper's chart check.
                const string marker = "objects in the Scene with overlapping UV";
                int at = condition.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) return;
                string head = condition.Substring(0, at).Trim();
                int space = head.LastIndexOf(' ');
                int n;
                if (int.TryParse(space >= 0 ? head.Substring(space + 1) : head, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) uvOverlaps = n;
            };
            Application.logMessageReceived += onLog;
            bool ok;
            try
            {
                Lightmapping.Clear();
                ok = Lightmapping.Bake();
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }

            sw.Stop();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ArtLibraryPaths.Ap005WorldScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string folder = Path.Combine(Path.GetDirectoryName(ArtLibraryPaths.Ap005WorldScene), ArtLibraryPaths.Ap005WorldSceneName).Replace('\\', '/');
            var files = new List<string>();
            long bytes = 0;
            int count = 0;
            if (Directory.Exists(folder))
            {
                foreach (string f in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".meta", StringComparison.Ordinal)) continue;
                    if (name.StartsWith("Lightmap-", StringComparison.Ordinal) || name.StartsWith("Reflection", StringComparison.Ordinal) || name.StartsWith("LightingData", StringComparison.Ordinal))
                    {
                        files.Add(name);
                        bytes += new FileInfo(f).Length;
                        if (name.StartsWith("Lightmap-", StringComparison.Ordinal) && name.Contains("_comp_light")) count++;
                    }
                }
            }

            files.Sort(StringComparer.Ordinal);
            var report = new BakeReport
            {
                utc = DateTime.UtcNow.ToString("o"),
                lightmapper = Lightmapping.lightingSettings != null ? Lightmapping.lightingSettings.lightmapper.ToString() : "?",
                lightmapResolution = LightmapResolution,
                lightmapMaxSize = LightmapMaxSize,
                contributeGI = prep.contributeGI,
                uvOverlaps = uvOverlaps,
                probes = prep.probes,
                reflectionProbes = prep.reflectionProbes,
                bakeSeconds = (float)sw.Elapsed.TotalSeconds,
                lightmapCount = count,
                lightmapBytes = bytes,
                lightmapBudgetMb = LightmapBudgetMb,
                withinBudget = bytes <= (long)LightmapBudgetMb * 1048576L,
                files = files.ToArray(),
                ok = ok
            };
            File.WriteAllText(BakeReportPath, JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(BakeReportPath);
            Debug.Log(LogPrefix + "bake " + (ok ? "OK" : "FAILED") + " in " + report.bakeSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s: " + count + " lightmaps, " + files.Count + " files, " + (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB (budget " + LightmapBudgetMb + " MB, " + (report.withinBudget ? "within" : "OVER") + "), uv overlaps " + uvOverlaps + ", probes " + prep.probes + ", reflection " + prep.reflectionProbes + " → " + BakeReportPath);
            if (!ok) throw new InvalidOperationException(LogPrefix + "Lightmapping.Bake returned false");
        }
    }
}
