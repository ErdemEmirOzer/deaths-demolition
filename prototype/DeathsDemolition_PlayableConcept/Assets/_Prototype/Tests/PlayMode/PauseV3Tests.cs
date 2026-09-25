using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 R1-C · Pause v3 (direction E25, AD R1-A SHOULD-3): the card is the kit's XL modal, its stack is measured so
    /// the slack sits in one band above RESUME / MAIN MENU, and the read-only "run so far" strip quotes the same producers
    /// as the HUD (round, kills, gold). At the Mac setting (UI 1.2 × text 1.25) the card grows to its content and stays
    /// on screen.
    /// </summary>
    public sealed class PauseV3Tests
    {
        private static readonly Vector3[] s_a = new Vector3[4];
        private static readonly Vector3[] s_b = new Vector3[4];

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            if (pause != null && pause.IsPaused)
            {
                pause.SetPaused(false);
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_KosuSeridi_TekBoslukBandi_1080()
        {
            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            RunDirector run = RunDirector.Instance;
            Assert.AreEqual(run.Round.ToString(), pause.RunValue(0), "ROUND = the director's round");
            Assert.AreEqual(RunEvents.Current.RunKills.ToString(), pause.RunValue(2), "KILLS = the HUD's producer");
            Assert.AreEqual(run.Wallet.Balance.ToString(), pause.RunValue(3), "GOLD = the wallet");
            Assert.IsFalse(string.IsNullOrEmpty(pause.RunBuild), "the build line is written");
            Vector2 xl = UiModal.SizeOf(UiModalSize.XL);
            Assert.AreEqual(xl.x, pause.PanelRect.rect.width, 0.5f, "XL width");
            Assert.AreEqual(xl.y, pause.PanelRect.rect.height, 0.5f, "1080: the stack fits the XL card");
            AssertStack(pause, "1080");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_Mac_KartIcerigeBuyur_EkrandaKalir()
        {
            yield return Open(1.2f, 1.25f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Debug.Log("[DD-TEST] r1c.pause mac panel=" + pause.PanelRect.rect.size);
            AssertStack(pause, "mac");
            RectTransform screen = (RectTransform)pause.PanelRect.GetComponentInParent<Canvas>().rootCanvas.transform;
            screen.GetWorldCorners(s_b);
            pause.PanelRect.GetWorldCorners(s_a);
            Assert.IsTrue(s_a[0].y >= s_b[0].y && s_a[1].y <= s_b[1].y, "mac: the card stays on screen");
        }

        private static IEnumerator Open(float uiScale, float textScale)
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            UiSettings.UiScale = uiScale;
            UiSettings.TextScale = textScale;
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return Bekle(() => RunEvents.Current.Phase == RunPhase.Fighting, 20f, "fighting");
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(pause);
            pause.SetPaused(true);
            yield return null;
            yield return null;
            Assert.IsTrue(pause.IsPaused);
        }

        /// <summary>Every block inside the card; the one slack band sits between the last caption and RESUME (≥ XL).</summary>
        private static void AssertStack(PauseScreen pause, string when)
        {
            pause.StackEnd.GetWorldCorners(s_a);
            pause.ResumeRect.GetWorldCorners(s_b);
            float scale = pause.PanelRect.lossyScale.y;
            float gap = (s_a[0].y - s_b[1].y) / scale;
            Assert.GreaterOrEqual(gap, (int)UiSpace.XL - 0.5f, when + ": the last caption clears RESUME by at least XL (" + gap + ")");
            pause.PanelRect.GetWorldCorners(s_b);
            foreach (TMPro.TMP_Text text in pause.PanelRect.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                text.rectTransform.GetWorldCorners(s_a);
                Assert.IsTrue(s_a[0].y >= s_b[0].y - 1f && s_a[1].y <= s_b[1].y + 1f, when + ": " + text.name + " inside the card");
                Assert.IsFalse(text.isTextTruncated, when + ": " + text.name + " is whole (" + text.text + ")");
            }
        }
    }
}
