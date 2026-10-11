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

        /// <summary>Saves the profile and leaves at once. The main menu's QUIT asks first (<see cref="RequestQuit"/>); probes and tools call this.</summary>
        public static void Quit()
        {
            ProfileService.Save();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ================================================= WP-098B: the QUIT confirmation

        /// <summary>The QUIT confirmation's root name (tests and captures find it by it).</summary>
        public const string QuitConfirmName = "QuitConfirm";

        private static UiModal s_quitConfirm;

        /// <summary>
        /// WP-098B (C-00 07.10; brush kit KILAVUZ §3, C31): QUIT THE GAME? - the heading only (no body: fewer letters), the
        /// SAFE "NO" first and focused so a pad's A pressed twice does not close the game; QUIT says what it does in a word
        /// and the Menu.Quit glyph, never a colour.
        /// </summary>
        public static UiBrushConfirm QuitSpec
        {
            get
            {
                return new UiBrushConfirm
                {
                    TitleKey = "menu.quit.confirm",
                    TitleFallback = "QUIT THE GAME?",
                    BodyKey = null,
                    BodyFallback = null,
                    ConfirmKey = "menu.quit",
                    ConfirmFallback = "QUIT",
                    ConfirmIcon = "Menu.Quit",
                    CancelKey = "common.no",
                    CancelFallback = "NO",
                    CancelIcon = "Menu.Back",
                    Destructive = true,
                };
            }
        }

        /// <summary>
        /// WP-098B (C-00 07.10): the main menu's QUIT asks first - the brush kit's Confirm (<see cref="UiBrushKit.Confirm"/>,
        /// the same dialog as the pause screen's END THIS RUN?) with <see cref="QuitSpec"/>; confirming is <see cref="Quit"/>,
        /// unchanged. Built on the main menu's canvas on each request (a press, never per frame), so the language and text
        /// scale are the current ones. With no main menu on screen it quits at once, as QUIT always did.
        /// </summary>
        public static void RequestQuit()
        {
            if (QuitConfirmOpen)
            {
                return;
            }

            RectTransform host = QuitHost();
            if (host == null)
            {
                Quit();
                return;
            }

            if (s_quitConfirm != null)
            {
                Object.Destroy(s_quitConfirm.gameObject);
            }

            s_quitConfirm = UiBrushKit.Confirm(QuitConfirmName, host, QuitSpec, Quit, null);
            PauseScreen.KeepConfirmNames(s_quitConfirm);
            PauseScreen.RaiseToDialogLayer(s_quitConfirm);
            s_quitConfirm.gameObject.AddComponent<QuitConfirmInput>().Bind(s_quitConfirm);
            s_quitConfirm.Open();
        }

        /// <summary>
        /// The QUIT confirmation is up. A screen that polls Cancel under it skips its own Cancel while this is true: the
        /// confirmation reads Cancel in LateUpdate (<see cref="QuitConfirmInput"/>), after every screen's Update, so on the
        /// press that closes it the flag is still set.
        /// </summary>
        public static bool QuitConfirmOpen
        {
            get { return s_quitConfirm != null && s_quitConfirm.IsOpen; }
        }

        /// <summary>The last QUIT confirmation built (tests); null outside the main menu.</summary>
        public static UiModal QuitConfirmation
        {
            get { return s_quitConfirm; }
        }

        /// <summary>The main menu screen's own canvas (its first child), so the dialog renders on the menu's path.</summary>
        private static RectTransform QuitHost()
        {
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Canvas canvas = menu != null ? menu.GetComponentInChildren<Canvas>() : null;
            return canvas != null ? (RectTransform)canvas.transform : null;
        }

        /// <summary>
        /// The QUIT confirmation's Cancel (pad B / Esc = NO) and focus reclaim. On the dialog's own root, so it runs only
        /// while the dialog is open. LateUpdate, not Update: it runs after every screen's Update whatever the script order,
        /// so the menu still reads <see cref="QuitConfirmOpen"/> on the press that closes it, and the focus this hands back
        /// to QUIT is the frame's last word.
        /// </summary>
        private sealed class QuitConfirmInput : MonoBehaviour
        {
            private UiModal _confirm;

            public void Bind(UiModal confirm)
            {
                _confirm = confirm;
            }

            private void LateUpdate()
            {
                if (_confirm == null)
                {
                    return;
                }

                if (UiInput.CancelPressed())
                {
                    _confirm.HandleCancel();
                    return;
                }

                UiInput.ReclaimFocus(_confirm.Secondary);
            }
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

        /// <summary>
        /// AP-035 R3 (S-11 O2, C-00 08.10, K4): the AP-035 logo plays on every SCREEN-depth loading screen except the way to
        /// the Results (the ink variant and its short key-art screen). The boot keeps its own seal (K5).
        /// </summary>
        public static bool ShowsLogo(string from, string to)
        {
            return to != PrototypeScenes.Results && from != PrototypeScenes.Boot;
        }

        private static string s_loadingScene;
        private static int s_loadGeneration;

        /// <summary>
        /// Direction §15.4 / UXW §2.2: a load that cannot start. The menu retries itself; every other scene offers the
        /// way back to the menu. QUIT is always the second action.
        /// </summary>
        private static void OnFirstSceneMissing(string sceneName)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            UiCurtain curtain = UiCurtain.Instance;
            curtain.Open(true);
            ShowLoadError(curtain, sceneName);
        }

        private static void ShowLoadError(UiCurtain curtain, string sceneName)
        {
            bool menuFailed = sceneName == PrototypeScenes.MainMenu;
            curtain.ShowError(sceneName, false, menuFailed ? UiStrings.Get("loading.error.retry", "TRY AGAIN") : UiStrings.Get("loading.error.to_menu", "RETURN TO MAIN MENU"),
                () => Load(menuFailed ? sceneName : PrototypeScenes.MainMenu));
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>R1-E E4 (dev / Editor only): the capture agent's way to request a load by name, a missing scene included.</summary>
        internal static void LoadForDiagnostics(string sceneName)
        {
            Load(sceneName);
        }
#endif

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
                // R1-E B1a (direction §15.4): the press is answered - the load error stands on the current screen with
                // the way back and QUIT, instead of a press that does nothing. Nothing writes the profile. Play mode
                // only: an EditMode caller (tests) has no curtain - it is a DontDestroyOnLoad object.
                if (Application.isPlaying)
                {
                    ShowLoadError(UiCurtain.Instance, sceneName);
                }

                return;
            }

            // WP-102: entering the Run scene fixes the run's difficulty (Hard / Demolition: a mode-applied config copy goes active);
            // any other scene puts the shipped config back
            DD.Prototype.Difficulty.DifficultyService.OnSceneLoad(sceneName == PrototypeScenes.Run);
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
            UiCurtain.LogoNext = !FastTransitions && ShowsLogo(from, sceneName);   // AP-035 R3 K4: the next ShowGroup plays it
            string subline = intoRun ? UiStrings.Upper(ProfileService.WeaponDisplayName(SelectedWeapon)) : string.Empty;
            string tip = null;
            bool fast = FastTransitions;
            float lastLoad = UiLoadTiming.LastLoadSeconds(sceneName);
            // §15.1 ink variant: Run → Results loads under the death beat's ink - no doors, no
            // plate group; the Ø 160 ring stands in the ink only past InkRingSeconds.
            bool ink = !fast && sceneName == PrototypeScenes.Results && curtain.InkCovering;
            bool screen = !fast && !ink && (lastLoad < 0f || lastLoad >= CurtainThresholdSeconds);
            float ringShownAt = -1f;

            float t0 = Time.unscaledTime;
            float closeSeconds = 0f;
            if (ink)
            {
                curtain.CoverWithInk();
            }
            else
            {
                // WP-098A (HAREKET-MENU M14): ScreenExiting at t0, CurtainClosed once the cover is whole (below). The close's own
                // length - the doors' Screen 280 or the brush's Emphasis 400 - is what the load waits for.
                UiEvents.Raise(UiEvent.ScreenExiting, sceneName);
                closeSeconds = curtain.Close(fast);
            }

            float groupShownAt = -1f;
            if (screen && UiMotion.ReducedMotion)
            {
                // Reduced Motion (AD 10b-1 MUST-1, §15.5): the doors cut shut at t0 and the group is
                // at rest at t0 - the SCREEN decision comes from the table only, never from the
                // anti-flicker race (removing motion must not remove the destination or the tip).
                tip = tipped ? UiLoadingTips.PickForLoadingScreen() : null;
                curtain.ShowGroup(UiCurtain.DestinationFor(sceneName), subline, tip, intoRun, UiCurtain.KeyArtFor(sceneName));
                groupShownAt = t0;
            }
            if (!fast && !ink && !UiMotion.ReducedMotion && closeSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(closeSeconds);
            }

            // Two frames with the doors fully shut before the load starts (batchmode rule),
            // so the load's own hitch never lands in the middle of a tween. The S-08 close cue
            // lands at least one frame before LoadSceneAsync.
            UiEvents.Raise(UiEvent.CurtainClosed, sceneName);
            yield return null;
            yield return null;

            float loadStart = Time.unscaledTime;
            float loadedAt = -1f;   // WP-081: the raw load (progress 0.9), reported beside the curtain-bound span
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
                ShowLoadError(curtain, sceneName);
                yield break;
            }

            op.allowSceneActivation = false;

            var watchdog = new LoadWatchdog(sceneName, curtain, Time.unscaledTime);
            while (true)
            {
                float now = Time.unscaledTime;
                float progress = op.progress;
                if (ArtificialLoadSeconds > 0f)
                {
                    progress = Mathf.Min(progress, LoadedProgress * Mathf.Clamp01((now - loadStart) / ArtificialLoadSeconds));
                }

                watchdog.Tick(progress, now);   // QUIT-only error at 60 s; if the load recovers, the modal goes

                if (ink && ringShownAt < 0f && now - t0 >= InkRingSeconds && progress < LoadedProgress)
                {
                    // WP-068 12b (C-00 decision A): a slow load under the ink opens the Results key art inside it.
                    curtain.ShowInkGroup(UiCurtain.DestinationFor(sceneName), UiCurtain.KeyArtFor(sceneName));
                    ringShownAt = now;
                    Debug.Log("[UI] Results key art opened in the ink at +" + (now - t0).ToString("F2") + " s (progress " + progress.ToString("F2") + ").");
                }

                if (ringShownAt >= 0f)
                {
                    curtain.SetProgress(progress);
                }

                if (!screen && !fast && !ink && groupShownAt < 0f && now - loadStart >= CurtainThresholdSeconds && progress < LoadedProgress)
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

                        curtain.ShowGroup(UiCurtain.DestinationFor(sceneName), subline, tipped ? tip : null, intoRun, UiCurtain.KeyArtFor(sceneName));
                        groupShownAt = now;
                    }
                }

                if (groupShownAt >= 0f)
                {
                    curtain.SetProgress(progress);
                }

                bool loaded = progress >= LoadedProgress;
                if (loaded && loadedAt < 0f)
                {
                    loadedAt = now;
                }

                bool shownLongEnough = fast
                    || (groupShownAt >= 0f
                        ? now - groupShownAt >= MinimumDisplaySeconds
                        : ink
                            ? ringShownAt < 0f || now - ringShownAt >= BootHandOffMinimumSeconds   // the death beat was the display
                            : !screen && now - t0 >= MinimumClosedSeconds);
                if (loaded && shownLongEnough)
                {
                    // AP-035 R3 K3: the logo's intro may hold a ready load until its canonical frame - at most 0.5 s.
                    curtain.NotifyLogoReady();
                    if (fast || curtain.LogoCanLeave)
                    {
                        break;
                    }
                }

                yield return null;
            }

            if (watchdog.ErrorShown)
            {
                curtain.HideError();
            }

            if (ink)
            {
                Debug.Log("[UI] Results load under the ink done at +" + (Time.unscaledTime - t0).ToString("F2") + " s (key art " + (ringShownAt >= 0f ? "shown" : "not needed") + ").");
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
                UiLoadTiming.RecordLoad(sceneName, Time.unscaledTime - loadStart, loadedAt >= 0f ? loadedAt - loadStart : -1f);
            }

            // The new scene renders once behind the closed doors.
            yield return null;
            yield return null;
            if (generation != s_loadGeneration)
            {
                yield break;
            }

            // WP-081: the main menu's real-time world arrives behind the closed doors (capped; the raw load above is P5's).
            yield return WaitForMenuWorld();
            if (generation != s_loadGeneration)
            {
                yield break;
            }

            curtain.Open(fast);
        }

        /// <summary>The doors' extra wait for the menu's world on the last arrival, seconds (0 = none; probes, the R2 report).</summary>
        public static float LastMenuWorldWaitSeconds { get; private set; }

        /// <summary>
        /// WP-081 (C-00 05.10, default 1): when the scene that just activated is the main menu and its cinematic world is
        /// still loading, the doors stay shut until it shows - the world's activation hitch lands behind them, never in the
        /// menu - for at most <see cref="MenuCinematic.RevealCapSeconds"/>; past that they open on the plates and the world
        /// fades in when it is ready. Nothing waits in batch or without the world (<see cref="MenuCinematic.Allowed"/>).
        /// </summary>
        private static IEnumerator WaitForMenuWorld()
        {
            LastMenuWorldWaitSeconds = 0f;
            if (!MenuCinematic.HoldsReveal)
            {
                yield break;
            }

            float t0 = Time.unscaledTime;
            while (MenuCinematic.HoldsReveal)
            {
                yield return null;
            }

            LastMenuWorldWaitSeconds = Time.unscaledTime - t0;
            MenuCinematic current = MenuCinematic.Current;
            Debug.Log("[UI] Menu world behind the doors: +" + LastMenuWorldWaitSeconds.ToString("F2") + " s"
                + (current != null && current.Live ? "." : " (cap reached: the doors open on the plates, the world fades in when ready)."));
        }

        // ============================================================ boot seal (10b-2)

        /// <summary>E1 hold: the seal choreography's length, and its Reduced Motion static composite (§15.5).</summary>
        public const float BootHoldSeconds = 1.6f;
        public const float BootHoldReducedSeconds = 1.0f;

        /// <summary>After the hand-off, LOADING and the ring never show for less than this (§15.2 anti-flicker).</summary>
        public const float BootHandOffMinimumSeconds = 0.6f;

        /// <summary>
        /// Direction §15.2 / §15.4 - one watchdog for the boot load and every later transition (M-70; c29b4108 had copied
        /// it). No progress for <see cref="StallSeconds"/>: the stall notice (and a log). No progress for
        /// <see cref="StallErrorSeconds"/>: the QUIT-only error, which stays until the load completes. Progress coming back
        /// before the error takes the notice away and re-arms the watchdog, so a second stall shows it again.
        /// </summary>
        private sealed class LoadWatchdog
        {
            private readonly string _scene;
            private readonly UiCurtain _curtain;
            private float _lastProgress = -1f;
            private float _lastProgressAt;
            private bool _stallShown;

            public LoadWatchdog(string scene, UiCurtain curtain, float now)
            {
                _scene = scene;
                _curtain = curtain;
                _lastProgressAt = now;
            }

            /// <summary>True once the error is up; the caller takes it down when the load completes.</summary>
            public bool ErrorShown { get; private set; }

            public void Tick(float progress, float now)
            {
                if (progress > _lastProgress + 0.001f)
                {
                    _lastProgress = progress;
                    _lastProgressAt = now;
                    if (_stallShown && !ErrorShown)
                    {
                        _stallShown = false;
                        _curtain.HideStalled();
                    }

                    return;
                }

                if (!_stallShown && !ErrorShown && now - _lastProgressAt > StallSeconds)
                {
                    _stallShown = true;
                    Debug.Log("[UI] Loading '" + _scene + "' has not progressed for " + StallSeconds + " s.");
                    _curtain.ShowStalled();
                }
                else if (!ErrorShown && now - _lastProgressAt > StallErrorSeconds)
                {
                    ErrorShown = true;
                    _curtain.ShowError(_scene, true, null, null);
                }
            }
        }

        /// <summary>
        /// Direction §15.3 / E1 (U7): the Boot scene hands its first load to the seal. With
        /// FastTransitions (batch, tests) the plain synchronous load stays - returns false.
        /// </summary>
        private static bool BootToFirstScene(string sceneName)
        {
            if (FastTransitions || s_loading || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                return false;
            }

            s_transition++;
            s_loading = true;
            s_loadingScene = sceneName;
            UiFlowRunner.Instance.Run(BootRoutine(sceneName, ++s_loadGeneration));
            return true;
        }

        /// <summary>
        /// t0 LoadSceneAsync with activation held while the seal choreography plays (ink blot,
        /// stamp + BootLogo bell, wordmark). At t 1.6 (1.0 RM) or on a skip: a scene that is
        /// ready is activated by the stamp - nothing else shows; one that is not hands off to
        /// LOADING and the ensō at its real progress (a skip never fakes readiness).
        /// </summary>
        private static IEnumerator BootRoutine(string sceneName, int generation)
        {
            UiCurtain curtain = UiCurtain.Instance;
            float t0 = Time.unscaledTime;
            curtain.BeginBoot();
            float hold = UiMotion.ReducedMotion ? BootHoldReducedSeconds : BootHoldSeconds;

            float loadStart = Time.unscaledTime;
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.Log("[UI] Scene '" + sceneName + "' could not start loading from Boot.");
                s_loading = false;
                curtain.Open(true);
                yield break;
            }

            op.allowSceneActivation = false;
            bool handedOff = false;
            float handOffAt = -1f;
            var watchdog = new LoadWatchdog(sceneName, curtain, Time.unscaledTime);
            while (true)
            {
                float now = Time.unscaledTime;
                float progress = op.progress;
                if (ArtificialLoadSeconds > 0f)
                {
                    progress = Mathf.Min(progress, LoadedProgress * Mathf.Clamp01((now - loadStart) / ArtificialLoadSeconds));
                }

                // The first load has the same watchdog as later scene transitions (§15.2/15.4) - the one LoadWatchdog (M-70).
                watchdog.Tick(progress, now);

                bool loaded = progress >= LoadedProgress;
                if (!handedOff && (curtain.BootSkipRequested || now - t0 >= hold))
                {
                    curtain.CompleteBoot();   // the choreography lands (a skip: t = 1, cues collapse)
                    if (loaded)
                    {
                        break;                // ready: the stamp is the activation
                    }

                    curtain.BootHandOff();
                    handedOff = true;
                    handOffAt = now;
                }

                if (handedOff)
                {
                    curtain.SetProgress(progress);
                    if (loaded && now - handOffAt >= BootHandOffMinimumSeconds)
                    {
                        break;
                    }
                }

                yield return null;
            }

            if (watchdog.ErrorShown)
            {
                curtain.HideError();
            }

            curtain.Activate();
            ReleaseSimulation();
            op.allowSceneActivation = true;
            while (!op.isDone)
            {
                yield return null;
            }

            s_loading = false;   // not recorded: the seal's hold hides the load time the table wants
            yield return null;
            yield return null;
            if (generation != s_loadGeneration)
            {
                yield break;
            }

            // WP-081: the first menu's world arrives behind the seal (capped, as after any load).
            yield return WaitForMenuWorld();
            if (generation != s_loadGeneration)
            {
                yield break;
            }

            curtain.Open(false);
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

            // WP-068 10b-2: the boot seal is the first loading screen (Core calls back into us).
            PrototypeBootstrapper.FirstSceneLoader = BootToFirstScene;
            // R1-E E4 B1b (S-01's Core hook): a first scene missing from the build opens the load error over Boot
            // (TRY AGAIN / QUIT) instead of a silent boot screen.
            PrototypeBootstrapper.FirstSceneMissing = OnFirstSceneMissing;

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
            if (summary.EndReason == RunEndReason.PlayerDeath && !FastTransitions)
            {
                UiFlowRunner.Instance.Run(InkAfter(s_transition, InkBleedStartSeconds));
            }
        }

        /// <summary>E22: the ink starts to bleed at t 0.6 of the death beat (the curtain's closing; §15.1 ink variant).</summary>
        public const float InkBleedStartSeconds = 0.6f;

        /// <summary>The Ø 160 ring appears in the ink only when the Results load runs past this (§15.3).</summary>
        public const float InkRingSeconds = 1.2f;

        private static IEnumerator InkAfter(int scheduledAt, float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            if (scheduledAt == s_transition && !s_loading)
            {
                UiCurtain.Instance.BeginInk();   // an abort (a newer transition) wins; its doors close under any ink
            }
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
            s_quitConfirm = null;
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
            int requirement = ProfileService.LevelRequirement(profile.ProfileLevel);
            return "L" + profile.ProfileLevel +
                   "  " + (UiLevelCap.IsCap(requirement) ? "MAX" : profile.XpIntoLevel + "/" + requirement + " XP") +
                   "  ·  " + profile.TotalKills + " kills  ·  best round " + profile.HighestRound +
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
