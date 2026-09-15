#if DEVELOPMENT_BUILD || UNITY_EDITOR
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
            public int achievedCount;
            public float forceSeconds;
            public bool forceTimedOut;

            // ---- 03 warmup
            public float warmupSeconds;
            public int warmupFrames;
            public float warmupMeanMs;

            // ---- 04 window: frame times
            public float windowSeconds;
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
            public string unavailableCounters;
            public int gen0CollectionsInWindow;
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
            private const int LoadIntegrityTolerance = 2;
            private const float LoadIntegrityRequired = 0.95f;
            private const int ReadabilityPointsPerEnemy = 5;
            private const int ReadabilityMostlyVisible = 3;

            private string _outDir;
            private string _scene;
            private float _windowSeconds;
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

                Stage s2 = Capture("02-force-40");
                s2.forcedTarget = ceiling;
                s2.achievedCount = dir.ActiveCount;
                s2.forceSeconds = Time.realtimeSinceStartup - t0;
                s2.forceTimedOut = forceTimedOut;
                s2.pass = !forceTimedOut && s2.achievedCount >= ceiling;
                s2.note = "forced target " + ceiling + " (pool ignored): reached " + s2.achievedCount + " in " + s2.forceSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s" + (forceTimedOut ? " TIMEOUT" : "");
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
                    new Counter(ProfilerCategory.Memory, "GC Allocated In Frame")
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

                t0 = Time.realtimeSinceStartup;
                while (true)
                {
                    float ms = Time.unscaledDeltaTime * 1000f;
                    _frameMs.Add(ms);
                    frames++;

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
                s4.lodBiasEffective = QualitySettings.lodBias;
                s4.windowSeconds = windowSeconds;
                s4.frameCount = frames;
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
                s4.note = frames + " frames / " + windowSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s uncapped (lodBias " + s4.lodBiasEffective.ToString("0.00", CultureInfo.InvariantCulture) + "): mean " + s4.frameMeanMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " ms (" + s4.avgFps.ToString("F1", CultureInfo.InvariantCulture) + " fps) median " + s4.frameMedianMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " p99 " + s4.frameP99Ms.ToString("F2", CultureInfo.InvariantCulture) + " 1%low " + s4.frameWorst1PctMeanMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " 0.1%low " + s4.frameWorst01PctMeanMs.ToString("F2", CultureInfo.InvariantCulture) + " max " + s4.frameMaxMs.ToString("F2", CultureInfo.InvariantCulture) +
                          " | >16.67ms " + s4.framesOver16_667Ms + " (" + (s4.fractionOver16_667Ms * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%) >33.33ms " + s4.framesOver33_333Ms +
                          " | draw " + s4.drawCallsAvg.ToString("F0", CultureInfo.InvariantCulture) + " setPass " + s4.setPassCallsAvg.ToString("F0", CultureInfo.InvariantCulture) +
                          " tris " + s4.trianglesAvg.ToString("F0", CultureInfo.InvariantCulture) + " verts " + s4.verticesAvg.ToString("F0", CultureInfo.InvariantCulture) + " batches " + s4.batchesAvg.ToString("F0", CultureInfo.InvariantCulture) +
                          " | tex " + Mb(s4.textureMemoryAvg) + " mesh " + Mb(s4.meshMemoryAvg) + " gcRes " + Mb(s4.gcReservedMemoryAvg) + " sys " + Mb(s4.systemUsedMemoryAvg) +
                          " | crowd " + s4.crowdSolveAvgMs.ToString("F3", CultureInfo.InvariantCulture) + "/" + s4.crowdSolveMaxMs.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                          " | alive min/avg/max " + s4.activeMin + "/" + s4.activeAvg.ToString("F1", CultureInfo.InvariantCulture) + "/" + s4.activeMax + " load>=" + loadFloor + " for " + (s4.loadIntegrityFraction * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%" +
                          " | gen0 " + gen0InWindow + " alloc/frame " + s4.gcAllocatedInFrameAvg.ToString("F0", CultureInfo.InvariantCulture) + " B heap/s " + s4.gcSubWindowBytesPerSecond + " B (gen0 " + subGen0 + ")" +
                          " | spikes " + spikeFrames + " withGen0 " + spikeFramesWithGen0 + (profilerLog ? " profilerLog yes" : "") +
                          (string.IsNullOrEmpty(s4.unavailableCounters) ? "" : " | unavailable: " + s4.unavailableCounters);
                Write(s4);
                WriteFrameTimes();

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
