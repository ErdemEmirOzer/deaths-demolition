using System;
using System.Collections.Generic;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 Faz 1 · The living ground: one per process (DontDestroyOnLoad), bound to the loaded world scene by
    /// <see cref="SetWorld"/> (the V6 hook-up in DD.Art). Build (once per world / tier): probe + S-05 masks →
    /// <see cref="VegetationZoner"/> (LOOK-BIBLE recipes) → <see cref="VegetationPlacer"/> → per (species, variant) matrix
    /// arrays in 8 m chunks. Per frame, 0 B:
    ///   LateUpdate  — wind globals (WIND-SPEC on top of WorldWind), the interaction map (player + live zombies + blasts);
    ///   beginCameraRendering (main camera) — CPU frustum culling per chunk (cached Plane[6]), LOD 0/1/2 by chunk distance with
    ///   a 2 m dither cross-fade in the shader, distance density (first N of each hash-sorted chunk range + per-instance
    ///   shrink in the shader: one instance at a time, no popping), camera-height density, then
    ///   Graphics.DrawMeshInstanced per group × LOD from preallocated 1023-matrix pages (the project's proven 0 B instanced
    ///   path — TracerRibbonRenderer / GelSystem; Metal-safe; no compute, no indirect args).
    /// Short grass and every species: receive shadows, cast none. Counters for the probe and tests.
    /// Meshes, materials, placement and the cull / draw live in <see cref="VegetationDrawSet"/>, shared with the edit-mode
    /// <see cref="VegetationPreview"/> so a capture shows exactly what the game places.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)]
    public sealed class VegetationSystem : MonoBehaviour
    {
        public const int MaxPerDraw = VegetationDrawSet.MaxPerDraw;
        public const int Seed = 9301;
        /// <summary>Shader grow band: an instance scales 0 → 1 over this much hash above the density threshold.</summary>
        public const float FadeBand = VegetationDrawSet.FadeBand;

        private static VegetationSystem s_instance;

        public static VegetationSystem Instance => s_instance;

        // ------------------------------------------------------------------ counters (tests, WP-044 probe)
        public int VisibleInstances => _draw != null ? _draw.VisibleInstances : 0;
        public int DrawCalls => _draw != null ? _draw.DrawCalls : 0;
        public int ChunksVisible => _draw != null ? _draw.ChunksVisible : 0;
        public int ChunksTotal => Layout != null ? Layout.ChunkCount : 0;
        public int TotalInstances => Layout != null ? Layout.TotalInstances : 0;
        public int PushersLastStep { get; private set; }
        public int BuiltTier => _draw != null ? _draw.BuiltTier : -1;
        public bool IsBuilt => Layout != null;
        public VegetationLayout Layout => _draw != null ? _draw.Layout : null;
        public VegetationDensityField Field => _field;
        public VegetationDrawSet DrawSet => _draw;
        public VegetationPushMap PushMap => _push;
        public VegetationRecipe Recipe => _recipe;
        public float LastBuildMilliseconds { get; private set; }

        // ------------------------------------------------------------------ configuration
        /// <summary>Tests / captures: the camera to cull and draw for (null = Camera.main).</summary>
        public Camera CameraOverride { get; set; }
        /// <summary>Tests: pin a tier (0 Performance, 1 High, 2 Cinematic); null = VfxTier.Current.</summary>
        public int? TierOverride { get; set; }
        /// <summary>False: nothing draws from the render callback (tests drive <see cref="RenderFor"/> directly).</summary>
        public bool DrawFromCameraCallback { get; set; } = true;
        /// <summary>False: no player / zombie stamping (tests stamp the map directly).</summary>
        public bool StampActors { get; set; } = true;

        private VegetationRecipe _recipe;
        private VegetationWindSettings _wind;
        private VegetationKit _kit;

        private Func<VegetationRecipe, VegetationDensityField> _builder;
        private Scene _scene;
        private bool _hasScene;
        private bool _buildPending;
        private VegetationDensityField _field;
        private VegetationDrawSet _draw;

        private VegetationPushMap _push;
        private int _pushTier = -1;
        private WindDriver _driver;
        private bool _driverSearched;

        // Zombie velocity tracker (open addressing on instance id; 0 B).
        private const int TrackCapacity = 256;
        private readonly int[] _trackId = new int[TrackCapacity];
        private readonly Vector3[] _trackPos = new Vector3[TrackCapacity];
        private readonly int[] _trackFrame = new int[TrackCapacity];

        private Action<ScriptableRenderContext, Camera> _onBeginCamera;
        private Action<RocketBlastInfo> _onDetonated;
        private UnityEngine.Events.UnityAction<Scene> _onSceneUnloaded;
        private UnityEngine.Events.UnityAction<Scene, LoadSceneMode> _onSceneLoaded;

        /// <summary>A/B and bisect switch: <c>-ddVegetationOff</c> or env <c>DD_VEGETATION=off</c> — the world hook-up binds nothing.</summary>
        public static bool DisabledByCommandLine
        {
            get
            {
                if (!s_offRead)
                {
                    s_offRead = true;
                    s_off = Array.IndexOf(Environment.GetCommandLineArgs(), "-ddVegetationOff") >= 0
                            || string.Equals(Environment.GetEnvironmentVariable("DD_VEGETATION"), "off", StringComparison.OrdinalIgnoreCase);
                }

                return s_off;
            }
        }

        private static bool s_off, s_offRead;

        // ------------------------------------------------------------------ shader ids
        private static readonly int PushTexId = Shader.PropertyToID("_DD_VegPushTex");
        private static readonly int PushRectId = Shader.PropertyToID("_DD_VegPushRect");
        private static readonly int PushOnId = Shader.PropertyToID("_DD_VegPushOn");
        private static readonly int PushFocusId = Shader.PropertyToID("_DD_VegPushFocus");

        // ------------------------------------------------------------------ lifecycle

        public static VegetationSystem Ensure()
        {
            if (s_instance == null)
            {
                var go = new GameObject("[VegetationSystem]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<VegetationSystem>();
            }

            return s_instance;
        }

        /// <summary>Explosions and other radial shoves (WIND-SPEC §3 burst): front at 15 m/s to 1.6 R. No-op without a system.</summary>
        public static void Push(Vector3 position, float radius, float strength)
        {
            VegetationSystem s = s_instance;
            if (s == null || s._push == null) return;
            s._push.AddBurst(new Vector2(position.x, position.z), radius, strength);
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            _recipe = VegetationRecipe.Load();
            _wind = VegetationWindSettings.Load();
            _kit = VegetationKit.Load();
            _onBeginCamera = OnBeginCamera;
            _onDetonated = OnDetonated;
            _onSceneUnloaded = OnSceneUnloaded;
            _onSceneLoaded = OnSceneLoaded;
            _draw = new VegetationDrawSet(_recipe, _kit);
            VegetationDrawSet.EnsureNoise();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += _onBeginCamera;
            RocketImpacts.Detonated += _onDetonated;
            SceneManager.sceneUnloaded += _onSceneUnloaded;
            SceneManager.sceneLoaded += _onSceneLoaded;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= _onBeginCamera;
            RocketImpacts.Detonated -= _onDetonated;
            SceneManager.sceneUnloaded -= _onSceneUnloaded;
            SceneManager.sceneLoaded -= _onSceneLoaded;
        }

        private void OnDestroy()
        {
            _draw?.Dispose();
            _draw = null;
            _push?.Dispose();
            _push = null;
            if (s_instance == this) s_instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_offRead = false;
        }

        /// <summary>
        /// Binds the system to a world scene: <paramref name="builder"/> makes the density field (probe + masks + zoner).
        /// It runs SYNCHRONOUSLY inside SceneManager.sceneLoaded of that scene — after every Awake / OnEnable there (NavRegion
        /// volumes registered), before the scene's first Start / frame — so the one-time build cost lands in the loading
        /// frame, never in gameplay frames. A scene already loaded (no sceneLoaded coming) builds on the next LateUpdate.
        /// Unloading the scene clears the vegetation.
        /// </summary>
        public void SetWorld(Scene scene, Func<VegetationRecipe, VegetationDensityField> builder)
        {
            ClearWorld();
            _scene = scene;
            _hasScene = scene.IsValid();
            _builder = builder;
            _buildPending = builder != null;
            _driverSearched = false;
        }

        /// <summary>Tests / tools: place from a ready field now.</summary>
        public void BuildNow(VegetationDensityField field)
        {
            _builder = null;
            _buildPending = false;
            _field = field;
            Place(CurrentTier());
        }

        public void ClearWorld()
        {
            _field = null;
            _draw?.Clear();
            _buildPending = false;
            _builder = null;
            _hasScene = false;
            _push?.Clear();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_buildPending && _hasScene && scene == _scene)
            {
                _buildPending = false;
                BuildFromSource();
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_hasScene && scene == _scene) ClearWorld();
        }

        private void OnDetonated(RocketBlastInfo info)
        {
            Push(info.Origin, Mathf.Max(1.5f, info.OuterRadius), 1f);
        }

        public int CurrentTier()
        {
            if (TierOverride.HasValue) return Mathf.Clamp(TierOverride.Value, 0, 2);
            return (int)VfxTier.Current;
        }

        private void LateUpdate()
        {
            if (_buildPending)
            {
                _buildPending = false;
                BuildFromSource();
            }

            if (_field != null && CurrentTier() != BuiltTier) Place(CurrentTier());   // tier change: re-place (allocates, rare)
            Step(Time.deltaTime);
        }

        private void BuildFromSource()
        {
            if (_builder == null) return;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                _field = _builder(_recipe);
            }
            catch (Exception e)
            {
                Debug.LogError("[Vegetation] world build failed; no vegetation this world.\n" + e);
                _field = null;
            }

            if (_field != null) Place(CurrentTier());
            LastBuildMilliseconds = (Time.realtimeSinceStartup - t0) * 1000f;
            VegetationLayout layout = Layout;
            if (layout != null)
            {
                Debug.Log("[Vegetation] built " + layout.TotalInstances + " instances in " + layout.ChunkCount + " chunks, tier " + BuiltTier
                          + " (" + LastBuildMilliseconds.ToString("F0") + " ms): " + PerTypeReport());
            }
        }

        private string PerTypeReport()
        {
            var sb = new System.Text.StringBuilder(160);
            for (int t = 0; t < VegetationTypes.Count; t++)
            {
                if (t > 0) sb.Append(", ");
                sb.Append(VegetationTypes.Codes[t]).Append(' ').Append(Layout.InstancesPerType[t]);
                if (_draw.UsesKit((VegetationType)t)) sb.Append(" (AP-031)");
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ build

        private void Place(int tier)
        {
            if (_field == null) return;
            _draw.Build(_field, tier, Seed);
        }

        // ------------------------------------------------------------------ per frame: wind + interaction (0 B)

        /// <summary>One simulation step: wind globals, the push map (pushers, bursts, upload) and its globals. 0 B.</summary>
        public void Step(float dt)
        {
            int tier = CurrentTier();
            ApplyWind(tier);
            EnsurePushMap(tier);
            StepPush(dt, tier);
        }

        private void ApplyWind(int tier)
        {
            if (!_driverSearched)
            {
                _driverSearched = true;
                _driver = FindObjectOfType<WindDriver>();
            }

            float baseDegrees = _driver != null ? _driver.DirectionDegrees : _wind.windDirDegrees;
            bool reduced = DD.Prototype.UI.UiMotion.ReducedMotion;
            _wind.ApplyGlobals(tier, reduced, baseDegrees, WorldWind.Strength);
        }

        private void EnsurePushMap(int tier)
        {
            int key = tier <= 0 ? 0 : 1;
            if (_push != null && _pushTier == key) return;
            _push?.Dispose();
            int size = key == 0 ? _wind.pushMapSizePerformance : _wind.pushMapSize;
            float world = key == 0 ? _wind.pushMapWorldPerformance : _wind.pushMapWorld;
            _push = new VegetationPushMap(size, world, true)
            {
                Omega = _wind.pushOmegaGrass, Zeta = _wind.pushZetaGrass, FlatHold = _wind.flatHoldGrass, FlatHalfLife = _wind.flatHalfLifeGrass
            };
            _pushTier = key;
        }

        private void StepPush(float dt, int tier)
        {
            VegetationPushMap map = _push;
            map.Advance(dt);
            Vector2 focus;
            PlayerRig primary = PlayerRegistry.Primary;
            Camera cam = TargetCamera();
            if (primary != null) focus = Flat(primary.transform.position);
            else if (cam != null) focus = Flat(cam.transform.position + cam.transform.forward * 20f);
            else focus = Vector2.zero;
            map.Recenter(focus);

            int pushers = 0;
            float radiusLimit = tier <= 0 ? _wind.performanceInteractionRadius : float.MaxValue;
            float r2Limit = radiusLimit >= float.MaxValue ? float.MaxValue : radiusLimit * radiusLimit;
            if (StampActors && dt > 0f)
            {
                IReadOnlyList<PlayerRig> rigs = PlayerRegistry.All;
                for (int i = 0; i < rigs.Count && pushers < _wind.maxPushers; i++)
                {
                    PlayerRig rig = rigs[i];
                    if (rig == null || !rig.isActiveAndEnabled) continue;
                    Vector3 v = rig.Motor != null ? rig.Motor.PlanarVelocity : Vector3.zero;
                    map.Stamp(Flat(rig.transform.position), new Vector2(v.x, v.z), _wind.playerRadius, _wind.playerStrength, _wind.playerFlattenRate, _wind.playerFlattenCap, _wind.playerVelocityBias, dt);
                    pushers++;
                }

                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                int frame = Time.frameCount;
                for (int i = 0; i < live.Count && pushers < _wind.maxPushers; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive || !z.isActiveAndEnabled) continue;
                    Vector3 p = z.transform.position;
                    Vector2 xz = Flat(p);
                    if ((xz - focus).sqrMagnitude > r2Limit) continue;
                    Vector2 vel = Velocity(z.GetInstanceID(), p, dt, frame);
                    bool run = vel.sqrMagnitude > _wind.zombieRunSpeed * _wind.zombieRunSpeed;
                    if (run) map.Stamp(xz, vel, _wind.zombieRunRadius, _wind.zombieRunStrength, _wind.zombieRunFlattenRate, _wind.zombieRunFlattenCap, _wind.zombieRunVelocityBias, dt);
                    else map.Stamp(xz, vel, _wind.zombieRadius, _wind.zombieStrength, _wind.zombieFlattenRate, _wind.zombieFlattenCap, 0f, dt);
                    pushers++;
                }
            }

            map.StepBursts(dt);
            map.Upload();
            PushersLastStep = pushers;

            Vector2 o = map.Origin;
            Shader.SetGlobalTexture(PushTexId, map.Texture);
            Shader.SetGlobalVector(PushRectId, new Vector4(o.x, o.y, map.WorldSize, map.Time));
            Shader.SetGlobalVector(PushOnId, new Vector4(1f, map.Size, tier <= 0 ? _wind.performanceInteractionRadius : 100000f, 0f));
            Shader.SetGlobalVector(PushFocusId, new Vector4(focus.x, focus.y, 0f, 0f));
        }

        private Vector2 Velocity(int id, Vector3 position, float dt, int frame)
        {
            int start = (int)((uint)id * 2654435761u >> 24) & (TrackCapacity - 1);
            int free = -1;
            for (int probe = 0; probe < TrackCapacity; probe++)
            {
                int s = (start + probe) & (TrackCapacity - 1);
                if (_trackId[s] == id)
                {
                    Vector3 d = position - _trackPos[s];
                    bool fresh = frame - _trackFrame[s] <= 2;
                    _trackPos[s] = position;
                    _trackFrame[s] = frame;
                    return fresh && dt > 1e-5f ? new Vector2(d.x, d.z) / dt : Vector2.zero;
                }

                if (free < 0 && (_trackId[s] == 0 || frame - _trackFrame[s] > 30)) free = s;
                if (_trackId[s] == 0) break;
            }

            if (free >= 0)
            {
                _trackId[free] = id;
                _trackPos[free] = position;
                _trackFrame[free] = frame;
            }

            return Vector2.zero;
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        private Camera TargetCamera() => CameraOverride != null ? CameraOverride : Camera.main;

        // ------------------------------------------------------------------ per camera: cull + draw (0 B)

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!DrawFromCameraCallback || Layout == null || cam == null) return;
            bool draw = cam.cameraType == CameraType.SceneView || (cam.cameraType == CameraType.Game && cam == TargetCamera());
            if (draw) RenderFor(cam);
        }

        /// <summary>Culls the chunks against <paramref name="cam"/> and submits the visible instances. 0 B.</summary>
        public void RenderFor(Camera cam)
        {
            _draw?.Render(cam, gameObject.layer);
        }
    }
}
