using System.Collections;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 hotfix (release f4453aa, user report): Pause → END RUN froze the screen — PauseScreen.ConfirmQuit cleared
    /// `_paused` without releasing the frozen-frame hold, so OnDestroy skipped it too and the layer-90 overlay stayed on
    /// top of the main menu. The real flow: pause over a live run (blur showing), END RUN, main menu → the frozen frame is
    /// gone, released by the pause screen itself (not by the scene-change safety net).
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiFrozenFrameFlowTests
    {
        private float _timeScaleWas;
        private RenderTexture _rt;

        [SetUp]
        public void SetUp()
        {
            _timeScaleWas = Time.timeScale;
            RunFlowClock.ClearHolds();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UiFrozenFrame.ReleaseAll();
            UiFrozenFrame.CameraOverride = null;
            if (_rt != null) { Object.Destroy(_rt); _rt = null; }
            RunFlowClock.ClearHolds();
            Time.timeScale = _timeScaleWas;
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator Pause_EndRun_AnaMenu_DonmusKareKalmaz()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            // batchmode: the Run camera renders to the screen, which does not render here — point it at a texture.
            Camera runCamera = Camera.main;
            Assert.IsNotNull(runCamera, "Run kamerası yok");
            _rt = new RenderTexture(640, 360, 24);
            runCamera.targetTexture = _rt;
            UiFrozenFrame.CameraOverride = runCamera;

            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(pause, "Run sahnesinde PauseScreen yok");
            int captures = UiFrozenFrame.CaptureCount;
            pause.SetPaused(true);
            Assert.IsTrue(pause.IsPaused, "pause açılmadı (CanOpen)");
            Assert.IsTrue(UiFrozenFrame.IsHeld, "pause donmuş kareyi tutmadı");

            // WP-098B (pm-1 #4): this is the Run camera's FIRST render (batch: it had never drawn before the RT above), and
            // on a cold shader cache that frame builds the world's pipeline states - seconds, in one frame (pm-1: 7.9 s for
            // a ~2.2 s boot-to-fight; the Metal first-use lines sit right before the capture log). A 3 s wall clock across
            // it measured the cache, not the pause: the frame after the capture resumed this wait past 3 s, before the
            // layer's LateUpdate had shown the frame. The contract is "one capture per hold, shown from the next frame on":
            // the capture gets a generous clock, the showing is counted in frames, and the hold must still stand.
            yield return Bekle(() => UiFrozenFrame.CaptureCount > captures, 30f, "pause altında yakalama olmadı");
            for (int i = 0; i < 3 && !UiFrozenFrame.IsShowing; i++)
            {
                yield return null;
            }

            Assert.IsTrue(UiFrozenFrame.IsHeld, "yakalamadan sonra tutma bırakıldı (" + UiFrozenFrame.DescribeOwners() + ")");
            Assert.IsTrue(UiFrozenFrame.IsShowing, "pause altında donmuş kare yakalamadan sonraki karede görünmedi");

            int forced = UiFrozenFrame.ForcedReleases;
            MethodInfo confirmQuit = typeof(PauseScreen).GetMethod("ConfirmQuit", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(confirmQuit, "PauseScreen.ConfirmQuit (END RUN) bulunamadı");
            confirmQuit.Invoke(pause, null);

            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            yield return null;
            Assert.IsFalse(UiFrozenFrame.IsHeld, "END RUN sonrası donmuş kare tutuluyor");
            Assert.IsFalse(UiFrozenFrame.IsShowing, "donmuş kare ana menüyü örtüyor");
            Assert.AreEqual(forced, UiFrozenFrame.ForcedReleases, "bırakma pause ekranından gelmeli, güvenlik ağından değil");
        }
    }
}
