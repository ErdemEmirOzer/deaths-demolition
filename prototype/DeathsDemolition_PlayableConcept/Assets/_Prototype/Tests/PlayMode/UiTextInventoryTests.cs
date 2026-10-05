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
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-081 B3 · the R2 U1 instrument ("less text, more icons"): the letters a player sees on a screen at rest. Every
    /// active TMP text with a visible letter counts - not under the curtain, not under a faded CanvasGroup, on screen -
    /// and only its drawn characters (a truncated tail is not seen), letters only (\p{L}: values, numbers and symbols are
    /// information, not wording). EN and TR at text 1.0, 1920×1080 reference; the user's settings file is never touched.
    ///
    /// Logs one line per text and a total per screen: [DD-TEST] wp081.text screen|path|letters|text and
    /// [DD-TEST] wp081.u1 screen=… letters=… texts=…. Every visible, interactable control needs a reader name - a
    /// UiIconButton's, or a word of its own or beside it in its row (scrollbars aside); the ones without are logged
    /// ([DD-TEST] wp081.unnamed).
    ///
    /// R2 U1 (r2-gates.json, tools/qa/r2-measure-contract.md): the gate screens are the main menu at entry and Selection
    /// in EN, against the R1 baseline below - lettersTotalReduction (both together) ≥ 0.40, lettersPerScreenReductionMin
    /// ≥ 0.25, unnamedSelectables (every measured EN screen) = 0; r2-U1.json under DD_R2_MEASURE_DIR. Explicit: the gate
    /// can only pass with menu v4 (WP-081 B4); the acceptance chain runs it by name.
    /// </summary>
    [Category("WP081")]
    [Category("Diagnostics")]
    public sealed class UiTextInventoryTests
    {
        /// <summary>
        /// The R1 baseline: EN letters at entry, measured by this test on R1's menus (WP-081 B3, guarded run b3r2 on
        /// dda50c04 - the menus of main 133dd007; batch PlayMode at 640×480, the canvas at reference 1920×1080). The main
        /// menu opens on its Profile pane. TR at R1: 676 + 753.
        /// </summary>
        private const int R1MainMenuLetters = 675;
        private const int R1SelectionLetters = 765;
        private const string R1BaselineCommit = "dda50c04";

        private static int s_unnamed;
        private static readonly UiContext[] Panes =
        {
            UiContext.Profile, UiContext.Statistics, UiContext.Achievements, UiContext.Settings, UiContext.Controls, UiContext.Accessibility
        };

        private string _settingsPath;
        private bool _fast;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            PrototypeUiFlow.FastTransitions = true;
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-wp081-text-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            UiSettings.TextScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.TextScale = 1f;
            UiSettings.Language = UiStrings.English;
            PrototypeUiFlow.FastTransitions = _fast;
            UiSettings.SetPathOverride(null);
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }

        [UnityTest]
        [Explicit("R2 U1 instrument: passes with menu v4 (B4); the acceptance chain runs it by name")]
        [Timeout(300000)]
        public IEnumerator U1_GorunurHarf_AnaMenuPanellerVeSecim_EnTr()
        {
            yield return BootTanMainMenuye();
            var totals = new Dictionary<string, int>();
            int unnamedEn = 0;
            int texts = 0;
            foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
            {
                UiSettings.Language = language;
                if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
                {
                    PrototypeUiFlow.GoToMainMenu();   // a screen reads the language when it is built
                    yield return SahneyiBekle(PrototypeScenes.MainMenu);
                }

                yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
                yield return new WaitForSecondsRealtime(1.5f);   // the menu's entrance settles
                totals[language + ".mainmenu"] = Measure(language + ".mainmenu", ref texts);
                unnamedEn += language == UiStrings.English ? s_unnamed : 0;

                MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
                Assert.IsNotNull(menu, "the main menu");
                foreach (UiContext pane in Panes)
                {
                    menu.ShowPane(pane);
                    yield return new WaitForSecondsRealtime(0.6f);
                    totals[language + ".pane." + pane] = Measure(language + ".pane." + pane, ref texts);
                    unnamedEn += language == UiStrings.English ? s_unnamed : 0;
                }

                PrototypeUiFlow.GoToSelection();
                yield return SahneyiBekle(PrototypeScenes.Selection);
                yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
                yield return new WaitForSecondsRealtime(1.5f);
                totals[language + ".selection"] = Measure(language + ".selection", ref texts);
                unnamedEn += language == UiStrings.English ? s_unnamed : 0;
                Debug.Log("[DD-TEST] wp081.u1 gate " + language + " mainmenu+selection=" + (totals[language + ".mainmenu"] + totals[language + ".selection"]));
            }

            foreach (KeyValuePair<string, int> total in totals)
            {
                Assert.Greater(total.Value, 0, total.Key + ": the instrument measured nothing");
            }

            int menuLetters = totals["en.mainmenu"];
            int selectionLetters = totals["en.selection"];
            Assert.Greater(R1MainMenuLetters, 0, "the R1 baseline is set (EN main menu " + menuLetters + ", Selection " + selectionLetters + " on this build)");
            double totalReduction = 1.0 - (double)(menuLetters + selectionLetters) / (R1MainMenuLetters + R1SelectionLetters);
            double perScreen = System.Math.Min(1.0 - (double)menuLetters / R1MainMenuLetters, 1.0 - (double)selectionLetters / R1SelectionLetters);
            Debug.Log("[DD-TEST] wp081.u1 values lettersTotalReduction=" + totalReduction.ToString("F3") + " lettersPerScreenReductionMin=" + perScreen.ToString("F3")
                      + " unnamedSelectables=" + unnamedEn + " (R1 " + R1MainMenuLetters + "+" + R1SelectionLetters + " @ " + R1BaselineCommit + ")");
            R2Olcum.Yaz("U1", "DD.Prototype.Tests.PlayMode.UiTextInventoryTests.U1_GorunurHarf_AnaMenuPanellerVeSecim_EnTr",
                new Dictionary<string, double>
                {
                    { "lettersTotalReduction", totalReduction },
                    { "lettersPerScreenReductionMin", perScreen },
                    { "unnamedSelectables", unnamedEn }
                },
                texts);
            Assert.GreaterOrEqual(totalReduction, 0.40, "U1: letters on the main menu + Selection, against R1");
            Assert.GreaterOrEqual(perScreen, 0.25, "U1: letters on each gate screen, against R1");
            Assert.AreEqual(0, unnamedEn, "U1: every visible control has a reader name");
        }

        /// <summary>Count and log the letters on screen now, and the controls without a reader name (<see cref="s_unnamed"/>).</summary>
        private static int Measure(string screen, ref int measuredTexts)
        {
            int total = 0;
            int texts = 0;
            var lines = new List<string>();
            foreach (TMP_Text text in Object.FindObjectsOfType<TMP_Text>())
            {
                int letters = VisibleLetters(text);
                if (letters == 0)
                {
                    continue;
                }

                total += letters;
                texts++;
                lines.Add(screen + "|" + PathOf(text.transform) + "|" + letters + "|" + text.GetParsedText().Replace('\n', ' ').Replace('|', '/'));
            }

            lines.Sort(System.StringComparer.Ordinal);
            foreach (string line in lines)
            {
                Debug.Log("[DD-TEST] wp081.text " + line);
            }

            s_unnamed = 0;
            foreach (Selectable control in Object.FindObjectsOfType<Selectable>())
            {
                if (control is Scrollbar || !control.isActiveAndEnabled || !control.IsInteractable() || !Visible(control.transform, (RectTransform)control.transform))
                {
                    continue;
                }

                if (!Named(control))
                {
                    s_unnamed++;
                    Debug.Log("[DD-TEST] wp081.unnamed " + screen + "|" + PathOf(control.transform));
                }
            }

            measuredTexts += texts;
            Debug.Log("[DD-TEST] wp081.u1 screen=" + screen + " letters=" + total + " texts=" + texts + " unnamed=" + s_unnamed + " at=" + Screen.width + "x" + Screen.height);
            return total;
        }

        /// <summary>A reader name: a UiIconButton's, a word inside the control, or a word beside it in its row.</summary>
        private static bool Named(Selectable control)
        {
            UiIconButton icon = control.GetComponent<UiIconButton>();
            if (icon != null)
            {
                return !string.IsNullOrEmpty(icon.AccessibleName);
            }

            foreach (TMP_Text text in control.GetComponentsInChildren<TMP_Text>())
            {
                if (VisibleLetters(text) > 0)
                {
                    return true;
                }
            }

            Transform row = control.transform.parent;
            if (row != null)
            {
                foreach (Transform sibling in row)
                {
                    TMP_Text text = sibling.GetComponent<TMP_Text>();
                    if (sibling != control.transform && text != null && VisibleLetters(text) > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The letters of a text the player can see: drawn characters only, under no faded group, on screen.</summary>
        public static int VisibleLetters(TMP_Text text)
        {
            if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text) || text.alpha <= 0.01f)
            {
                return 0;
            }

            if (!Visible(text.transform, text.rectTransform))
            {
                return 0;
            }

            text.ForceMeshUpdate();
            TMP_TextInfo info = text.textInfo;
            int letters = 0;
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo c = info.characterInfo[i];
                if (c.isVisible && char.IsLetter(c.character))
                {
                    letters++;
                }
            }

            return letters;
        }

        /// <summary>Not under the curtain, under no faded CanvasGroup, on screen.</summary>
        private static bool Visible(Transform node, RectTransform rect)
        {
            if (UiCurtain.Instance != null && node.IsChildOf(UiCurtain.Instance.transform))
            {
                return false;
            }

            for (Transform t = node; t != null; t = t.parent)
            {
                CanvasGroup group = t.GetComponent<CanvasGroup>();
                if (group != null && group.enabled && group.alpha <= 0.01f)
                {
                    return false;
                }
            }

            return OnScreen(rect);
        }

        private static bool OnScreen(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y).Overlaps(new Rect(0f, 0f, Screen.width, Screen.height));
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
