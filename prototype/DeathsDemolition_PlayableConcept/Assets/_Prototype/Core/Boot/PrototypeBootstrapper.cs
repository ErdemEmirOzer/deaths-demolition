using System.Collections;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.Pooling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Core.Boot
{
    /// <summary>
    /// The only thing in the Boot scene. Loads the config, applies the runtime
    /// settings it dictates, brings up the persistent services and hands off to the
    /// first real scene.
    ///
    /// Boot holds no gameplay state, so nothing downstream has to reach back into it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeBootstrapper : PrototypeConfigBehaviour
    {
        [Header("Hand-off")]
        [Tooltip("Leave empty to use PrototypeBootFlags.FirstSceneName.")]
        [SerializeField] private string _forcedFirstScene = "";

        [Tooltip("Seconds to hold on the boot scene before loading. Keep at 0 for the prototype.")]
        [SerializeField] private float _handoffDelay;

        [SerializeField] private bool _spawnDebugPanel = true;

        private DebugPanelToggle _directToRunToggle;
        private DebugPanelReadout _poolReadout;

        // The persistent (DontDestroyOnLoad) bootstrapper of this process. Boot is
        // loaded once per launch, but the test runner (and any future "back to boot")
        // can load it again; a second instance then stays scene-local and dies with
        // the Boot scene instead of piling up as another persistent object (WP-001).
        private static PrototypeBootstrapper s_persistent;

        private void Awake()
        {
            PrototypeConfig config = ConfigOverride != null
                ? ConfigOverride
                : Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);

            if (config == null)
            {
                Debug.LogError(
                    "[Boot] No PrototypeConfig found. Expected an asset at " +
                    "Assets/_Prototype/Core/Resources/PrototypeConfig.asset. The prototype cannot run without it.");
            }
            else
            {
                PrototypeConfig.SetActive(config);
            }

            if (s_persistent == null)
            {
                s_persistent = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Debug.Log("[Boot] Boot scene loaded again; the persistent bootstrapper already exists, this instance stays scene-local.");
            }

            // Touch the pool root so it exists (and is DontDestroyOnLoad) before any
            // gameplay scene tries to spawn into it.
            _ = PoolService.Root;

            if (_spawnDebugPanel)
            {
                PrototypeDebugPanel.EnsureExists();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            _directToRunToggle = DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Misc,
                "PROTOTYPE_DIRECT_TO_RUN (next boot)",
                () => PrototypeBootFlags.DirectToRun,
                v => PrototypeBootFlags.DirectToRun = v,
                -100);

            _poolReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Performance,
                "Pools",
                DescribePools,
                -100);
        }

        protected override void OnDisable()
        {
            DebugPanelRegistry.Remove(_directToRunToggle);
            DebugPanelRegistry.Remove(_poolReadout);
            base.OnDisable();
        }

        /// <summary>
        /// WP-068 slice 10b-2 (direction §15.3, U7): the boot seal IS the first loading screen.
        /// The UI registers a loader here that loads the first scene itself (async, activation
        /// held under the seal choreography) and returns true; null or false = the plain
        /// synchronous load below (batch runs, tests with FastTransitions, a build without the UI).
        /// Core cannot reference the UI assembly, so the dependency points this way.
        /// </summary>
        public static System.Func<string, bool> FirstSceneLoader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_persistent = null;
            FirstSceneLoader = null;
        }

        private IEnumerator Start()
        {
            if (_handoffDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(_handoffDelay);
            }
            else
            {
                yield return null;
            }

            string target = string.IsNullOrEmpty(_forcedFirstScene)
                ? PrototypeBootFlags.FirstSceneName
                : _forcedFirstScene;

            if (!Application.CanStreamedLevelBeLoaded(target))
            {
                // Expected while the later tasks have not created their scenes yet.
                // Deliberately not an error: the acceptance criteria forbid console
                // errors during normal use.
                Debug.Log($"[Boot] Scene '{target}' is not in the build settings yet. Staying in Boot.");
                yield break;
            }

            if (FirstSceneLoader != null && FirstSceneLoader(target))
            {
                yield break;
            }

            SceneManager.LoadScene(target, LoadSceneMode.Single);
        }

        /// <summary>
        /// Applies everything the config says about the runtime environment. Runs again
        /// on every live config edit, so changing the frame cap in the F1 panel takes
        /// effect immediately.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            SystemConfigSection system = config.System;

            QualitySettings.vSyncCount = Mathf.Max(0, system.VSyncCount);
            Application.targetFrameRate = Mathf.Max(0, system.TargetFrameRate);

            if (system.FixedDeltaTime > 0.001f)
            {
                Time.fixedDeltaTime = system.FixedDeltaTime;
            }
        }

        private static string DescribePools()
        {
            int spawned = 0;
            int available = 0;
            var pools = PoolService.Pools;
            for (int i = 0; i < pools.Count; i++)
            {
                spawned += pools[i].CountSpawned;
                available += pools[i].CountAvailable;
            }

            return $"{pools.Count} pools, {spawned} spawned, {available} free";
        }
    }
}
