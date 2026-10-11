using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using DD.Prototype.Core.DebugTools;
#endif

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B3 · ticks <see cref="TreeSystemsRunner"/> once a frame right after <see cref="ProjectileDriver"/> (order 61 &gt; 60),
    /// on the scaled clock, and resets the systems on run start / end and on every active-scene change. Created lazily on the
    /// first registration / blast / well (the ProjectileDriver pattern): a run without a tree build never creates it, and
    /// EditMode tests drive <see cref="TreeSystemsRunner.Tick"/> themselves. Holds no gameplay state. Development builds add the
    /// F1 Perf readout (slots, queue, merges, overlap / frame).
    /// </summary>
    [DefaultExecutionOrder(61)]
    public sealed class TreeSystemsDriver : MonoBehaviour
    {
        private static TreeSystemsDriver s_instance;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private DebugPanelReadout _readout;
#endif

        /// <summary>Creates the driver in play mode (idempotent; nothing in edit mode or tests without play).</summary>
        public static void EnsureExists()
        {
            if (s_instance != null || !Application.isPlaying) return;
            var go = new GameObject("TreeSystemsDriver");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<TreeSystemsDriver>();
        }

        private void OnEnable()
        {
            SpecialWeaponRoundBridge.RunStarted -= OnRunStarted;
            SpecialWeaponRoundBridge.RunStarted += OnRunStarted;
            SpecialWeaponRoundBridge.RunEnded -= OnRunEnded;
            SpecialWeaponRoundBridge.RunEnded += OnRunEnded;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _readout = DebugPanelRegistry.AddReadout(DebugPanelTabs.Performance, "WP-104 tree systems", TreeSystemsRunner.DebugLine);
#endif
        }

        private void OnDisable()
        {
            SpecialWeaponRoundBridge.RunStarted -= OnRunStarted;
            SpecialWeaponRoundBridge.RunEnded -= OnRunEnded;
            SceneManager.activeSceneChanged -= OnSceneChanged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugPanelRegistry.Remove(_readout);
            _readout = null;
#endif
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private void Update()
        {
            Transform player = EnemyTargeting.Player;
            if (player != null) TreeSystemsRunner.SetAnchor(player.position);
            else TreeSystemsRunner.ClearAnchor();
            TreeSystemsRunner.Tick(Time.deltaTime, Time.time);
        }

        // A run's seed: the run serial mixed (Knuth) so two runs never share the chain-roll stream.
        private static void OnRunStarted(int run) => TreeSystemsRunner.Reset(unchecked((uint)run * 2654435761u + 1u));

        private static void OnRunEnded() => TreeSystemsRunner.Reset();

        private static void OnSceneChanged(Scene from, Scene to) => TreeSystemsRunner.Reset();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            TreeSystemsRunner.Reset();
            TreeAreaPool.ResetForTests();
        }
    }
}
