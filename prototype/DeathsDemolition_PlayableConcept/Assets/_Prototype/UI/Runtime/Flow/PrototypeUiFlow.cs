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
            Load(PrototypeScenes.MainMenu);
        }

        public static void GoToSelection()
        {
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

            Load(PrototypeScenes.Run);
        }

        public static void GoToResults()
        {
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

        // ====================================================== WP-068 slice 10: async load

        /// <summary>
        /// A scene load is in flight - from the request to the new scene's activation (one at
        /// a time; a second Load() is ignored with a log line). The reveal after activation is
        /// not part of it: the new scene is live, and a load asked for during the reveal takes
        /// over the still-shut doors.
        /// </summary>
        public static bool IsLoading
        {
            get { return s_loading; }
        }

        /// <summary>
        /// Tests and batch tools: keep the load asynchronous but skip the curtain's timings
        /// (instant doors, no loading group, no minimum display). Defaults to batch mode, so
        /// the PlayMode suites are not slowed by ~1.8 s per transition; the loading-screen
        /// tests switch it off.
        /// </summary>
        public static bool FastTransitions = Application.isBatchMode;

        /// <summary>A scene whose last measured load was shorter than this gets the CURTAIN depth, otherwise (or unmeasured) the SCREEN depth (direction §15.1).</summary>
        public const float CurtainThresholdSeconds = 0.9f;

        /// <summary>
        /// Tests and captures (direction §15.7 T15/T16 "yapay yükleme"): &gt; 0 caps the reported
        /// progress so a load takes at least this long (0.9 reached at this many seconds after
        /// LoadSceneAsync). 0 = the real progress.
        /// </summary>
        public static float ArtificialLoadSeconds;

        /// <summary>The loading group stays at least this long (2 × Reveal) once shown - no flicker.</summary>
        public const float MinimumDisplaySeconds = 1.2f;

        /// <summary>If the load is done before the group would enter, the doors stay shut at least this long (CURTAIN depth).</summary>
        public const float MinimumClosedSeconds = 0.3f;

        /// <summary>The group enters this long after the doors start closing; a load already at 0.9 by then never shows it.</summary>
        public const float GroupEntrySeconds = 0.3f;

        /// <summary>No progress for this long: a log line and the on-screen notice under the tip.</summary>
        public const float StallSeconds = 10f;

        /// <summary>No progress for this long: the load error modal (QUIT only - an async load cannot be cancelled).</summary>
        public const float StallErrorSeconds = 60f;

        /// <summary>AsyncOperation.progress stops at 0.9 while activation is held.</summary>
        private const float LoadedProgress = 0.9f;

        private static bool s_loading;

        /// <summary>
        /// U9 / direction §15.1: a loading screen carries a tip on the way to Selection, into the run
        /// and back to the menu from Results - never at Boot, into Results, or after a Pause abort
        /// (no coaching after quitting).
        /// </summary>
        public static bool ShowsTip(string from, string to)
        {
            if (to == PrototypeScenes.Results || from == PrototypeScenes.Boot)
            {
                return false;
            }

            return !(from == PrototypeScenes.Run && to == PrototypeScenes.MainMenu);
        }
        private static string s_loadingScene;
        private static int s_loadGeneration;

        private static void Load(string sceneName)
        {
            if (s_loading)
            {
                Debug.Log("[UI] Load of '" + sceneName + "' ignored: a scene is still loading.");
                return;
            }

            s_transition++;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                // Deliberately a log, not an error: the scenes are created by
                // Tools > Deaths Demolition > UI > Build Meta Scenes, and acceptance
                // criterion 27 forbids console errors during normal use.
                ReleaseSimulation();
                Debug.Log(
                    "[UI] Scene '" + sceneName + "' is not in the build settings. " +
                    "Run Tools > Deaths Demolition > UI > Build Meta Scenes to create the meta scenes.");
                return;
            }

            s_loading = true;
            s_loadingScene = sceneName;
            string from = SceneManager.GetActiveScene().name;
            UiFlowRunner.Instance.Run(LoadRoutine(sceneName, from, ++s_loadGeneration));
        }

        /// <summary>
        /// Direction §15.2: doors close → two frames → LoadSceneAsync with activation held →
        /// honest progress 0–0.9 → gate (progress ≥ 0.9 and the minimum display) →
        /// activation (ReleaseSimulation here, not at the request: the frozen world never
        /// runs behind the doors) → two frames so the new scene renders once behind the
        /// doors → group out, doors open, Revealed. One load at a time (IsLoading ends in
        /// OnSceneLoaded); WP-061J's counter bumps at the request and again in OnSceneLoaded,
        /// exactly as before. A newer load (<paramref name="generation"/>) owns the doors from
        /// its request on, so this routine's reveal steps aside.
        /// </summary>
        private static IEnumerator LoadRoutine(string sceneName, string from, int generation)
        {
            UiCurtain curtain = UiCurtain.Instance;
            // Direction §15.1 / UXW §1.1: tips on the way to Selection, into the run and back to the
            // menu from Results; none at Boot, into Results, or after a Pause abort (no coaching
            // after quitting). The mid plate (lanterns lit) and the weapon sub-line go into the run.
            bool intoRun = sceneName == PrototypeScenes.Run;
            bool tipped = ShowsTip(from, sceneName);
            string subline = intoRun ? ProfileService.WeaponDisplayName(SelectedWeapon).ToUpperInvariant() : string.Empty;
            string tip = null;
            bool fast = FastTransitions;
            float lastLoad = UiLoadTiming.LastLoadSeconds(sceneName);
            bool screen = !fast && (lastLoad < 0f || lastLoad >= CurtainThresholdSeconds);

            float t0 = Time.unscaledTime;
            curtain.Close(fast);
            float groupShownAt = -1f;
            if (screen && UiMotion.ReducedMotion)
            {
                // Reduced Motion (AD 10b-1 MUST-1, §15.5): the doors cut shut at t0 and the group is
                // at rest at t0 - the SCREEN decision comes from the table only, never from the
                // anti-flicker race (removing motion must not remove the destination or the tip).
                tip = tipped ? UiLoadingTips.PickForLoadingScreen() : null;
                curtain.ShowGroup(UiCurtain.DestinationFor(sceneName), subline, tip, intoRun);
                groupShownAt = t0;
            }
            if (!fast && !UiMotion.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(UiMotionTokens.Seconds(UiDuration.Screen));
            }

            // Two frames with the doors fully shut before the load starts (batchmode rule),
            // so the load's own hitch never lands in the middle of a tween. The S-08 close cue
            // lands at least one frame before LoadSceneAsync.
            UiEvents.Raise(UiEvent.CurtainClosed, sceneName);
            yield return null;
            yield return null;

            float loadStart = Time.unscaledTime;
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                // Direction §15.4: the doors open over the old scene after the minimum display and
                // the error modal stands on it. Nothing writes the profile.
                Debug.Log("[UI] Scene '" + sceneName + "' could not start loading; staying where we are.");
                if (!fast)
                {
                    yield return new WaitForSecondsRealtime(MinimumDisplaySeconds);
                }

                ReleaseSimulation();
                s_loading = false;
                curtain.Open(fast);
                bool menuFailed = sceneName == PrototypeScenes.MainMenu;
                string failed = sceneName;
                curtain.ShowError(sceneName, false, menuFailed ? "TRY AGAIN" : "RETURN TO MAIN MENU",
                    () => Load(menuFailed ? failed : PrototypeScenes.MainMenu));
                yield break;
            }

            op.allowSceneActivation = false;

            float lastProgress = -1f;
            float lastProgressAt = Time.unscaledTime;
            bool stallLogged = false;
            bool stallError = false;
            while (true)
            {
                float now = Time.unscaledTime;
                float progress = op.progress;
                if (ArtificialLoadSeconds > 0f)
                {
                    progress = Mathf.Min(progress, LoadedProgress * Mathf.Clamp01((now - loadStart) / ArtificialLoadSeconds));
                }

                if (progress > lastProgress + 0.001f)
                {
                    lastProgress = progress;
                    lastProgressAt = now;
                }
                else if (!stallLogged && now - lastProgressAt > StallSeconds)
                {
                    stallLogged = true;
                    Debug.Log("[UI] Loading '" + sceneName + "' has not progressed for " + StallSeconds + " s.");
                    curtain.ShowStalled();
                }
                else if (!stallError && now - lastProgressAt > StallErrorSeconds)
                {
                    stallError = true;
                    curtain.ShowError(sceneName, true, null, null); // QUIT only; if the load recovers, the modal goes
                }

                if (!screen && !fast && groupShownAt < 0f && now - loadStart >= CurtainThresholdSeconds && progress < LoadedProgress)
                {
                    // §15.2 "slower than expected": a load measured fast (CURTAIN) that is still
                    // running 0.9 s into LoadSceneAsync - the span load-timing.json records - gets the
                    // full group late; the minimum display counts from here.
                    screen = true;
                }

                if (screen && groupShownAt < 0f && now - t0 >= GroupEntrySeconds)
                {
                    if (progress >= LoadedProgress)
                    {
                        screen = false; // done before the group would enter: CURTAIN, no flicker
                    }
                    else
                    {
                        if (tipped && tip == null)
                        {
                            tip = UiLoadingTips.PickForLoadingScreen(); // one tip per screen, never swapped mid-load
                        }

                        curtain.ShowGroup(UiCurtain.DestinationFor(sceneName), subline, tipped ? tip : null, intoRun);
                        groupShownAt = now;
                    }
                }

                if (groupShownAt >= 0f)
                {
                    curtain.SetProgress(progress);
                }

                bool loaded = progress >= LoadedProgress;
                bool shownLongEnough = fast
                    || (groupShownAt >= 0f
                        ? now - groupShownAt >= MinimumDisplaySeconds
                        : !screen && now - t0 >= MinimumClosedSeconds);
                if (loaded && shownLongEnough)
                {
                    break;
                }

                yield return null;
            }

            if (stallError)
            {
                curtain.HideError();
            }

            // Activation: the seal step, the world is released, the scene may activate.
            curtain.Activate();
            ReleaseSimulation();
            op.allowSceneActivation = true;
            while (!op.isDone)
            {
                yield return null;
            }

            s_loading = false; // normally already cleared by OnSceneLoaded
            if (!fast)
            {
                // Only real loads are measured: FastTransitions (batch, tests) skips the curtain's
                // timings, and a batch run writes no load-timing.json.
                UiLoadTiming.RecordLoad(sceneName, Time.unscaledTime - loadStart);
            }

            // The new scene renders once behind the closed doors.
            yield return null;
            yield return null;
            if (generation != s_loadGeneration)
            {
                yield break;
            }

            curtain.Open(fast);
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
            SceneManager.sceneLoaded += OnSceneLoaded;

            // WP-022D (WP-035 contract): the cosmetics source lists skins with the
            // SESSION selection (DEC-039: never persisted), and the UI flow owns it.
            ProfileService.SessionSkinProvider = () => SelectedSkin;

            // Created eagerly rather than on first use: it is also where the profile is
            // flushed on quit and where the F1 rows live, and both of those have to
            // exist before anything interesting happens.
            _ = UiFlowRunner.Instance;
        }

        /// <summary>
        /// WP-061J: any single-mode scene load is a transition, whoever asked for it. The delayed death →
        /// Results hand-off was only cancelled by <see cref="Load"/>, so a Run scene entered another way
        /// (direct-to-run boot, a probe, a test's reload) was yanked to Results by the PREVIOUS run's pending
        /// trip — the new run lost its spawn director mid-start. A fresh Run scene also re-arms the hand-off.
        /// </summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;   // the additive world scene is part of the same Run
            s_transition++;
            if (s_loading && scene.name == s_loadingScene) s_loading = false;   // WP-068: the load ends at activation, not after the reveal
            if (scene.name == PrototypeScenes.Run) s_resultsScheduled = false;
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
            s_loading = false;
            s_loadingScene = null;
            s_loadGeneration = 0;
            FastTransitions = Application.isBatchMode;
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
