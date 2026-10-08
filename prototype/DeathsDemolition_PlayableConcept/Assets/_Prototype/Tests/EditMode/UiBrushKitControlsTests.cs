using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-098A P0b (C-00 07.10, KIT-API §6 tests 6–8 + KIT-KILAVUZU §7 "FircaZeminleri_MetinDogrusalIsikta") · the brush kit's
    /// controls - the C12 tabs, the C13–C16 setting rows, C18 list row, C21 big counter, C27 tooltip and C35 scroller - next to
    /// K1's <c>UiBrushKitTests</c> (tests 1–5) in a class of their own:
    ///   6   rows at TextScale 1.25 are measured from the text (EN and TR): no label or value is cut; a row is RowHeight;
    ///   7   no Update in the kit's types, no Input System / Input.Get in the kit's sources (ADR-024-2);
    ///   8   every brush sprite resolves (AP-034's file or the code fallback), never null; no file → IsAuthored false;
    ///   F   every brush ground the text stands on, in LINEAR light over the Cream100 grey ramp and pure white: text ≥ 4.5,
    ///       C8 ≥ 3, C7 ≥ 3, the controls' boundaries ≥ 3 (KILAVUZ §2.2, §4.4);
    /// and each control's state matrix / Reduced Motion (KILAVUZ §4.1–4.3), plus V3 left as it was for every caller that
    /// does not ask for Brush.
    /// </summary>
    [TestFixture]
    [Category("WP098")]
    public sealed class UiBrushKitControlsTests
    {
        private const float Text = 4.5f;
        private const float Boundary = 3f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private float _textScale;
        private bool _reduced;
        private string _language;
        private UiPalette _palette;
        private UiControlStyle _tooltipStyle;
        private RectTransform _root;

        [SetUp]
        public void SetUp()
        {
            _textScale = UiTokens.TextScale;
            _reduced = UiMotion.ReducedMotion;
            _language = UiStrings.Language;
            _palette = UiTokens.Palette;
            _tooltipStyle = UiTooltip.Style;
            UiTokens.Palette = UiPalette.Default;
            UiMotion.ReducedMotion = false;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 50f;
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
            UiTokens.Palette = _palette;
            UiTooltip.Style = _tooltipStyle;
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            foreach (GameObject go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private static void Step(float seconds)
        {
            UiMotion.TestNow += seconds;
            UiMotion.Tick();
        }

        private static void AssertFits(TMP_Text label, string text, string what)
        {
            float width = label.rectTransform.rect.width;
            Assert.Greater(width, 1f, what + ": kutunun genişliği var");
            float preferred = label.GetPreferredValues(text).x;
            Debug.Log("[DD-TEST] wp098.fit " + what + " " + preferred.ToString("F0") + "/" + width.ToString("F0"));
            Assert.LessOrEqual(preferred, width + 0.5f, what + " kesilmez (tercih edilen genişlik ≤ kutu)");
        }

        private static void AssertRgb(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, what + " r");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, what + " g");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, what + " b");
        }

        /// <summary>AP-034 Q4 (AD, S-09 08.10): the tooltip is the cloud and the word - Edge, Plate, Word and nothing else; no image wears the pointer.</summary>
        private static void AssertNoPointer(Transform box, string what)
        {
            Assert.AreEqual(3, box.childCount, what + ": ipucu yalnız Edge + Plate + Word (işaretçi yok)");
            Sprite pointer = UiBrushArt.Get(UiBrushSprite.TooltipPointer);
            Sprite framePointer = UiArt.Kit("Frame.TooltipPointer");
            foreach (Image image in box.GetComponentsInChildren<Image>(true))
            {
                StringAssert.DoesNotContain("Pointer", image.name, what + ": işaretçi nesnesi yok");
                Assert.AreNotSame(pointer, image.sprite, what + ": TooltipPointer çizilmez");
                if (framePointer != null) Assert.AreNotSame(framePointer, image.sprite, what + ": Frame.TooltipPointer çizilmez");
            }
        }

        private static RectTransform Child(Component owner, string path)
        {
            Transform t = owner.transform.Find(path);
            Assert.IsNotNull(t, owner.name + "/" + path + " yok");
            return (RectTransform)t;
        }

        // ================================================================== 6 · rows at 1.25, measured

        [Test]
        public void Satirlar_125te_MetindenOlculur_Tasmaz()
        {
            foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
            {
                UiStrings.Language = language;
                foreach (float scale in new[] { 1f, 1.25f })
                {
                    UiTokens.TextScale = scale;
                    string tag = language + " " + scale.ToString("0.00") + " ";
                    float row = UiBrushTokens.RowHeight(UiType.MenuBody, UiBrushTokens.RowHit);
                    Assert.GreaterOrEqual(row, UiFactory.LineHeight(UiType.MenuBody) + (int)UiSpace.S, tag + "satır metinden ölçülür");

                    UiSlider slider = UiBrushKit.Slider(_root, "Setting.SHAKE", "pause.shake", "CAMERA SHAKE INTENSITY", 0f, 2f, 1f, 0.1f, null, null);
                    Assert.AreEqual(row, ((RectTransform)slider.transform).sizeDelta.y, 1e-3f, tag + "kaydırıcı satırı = RowHeight");
                    AssertFits(slider.Label, UiStrings.Get("pause.shake", "CAMERA SHAKE INTENSITY"), tag + "kaydırıcı etiketi");
                    AssertFits(slider.ValueLabel, slider.ValueText, tag + "kaydırıcı değeri");

                    UiSwitch toggle = UiBrushKit.Toggle(_root, "Setting.REDUCED MOTION", "access.row.reduced_motion", "REDUCED MOTION", true, null);
                    Assert.AreEqual(row, ((RectTransform)toggle.transform).sizeDelta.y, 1e-3f, tag + "anahtar satırı = RowHeight");
                    AssertFits(toggle.Label, UiStrings.Get("access.row.reduced_motion", "REDUCED MOTION"), tag + "anahtar etiketi");
                    AssertFits(toggle.Word, UiStrings.Get("common.on", "ON"), tag + "anahtar kelimesi AÇIK");
                    AssertFits(toggle.Word, UiStrings.Get("common.off", "OFF"), tag + "anahtar kelimesi KAPALI");

                    string[] palettes = { UiStrings.Get("access.palette.default", "DEFAULT"), UiStrings.Get("access.palette.colour_blind", "COLOUR-BLIND SAFE") };
                    UiStepper cycle = UiBrushKit.Cycle(_root, "Setting.PALETTE", "access.row.palette", "PALETTE", palettes, 1, true, null);
                    Assert.AreEqual(row, ((RectTransform)cycle.transform).sizeDelta.y, 1e-3f, tag + "döngü satırı = RowHeight");
                    AssertFits(cycle.Label, UiStrings.Get("access.row.palette", "PALETTE"), tag + "döngü etiketi");
                    foreach (string value in palettes) AssertFits(cycle.ValueLabel, value, tag + "döngü değeri " + value);

                    string weapon = UiStrings.Get("weapon.class.smg", "SUBMACHINE GUN");
                    UiButton list = UiBrushKit.ListRow(_root, "Weapon.smg", weapon, null, null);
                    UiBrushKit.SetRowStatus(list, "Selection.Selected", UiStrings.Get("menu.tab.profile", "PROFILE"));
                    Assert.AreEqual(UiBrushTokens.RowHeight(UiType.MenuBody, UiBrushTokens.RowHitCompact), ((RectTransform)list.transform).sizeDelta.y, 1e-3f, tag + "liste satırı");
                    AssertFits(list.LabelText, weapon, tag + "liste satırı adı");

                    UiCounter counter = UiBrushKit.BigCounter(_root, "Gold", "results.gold", "GOLD EARNED", UiCounterStyle.Currency, 123456);
                    Assert.GreaterOrEqual(((RectTransform)counter.transform).sizeDelta.y,
                        UiFactory.LineHeight(UiType.MenuSmall) + UiFactory.LineHeight(UiType.NumericHero), tag + "sayaç bantları metinden");
                }
            }
        }

        // ================================================================== 7 · no Update, no Input System

        [Test]
        public void KitTurleri_UpdateYok_InputSystemYok()
        {
            var failures = new List<string>();
            var kitTypes = new List<Type>
            {
                typeof(UiSlider), typeof(UiSwitch), typeof(UiStepper), typeof(UiStepperArrow), typeof(UiScrollView), typeof(UiTooltip),
                typeof(UiIconButton)
            };

            foreach (Type t in typeof(UiBrushTokens).Assembly.GetTypes())
            {
                Type outer = t;
                while (outer.DeclaringType != null) outer = outer.DeclaringType;
                if (outer.Name.StartsWith("UiBrush", StringComparison.Ordinal) && !kitTypes.Contains(t)) kitTypes.Add(t);   // nested kit parts too
            }

            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Type t in kitTypes)
            {
                foreach (string loop in new[] { "Update", "LateUpdate", "FixedUpdate" })
                {
                    if (t.GetMethod(loop, Declared, null, Type.EmptyTypes, null) != null) failures.Add(t.Name + "." + loop);
                }
            }

            string kit = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Kit");
            var files = new List<string>(Directory.GetFiles(kit, "UiBrush*.cs", SearchOption.AllDirectories));
            foreach (string own in new[] { "UiSlider.cs", "UiSwitch.cs", "UiStepper.cs", "UiScrollView.cs", "UiTooltip.cs", "UiIconButton.cs" })
            {
                files.Add(Path.Combine(kit, "Components", own));
            }

            var input = new Regex(@"UnityEngine\.InputSystem|(?<![\w.])Input\.Get");
            foreach (string file in files)
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int comment = line.IndexOf("//", StringComparison.Ordinal);
                    string code = comment >= 0 ? line.Substring(0, comment) : line;
                    if (input.IsMatch(code)) failures.Add(Path.GetFileName(file) + ":" + (i + 1) + " " + line.Trim());
                }
            }

            Debug.Log("[DD-TEST] wp098.kitTypes " + kitTypes.Count + " · files " + files.Count);
            Assert.IsEmpty(failures, "kit türünde Update ya da kit kaynağında Input System / Input.Get (ADR-024-2):\n" + string.Join("\n", failures));
        }

        // ================================================================== 8 · textures: AP-034 or the code fallback

        [Test]
        public void Doku_YoksaKodYedegi_AsalaNull()
        {
            foreach (UiBrushSprite id in Enum.GetValues(typeof(UiBrushSprite)))
            {
                Sprite sprite = UiBrushArt.Get(id);
                Assert.IsNotNull(sprite, id + " null");
                string file = UiBrushArt.FileName(id);
                StringAssert.StartsWith("ap034_kit_", file, id + " AP-034 adı");
                bool present = Resources.Load<Sprite>(UiBrushArt.Folder + file) != null;
                Assert.AreEqual(present, UiBrushArt.IsAuthored(id), id + ": IsAuthored dosyanın varlığını söyler");
                Debug.Log("[DD-TEST] wp098.sprite " + id + " authored=" + present + " → " + sprite.name);
            }

            Assert.IsNotNull(UiBrushArt.Wash(new Vector2(200f, 600f)), "uzun yıkama");
            Assert.IsNotNull(UiBrushArt.Wash(new Vector2(600f, 200f)), "geniş yıkama");
        }

        // ================================================================== F · brush grounds, linear light

        private static Color Opaque(UiColor token)
        {
            Color c = UiTokens.Get(token);
            c.a = 1f;
            return c;
        }

        private static Color Ink(float alpha)
        {
            Color c = UiTokens.Get(UiColor.SurfaceInset);
            c.a = alpha;
            return c;
        }

        /// <summary>The worst ratio of <paramref name="fg"/> (opaque token, or composited at its α) over ink at <paramref name="alpha"/> on the Cream100 grey ramp and pure white.</summary>
        private static float Worst(UiColor fg, float alpha, float fgAlpha = 1f)
        {
            Color cream = Opaque(UiColor.TextPrimary);
            float creamGrey = 1.055f * Mathf.Pow(UiContrast.RelativeLuminance(cream), 1f / 2.4f) - 0.055f;
            float worst = float.MaxValue;
            for (int i = 0; i <= 65; i++)
            {
                Color world;
                if (i <= 63) world = new Color(creamGrey * i / 63f, creamGrey * i / 63f, creamGrey * i / 63f, 1f);
                else if (i == 64) world = cream;
                else world = Color.white;
                Color ground = UiContrast.Composite(Ink(alpha), world);
                Color c = UiTokens.Get(fg);
                c.a *= fgAlpha;
                worst = Mathf.Min(worst, UiContrast.Ratio(UiContrast.Composite(c, ground), ground));
            }

            return worst;
        }

        [Test]
        public void FircaZeminleri_MetinDogrusalIsikta()
        {
            Assert.IsTrue(UiContrast.BlendsInLinear, "proje Linear: kit α'ları doğrusal ışık içindir (KILAVUZ §0 T1)");
            var failures = new List<string>();
            var grounds = new[]
            {
                new KeyValuePair<string, float>("Window", UiBrushTokens.Scrim(UiBrushScrim.Window)),
                new KeyValuePair<string, float>("Pane+Wash", UiBrushTokens.Combined(UiBrushTokens.ScrimPane, UiBrushTokens.WashCore)),
                new KeyValuePair<string, float>("Wallpaper+Wash", UiBrushTokens.Combined(UiBrushTokens.ScrimWallpaper, UiBrushTokens.WashCore)),
                new KeyValuePair<string, float>("Confirm", UiBrushTokens.Scrim(UiBrushScrim.Confirm)),
                new KeyValuePair<string, float>("Tooltip", UiBrushTokens.TooltipWash),
            };

            foreach (KeyValuePair<string, float> g in grounds)
            {
                if (g.Value < UiBrushTokens.TextGroundMin) failures.Add(g.Key + " α " + g.Value.ToString("0.000") + " < TextGroundMin");
                foreach (UiColor text in new[] { UiColor.TextPrimary, UiColor.TextSecondary, UiColor.StateGold })
                {
                    float r = Worst(text, g.Value);
                    Debug.Log("[DD-TEST] wp098.ground " + g.Key + " " + text + " " + r.ToString("F2"));
                    if (r < Text) failures.Add(g.Key + " " + text + " " + r.ToString("F2") + " < 4.5");
                }

                // The controls' marks on the same ground: fill / head / pill / grip (cream), arrows and the hollow pill (TextSecondary).
                foreach (UiColor mark in new[] { UiColor.TextPrimary, UiColor.TextSecondary })
                {
                    float r = Worst(mark, g.Value);
                    if (r < Boundary) failures.Add(g.Key + " mark " + mark + " " + r.ToString("F2") + " < 3");
                }
            }

            // Text never stands on Pane / Wallpaper alone: that is why the C2 wash is there (KILAVUZ §4.4).
            Assert.Less(UiBrushTokens.ScrimPane, UiBrushTokens.TextGroundMin);
            Assert.Less(UiBrushTokens.ScrimWallpaper, UiBrushTokens.TextGroundMin);
            Debug.Log("[DD-TEST] wp098.ground Pane alone TextSecondary " + Worst(UiColor.TextSecondary, UiBrushTokens.ScrimPane).ToString("F2") + " (C2 required)");

            // C8: vermilion holds 3:1 on its opaque ink bed only; C7: the cream arms on their ink halo.
            float c8 = Worst(UiColor.AccentPrimary, UiBrushTokens.BedAlpha);
            float c7 = UiContrast.Ratio(Opaque(UiColor.TextPrimary), Opaque(UiColor.BorderFocusHalo));
            Debug.Log("[DD-TEST] wp098.c8 " + c8.ToString("F2") + " c7 " + c7.ToString("F2"));
            if (c8 < Boundary) failures.Add("C8 core on its bed " + c8.ToString("F2"));
            if (c7 < Boundary) failures.Add("C7 arms on the halo " + c7.ToString("F2"));

            Assert.IsEmpty(failures, "fırça zemininde metin / sınır eşik altı (doğrusal ışık):\n" + string.Join("\n", failures));
        }

        // ================================================================== C12 · tabs

        private static UiBrushTab[] Tabs3()
        {
            return new[]
            {
                new UiBrushTab { ObjectName = "Tab.Profile", IconKey = "Menu.Profile", NameKey = "menu.tab.profile", NameFallback = "PROFILE" },
                new UiBrushTab { ObjectName = "Tab.Statistics", IconKey = "Menu.Statistics", NameKey = "menu.tab.statistics", NameFallback = "STATISTICS" },
                new UiBrushTab { ObjectName = "Tab.Accessibility", IconKey = "Menu.Accessibility", NameKey = "menu.tab.accessibility", NameFallback = "ACCESSIBILITY" },
            };
        }

        [Test]
        public void Sekmeler_TekIz_AdimAraligi_OkZinciri_SubmitSecer_OdakSecmez()
        {
            int chosen = -1;
            UiBrushChoiceGroup group = UiBrushKit.Tabs(_root, "Tabs", Tabs3(), 1, i => chosen = i, true);
            Assert.AreEqual(3, group.Count);
            Assert.AreEqual(1, group.Selected);
            Assert.AreEqual(1, group.GetComponentsInChildren<UiBrushMark>(true).Length, "şeritte tek C8");
            Assert.AreEqual(UiBrushMarkKind.Selection, group.Mark.Kind);

            for (int i = 0; i < 3; i++)
            {
                UiIconButton tab = group.Tab(i);
                Assert.AreEqual(Tabs3()[i].ObjectName, tab.name, "ad çağırandan gelir");
                Assert.AreEqual(UiControlStyle.Brush, tab.Style);
                Assert.AreEqual(UiButtonVariant.Brush, tab.Button.Variant, "çerçevesiz: Nav dolgusu yok");
                Assert.IsNull(tab.transform.Find("ChosenBar"), "seçim çubuğu yok: seçimi grubun C8'i taşır");
                // S-09 §8 S1 (G2 closed with AP-034 v002.1's Menu.Display): the word under the chosen tab only.
                Assert.AreEqual(UiIconLabel.BelowChosen, tab.LabelMode, "G2 kapandı: kelime yalnız seçili sekmenin altında");
                Assert.AreEqual(i == 1, tab.WordVisible, "kelime yalnız seçili sekmede: " + i);
                Assert.AreEqual((int)UiIconSize.L, (int)tab.Size, "glif 40");
                Assert.GreaterOrEqual(((RectTransform)tab.transform).sizeDelta.x, UiBrushTokens.TabWidth);
            }

            var a = (RectTransform)group.Tab(0).transform;
            var b = (RectTransform)group.Tab(1).transform;
            float gap = (b.anchoredPosition.x - b.sizeDelta.x * 0.5f) - (a.anchoredPosition.x + a.sizeDelta.x * 0.5f);
            Assert.AreEqual(UiBrushTokens.TabStep - UiBrushTokens.TabWidth, gap, 0.5f, "sekmeler arası 16 (adım 88 − 72)");
            Assert.AreSame(group[1], group[0].navigation.selectOnRight, "→ zinciri");
            Assert.AreSame(group[0], group[1].navigation.selectOnLeft, "← zinciri");
            Assert.IsNull(group[0].navigation.selectOnLeft, "şerit sarmaz");

            Vector2 strip = ((RectTransform)group.Tab(1).transform).anchoredPosition + ((RectTransform)group.Tab(2).transform).sizeDelta;
            group[2].OnSelect(new BaseEventData(null));
            Assert.AreEqual(1, group.Selected, "odak sekmeyi seçmez (her okta pane kurulmaz)");
            group[2].OnSubmit(new BaseEventData(null));
            Assert.AreEqual(2, group.Selected, "Submit seçer");
            Assert.AreEqual(2, chosen, "bildirildi");
            Assert.IsTrue(group.Tab(2).Button.IsSelectedChoice);
            Assert.IsFalse(group.Tab(1).Button.IsSelectedChoice);
            Assert.IsTrue(group.Tab(2).WordVisible, "kelime seçimle taşınır");
            Assert.IsFalse(group.Tab(1).WordVisible);
            Assert.AreEqual(strip, ((RectTransform)group.Tab(1).transform).anchoredPosition + ((RectTransform)group.Tab(2).transform).sizeDelta,
                "şerit kaymaz: her sekme kelimenin yerini tutar");
            AssertRgb(UiTokens.Get(UiColor.TextPrimary), group.Tab(2).Glyph.color, "seçili glif krem");
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), group.Tab(1).Glyph.color, "diğer glif ikincil");

            UiBrushChoiceGroup bare = UiBrushKit.Tabs(_root, "TabsBare", Tabs3(), 0, null);
            UiIconButton icon = bare.Tab(0);
            Assert.AreEqual(UiIconLabel.Tooltip, icon.LabelMode, "kelimesiz şerit: kelime ipucunda");
            Assert.AreEqual(new Vector2(UiBrushTokens.TabWidth, UiBrushTokens.ControlHeight), ((RectTransform)icon.transform).sizeDelta, "72×64");
        }

        // ================================================================== C14 · slider

        [Test]
        public void Kaydirici_Firca_RayDolguBas_TikHalkaYok_DevreDisiBasYok()
        {
            float reported = -1f;
            UiSlider slider = UiBrushKit.Slider(_root, "Setting.SHAKE", "pause.shake", "CAMERA SHAKE INTENSITY", 0f, 2f, 1f, 0.1f, null, v => reported = v);
            Assert.AreEqual(UiControlStyle.Brush, slider.Style);
            Assert.AreEqual("Setting.SHAKE", slider.name);
            Assert.AreEqual(UiBrushTokens.LabelColumn, slider.Label.rectTransform.rect.width, 0.5f, "etiket sütunu 600");
            RectTransform rail = Child(slider, "Rail");
            Assert.AreEqual(UiBrushTokens.SliderRail, rail.rect.height, 1e-3f, "ray 1 px");
            Assert.AreEqual(UiBrushTokens.SliderFill, Child(slider, "Rail/Fill").sizeDelta.y, 1e-3f, "dolgu 3 px");
            Assert.AreEqual(UiBrushTokens.SliderHead + 2f * UiTokens.StrokeControl, Child(slider, "Rail/Bead").sizeDelta.x, 1e-3f, "baş 12 + 2 px mürekkep kenar");
            Assert.IsNull(slider.transform.Find("Rail/Bead/FocusRing"), "v3 halka yok: odak = C7");
            Assert.IsNull(slider.transform.Find("Rail/Tick1"), "fırça kaydırıcısında tik yok");
            // SetStops replaces the even step with stop-to-stop travel (V3 and Brush alike): check it on a row of its own.
            UiSlider stops = UiBrushKit.Slider(_root, "Setting.TEXT SCALE", "access.row.text_scale", "TEXT SCALE", 1f, 1.25f, 1f, 0f, null, null);
            stops.SetStops(1f, 1.1f, 1.25f);
            Assert.IsNull(stops.transform.Find("Rail/Tick1"), "durak da tik çizmez");
            stops.Step(+1);
            Assert.AreEqual(1.1f, stops.Value, 1e-4f, "duraklı fırça kaydırıcısı duraktan durağa");
            AssertRgb(UiTokens.Get(UiColor.TextPrimary), Child(slider, "Rail/Fill").GetComponent<Image>().color, "dolgu krem (altın değil)");

            slider.Step(+1);
            Assert.AreEqual(1.1f, slider.Value, 1e-4f, "→ bir adım (0,1)");
            Assert.AreEqual(1.1f, reported, 1e-4f, "bildirildi");
            UiMotion.ReducedMotion = true;
            slider.Step(-1);
            Assert.AreEqual(rail.rect.width * 0.5f, slider.BeadX, 0.5f, "RM: adım anında");

            Assert.IsTrue(slider.HeadVisible);
            slider.interactable = false;
            Assert.IsFalse(slider.HeadVisible, "devre dışı: baş yok");
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), slider.ValueLabel.color, "devre dışı değer ikincil");
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), slider.Label.color, "devre dışı etiket ikincil");
            slider.interactable = true;
            Assert.IsTrue(slider.HeadVisible);
            AssertRgb(UiTokens.Get(UiColor.TextPrimary), slider.ValueLabel.color, "geri krem");
        }

        // ================================================================== C15 · toggle

        [Test]
        public void Anahtar_Firca_DoluBosHap_Kelime_CaprazGecis_AzHareketteKesme()
        {
            bool reported = false;
            UiSwitch toggle = UiBrushKit.Toggle(_root, "Setting.HINTS", "settings.row.hints", "HINTS", false, v => reported = v);
            Assert.AreEqual(UiControlStyle.Brush, toggle.Style);
            Assert.AreEqual(UiButtonVariant.Brush, toggle.Button.Variant);
            Assert.IsNull(toggle.transform.Find("Track"), "ray / düğme yok");
            Assert.AreEqual(new Vector2(UiBrushTokens.TogglePillWidth, UiBrushTokens.TogglePillHeight), Child(toggle, "PillOn").sizeDelta);
            Assert.AreEqual(UiStrings.Get("common.off", "OFF"), toggle.WordText);
            Assert.AreEqual(0f, toggle.PillOnAlpha, 1e-3f, "KAPALI: dolu hap görünmez");
            Assert.AreEqual(1f, toggle.PillOffAlpha, 1e-3f, "KAPALI: içi boş kontur");
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), toggle.Word.color, "KAPALI kelime ikincil");

            toggle.Toggle();
            Assert.IsTrue(reported);
            Assert.AreEqual(UiStrings.Get("common.on", "ON"), toggle.WordText, "kelime anında");
            AssertRgb(UiTokens.Get(UiColor.TextPrimary), toggle.Word.color, "AÇIK kelime krem");
            Step(0.2f);
            Assert.AreEqual(1f, toggle.PillOnAlpha, 1e-3f, "AÇIK: dolu hap (Fast çapraz geçiş)");
            Assert.AreEqual(0f, toggle.PillOffAlpha, 1e-3f);

            UiMotion.ReducedMotion = true;
            toggle.Toggle();
            Assert.AreEqual(0f, toggle.PillOnAlpha, 1e-3f, "RM: kesme");
            Assert.AreEqual(1f, toggle.PillOffAlpha, 1e-3f);

            toggle.Set(true, false);
            toggle.SetInteractable(false);
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), Child(toggle, "PillOn").GetComponent<Image>().color, "devre dışı: hepsi ikincil");
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), toggle.Word.color, "devre dışı kelime ikincil");
        }

        // ================================================================== C16 · value cycle

        [Test]
        public void Dongu_Firca_UcOkSoluk_OteyeItinceReddedilir_UzerindekiOkKrem()
        {
            int last = -1;
            UiStepper cycle = UiBrushKit.Cycle(_root, "Setting.QUALITY", "settings.row.quality", "GRAPHICS QUALITY", new[] { "LOW", "MEDIUM", "HIGH" }, 0, false, i => last = i);
            Assert.AreEqual(UiControlStyle.Brush, cycle.Style);
            Assert.AreEqual(UiTokens.OpacityDisabled, cycle.PreviousArrow.color.a, 1e-3f, "uçtaki ok %55");
            Assert.AreEqual(1f, cycle.NextArrow.color.a, 1e-3f);
            Assert.AreEqual((float)(int)UiIconSize.S, cycle.NextArrow.rectTransform.sizeDelta.x, 1e-3f, "ok glifi 24");
            // WP-098A: AP-034 v002.1 I6 - the C16 arrows are their own O1 glyphs, two geometries (a mirror would turn the light).
            Assert.AreSame(UiArt.Icon(UiStepper.CycleLeftIcon), cycle.PreviousArrow.sprite, "◂ = Menu.CycleLeft");
            Assert.AreSame(UiArt.Icon(UiStepper.CycleRightIcon), cycle.NextArrow.sprite, "▸ = Menu.CycleRight");
            Assert.AreEqual(Quaternion.identity, cycle.PreviousArrow.transform.localRotation, "sol ok döndürülmez / aynalanmaz");
            Assert.GreaterOrEqual(Child(cycle, "Next").sizeDelta.x, UiTokens.TargetPreferred, "ok isabeti ≥ 48");

            cycle.Step(-1);
            Assert.AreEqual(0, cycle.Index, "uçta durur");
            Assert.AreEqual(-1, last, "reddedilen adım bildirilmez");

            cycle.OnMove(new AxisEventData(null) { moveDir = MoveDirection.Right });
            Assert.AreEqual(1, cycle.Index);
            Assert.AreEqual("MEDIUM", cycle.ValueText);
            Assert.AreEqual(1, last);
            Assert.AreEqual(1f, cycle.PreviousArrow.color.a, 1e-3f, "artık uçta değil");
            Step(0.3f);
            Assert.AreEqual(Vector2.zero, cycle.NextArrow.rectTransform.anchoredPosition, "ok kayması yerine döner");

            Child(cycle, "Next").GetComponent<UiStepperArrow>().OnPointerEnter(new PointerEventData(null));
            AssertRgb(UiTokens.Get(UiColor.TextPrimary), cycle.NextArrow.color, "üzerindeki ok krem");
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), cycle.PreviousArrow.color, "diğer ok ikincil");
            Child(cycle, "Next").GetComponent<UiStepperArrow>().OnPointerExit(new PointerEventData(null));
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), cycle.NextArrow.color, "ok geri ikincil");

            cycle.Step(+1);
            Assert.AreEqual(UiTokens.OpacityDisabled, cycle.NextArrow.color.a, 1e-3f);
            UiMotion.ReducedMotion = true;
            cycle.Step(+1);
            Assert.AreEqual(2, cycle.Index, "sıralı değer uçta durur");
            Assert.AreEqual(Vector2.zero, cycle.NextArrow.rectTransform.anchoredPosition, "RM: sarsıntı yok");
        }

        // ================================================================== C35 · scroll

        [Test]
        public void Kaydirma_Firca_KilRay_KremTutamak_EnAz40_MurekkepKenar()
        {
            UiScrollView scroll = UiBrushKit.Scroll(_root, "Scroll");
            Assert.AreEqual(UiControlStyle.Brush, scroll.Style);
            RectTransform track = Child(scroll, "Rail/Track");
            Assert.AreEqual(UiTokens.StrokeHairline, track.sizeDelta.x, 1e-3f, "ray 1 px");
            Color line = UiBrushTokens.LineColour();
            Assert.AreEqual(line, track.GetComponent<Image>().color, "ray çizgi tonunda");
            RectTransform pill = Child(scroll, "Rail/SlideArea/Grip/Pill");
            Assert.AreEqual(UiBrushTokens.ScrollGrip, pill.sizeDelta.x, 1e-3f, "tutamak 4 px");
            Assert.AreEqual(UiBrushTokens.ScrollGripMin, pill.sizeDelta.y, 1e-3f, "tutamak tutamaç sapının ötesine taşar: en az 40");
            RectTransform slide = Child(scroll, "Rail/SlideArea");
            Assert.AreEqual(UiBrushTokens.ScrollGripMin * 0.5f, slide.offsetMin.y, 1e-3f);
            Assert.AreEqual(-UiBrushTokens.ScrollGripMin * 0.5f, slide.offsetMax.y, 1e-3f);
            AssertRgb(UiTokens.Get(UiColor.TextPrimary), pill.GetComponent<Image>().color, "tutamak krem (altın değil)");
            AssertRgb(UiTokens.Get(UiColor.SurfaceInset), Child(scroll, "FadeTop").GetComponent<Image>().color, "kenar soluklaşması mürekkebe");
            Assert.IsNotNull(scroll.Content);
        }

        // ================================================================== C27 · tooltip

        [Test]
        public void Ipucu_Firca_Varsayilan_MurekkepBulutu_384teSarar_DevreDisidaDaAcilir()
        {
            Assert.AreEqual(UiControlStyle.Brush, UiTooltip.Style, "WP-098A: varsayılan fırça");
            const string reason = "LOCKED - REACH ROUND 8 TO UNLOCK THIS WEAPON FOR EVERY RUN AFTER THIS ONE";
            UiButton row = UiBrushKit.ListRow(_root, "Weapon.lmg", "LIGHT MACHINE GUN", "State.Locked", null);
            ((RectTransform)row.transform).anchoredPosition = new Vector2(0f, 200f);
            row.SetInteractable(false);
            UiBrushKit.Tooltip(row, "missing.reason.key", reason);

            ExecuteEvents.Execute(row.gameObject, new BaseEventData(null), ExecuteEvents.selectHandler);
            Assert.AreEqual(row.transform, UiTooltip.Target, "devre dışı satırda da açılır (gerekçe böyle okunur)");
            Assert.IsFalse(UiTooltip.Visible, "hemen değil");
            Step(UiIconTokens.TooltipDelaySeconds + 0.1f);
            Assert.IsTrue(UiTooltip.Visible);
            Assert.AreEqual(reason, UiTooltip.Text);
            Assert.AreEqual(UiControlStyle.Brush, UiTooltip.DrawnStyle);
            Rect tip = UiTooltip.Bounds;
            Assert.LessOrEqual(tip.width, UiBrushTokens.TooltipMaxWidth + 0.5f, "en fazla 384");
            Assert.Greater(tip.height, UiFactory.LineHeight(UiType.MenuSmall) + 2f * UiBrushTokens.TooltipPadY + 0.5f, "uzun gerekçe sarar (iki satır)");

            Transform box = UiTooltip.Target.root.Find("Tooltip");
            Assert.IsNotNull(box);
            Assert.IsFalse(box.Find("Edge").GetComponent<Image>().enabled, "kenar çizgisi yok");
            Image plate = box.Find("Plate").GetComponent<Image>();
            Assert.AreEqual(UiBrushTokens.TooltipWash, plate.color.a, 1e-3f, "mürekkep bulutu α 0,95");
            Assert.AreEqual(UiTooltip.WashOverhang, -((RectTransform)plate.transform).offsetMin.x, 1e-3f, "bulut kutunun 16 dışına taşar");
            Assert.AreEqual(UiTokens.FontSize(UiType.MenuSmall), box.GetComponentInChildren<TMP_Text>().fontSize, 1e-3f, "MenuSmall");
            AssertNoPointer(box, "fırça");

            ExecuteEvents.Execute(row.gameObject, new BaseEventData(null), ExecuteEvents.deselectHandler);
            Assert.IsFalse(UiTooltip.Visible, "odak gidince anında kapanır");

            UiTooltip.Style = UiControlStyle.V3;
            ExecuteEvents.Execute(row.gameObject, new PointerEventData(null), ExecuteEvents.pointerEnterHandler);
            Step(UiIconTokens.TooltipDelaySeconds + 0.1f);
            Assert.IsTrue(UiTooltip.Visible, "üzerine gelince de açılır");
            Assert.AreEqual(UiControlStyle.V3, UiTooltip.DrawnStyle);
            Assert.IsTrue(box.Find("Edge").GetComponent<Image>().enabled, "V3: kenar geri");
            AssertNoPointer(box, "v3 (değişmedi)");
            Assert.LessOrEqual(UiTooltip.Bounds.width, UiIconTokens.TooltipMaxWidth + 0.5f, "V3: 320");
            ExecuteEvents.Execute(row.gameObject, new PointerEventData(null), ExecuteEvents.pointerExitHandler);
            Assert.IsFalse(UiTooltip.Visible);
        }

        // ================================================================== C18 · list row

        [Test]
        public void ListeSatiri_DurumGlifiVeKucukMetin_EtiketeYerAcar_SecimCheckGlifi()
        {
            UiButton row = UiBrushKit.ListRow(_root, "Weapon.smg", "SUBMACHINE GUN", null, null);
            Assert.AreEqual("Weapon.smg", row.name);
            Assert.AreEqual(UiButtonVariant.Brush, row.Variant, "çerçevesiz satır");
            Image status = Child(row, "Status").GetComponent<Image>();
            TMP_Text trailing = Child(row, "Trailing").GetComponent<TMP_Text>();
            Assert.IsFalse(status.enabled, "glif yok: gizli");
            Assert.AreEqual(0f, row.LabelText.rectTransform.offsetMax.x, 1e-3f, "etiket satırın sonuna kadar");
            Assert.AreEqual((float)(int)UiIconSize.S, status.rectTransform.sizeDelta.x, 1e-3f, "yoğun satırda glif 24");
            Assert.AreEqual(UiTokens.FontSize(UiType.MenuBody), row.LabelText.fontSize, 1e-3f, "ad MenuBody");

            UiBrushKit.SetRowStatus(row, "Selection.Selected", "EQUIPPED");
            Assert.AreEqual(UiArt.HasIcon("Selection.Selected"), status.enabled, "seçim = check glifi (atlasta varsa)");
            Assert.AreEqual("EQUIPPED", trailing.text);
            Assert.AreEqual(UiTokens.FontSize(UiType.MenuSmall), trailing.fontSize, 1e-3f, "küçük metin MenuSmall");
            Assert.Less(row.LabelText.rectTransform.offsetMax.x, -trailing.rectTransform.sizeDelta.x, "etiket sondaki bloğa yer açar (ölçülerek)");

            UiBrushKit.SetRowStatus(row, null, null);
            Assert.IsFalse(status.enabled);
            Assert.AreEqual(0f, row.LabelText.rectTransform.offsetMax.x, 1e-3f);

            UiButton tall = UiBrushKit.ListRow(_root, "Row.tall", "PROFILE", "Selection.Selected", null, false);
            Assert.AreEqual(UiBrushTokens.RowHeight(UiType.MenuBody, UiBrushTokens.RowHit), ((RectTransform)tall.transform).sizeDelta.y, 1e-3f);
            Assert.AreEqual((float)(int)UiIconSize.M, Child(tall, "Status").sizeDelta.x, 1e-3f, "C13 boyunda glif 32");
        }

        // ================================================================== C21 · big counter

        [Test]
        public void BuyukSayac_ParaAltin_DigerKrem_BaslikVeKilCizgi_AzHareketteSonDeger()
        {
            UiCounter gold = UiBrushKit.BigCounter(_root, "Gold", "results.gold", "GOLD EARNED", UiCounterStyle.Currency, 0);
            UiCounter kills = UiBrushKit.BigCounter(_root, "Kills", "results.kills", "KILLS", UiCounterStyle.Plain, 0);
            AssertRgb(UiTokens.Get(UiColor.StateGold), gold.ValueText.color, "para StateGold (rezerv burada)");
            AssertRgb(UiTokens.Get(UiColor.TextPrimary), kills.ValueText.color, "diğer sayı krem");
            Assert.AreEqual(UiTokens.FontSize(UiType.NumericHero), gold.ValueText.fontSize, 1e-3f, "NumericHero 56");
            TMP_Text caption = Child(gold, "Caption").GetComponent<TMP_Text>();
            Assert.AreEqual("GOLD EARNED", caption.text);
            AssertRgb(UiTokens.Get(UiColor.TextSecondary), caption.color, "başlık ikincil");
            RectTransform rule = Child(gold, "Rule");
            Assert.AreEqual(UiTokens.StrokeHairline, rule.rect.height, 1e-3f, "altında 1 px çizgi");
            Assert.AreEqual(UiBrushTokens.LineColour(), rule.GetComponent<Image>().color);
            Assert.LessOrEqual(-gold.ValueText.rectTransform.offsetMax.y, -rule.offsetMax.y, "sayı çizginin üstünde");

            UiMotion.ReducedMotion = true;
            gold.RollTo(240);
            Assert.AreEqual("240", gold.ValueText.text, "RM: son değer");

            UiCounter small = UiBrushKit.BigCounter(_root, "Round", "results.round", "ROUND", UiCounterStyle.Fraction, 3, false, 10);
            Assert.AreEqual(UiTokens.FontSize(UiType.NumericL), small.ValueText.fontSize, 1e-3f, "hero değil: NumericL");
            Assert.IsNotNull(small.transform.Find("Max"), "kesir: '/ en çok'");
        }

        // ================================================================== V3 stays V3

        [Test]
        public void Varsayilan_V3_Degismez()
        {
            UiSlider slider = UiSlider.Create("S", _root, "UI SCALE", 0.9f, 1.2f, 1f, 0.05f, null, null);
            Assert.AreEqual(UiControlStyle.V3, slider.Style);
            Assert.AreEqual(UiSlider.RowHeight, ((RectTransform)slider.transform).sizeDelta.y, 1e-3f);
            Assert.IsNotNull(slider.transform.Find("Rail/Bead/FocusRing"), "v3 halka yerinde");
            Assert.IsNotNull(slider.transform.Find("Rail/Tick1"), "v3 tikleri yerinde");
            Assert.AreEqual(Child(slider, "Rail").rect.height, Child(slider, "Rail/Fill").rect.height, 1e-3f, "v3 dolgu rayı doldurur");

            UiSwitch toggle = UiSwitch.Create("T", _root, "HINTS", false, null);
            Assert.AreEqual(UiControlStyle.V3, toggle.Style);
            Assert.IsNotNull(toggle.transform.Find("Track"));
            Assert.IsNull(toggle.transform.Find("PillOn"));
            Assert.AreEqual(UiButtonVariant.Secondary, toggle.Button.Variant);

            UiStepper stepper = UiStepper.Create("C", _root, "QUALITY", new[] { "LOW", "HIGH" }, 0, false, null);
            Assert.AreEqual(UiControlStyle.V3, stepper.Style);
            Assert.AreEqual(0.35f, stepper.PreviousArrow.color.a, 1e-3f, "v3 uç oku 0,35");
            Assert.AreEqual(UiStepper.ChevronSize, Child(stepper, "Previous").sizeDelta.x, 1e-3f);

            UiScrollView scroll = UiScrollView.Create("V", _root);
            Assert.AreEqual(UiControlStyle.V3, scroll.Style);
            AssertRgb(UiTokens.Get(UiScrollView.EdgeColor), Child(scroll, "FadeTop").GetComponent<Image>().color, "v3 kenar kart zemininde");
            Assert.AreEqual(UiScrollView.RailWidth, Child(scroll, "Rail/Track").sizeDelta.x, 1e-3f);

            UiIconButton tab = UiIconControls.Tab(_root, "Menu.Profile", "menu.tab.profile", "PROFILE", null);
            Assert.AreEqual(UiControlStyle.V3, tab.Style);
            Assert.AreEqual(UiButtonVariant.Nav, tab.Button.Variant);
            Assert.IsNotNull(tab.transform.Find("ChosenBar"), "v3 seçim çubuğu yerinde");
        }
    }
}
