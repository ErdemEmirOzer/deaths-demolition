using System;
using System.Collections;
using System.IO;
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
            Assert.IsNull(modal.Secondary, "A held async load offers QUIT only; it cannot be cancelled.");
            Assert.AreEqual(UiStrings.Get("menu.quit"), modal.Primary.LabelText.text);
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
            Assert.IsNotNull(modal.Secondary);
            Assert.AreEqual(UiStrings.Get("loading.error.to_menu"), modal.Primary.LabelText.text);
            Assert.AreEqual(UiStrings.Get("menu.quit"), modal.Secondary.LabelText.text);
            Assert.AreSame(modal.Primary.gameObject, EventSystem.current.currentSelectedGameObject, "Safe primary is the initial focus.");
            AssertIgnoredCancel(modal);
            Assert.AreEqual(0, primaryCalls);
            TMP_Text body = modal.transform.Find("Card/Content/Body").GetComponent<TMP_Text>();
            Assert.AreEqual(UiStrings.Format("loading.error.body", "value", UiCurtain.SentenceFor(PrototypeScenes.Run), "reason", UiStrings.Get("loading.error.missing"), ""), body.text);
            foreach (TMP_Text text in modal.GetComponentsInChildren<TMP_Text>()) AssertFits(text, language);
            Assert.AreEqual(_profileHash, ProfileHash());
            Assert.IsTrue(ExecuteEvents.Execute(modal.Primary.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler));
            Assert.AreEqual(1, primaryCalls, "Actual primary Submit invokes its safe action once.");
            Assert.IsFalse(curtain.ErrorOpen, "Primary Submit closes the modal.");
            Assert.AreEqual(_profileHash, ProfileHash());
            curtain.HideError();
            curtain.Open(true);
            _manualError = false;
            Debug.Log("[S09-WATCHDOG] modal language=" + language + " textScale=1.25 primaryCalls=1 cancelIgnored=1 profile=" + _profileHash);
        }

        private static UiModal OpenError(UiCurtain curtain)
        {
            UiModal modal = curtain.GetComponentInChildren<UiModal>();
            Assert.IsNotNull(modal);
            Assert.AreEqual("LoadError", modal.name);
            Assert.IsTrue(modal.IsOpen);
            return modal;
        }

        private static void AssertIgnoredCancel(UiModal modal)
        {
            Assert.IsNotNull(EventSystem.current);
            GameObject focused = EventSystem.current.currentSelectedGameObject;
            Assert.AreSame(modal.Primary.gameObject, focused);
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
