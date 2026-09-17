using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.UI
{
    /// <summary>
    /// The meta flow: Main Menu -> Skin and Weapon Selection -> Run -> Run Results -> Main Menu.
    ///
    /// Nothing else in the build changes scenes. <c>PrototypeBootstrapper</c> hands off
    /// to the first scene and stops; from there every transition goes through here, so
    /// the four things that must happen around a transition happen in one place:
    ///
    ///   * the simulation clock is released (a menu loaded while a pause hold is still
    ///     registered would open at timeScale 0 and look like a hang);
    ///   * the finished run has been folded into the profile and written to disk
    ///     (ProfileRunCommitter, on the same RunEnded event) BEFORE the results screen
    ///     exists, so the screen only reads the persisted receipt;
    ///   * a missing scene degrades to a log line rather than an exception, which is
    ///     what lets the module be built and compiled with the scenes not yet created;
    ///   * the session-scoped loadout survives scene loads and dies with the process.
    ///
    /// SESSION VS PERSISTENT (acceptance criteria 23 and 24)
    /// The selected skin and weapon are static fields reset by
    /// RuntimeInitializeOnLoadMethod. They therefore carry from one run to the next
    /// inside a session, and are back to the prototype defaults the next time the
    /// application starts. They are deliberately NOT in the profile save: a cosmetic
    /// choice is not progress.
    /// </summary>
    public static class PrototypeUiFlow
    {
        /// <summary>PROTOTYPE-ONLY: how long the run scene holds after death before Results.</summary>
        private const float ResultsDelaySeconds = 1.6f;

        // Counts scene transitions requested through this flow. A delayed hand-off
        // captures the value when scheduled and gives up if another transition
        // happened in between (WP-001 A1: quitting to the main menu during the death
        // beat must not have Results loaded over the menu 1.6 s later).
        private static int s_transition;

        private static bool s_installed;
        private static bool s_resultsScheduled;

        /// <summary>Cosmetic skin chosen on the selection screen. Session scoped.</summary>
        public static PlayerSkin SelectedSkin { get; set; } = PlayerSkins.Default;

        /// <summary>
        /// Classic weapon chosen on the selection screen. Stored on
        /// <see cref="WeaponRegistry"/> because that is where the weapon systems already
        /// look for it; this property only forwards, so there is one owner of the value.
        /// </summary>
        public static ClassicWeaponId SelectedWeapon
        {
            get { return WeaponRegistry.SelectedWeapon; }
            set { WeaponRegistry.SelectedWeapon = value; }
        }

        /// <summary>Name of the scene the flow is currently in.</summary>
        public static string CurrentSceneName
        {
            get { return SceneManager.GetActiveScene().name; }
        }

        // ============================================================== transitions

        public static void GoToMainMenu()
        {
            ReleaseSimulation();
            Load(PrototypeScenes.MainMenu);
        }

        public static void GoToSelection()
        {
            ReleaseSimulation();
            Load(PrototypeScenes.Selection);
        }

        /// <summary>
        /// Commits the loadout and enters the run. The weapon is re-validated here and
        /// not only on the selection screen: a profile edited between the two would
        /// otherwise start a run with a weapon the player has not unlocked.
        /// </summary>
        public static void StartRun()
        {
            if (!ProfileService.IsWeaponUnlocked(SelectedWeapon))
            {
                SelectedWeapon = ProfileService.FirstUnlockedWeapon();
            }

            ProfileService.ClearLastReport();
            s_resultsScheduled = false;

            ReleaseSimulation();
            Load(PrototypeScenes.Run);
        }

        public static void GoToResults()
        {
            ReleaseSimulation();
            Load(PrototypeScenes.Results);
        }

        /// <summary>
        /// Leaves a live run from the pause screen. The confirmation dialog is the
        /// pause screen's job; by the time this is called the player has confirmed.
        /// </summary>
        public static void AbortRunToMainMenu()
        {
            s_resultsScheduled = true; // suppress the automatic hand-off to Results

            // WP-009H: the aborted run's completion capability dies here; the profile
            // (ordinal included) is untouched - the B5 behaviour is unchanged.
            CompletionAuthority.InvalidateLive();

            ReleaseSimulation();
            Load(PrototypeScenes.MainMenu);
        }

        public static void Quit()
        {
            ProfileService.Save();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ================================================================ plumbing

        /// <summary>
        /// Drops every simulation hold and restores the time scale. Called before every
        /// transition: <c>RunFlowClock</c> is the only owner of "the world is stopped",
        /// and a hold that outlives its scene would freeze the next one.
        /// </summary>
        public static void ReleaseSimulation()
        {
            RunFlowClock.ClearHolds();
        }

        private static void Load(string sceneName)
        {
            s_transition++;

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                // Deliberately a log, not an error: the scenes are created by
                // Tools > Deaths Demolition > UI > Build Meta Scenes, and acceptance
                // criterion 27 forbids console errors during normal use.
                Debug.Log(
                    "[UI] Scene '" + sceneName + "' is not in the build settings. " +
                    "Run Tools > Deaths Demolition > UI > Build Meta Scenes to create the meta scenes.");
                return;
            }

            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        // =============================================================== run hand-off

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;

            RunEvents.RunEnded += OnRunEnded;
            RunEvents.RunStarted += OnRunStarted;

            // WP-022D (WP-035 contract): the cosmetics source lists skins with the
            // SESSION selection (DEC-039: never persisted), and the UI flow owns it.
            ProfileService.SessionSkinProvider = () => SelectedSkin;

            // Created eagerly rather than on first use: it is also where the profile is
            // flushed on quit and where the F1 rows live, and both of those have to
            // exist before anything interesting happens.
            _ = UiFlowRunner.Instance;
        }

        private static void OnRunStarted(int startRound)
        {
            s_resultsScheduled = false;
            ProfileService.ClearLastReport();
        }

        /// <summary>
        /// Navigation only (WP-009). The run becomes profile progress in
        /// <see cref="ProfileRunCommitter"/>, which listens to the same event and commits
        /// synchronously through <see cref="ProfileService.ApplyRun"/>: by the time the
        /// delayed hand-off below fires, the receipt is already on disk. The
        /// <c>s_resultsScheduled</c> flag guards the trip to Results, not the profile;
        /// the profile's exactly-once is the durable completion identity.
        /// </summary>
        private static void OnRunEnded(RunSummary summary)
        {
            if (s_resultsScheduled)
            {
                return;
            }

            s_resultsScheduled = true;

            if (summary.EndReason == RunEndReason.Aborted)
            {
                // The pause screen is already taking the player to the main menu.
                return;
            }

            // WP-059: a death waits for the death beat (GameFeelSettings, one source with the
            // slow-down); any other end keeps the short hand-off.
            float delay = summary.EndReason == RunEndReason.PlayerDeath
                ? DD.Prototype.Feedback.Vfx.GameFeelSettings.Active.DeathResultsDelaySeconds
                : ResultsDelaySeconds;
            UiFlowRunner.Instance.Run(GoToResultsAfterDelay(s_transition, delay));
        }

        private static IEnumerator GoToResultsAfterDelay(int scheduledAt, float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);

            if (scheduledAt != s_transition)
            {
                // The player already left the run (pause menu -> main menu) while the
                // death beat was playing. The menu is where they asked to be.
                yield break;
            }

            GoToResults();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_installed = false;
            s_resultsScheduled = false;
            s_transition = 0;
            SelectedSkin = PlayerSkins.Default;
        }
    }

    /// <summary>
    /// A place to run a coroutine from static code, and the hook that flushes the
    /// profile when the application closes. Hidden, and it holds no state of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiFlowRunner : MonoBehaviour
    {
        private static UiFlowRunner s_instance;

        public static UiFlowRunner Instance
        {
            get
            {
                if (s_instance != null)
                {
                    return s_instance;
                }

                var go = new GameObject("UI Flow Runner")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<UiFlowRunner>();
                return s_instance;
            }
        }

        private DebugPanelReadout _profileReadout;
        private DebugPanelReadout _contrastReadout;
        private DebugPanelToggle _soundToggle;
        private DebugPanelButton _resetButton;

        public void Run(IEnumerator routine)
        {
            StartCoroutine(routine);
        }

        private void OnEnable()
        {
            _profileReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Profile", DescribeProfile, -60);

            _contrastReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "UI contrast audit", DescribeContrast, -59);

            _soundToggle = DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Misc, "UI sound",
                () => UiSound.Enabled,
                v => UiSound.Enabled = v,
                -58);

            _resetButton = DebugPanelRegistry.AddButton(
                DebugPanelTabs.Misc, "Reset local profile", ProfileService.ResetProfile, -57);
        }

        private void OnDisable()
        {
            DebugPanelRegistry.Remove(_profileReadout);
            DebugPanelRegistry.Remove(_contrastReadout);
            DebugPanelRegistry.Remove(_soundToggle);
            DebugPanelRegistry.Remove(_resetButton);
        }

        private static string DescribeProfile()
        {
            ProfileData profile = ProfileService.Current;
            return "L" + profile.ProfileLevel +
                   "  " + profile.XpIntoLevel + "/" + ProfileService.LevelRequirement(profile.ProfileLevel) +
                   " XP  ·  " + profile.TotalKills + " kills  ·  best round " + profile.HighestRound +
                   "  ·  v" + profile.SaveVersion + (ProfileStore.SaveLocked ? "  ·  READ-ONLY" : string.Empty) +
                   "  ·  ord " + profile.LastAppliedCompletionOrdinal + (CompletionAuthority.HasLiveCapability ? " live " + CompletionAuthority.LiveOrdinal : string.Empty) +
                   "  ·  commits " + ProfileRunCommitter.Commits + "/" + ProfileRunCommitter.EventsSeen + "  ·  " + ProfileService.LastVerdict;
        }

        private static string DescribeContrast()
        {
            int failures = UiContrast.CountFailures();
            return failures == 0
                ? UiContrast.Cases.Count + " pairs, all passing"
                : UiContrast.Cases.Count + " pairs, " + failures + " FAILING";
        }

        private void OnApplicationQuit()
        {
            ProfileService.Save();
            ProfileStore.ReleaseOwnership();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
