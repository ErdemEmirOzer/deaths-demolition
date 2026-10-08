using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Art
{
    /// <summary>
    /// WP-092 A (R3 #8, S-05) · The world content the top-down camera never sees (TdVisibilityAudit: no pixel from any TD pose, no
    /// shadow into one, no plaza reflection) lives in its own scene, <see cref="DefaultSceneName"/>, which GreyboxV6Builder writes
    /// next to the world (WorldFptpSplit). This component sits on the world root and loads that scene additively only when a camera
    /// that can see it is in use: third person, first person, or the menu's cinematic backdrop (<see cref="WorldViewer"/>).
    /// Top-down never loads it. A run that starts in TP / FP loads it with the world (no pop-in); a mode change mid-run (Pause menu)
    /// loads or unloads it asynchronously.
    /// Colliders, NavMesh, spawn and audio data never move (they stay in the world scene), so loading or not changes only pixels.
    /// The scene leaves with the world (the Single load that replaces the run or the menu unloads every additive scene).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldFptpOnlyLoader : MonoBehaviour
    {
        public const string DefaultSceneName = "World_FPTPOnly";

        [SerializeField] private string _sceneName = DefaultSceneName;
        [Tooltip("Units the builder moved into the scene (information; written by WorldFptpSplit).")]
        [SerializeField] private int _movedUnits;

        private FptpSceneDriver _driver;

        public string SceneName => _sceneName;
        public int MovedUnits => _movedUnits;

        /// <summary>The loader of the world currently loaded (null when none).</summary>
        public static WorldFptpOnlyLoader Current { get; private set; }

        /// <summary>The driver (state, load / unload counts) — diagnostics and tests.</summary>
        public FptpSceneDriver Driver => _driver;

#if UNITY_EDITOR
        /// <summary>Builder only (WorldFptpSplit).</summary>
        public void Configure(string sceneName, int movedUnits)
        {
            _sceneName = sceneName;
            _movedUnits = movedUnits;
        }
#endif

        private void Awake()
        {
            Current = this;
            _driver = new FptpSceneDriver(_sceneName, FptpSceneDriver.RuntimeOps());
            // The rig requests the start mode in its OnEnable; until a rig exists the run's start mode is the resolved one.
            _driver.Begin(CameraModeService.ResolveStartMode(), WorldViewer.Active);
        }

        private void Update()
        {
            CameraMode mode = PrototypeCameraRig.Active != null ? CameraModeService.Target : CameraModeService.ResolveStartMode();
            _driver.Tick(mode, WorldViewer.Active);
        }

        private void OnDestroy()
        {
            _driver?.Release();
            if (Current == this) Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }
    }

    /// <summary>
    /// WP-092 A · The load policy of <see cref="WorldFptpOnlyLoader"/> as plain code (EditMode-testable): the scene is wanted in every
    /// camera mode but top-down, and always under the menu backdrop. One operation at a time; the wanted state is re-read every tick,
    /// so a TD → TP → TD flick while loading ends unloaded.
    /// </summary>
    public sealed class FptpSceneDriver
    {
        public enum State { Unloaded, Loading, Loaded, Unloading }

        /// <summary>The scene operations (the runtime's SceneManager calls, or a test double).</summary>
        public sealed class Ops
        {
            /// <summary>Whether the scene is in the build (Application.CanStreamedLevelBeLoaded).</summary>
            public Func<string, bool> CanLoad;
            /// <summary>Additive load completing with the frame (start of a TP / FP run, the menu backdrop): no pop-in.</summary>
            public Action<string> LoadNow;
            /// <summary>Additive async load; returns its "done" poll.</summary>
            public Func<string, Func<bool>> LoadAsync;
            /// <summary>Async unload; returns its "done" poll (null when the scene was not loaded).</summary>
            public Func<string, Func<bool>> UnloadAsync;
            /// <summary>Whether the scene is loaded now.</summary>
            public Func<string, bool> IsLoaded;
            /// <summary>After a load completes (the world's baked probes are re-asserted).</summary>
            public Action AfterLoad;
            /// <summary>After an unload completes (unused assets are released).</summary>
            public Action AfterUnload;
        }

        private readonly string _scene;
        private readonly Ops _ops;
        private Func<bool> _pending;
        private bool _missingLogged;

        public FptpSceneDriver(string scene, Ops ops)
        {
            _scene = scene;
            _ops = ops;
        }

        public State Current { get; private set; } = State.Unloaded;

        /// <summary>Load requests issued (sync + async).</summary>
        public int Loads { get; private set; }

        public int Unloads { get; private set; }

        /// <summary>The policy: TP / FP and the menu backdrop see the content; top-down never does.</summary>
        public static bool ShouldLoad(CameraMode mode, bool viewer) => viewer || mode != CameraMode.TopDown;

        /// <summary>World load (Awake): a TP / FP run or the menu backdrop loads with the frame; top-down does nothing.</summary>
        public void Begin(CameraMode startMode, bool viewer)
        {
            if (!ShouldLoad(startMode, viewer) || !Available()) return;
            Loads++;
            _ops.LoadNow(_scene);
            Current = State.Loading;
            _pending = () => _ops.IsLoaded(_scene);
        }

        /// <summary>Per frame: finishes the operation in flight, then starts the one the wanted state needs.</summary>
        public void Tick(CameraMode mode, bool viewer)
        {
            if (_pending != null)
            {
                if (!_pending()) return;
                _pending = null;
                if (Current == State.Loading)
                {
                    Current = State.Loaded;
                    _ops.AfterLoad?.Invoke();
                }
                else
                {
                    Current = State.Unloaded;
                    _ops.AfterUnload?.Invoke();
                }
            }

            bool want = ShouldLoad(mode, viewer);
            if (want && Current == State.Unloaded && Available())
            {
                Loads++;
                Current = State.Loading;
                _pending = _ops.LoadAsync(_scene) ?? (() => _ops.IsLoaded(_scene));
            }
            else if (!want && Current == State.Loaded)
            {
                Unloads++;
                Current = State.Unloading;
                _pending = _ops.UnloadAsync(_scene) ?? (() => true);
            }
        }

        /// <summary>
        /// The world is going away. The game removes it only with a Single scene load (PrototypeUiFlow / PrototypeBootstrapper),
        /// which unloads World_FPTPOnly with it, so nothing is requested here (an unload request inside that teardown would race it).
        /// </summary>
        public void Release()
        {
            _pending = null;
            Current = State.Unloaded;
        }

        private bool Available()
        {
            if (_ops.CanLoad(_scene)) return true;
            if (!_missingLogged)
            {
                _missingLogged = true;
                Debug.LogWarning("[World] FP/TP-only scene '" + _scene + "' is not in the build settings; TP / FP show the top-down world.");
            }

            return false;
        }

        /// <summary>The runtime operations (SceneManager).</summary>
        public static Ops RuntimeOps()
        {
            return new Ops
            {
                CanLoad = Application.CanStreamedLevelBeLoaded,
                LoadNow = scene => SceneManager.LoadScene(scene, LoadSceneMode.Additive),
                LoadAsync = scene =>
                {
                    AsyncOperation op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);
                    if (op == null) return () => true;
                    return () => op.isDone;
                },
                UnloadAsync = scene =>
                {
                    Scene s = SceneManager.GetSceneByName(scene);
                    if (!s.IsValid() || !s.isLoaded) return null;
                    AsyncOperation op = SceneManager.UnloadSceneAsync(s);
                    if (op == null) return null;
                    return () => op.isDone;
                },
                IsLoaded = scene =>
                {
                    Scene s = SceneManager.GetSceneByName(scene);
                    return s.IsValid() && s.isLoaded;
                },
                AfterLoad = () =>
                {
                    // An additive scene with lighting data may hand Unity other probes; the world's own stay bound (R3 #1).
                    if (WorldBakedProbes.Current != null) WorldBakedProbes.Current.Rebind();
                    Debug.Log("[World] FP/TP-only content loaded.");
                },
                AfterUnload = () =>
                {
                    Resources.UnloadUnusedAssets();
                    Debug.Log("[World] FP/TP-only content unloaded (top-down).");
                }
            };
        }
    }
}
