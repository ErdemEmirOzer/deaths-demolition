using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 slice 3 · T3 / T11 at TextScale 1.25 on the real screens (AD slice 3 MUST-1: a face
    /// change may not make visible text vanish or overlap at any supported TextScale). Selection,
    /// the run HUD and the main menu are built at 1.25 (a temp ui-settings file - the user's is
    /// never touched); every visible, non-empty TMP text must lay out untruncated, and the three
    /// pairs that collided in the f4 frames must not overlap: the weapon card name vs its stat
    /// line, the HUD health label / value vs the bar.
    /// </summary>
    [Category("WP068")]
    public sealed class UiTextScaleTests
    {
        private string _settingsPath;
        private bool _fast;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            PrototypeUiFlow.FastTransitions = true;
            _settingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dd-wp068-textscale-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.TextScale = 1f;
            PrototypeUiFlow.FastTransitions = _fast;
            UiSettings.SetPathOverride(null);
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }

        /// <summary>T3: every visible, non-empty TMP text on screen lays out untruncated.</summary>
        private static List<string> Truncated(string where)
        {
            var failures = new List<string>();
            foreach (TMP_Text text in Object.FindObjectsOfType<TMP_Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text) || text.alpha <= 0f)
                {
                    continue;
                }

                text.ForceMeshUpdate();
                if (text.isTextTruncated)
                {
                    failures.Add(where + " · " + PathOf(text.transform) + " '" + text.text + "'");
                }
            }

            return failures;
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

        /// <summary>Screen-space rect of the drawn glyphs of a text (its textBounds), or of a RectTransform.</summary>
        private static Rect GlyphRect(TMP_Text text)
        {
            text.ForceMeshUpdate();
            Bounds b = text.textBounds;
            Vector3 min = text.transform.TransformPoint(b.min);
            Vector3 max = text.transform.TransformPoint(b.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static Transform Find(string name)
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>())
            {
                if (t.name == name && t.gameObject.activeInHierarchy)
                {
                    return t;
                }
            }

            return null;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator TextScale125_SecimHudAnaMenu_KesilmeYok_CakismaYok()
        {
            yield return BootTanMainMenuye();
            UiSettings.TextScale = 1.25f;
            var failures = new List<string>();

            // Selection: the weapon card name and its stat line.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            yield return null;
            failures.AddRange(Truncated("selection"));
            Transform card = null;
            foreach (UiButton button in Object.FindObjectsOfType<UiButton>())
            {
                if (button.name.StartsWith("Weapon.", System.StringComparison.Ordinal) && button.IsInteractable())
                {
                    card = button.transform;
                    break;
                }
            }

            if (card != null)
            {
                TMP_Text name = card.GetComponent<UiButton>().LabelText;
                TMP_Text detail = card.Find("Detail").GetComponent<TMP_Text>();
                if (GlyphRect(name).Overlaps(GlyphRect(detail)))
                {
                    failures.Add("selection · weapon name overlaps its stat line (T11)");
                }
            }
            else
            {
                failures.Add("selection · weapon card not found");
            }

            // Run HUD: the health label / value against the bar.
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            yield return new WaitForSecondsRealtime(0.5f);
            failures.AddRange(Truncated("hud"));
            Transform health = Find("Health");
            if (health != null)
            {
                Rect bar = WorldRect((RectTransform)health.GetComponentInChildren<UiBar>().transform);
                foreach (TMP_Text text in health.GetComponentsInChildren<TMP_Text>())
                {
                    if ((text.name == "Label" || text.name == "Value") && !string.IsNullOrEmpty(text.text) && GlyphRect(text).Overlaps(bar))
                    {
                        failures.Add("hud · health " + text.name + " overlaps the bar (T11)");
                    }
                }
            }
            else
            {
                failures.Add("hud · health panel not found");
            }

            // Main menu (profile pane with the picker chips).
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            yield return null;
            failures.AddRange(Truncated("mainmenu"));

            // Settings / Accessibility panes (measured captions, the HINTS row).
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            foreach (UiContext pane in new[] { UiContext.Settings, UiContext.Accessibility })
            {
                menu.ShowPane(pane);
                yield return null;
                yield return null;
                failures.AddRange(Truncated("mainmenu." + pane));
            }

            Assert.IsEmpty(failures, "TextScale 1,25 (T3 / T11):\n" + string.Join("\n", failures));
        }
    }
}
