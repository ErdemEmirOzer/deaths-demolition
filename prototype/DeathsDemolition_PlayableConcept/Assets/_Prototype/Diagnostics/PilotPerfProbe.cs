#if DEVELOPMENT_BUILD || UNITY_EDITOR || DD_PERF_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Rendering.Universal;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-011 · test-only rendering / load measurement probe for the real Player.
    ///
    /// Drives one real run into a chosen scene (the procedural Run scene or the AP-001
    /// golden-slice pilot) through the public flow API, pins the spawn director at its
    /// safety ceiling so the load is the same in both scenes, and then measures, in the
    /// Player rather than the Editor, what the content pipeline pilot has to report: the
    /// frame-time distribution of an uncapped window, the render and memory counters
    /// (draw calls, SetPass, triangles, vertices, batches, texture / mesh / GC / system
    /// memory), the crowd solve cost, the per-second managed-heap delta with a positive
    /// control, enemy readability from the fixed camera (five rays per body against the
    /// occluding layers, on-screen count and screen height), and one gameplay capture.
    /// Nothing here changes product behaviour: it calls the same public methods the F1
    /// panel calls, reads public state, and puts back the frame-rate cap it lifted.
    ///
    /// PASS on every stage means MEASUREMENT INTEGRITY (the numbers were obtained under
    /// the intended load), never a frame-rate verdict: the 60 fps target is a number the
    /// report reads, not a gate the probe closes (WP-011 contract Y0-8).
    ///
    /// WP-078 (S-10 plan §4): also compiled into the non-distributed `release-probe` build
    /// (tools/build.sh release-probe: release settings + the DD_PERF_PROBE define), so the
    /// gate perf numbers come from release code paths.
    /// Compiled ONLY into development builds and the Editor (file-level #if); a release
    /// Player contains none of it, and without <c>-ddPilotPerfProbe</c> a development
    /// Player allocates nothing either. The profile path is redirected to the isolated
    /// file the runner passes, so the real profile is never touched.
    ///
    /// Command line:
    ///   -ddPilotPerfProbe &lt;outDir&gt;       enable; write stage JSON, the frame-time CSV and the capture into outDir, then quit
    ///   -ddPilotScene &lt;Run|AP001_GoldenSlice&gt;  scene to measure (default AP001_GoldenSlice)
    ///   -ddPilotWindowSeconds &lt;n&gt;        measurement window length (default 60)
    ///   -ddPilotWarmupSeconds &lt;n&gt;        warm-up after the load is reached (default 10)
    ///   -ddPilotStart &lt;x,z,yaw&gt;          WP-074 B9: measured pose on the NavMesh (≤ 2 m), sweep held off
    ///   -ddProfilePath &lt;file&gt;            redirect the profile file (keeps the real profile untouched)
    /// </summary>
    public static class PilotPerfProbe
    {
        private const string OutFlag = "-ddPilotPerfProbe";
        private const string SceneFlag = "-ddPilotScene";
        private const string WindowFlag = "-ddPilotWindowSeconds";
        private const string WarmupFlag = "-ddPilotWarmupSeconds";

        /// <summary>
        /// "-ddPilotResolution WxH" (default 1920x1080): the binding measurement device is
        /// 1920x1080, but a Retina desktop opens the Player at whatever PrototypeWindowFit
        /// picks, so the probe pins the framebuffer itself and records what it actually got
        /// in stage 01 (Screen.width/height are pixels, not points).
        /// </summary>
        private const string ResolutionFlag = "-ddPilotResolution";

        /// <summary>
        /// "-ddPilotLodBias f" (optional, evidence-only A/B): overrides QualitySettings.lodBias
        /// for the measurement window and restores it afterwards. The project's Quality asset
        /// is not touched; the value is recorded in stage 01 so the run is labelled.
        /// </summary>
        private const string LodBiasFlag = "-ddPilotLodBias";

        /// <summary>
        /// WP-013 · "-ddPilotAtmosphere off" (optional, evidence-only A/B): after the scene is
        /// ready, freezes the wind (foliage shader + WindZone), stops the drift fields and
        /// switches the camera's post-processing off, so one build measures both sides of
        /// the WP-013 budget. The materials stay on the wind shader either way (a shader
        /// swap would be a different build); the A/B therefore isolates the animated and
        /// particle/post cost, and the WP-012 numbers are the pre-shader baseline.
        /// </summary>
        private const string AtmosphereFlag = "-ddPilotAtmosphere";

        /// <summary>
        /// WP-019 · "-ddPilotVfxStress" (optional): during the window the probe drives the feedback
        /// director with the dense effect scenario of the WP-019 budget — 10 surface impacts/s,
        /// 1 explosion/s and 5 death dressings/s around the player — so the AP-010 layer is measured
        /// on top of the 40-body crowd. Counts and the pool state go into the 04-window stage.
        /// </summary>
        private const string VfxStressFlag = "-ddPilotVfxStress";
        /// <summary>
        /// WP-068 · "-ddPilotHudAB" (optional): after 04d the probe switches the run HUD off and on in
        /// alternating blocks under the same 40-body load and writes 04h-hud: the frame, GPU and UI-marker
        /// deltas the HUD costs (the HUD budget, tools/qa/aggregate.py perf.hudCostMs).
        /// </summary>
        private const string HudAbFlag = "-ddPilotHudAB";
        /// <summary>WP-017 · <c>-ddArmor all</c>: every forced zombie carries all three AP-008 pieces at full durability (worst-case crowd; the spawn roll is untouched).</summary>
        private const string ArmorFlag = "-ddArmor";

        /// <summary>
        /// WP-012 · "-ddPilotProfilerLog path.raw" (optional): writes the Unity Profiler's binary
        /// log for the measurement window only (Profiler.logFile + enableBinaryLog), so the
        /// frames the CSV shows as spikes can be opened frame by frame in the Editor
        /// (ProfilerDriver.LoadProfile / PilotProfilerAnalysisCli). Development build only,
        /// like the rest of the probe.
        /// </summary>
        private const string ProfilerLogFlag = "-ddPilotProfilerLog";
        private const int DefaultResolutionWidth = 1920;
        private const int DefaultResolutionHeight = 1080;
        private const string ProfileFlag = "-ddProfilePath";

        /// <summary>
        /// WP-078 (S-10 plan §8): <c>-ddPilotCameraSweep 1</c> — in TP/FP the camera turns a full
        /// 360° of yaw at pitch 0 across the measurement window (fixed, repeatable camera program),
        /// and the window stage reports the mean number of living bodies inside the view.
        /// </summary>
        private const string CameraSweepFlag = "-ddPilotCameraSweep";

        /// <summary>
        /// WP-074 B9 (S-05; S-10 30.09): <c>-ddPilotStart x,z,yaw</c> (optional) — before the scene is counted the player stands on the
        /// NavMesh nearest (x, z) (≤ <see cref="StartSampleMetres"/>; farther fails the run, no silent fallback) facing yaw, and the
        /// camera is turned to it. The pose is the measurement, so the camera sweep stays off with it (reported:
        /// <c>sweepOffByStart</c>). The 01 and 04 stages carry <c>start</c> {x, z, yaw, sampled}. Absent: behaviour unchanged.
        /// </summary>
        private const string StartFlag = "-ddPilotStart";
        private const float StartSampleMetres = 2f;

        /// <summary>The pilot scene by name: this assembly does not reference DD.Art, so the string is the contract.</summary>
        public const string PilotSceneName = "AP001_GoldenSlice";

        private const float DefaultWindowSeconds = 60f;
        private const float DefaultWarmupSeconds = 10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(Arg(OutFlag)))
            {
                return;
            }

            string profilePath = Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
                Debug.Log("[PilotPerfProbe] profile path override -> " + profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            string scene = Arg(SceneFlag);
            if (string.IsNullOrEmpty(scene))
            {
                scene = PilotSceneName;
            }

            float window = ArgSeconds(WindowFlag, DefaultWindowSeconds, 1f, 3600f);
            float warmup = ArgSeconds(WarmupFlag, DefaultWarmupSeconds, 0f, 600f);
            int resW = DefaultResolutionWidth;
            int resH = DefaultResolutionHeight;
            string res = Arg(ResolutionFlag);
            if (!string.IsNullOrEmpty(res))
            {
                string[] parts = res.ToLowerInvariant().Split('x');
                int w, h;
                if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out w) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out h) && w >= 320 && h >= 240)
                {
                    resW = w;
                    resH = h;
                }
                else
                {
                    Debug.LogWarning("[PilotPerfProbe] ignoring malformed " + ResolutionFlag + " '" + res + "'; using " + resW + "x" + resH);
                }
            }

            float lodBiasOverride = -1f;
            string lb = Arg(LodBiasFlag);
            if (!string.IsNullOrEmpty(lb))
            {
                float parsed;
                if (float.TryParse(lb, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0f && parsed <= 10f)
                {
                    lodBiasOverride = parsed;
                }
                else
                {
                    Debug.LogWarning("[PilotPerfProbe] ignoring malformed " + LodBiasFlag + " '" + lb + "'");
                }
            }

            var carrier = new GameObject("PilotPerfProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            Driver driver = carrier.AddComponent<Driver>();
            driver.Configure(outDir, scene, window, warmup, resW, resH, lodBiasOverride);
            driver.ProfilerLogPath = Arg(ProfilerLogFlag);
            driver.AtmosphereOff = string.Equals(Arg(AtmosphereFlag), "off", StringComparison.OrdinalIgnoreCase);
            driver.VfxStress = Array.IndexOf(Environment.GetCommandLineArgs(), VfxStressFlag) >= 0;
            driver.HudAB = Array.IndexOf(Environment.GetCommandLineArgs(), HudAbFlag) >= 0;
            driver.ArmorAll = string.Equals(Arg(ArmorFlag), "all", StringComparison.OrdinalIgnoreCase);
            Debug.Log("[PilotPerfProbe] armed scene=" + scene + " window=" + window.ToString("F0", CultureInfo.InvariantCulture) +
                      "s warmup=" + warmup.ToString("F0", CultureInfo.InvariantCulture) + "s out=" + outDir);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static float ArgSeconds(string name, float fallback, float min, float max)
        {
            string raw = Arg(name);
            float value;
            if (string.IsNullOrEmpty(raw) || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            return Mathf.Clamp(value, min, max);
        }

        /// <summary>
        /// One record per stage, every field serialised into NN-stage.json. Fields a stage
        /// does not measure stay at their defaults (same convention as the other probes).
        /// </summary>
        /// <summary>WP-074 B9: the -ddPilotStart pose (requested x / z / yaw; sampled = placed on the NavMesh at sampledX / Z).</summary>
        [Serializable]
        public sealed class StartPose
        {
            public bool set;
            public float x, z, yaw;
            public bool sampled;
            public float sampledX, sampledY, sampledZ;
        }

        [Serializable]
        public sealed class Stage
        {
            public string stage;
            public string utc;
            public string scene;
            public bool pass;
            public string note;
            public string flow;

            // ---- run state at capture time (every stage)
            public int runRound;
            public string runPhase;
            public int alive;
            public int liveBodies;
            public int targetActive;
            public int ceiling;
            public int crowdMembers;
            public float playerX;
            public float playerZ;
            public int targetFrameRate;
            public int vSyncCount;
            public string graphicsDeviceType;
            public string graphicsDeviceName;

            // ---- WP-019 VFX stress (04-window)
            public bool vfxStress;
            public int vfxImpacts;
            public int vfxExplosions;
            public int vfxDeaths;
            public int vfxPlayed;
            public int vfxDecals;
            public int vfxFlipbookSpawned;
            public int vfxFlipbookTotal;
            public int vfxDecalSpawned;
            public int vfxDecalTotal;
            // WP-019b blood layer
            public bool bloodReady;
            public int bloodSplashes;
            public int bloodDropletBursts;
            public int bloodMarks;
            public int bloodPools;
            public int bloodMerged;
            public int bloodEvicted;
            public int bloodSkippedGold;
            public int bloodLive;
            public int bloodDecalSpawned;
            public int bloodDecalTotal;
            public int bloodMergedQuads;
            // 04d GPU sub-window (dev build, Profiler on): average / max GPU ms per frame of the URP draw passes
            public float gpuTransparentsAvgMs = -1f;
            public float gpuTransparentsMaxMs = -1f;
            public float gpuOpaquesAvgMs = -1f;
            public float gpuCameraAvgMs = -1f;
            public float gpuFrameAvgMs = -1f;
            public float gpuFrameMaxMs = -1f;
            public string gpuMeasurementSource;
            public int gpuSampledFrames;
            public float gpuSubWindowSeconds;
            public float gpuSubWindowFrameMeanMs;
            public bool vfxLayerReady;

            // ---- WP-068 04h HUD A/B (dev build, Profiler off): the run HUD on vs off in alternating blocks.
            // CPU ms are per-frame sums of the named main-thread markers; GPU ms is FrameTimingManager.gpuFrameTime.
            public int hudBlocks;
            public int hudFramesOn;
            public int hudFramesOff;
            public float hudFrameOnMs = -1f;
            public float hudFrameOffMs = -1f;
            public float hudFrameDeltaMs;
            public float hudCpuMainOnMs = -1f;
            public float hudCpuMainOffMs = -1f;
            public float hudGpuOnMs = -1f;
            public float hudGpuOffMs = -1f;
            public float hudGpuDeltaMs;
            public float hudGpuDeltaRobustMs;        // trimmed mean of the five on/off pair deltas
            public float hudGpuUiMs = -1f;           // UGUI.Rendering.RenderOverlays GPU time, Profiler sub-window (HUD on)
            public int hudGpuUiSamples;
            public float hudGpuTermMs;               // the GPU term of hudCostMs
            public float hudScriptsOnMs = -1f;       // Update + LateUpdate script runs (every behaviour)
            public float hudScriptsOffMs = -1f;
            public float hudCanvasUpdateOnMs = -1f;  // PlayerUpdateCanvases: WillRenderCanvases, layout + graphic rebuilds, TMP
            public float hudCanvasUpdateOffMs = -1f;
            public float hudEmitGeometryOnMs = -1f;  // PlayerEmitCanvasGeometry: batch build, geometry upload
            public float hudEmitGeometryOffMs = -1f;
            public float hudRenderOverlaysOnMs = -1f; // UGUI.Rendering.RenderOverlays: overlay canvas draw submission
            public float hudRenderOverlaysOffMs = -1f;
            public float hudSetPassOn;
            public float hudSetPassOff;
            public float hudBatchesOn;
            public float hudBatchesOff;
            public float hudDrawCallsOn;
            public float hudDrawCallsOff;
            public float hudCpuMs;                   // Δscripts + UI markers with the HUD on (every canvas: conservative)
            public float hudCostMs;                  // hudCpuMs + hudGpuTermMs: the budgeted number
            public float hudBudgetMs;
            public int hudCanvases;
            public int hudGraphics;
            public int hudTexts;
            public string hudMarkersFound;
            public int hudHiddenGraphics;            // active + enabled but alpha 0 (own, renderer or inherited)
            public float hudAlphaZeroSetPass = -1f;  // HUD root active under a CanvasGroup at alpha 0: are invisible graphics still drawn?
            public float hudAlphaZeroDrawCalls = -1f;
            public string hudDrawInventory;          // canvas | material | texture [| hidden] = graphics, in draw-state groups

            // ---- WP-013 atmosphere (every stage)
            public bool atmosphereOn;
            public float windStrength;
            public int driftFields;
            public int driftParticlesAlive;
            public bool postProcessing;

            // ---- 01 scene-ready
            public float sceneReadySeconds;
            public int rendererCount;
            public int renderersVisible;
            public int lodGroupCount;
            public int meshColliderCount;
            public int boxColliderCount;
            public int capsuleColliderCount;
            public int navMeshTriangles;
            public int navMeshVertices;
            public int screenWidth;
            public int screenHeight;
            public float lodBias;
            public float lodBiasEffective;
            public int qualityLevel;
            public string qualityName;
            public int targetFrameRateBefore;
            public int vSyncCountBefore;
            public float cameraFov;
            public float cameraX;
            public float cameraY;
            public float cameraZ;
            public int lodEstimated0;
            public int lodEstimated1;
            public int lodEstimated2;
            public int lodEstimatedCulled;
            public string lodEstimateMethod;

            // ---- 02 force-40
            public int forcedTarget;
            public int configuredSafetyCeiling;
            public int achievedCount;
            public float forceSeconds;
            public bool forceTimedOut;

            // ---- 03 warmup
            public float warmupSeconds;
            public int warmupFrames;
            public float warmupMeanMs;

            // ---- 04 window: frame times
            public float windowSeconds;
            public string cameraMode;            // WP-078: CameraModeService.Current during the window
            public string qualityTier;           // WP-078: QualityTiers.Current during the window (-ddQualityTier for the matrix)
            public bool cameraSweep;             // WP-078: -ddPilotCameraSweep 1 active (TP/FP only)
            public bool sweepOffByStart;         // WP-074 B9: -ddPilotCameraSweep asked but -ddPilotStart holds the pose
            public StartPose start = new StartPose();   // WP-074 B9: -ddPilotStart (01 and 04)
            public float cameraSweepMeanOnScreen; // WP-078: mean living bodies inside the view during the sweep
            public int frameCount;
            public int frameCountRequired;
            public float frameSumMs;
            public float avgFps;
            public float frameMeanMs;
            public float frameMedianMs;
            public float frameP99Ms;
            public float frameP999Ms;
            public float frameWorst1PctMeanMs;
            public float frameWorst01PctMeanMs;
            public float frameMaxMs;
            public float frameMinMs;
            public int framesOver16_667Ms;
            public float fractionOver16_667Ms;
            public int framesOver33_333Ms;
            public float fractionOver33_333Ms;
            public string frameTimesCsv;

            // ---- 04 window: load integrity
            public int activeMin;
            public int activeMax;
            public float activeAvg;
            public int loadIntegrityFloor;
            public float loadIntegrityFraction;
            public int liveBodiesMin;
            public int liveBodiesMax;
            public int crowdMemberMin;
            public int crowdMemberMax;

            // ---- 04 window: crowd solve
            public float crowdSolveAvgMs;
            public float crowdSolveMaxMs;
            public float crowdSolverOwnAvgMs;
            public float crowdSolverOwnPeakMs;

            // ---- 04 window: ProfilerRecorder counters (avg / max over the window; -1 when unavailable)
            public double drawCallsAvg;
            public long drawCallsMax;
            public double setPassCallsAvg;
            public long setPassCallsMax;
            public double trianglesAvg;
            public long trianglesMax;
            public double verticesAvg;
            public long verticesMax;
            public double batchesAvg;
            public long batchesMax;
            public double textureMemoryAvg;
            public long textureMemoryMax;
            public double meshMemoryAvg;
            public long meshMemoryMax;
            public double gcReservedMemoryAvg;
            public long gcReservedMemoryMax;
            public double gcUsedMemoryAvg;
            public long gcUsedMemoryMax;
            public double systemUsedMemoryAvg;
            public long systemUsedMemoryMax;
            public double totalUsedMemoryAvg;
            public long totalUsedMemoryMax;
            public double totalReservedMemoryAvg;
            public long totalReservedMemoryMax;
            public double gcAllocatedInFrameAvg;
            public long gcAllocatedInFrameMax;
            public long gcAllocatedInWindowTotal;
            public double mainThreadCpuAvgMs;
            public double mainThreadCpuMaxMs;
            public double renderThreadCpuAvgMs;
            public double renderThreadCpuMaxMs;
            public string unavailableCounters;
            public int gen0CollectionsInWindow;
            /// <summary>WP-014: p99 over the frames at or under 33.33 ms (host presentation stalls excluded) and how many frames that excluded.</summary>
            public float frameP99FilteredMs;
            public int framesExcludedAsStalls;
            /// <summary>WP-014: the world the Run scene loaded (WorldBootstrap.ActiveSource) — Procedural or AP005.</summary>
            public string worldSource;

            // ---- WP-015 04b texture-inventory (ADR-015-1 measurement)
            /// <summary>Resident bytes of the art textures (Texture2D named T_*, the Import Contract set).</summary>
            public long artTextureBytes;
            public int artTextureCount;
            /// <summary>Every other Texture2D/Cubemap/2DArray/3D (UI, fonts, engine, sky).</summary>
            public long otherTextureBytes;
            public int otherTextureCount;
            /// <summary>RenderTextures (camera targets, shadow maps, post-fx temporaries) — the counter's floor that no texture policy moves.</summary>
            public long renderTextureBytes;
            public int renderTextureCount;
            public long textureCurrentBytes;
            public long textureDesiredBytes;
            public long textureTargetBytes;
            public long textureNonStreamingBytes;
            public int streamingTextureCount;
            public int nonStreamingTextureCount;
            public bool streamingMipmapsActive;
            public float streamingMipmapsBudgetMb;
            /// <summary>Art textures that are streamed and currently resident below their top mip (mipmapCount − loadedMipmapLevel < mipmapCount).</summary>
            public int artTexturesReduced;
            public string inventoryCsv;
            // WP-017 · 04c mesh inventory: every loaded Mesh by group (zombie armour / zombie modules+bodies / player / world families / other).
            public int meshCount;
            public long meshBytes;
            public long meshArmorBytes;
            public int meshArmorCount;
            public long meshZombieBytes;
            public int meshZombieCount;
            public long meshPlayerBytes;
            public long meshWorldBytes;
            public int meshWorldCount;
            public long meshOtherBytes;
            public string meshTop;

            /// <summary>WP-012: frames over 16.667 ms in the window, and how many of those coincided with a gen0 collection.</summary>
            public int spikeFrames;
            public int spikeFramesWithGen0;
            public string profilerLog;

            // ---- 04 window: managed heap over one second of frames under load
            public long heapNoopGcDelta;
            public long heapNoopMonoDelta;
            public int heapNoopGen0;
            public float gcSubWindowSeconds;
            public int gcSubWindowFrames;
            public long gcSubWindowGcDelta;
            public long gcSubWindowMonoDelta;
            public int gcSubWindowGen0;
            public long gcSubWindowBytesPerSecond;

            // ---- 05 gc-positive-control
            public int controlBytes;
            public long controlGcDelta;
            public long controlMonoDelta;
            public long controlDelta;
            public int controlGen0;

            // ---- 06 readability
            public int enemiesChecked;
            public int enemiesOnScreen;
            public int enemiesBehindCamera;
            public int enemiesMostlyVisible;
            public int enemiesFullyHidden;
            public float fractionMostlyVisible;
            public float meanVisiblePoints;
            public float meanScreenHeightFraction;
            public int raysCast;
            public int raysBlocked;
            public int raysBlockedByEnvironment;
            public int raysBlockedByCameraOccluder;
            public string occlusionLayers;
            public int[] perEnemyVisiblePoints;
            public int[] perEnemyOnScreen;
            public float[] perEnemyScreenHeight;
            public float[] perEnemyDistance;

            // ---- 07 captures
            public string captureFile;
            public bool captureExists;
            public float captureWaitSeconds;

            // ---- 08 restore
            public int restoredTargetFrameRate;
            public int restoredVSyncCount;

            // 08-occluder-fade (REQ-035/111 with real assets in a Player)
            public Vector3 occluderTeleportPosition;
            public int occluderStatesTotal;
            public int occluderStatesEnabled;
            public int occluderRenderersFaded;
            public float occluderMinAlpha;
            public float occluderSecondsToFade;
            public bool occluderFadeObserved;
            public bool occluderRestoredOpaque;
        }

        /// <summary>
        /// One ProfilerRecorder with its running sum / extremes. Sampling reads LastValue
        /// only, so the per-frame path allocates nothing.
        /// </summary>
        private sealed class Counter
        {
            public readonly string Name;
            private readonly ProfilerCategory _category;
            private ProfilerRecorder _recorder;

            public bool Valid;
            public double Sum;
            public long Max;
            public long Min;
            public int Samples;

            public Counter(ProfilerCategory category, string name)
            {
                _category = category;
                Name = name;
            }

            public void Start()
            {
                _recorder = ProfilerRecorder.StartNew(_category, Name);
                Valid = _recorder.Valid;
                Sum = 0d;
                Max = long.MinValue;
                Min = long.MaxValue;
                Samples = 0;
            }

            public void Sample()
            {
                if (!Valid)
                {
                    return;
                }

                long v = _recorder.LastValue;
                Sum += v;
                Samples++;
                if (v > Max) Max = v;
                if (v < Min) Min = v;
            }

            public void Stop()
            {
                if (_recorder.Valid)
                {
                    _recorder.Dispose();
                }
            }

            public double Avg => Valid && Samples > 0 ? Sum / Samples : -1d;
            public long MaxOrMinusOne => Valid && Samples > 0 ? Max : -1L;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeoutSeconds = 60f;
            private const float ForceTimeoutSeconds = 90f;
            private const float CaptureTimeoutSeconds = 10f;
            private const float FrameBudget60Ms = 1000f / 60f;
            private const float FrameBudget30Ms = 1000f / 30f;
            private const int SampleCapacity = 20000;
            private const int ControlBytes = 1024 * 1024;
            // WP-064 / DEC-088 is binding at exactly 40 living enemies for the full
            // measurement window. A lower load is an invalid attempt, never a softer pass.
            private const int LoadIntegrityTolerance = 0;
            private const float LoadIntegrityRequired = 1f;
            private const int ReadabilityPointsPerEnemy = 5;
            private const int ReadabilityMostlyVisible = 3;

            private string _outDir;
            private string _scene;
            private float _windowSeconds;
            private readonly StartPose _start = new StartPose();
            private float _warmupSeconds;

            private readonly List<Stage> _stages = new List<Stage>();
            private readonly List<float> _frameMs = new List<float>(SampleCapacity);

            private bool _timedOut;
            private string _timeoutWhat;
            private int _targetFrameRateBefore;
            private int _vSyncCountBefore;

            private int _resolutionWidth;
            private int _resolutionHeight;

            private float _lodBiasOverride = -1f;
            private float _lodBiasBefore;

            /// <summary>Binary Profiler log written during the window (null = off).</summary>
            internal string ProfilerLogPath;

            internal void Configure(string outDir, string scene, float windowSeconds, float warmupSeconds, int resolutionWidth, int resolutionHeight, float lodBiasOverride)
            {
                _lodBiasOverride = lodBiasOverride;
                _outDir = outDir;
                _scene = scene;
                _windowSeconds = windowSeconds;
                _warmupSeconds = warmupSeconds;
                _resolutionWidth = resolutionWidth;
                _resolutionHeight = resolutionHeight;
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);

                // Pin the framebuffer before anything renders at a size that matters. Two
                // frames is what the capture agent waits as well; the achieved size is
                // recorded in stage 01 rather than assumed.
                Screen.SetResolution(_resolutionWidth, _resolutionHeight, FullScreenMode.Windowed);
                yield return null;
                yield return null;
                Debug.Log("[PilotPerfProbe] resolution requested " + _resolutionWidth + "x" + _resolutionHeight +
                          " -> screen " + Screen.width + "x" + Screen.height + " dpi=" + Screen.dpi.ToString("F0", CultureInfo.InvariantCulture));

                // ---------------------------------------------------------------- flow: Boot -> MainMenu -> Selection -> scene
                yield return WaitForScene(PrototypeScenes.MainMenu, SceneTimeoutSeconds);
                if (_timedOut) { yield return Abort("01-scene-ready", _timeoutWhat); yield break; }
                yield return null;

                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection, SceneTimeoutSeconds);
                if (_timedOut) { yield return Abort("01-scene-ready", _timeoutWhat); yield break; }
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;

                float loadStart = Time.realtimeSinceStartup;
                if (_scene == PrototypeScenes.Run)
                {
                    PrototypeUiFlow.StartRun();
                }
                else
                {
                    if (!Application.CanStreamedLevelBeLoaded(_scene))
                    {
                        yield return Abort("01-scene-ready", "scene '" + _scene + "' is not in the build settings");
                        yield break;
                    }

                    // The pilot scene carries its own bootstrap (run systems + HUD), so the
                    // flow's StartRun (which only knows the Run scene) is bypassed; the
                    // simulation clock is still released the way every flow transition does it.
                    PrototypeUiFlow.ReleaseSimulation();
                    SceneManager.LoadScene(_scene, LoadSceneMode.Single);
                }

                yield return WaitForScene(_scene, SceneTimeoutSeconds);
                if (_timedOut) { yield return Abort("01-scene-ready", _timeoutWhat); yield break; }
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting", SceneTimeoutSeconds);
                if (_timedOut) { yield return Abort("01-scene-ready", _timeoutWhat); yield break; }
                float sceneReadySeconds = Time.realtimeSinceStartup - loadStart;

                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = true;   // the probe player stands still under 40 bodies; keep the window alive

                // WP-074 B9: -ddPilotStart x,z,yaw — the measured pose (the world's corners, the plaza), before anything is counted.
                string start = Arg(StartFlag);
                if (!string.IsNullOrEmpty(start))
                {
                    string[] c = start.Split(',');
                    _start.set = true;
                    if (c.Length != 3 || !float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _start.x) ||
                        !float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _start.z) ||
                        !float.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _start.yaw))
                    {
                        yield return Abort("01-scene-ready", StartFlag + " '" + start + "' is not x,z,yaw");
                        yield break;
                    }

                    if (!NavMesh.SamplePosition(new Vector3(_start.x, rig.transform.position.y, _start.z), out NavMeshHit startHit, StartSampleMetres, NavMesh.AllAreas))
                    {
                        yield return Abort("01-scene-ready", StartFlag + " (" + start + ") is not within " + StartSampleMetres + " m of the NavMesh");
                        yield break;
                    }

                    _start.sampled = true;
                    _start.sampledX = startHit.position.x;
                    _start.sampledY = startHit.position.y;
                    _start.sampledZ = startHit.position.z;
                    rig.Teleport(startHit.position, Quaternion.Euler(0f, _start.yaw, 0f));
                    if (PrototypeCameraRig.Active != null) PrototypeCameraRig.Active.SetViewAngles(_start.yaw, 0f);
                    Debug.Log("[PilotPerfProbe] start pose " + startHit.position.ToString("F2") + " yaw " + _start.yaw.ToString("F0", CultureInfo.InvariantCulture));
                    yield return null;
                }

                // WP-013 A/B: the "off" side of the budget, applied before anything is counted.
                if (AtmosphereOff)
                {
                    SetAtmosphere(false);
                    Debug.Log("[PilotPerfProbe] atmosphere OFF (wind frozen, drift stopped, post-processing off)");
                }

                yield return null;                          // one rendered frame before counting what the scene contains

                // ---------------------------------------------------------------- 01 scene-ready
                Stage s1 = Capture("01-scene-ready");
                s1.sceneReadySeconds = sceneReadySeconds;
                s1.start = _start;
                CountScene(s1);
                s1.targetFrameRateBefore = Application.targetFrameRate;
                s1.vSyncCountBefore = QualitySettings.vSyncCount;
                s1.pass = s1.scene == _scene && s1.rendererCount > 0 && s1.navMeshTriangles > 0;
                s1.note = "scene '" + s1.scene + "' ready in " + sceneReadySeconds.ToString("F2", CultureInfo.InvariantCulture) + " s: renderers=" + s1.rendererCount +
                          " visible=" + s1.renderersVisible + " lodGroups=" + s1.lodGroupCount + " colliders mesh/box/capsule=" + s1.meshColliderCount + "/" + s1.boxColliderCount + "/" + s1.capsuleColliderCount +
                          " navTris=" + s1.navMeshTriangles + " screen=" + s1.screenWidth + "x" + s1.screenHeight + " lodBias=" + s1.lodBias.ToString("F2", CultureInfo.InvariantCulture) +
                          " cap=" + s1.targetFrameRateBefore + " vsync=" + s1.vSyncCountBefore;
                Write(s1);

                // Uncapped for the measurement: the boot applies the 60 fps cap (B-13), which
                // would turn every frame time into the cap and hide the real ceiling. Put back
                // in stage 08.
                _targetFrameRateBefore = Application.targetFrameRate;
                _vSyncCountBefore = QualitySettings.vSyncCount;
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
                _lodBiasBefore = QualitySettings.lodBias;
                if (_lodBiasOverride > 0f)
                {
                    QualitySettings.lodBias = _lodBiasOverride;
                    Debug.Log("[PilotPerfProbe] lodBias override " + _lodBiasBefore.ToString("0.00", CultureInfo.InvariantCulture) +
                              " -> " + _lodBiasOverride.ToString("0.00", CultureInfo.InvariantCulture) + " (evidence-only A/B; Quality asset untouched)");
                }

                // ---------------------------------------------------------------- 02 force-40
                int configuredCeiling = PrototypeConfig.Active.AbsoluteSafetyCeiling;
                int ceiling = dir.SafetyCeiling;
                dir.ForceActiveTarget(ceiling, true);
                float t0 = Time.realtimeSinceStartup;
                bool forceTimedOut = false;
                while (dir.ActiveCount < ceiling)
                {
                    if (Time.realtimeSinceStartup - t0 > ForceTimeoutSeconds)
                    {
                        forceTimedOut = true;
                        break;
                    }

                    yield return null;
                }

                int armorFitted = ArmorAll ? FitArmorOnCrowd() : 0;
                Stage s2 = Capture("02-force-40");
                s2.forcedTarget = ceiling;
                s2.configuredSafetyCeiling = configuredCeiling;
                s2.achievedCount = dir.ActiveCount;
                s2.forceSeconds = Time.realtimeSinceStartup - t0;
                s2.forceTimedOut = forceTimedOut;
                s2.pass = !forceTimedOut && configuredCeiling == 40 && ceiling == configuredCeiling && s2.achievedCount == configuredCeiling;
                s2.note = "configured/ledger/forced " + configuredCeiling + "/" + ceiling + "/" + s2.achievedCount + " (pool ignored) in " + s2.forceSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s" + (forceTimedOut ? " TIMEOUT" : "") + (ArmorAll ? " | armor all: " + armorFitted + " pieces fitted" : "");
                Write(s2);

                // ---------------------------------------------------------------- 03 warmup
                t0 = Time.realtimeSinceStartup;
                int warmFrames = 0;
                double warmSum = 0d;
                while (Time.realtimeSinceStartup - t0 < _warmupSeconds)
                {
                    warmFrames++;
                    warmSum += Time.unscaledDeltaTime * 1000f;
                    yield return null;
                }

                Stage s3 = Capture("03-warmup");
                s3.warmupSeconds = Time.realtimeSinceStartup - t0;
                s3.warmupFrames = warmFrames;
                s3.warmupMeanMs = warmFrames > 0 ? (float)(warmSum / warmFrames) : 0f;
                s3.pass = _warmupSeconds <= 0f || warmFrames > 0;
                s3.note = "warm-up " + s3.warmupSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s, " + warmFrames + " frames, mean " + s3.warmupMeanMs.ToString("F2", CultureInfo.InvariantCulture) + " ms, alive=" + s3.alive;
                Write(s3);

                // ---------------------------------------------------------------- 04 window
                Counter[] counters =
                {
                    new Counter(ProfilerCategory.Render, "Draw Calls Count"),
                    new Counter(ProfilerCategory.Render, "SetPass Calls Count"),
                    new Counter(ProfilerCategory.Render, "Triangles Count"),
                    new Counter(ProfilerCategory.Render, "Vertices Count"),
                    new Counter(ProfilerCategory.Render, "Batches Count"),
                    new Counter(ProfilerCategory.Memory, "Texture Memory"),
                    new Counter(ProfilerCategory.Memory, "Mesh Memory"),
                    new Counter(ProfilerCategory.Memory, "GC Reserved Memory"),
                    new Counter(ProfilerCategory.Memory, "GC Used Memory"),
                    new Counter(ProfilerCategory.Memory, "System Used Memory"),
                    new Counter(ProfilerCategory.Memory, "Total Used Memory"),
                    new Counter(ProfilerCategory.Memory, "Total Reserved Memory"),
                    new Counter(ProfilerCategory.Memory, "GC Allocated In Frame"),
                    new Counter(ProfilerCategory.Internal, "Main Thread"),
                    new Counter(ProfilerCategory.Internal, "Render Thread")
                };
                for (int i = 0; i < counters.Length; i++) counters[i].Start();
                CrowdSolver solver = CrowdService.Solver;
                _frameMs.Clear();

                // The recorders deliver their first LastValue one frame after StartNew;
                // the window opens on the frame after so no sample is a stale zero.
                yield return null;

                int frames = 0;
                int activeMin = int.MaxValue, activeMax = int.MinValue;
                long activeSum = 0L;
                int loadOkFrames = 0;
                int loadFloor = ceiling - LoadIntegrityTolerance;
                int liveMin = int.MaxValue, liveMax = int.MinValue;
                int memberMin = int.MaxValue, memberMax = int.MinValue;
                double solveSum = 0d;
                float solveMax = 0f;
                int gen0Before = GC.CollectionCount(0);
                int gen0Last = gen0Before;
                int spikeFrames = 0;
                int spikeFramesWithGen0 = 0;
                bool profilerLog = !string.IsNullOrEmpty(ProfilerLogPath);
                if (profilerLog)
                {
                    UnityEngine.Profiling.Profiler.logFile = ProfilerLogPath;
                    UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                    UnityEngine.Profiling.Profiler.enabled = true;
                    Debug.Log("[PilotPerfProbe] profiler binary log -> " + ProfilerLogPath);
                }

                bool sweepAsked = string.Equals(Arg(CameraSweepFlag), "1", StringComparison.Ordinal) && CameraModeService.IsLookMode;
                bool sweep = sweepAsked && !_start.set;   // WP-074 B9: a -ddPilotStart pose is held, not swept
                float sweepYaw0 = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.ViewYaw : 0f;
                long sweepOnScreenSum = 0;
                int sweepSamples = 0;
                t0 = Time.realtimeSinceStartup;
                while (true)
                {
                    float ms = Time.unscaledDeltaTime * 1000f;
                    _frameMs.Add(ms);
                    frames++;

                    if (sweep && PrototypeCameraRig.Active != null)
                    {
                        float e = (Time.realtimeSinceStartup - t0) / Mathf.Max(1f, _windowSeconds);
                        PrototypeCameraRig.Active.SetViewAngles(sweepYaw0 + 360f * e, 0f);
                        if (frames % 10 == 0)
                        {
                            IReadOnlyList<ZombieEnemy> bodies = ZombieEnemy.Live;
                            int inView = 0;
                            for (int b = 0; b < bodies.Count; b++)
                            {
                                ZombieEnemy z = bodies[b];
                                if (z != null && z.IsAlive && CameraView.IsInView(z.transform.position + Vector3.up)) inView++;
                            }

                            sweepOnScreenSum += inView;
                            sweepSamples++;
                        }
                    }

                    // Spike ↔ GC correlation: a hitch frame that also saw a gen0 collection is
                    // a GC hitch; one that did not is something else (the profiler log says what).
                    int gen0Now = GC.CollectionCount(0);
                    if (ms > 16.667f)
                    {
                        spikeFrames++;
                        if (gen0Now != gen0Last) spikeFramesWithGen0++;
                    }

                    gen0Last = gen0Now;

                    int active = dir.ActiveCount;
                    activeSum += active;
                    if (active < activeMin) activeMin = active;
                    if (active > activeMax) activeMax = active;
                    if (active >= loadFloor) loadOkFrames++;

                    int live = ZombieEnemy.Live.Count;
                    if (live < liveMin) liveMin = live;
                    if (live > liveMax) liveMax = live;

                    int members = solver.MemberCount;
                    if (members < memberMin) memberMin = members;
                    if (members > memberMax) memberMax = members;

                    float solve = solver.LastSolveMilliseconds;
                    solveSum += solve;
                    if (solve > solveMax) solveMax = solve;

                    for (int i = 0; i < counters.Length; i++) counters[i].Sample();

                    if (VfxStress)
                    {
                        StepVfxStress(Time.realtimeSinceStartup, rig);
                    }

                    if (Time.realtimeSinceStartup - t0 >= _windowSeconds)
                    {
                        break;
                    }

                    yield return null;
                }

                float windowSeconds = Time.realtimeSinceStartup - t0;
                int gen0InWindow = GC.CollectionCount(0) - gen0Before;
                for (int i = 0; i < counters.Length; i++) counters[i].Stop();
                if (profilerLog)
                {
                    UnityEngine.Profiling.Profiler.enabled = false;
                    UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                    UnityEngine.Profiling.Profiler.logFile = "";
                }

                // Managed heap over one second of frames with the load still pinned: the
                // HeapWindow counters read across frames by hand (Measure is synchronous),
                // bracketed by the same Prepare and a synchronous no-op window as the zero
                // reference. The verdict on the sub-window is the caller's - reported only.
                HeapWindow.Prepare();
                yield return null;
                yield return null;
                HeapWindow.Result noop = HeapWindow.Measure(() => { }, 0);
                int subGen0Before = GC.CollectionCount(0);
                long subGcBefore = GC.GetTotalMemory(false);
                long subMonoBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                float subStart = Time.realtimeSinceStartup;
                int subFrames = 0;
                while (Time.realtimeSinceStartup - subStart < 1f)
                {
                    subFrames++;
                    yield return null;
                }

                float subSeconds = Time.realtimeSinceStartup - subStart;
                long subGcDelta = GC.GetTotalMemory(false) - subGcBefore;
                long subMonoDelta = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - subMonoBefore;
                int subGen0 = GC.CollectionCount(0) - subGen0Before;

                Stage s4 = Capture("04-window");
                FillVfxStage(s4);
                s4.lodBiasEffective = QualitySettings.lodBias;
                s4.worldSource = _scene == PrototypeScenes.Run ? WorldBootstrap.ActiveSource.ToString() : _scene;
                s4.windowSeconds = windowSeconds;
                s4.frameCount = frames;
                s4.cameraMode = CameraModeService.Current.ToString();
                s4.qualityTier = QualityTiers.Current.ToString();
                s4.cameraSweep = sweep;
                s4.sweepOffByStart = sweepAsked && _start.set;
                s4.start = _start;
                s4.cameraSweepMeanOnScreen = sweepSamples > 0 ? (float)sweepOnScreenSum / sweepSamples : -1f;
                s4.frameCountRequired = Mathf.CeilToInt(_windowSeconds * 20f);
                ComputeFrameStats(_frameMs, s4);
                s4.frameTimesCsv = "frame-times-ms.csv";
                s4.activeMin = frames > 0 ? activeMin : 0;
                s4.activeMax = frames > 0 ? activeMax : 0;
                s4.activeAvg = frames > 0 ? (float)((double)activeSum / frames) : 0f;
                s4.loadIntegrityFloor = loadFloor;
                s4.loadIntegrityFraction = frames > 0 ? (float)loadOkFrames / frames : 0f;
                s4.liveBodiesMin = frames > 0 ? liveMin : 0;
                s4.liveBodiesMax = frames > 0 ? liveMax : 0;
                s4.crowdMemberMin = frames > 0 ? memberMin : 0;
                s4.crowdMemberMax = frames > 0 ? memberMax : 0;
                s4.crowdSolveAvgMs = frames > 0 ? (float)(solveSum / frames) : 0f;
                s4.crowdSolveMaxMs = solveMax;
                s4.crowdSolverOwnAvgMs = solver.AverageSolveMilliseconds;
                s4.crowdSolverOwnPeakMs = solver.PeakSolveMilliseconds;
                AssignCounters(counters, s4);
                s4.gen0CollectionsInWindow = gen0InWindow;
                s4.spikeFrames = spikeFrames;
                s4.spikeFramesWithGen0 = spikeFramesWithGen0;
                s4.profilerLog = profilerLog ? ProfilerLogPath : null;
                s4.heapNoopGcDelta = noop.GcDelta;
                s4.heapNoopMonoDelta = noop.MonoDelta;
                s4.heapNoopGen0 = noop.Gen0;
                s4.gcSubWindowSeconds = subSeconds;
                s4.gcSubWindowFrames = subFrames;
                s4.gcSubWindowGcDelta = subGcDelta;
                s4.gcSubWindowMonoDelta = subMonoDelta;
                s4.gcSubWindowGen0 = subGen0;
                s4.gcSubWindowBytesPerSecond = subSeconds > 0f ? (long)(Math.Max(subGcDelta, subMonoDelta) / subSeconds) : 0L;
                // Integrity, not speed: enough frames to be a distribution, and the load the
                // window was meant to measure actually present for (nearly) all of it.
                s4.pass = frames >= s4.frameCountRequired && s4.loadIntegrityFraction >= LoadIntegrityRequired;
                s4.note = frames + " frames / " + windowSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s uncapped (lodBias " + s4.lodBiasEffective.ToString("0.00", CultureInfo.InvariantCulture) + ", world " + s4.worldSource + "): mean " + s4.frameMeanMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " ms (" + s4.avgFps.ToString("F1", CultureInfo.InvariantCulture) + " fps) median " + s4.frameMedianMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " p99 " + s4.frameP99Ms.ToString("F2", CultureInfo.InvariantCulture) + " p99filtered " + s4.frameP99FilteredMs.ToString("F2", CultureInfo.InvariantCulture) + " (stalls>33.3ms " + s4.framesExcludedAsStalls + ")" + " 1%low " + s4.frameWorst1PctMeanMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " 0.1%low " + s4.frameWorst01PctMeanMs.ToString("F2", CultureInfo.InvariantCulture) + " max " + s4.frameMaxMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " | >16.67ms " + s4.framesOver16_667Ms + " (" + (s4.fractionOver16_667Ms * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%) >33.33ms " + s4.framesOver33_333Ms +
                          " | draw " + s4.drawCallsAvg.ToString("F0", CultureInfo.InvariantCulture) + " setPass " + s4.setPassCallsAvg.ToString("F0", CultureInfo.InvariantCulture) +
                          " tris " + s4.trianglesAvg.ToString("F0", CultureInfo.InvariantCulture) + " verts " + s4.verticesAvg.ToString("F0", CultureInfo.InvariantCulture) + " batches " + s4.batchesAvg.ToString("F0", CultureInfo.InvariantCulture) +
                          " | cpu main " + s4.mainThreadCpuAvgMs.ToString("F3", CultureInfo.InvariantCulture) + "/" + s4.mainThreadCpuMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                          " render " + s4.renderThreadCpuAvgMs.ToString("F3", CultureInfo.InvariantCulture) + "/" + s4.renderThreadCpuMaxMs.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                          " | tex " + Mb(s4.textureMemoryAvg) + " mesh " + Mb(s4.meshMemoryAvg) + " gcRes " + Mb(s4.gcReservedMemoryAvg) + " sys " + Mb(s4.systemUsedMemoryAvg) +
                          " | crowd " + s4.crowdSolveAvgMs.ToString("F3", CultureInfo.InvariantCulture) + "/" + s4.crowdSolveMaxMs.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                          " | alive min/avg/max " + s4.activeMin + "/" + s4.activeAvg.ToString("F1", CultureInfo.InvariantCulture) + "/" + s4.activeMax + " load>=" + loadFloor + " for " + (s4.loadIntegrityFraction * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%" +
                          " | gen0 " + gen0InWindow + " alloc/frame " + s4.gcAllocatedInFrameAvg.ToString("F0", CultureInfo.InvariantCulture) + " B heap/s " + s4.gcSubWindowBytesPerSecond + " B (gen0 " + subGen0 + ")" +
                          " | spikes " + spikeFrames + " withGen0 " + spikeFramesWithGen0 + (profilerLog ? " profilerLog yes" : "") +
                          (string.IsNullOrEmpty(s4.unavailableCounters) ? "" : " | unavailable: " + s4.unavailableCounters);
                s4.note += " | camera " + s4.cameraMode + (sweep ? " sweep 360° onScreen mean " + s4.cameraSweepMeanOnScreen.ToString("F1", CultureInfo.InvariantCulture) : "");
                s4.note += " | tier " + s4.qualityTier;

                if (VfxStress)
                {
                    s4.note += " | vfxStress impacts=" + s4.vfxImpacts + " explosions=" + s4.vfxExplosions + " deaths=" + s4.vfxDeaths + " played=" + s4.vfxPlayed + " decals=" + s4.vfxDecals +
                               " flipbookPool=" + s4.vfxFlipbookSpawned + "/" + s4.vfxFlipbookTotal + " decalPool=" + s4.vfxDecalSpawned + "/" + s4.vfxDecalTotal + " ready=" + s4.vfxLayerReady +
                               " | blood splashes=" + s4.bloodSplashes + " droplets=" + s4.bloodDropletBursts + " marks=" + s4.bloodMarks + " pools=" + s4.bloodPools + " live=" + s4.bloodLive +
                               " merged=" + s4.bloodMerged + " evicted=" + s4.bloodEvicted + " bloodPool=" + s4.bloodDecalSpawned + "/" + s4.bloodDecalTotal;
                }
                Write(s4);
                WriteFrameTimes();

                // ---------------------------------------------------------------- 04b texture-inventory (WP-015)
                // The Memory counter above is one number; ADR-015-1 budgets the ART set, so
                // the resident textures are listed by object and summed by kind here.
                Stage s4b = Capture("04b-texture-inventory");
                MeasureTextureInventory(s4b);
                s4b.pass = s4b.artTextureCount + s4b.otherTextureCount + s4b.renderTextureCount > 0;
                s4b.note = "art T_* " + s4b.artTextureCount + " = " + Mb(s4b.artTextureBytes) + " (reduced " + s4b.artTexturesReduced + ") | other " + s4b.otherTextureCount + " = " + Mb(s4b.otherTextureBytes) +
                           " | renderTextures " + s4b.renderTextureCount + " = " + Mb(s4b.renderTextureBytes) +
                           " | Texture.current " + Mb(s4b.textureCurrentBytes) + " desired " + Mb(s4b.textureDesiredBytes) + " target " + Mb(s4b.textureTargetBytes) + " nonStreaming " + Mb(s4b.textureNonStreamingBytes) +
                           " | streaming " + (s4b.streamingMipmapsActive ? "on" : "off") + " budget " + s4b.streamingMipmapsBudgetMb.ToString("F0", CultureInfo.InvariantCulture) + " MB textures " + s4b.streamingTextureCount + "/" + (s4b.streamingTextureCount + s4b.nonStreamingTextureCount) +
                           " | csv " + s4b.inventoryCsv;
                Write(s4b);

                // ---------------------------------------------------------------- 04c mesh-inventory (WP-017)
                // The Mesh Memory counter is one number; the AP-008 library references 312 FBX
                // meshes and the world v2 keeps its heavy roofs unbatched, so the resident meshes
                // are summed by group here (armour / zombie / player / world / other) + top 10.
                Stage s4c = Capture("04c-mesh-inventory");
                MeasureMeshInventory(s4c);
                s4c.pass = s4c.meshCount > 0;
                s4c.note = "meshes " + s4c.meshCount + " = " + Mb(s4c.meshBytes) + " | armour " + s4c.meshArmorCount + " = " + Mb(s4c.meshArmorBytes) + " | zombie " + s4c.meshZombieCount + " = " + Mb(s4c.meshZombieBytes) +
                           " | player " + Mb(s4c.meshPlayerBytes) + " | world " + s4c.meshWorldCount + " = " + Mb(s4c.meshWorldBytes) + " | other " + Mb(s4c.meshOtherBytes) + " | top: " + s4c.meshTop;
                Write(s4c);

                // ---------------------------------------------------------------- 04d gpu (WP-019b)
                Stage s4d = Capture("04d-gpu");
                yield return MeasureGpu(s4d, rig, 8f);
                FillVfxStage(s4d);
                s4d.pass = s4d.gpuSampledFrames > 0 && s4d.gpuFrameAvgMs > 0f && !float.IsNaN(s4d.gpuFrameAvgMs) && !float.IsInfinity(s4d.gpuFrameAvgMs);
                s4d.note = "gpu transparents avg " + s4d.gpuTransparentsAvgMs.ToString("F3", CultureInfo.InvariantCulture) + " max " + s4d.gpuTransparentsMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                           " opaques " + s4d.gpuOpaquesAvgMs.ToString("F3", CultureInfo.InvariantCulture) + " camera " + s4d.gpuCameraAvgMs.ToString("F3", CultureInfo.InvariantCulture) +
                           " | frame " + s4d.gpuFrameAvgMs.ToString("F3", CultureInfo.InvariantCulture) + "/" + s4d.gpuFrameMaxMs.ToString("F3", CultureInfo.InvariantCulture) + " ms via " + s4d.gpuMeasurementSource +
                           " over " + s4d.gpuSampledFrames + " sampled frames / " + s4d.gpuSubWindowSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s (frame mean " + s4d.gpuSubWindowFrameMeanMs.ToString("F2", CultureInfo.InvariantCulture) + " ms, profiler on)" +
                           (s4d.gpuSampledFrames == 0 ? " GPU SAMPLES UNAVAILABLE" : "") +
                           " | blood ready=" + s4d.bloodReady + " splashes=" + s4d.bloodSplashes + " droplets=" + s4d.bloodDropletBursts + " marks=" + s4d.bloodMarks + " pools=" + s4d.bloodPools +
                           " live=" + s4d.bloodLive + " merged=" + s4d.bloodMerged + "(" + s4d.bloodMergedQuads + " quads) evicted=" + s4d.bloodEvicted + " skippedGold=" + s4d.bloodSkippedGold +
                           " bloodPool=" + s4d.bloodDecalSpawned + "/" + s4d.bloodDecalTotal;
                Write(s4d);

                // ---------------------------------------------------------------- 04h hud A/B (WP-068)
                if (HudAB)
                {
                    Stage s4h = Capture("04h-hud");
                    yield return MeasureHud(s4h, rig);
                    Write(s4h);
                }

                // ---------------------------------------------------------------- 05 gc-positive-control
                // A measurement that cannot see a known 1 MB allocation is blind, and every
                // heap number above would be meaningless; the bait is kept referenced (its
                // length is recorded) so the allocation cannot be elided.
                byte[] bait = null;
                HeapWindow.Result control = HeapWindow.Measure(() => { bait = new byte[ControlBytes]; }, 0);
                Stage s5 = Capture("05-gc-positive-control");
                s5.controlBytes = bait != null ? bait.Length : 0;
                s5.controlGcDelta = control.GcDelta;
                s5.controlMonoDelta = control.MonoDelta;
                s5.controlDelta = control.Delta;
                s5.controlGen0 = control.Gen0;
                s5.pass = s5.controlBytes == ControlBytes && control.Delta >= ControlBytes;
                s5.note = "1 MB bait: gc=" + control.GcDelta + " mono=" + control.MonoDelta + " delta=" + control.Delta + " gen0=" + control.Gen0 + (s5.pass ? " detected" : " NOT DETECTED (heap numbers blind)");
                Write(s5);
                bait = null;

                // ---------------------------------------------------------------- 06 readability
                Stage s6 = Capture("06-readability");
                MeasureReadability(s6);
                s6.pass = s6.enemiesChecked > 0 && s6.raysCast == s6.enemiesChecked * ReadabilityPointsPerEnemy;
                s6.note = s6.enemiesChecked + " living bodies from the camera: onScreen=" + s6.enemiesOnScreen + " mostlyVisible(>=" + ReadabilityMostlyVisible + "/5)=" + s6.enemiesMostlyVisible +
                          " (" + (s6.fractionMostlyVisible * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%) fullyHidden=" + s6.enemiesFullyHidden +
                          " meanVisiblePoints=" + s6.meanVisiblePoints.ToString("F2", CultureInfo.InvariantCulture) + " meanScreenHeight=" + (s6.meanScreenHeightFraction * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%" +
                          " rays " + s6.raysBlocked + "/" + s6.raysCast + " blocked (env " + s6.raysBlockedByEnvironment + ", occluder " + s6.raysBlockedByCameraOccluder + ")" +
                          (s6.enemiesChecked == 0 ? " NO CAMERA OR NO LIVING ENEMY" : "");
                Write(s6);

                // ---------------------------------------------------------------- 07 captures
                string captureFile = "capture-gameplay-" + _scene + ".png";
                string capturePath = Path.Combine(_outDir, captureFile);
                ScreenCapture.CaptureScreenshot(capturePath);
                yield return null;
                t0 = Time.realtimeSinceStartup;
                while (!File.Exists(capturePath) && Time.realtimeSinceStartup - t0 < CaptureTimeoutSeconds)
                {
                    yield return null;
                }

                Stage s7 = Capture("07-captures");
                s7.captureFile = captureFile;
                s7.captureExists = File.Exists(capturePath);
                s7.captureWaitSeconds = Time.realtimeSinceStartup - t0;
                s7.pass = s7.captureExists;
                s7.note = captureFile + (s7.captureExists ? " written" : " MISSING") + " after " + s7.captureWaitSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s";
                Write(s7);

                // ---------------------------------------------------------------- 08 occluder fade
                // REQ-035/111 with the real prefabs, in a Player: the player is teleported
                // just north of the palace wall line (contract z 34.15, wall 6.5 m). The
                // rig (15 up, 13 back) then looks through the wall at the player, the
                // sphere cast hits a CameraOccluder collider and OccluderFadeState must
                // enable itself and drive the clone's alpha down. The same spot sits inside
                // the procedural palace in Run, so both scenes exercise the same path.
                // Evidence only: a Player build strips shader variants no material asset
                // references (PRA K-12) — this stage is what proves the transparent variant
                // survived, or not.
                Vector3 before = rig.transform.position;
                Vector3 behindWall = new Vector3(3f, 0.1f, 37f);
                rig.Teleport(behindWall, Quaternion.identity);
                float fadeT0 = Time.realtimeSinceStartup;
                float fadeSeconds = -1f;
                float minAlpha = 1f;
                int enabledStates = 0;
                int fadedRenderers = 0;
                while (Time.realtimeSinceStartup - fadeT0 < 3f)
                {
                    yield return null;
                    OccluderFadeState[] states = FindObjectsOfType<OccluderFadeState>();
                    enabledStates = 0;
                    for (int i = 0; i < states.Length; i++)
                    {
                        if (!states[i].enabled) continue;
                        enabledStates++;
                        if (states[i].Alpha < minAlpha) minAlpha = states[i].Alpha;
                    }

                    if (enabledStates > 0 && minAlpha < 0.99f && fadeSeconds < 0f)
                    {
                        fadeSeconds = Time.realtimeSinceStartup - fadeT0;
                    }

                    if (fadeSeconds >= 0f && Time.realtimeSinceStartup - fadeT0 > 1.5f)
                    {
                        break;
                    }
                }

                Renderer[] all = FindObjectsOfType<Renderer>();
                for (int i = 0; i < all.Length; i++)
                {
                    Material m = all[i].sharedMaterial;
                    if (m != null && m.name.Contains("(Occluder Fade)")) fadedRenderers++;
                }

                Stage s8f = Capture("08-occluder-fade");
                s8f.occluderTeleportPosition = behindWall;
                s8f.occluderStatesTotal = FindObjectsOfType<OccluderFadeState>().Length;
                s8f.occluderStatesEnabled = enabledStates;
                s8f.occluderRenderersFaded = fadedRenderers;
                s8f.occluderMinAlpha = minAlpha;
                s8f.occluderSecondsToFade = fadeSeconds;
                s8f.occluderFadeObserved = fadeSeconds >= 0f;

                // Back to where the window ran; the fade must release (alpha back to 1, clones dropped).
                rig.Teleport(before, Quaternion.identity);
                float releaseT0 = Time.realtimeSinceStartup;
                bool releasedOpaque = false;
                while (Time.realtimeSinceStartup - releaseT0 < 3f)
                {
                    yield return null;
                    OccluderFadeState[] states = FindObjectsOfType<OccluderFadeState>();
                    bool anyEnabled = false;
                    for (int i = 0; i < states.Length; i++)
                    {
                        if (states[i].enabled) { anyEnabled = true; break; }
                    }

                    if (!anyEnabled) { releasedOpaque = true; break; }
                }

                s8f.occluderRestoredOpaque = releasedOpaque;
                s8f.pass = s8f.occluderFadeObserved && releasedOpaque;
                s8f.note = "states " + s8f.occluderStatesEnabled + "/" + s8f.occluderStatesTotal + " enabled, faded renderers " + fadedRenderers +
                           ", min alpha " + minAlpha.ToString("F3", CultureInfo.InvariantCulture) +
                           (fadeSeconds >= 0f ? " after " + fadeSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s" : " (NO FADE within 3 s)") +
                           (releasedOpaque ? ", released" : ", NOT released within 3 s");

                // WP-078: the stage stages the top-down "behind a wall" case. In TP the camera pulls
                // in instead (FadeCameraLine), in FP nothing fades (None): not applicable there.
                CameraOccluderPolicy occluderPolicy = CameraModeService.CurrentProfile.OccluderPolicy;
                if (occluderPolicy != CameraOccluderPolicy.FadeAll)
                {
                    s8f.pass = true;
                    s8f.note = "N/A in " + CameraModeService.Current + " (occluder policy " + occluderPolicy + ", fade not expected) · " + s8f.note;
                }

                Write(s8f);

                // ---------------------------------------------------------------- 09 restore
                dir.ForceActiveTarget(0, false);
                Application.targetFrameRate = _targetFrameRateBefore;
                QualitySettings.lodBias = _lodBiasBefore;
                QualitySettings.vSyncCount = _vSyncCountBefore;
                yield return null;
                Stage s8 = Capture("09-restore");
                s8.restoredTargetFrameRate = Application.targetFrameRate;
                s8.restoredVSyncCount = QualitySettings.vSyncCount;
                s8.pass = s8.restoredTargetFrameRate == _targetFrameRateBefore && s8.restoredVSyncCount == _vSyncCountBefore;
                s8.note = "forced target released (target now " + s8.targetActive + "), cap " + s8.restoredTargetFrameRate + " vsync " + s8.restoredVSyncCount + " restored";
                Write(s8);

                WriteSummary();
                yield return new WaitForSecondsRealtime(0.5f);
                PrototypeUiFlow.Quit();
            }

            // ================================================================ measurements

            private void CountScene(Stage s)
            {
                Renderer[] renderers = FindObjectsOfType<Renderer>();
                s.rendererCount = renderers.Length;
                int visible = 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].isVisible) visible++;
                }

                s.renderersVisible = visible;
                LODGroup[] groups = FindObjectsOfType<LODGroup>();
                s.lodGroupCount = groups.Length;
                s.meshColliderCount = FindObjectsOfType<MeshCollider>().Length;
                s.boxColliderCount = FindObjectsOfType<BoxCollider>().Length;
                s.capsuleColliderCount = FindObjectsOfType<CapsuleCollider>().Length;

                NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
                s.navMeshTriangles = tri.indices != null ? tri.indices.Length / 3 : 0;
                s.navMeshVertices = tri.vertices != null ? tri.vertices.Length : 0;

                s.screenWidth = Screen.width;
                s.screenHeight = Screen.height;
                s.lodBias = QualitySettings.lodBias;
                s.qualityLevel = QualitySettings.GetQualityLevel();
                string[] names = QualitySettings.names;
                s.qualityName = s.qualityLevel >= 0 && s.qualityLevel < names.Length ? names[s.qualityLevel] : "?";

                Camera cam = ResolveCamera();
                if (cam == null)
                {
                    s.lodEstimateMethod = "no camera";
                    return;
                }

                s.cameraFov = cam.fieldOfView;
                Vector3 camPos = cam.transform.position;
                s.cameraX = camPos.x;
                s.cameraY = camPos.y;
                s.cameraZ = camPos.z;
                EstimateLods(groups, cam, s);
            }

            /// <summary>
            /// Which LOD each group would be showing from the current camera, by the same
            /// geometry the LODGroup uses (relative screen height = size / (2 d tan(fov/2)),
            /// scaled by lodBias). Unity exposes no runtime "current LOD" so this is an
            /// estimate and is labelled as one; the transition heights themselves are read
            /// from the groups, so a prefab authored differently is still counted right.
            /// </summary>
            /// <summary>WP-013 A/B switch (see AtmosphereFlag).</summary>
            public bool AtmosphereOff;

            /// <summary>WP-019 dense effect scenario (see VfxStressFlag).</summary>
            public bool VfxStress;
            private int _stressImpacts, _stressExplosions, _stressDeaths;
            private float _stressImpactNext, _stressExplosionNext, _stressDeathNext;
            private readonly DD.Prototype.Feedback.SurfaceKind[] _stressSurfaces =
            {
                DD.Prototype.Feedback.SurfaceKind.Stone, DD.Prototype.Feedback.SurfaceKind.Wood, DD.Prototype.Feedback.SurfaceKind.Dirt,
                DD.Prototype.Feedback.SurfaceKind.Flesh, DD.Prototype.Feedback.SurfaceKind.Metal
            };

            /// <summary>One frame of the stress driver: 10 impacts/s, 1 explosion/s, 5 deaths/s around the player.</summary>
            private void StepVfxStress(float now, PlayerRig rig)
            {
                DD.Prototype.Feedback.HitFeedbackDirector fx = DD.Prototype.Feedback.HitFeedbackDirector.Instance;
                if (fx == null || rig == null)
                {
                    return;
                }

                Vector3 centre = rig.transform.position;
                if (now >= _stressImpactNext)
                {
                    _stressImpactNext = now + 0.1f;
                    int i = _stressImpacts++;
                    float a = i * 2.399f;
                    Vector3 p = centre + new Vector3(Mathf.Cos(a) * (3f + (i % 5)), 0.6f + (i % 3) * 0.3f, Mathf.Sin(a) * (3f + (i % 5)));
                    if ((i & 1) == 0)
                    {
                        fx.ReportSurfaceImpact(p, Vector3.up, _stressSurfaces[i % _stressSurfaces.Length], 1f);
                    }
                    else
                    {
                        // WP-019b: every other impact is a flesh hit (blood: splash + droplets + marks), critical every 4th.
                        Vector3 toCentre = (centre - p).normalized;
                        var hit = new DD.Prototype.Damage.DamageResult { HealthDamage = (i % 4 == 3) ? 45f : 12f, Killed = false, Region = DD.Prototype.Damage.BodyRegion.UpperBody };
                        fx.ReportEnemyHit(new DD.Prototype.Feedback.EnemyHitFeedbackInfo
                        {
                            Point = p, Direction = -toCentre, Normal = toCentre, Result = hit,
                            Power = DD.Prototype.Feedback.WeaponPower.Medium, TargetId = 80000 + (i % 40), TargetRoot = null, Weight = DD.Prototype.Feedback.FeedbackWeight.Standard, ImpactForce = 1f
                        });
                    }
                }

                if (now >= _stressExplosionNext)
                {
                    _stressExplosionNext = now + 1f;
                    int i = _stressExplosions++;
                    float a = i * 1.7f;
                    fx.ReportExplosion(centre + new Vector3(Mathf.Cos(a) * 6f, 0.1f, Mathf.Sin(a) * 6f), 2.5f, 0, DD.Prototype.Feedback.ExplosionSource.Rocket);   // M-62: the launcher-grade blast (as before the source split)
                }

                if (now >= _stressDeathNext)
                {
                    _stressDeathNext = now + 0.2f;
                    int i = _stressDeaths++;
                    float a = i * 0.9f;
                    Vector3 feet = centre + new Vector3(Mathf.Cos(a) * (4f + (i % 4)), 0f, Mathf.Sin(a) * (4f + (i % 4)));
                    var r = new DD.Prototype.Damage.DamageResult { HealthDamage = 30f, Killed = true, Region = DD.Prototype.Damage.BodyRegion.UpperBody };
                    fx.ReportEnemyHit(new DD.Prototype.Feedback.EnemyHitFeedbackInfo
                    {
                        Point = feet + Vector3.up, Direction = Vector3.forward, Normal = Vector3.back, Result = r,
                        Power = DD.Prototype.Feedback.WeaponPower.Medium, TargetId = 90000 + i, TargetRoot = null, Weight = DD.Prototype.Feedback.FeedbackWeight.Standard, ImpactForce = 1f
                    });
                }
            }

            private void FillVfxStage(Stage s)
            {
                DD.Prototype.Feedback.HitFeedbackDirector fx = DD.Prototype.Feedback.HitFeedbackDirector.Instance;
                s.vfxStress = VfxStress;
                s.vfxImpacts = _stressImpacts;
                s.vfxExplosions = _stressExplosions;
                s.vfxDeaths = _stressDeaths;
                if (fx == null || fx.Vfx == null)
                {
                    return;
                }

                s.vfxLayerReady = fx.Vfx.IsReady;
                s.vfxPlayed = fx.Vfx.Played;
                s.vfxDecals = fx.Vfx.DecalsPlayed;
                if (fx.Vfx.FlipbookPool != null) { s.vfxFlipbookSpawned = fx.Vfx.FlipbookPool.CountSpawned; s.vfxFlipbookTotal = fx.Vfx.FlipbookPool.CountTotal; }
                if (fx.Vfx.DecalPool != null) { s.vfxDecalSpawned = fx.Vfx.DecalPool.CountSpawned; s.vfxDecalTotal = fx.Vfx.DecalPool.CountTotal; }
                DD.Prototype.Feedback.Vfx.BloodSystem blood = fx.Vfx.Blood;
                if (blood != null)
                {
                    s.bloodReady = blood.IsReady;
                    s.bloodSplashes = blood.Splashes; s.bloodDropletBursts = blood.DropletBursts; s.bloodMarks = blood.Marks; s.bloodPools = blood.Pools;
                    s.bloodMerged = blood.Merged; s.bloodEvicted = blood.Evicted; s.bloodSkippedGold = blood.SkippedForGold; s.bloodLive = blood.LiveMarks;
                    s.bloodMergedQuads = blood.Merger != null ? blood.Merger.MergedQuads : 0;
                    if (blood.DecalPool != null) { s.bloodDecalSpawned = blood.DecalPool.CountSpawned; s.bloodDecalTotal = blood.DecalPool.CountTotal; }
                }
            }

            /// <summary>
            /// WP-019b · 04d: GPU time of the URP draw passes (Recorder.gpuElapsedNanoseconds; a development
            /// build with the Profiler switched on for this sub-window only, so the 04 frame statistics stay
            /// unbiased). Effect GPU = transparents(stress) − transparents(base) across the A/B pair.
            /// -1 when the platform reports no GPU samples.
            /// </summary>
            private IEnumerator MeasureGpu(Stage s, PlayerRig rig, float seconds)
            {
                bool wasEnabled = UnityEngine.Profiling.Profiler.enabled;
                UnityEngine.Profiling.Profiler.enabled = true;
                UnityEngine.Profiling.Recorder transparents = UnityEngine.Profiling.Recorder.Get("DrawTransparentObjects");
                UnityEngine.Profiling.Recorder opaques = UnityEngine.Profiling.Recorder.Get("DrawOpaqueObjects");
                UnityEngine.Profiling.Recorder camera = UnityEngine.Profiling.Recorder.Get("Camera.Render");
                transparents.enabled = true; opaques.enabled = true; camera.enabled = true;
                double tSum = 0, tMax = 0, oSum = 0, cSum = 0, frameSum = 0;
                double gpuFrameSum = 0, gpuFrameMax = 0;
                int n = 0, frameTimingSamples = 0, frames = 0;
                var timings = new FrameTiming[4];
                float t0 = Time.realtimeSinceStartup;
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                while (Time.realtimeSinceStartup - t0 < seconds)
                {
                    if (VfxStress) StepVfxStress(Time.realtimeSinceStartup, rig);
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    frames++;
                    frameSum += Time.unscaledDeltaTime * 1000.0;
                    uint timingCount = FrameTimingManager.GetLatestTimings((uint)timings.Length, timings);
                    if (timingCount > 0)
                    {
                        double gpuMs = timings[(int)timingCount - 1].gpuFrameTime;
                        if (gpuMs > 0.0 && !double.IsNaN(gpuMs) && !double.IsInfinity(gpuMs))
                        {
                            gpuFrameSum += gpuMs;
                            if (gpuMs > gpuFrameMax) gpuFrameMax = gpuMs;
                            frameTimingSamples++;
                        }
                    }
                    if (transparents.gpuSampleBlockCount > 0)
                    {
                        double t = transparents.gpuElapsedNanoseconds / 1e6;
                        tSum += t; if (t > tMax) tMax = t;
                        oSum += opaques.gpuSampleBlockCount > 0 ? opaques.gpuElapsedNanoseconds / 1e6 : 0;
                        cSum += camera.gpuSampleBlockCount > 0 ? camera.gpuElapsedNanoseconds / 1e6 : 0;
                        n++;
                    }
                }

                UnityEngine.Profiling.Profiler.enabled = wasEnabled;
                s.gpuSubWindowSeconds = Time.realtimeSinceStartup - t0;
                s.gpuSampledFrames = frameTimingSamples;
                s.gpuMeasurementSource = frameTimingSamples > 0 ? "FrameTimingManager" : "unavailable";
                s.gpuSubWindowFrameMeanMs = frames > 0 ? (float)(frameSum / frames) : -1f;
                if (frameTimingSamples > 0)
                {
                    s.gpuFrameAvgMs = (float)(gpuFrameSum / frameTimingSamples);
                    s.gpuFrameMaxMs = (float)gpuFrameMax;
                }
                if (n > 0)
                {
                    s.gpuTransparentsAvgMs = (float)(tSum / n);
                    s.gpuTransparentsMaxMs = (float)tMax;
                    s.gpuOpaquesAvgMs = (float)(oSum / n);
                    s.gpuCameraAvgMs = (float)(cSum / n);
                }
            }

            /// <summary>WP-068 HUD A/B switch (see HudAbFlag).</summary>
            public bool HudAB;

            /// <summary>The HUD budget (C-00 24.09, DEC-088 margin 0.10-0.18 ms): UI CPU + GPU per frame at 1080p under 40 bodies.</summary>
            public const float HudBudgetMs = 0.35f;
            private const int HudBlockCount = 10;         // on, off × 5: crowd drift cancels across the pairs, and five pair deltas allow a trimmed mean
            private const float HudBlockSeconds = 3f;
            private const float HudSettleSeconds = 0.75f; // the rebuild after a switch and the FrameTimingManager lag stay out
            private static readonly string[] HudMarkerNames =
            {
                "Update.ScriptRunBehaviourUpdate",
                "PreLateUpdate.ScriptRunBehaviourLateUpdate",
                "PostLateUpdate.PlayerUpdateCanvases",
                "PostLateUpdate.PlayerEmitCanvasGeometry",
                "UGUI.Rendering.RenderOverlays",
            };

            private readonly FrameTiming[] _hudTimings = new FrameTiming[1];

            /// <summary>
            /// WP-068 · 04h: the run HUD's frame cost. The HUD root is switched off and on in alternating
            /// blocks under the same load (Profiler off, so the numbers are the Player's own). Per block:
            /// frame ms, FrameTimingManager main-thread and GPU ms, the main-thread UI markers (sum per frame,
            /// recorded through ProfilerRecorder handles found by name) and the SetPass / batch / draw counters.
            /// HUD cost = Δscripts + UI markers with the HUD on (all canvases, so it is an upper bound) + the UI pass GPU time
            /// (UGUI.Rendering.RenderOverlays, a short Profiler sub-window; the trimmed mean on/off frame-GPU delta when unavailable).
            /// </summary>
            private IEnumerator MeasureHud(Stage s, PlayerRig rig)
            {
                s.hudBudgetMs = HudBudgetMs;
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                if (hud == null)
                {
                    s.pass = false;
                    s.note = "no RunHudScreen in the run";
                    yield break;
                }

                GameObject root = hud.gameObject;
                s.hudCanvases = root.GetComponentsInChildren<Canvas>(false).Length;
                s.hudGraphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false).Length;
                s.hudTexts = root.GetComponentsInChildren<TMPro.TMP_Text>(false).Length;
                s.hudDrawInventory = HudDrawInventory(root, out s.hudHiddenGraphics);

                var available = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(available);
                var markers = new ProfilerRecorder[HudMarkerNames.Length];
                var found = new StringBuilder();
                for (int m = 0; m < HudMarkerNames.Length; m++)
                {
                    for (int h = 0; h < available.Count; h++)
                    {
                        if (ProfilerRecorderHandle.GetDescription(available[h]).Name == HudMarkerNames[m])
                        {
                            markers[m] = new ProfilerRecorder(available[h], 1, ProfilerRecorderOptions.Default);
                            markers[m].Start();
                            break;
                        }
                    }

                    found.Append(markers[m].Valid ? "+" : "-").Append(HudMarkerNames[m]).Append(' ');
                }

                s.hudMarkersFound = found.ToString().Trim();
                ProfilerRecorder setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                ProfilerRecorder batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                ProfilerRecorder draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");

                // [0] = HUD on, [1] = HUD off
                var frames = new int[2];
                var frameSum = new double[2];
                var gpuN = new int[2];
                var blockGpuSum = new double[HudBlockCount];
                var blockGpuN = new int[HudBlockCount];
                var gpuSum = new double[2];
                var cpuN = new int[2];
                var cpuSum = new double[2];
                var markerSum = new double[2, HudMarkerNames.Length];
                var counterSum = new double[2, 3];

                for (int block = 0; block < HudBlockCount; block++)
                {
                    int k = block % 2;
                    root.SetActive(k == 0);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < HudSettleSeconds + HudBlockSeconds)
                    {
                        if (VfxStress) StepVfxStress(Time.realtimeSinceStartup, rig);
                        yield return null;
                        if (Time.realtimeSinceStartup - t0 < HudSettleSeconds)
                        {
                            continue;
                        }

                        frames[k]++;
                        frameSum[k] += Time.unscaledDeltaTime * 1000.0;
                        for (int m = 0; m < markers.Length; m++)
                        {
                            if (markers[m].Valid)
                            {
                                markerSum[k, m] += markers[m].LastValue / 1e6;
                            }
                        }

                        counterSum[k, 0] += setPass.Valid ? setPass.LastValue : 0;
                        counterSum[k, 1] += batches.Valid ? batches.LastValue : 0;
                        counterSum[k, 2] += draws.Valid ? draws.LastValue : 0;

                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, _hudTimings) > 0)
                        {
                            if (_hudTimings[0].gpuFrameTime > 0d)
                            {
                                gpuSum[k] += _hudTimings[0].gpuFrameTime;
                                gpuN[k]++;
                                blockGpuSum[block] += _hudTimings[0].gpuFrameTime;
                                blockGpuN[block]++;
                            }

                            if (_hudTimings[0].cpuMainThreadFrameTime > 0d)
                            {
                                cpuSum[k] += _hudTimings[0].cpuMainThreadFrameTime;
                                cpuN[k]++;
                            }
                        }
                    }
                }

                root.SetActive(true);

                // One more block: the HUD active but under a CanvasGroup at alpha 0. If the draw counters stay at the
                // "on" level, graphics hidden by alpha are still submitted, and hiding by SetActive/enabled is the saving.
                bool addedGroup = root.GetComponent<CanvasGroup>() == null;
                CanvasGroup veil = addedGroup ? root.AddComponent<CanvasGroup>() : root.GetComponent<CanvasGroup>();
                float oldVeil = veil.alpha;
                veil.alpha = 0f;
                double zSet = 0, zDraw = 0;
                int zFrames = 0;
                float tz = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tz < HudSettleSeconds + 2f)
                {
                    yield return null;
                    if (Time.realtimeSinceStartup - tz < HudSettleSeconds)
                    {
                        continue;
                    }

                    zFrames++;
                    zSet += setPass.Valid ? setPass.LastValue : 0;
                    zDraw += draws.Valid ? draws.LastValue : 0;
                }

                veil.alpha = oldVeil;
                if (addedGroup)
                {
                    Destroy(veil);
                }

                if (zFrames > 0)
                {
                    s.hudAlphaZeroSetPass = (float)(zSet / zFrames);
                    s.hudAlphaZeroDrawCalls = (float)(zDraw / zFrames);
                }

                // The GPU time of the UI pass itself ("GPU UI pass"): the Profiler on for this sub-window only
                // (as 04d), HUD on, so every overlay canvas is in it. The whole-frame GPU delta above is kept as a
                // fallback; on a shared GPU (WindowServer, other windows) it swings by ±0.1 ms between runs.
                bool profilerWas = UnityEngine.Profiling.Profiler.enabled;
                UnityEngine.Profiling.Profiler.enabled = true;
                UnityEngine.Profiling.Recorder uiGpu = UnityEngine.Profiling.Recorder.Get("UGUI.Rendering.RenderOverlays");
                uiGpu.enabled = true;
                double uiGpuSum = 0;
                int uiGpuN = 0;
                float tu = Time.realtimeSinceStartup;
                yield return null;
                while (Time.realtimeSinceStartup - tu < 3f)
                {
                    yield return null;
                    if (uiGpu.gpuSampleBlockCount > 0)
                    {
                        uiGpuSum += uiGpu.gpuElapsedNanoseconds / 1e6;
                        uiGpuN++;
                    }
                }

                uiGpu.enabled = false;
                UnityEngine.Profiling.Profiler.enabled = profilerWas;
                s.hudGpuUiSamples = uiGpuN;
                s.hudGpuUiMs = uiGpuN > 0 ? (float)(uiGpuSum / uiGpuN) : -1f;

                var valid = new bool[markers.Length];
                for (int m = 0; m < markers.Length; m++)
                {
                    valid[m] = markers[m].Valid;
                    if (valid[m]) markers[m].Dispose();
                }

                if (setPass.Valid) setPass.Dispose();
                if (batches.Valid) batches.Dispose();
                if (draws.Valid) draws.Dispose();

                s.hudBlocks = HudBlockCount;
                s.hudFramesOn = frames[0];
                s.hudFramesOff = frames[1];
                if (frames[0] == 0 || frames[1] == 0)
                {
                    s.pass = false;
                    s.note = "HUD A/B: no frames";
                    yield break;
                }

                s.hudFrameOnMs = (float)(frameSum[0] / frames[0]);
                s.hudFrameOffMs = (float)(frameSum[1] / frames[1]);
                s.hudFrameDeltaMs = s.hudFrameOnMs - s.hudFrameOffMs;
                if (cpuN[0] > 0 && cpuN[1] > 0)
                {
                    s.hudCpuMainOnMs = (float)(cpuSum[0] / cpuN[0]);
                    s.hudCpuMainOffMs = (float)(cpuSum[1] / cpuN[1]);
                }

                if (gpuN[0] > 0 && gpuN[1] > 0)
                {
                    s.hudGpuOnMs = (float)(gpuSum[0] / gpuN[0]);
                    s.hudGpuOffMs = (float)(gpuSum[1] / gpuN[1]);
                    s.hudGpuDeltaMs = s.hudGpuOnMs - s.hudGpuOffMs;
                    var pairs = new List<float>();
                    for (int b = 0; b + 1 < HudBlockCount; b += 2)
                    {
                        if (blockGpuN[b] > 0 && blockGpuN[b + 1] > 0)
                        {
                            pairs.Add((float)(blockGpuSum[b] / blockGpuN[b] - blockGpuSum[b + 1] / blockGpuN[b + 1]));
                        }
                    }

                    // Trimmed mean of the pair deltas (the highest and lowest dropped once there are five): one pair that
                    // caught a WindowServer burst on the shared GPU no longer decides the term.
                    pairs.Sort();
                    int drop = pairs.Count >= 5 ? 1 : 0;
                    double kept = 0;
                    for (int i = drop; i < pairs.Count - drop; i++) kept += pairs[i];
                    s.hudGpuDeltaRobustMs = pairs.Count - 2 * drop > 0 ? (float)(kept / (pairs.Count - 2 * drop)) : s.hudGpuDeltaMs;
                }

                float MarkerAvg(int k2, int m) => valid[m] ? (float)(markerSum[k2, m] / frames[k2]) : -1f;
                s.hudScriptsOnMs = MarkerAvg(0, 0) + MarkerAvg(0, 1);
                s.hudScriptsOffMs = MarkerAvg(1, 0) + MarkerAvg(1, 1);
                s.hudCanvasUpdateOnMs = MarkerAvg(0, 2);
                s.hudCanvasUpdateOffMs = MarkerAvg(1, 2);
                s.hudEmitGeometryOnMs = MarkerAvg(0, 3);
                s.hudEmitGeometryOffMs = MarkerAvg(1, 3);
                s.hudRenderOverlaysOnMs = MarkerAvg(0, 4);
                s.hudRenderOverlaysOffMs = MarkerAvg(1, 4);
                s.hudSetPassOn = (float)(counterSum[0, 0] / frames[0]);
                s.hudSetPassOff = (float)(counterSum[1, 0] / frames[1]);
                s.hudBatchesOn = (float)(counterSum[0, 1] / frames[0]);
                s.hudBatchesOff = (float)(counterSum[1, 1] / frames[1]);
                s.hudDrawCallsOn = (float)(counterSum[0, 2] / frames[0]);
                s.hudDrawCallsOff = (float)(counterSum[1, 2] / frames[1]);

                bool markersOk = valid[0] && valid[1] && valid[2] && valid[3];
                s.hudCpuMs = Mathf.Max(0f, s.hudScriptsOnMs - s.hudScriptsOffMs) +
                             Mathf.Max(0f, s.hudCanvasUpdateOnMs) + Mathf.Max(0f, s.hudEmitGeometryOnMs) + Mathf.Max(0f, s.hudRenderOverlaysOnMs);
                // GPU term: the UI pass's own GPU time when the platform reports it, else the trimmed mean on/off pair delta.
                s.hudGpuTermMs = s.hudGpuUiMs >= 0f ? s.hudGpuUiMs : Mathf.Max(0f, s.hudGpuDeltaRobustMs);
                s.hudCostMs = s.hudCpuMs + s.hudGpuTermMs;
                // PASS = the measurement saw what it had to (markers and GPU samples); the budget verdict is the aggregate's.
                s.pass = markersOk && (uiGpuN > 0 || gpuN[0] > 0);
                s.note = "HUD cost " + F3(s.hudCostMs) + " ms (budget " + F3(HudBudgetMs) + ") = cpu " + F3(s.hudCpuMs) +
                         " [scripts Δ" + F3(s.hudScriptsOnMs - s.hudScriptsOffMs) + " canvas " + F3(s.hudCanvasUpdateOnMs) + "/" + F3(s.hudCanvasUpdateOffMs) +
                         " emit " + F3(s.hudEmitGeometryOnMs) + "/" + F3(s.hudEmitGeometryOffMs) + " overlays " + F3(s.hudRenderOverlaysOnMs) + "/" + F3(s.hudRenderOverlaysOffMs) +
                         "] + gpu " + F3(s.hudGpuTermMs) + " (UI pass " + F3(s.hudGpuUiMs) + " over " + uiGpuN + " frames; frame Δ" + F3(s.hudGpuDeltaMs) + " trimmed Δ" + F3(s.hudGpuDeltaRobustMs) +
                         " " + F3(s.hudGpuOnMs) + "/" + F3(s.hudGpuOffMs) + ") | frame " + F3(s.hudFrameOnMs) + "/" + F3(s.hudFrameOffMs) +
                         " Δ" + F3(s.hudFrameDeltaMs) + " | setpass " + s.hudSetPassOn.ToString("F1", CultureInfo.InvariantCulture) + "/" + s.hudSetPassOff.ToString("F1", CultureInfo.InvariantCulture) +
                         " batches " + s.hudBatchesOn.ToString("F1", CultureInfo.InvariantCulture) + "/" + s.hudBatchesOff.ToString("F1", CultureInfo.InvariantCulture) +
                         " | canvases " + s.hudCanvases + " graphics " + s.hudGraphics + " (hidden " + s.hudHiddenGraphics + ") texts " + s.hudTexts +
                         " | alpha-0 setpass " + s.hudAlphaZeroSetPass.ToString("F1", CultureInfo.InvariantCulture) + " draws " + s.hudAlphaZeroDrawCalls.ToString("F1", CultureInfo.InvariantCulture) +
                         " | " + s.hudMarkersFound;
            }

            private static string F3(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

            /// <summary>The HUD's graphics grouped by draw state (canvas, material, texture), hidden ones apart: what each batch is made of.</summary>
            private static string HudDrawInventory(GameObject root, out int hidden)
            {
                hidden = 0;
                var groups = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (UnityEngine.UI.Graphic g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                {
                    if (!g.enabled)
                    {
                        continue;
                    }

                    CanvasRenderer cr = g.canvasRenderer;
                    bool invisible = g.color.a < 0.001f || cr.GetAlpha() < 0.001f || cr.GetInheritedAlpha() < 0.001f;
                    if (invisible)
                    {
                        hidden++;
                    }

                    Material m = g.materialForRendering;
                    Texture t = g.mainTexture;
                    var image = g as UnityEngine.UI.Image;
                    string tex = t == null ? "-" : (!string.IsNullOrEmpty(t.name) ? t.name : "tex#" + t.GetInstanceID());
                    if (image != null && image.sprite != null && string.IsNullOrEmpty(t != null ? t.name : null))
                    {
                        tex += "(" + image.sprite.name + ")";
                    }

                    string key = (g.canvas != null ? g.canvas.name : "?") + "|" + (m != null ? m.name : "?") + "|" + tex + (invisible ? "|hidden" : "");
                    int n;
                    groups[key] = groups.TryGetValue(key, out n) ? n + 1 : 1;
                }

                var sb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in groups)
                {
                    sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append("; ");
                }

                return sb.ToString();
            }

            /// <summary>WP-017 · Fit helmet + body + leg on every live zombie that is not fully equipped (see ArmorFlag). Returns pieces fitted.</summary>
            public bool ArmorAll;

            private int FitArmorOnCrowd()
            {
                int fitted = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || z.Armor == null || z.Armor.EquippedCount >= 3) continue;
                    fitted += z.FitArmorComposition(true, true, true);
                }

                return fitted;
            }

            private static void SetAtmosphere(bool on)
            {
                foreach (WindDriver wind in FindObjectsOfType<WindDriver>())
                {
                    wind.EnabledScale = on ? 1f : 0f;
                }

                foreach (AtmosphereDrift drift in FindObjectsOfType<AtmosphereDrift>())
                {
                    drift.SetEnabledScale(on ? 1f : 0f);
                    if (!on)
                    {
                        drift.System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }

                Camera cam = ResolveCamera();
                if (cam != null)
                {
                    cam.GetUniversalAdditionalCameraData().renderPostProcessing = on && WorldAtmosphere.PostFx.Enabled;
                }
            }

            private static bool PostProcessingOn()
            {
                Camera cam = ResolveCamera();
                return cam != null && cam.GetUniversalAdditionalCameraData().renderPostProcessing;
            }

            private static int CountDriftFields(out int alive)
            {
                int fields = 0;
                alive = 0;
                foreach (AtmosphereDrift drift in FindObjectsOfType<AtmosphereDrift>())
                {
                    fields++;
                    alive += drift.System.particleCount;
                }

                return fields;
            }

            private static void EstimateLods(LODGroup[] groups, Camera cam, Stage s)
            {
                float halfTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float bias = Mathf.Max(0.0001f, QualitySettings.lodBias);
                Vector3 camPos = cam.transform.position;
                int l0 = 0, l1 = 0, l2 = 0, culled = 0;
                for (int g = 0; g < groups.Length; g++)
                {
                    LODGroup group = groups[g];
                    LOD[] lods = group.GetLODs();
                    if (lods == null || lods.Length == 0)
                    {
                        continue;
                    }

                    Vector3 scale = group.transform.lossyScale;
                    float worldSize = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    float distance = Vector3.Distance(camPos, group.transform.TransformPoint(group.localReferencePoint));
                    float relative = distance > 0.0001f ? (worldSize * 0.5f) / (distance * halfTan) * bias : 1f;

                    int index = -1;
                    for (int i = 0; i < lods.Length; i++)
                    {
                        if (relative >= lods[i].screenRelativeTransitionHeight)
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index == 0) l0++;
                    else if (index == 1) l1++;
                    else if (index >= 2) l2++;
                    else culled++;
                }

                s.lodEstimated0 = l0;
                s.lodEstimated1 = l1;
                s.lodEstimated2 = l2;
                s.lodEstimatedCulled = culled;
                s.lodEstimateMethod = "estimated: size/(2*d*tan(fov/2))*lodBias vs screenRelativeTransitionHeight";
            }

            private static void ComputeFrameStats(List<float> samples, Stage s)
            {
                int n = samples.Count;
                if (n == 0)
                {
                    return;
                }

                float[] sorted = samples.ToArray();
                Array.Sort(sorted);
                double sum = 0d;
                int over16 = 0, over33 = 0;
                for (int i = 0; i < n; i++)
                {
                    sum += sorted[i];
                    if (sorted[i] > FrameBudget60Ms) over16++;
                    if (sorted[i] > FrameBudget30Ms) over33++;
                }

                s.frameSumMs = (float)sum;
                s.frameMeanMs = (float)(sum / n);
                s.frameMedianMs = (n & 1) == 1 ? sorted[n / 2] : 0.5f * (sorted[n / 2 - 1] + sorted[n / 2]);
                s.frameP99Ms = Percentile(sorted, 0.99f);
                s.frameP999Ms = Percentile(sorted, 0.999f);
                // WP-014: the stall-filtered p99 — frames above two 60 Hz intervals (33.33 ms)
                // are presentation stalls of the host (another window, a batch job), not the
                // scene; they are counted separately and excluded from this percentile.
                int kept = 0;
                for (int i = 0; i < n; i++) if (sorted[i] <= FrameBudget30Ms) kept++;
                s.frameP99FilteredMs = kept > 0 ? Percentile(sorted, Mathf.Min(0.99f * kept / n, 1f)) : 0f;
                s.framesExcludedAsStalls = n - kept;
                s.frameWorst1PctMeanMs = WorstMean(sorted, 0.01f);
                s.frameWorst01PctMeanMs = WorstMean(sorted, 0.001f);
                s.frameMaxMs = sorted[n - 1];
                s.frameMinMs = sorted[0];
                s.framesOver16_667Ms = over16;
                s.fractionOver16_667Ms = (float)over16 / n;
                s.framesOver33_333Ms = over33;
                s.fractionOver33_333Ms = (float)over33 / n;
                s.avgFps = sum > 0d ? (float)(n / (sum / 1000d)) : 0f;
            }

            private static float Percentile(float[] sorted, float p)
            {
                int index = Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1);
                return sorted[index];
            }

            /// <summary>Mean of the worst <paramref name="fraction"/> of frames (at least one frame) - the "1% low" convention.</summary>
            private static float WorstMean(float[] sorted, float fraction)
            {
                int count = Mathf.Max(1, Mathf.FloorToInt(fraction * sorted.Length));
                double sum = 0d;
                for (int i = sorted.Length - count; i < sorted.Length; i++)
                {
                    sum += sorted[i];
                }

                return (float)(sum / count);
            }

            private static void AssignCounters(Counter[] c, Stage s)
            {
                s.drawCallsAvg = c[0].Avg; s.drawCallsMax = c[0].MaxOrMinusOne;
                s.setPassCallsAvg = c[1].Avg; s.setPassCallsMax = c[1].MaxOrMinusOne;
                s.trianglesAvg = c[2].Avg; s.trianglesMax = c[2].MaxOrMinusOne;
                s.verticesAvg = c[3].Avg; s.verticesMax = c[3].MaxOrMinusOne;
                s.batchesAvg = c[4].Avg; s.batchesMax = c[4].MaxOrMinusOne;
                s.textureMemoryAvg = c[5].Avg; s.textureMemoryMax = c[5].MaxOrMinusOne;
                s.meshMemoryAvg = c[6].Avg; s.meshMemoryMax = c[6].MaxOrMinusOne;
                s.gcReservedMemoryAvg = c[7].Avg; s.gcReservedMemoryMax = c[7].MaxOrMinusOne;
                s.gcUsedMemoryAvg = c[8].Avg; s.gcUsedMemoryMax = c[8].MaxOrMinusOne;
                s.systemUsedMemoryAvg = c[9].Avg; s.systemUsedMemoryMax = c[9].MaxOrMinusOne;
                s.totalUsedMemoryAvg = c[10].Avg; s.totalUsedMemoryMax = c[10].MaxOrMinusOne;
                s.totalReservedMemoryAvg = c[11].Avg; s.totalReservedMemoryMax = c[11].MaxOrMinusOne;
                s.gcAllocatedInFrameAvg = c[12].Avg; s.gcAllocatedInFrameMax = c[12].MaxOrMinusOne;
                s.gcAllocatedInWindowTotal = c[12].Valid ? (long)c[12].Sum : -1L;
                s.mainThreadCpuAvgMs = c[13].Valid ? c[13].Avg / 1000000d : -1d;
                s.mainThreadCpuMaxMs = c[13].Valid ? c[13].MaxOrMinusOne / 1000000d : -1d;
                s.renderThreadCpuAvgMs = c[14].Valid ? c[14].Avg / 1000000d : -1d;
                s.renderThreadCpuMaxMs = c[14].Valid ? c[14].MaxOrMinusOne / 1000000d : -1d;

                var unavailable = new StringBuilder();
                for (int i = 0; i < c.Length; i++)
                {
                    if (c[i].Valid) continue;
                    if (unavailable.Length > 0) unavailable.Append(',');
                    unavailable.Append(c[i].Name);
                }

                s.unavailableCounters = unavailable.Length > 0 ? unavailable.ToString() : string.Empty;
            }

            /// <summary>
            /// Five rays per living body from the camera position: feet + 0.2 m, mid, head,
            /// left and right shoulder, against the layers that can stand between the
            /// camera and the fight (Environment | CameraOccluder - the aim-block mask). A
            /// blocked ray is what the player cannot see; the fader may later make an
            /// occluder translucent, and that ray is counted separately so the report can
            /// tell the two apart.
            /// </summary>
            private void MeasureReadability(Stage s)
            {
                s.occlusionLayers = PrototypeLayers.EnvironmentName + "|" + PrototypeLayers.CameraOccluderName;
                Camera cam = ResolveCamera();
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                var visiblePoints = new List<int>(live.Count);
                var onScreen = new List<int>(live.Count);
                var screenHeight = new List<float>(live.Count);
                var distance = new List<float>(live.Count);
                if (cam == null)
                {
                    Debug.LogError("[PilotPerfProbe] readability: no camera");
                }

                int mask = PrototypeLayers.AimBlockMask;
                int environmentLayer = PrototypeLayers.Environment;
                int occluderLayer = PrototypeLayers.CameraOccluder;
                Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                int raysCast = 0, raysBlocked = 0, blockedEnv = 0, blockedOcc = 0;
                int mostlyVisible = 0, fullyHidden = 0, screenCount = 0, behind = 0;
                double heightSum = 0d;
                int heightSamples = 0;
                RaycastHit hit;
                Vector3[] points = new Vector3[ReadabilityPointsPerEnemy];

                for (int e = 0; cam != null && e < live.Count; e++)
                {
                    ZombieEnemy z = live[e];
                    if (z == null || !z.IsAlive)
                    {
                        continue;
                    }

                    Transform t = z.transform;
                    Vector3 feet = t.position;
                    float radius = Mathf.Max(0.05f, z.BodyRadius);
                    float height = ZombieBodyBuilder.StandHeight;
                    CharacterController controller = z.GetComponent<CharacterController>();
                    if (controller != null && controller.height > 0.1f)
                    {
                        height = controller.height;
                    }

                    Vector3 up = Vector3.up;
                    Vector3 right = t.right;
                    points[0] = feet + up * 0.2f;
                    points[1] = feet + up * (height * 0.5f);
                    points[2] = feet + up * (height - 0.1f);
                    points[3] = feet + up * (height * 0.75f) - right * radius;
                    points[4] = feet + up * (height * 0.75f) + right * radius;

                    int visible = 0;
                    for (int p = 0; p < points.Length; p++)
                    {
                        Vector3 delta = points[p] - camPos;
                        float length = delta.magnitude;
                        raysCast++;
                        if (length <= 0.001f)
                        {
                            visible++;
                            continue;
                        }

                        if (Physics.Raycast(camPos, delta / length, out hit, length, mask, QueryTriggerInteraction.Ignore))
                        {
                            raysBlocked++;
                            int layer = hit.collider.gameObject.layer;
                            if (layer == environmentLayer) blockedEnv++;
                            else if (layer == occluderLayer) blockedOcc++;
                        }
                        else
                        {
                            visible++;
                        }
                    }

                    if (visible >= ReadabilityMostlyVisible) mostlyVisible++;
                    if (visible == 0) fullyHidden++;

                    Vector3 vpFeet = cam.WorldToViewportPoint(feet);
                    Vector3 vpHead = cam.WorldToViewportPoint(points[2]);
                    Vector3 vpMid = cam.WorldToViewportPoint(points[1]);
                    bool inFront = vpMid.z > 0f;
                    bool isOnScreen = inFront && vpMid.x >= 0f && vpMid.x <= 1f && vpMid.y >= 0f && vpMid.y <= 1f;
                    float fraction = inFront && vpFeet.z > 0f && vpHead.z > 0f ? Mathf.Abs(vpHead.y - vpFeet.y) : 0f;
                    if (!inFront) behind++;
                    if (isOnScreen)
                    {
                        screenCount++;
                        heightSum += fraction;
                        heightSamples++;
                    }

                    visiblePoints.Add(visible);
                    onScreen.Add(isOnScreen ? 1 : 0);
                    screenHeight.Add(fraction);
                    distance.Add(Vector3.Distance(camPos, points[1]));
                }

                int checkedCount = visiblePoints.Count;
                double pointSum = 0d;
                for (int i = 0; i < checkedCount; i++) pointSum += visiblePoints[i];

                s.enemiesChecked = checkedCount;
                s.enemiesOnScreen = screenCount;
                s.enemiesBehindCamera = behind;
                s.enemiesMostlyVisible = mostlyVisible;
                s.enemiesFullyHidden = fullyHidden;
                s.fractionMostlyVisible = checkedCount > 0 ? (float)mostlyVisible / checkedCount : 0f;
                s.meanVisiblePoints = checkedCount > 0 ? (float)(pointSum / checkedCount) : 0f;
                s.meanScreenHeightFraction = heightSamples > 0 ? (float)(heightSum / heightSamples) : 0f;
                s.raysCast = raysCast;
                s.raysBlocked = raysBlocked;
                s.raysBlockedByEnvironment = blockedEnv;
                s.raysBlockedByCameraOccluder = blockedOcc;
                s.perEnemyVisiblePoints = visiblePoints.ToArray();
                s.perEnemyOnScreen = onScreen.ToArray();
                s.perEnemyScreenHeight = screenHeight.ToArray();
                s.perEnemyDistance = distance.ToArray();
                if (cam != null)
                {
                    s.cameraFov = cam.fieldOfView;
                    s.cameraX = camPos.x;
                    s.cameraY = camPos.y;
                    s.cameraZ = camPos.z;
                }
            }

            private static Camera ResolveCamera()
            {
                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                if (rig != null && rig.Camera != null)
                {
                    return rig.Camera;
                }

                return Camera.main;
            }

            /// <summary>WP-017 · every loaded Mesh grouped by name prefix, with the resident size the profiler attributes to it.</summary>
            private void MeasureMeshInventory(Stage s)
            {
                Mesh[] all = Resources.FindObjectsOfTypeAll<Mesh>();
                var top = new List<KeyValuePair<string, long>>();
                for (int i = 0; i < all.Length; i++)
                {
                    Mesh m = all[i];
                    if (m == null) continue;
                    long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);
                    string n = m.name ?? string.Empty;
                    s.meshCount++;
                    s.meshBytes += bytes;
                    if (n.StartsWith("zombie_armor_", StringComparison.Ordinal)) { s.meshArmorCount++; s.meshArmorBytes += bytes; }
                    else if (n.StartsWith("zombie_", StringComparison.Ordinal)) { s.meshZombieCount++; s.meshZombieBytes += bytes; }
                    else if (n.StartsWith("Player_", StringComparison.Ordinal)) s.meshPlayerBytes += bytes;
                    else if (n.StartsWith("AP0", StringComparison.Ordinal) || n.StartsWith("SM_", StringComparison.Ordinal) || n.Contains("_LOD") || n.StartsWith("Combined Mesh", StringComparison.Ordinal)) { s.meshWorldCount++; s.meshWorldBytes += bytes; }
                    else s.meshOtherBytes += bytes;
                    top.Add(new KeyValuePair<string, long>(n + "(" + (m.vertexCount) + "v)", bytes));
                }

                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new StringBuilder();
                for (int i = 0; i < Mathf.Min(10, top.Count); i++) sb.Append(i > 0 ? ", " : string.Empty).Append(top[i].Key).Append(' ').Append(Mb(top[i].Value));
                s.meshTop = sb.ToString();
            }

            /// <summary>WP-015 · every loaded Texture by kind, with the resident size the profiler attributes to it; art = the Import Contract set (T_*).</summary>
            private void MeasureTextureInventory(Stage s)
            {
                var sb = new StringBuilder();
                sb.AppendLine("name,kind,width,height,format,mipmapCount,streaming,requestedMip,loadedMip,bytes");
                Texture[] all = Resources.FindObjectsOfTypeAll<Texture>();
                for (int i = 0; i < all.Length; i++)
                {
                    Texture t = all[i];
                    if (t == null) continue;
                    long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                    string kind;
                    string format = string.Empty;
                    int mips = 0, requested = -1, loaded = -1;
                    bool streaming = false;
                    if (t is RenderTexture rt)
                    {
                        kind = "RenderTexture";
                        format = rt.graphicsFormat.ToString();
                        s.renderTextureBytes += bytes;
                        s.renderTextureCount++;
                    }
                    else if (t is Texture2D t2)
                    {
                        format = t2.format.ToString();
                        mips = t2.mipmapCount;
                        streaming = t2.streamingMipmaps;
                        if (streaming)
                        {
                            requested = t2.requestedMipmapLevel;
                            loaded = t2.loadedMipmapLevel;
                        }

                        bool art = t2.name.StartsWith("T_", StringComparison.Ordinal);
                        kind = art ? "Art" : "Texture2D";
                        if (art)
                        {
                            s.artTextureBytes += bytes;
                            s.artTextureCount++;
                            if (streaming && loaded > 0) s.artTexturesReduced++;
                        }
                        else
                        {
                            s.otherTextureBytes += bytes;
                            s.otherTextureCount++;
                        }
                    }
                    else
                    {
                        kind = t.GetType().Name;
                        s.otherTextureBytes += bytes;
                        s.otherTextureCount++;
                    }

                    sb.Append(t.name.Replace(',', ' ')).Append(',').Append(kind).Append(',').Append(t.width).Append(',').Append(t.height).Append(',').Append(format).Append(',')
                      .Append(mips).Append(',').Append(streaming ? 1 : 0).Append(',').Append(requested).Append(',').Append(loaded).Append(',').Append(bytes).Append('\n');
                }

                s.textureCurrentBytes = (long)Texture.currentTextureMemory;
                s.textureDesiredBytes = (long)Texture.desiredTextureMemory;
                s.textureTargetBytes = (long)Texture.targetTextureMemory;
                s.textureNonStreamingBytes = (long)Texture.nonStreamingTextureMemory;
                s.streamingTextureCount = (int)Texture.streamingTextureCount;
                s.nonStreamingTextureCount = (int)Texture.nonStreamingTextureCount;
                s.streamingMipmapsActive = QualitySettings.streamingMipmapsActive;
                s.streamingMipmapsBudgetMb = QualitySettings.streamingMipmapsMemoryBudget;
                s.inventoryCsv = "texture-inventory.csv";
                File.WriteAllText(Path.Combine(_outDir, s.inventoryCsv), sb.ToString());
            }

            private static string Mb(double bytes)
            {
                return bytes < 0d ? "n/a" : (bytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + "MB";
            }

            // ================================================================ records

            private Stage Capture(string stage)
            {
                EnemySpawnDirector d = EnemySpawnDirector.Instance;
                RunDirector r = RunDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                bool hasDirector = !ReferenceEquals(d, null);
                bool hasRun = !ReferenceEquals(r, null);
                Vector3 pos = rig != null ? rig.transform.position : Vector3.zero;
                int driftFields = CountDriftFields(out int driftAlive);

                return new Stage
                {
                    stage = stage,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    runRound = hasRun ? r.Round : 0,
                    runPhase = hasRun ? r.Phase.ToString() : "none",
                    alive = hasDirector ? d.ActiveCount : 0,
                    liveBodies = ZombieEnemy.Live.Count,
                    targetActive = hasDirector ? d.TargetActive : 0,
                    ceiling = hasDirector ? d.SafetyCeiling : 0,
                    crowdMembers = CrowdService.MemberCount,
                    playerX = pos.x,
                    playerZ = pos.z,
                    atmosphereOn = !AtmosphereOff,
                    windStrength = WorldWind.Strength,
                    postProcessing = PostProcessingOn(),
                    driftFields = driftFields,
                    driftParticlesAlive = driftAlive,
                    targetFrameRate = Application.targetFrameRate,
                    vSyncCount = QualitySettings.vSyncCount,
                    graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                    graphicsDeviceName = SystemInfo.graphicsDeviceName,
                    flow = hasDirector ? d.DescribeFlow() : "no director"
                };
            }

            private void Write(Stage s)
            {
                _stages.Add(s);
                File.WriteAllText(Path.Combine(_outDir, s.stage + ".json"), JsonUtility.ToJson(s, true));
                Debug.Log("[PilotPerfProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.flow);
            }

            private void WriteFrameTimes()
            {
                var sb = new StringBuilder(_frameMs.Count * 8);
                for (int i = 0; i < _frameMs.Count; i++)
                {
                    sb.Append(_frameMs[i].ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
                }

                File.WriteAllText(Path.Combine(_outDir, "frame-times-ms.csv"), sb.ToString());
            }

            private void WriteSummary()
            {
                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"),
                    "PilotPerfProbe " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[PilotPerfProbe] DONE scene=" + _scene + " stages=" + _stages.Count + " pass=" + passCount);
            }

            private IEnumerable<string> StageLines()
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    yield return (_stages[i].pass ? "PASS " : "FAIL ") + _stages[i].stage + " | " + _stages[i].note + " | " + _stages[i].flow;
                }
            }

            /// <summary>
            /// A flow timeout still leaves evidence: the failing stage and the summary are
            /// written before the Player exits with code 3 (the RoundProbe convention).
            /// </summary>
            private IEnumerator Abort(string stageName, string why)
            {
                Stage s = Capture(stageName);
                s.pass = false;
                s.note = "ABORT: " + why;
                Write(s);
                WriteSummary();
                Debug.LogError("[PilotPerfProbe] ABORT " + why);
                yield return null;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit(3);
#endif
            }

            // ================================================================ waits

            private IEnumerator WaitForScene(string name, float timeoutSeconds)
            {
                yield return WaitUntil(
                    () => SceneManager.GetActiveScene().name == name && SceneManager.GetActiveScene().isLoaded,
                    "scene '" + name + "' not active", timeoutSeconds);
            }

            private IEnumerator WaitUntil(Func<bool> condition, string what, float timeoutSeconds)
            {
                _timedOut = false;
                _timeoutWhat = null;
                float start = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - start > timeoutSeconds)
                    {
                        _timedOut = true;
                        _timeoutWhat = what;
                        Debug.LogError("[PilotPerfProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
#endif
