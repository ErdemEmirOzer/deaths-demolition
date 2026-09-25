using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 R1-D · Results v3 (direction E26) after a real death: the hero tiles roll to the run's numbers, the
    /// choreography ends within its 2.2 s budget (or at the first skip), MAIN MENU takes the focus only at the end, and
    /// every block and word sits inside the screen at 1080 and at the Mac setting (UI 1.2 × text 1.25).
    /// </summary>
    public sealed class ResultsV3Tests
    {
        private static readonly Vector3[] s_a = new Vector3[4];
        private static readonly Vector3[] s_b = new Vector3[4];

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Results_KahramanKaroSayaclari_Koreografi_2_2s_SonraAnaMenuOdak()
        {
            yield return DieAndOpenResults(1f, 1f);
            ResultsScreen screen = Object.FindObjectOfType<ResultsScreen>();
            Assert.IsNotNull(screen);
            yield return UiCurtain.WaitRevealed();
            float t0 = Time.realtimeSinceStartup;
            Assert.IsTrue(screen.Playing, "the choreography plays once the curtain has opened");
            Assert.AreNotEqual("MainMenu", EventSystem.current.currentSelectedGameObject != null ? EventSystem.current.currentSelectedGameObject.name : string.Empty,
                "MAIN MENU is not focused while the beats play (the first press skips, it does not leave)");
            yield return Bekle(() => !screen.Playing, 5f, "the choreography ends");
            float seconds = Time.realtimeSinceStartup - t0;
            Debug.Log("[DD-TEST] r1d.results choreography=" + seconds.ToString("F2") + "s");
            Assert.LessOrEqual(seconds, 2.5f, "E26: ≤ 2.2 s (frame slack)");
            Assert.AreEqual(screen.Model.HeroRound, screen.Tile(0).Target);
            Assert.AreEqual(screen.Model.HeroKills, screen.Tile(1).Target);
            Assert.AreEqual(screen.Model.HeroGold, screen.Tile(2).Target);
            Assert.AreEqual(ResultsViewModel.FormatDuration(screen.Model.HeroSeconds), screen.Tile(3).ValueText.text);
            Assert.AreEqual("MainMenu", EventSystem.current.currentSelectedGameObject.name, "MAIN MENU takes the focus at the end");
            AssertFits(screen, "1080");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Results_Mac_Atla_TumBloklarEkranda()
        {
            // A long unknown killer id: the H1 must yield (Display H2) before it reaches the NEW BEST ribbon (AD R1-D SHOULD-5).
            yield return DieAndOpenResults(1.2f, 1.25f, "r1d-results-a-long-unknown-killing-blow-id");
            ResultsScreen screen = Object.FindObjectOfType<ResultsScreen>();
            yield return UiCurtain.WaitRevealed();
            screen.Skip();
            Assert.IsFalse(screen.Playing, "a skip ends the choreography");
            Assert.AreEqual(screen.Model.HeroKills, screen.Tile(1).Target, "the end state is the same after a skip");
            yield return null;
            AssertFits(screen, "mac");
            AssertTitleClearsRibbon(screen, "mac");
        }

        private static IEnumerator DieAndOpenResults(float uiScale, float textScale, string killer = "r1d-results")
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
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = false;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, killer);
            yield return SahneyiBekle(PrototypeScenes.Results);
            yield return null;
        }

        /// <summary>The H1's printed words never reach the NEW BEST ribbon (when the run set one).</summary>
        private static void AssertTitleClearsRibbon(ResultsScreen screen, string when)
        {
            TMP_Text title = null;
            RectTransform ribbon = null;
            foreach (TMP_Text text in screen.GetComponentsInChildren<TMP_Text>(false))
            {
                if (text.name == "Title")
                {
                    title = text;
                }

                if (text.transform.parent != null && text.transform.parent.name == "NewBest")
                {
                    ribbon = (RectTransform)text.transform.parent;
                }
            }

            if (title == null || ribbon == null)
            {
                return;   // no new best on this run: nothing to clear
            }

            title.ForceMeshUpdate();
            ribbon.GetWorldCorners(s_b);
            float wordsRight = title.transform.TransformPoint(new Vector3(title.textBounds.max.x, 0f, 0f)).x;
            Assert.Less(wordsRight, s_b[0].x, when + ": the title '" + title.text + "' stops before the NEW BEST ribbon");
        }

        /// <summary>Every visible word inside the screen and whole; the three blocks inside the page.</summary>
        private static void AssertFits(ResultsScreen screen, string when)
        {
            RectTransform root = (RectTransform)screen.GetComponentInChildren<Canvas>().transform;
            root.GetWorldCorners(s_b);
            foreach (TMP_Text text in screen.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                text.ForceMeshUpdate();
                text.rectTransform.GetWorldCorners(s_a);
                Assert.IsTrue(s_a[0].x >= s_b[0].x - 1f && s_a[0].y >= s_b[0].y - 1f && s_a[2].x <= s_b[2].x + 1f && s_a[2].y <= s_b[2].y + 1f,
                    when + ": '" + text.text + "' inside the screen");
                Assert.IsFalse(text.isTextTruncated, when + ": '" + text.text + "' is whole");
            }
        }
    }
}
