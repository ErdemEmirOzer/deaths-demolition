using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Boot;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>WP-068 closure: actual unscaled 10/60 s watchdog, recovery, and error actions.</summary>
    public sealed class LoadingWatchdogTests
    {
        private const float HeldLoadSeconds = 1000000000f;
        private bool _fast;
        private string _settingsPath;
        private string _timingPath;
        private string _profileHash;
        private bool _manualError;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-s09-watchdog-ui-" + Guid.NewGuid().ToString("N") + ".json");
            _timingPath = Path.Combine(Path.GetTempPath(), "dd-s09-watchdog-timing-" + Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            UiLoadTiming.SetPathOverride(_timingPath);
            UiSettings.EnsureLoaded();
            UiSettings.Language = UiStrings.English;
            UiSettings.TextScale = 1.25f;
            UiSettings.ReducedMotion = false;
            ProfileService.EnsureLoaded();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path, "Assembly profile isolation must be active.");
            _profileHash = ProfileHash();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // An activation-held AsyncOperation cannot be cancelled. Release the existing
            // load even after an assertion failed, before another case or Editor shutdown.
            PrototypeUiFlow.ArtificialLoadSeconds = 0f;
            if (_manualError && !PrototypeUiFlow.IsLoading)
            {
                UiCurtain.Instance.HideError();
                UiCurtain.Instance.Open(true);
                _manualError = false;
            }
            float deadline = Time.realtimeSinceStartup + 40f;
            while ((PrototypeUiFlow.IsLoading || UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
                yield return null;
            bool pending = PrototypeUiFlow.IsLoading || UiCurtain.IsCovering;
            Assert.IsFalse(pending, "Cleanup could not finish the existing real async load; preserve the original test failure and isolation, and stop this run.");
            UiCurtain.Instance.HideError();
            PrototypeUiFlow.FastTransitions = _fast;
            RunFlowClock.ClearHolds();
            UiSettings.SetPathOverride(null);
            UiLoadTiming.SetPathOverride(null);
            foreach (string path in new[] { _settingsPath, _timingPath })
                if (File.Exists(path)) File.Delete(path);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Boot_AutomaticWatchdog_10sNotice_60sQuitOnly_Recovery()
        {
            PrototypeUiFlow.FastTransitions = false;
            PrototypeUiFlow.ArtificialLoadSeconds = HeldLoadSeconds;
            float requestAt = Time.realtimeSinceStartup;
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return Bekle(() => PrototypeUiFlow.IsLoading && UiCurtain.Instance.BootVisible, 10f, "Boot async load did not start.");
            UiFactory.EnsureEventSystem();
            yield return ProveWatchdog(PrototypeScenes.Boot, PrototypeScenes.MainMenu, requestAt);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Transition_AutomaticWatchdog_10sNotice_60sQuitOnly_Recovery()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f);
            PrototypeUiFlow.ArtificialLoadSeconds = HeldLoadSeconds;
            float requestAt = Time.realtimeSinceStartup;
            PrototypeUiFlow.GoToSelection();
            yield return ProveWatchdog(PrototypeScenes.MainMenu, PrototypeScenes.Selection, requestAt);
        }

        /// <summary>
        /// M-70 (Codex fix list): the load held for 12 s shows the stall notice; when progress comes back (the hold eased
        /// to a slow real climb, still loading) the notice goes and the watchdog re-arms - no error, the load completes.
        /// </summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator Transition_StallNotice_LeavesWhenProgressReturns()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f);
            PrototypeUiFlow.ArtificialLoadSeconds = HeldLoadSeconds;
            float requestAt = Time.realtimeSinceStartup;
            PrototypeUiFlow.GoToSelection();
            UiCurtain curtain = UiCurtain.Instance;
            yield return Bekle(() => curtain.StallVisible, PrototypeUiFlow.StallSeconds + 5f, "Stall notice did not show while the load was held.");
            while (Time.realtimeSinceStartup - requestAt < 12f)
                yield return null;
            Assert.IsTrue(curtain.StallVisible, "Held 12 s: the notice stands.");
            // Progress returns but the load is not done: 0.9 × elapsed / 60 climbs from ≈ 0.18, activation stays held.
            PrototypeUiFlow.ArtificialLoadSeconds = 60f;
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsTrue(PrototypeUiFlow.IsLoading, "Still loading after progress returned.");
            Assert.IsFalse(curtain.StallVisible, "Progress came back: the stall notice goes.");
            Assert.IsFalse(curtain.ErrorOpen, "No error on a recovered load.");
            PrototypeUiFlow.ArtificialLoadSeconds = 0f;
            yield return Bekle(() => !PrototypeUiFlow.IsLoading && !UiCurtain.IsCovering, 30f, "The recovered load did not complete.");
            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
            Debug.Log("[S09-WATCHDOG] M-70 stall notice left when progress returned at 12 s; load completed");
        }

        /// <summary>
        /// R4 WP-105 (V-49): a load to the selection screen held for 60 s opens the error with RETURN TO MAIN MENU (focused) and
        /// QUIT. Choosing the way back closes the modal and the loading screen names the main menu at once; the held load is
        /// not cancelled (Unity cannot) - when it lets go, it activates behind the closed cover and the main menu follows.
        /// Nothing writes the profile and no hold outlives it.
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator Takilma_AnaMenuyeDon_TakiliYukBirakilincaAnaMenuyeVarir()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            UiFactory.EnsureEventSystem();
            PrototypeUiFlow.FastTransitions = false;
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f);
            PrototypeUiFlow.ArtificialLoadSeconds = HeldLoadSeconds;
            float requestAt = Time.realtimeSinceStartup;
            PrototypeUiFlow.GoToSelection();
            UiCurtain curtain = UiCurtain.Instance;
            yield return Bekle(() => curtain.GroupVisible, 8f, "Loading group did not show.");
            yield return null;
            _profileHash = ProfileHash();
            yield return Bekle(() => curtain.ErrorOpen, PrototypeUiFlow.StallErrorSeconds + 20f, "The watchdog's error did not open.");
            Assert.GreaterOrEqual(Time.realtimeSinceStartup - requestAt, PrototypeUiFlow.StallErrorSeconds - 0.1f, "No early error.");

            UiModal modal = OpenError(curtain);
            Assert.IsNotNull(modal.Secondary, "the way back is offered off the main menu");
            Assert.AreEqual(UiStrings.Get("loading.error.to_menu"), modal.Secondary.LabelText.text);
            Assert.AreSame(modal.Secondary.gameObject, EventSystem.current.currentSelectedGameObject, "the way back is the safe, focused action");
            AssertErrorOnTop(curtain);

            Assert.IsTrue(ExecuteEvents.Execute(modal.Secondary.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler));
            yield return null;
            yield return null;
            Assert.IsFalse(curtain.ErrorOpen, "the way back closes the error");
            Assert.IsTrue(curtain.GroupVisible && curtain.DeviceVisible, "the loading screen is back, ring and labels with it");
            Assert.AreEqual(UiCurtain.DestinationFor(PrototypeScenes.MainMenu), curtain.DestinationText, "it now names the main menu");
            Assert.IsTrue(PrototypeUiFlow.IsLoading, "the held load is still in flight (an async load cannot be cancelled)");
            Assert.IsTrue(UiCurtain.IsCovering, "the cover stays closed");
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "activation is still held");

            // The held load lets go: Selection activates behind the cover, the main menu follows it.
            bool sawSelection = false;
            PrototypeUiFlow.ArtificialLoadSeconds = 0f;
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!(SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && !PrototypeUiFlow.IsLoading && !UiCurtain.IsCovering)
                   && Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().name == PrototypeScenes.Selection)
                {
                    sawSelection = true;
                    Assert.IsTrue(UiCurtain.IsCovering, "the selection screen is never revealed on the way back");
                }

                yield return null;
            }

            Assert.IsTrue(sawSelection, "the held load activated (it was let through, not dropped)");
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "the way back ends on the main menu");
            Assert.IsFalse(UiCurtain.IsCovering);
            Assert.IsFalse(curtain.ErrorOpen);
            Assert.AreEqual(0, RunFlowClock.HoldCount);
            Assert.AreEqual(_profileHash, ProfileHash(), "the way back writes no profile progress");
            Debug.Log("[S09-WATCHDOG] V-49 way back: held Selection load let through, main menu reached, profile=" + _profileHash);
        }

        /// <summary>
        /// R4 WP-105 (V-48): the load error is a canvas layer of its own (override sorting at UiCurtain.ErrorSortingOrder) on
        /// an overlay root - above every other canvas on screen but the debug overlay, the loading logo's included.
        /// </summary>
        private static void AssertErrorOnTop(UiCurtain curtain)
        {
            Canvas layer = curtain.ErrorCanvas;
            Assert.IsNotNull(layer, "the load error has its own canvas");
            Assert.IsTrue(layer.overrideSorting, "its own sorting order");
            Assert.AreEqual(UiCurtain.ErrorSortingOrder, layer.sortingOrder);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, layer.rootCanvas.renderMode, "drawn after every camera");
            Assert.Greater(UiCurtain.ErrorSortingOrder, UiCurtain.LogoSortingOrder, "over the loading logo's canvas");
            foreach (Canvas other in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (other == layer || !other.isActiveAndEnabled || (!other.isRootCanvas && !other.overrideSorting))
                {
                    continue;
                }

                if (other.renderMode == RenderMode.ScreenSpaceOverlay && other.sortingOrder < UiLayer.Debug)
                {
                    Assert.Less(other.sortingOrder, layer.sortingOrder, other.name + " would draw over the load error");
                }
            }
        }

        private IEnumerator ProveWatchdog(string oldScene, string destination, float requestAt)
        {
            UiCurtain curtain = UiCurtain.Instance;
            yield return Bekle(() => curtain.GroupVisible, 8f, "Loading group did not settle before the watchdog.");
            yield return null;
            // Ordinary screen entry may persist its HINT mask. Baseline after that
            // legitimate write, so the comparison isolates watchdog/modal/recovery.
            _profileHash = ProfileHash();
            float noticeAt = -1f;
            float deadline = requestAt + 80f;
            while (!curtain.ErrorOpen && Time.realtimeSinceStartup < deadline)
            {
                if (curtain.StallVisible && noticeAt < 0f)
                {
                    noticeAt = Time.realtimeSinceStartup;
                    Debug.Log("[S09-WATCHDOG] notice destination=" + destination + " elapsed=" + (noticeAt - requestAt).ToString("F3"));
                }
                Assert.IsTrue(PrototypeUiFlow.IsLoading, "Held real async load must remain in flight.");
                Assert.AreEqual(oldScene, SceneManager.GetActiveScene().name, "Held activation must preserve the old scene.");
                Assert.Less(curtain.ShownProgress, 0.001f, "Existing artificial delay holds reported progress without changing clocks.");
                yield return null;
            }
            float errorAt = Time.realtimeSinceStartup;
            Assert.GreaterOrEqual(noticeAt - requestAt, PrototypeUiFlow.StallSeconds - 0.1f, "No early stall notice.");
            Assert.IsTrue(curtain.ErrorOpen, "Actual watchdog must open the error modal by 80 s.");
            Assert.GreaterOrEqual(errorAt - requestAt, PrototypeUiFlow.StallErrorSeconds - 0.1f, "No early 60 s modal.");
            Assert.IsFalse(curtain.DeviceVisible, "The loading device steps out under the modal.");
            Assert.IsFalse(curtain.StallVisible, "The modal replaces the stall notice.");
            UiModal modal = OpenError(curtain);
            if (PrototypeUiFlow.StallOffersWayBack(destination))
            {
                // R4 WP-105 (V-49): off the main menu the stalled load also offers the way back (the safe action, focused).
                Assert.IsNotNull(modal.Secondary, "A held load bound for " + destination + " offers RETURN TO MAIN MENU beside QUIT.");
                Assert.AreEqual(UiStrings.Get("loading.error.to_menu"), modal.Secondary.LabelText.text);
            }
            else
            {
                Assert.IsNull(modal.Secondary, "A held load to the main menu offers QUIT only; it cannot be cancelled.");
            }

            Assert.AreEqual(UiStrings.Get("menu.quit"), modal.Primary.LabelText.text);
            AssertErrorOnTop(curtain);
            AssertIgnoredCancel(modal);
            LogAssert.Expect(LogType.Log, new Regex("ignored: a scene is still loading"));
            PrototypeUiFlow.GoToResults();
            Assert.IsTrue(PrototypeUiFlow.IsLoading);
            Assert.AreEqual(oldScene, SceneManager.GetActiveScene().name);
            Assert.AreEqual(_profileHash, ProfileHash(), "Neither watchdog nor modal writes the isolated profile.");
            Debug.Log("[S09-WATCHDOG] error destination=" + destination + " elapsed=" + (errorAt - requestAt).ToString("F3") + " quitOnly=1 cancelIgnored=1 profile=" + _profileHash);

            PrototypeUiFlow.ArtificialLoadSeconds = 0f;
            yield return Bekle(() => SceneManager.GetActiveScene().name == destination && !PrototypeUiFlow.IsLoading && !UiCurtain.IsCovering, 30f, "Existing real async load did not recover and reveal.");
            Assert.IsFalse(curtain.ErrorOpen, "Recovery removes the error modal before activation.");
            Assert.AreEqual(0, RunFlowClock.HoldCount);
            Assert.AreEqual(_profileHash, ProfileHash(), "Recovery does not write profile progress.");
            Debug.Log("[S09-WATCHDOG] recovered destination=" + destination + " errorOpen=0 loading=0 covering=0 profile=" + _profileHash);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ErrorModal_EN_PrimarySubmit_CancelIgnored_CopyFits()
        {
            yield return ProveErrorActions(UiStrings.English);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ErrorModal_TR_PrimarySubmit_CancelIgnored_CopyFits()
        {
            yield return ProveErrorActions(UiStrings.Turkish);
        }

        private IEnumerator ProveErrorActions(string language)
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            UiSettings.Language = language;
            UiCurtain curtain = UiCurtain.Instance;
            _manualError = true;
            curtain.Close(true);
            curtain.ShowGroup(UiCurtain.DestinationFor(PrototypeScenes.Run), string.Empty, null, false);
            int primaryCalls = 0;
            curtain.ShowError(PrototypeScenes.Run, false, UiStrings.Get("loading.error.to_menu"), () => primaryCalls++);
            yield return null;
            Canvas.ForceUpdateCanvases();
            UiModal modal = OpenError(curtain);
            // WP-098B (kit Confirm): the way back is the SAFE action (Secondary: first, focused), QUIT the confirming one.
            Assert.IsNotNull(modal.Secondary);
            Assert.AreEqual(UiStrings.Get("loading.error.to_menu"), modal.Secondary.LabelText.text);
            Assert.AreEqual(UiStrings.Get("menu.quit"), modal.Primary.LabelText.text);
            Assert.AreSame(modal.Secondary.gameObject, EventSystem.current.currentSelectedGameObject, "The safe way back is the initial focus.");
            AssertIgnoredCancel(modal);
            Assert.AreEqual(0, primaryCalls);
            TMP_Text body = FindText(modal, "Body");
            Assert.AreEqual(UiStrings.Format("loading.error.body", "value", UiCurtain.SentenceFor(PrototypeScenes.Run), "reason", UiStrings.Get("loading.error.missing"), ""), body.text);
            foreach (TMP_Text text in modal.GetComponentsInChildren<TMP_Text>()) AssertFits(text, language);
            Assert.AreEqual(_profileHash, ProfileHash());
            Assert.IsTrue(ExecuteEvents.Execute(modal.Secondary.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler));
            Assert.AreEqual(1, primaryCalls, "Actual Submit on the way back invokes its safe action once.");
            Assert.IsFalse(curtain.ErrorOpen, "Submit on the way back closes the modal.");
            Assert.AreEqual(_profileHash, ProfileHash());
            curtain.HideError();
            curtain.Open(true);
            _manualError = false;
            Debug.Log("[S09-WATCHDOG] modal language=" + language + " textScale=1.25 primaryCalls=1 cancelIgnored=1 profile=" + _profileHash);
        }

        /// <summary>
        /// R1-E B1a/B2: a scene missing from the build answers the press with the load error over the current screen
        /// (not a press that does nothing), and Escape / B under that modal is not the screen's "back".
        /// </summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator MissingScene_ErrorOverMenu_CancelIgnoredUnderModal_PrimaryReturns()
        {
            const string missing = "DD_R1E_NoSuchScene";
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            UiFactory.EnsureEventSystem();
            MainMenuScreen menu = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);
            menu.ShowPane(UiContext.Settings);
            yield return null;
            Assert.AreEqual(UiContext.Settings, UiNavigation.Layer);
            _profileHash = ProfileHash();
            _manualError = true;
            MethodInfo load = typeof(PrototypeUiFlow).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            Assert.IsNotNull(load);
            load.Invoke(null, new object[] { missing });
            yield return null;
            Canvas.ForceUpdateCanvases();
            UiCurtain curtain = UiCurtain.Instance;
            UiModal modal = OpenError(curtain);
            Assert.IsNotNull(modal.Secondary);
            Assert.AreEqual(UiStrings.Get("loading.error.to_menu"), modal.Secondary.LabelText.text);
            Assert.AreEqual(UiStrings.Get("menu.quit"), modal.Primary.LabelText.text);
            Assert.AreSame(modal.Secondary.gameObject, EventSystem.current.currentSelectedGameObject, "The safe way back is the initial focus.");
            Assert.IsFalse(PrototypeUiFlow.IsLoading, "Nothing is loading: the scene never started.");
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name);

            // R4 WP-105 (V-48): the error is its own canvas layer over every menu canvas (the rail dims under its ink).
            AssertErrorOnTop(curtain);
            // R4 WP-105 (SHOULD-13): the error owns the focus - the menu's own reclaim (PaneWatch) is turned into the error's
            // safe action, never a control behind it; a click on the ink that emptied the selection is no way behind it either.
            Assert.AreSame(modal, UiInput.TopModal, "the load error is the modal on top");
            UnityEngine.UI.Selectable behind = menu.GetComponentInChildren<UnityEngine.UI.Selectable>();
            Assert.IsNotNull(behind, "a control on the menu under the error");
            Assert.AreSame(modal.Secondary, UiInput.ReclaimTarget(behind), "a reclaim from the menu lands on the error's safe action");
            EventSystem.current.SetSelectedGameObject(null);   // what a click on the ink leaves behind
            yield return null;
            yield return null;
            GameObject afterClick = EventSystem.current.currentSelectedGameObject;
            Assert.IsTrue(afterClick == null || afterClick.transform.IsChildOf(modal.transform),
                "an emptied selection never lands on the menu behind the error (" + (afterClick != null ? afterClick.name : "none") + ")");
            UiInput.Focus(modal.Secondary);

            // Escape / B under the modal: the menu takes no "back" (Settings → Profile) and focus stays on the modal.
            UiInput.SimulateCancelNextFrame();
            yield return null;
            yield return null;
            Assert.IsTrue(curtain.ErrorOpen, "Cancel keeps the load error open.");
            Assert.AreEqual(UiContext.Settings, UiNavigation.Layer, "The screen under the load error ignores Cancel.");
            Assert.AreSame(modal.Secondary.gameObject, EventSystem.current.currentSelectedGameObject, "Cancel does not move focus off the modal.");
            Assert.AreEqual(_profileHash, ProfileHash(), "Neither the error nor Cancel writes the profile.");

            // The safe action is the way back: a real main-menu load.
            Assert.IsTrue(ExecuteEvents.Execute(modal.Secondary.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler));
            Assert.IsFalse(curtain.ErrorOpen, "Submit on the way back closes the modal.");
            Assert.IsNull(UiInput.TopModal, "the closed error gives the focus back to the screens");
            _manualError = false;
            yield return Bekle(() => !PrototypeUiFlow.IsLoading && !UiCurtain.IsCovering, 20f, "The way back did not reach the main menu.");
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name);

            // Control: with no modal the same simulated press reaches the menu, so the check above is meaningful.
            menu = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
            menu.ShowPane(UiContext.Settings);
            yield return null;
            UiInput.SimulateCancelNextFrame();
            yield return null;
            yield return null;
            Assert.AreEqual(UiContext.None, UiNavigation.Layer, "Without the modal, Cancel takes the menu's back (WP-081 v4: the open pane closes).");
            Debug.Log("[S09-WATCHDOG] missing scene: modal over MainMenu, cancel ignored under it, primary returned, control cancel reached the menu");
        }

        /// <summary>R1-E E4 B1b: Core's FirstSceneMissing (the boot's first scene not in the build) opens the load error, TRY AGAIN + QUIT.</summary>
        [UnityTest, Timeout(30000)]
        public IEnumerator BootFirstSceneMissing_OpensLoadError_RetryAndQuit()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            UiFactory.EnsureEventSystem();
            Assert.IsNotNull(PrototypeBootstrapper.FirstSceneMissing, "The UI registers with Core's hook at install.");
            _manualError = true;
            PrototypeBootstrapper.FirstSceneMissing(PrototypeScenes.MainMenu);
            yield return null;
            UiCurtain curtain = UiCurtain.Instance;
            UiModal modal = OpenError(curtain);
            Assert.IsNotNull(modal.Secondary);
            Assert.AreEqual(UiStrings.Get("loading.error.retry"), modal.Secondary.LabelText.text, "The menu itself missing: TRY AGAIN (the safe action).");
            Assert.AreEqual(UiStrings.Get("menu.quit"), modal.Primary.LabelText.text);
            Assert.AreSame(modal.Secondary.gameObject, EventSystem.current.currentSelectedGameObject, "TRY AGAIN is the initial focus.");
            Assert.IsFalse(PrototypeUiFlow.IsLoading);
            curtain.HideError();
            curtain.Open(true);
            _manualError = false;
        }

        /// <summary>
        /// WP-098B (brush kit KILAVUZ §3): a stalled load is the single-action notice - QUIT only (no way back, CancelKey
        /// null), focused, Cancel ignored; the kit's Confirm scrim; QUIT said in a word, never in Danger red.
        /// </summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator ErrorModal_Takilma_TekEylem_ConfirmPerdesi_IptalYok()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            UiFactory.EnsureEventSystem();
            UiCurtain curtain = UiCurtain.Instance;
            _manualError = true;
            curtain.Close(true);
            curtain.ShowGroup(UiCurtain.DestinationFor(PrototypeScenes.Results), string.Empty, null, false);
            curtain.ShowError(PrototypeScenes.Results, true, null, null);
            yield return null;
            UiModal modal = OpenError(curtain);
            Assert.IsNull(modal.Secondary, "tek eylem: geri dönüş yok");
            Assert.AreEqual(UiStrings.Get("menu.quit"), modal.Primary.LabelText.text);
            Assert.AreNotEqual(UiButtonVariant.Danger, modal.Primary.Variant, "QUIT renkle değil kelime + ikonla");
            Assert.AreSame(modal.Primary.gameObject, EventSystem.current.currentSelectedGameObject, "tek eylem odakta");
            Assert.AreEqual(UiBrushTokens.ScrimConfirm, ScrimAlpha(modal), 1e-3f, "yükleme hatası = Confirm perdesi");
            AssertIgnoredCancel(modal);
            UiInput.SimulateCancelNextFrame();
            yield return null;
            yield return null;
            Assert.IsTrue(curtain.ErrorOpen, "Cancel bilerek yok sayılır");

            UiBrushConfirm spec = UiCurtain.LoadErrorSpec("body", null);
            Assert.IsNull(spec.CancelKey, "tek eylemli bildirim: CancelKey null");
            Assert.AreEqual("Menu.Quit", spec.ConfirmIcon);
            Assert.IsTrue(spec.Destructive);
            spec = UiCurtain.LoadErrorSpec("body", UiStrings.Get("loading.error.retry"));
            Assert.AreEqual("loading.error.retry", spec.CancelKey, "geri dönüşün okuyucu adı bir anahtar");
            Assert.AreEqual("Menu.Retry", spec.CancelIcon);
            spec = UiCurtain.LoadErrorSpec("body", UiStrings.Get("loading.error.to_menu"));
            Assert.AreEqual("loading.error.to_menu", spec.CancelKey);
            Assert.AreEqual("Menu.Home", spec.CancelIcon);

            curtain.HideError();
            curtain.Open(true);
            _manualError = false;
        }

        private static TMP_Text FindText(UiModal modal, string name)
        {
            foreach (TMP_Text text in modal.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == name)
                {
                    return text;
                }
            }

            Assert.Fail(modal.name + ": no " + name + " text");
            return null;
        }

        /// <summary>The confirmation's C1: the full-screen ink image named "Scrim" (the kit modal's own name for it).</summary>
        private static float ScrimAlpha(UiModal modal)
        {
            foreach (UnityEngine.UI.Image image in modal.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.name == "Scrim")
                {
                    return image.color.a;
                }
            }

            Assert.Fail(modal.name + ": no Scrim");
            return 0f;
        }

        private static UiModal OpenError(UiCurtain curtain)
        {
            UiModal modal = curtain.GetComponentInChildren<UiModal>();
            Assert.IsNotNull(modal);
            Assert.AreEqual(UiCurtain.LoadErrorName, modal.name);
            Assert.IsTrue(modal.IsOpen);
            return modal;
        }

        private static void AssertIgnoredCancel(UiModal modal)
        {
            Assert.IsNotNull(EventSystem.current);
            GameObject focused = EventSystem.current.currentSelectedGameObject;
            Assert.AreSame((modal.Secondary != null ? modal.Secondary : modal.Primary).gameObject, focused);
            Assert.IsFalse(ExecuteEvents.Execute(focused, new BaseEventData(EventSystem.current), ExecuteEvents.cancelHandler), "Loading error does not wire a Cancel action.");
            Assert.IsTrue(modal.IsOpen, "Cancel event keeps the load error open.");
        }

        private static void AssertFits(TMP_Text text, string language)
        {
            text.ForceMeshUpdate();
            Debug.Log("[S09-WATCHDOG] copy language=" + language + " name=" + text.name + " truncated=" + text.isTextTruncated + " label=" + text.rectTransform.rect.size + " mesh=" + text.textBounds.size);
            Assert.IsFalse(text.isTextTruncated, language + " " + text.name + " must not truncate at TextScale 1.25.");
            Assert.LessOrEqual(text.textBounds.size.x, text.rectTransform.rect.width + 1f, text.name + " horizontal glyph containment.");
            Assert.LessOrEqual(text.textBounds.size.y, text.rectTransform.rect.height + 1f, text.name + " vertical glyph containment.");
        }

        private static string ProfileHash()
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(TestProfileIsolation.TestProfilePath))).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
