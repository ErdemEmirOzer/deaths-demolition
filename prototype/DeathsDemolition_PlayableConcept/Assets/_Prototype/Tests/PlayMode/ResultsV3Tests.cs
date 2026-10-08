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
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 R1-D · Results v3 (direction E26) after a real death: the hero tiles roll to the run's numbers, the
    /// choreography ends within its 2.2 s budget (or at the first skip), MAIN MENU takes the focus only at the end, and
    /// every block and word sits inside the screen at 1080 and at the Mac setting (UI 1.2 × text 1.25).
    /// WP-098B: the window is the brush kit's (Window scrim, C5 title, C21 counters, C8 under NEW BEST, C11 MAIN MENU).
    /// </summary>
    public sealed class ResultsV3Tests
    {
        private static readonly Vector3[] s_a = new Vector3[4];
        private static readonly Vector3[] s_b = new Vector3[4];
        private static readonly string[] s_tileKeys = { "results.tile.round", "results.tile.kills", "results.tile.gold", "results.tile.time" };

        private bool _reduced;
        private string _language;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            _language = UiStrings.Language;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            UiMotion.ReducedMotion = _reduced;   // the mirror only - the saved setting is never written here
            UiStrings.Language = _language;
        }

        // ------------------------------------------------------------------ WP-098B count-up

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Results_Sayaclar_SifirdanSayar_1_5sIcindeSonDeger_AdlarKorunur_EN()
        {
            yield return CountUp(UiStrings.English);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Results_Sayaclar_SifirdanSayar_1_5sIcindeSonDeger_AdlarKorunur_TR()
        {
            yield return CountUp(UiStrings.Turkish);
        }

        private static IEnumerator CountUp(string language)
        {
            yield return DieAndOpenResults(1f, 1f, "wp098b-results-" + language, language, false);
            ResultsScreen screen = Object.FindObjectOfType<ResultsScreen>();
            Assert.IsNotNull(screen);
            yield return UiCurtain.WaitRevealed();
            float t0 = Time.realtimeSinceStartup;
            Assert.IsTrue(screen.Playing, language + ": the count plays");
            Assert.AreEqual("0:00", screen.Tile(3).ValueText.text, language + ": TIME rolls last, from 0:00");

            string time = ResultsViewModel.FormatDuration(screen.Model.HeroSeconds);
            yield return Bekle(() => screen.Tile(3).ValueText.text == time, 5f, language + ": the count lands");
            float counted = Time.realtimeSinceStartup - t0;
            Debug.Log("[DD-TEST] wp098b.results count=" + counted.ToString("F2") + "s lang=" + language);
            Assert.LessOrEqual(ResultsScreen.Look.CountPhaseSeconds, 1.5f, "the authored count phase");
            Assert.LessOrEqual(counted, ResultsScreen.Look.CountPhaseSeconds + 0.6f, language + ": all four numbers land in ≤ ~1.5 s (frame slack)");

            yield return Bekle(() => !screen.Playing, 5f, language + ": the choreography ends");
            AssertTilesFinal(screen, language);
            for (int i = 0; i < 4; i++)
            {
                // WP-098B: each number is the kit's C21 (BigCounter) - its caption is the counter root's own "Caption".
                Transform captionNode = screen.Tile(i).transform.Find("Caption");
                Assert.IsNotNull(captionNode, language + ": tile " + i + " is a kit BigCounter (caption + number + line)");
                TMP_Text caption = captionNode.GetComponent<TMP_Text>();
                Assert.AreEqual(UiStrings.Get(s_tileKeys[i]), caption.text, language + ": the caption is the pack's word");
                Assert.AreEqual(1f, screen.Tile(i).transform.localScale.x, 1e-4f, language + ": the counter rests");
                Assert.AreEqual(1f, screen.Tile(i).ValueText.rectTransform.localScale.x, 1e-4f, language + ": the punch is back at rest");
            }

            // The names the PM tests find the screen by (EKIP-KURALLARI), and the ground: the plate, no v2 dress.
            foreach (string name in new[] { "Footer", "Report", "Rewards", "Tiles", "MainMenu", "Backdrop", "Reason", ResultsScreen.PaneName })
            {
                Assert.IsNotNull(FindNamed(screen, name), language + ": '" + name + "' is kept");
            }

            yield return new WaitForSecondsRealtime(0.5f);   // NEW BEST's C8 inks in over Reselect (400 ms) after the last beat
            AssertBrushWindow(screen, language);

            foreach (string name in new[] { "Petal", "SkyVeil", "GroundVeil", "FootRule" })
            {
                Assert.IsNull(FindNamed(screen, name), language + ": no v2 backdrop '" + name + "'");
            }

            Transform ornament = FindNamed(screen, "HeaderOrnament");
            Assert.IsTrue(ornament == null || !ornament.gameObject.activeInHierarchy, language + ": no AP-012 lozenge on the header rule");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Results_SayimAtla_DegerlerAnindaSon_TokBirKez()
        {
            yield return DieAndOpenResults(1f, 1f, "wp098b-results-skip", null, false);
            ResultsScreen screen = Object.FindObjectOfType<ResultsScreen>();
            yield return UiCurtain.WaitRevealed();
            yield return null;   // the count has started
            Assert.IsTrue(screen.Playing);
            int settled = 0;
            System.Action<UiEvent, string> onEvent = (e, d) => { if (e == UiEvent.CounterSettled) settled++; };
            UiEvents.Raised += onEvent;
            try
            {
                screen.Skip();
            }
            finally
            {
                UiEvents.Raised -= onEvent;
            }

            Assert.IsFalse(screen.Playing, "a skip ends the choreography");
            AssertTilesFinal(screen, "skip");   // the same frame: no roll left behind
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(1f, screen.Tile(i).ValueText.rectTransform.localScale.x, 1e-4f, "skip lands the punch at rest");
            }

            if (screen.NewBestMark != null)
            {
                Assert.IsTrue(screen.NewBestMark.Drawn, "a skip lands NEW BEST's C8 drawn (a cut, never an ink-in after the skip)");
            }

            Assert.LessOrEqual(settled, 1, "the cues collapse to the last settle (the roll voice stops once)");
            yield return null;
            AssertTilesFinal(screen, "skip+1");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Results_AzHareket_SayimSuresiSifir_SonDegerAninda()
        {
            yield return DieAndOpenResults(1f, 1f, "wp098b-results-rm", null, true);
            ResultsScreen screen = Object.FindObjectOfType<ResultsScreen>();
            yield return UiCurtain.WaitRevealed();
            yield return null;
            yield return null;
            Assert.IsFalse(screen.Playing, "Reduced Motion: the end state at once (count time 0)");
            AssertTilesFinal(screen, "rm");
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(1f, screen.Tile(i).ValueText.rectTransform.localScale.x, 1e-4f, "rm: no punch");
            }

            Assert.AreEqual(UiBrushTokens.ScrimWindow, screen.Pane.Scrim.color.a * screen.Pane.Scrim.canvasRenderer.GetAlpha(), 0.01f,
                "rm: the scrim is at its level at once");
            if (screen.NewBestMark != null)
            {
                Assert.IsTrue(screen.NewBestMark.Drawn, "rm: NEW BEST's C8 drawn at once");
            }
        }

        /// <summary>
        /// WP-098B (brush kit KILAVUZ §3 "Sonuç"): the window is the kit's - the Window scrim at its linear α (no hand-set
        /// 0.90), every word on ≥ 0.945 of ink, no card frame on a block, no vermilion heading tick; the traces within the
        /// window budget (title brush 1, C8 ≤ 1 - NEW BEST's, drawn under its words), MAIN MENU a kit C11 (≥ 48 high).
        /// </summary>
        private static void AssertBrushWindow(ResultsScreen screen, string when)
        {
            UiBrushPane pane = screen.Pane;
            Assert.IsNotNull(pane, when + ": the kit's pane carries the window");
            Assert.IsTrue(pane.IsOpen, when + ": the window is open");
            Assert.AreEqual(UiBrushScrim.Window, pane.ScrimLevel, when + ": KILAVUZ §3 - the Window scrim");
            Assert.AreEqual(ResultsScreen.Look.Scrim, pane.ScrimLevel);
            Image scrim = pane.Scrim;
            Assert.IsNotNull(scrim, when + ": C1");
            Assert.AreEqual(UiBrushTokens.ScrimWindow, scrim.color.a * scrim.canvasRenderer.GetAlpha(), 0.01f, when + ": the scrim at the kit's linear 0.95");
            Assert.GreaterOrEqual(pane.TextGroundAlpha, UiBrushTokens.TextGroundMin, when + ": text ground ≥ 0.945");
            Assert.GreaterOrEqual(ResultsScreen.Look.TextGroundAlpha, UiBrushTokens.TextGroundMin);

            foreach (Transform t in screen.GetComponentsInChildren<Transform>(false))
            {
                Assert.IsFalse(t.name == "Heading" && t.Find("Tick") != null, when + ": no vermilion heading tick in a brush window");
                Assert.AreNotEqual("Frame", t.name, when + ": no card frame on a block");
            }

            int titles = 0, selections = 0, others = 0;
            foreach (UiBrushMark mark in screen.GetComponentsInChildren<UiBrushMark>(false))
            {
                if (mark.Kind == UiBrushMarkKind.TitleStroke) titles++;
                else if (mark.Kind == UiBrushMarkKind.Selection) selections++;
                else others++;
            }

            Assert.AreEqual(UiBrushTokens.MaxTitle, titles, when + ": one title brush");
            Assert.LessOrEqual(selections, 1, when + ": one C8 at most (NEW BEST)");
            Assert.AreEqual(0, others, when + ": no divider / edge on Results");
            if (screen.NewBestMark != null)
            {
                Assert.IsTrue(screen.NewBestMark.Drawn, when + ": NEW BEST's C8 has landed under its words");
                Transform words = FindNamed(screen, "NewBest").Find("Text");
                words.GetComponent<RectTransform>().GetWorldCorners(s_a);
                screen.NewBestMark.Rect.GetWorldCorners(s_b);
                Assert.Less(s_b[1].y, s_a[0].y + 0.5f, when + ": the C8 lies under the NEW BEST words");
            }

            UiButton mainMenu = FindNamed(screen, "MainMenu").GetComponent<UiButton>();
            Assert.IsFalse(mainMenu.DrawsFocusRing, when + ": MAIN MENU is a kit C11 (focus = the brackets)");
            Assert.GreaterOrEqual(((RectTransform)mainMenu.transform).rect.height, UiTokens.TargetPreferred, when + ": MAIN MENU's hit ≥ 48");
        }

        private static void AssertTilesFinal(ResultsScreen screen, string when)
        {
            int[] values = { screen.Model.HeroRound, screen.Model.HeroKills, screen.Model.HeroGold };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(values[i], screen.Tile(i).Target, when + ": tile " + i + " target");
                Assert.AreEqual(values[i].ToString(), screen.Tile(i).ValueText.text, when + ": tile " + i + " reads its final value");
            }

            Assert.AreEqual(ResultsViewModel.FormatDuration(screen.Model.HeroSeconds), screen.Tile(3).ValueText.text, when + ": time");
        }

        private static Transform FindNamed(ResultsScreen screen, string name)
        {
            foreach (Transform t in screen.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
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
            // A long unknown killer id: the reason line wraps before it reaches NEW BEST (WP-098B; was AD R1-D SHOULD-5's H1 yield).
            yield return DieAndOpenResults(1.2f, 1.25f, "r1d-results-a-long-unknown-killing-blow-id");
            ResultsScreen screen = Object.FindObjectOfType<ResultsScreen>();
            yield return UiCurtain.WaitRevealed();
            screen.Skip();
            Assert.IsFalse(screen.Playing, "a skip ends the choreography");
            Assert.AreEqual(screen.Model.HeroKills, screen.Tile(1).Target, "the end state is the same after a skip");
            yield return null;
            AssertFits(screen, "mac");
            AssertReasonClearsNewBest(screen, "mac");
            AssertBlocksClearFooter(screen, "mac");
        }

        /// <summary>AD 12c MUST-4: the blocks grow into the band over the footer, never into it (MAIN MENU keeps its air).</summary>
        private static void AssertBlocksClearFooter(ResultsScreen screen, string when)
        {
            RectTransform footer = null;
            var blocks = new System.Collections.Generic.List<RectTransform>();
            foreach (RectTransform rect in screen.GetComponentsInChildren<RectTransform>(false))
            {
                if (rect.name == "Footer")
                {
                    footer = rect;
                }
                else if (rect.name == "Report" || rect.name == "Rewards")
                {
                    blocks.Add(rect);
                }
            }

            Assert.IsNotNull(footer, when + ": footer");
            Assert.AreEqual(2, blocks.Count, when + ": the two blocks");
            footer.GetWorldCorners(s_a);
            float footerTop = s_a[1].y;
            foreach (RectTransform block in blocks)
            {
                block.GetWorldCorners(s_b);
                Assert.Greater(s_b[0].y, footerTop, when + ": " + block.name + " ends above the footer");
            }
        }

        private static IEnumerator DieAndOpenResults(float uiScale, float textScale, string killer = "r1d-results",
            string language = null, bool? reducedMotion = null)
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            UiSettings.UiScale = uiScale;
            UiSettings.TextScale = textScale;
            // WP-098B: the language and the motion mirror only (UiStrings / UiMotion), set after boot has loaded the saved
            // settings - nothing is written to the player's settings file.
            if (language != null)
            {
                UiStrings.Language = language;
            }

            if (reducedMotion.HasValue)
            {
                UiMotion.ReducedMotion = reducedMotion.Value;
            }
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

        /// <summary>
        /// WP-098B (AP-028 §3.9): the killer / loadout line's printed words never reach NEW BEST at its right (when the run set
        /// one) - a long unknown killer id wraps instead (the title is the ending alone, so it no longer meets NEW BEST).
        /// </summary>
        private static void AssertReasonClearsNewBest(ResultsScreen screen, string when)
        {
            TMP_Text reason = null;
            RectTransform newBest = null;
            foreach (TMP_Text text in screen.GetComponentsInChildren<TMP_Text>(false))
            {
                if (text.name == "Reason")
                {
                    reason = text;
                }

                if (text.transform.parent != null && text.transform.parent.name == "NewBest")
                {
                    newBest = (RectTransform)text.transform.parent;
                }
            }

            Assert.IsNotNull(reason, when + ": the reason line");
            if (newBest == null)
            {
                return;   // no new best on this run: nothing to clear
            }

            reason.ForceMeshUpdate();
            newBest.GetWorldCorners(s_b);
            float wordsRight = reason.transform.TransformPoint(new Vector3(reason.textBounds.max.x, 0f, 0f)).x;
            Assert.Less(wordsRight, s_b[0].x, when + ": the reason '" + reason.text + "' stops before NEW BEST");
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
