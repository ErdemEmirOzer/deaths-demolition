using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-101b (S-02 measure 12.10: a 157 ms / 3.4 MB frame ~22 frames into Fighting, the AmbientVfxSystem and LanternFlames
    /// settle scans). The two settle scans announce their frame through <see cref="VfxSettleGate"/>; the loading flow keeps the
    /// curtain shut while it holds (PrototypeUiFlow.WaitForVfxSettle, capped). Here: a Run load holds the gate up to the settle
    /// frame, both settle scans have run by the time it lets go, and the cap covers a 30-frame settle at 30 fps.
    /// </summary>
    [TestFixture]
    public sealed class Wp101bSettleCurtainTests
    {
        [Test]
        public void Gate_LatestFrameWins_AndReleasesOnItsOwn()
        {
            typeof(VfxSettleGate).GetMethod("ResetStatics", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
            Assert.IsFalse(VfxSettleGate.Holding, "nothing pending");
            int now = Time.frameCount;
            VfxSettleGate.HoldUntil(now + 30);
            VfxSettleGate.HoldUntil(now + 10);   // an earlier holder never shortens the wait
            Assert.AreEqual(now + 30, VfxSettleGate.UntilFrame);
            Assert.IsTrue(VfxSettleGate.Holding);
            VfxSettleGate.HoldUntil(now - 1);
            Assert.AreEqual(now + 30, VfxSettleGate.UntilFrame);
            Assert.That(DD.Prototype.UI.PrototypeUiFlow.VfxSettleCapSeconds, Is.GreaterThanOrEqualTo(31f / 30f), "the cap covers a 30-frame settle at 30 fps");
        }

        // S-02 12.10: the proof is on the player's way in - the flow with the curtain's real timings into the Run: every frame the
        // settle gate holds, the doors are shut; on the first open frame the gate has let go.
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator UiFlow_RunGirisi_SettleTaramalariPerdeKapaliyken()
        {
            bool fast = PrototypeUiFlow.FastTransitions;
            string settings = Path.Combine(Path.GetTempPath(), "dd-wp101b-ui-" + System.Guid.NewGuid().ToString("N") + ".json");
            string timing = Path.Combine(Path.GetTempPath(), "dd-wp101b-timing-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(settings);
            UiLoadTiming.SetPathOverride(timing);
            try
            {
                PrototypeUiFlow.FastTransitions = true;
                yield return BootTanMainMenuye();
                PrototypeUiFlow.FastTransitions = false;
                PrototypeUiFlow.StartRun();
                bool heldCovered = false, heldOpen = false;
                float deadline = Time.realtimeSinceStartup + 90f;
                while ((PrototypeUiFlow.IsLoading || UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
                {
                    if (SceneManager.GetActiveScene().name == PrototypeScenes.Run && VfxSettleGate.Holding)
                    {
                        if (UiCurtain.IsCovering) heldCovered = true; else heldOpen = true;
                    }

                    yield return null;
                }

                Assert.AreEqual(PrototypeScenes.Run, SceneManager.GetActiveScene().name, "Run'a girildi");
                Assert.IsFalse(UiCurtain.IsCovering, "perde açıldı (90 s içinde)");
                Assert.IsTrue(heldCovered, "settle taramaları perde kapalıyken bekletildi");
                Assert.IsFalse(heldOpen || VfxSettleGate.Holding, "perde açıkken bekleyen settle taraması yok");
                Assert.Less(PrototypeUiFlow.LastVfxSettleWaitSeconds, PrototypeUiFlow.VfxSettleCapSeconds, "tavana çarpmadan bitti");
                Debug.Log("[DD-TEST] wp101b.settle-curtain wait=" + PrototypeUiFlow.LastVfxSettleWaitSeconds.ToString("F2") + " s");
            }
            finally
            {
                PrototypeUiFlow.FastTransitions = fast;
                RunFlowClock.ClearHolds();
                UiSettings.SetPathOverride(null);
                UiLoadTiming.SetPathOverride(null);
                foreach (string path in new[] { settings, timing }) if (File.Exists(path)) File.Delete(path);
            }
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator RunLoad_HoldsUntilBothSettleScansRan()
        {
            LogAssert.ignoreFailingMessages = true;
            int previousScene = SceneManager.GetActiveScene().handle;
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            float limit = Time.realtimeSinceStartup + 120f;
            while (!(SceneManager.GetActiveScene().handle != previousScene && SceneManager.GetActiveScene().name == PrototypeScenes.Run && RunDirector.Instance != null))
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "Run sahnesi gelmedi");
                yield return null;
            }

            int activated = Time.frameCount;
            for (int i = 0; i < 3; i++) yield return null;   // Start / the first director Update have scheduled the settle scans
            Assert.IsTrue(VfxSettleGate.Holding, "the settle scans hold the reveal");
            int until = VfxSettleGate.UntilFrame;
            Assert.That(until - activated, Is.InRange(20, 45), "the settle frame sits ~30 frames after activation");
            int lanternScans = LanternFlames.Current != null ? LanternFlames.Current.Scans : -1;
            while (VfxSettleGate.Holding)
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "the gate never let go");
                yield return null;
            }

            Assert.Greater(Time.frameCount, until, "released after the settle frame");
            if (LanternFlames.Current != null && lanternScans >= 0)
                Assert.Greater(LanternFlames.Current.Scans, lanternScans, "the lantern settle scan ran inside the hold");
            var settle = typeof(AmbientVfxSystem).GetField("_settleFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (AmbientVfxSystem.Active != null) Assert.AreEqual(-1, (int)settle.GetValue(AmbientVfxSystem.Active), "the ambient settle scan ran inside the hold");
        }
    }
}
