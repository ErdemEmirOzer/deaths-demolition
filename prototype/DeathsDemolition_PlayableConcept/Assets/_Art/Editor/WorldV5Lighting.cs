using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-074 B8 · Lighting v5 for the World v5 dev scene: the World_AP005 bake recipe (ArtLightingBake, WP-029) on the v6 layout, run
    /// right after the builder (the rebuild makes a new scene, so its lighting data is baked again). It lays out a Mixed sun (indirect
    /// only; the realtime shadows and the cloud cookie stay), the camera-side character fill and the environment the runtime writes
    /// (v6 → sky v2). Light probes sit on a 6 m grid over ±54 m at 0.6 / 2.0 / 4.5 m (a point inside a collider is skipped) plus the
    /// hall interior. Eighteen baked reflection probes at 512 (QUALITY-TIERS-V2 r2 Cinematic: ≥ 16 × 512) cover the courts, hall,
    /// ponds, grove, kairō and gardens; the plaza's two box probes become baked too. Settings DD_World_v5 (GPU lightmapper, 16
    /// texels/m, AO, 2 bounces); structures and ground are lightmapped, dressing and vegetation probe-lit (WorldV5Dress.Place).
    /// Report: DD_World_v5.bake-report.json (budget 96 MB of imported lightmaps + reflections; the EXR on disk is listed too).
    /// <para>R3 #1 (C-00 07.10 decision B): the world is loaded additively, so Unity never binds these probes on its own; after the
    /// bake the builder writes them into the root's <see cref="WorldBakedProbes"/>, which binds them at runtime. The ground and the
    /// water stay out of the probes (<see cref="ApplyProbeUsage"/>, AD 07.10 M1): one SH sample at a 170 m plate's centre turned the
    /// approved cold slate warm; they keep the runtime ambient, the characters, bamboo, hall and props take the baked light.</para>
    /// </summary>
    public static class WorldV5Lighting
    {
        public const string SettingsPath = ArtLibraryPaths.LightingFolder + "/DD_World_v5.lighting";
        public const string ReportPath = ArtLibraryPaths.LightingFolder + "/DD_World_v5.bake-report.json";
        public const string ProbeGroupName = "Light Probes (B8)", ReflectionGroupName = "Reflection Probes (B8)";
        public const float LightmapResolution = 16f, ProbeGridHalf = 54f, ProbeGridStep = 6f;
        public static readonly float[] ProbeHeights = { 0.6f, 2.0f, 4.5f };
        public const float InteriorProbeStep = 1.5f;
        public static readonly float[] InteriorProbeHeights = { 0.5f, 1.6f, 2.6f };
        public const int ReflectionResolution = 512, BudgetMb = 96;

        /// <summary>R3 #1: the standalone copy of the baked probes, used only when the lighting data's own LightProbes cannot be loaded.</summary>
        public static string StandaloneProbesPath => GreyboxV6Builder.DataFolder + "/V6_LightProbes.asset";

        /// <summary>R3 #1 (AD 07.10 M1) · the large ground and water renderers that keep the runtime ambient instead of the baked
        /// probes: the greybox ground, the plaza slate, the rock garden gravel and every pond's water.</summary>
        public static bool IsProbeOff(string rendererName)
        {
            return rendererName == "Ground" || rendererName == WorldV5Ground.PlazaName || rendererName == "Karesansui_Gravel"
                   || (rendererName.StartsWith("Pond", StringComparison.Ordinal) && rendererName.EndsWith("_Water", StringComparison.Ordinal));
        }

        /// <summary>Unlit cards and sky that never sample probes (left as they are).</summary>
        public static readonly string[] ProbeIgnoredShaders = { "DD/FogSheet", "DD/ShaftCard", "DD/SkyPlate", "DD/SkyPanorama", "DD/UnderstorySkirt", "DD/SunDisc" };

        /// <summary>
        /// R3 #1 · sets <see cref="IsProbeOff"/>'s renderers to LightProbeUsage.Off; returns how many (the builder runs it after the dress).
        /// WP-092 (R3): every other world renderer now BLENDS the baked light probes and reflection probes. The kit prefabs are
        /// built with both Off (ArtLibraryPrefabBuilder, WP-012 — there were no baked probes then), so the dress's LOD1/2, its
        /// dressing and its vegetation took the runtime ambient SH and ignored the bake's probes and the lamp-lit interiors.
        /// Lightmapped LOD0 pieces keep their lightmap; the probes only feed what the lightmap does not cover.
        /// </summary>
        public static int ApplyProbeUsage(Transform root)
        {
            int n = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsProbeOff(r.name))
                {
                    r.lightProbeUsage = LightProbeUsage.Off;
                    n++;
                    continue;
                }

                if (r.sharedMaterial != null && Array.IndexOf(ProbeIgnoredShaders, r.sharedMaterial.shader.name) >= 0) continue;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            }

            return n;
        }

        /// <summary>WP-092 · how many world renderers blend the baked probes after <see cref="ApplyProbeUsage"/> (evidence line).</summary>
        public static int CountProbeBlending(Transform root)
        {
            int n = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.lightProbeUsage == LightProbeUsage.BlendProbes) n++;
            return n;
        }

        /// <summary>Baked reflection volumes (centre, size) — the courts beyond the plaza's own two probes.</summary>
        public static readonly (string name, Vector3 centre, Vector3 size)[] ReflectionVolumes =
        {
            ("HallInterior", new Vector3(0f, 4f, 44f), new Vector3(33f, 6f, 10f)),
            ("HallFront", new Vector3(0f, 3f, 34f), new Vector3(44f, 8f, 8f)),
            ("KaramonAxis", new Vector3(0f, 3f, -17f), new Vector3(10f, 8f, 6f)),
            ("Ponds", new Vector3(5f, 3f, -30f), new Vector3(30f, 8f, 20f)),
            ("SouthShore", new Vector3(-11f, 3f, -46f), new Vector3(24f, 8f, 8f)),
            ("TeaHouse", new Vector3(-32f, 3f, -36f), new Vector3(10f, 8f, 10f)),
            ("GroveSouth", new Vector3(-46f, 5f, -15f), new Vector3(18f, 10f, 30f)),
            ("GroveNorth", new Vector3(-46f, 5f, 15f), new Vector3(18f, 10f, 30f)),
            ("WestKairo", new Vector3(-33f, 2f, 2f), new Vector3(6f, 4f, 36f)),
            ("ServiceYard", new Vector3(-38f, 4f, 39f), new Vector3(20f, 8f, 18f)),
            ("RearKairo", new Vector3(0f, 3f, 52.5f), new Vector3(46f, 6f, 6f)),
            ("EastPavilion", new Vector3(37f, 3f, 26f), new Vector3(22f, 8f, 14f)),
            ("SakuraCourt", new Vector3(36f, 4f, 1f), new Vector3(18f, 8f, 18f)),
            ("EastGardenSouth", new Vector3(30f, 3f, -25f), new Vector3(30f, 8f, 30f)),
            ("NorthEastPines", new Vector3(44f, 5f, 40f), new Vector3(20f, 10f, 20f)),
            ("NorthWest", new Vector3(-28f, 3f, 24f), new Vector3(14f, 8f, 14f)),
            ("WestGate", new Vector3(-45f, 4f, 0f), new Vector3(20f, 8f, 12f)),
            ("RockGarden", new Vector3(20f, 3f, -23f), new Vector3(18f, 8f, 16f)),
        };

        [Serializable]
        public sealed class Report
        {
            public string utc, lightmapper;
            public float lightmapResolution;
            public int contributeGI, probes, reflectionProbes, lightmapCount, budgetMb;
            public float bakeSeconds;
            /// <summary>EXR on disk (uncompressed half float) and the imported textures' size (format × mips × faces) — the budget reads the latter.</summary>
            public long bytes, shippedBytes;
            public bool withinBudget, ok;
            public string[] files;
            /// <summary>R3 #1: where the root's WorldBakedProbes took the probes from ("lightingData" / "standalone" / "none") and their count.</summary>
            public string probeSource;
            public int probeRefCount;
        }

        /// <summary>Prepares and bakes the (already saved) v6 scene under <paramref name="root"/>; returns a one-line summary.</summary>
        public static string Bake(Scene scene, Transform root)
        {
            WorldLayoutKind previous = WorldLayout.Current;
            WorldLayout.SetCurrent(WorldLayoutKind.V6);   // the environment the bake sees = the runtime's (sky v2)
            try
            {
                Light sun = WorldAtmosphere.FindBakedSun();
                if (sun == null)
                {
                    sun = new GameObject(WorldAtmosphere.BakedSunName).AddComponent<Light>();
                    sun.transform.SetParent(root, false);
                }

                sun.transform.rotation = Quaternion.Euler(WorldAtmosphere.SunEulerFor(WorldLayoutKind.V6));   // DEC-102: the high sun
                WorldAtmosphere.ConfigureSun(sun);
                sun.lightmapBakeType = LightmapBakeType.Mixed;
                sun.bounceIntensity = 1f;
                WorldV5SunFlare.Attach(sun);   // DEC-102: the strong sun's flare (occluded)
                RenderSettings.sun = sun;
                ArtLightingBake.ConfigureCharacterFill(root);
                WorldAtmosphere.ApplyEnvironment();

                Physics.SyncTransforms();
                var probes = new GameObject(ProbeGroupName).AddComponent<LightProbeGroup>();
                probes.transform.SetParent(root, false);
                Vector3[] positions = ProbePositions();
                probes.probePositions = positions;
                probes.dering = true;

                var reflections = new GameObject(ReflectionGroupName).transform;
                reflections.SetParent(root, false);
                foreach ((string name, Vector3 centre, Vector3 size) in ReflectionVolumes)
                {
                    var probe = new GameObject("Reflection " + name).AddComponent<ReflectionProbe>();
                    probe.transform.SetParent(reflections, false);
                    probe.transform.position = centre;
                    probe.size = size;
                    Configure(probe);
                }

                int reflectionCount = ReflectionVolumes.Length;
                foreach (ReflectionProbe plaza in root.GetComponentsInChildren<ReflectionProbe>(true))
                {
                    if (plaza.transform.parent == reflections) continue;
                    Configure(plaza);   // the plaza's box probes (WorldV5Ground) join the bake
                    reflectionCount++;
                }

                int contributeGI = 0;
                foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.enabled && (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) != 0) contributeGI++;

                LightingSettings settings = ArtLightingBake.EnsureLightingSettings(SettingsPath, "DD_World_v5", LightmapResolution);
                Lightmapping.lightingSettings = settings;
                SaveLoaded();

                // WP-092 A: every loaded scene bakes together — the world (active: its settings) and World_FPTPOnly next to it — so the
                // FP / TP-only content keeps its bounce and shadows on the world and the world's lightmaps stay one bake.
                var sw = Stopwatch.StartNew();
                Lightmapping.Clear();
                bool ok = Lightmapping.Bake();
                sw.Stop();
                (LightProbes baked, string probeSource) = ok ? WriteProbeReference(root) : (null, "none");
                SaveLoaded();
                Debug.Log("[WorldV5Lighting] scenes baked together: " + LoadedScenes() + "; other scenes' ContributeGI renderers lightmapped " + OtherScenesLightmapped(scene));
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var report = new Report
                {
                    utc = DateTime.UtcNow.ToString("o"),
                    lightmapper = settings.lightmapper.ToString(),
                    lightmapResolution = LightmapResolution,
                    contributeGI = contributeGI,
                    probes = positions.Length,
                    reflectionProbes = reflectionCount,
                    budgetMb = BudgetMb,
                    bakeSeconds = (float)sw.Elapsed.TotalSeconds,
                    ok = ok,
                    probeSource = probeSource,
                    probeRefCount = baked != null ? baked.count : 0,
                };
                Measure(scene.path, report);
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(ReportPath);
                string line = "bake " + (ok ? "OK" : "FAILED") + " " + report.bakeSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s: " + report.lightmapCount + " lightmaps, " +
                              (report.shippedBytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB imported (" + (report.bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) +
                              " MB EXR on disk; budget " + BudgetMb + "), ContributeGI " + contributeGI + ", probes " + positions.Length + " (bound at runtime: " + report.probeRefCount + ", " + probeSource + "), reflection " + reflectionCount;
                Debug.Log("[WorldV5Lighting] " + line);
                if (!ok) throw new InvalidOperationException("[WorldV5Lighting] Lightmapping.Bake returned false");
                return line;
            }
            finally
            {
                WorldLayout.SetCurrent(previous);
            }
        }

        private static void SaveLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || string.IsNullOrEmpty(s.path)) continue;
                EditorSceneManager.MarkSceneDirty(s);
                EditorSceneManager.SaveScene(s);
            }
        }

        private static string LoadedScenes()
        {
            var names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isLoaded) names.Add(SceneManager.GetSceneAt(i).name);
            return string.Join(" + ", names);
        }

        private static string OtherScenesLightmapped(Scene world)
        {
            int gi = 0, mapped = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || s == world) continue;
                foreach (GameObject go in s.GetRootGameObjects())
                foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.enabled || (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) == 0 || r.receiveGI != ReceiveGI.Lightmaps) continue;
                    gi++;
                    if (r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE) mapped++;
                }
            }

            return mapped + "/" + gi;
        }

        /// <summary>
        /// R3 #1 · after the bake: the scene's LightProbes — the lighting data's own (a sub-object of LightingData.asset), else a
        /// standalone copy of <c>LightmapSettings.lightProbes</c> at <see cref="StandaloneProbesPath"/> — written into the root's
        /// <see cref="WorldBakedProbes"/>. Returns the probes and their source.
        /// </summary>
        public static (LightProbes probes, string source) WriteProbeReference(Transform root)
        {
            LightProbes probes = null;
            string source = "none";
            LightingDataAsset data = Lightmapping.lightingDataAsset;
            if (data != null)
            {
                foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(data)))
                {
                    if (o is LightProbes lp && lp.count > 0 && (probes == null || lp.count > probes.count))
                    {
                        probes = lp;
                        source = "lightingData";
                    }
                }
            }

            if (probes == null && LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0)
            {
                LightProbes copy = UnityEngine.Object.Instantiate(LightmapSettings.lightProbes);
                copy.name = "V6_LightProbes";
                AssetDatabase.DeleteAsset(StandaloneProbesPath);
                AssetDatabase.CreateAsset(copy, StandaloneProbesPath);
                probes = copy;
                source = "standalone";
            }

            WorldBakedProbes holder = root.GetComponent<WorldBakedProbes>();
            if (holder == null) holder = root.gameObject.AddComponent<WorldBakedProbes>();
            holder.SetBaked(probes, probes != null ? probes.count : 0, source);
            EditorUtility.SetDirty(holder);
            Debug.Log("[WorldV5Lighting] baked probes for the runtime: " + (probes != null ? probes.count : 0) + " (" + source + ")");
            return (probes, source);
        }

        /// <summary>The bake outputs next to the scene: files, EXR bytes on disk, the imported textures' memory, the lightmap count, the budget verdict.</summary>
        public static void Measure(string scenePath, Report report)
        {
            string folder = Path.Combine(Path.GetDirectoryName(scenePath), Path.GetFileNameWithoutExtension(scenePath)).Replace('\\', '/');
            var files = new List<string>();
            long bytes = 0, shipped = 0;
            int lightmaps = 0;
            if (Directory.Exists(folder))
                foreach (string f in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".meta", StringComparison.Ordinal) || name.StartsWith("NavMesh", StringComparison.Ordinal)) continue;
                    files.Add(name);
                    bytes += new FileInfo(f).Length;
                    if (name.StartsWith("Lightmap-", StringComparison.Ordinal) && name.Contains("_comp_light")) lightmaps++;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(folder + "/" + name);
                    if (texture != null) shipped += ImportedBytes(texture);
                }

            files.Sort(StringComparer.Ordinal);
            report.files = files.ToArray();
            report.bytes = bytes;
            report.shippedBytes = shipped;
            report.lightmapCount = lightmaps;
            report.withinBudget = shipped <= (long)BudgetMb * 1048576L;
        }

        /// <summary>
        /// The imported texture's size from its format and mip chain (× 6 faces for a cubemap). Deterministic: the profiler's runtime
        /// size doubled right after a bake (the freshly imported textures still held their CPU copy).
        /// </summary>
        public static long ImportedBytes(Texture texture)
        {
            long total = 0;
            int faces = texture is Cubemap ? 6 : 1;
            for (int m = 0; m < Mathf.Max(1, texture.mipmapCount); m++)
                total += (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(Mathf.Max(1, texture.width >> m), Mathf.Max(1, texture.height >> m), texture.graphicsFormat) * faces;
            return total;
        }

        /// <summary>Batch: re-measure the existing bake (no re-bake) and rewrite the report.</summary>
        public static void RefreshReportCli()
        {
            var report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            report.budgetMb = BudgetMb;
            Measure(ArtLibraryPaths.GreyboxV6Scene, report);
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ReportPath);
            Debug.Log("[WorldV5Lighting] report: " + (report.shippedBytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB imported, " +
                      (report.bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB on disk, within " + report.withinBudget);
        }

        private static void Configure(ReflectionProbe probe)
        {
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = ReflectionResolution;
            probe.hdr = true;
            probe.center = Vector3.zero;
            probe.boxProjection = true;
            probe.importance = 1;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        }

        /// <summary>The 6 m grid over ±54 m at three heights and the hall interior (2.2 / 3.8 m over the 1.28 floor), skipping points inside colliders.</summary>
        public static Vector3[] ProbePositions()
        {
            var list = new List<Vector3>();
            int mask = ~((1 << 2) | (1 << DD.Prototype.Core.PrototypeLayers.CameraOccluder));
            void Add(Vector3 p)
            {
                if (!Physics.CheckSphere(p, 0.15f, mask, QueryTriggerInteraction.Ignore)) list.Add(p);
            }

            for (float x = -ProbeGridHalf; x <= ProbeGridHalf + 0.01f; x += ProbeGridStep)
            for (float z = -ProbeGridHalf; z <= ProbeGridHalf + 0.01f; z += ProbeGridStep)
                foreach (float h in ProbeHeights) Add(new Vector3(x, h, z));
            for (float x = -15f; x <= 15.01f; x += 3f)
            for (float z = 40f; z <= 48.01f; z += 2.7f)
            {
                Add(new Vector3(x, 2.2f, z));
                Add(new Vector3(x, 3.8f, z));
            }

            // WP-092: the interiors' lamp pools on the furniture (probe-lit dressing) — a 1.5 m grid inside every layout building, three heights.
            foreach (Bounds room in WorldV5Layout.InteriorVolumes(WorldV5Layout.Load()))
            for (float x = room.min.x + 0.75f; x <= room.max.x - 0.5f; x += InteriorProbeStep)
            for (float z = room.min.z + 0.75f; z <= room.max.z - 0.5f; z += InteriorProbeStep)
                foreach (float h in InteriorProbeHeights) Add(new Vector3(x, room.min.y + h, z));

            return list.ToArray();
        }
    }
}
