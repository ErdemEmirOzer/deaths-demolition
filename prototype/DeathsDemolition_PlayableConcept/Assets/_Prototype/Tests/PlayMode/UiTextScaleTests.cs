using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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

            // Selection v3 (WP-068 slice 7): the stage's lines never meet, a stat's label never meets its value, and a
            // weapon row's name never meets its chip.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            yield return null;
            yield return null;
            failures.AddRange(Truncated("selection"));
            SelectionScreen selection = Object.FindObjectOfType<SelectionScreen>();
            if (selection == null)
            {
                failures.Add("selection · screen not found");
            }
            else
            {
                TMP_Text[] stage = selection.StageTexts;
                for (int i = 0; i + 1 < stage.Length; i++)
                {
                    if (!string.IsNullOrEmpty(stage[i].text) && !string.IsNullOrEmpty(stage[i + 1].text) && GlyphRect(stage[i]).Overlaps(GlyphRect(stage[i + 1])))
                    {
                        failures.Add("selection · stage " + stage[i].name + " overlaps " + stage[i + 1].name + " (T11)");
                    }
                }

                foreach (Transform t in selection.GetComponentsInChildren<Transform>())
                {
                    if (!t.name.StartsWith("Stat", System.StringComparison.Ordinal) || t.Find("Label") == null)
                    {
                        continue;
                    }

                    TMP_Text label = t.Find("Label").GetComponent<TMP_Text>();
                    TMP_Text value = t.Find("Value").GetComponent<TMP_Text>();
                    if (GlyphRect(label).Overlaps(GlyphRect(value)))
                    {
                        failures.Add("selection · " + t.name + " label overlaps its value (T11)");
                    }
                }

                foreach (UiButton button in selection.GetComponentsInChildren<UiButton>())
                {
                    if (!button.name.StartsWith("Weapon.", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (TMP_Text chip in button.GetComponentsInChildren<TMP_Text>())
                    {
                        if (chip != button.LabelText && chip.transform.parent.name == "Chip" && GlyphRect(chip).Overlaps(GlyphRect(button.LabelText)))
                        {
                            failures.Add("selection · " + button.name + " name overlaps its chip (T11)");
                        }
                    }
                }
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

            // Settings / Accessibility panes (measured captions, the HINTS row); Achievements (the row's title and
            // description bands follow the line boxes - a fixed 24 px band truncated every title after slice 3).
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            foreach (UiContext pane in new[] { UiContext.Settings, UiContext.Accessibility, UiContext.Achievements })
            {
                menu.ShowPane(pane);
                yield return null;
                yield return null;
                failures.AddRange(Truncated("mainmenu." + pane));
                if (pane == UiContext.Achievements)
                {
                    // The pager stays inside the pane at 1.25: rows per page follow the pane's room.
                    Transform next = Find("Pager.Next");
                    Transform note = Find("Note");
                    RectTransform clip = next != null ? next.GetComponentInParent<RectMask2D>()?.rectTransform : null;
                    if (next == null || clip == null)
                    {
                        failures.Add("mainmenu.Achievements · pager or pane clip not found");
                    }
                    else
                    {
                        Rect paneRect = WorldRect(clip);
                        foreach (Transform t in new[] { next, note })
                        {
                            Rect r = t != null ? WorldRect((RectTransform)t) : Rect.zero;
                            if (t == null || r.yMin < paneRect.yMin - 0.5f)
                            {
                                failures.Add("mainmenu.Achievements · " + (t != null ? t.name : "?") + " below the pane: " + r + " vs " + paneRect);
                            }
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, "TextScale 1,25 (T3 / T11):\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// WP-068 slice 6 · E6 at TextScale 1.25 (T3 / T8). The pane has four sections, and
        /// every settings row, once focused, lies inside the pane's viewport: the pane scrolls
        /// instead of clipping. The rail shows exactly when something is hidden, and the
        /// preview follows the UI scale.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ayarlar125_HerSatirOdaktaGorunur_RayYalnizGerekince()
        {
            yield return BootTanMainMenuye();
            UiSettings.TextScale = 1.25f;
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            menu.ShowPane(UiContext.Settings);
            yield return null;
            yield return null;

            var failures = new List<string>(Truncated("settings.125"));
            UiScrollView scroll = menu.GetComponentInChildren<UiScrollView>();
            Assert.IsNotNull(scroll, "Settings pane'i kaydırıcıda");
            foreach (string section in new[] { "DISPLAY", "AUDIO", "GRAPHICS", "DATA" })
            {
                if (Find("Section." + section) == null) failures.Add("bölüm yok: " + section);
            }

            // Twice: at the pane's own height (the batch Editor's 4:3 canvas is tall, the whole
            // pane may fit), then in a short 360 px viewport - a Mac window / 1.25 stand-in in
            // which the pane must scroll, show its rail and bring each focused row into view.
            string[] rows = { "Display", "Setting.UI SCALE", "Setting.MASTER VOLUME", "Setting.UI SOUNDS", "Setting.GRAPHICS QUALITY", "Telemetry", "Hints" };
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    var root = (RectTransform)scroll.transform;
                    root.anchorMin = new Vector2(0f, 1f);
                    root.sizeDelta = new Vector2(root.sizeDelta.x, 360f);
                    scroll.SetContentHeight(scroll.Content.rect.height);
                    yield return null;
                    yield return null;
                    Assert.IsTrue(scroll.Scrolls, "360 px görünümde pane kayar");
                }

                Rect viewport = WorldRect(scroll.Viewport);
                foreach (string name in rows)
                {
                    Transform row = Find(name);
                    if (row == null)
                    {
                        failures.Add("satır yok: " + name);
                        continue;
                    }

                    UiInput.Focus(row.GetComponent<UnityEngine.UI.Selectable>());
                    yield return new WaitForSecondsRealtime(0.3f);
                    Rect r = WorldRect((RectTransform)row);
                    if (r.yMin < viewport.yMin - 0.01f * viewport.height || r.yMax > viewport.yMax + 0.01f * viewport.height)
                    {
                        failures.Add("geçiş " + pass + " · " + name + " odakta görünüm dışında: " + r + " / " + viewport);
                    }
                }

                Assert.AreEqual(scroll.Scrolls, scroll.RailVisible, "ray yalnız gizli içerik varken görünür (geçiş " + pass + ")");
            }

            Transform hud = Find("PreviewHud");
            UiSlider scale = Find("Setting.UI SCALE").GetComponent<UiSlider>();
            if (hud != null)
            {
                Assert.AreEqual(scale.Value, hud.localScale.x, 1e-3f, "önizleme 1:1 × UI ölçeği (AD 6ab SHOULD-3)");
            }

            Debug.Log("[DD-TEST] wp068.s6.settings125 content=" + scroll.Content.rect.height.ToString("0") + " viewport(short)=" + scroll.Viewport.rect.height.ToString("0") + " offset=" + scroll.Offset.ToString("0") + " preview=" + (hud != null));
            Assert.IsEmpty(failures, "Settings 1,25 (T3 / T8):\n" + string.Join("\n", failures));
        }
    }
}
