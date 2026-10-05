using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-081 B4 · the R2 U2 instrument (r2-gates.json: overflows = 0): no visible text on the menu screens overflows its
    /// box or is cut, in EN and TR, at text 1.0 and 1.25, and at the Mac UI scale 1.2 with text 1.25 (the setting the
    /// player's Mac runs). Each combination rebuilds the screens (they read the settings when built): Selection, the main
    /// menu at entry and its six panes, the run HUD, the pause screen, the Results.
    ///
    /// A text counts once per combination, screen and object when it is active and non-empty, not under the curtain, and
    /// TMP reports it truncated or overflowing its rect. Logs [DD-TEST] wp081.overflow combo|screen|path|text and
    /// [DD-TEST] wp081.u2 …; r2-U2.json through R2Olcum. A temp ui-settings file and a temp load-timing file - the user's
    /// are never touched. Explicit + Diagnostics: a slow walk (six rebuilds of every screen); the acceptance chain runs it
    /// by name. UiTextScaleTests keeps its own 1.25 walk in CI.
    /// </summary>
    [Category("WP081")]
    [Category("Diagnostics")]
    public sealed class UiOverflowMatrixTests
    {
        private static readonly UiContext[] Panes =
        {
            UiContext.Profile, UiContext.Statistics, UiContext.Achievements, UiContext.Settings, UiContext.Controls, UiContext.Accessibility
        };

        private static readonly object[][] Matrix =
        {
            new object[] { UiStrings.English, 1f, 1f },
            new object[] { UiStrings.English, 1.25f, 1f },
            new object[] { UiStrings.Turkish, 1f, 1f },
            new object[] { UiStrings.Turkish, 1.25f, 1f },
            new object[] { UiStrings.English, 1.25f, 1.2f },
            new object[] { UiStrings.Turkish, 1.25f, 1.2f }
        };

        private string _settingsPath;
        private string _timingPath;
        private bool _fast;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            PrototypeUiFlow.FastTransitions = true;
            string id = System.Guid.NewGuid().ToString("N");
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-wp081-u2-settings-" + id + ".json");
            _timingPath = Path.Combine(Path.GetTempPath(), "dd-wp081-u2-timing-" + id + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            UiLoadTiming.SetPathOverride(_timingPath);
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            UiSettings.Language = UiStrings.English;
            PrototypeUiFlow.FastTransitions = _fast;
            UiSettings.SetPathOverride(null);
            UiLoadTiming.SetPathOverride(null);
            foreach (string path in new[] { _settingsPath, _timingPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [UnityTest]
        [Explicit("R2 U2 instrument: six rebuilds of every menu screen; the acceptance chain runs it by name")]
        [Timeout(900000)]
        public IEnumerator U2_Tasma_TrEn_Metin1ve125_MacUi12()
        {
            yield return BootTanMainMenuye();
            var found = new List<string>();
            int texts = 0;
            foreach (object[] combo in Matrix)
            {
                string language = (string)combo[0];
                float textScale = (float)combo[1];
                float uiScale = (float)combo[2];
                string name = language + ".t" + textScale.ToString("0.00") + ".u" + uiScale.ToString("0.0");
                UiSettings.Language = language;
                UiSettings.TextScale = textScale;
                UiSettings.UiScale = uiScale;

                PrototypeUiFlow.GoToSelection();
                yield return SahneyiBekle(PrototypeScenes.Selection);
                yield return Settle();
                texts += Overflows(name, "selection", found);

                PrototypeUiFlow.GoToMainMenu();
                yield return SahneyiBekle(PrototypeScenes.MainMenu);
                yield return Settle();
                texts += Overflows(name, "mainmenu", found);
                MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
                Assert.IsNotNull(menu, "the main menu");
                foreach (UiContext pane in Panes)
                {
                    menu.ShowPane(pane);
                    yield return new WaitForSecondsRealtime(0.6f);
                    texts += Overflows(name, "pane." + pane, found);
                }

                PrototypeUiFlow.GoToSelection();
                yield return SahneyiBekle(PrototypeScenes.Selection);
                PrototypeUiFlow.StartRun();
                yield return SahneyiBekle(PrototypeScenes.Run);
                yield return KosuHazirOlsun();
                yield return Settle();
                texts += Overflows(name, "hud", found);

                PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
                Assert.IsNotNull(pause, "the pause screen");
                pause.SetPaused(true);
                yield return new WaitForSecondsRealtime(0.6f);
                texts += Overflows(name, "pause", found);
                pause.SetPaused(false);
                yield return null;

                PrototypeUiFlow.GoToResults();
                yield return SahneyiBekle(PrototypeScenes.Results);
                yield return Settle();
                texts += Overflows(name, "results", found);
            }

            foreach (string line in found)
            {
                Debug.Log("[DD-TEST] wp081.overflow " + line);
            }

            Debug.Log("[DD-TEST] wp081.u2 combos=" + Matrix.Length + " texts=" + texts + " overflows=" + found.Count + " at=" + Screen.width + "x" + Screen.height);
            R2Olcum.Yaz("U2", "DD.Prototype.Tests.PlayMode.UiOverflowMatrixTests.U2_Tasma_TrEn_Metin1ve125_MacUi12",
                new Dictionary<string, double> { { "overflows", found.Count } }, texts);
            Assert.IsEmpty(found, "U2: no text overflows or is cut");
        }

        private static IEnumerator Settle()
        {
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            yield return new WaitForSecondsRealtime(1f);
        }

        /// <summary>Adds every cut or overflowing visible text on screen to <paramref name="found"/>; returns how many texts were checked.</summary>
        private static int Overflows(string combo, string screen, List<string> found)
        {
            int checkedTexts = 0;
            foreach (TMP_Text text in Object.FindObjectsOfType<TMP_Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text) || text.alpha <= 0.01f)
                {
                    continue;
                }

                if (UiCurtain.Instance != null && text.transform.IsChildOf(UiCurtain.Instance.transform))
                {
                    continue;
                }

                text.ForceMeshUpdate();
                checkedTexts++;
                if (text.isTextTruncated || text.isTextOverflowing)
                {
                    string line = combo + "|" + screen + "|" + PathOf(text.transform) + "|" + text.GetParsedText().Replace('\n', ' ').Replace('|', '/');
                    if (!found.Contains(line))
                    {
                        found.Add(line);
                    }
                }
            }

            Debug.Log("[DD-TEST] wp081.u2 screen=" + combo + "." + screen + " checked=" + checkedTexts + " active=" + SceneManager.GetActiveScene().name);
            return checkedTexts;
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && sb.Length < 120; p = p.parent)
            {
                sb.Insert(0, p.name + "/");
            }

            return sb.ToString();
        }
    }
}
