using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-081 B2 · the icon controls of menu v4 (R2 U1 "less text, more icons", U2 overflow):
    ///   grid      glyphs 24 / 32 / 40 on the 8 px grid, hits ≥ 44, tabs on the grid;
    ///   words     a reader name from the string table in both languages, the word under a tab fits at text 1.0 and 1.25;
    ///   state     the chosen tab is shape + luminance (bar, fill, outline), not hue; glyph tints ≥ 3:1 on their fills;
    ///   tooltip   on hover and on keyboard / gamepad focus alike, after the settle delay (Reduced Motion too), under the
    ///             control or above it at the foot, inside the safe inset;
    ///   G1        every menu key is an SDF glyph; a missing key hides the glyph, keeps the words and is recorded.
    /// </summary>
    [TestFixture]
    [Category("WP081")]
    public sealed class UiIconKitTests
    {
        private static readonly string[][] MenuTabs =
        {
            new[] { "Menu.Profile", "menu.tab.profile", "PROFILE" },
            new[] { "Menu.Statistics", "menu.tab.statistics", "STATISTICS" },
            new[] { "Menu.Achievements", "menu.tab.achievements", "ACHIEVEMENTS" },
            new[] { "Menu.Settings", "menu.tab.settings", "SETTINGS" },
            new[] { "Menu.Controls", "menu.tab.controls", "CONTROLS" },
            new[] { "Menu.Accessibility", "menu.tab.accessibility", "ACCESSIBILITY" }
        };

        private readonly List<GameObject> _created = new List<GameObject>();
        private float _textScale;
        private bool _reduced;
        private string _language;
        private RectTransform _root;

        [SetUp]
        public void SetUp()
        {
            _textScale = UiTokens.TextScale;
            _reduced = UiMotion.ReducedMotion;
            _language = UiStrings.Language;
            UiMotion.ReducedMotion = false;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 50f;
            UiIconButton.ResetMissingForTests();
            var go = new GameObject("Root", typeof(RectTransform));
            _created.Add(go);
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void TearDown()
        {
            UiTokens.TextScale = _textScale;
            UiMotion.ReducedMotion = _reduced;
            UiStrings.Language = _language;
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            UiIconButton.ResetMissingForTests();
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private static void Step(float seconds)
        {
            UiMotion.TestNow += seconds;
            UiMotion.Tick();
        }

        private static bool OnGrid(float value)
        {
            return Mathf.Abs(value / UiIconTokens.Grid - Mathf.Round(value / UiIconTokens.Grid)) < 1e-4f;
        }

        /// <summary>The control's rect in the root's local space (the root's pivot is its centre).</summary>
        private static Rect RectIn(RectTransform control)
        {
            Vector2 size = control.sizeDelta;
            return new Rect(control.anchoredPosition - size * 0.5f, size);
        }

        [Test]
        public void Izgara_GlifTabaniVeIsabet_44()
        {
            foreach (UiIconSize size in new[] { UiIconSize.S, UiIconSize.M, UiIconSize.L })
            {
                Assert.GreaterOrEqual((int)size, UiIconTokens.GlyphFloor, size + ": the 24 px floor");
                Assert.IsTrue(OnGrid((int)size), size + ": on the 8 px grid");
                float hit = UiIconTokens.Hit(size);
                Assert.GreaterOrEqual(hit, UiTokens.TargetCompact, size + ": hit ≥ 44");
                Assert.GreaterOrEqual(hit, (int)size + 2 * UiIconTokens.Grid, size + ": a grid unit of air around the glyph");

                UiIconButton icon = UiIconControls.Button(_root, "Menu.Settings", "menu.tab.settings", "SETTINGS", null, size);
                var rect = (RectTransform)icon.transform;
                Assert.AreEqual(new Vector2(hit, hit), rect.sizeDelta, size + ": the button is its square hit");
                Assert.AreEqual((float)(int)size, ((RectTransform)icon.Glyph.transform).sizeDelta.x, size + ": the glyph size");
            }

            Assert.AreEqual(44f, UiIconTokens.Hit(UiIconSize.S));
            Assert.AreEqual(48f, UiIconTokens.Hit(UiIconSize.M));
            Assert.AreEqual(56f, UiIconTokens.Hit(UiIconSize.L));
        }

        [Test]
        public void OkuyucuAdi_TabloDan_IkiDilde_NesneAdiVeAnlatim()
        {
            foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
            {
                UiStrings.Language = language;
                foreach (string[] tab in MenuTabs)
                {
                    Assert.IsTrue(UiStrings.Has(tab[1]), tab[1] + ": a string-table row, not a literal");
                    UiIconButton icon = UiIconControls.Button(_root, tab[0], tab[1], tab[2], null);
                    string expected = UiStrings.Get(tab[1]);
                    Assert.IsFalse(string.IsNullOrEmpty(expected), language + " " + tab[1] + ": the row has this language");
                    Assert.AreEqual(expected, icon.AccessibleName, language + " " + tab[1]);
                    Assert.AreEqual(expected, icon.gameObject.name, "the object (and its events) carry the reader name");
                    Assert.AreEqual(expected, icon.ShortLabel, "no short key: the word is the name");
                    Assert.AreEqual(expected, icon.Narration, "a plain button: the name alone");
                    Assert.IsFalse(icon.IsTab);
                }
            }

            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("AYARLAR", UiIconControls.Button(_root, "Menu.Settings", "menu.tab.settings", "SETTINGS", null).AccessibleName);
        }

        [Test]
        public void Sekme_KelimeSigar_TrEn_Metin1ve125_IzgaraUzerinde()
        {
            foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
            {
                foreach (float scale in new[] { 1f, 1.25f })
                {
                    UiStrings.Language = language;
                    UiTokens.TextScale = scale;
                    var tabs = new List<UiIconButton>();
                    foreach (string[] tab in MenuTabs)
                    {
                        UiIconButton icon = UiIconControls.Tab(_root, tab[0], tab[1], tab[2], null);
                        tabs.Add(icon);
                        string name = language + " ×" + scale + " " + tab[1];
                        var rect = (RectTransform)icon.transform;
                        Assert.IsTrue(OnGrid(rect.sizeDelta.x) && OnGrid(rect.sizeDelta.y), name + ": " + rect.sizeDelta + " on the grid");
                        Assert.GreaterOrEqual(rect.sizeDelta.x, UiIconTokens.TabMinWidth, name);
                        Assert.GreaterOrEqual(rect.sizeDelta.y, UiTokens.TargetCompact, name + ": hit ≥ 44");

                        TMP_Text label = icon.Button.LabelText;
                        Assert.AreEqual(icon.ShortLabel, label.text, name + ": the word under the glyph");
                        Rect box = ((RectTransform)label.transform).rect;
                        Vector2 need = label.GetPreferredValues(label.text);
                        Debug.Log("[DD-TEST] wp081.tab " + name + " box=" + box.width.ToString("F0") + "x" + box.height.ToString("F0")
                                  + " need=" + need.x.ToString("F0") + "x" + need.y.ToString("F0"));
                        Assert.LessOrEqual(need.x, box.width + 0.5f, name + ": the word fits across (U2)");
                        Assert.LessOrEqual(need.y, box.height + 0.5f, name + ": the word fits in its line (U2)");

                        Rect glyph = ((RectTransform)icon.Glyph.transform).rect;
                        float glyphBottom = rect.sizeDelta.y * 0.5f - (int)UiIconTokens.TabPadding - glyph.height;
                        float labelTop = -rect.sizeDelta.y * 0.5f + (int)UiIconTokens.TabPadding + box.height;
                        Assert.GreaterOrEqual(glyphBottom - labelTop, (int)UiIconTokens.TabLabelGap - 0.5f, name + ": glyph and word do not touch");
                    }

                    float width = UiIconControls.Tabs(tabs, new Vector2(-900f, 0f));
                    float sum = 0f;
                    foreach (UiIconButton icon in tabs)
                    {
                        sum += ((RectTransform)icon.transform).sizeDelta.x;
                    }

                    Assert.AreEqual(sum + (tabs.Count - 1) * (int)UiTokens.TargetGap, width, 1e-3f, "the row: tabs a grid unit apart");
                    for (int i = 0; i < tabs.Count; i++)
                    {
                        UnityEngine.UI.Navigation nav = tabs[i].Button.navigation;
                        Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, nav.mode);
                        Assert.AreEqual(i > 0 ? tabs[i - 1].Button : null, nav.selectOnLeft, "left goes to the previous tab");
                        Assert.AreEqual(i < tabs.Count - 1 ? tabs[i + 1].Button : null, nav.selectOnRight, "right goes to the next tab");
                    }

                    foreach (UiIconButton icon in tabs)
                    {
                        Object.DestroyImmediate(icon.gameObject);
                    }
                }
            }
        }

        [Test]
        public void SeciliSekme_SekilVeParlaklik_RenkTekBasinaDegil_Kontrast3()
        {
            UiIconButton tab = UiIconControls.Tab(_root, "Menu.Settings", "menu.tab.settings", "SETTINGS", null);
            Assert.IsTrue(tab.IsTab);
            Assert.IsFalse(tab.BarVisible, "not chosen: no bar");
            Assert.AreEqual(UiTokens.Get(UiColor.TextSecondary), tab.Glyph.color, "the other tabs: TextSecondary");

            tab.SetSelected(true);
            Assert.IsTrue(tab.Button.IsSelectedChoice);
            Assert.IsTrue(tab.BarVisible, "chosen: the bar under the word - a shape, not a hue");
            Assert.AreEqual(UiTokens.Get(UiColor.TextPrimary), tab.Glyph.color, "the choice: TextPrimary");
            Assert.AreEqual(UiStrings.Get("menu.tab.settings") + ", " + UiStrings.Get("selection.selected"), tab.Narration);

            tab.SetInteractable(false);
            Assert.AreEqual(UiTokens.Get(UiColor.TextSecondary), tab.Glyph.color, "unavailable: TextSecondary");
            StringAssert.EndsWith(UiStrings.Get("selection.unavailable"), tab.Narration);

            UiIconButton plain = UiIconControls.Button(_root, "Menu.Back", "common.main_menu", "MAIN MENU", null);
            Assert.AreEqual(UiTokens.Get(UiColor.TextPrimary), plain.Glyph.color, "a plain button: TextPrimary");

            // The glyph tints against the fills UiButton gives each state (ResolveTargets): WCAG 1.4.11, a graphic ≥ 3:1.
            var pairs = new[]
            {
                new object[] { "plain on SurfaceBase", UiColor.TextPrimary, UiColor.SurfaceBase },
                new object[] { "chosen tab on SurfaceRaised", UiColor.TextPrimary, UiColor.SurfaceRaised },
                new object[] { "other tab on SurfaceBase", UiColor.TextSecondary, UiColor.SurfaceBase },
                new object[] { "unavailable on SurfaceRaised", UiColor.TextSecondary, UiColor.SurfaceRaised },
                new object[] { "chosen bar on SurfaceRaised", UiColor.TextPrimary, UiColor.SurfaceRaised }
            };
            foreach (object[] pair in pairs)
            {
                float ratio = UiContrast.Ratio(UiTokens.Get((UiColor)pair[1]), UiTokens.Get((UiColor)pair[2]));
                Debug.Log("[DD-TEST] wp081.contrast " + pair[0] + "=" + ratio.ToString("F2"));
                Assert.GreaterOrEqual(ratio, 3f, (string)pair[0]);
            }

            float tip = UiContrast.Ratio(UiTokens.Get(UiColor.TextPrimary), UiTokens.Get(UiColor.SurfaceRaised));
            Assert.GreaterOrEqual(tip, 4.5f, "the tooltip word: text on its plate");
        }

        [Test]
        public void Ipucu_OdaktaVeUzerindeyken_BeklemeSonrasi_AltaYaDaUste_GuvenliAlanda()
        {
            UiIconButton icon = UiIconControls.Button(_root, "Menu.Settings", "menu.tab.settings", "SETTINGS", null);
            var rect = (RectTransform)icon.transform;
            rect.anchoredPosition = new Vector2(0f, 200f);

            icon.Button.OnSelect(new BaseEventData(null));
            Assert.AreEqual(rect, UiTooltip.Target, "focus lifts the control: the tooltip is its");
            Assert.IsFalse(UiTooltip.Visible, "not at once");
            Step(0.1f);
            Assert.IsFalse(UiTooltip.Visible, "a pointer passing over draws nothing");
            Step(UiIconTokens.TooltipDelaySeconds);
            Assert.IsTrue(UiTooltip.Visible, "settled: the word shows");
            Assert.AreEqual(icon.ShortLabel, UiTooltip.Text);

            Rect control = RectIn(rect);
            Rect tip = UiTooltip.Bounds;
            Assert.LessOrEqual(tip.yMax, control.yMin - (int)UiIconTokens.TooltipGap + 0.5f, "under the control, a grid unit away");
            Assert.AreEqual(control.center.x, tip.center.x, 0.5f, "centred on it");
            Assert.LessOrEqual(tip.width, UiIconTokens.TooltipMaxWidth + 0.5f);

            icon.Button.OnDeselect(new BaseEventData(null));
            Assert.IsFalse(UiTooltip.Visible, "the control drops: the tooltip goes at once");
            Assert.IsNull(UiTooltip.Target);

            // At the foot of the screen, at its right edge: above, and inside the safe inset.
            rect.anchoredPosition = new Vector2(960f - 24f, -540f + 40f);
            icon.Button.OnSelect(new BaseEventData(null));
            Step(UiIconTokens.TooltipDelaySeconds + 0.2f);
            Assert.IsTrue(UiTooltip.Visible);
            control = RectIn(rect);
            tip = UiTooltip.Bounds;
            Assert.GreaterOrEqual(tip.yMin, control.yMax + (int)UiIconTokens.TooltipGap - 0.5f, "no room below: above");
            Assert.LessOrEqual(tip.xMax, 960f - (int)UiTokens.SafeInset + 0.5f, "inside the safe inset");
            icon.Button.OnDeselect(new BaseEventData(null));

            // Reduced Motion keeps the settle (Data class): no tooltip flashing under a moving pointer.
            UiMotion.ReducedMotion = true;
            icon.Button.OnSelect(new BaseEventData(null));
            Step(0.1f);
            Assert.IsFalse(UiTooltip.Visible, "RM: still waits");
            Step(UiIconTokens.TooltipDelaySeconds + 0.1f);
            Assert.IsTrue(UiTooltip.Visible, "RM: then shows");
            icon.Button.OnDeselect(new BaseEventData(null));

            UiIconButton tab = UiIconControls.Tab(_root, "Menu.Profile", "menu.tab.profile", "PROFILE", null);
            tab.Button.OnSelect(new BaseEventData(null));
            Step(UiIconTokens.TooltipDelaySeconds + 0.2f);
            Assert.IsFalse(UiTooltip.Visible, "a tab already shows its word: no tooltip");
        }

        [Test]
        public void G1_MenuAnahtarlariSdfGlif_EksikAnahtarKayitliVeKelimelerKalir()
        {
            var keys = new List<string> { "Menu.Back", "Menu.Confirm" };
            foreach (string[] tab in MenuTabs)
            {
                keys.Add(tab[0]);
            }

            foreach (string key in keys)
            {
                UiIconButton icon = UiIconControls.Button(_root, key, "menu.tab.settings", "SETTINGS", null);
                Assert.IsTrue(icon.HasGlyph, key + ": in the G1 atlas");
                Assert.IsTrue(UiArt.IsGlyph(icon.Glyph.sprite), key + ": a signed-distance glyph, not a raster sprite");
            }

            CollectionAssert.IsEmpty(UiIconButton.MissingIconKeys, "every menu key resolves");

            UiIconButton missing = UiIconControls.Button(_root, "Menu.NotDrawnYet", "menu.tab.settings", "SETTINGS", null);
            Assert.IsFalse(missing.HasGlyph, "no art: no glyph drawn");
            CollectionAssert.Contains(UiIconButton.MissingIconKeys, "Menu.NotDrawnYet", "and the key is recorded for the screen tests");
            Assert.AreEqual(UiStrings.Get("menu.tab.settings"), missing.AccessibleName, "the reader name stays");
            Assert.AreEqual(new Vector2(48f, 48f), ((RectTransform)missing.transform).sizeDelta, "and the hit");
        }
    }
}
