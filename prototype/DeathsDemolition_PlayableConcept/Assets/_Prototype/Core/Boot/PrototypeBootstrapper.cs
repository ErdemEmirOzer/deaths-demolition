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

            DontDestroyOnLoad(gameObject);

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
