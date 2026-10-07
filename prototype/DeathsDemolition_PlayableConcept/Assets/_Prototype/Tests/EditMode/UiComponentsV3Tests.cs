using System;
using System.Collections.Generic;
using System.IO;
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
    /// WP-068 slice 5 · component library v3 (UI v3 direction §4; T3 overflow, T5 RM,
    /// T7 docs, T8 focus): switch, stepper, chip, notice, modal, counter, ring, pips.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiComponentsV3Tests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private float _textScale;
        private bool _reduced;
        private RectTransform _root;

        [SetUp]
        public void SetUp()
        {
            _textScale = UiTokens.TextScale;
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false; // the machine's saved UI settings may have it on; each test turns it on itself
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

        [Test]
        public void Anahtar_KelimeVeKonum_AzHareketteAninda()
        {
            bool reported = false;
            UiSwitch sw = UiSwitch.Create("Switch", _root, "REDUCE FLASHES", false, v => reported = v);
            Assert.AreEqual("OFF", sw.WordText);
            float offX = sw.KnobX;
            sw.Toggle();
            Assert.IsTrue(sw.Value);
            Assert.IsTrue(reported, "değişim bildirildi");
            Assert.AreEqual("ON", sw.WordText, "kelime anında değişir");
            Step(0.2f);
            Assert.Greater(sw.KnobX, offX, "düğme sağa kaydı (konum = şekil)");

            UiMotion.ReducedMotion = true;
            sw.Toggle();
            Assert.AreEqual(offX, sw.KnobX, 1e-3f, "RM: düğme anında yerinde");
        }

        [Test]
        public void Adimlayici_SolSag_UclarDurur_KategoriSarar()
        {
            int last = -1;
            UiStepper quality = UiStepper.Create("Quality", _root, "GRAPHICS QUALITY", new[] { "LOW", "MEDIUM", "HIGH" }, 0, false, i => last = i);
            quality.OnMove(new AxisEventData(null) { moveDir = MoveDirection.Right });
            Assert.AreEqual(1, quality.Index);
            Assert.AreEqual("MEDIUM", quality.ValueText);
            Assert.AreEqual(1, last);
            quality.Step(+1);
            quality.Step(+1);
            Assert.AreEqual(2, quality.Index, "sıralı değerler uçta durur");
            quality.OnMove(new AxisEventData(null) { moveDir = MoveDirection.Left });
            Assert.AreEqual(1, quality.Index);

            UiStepper palette = UiStepper.Create("Palette", _root, "PALETTE", new[] { "DEFAULT", "COLOUR-BLIND SAFE" }, 1, true, null);
            palette.Step(+1);
            Assert.AreEqual(0, palette.Index, "kategoriler sarar");
        }

        /// <summary>T3: <paramref name="text"/> at the label's style fits the label's rect on one line.</summary>
        private static void AssertFits(TMP_Text label, string text, string what)
        {
            float width = label.rectTransform.rect.width;
            Assert.Greater(width, 1f, what + ": rect has a width");
            Assert.LessOrEqual(label.GetPreferredValues(text).x, width + 0.5f, what + " kesilmez (tercih edilen genişlik ≤ kutu)");
        }

        [Test]
        public void Adimlayici_EnUzunDeger_125te_AyarSatirindaKesilmez()
        {
            UiTokens.TextScale = 1.25f;
            UiStepper palette = UiStepper.Create("Palette", _root, "PALETTE", new[] { "DEFAULT", "COLOUR-BLIND SAFE" }, 1, true, null);
            ((RectTransform)palette.transform).sizeDelta = new Vector2(580f, UiSize.Target);
            // T3 by measurement (TMP does not lay out text without a canvas in EditMode, so
            // isTextTruncated would pass vacuously): the text's own preferred width fits its rect.
            AssertFits(palette.ValueLabel, "COLOUR-BLIND SAFE", "değer");
            AssertFits(palette.Label, "PALETTE", "etiket");
        }

        [Test]
        public void Cip_KelimesizReddedilir_GenislikMetinden_YukseklikOlcekle()
        {
            Assert.Throws<ArgumentException>(() => UiChips.Create("Bad", _root, UiChipKind.Neutral, ""));
            Assert.Throws<ArgumentException>(() => UiChips.Create("BadState", _root, UiChipKind.State, "NET", UiChipSize.Card, UiChipState.Status, null));
            Assert.AreEqual(UiColor.TextPrimary, UiChips.ColourOf(UiChipState.Status), "düşman durumu kremdir, uyarı kehribarı değil (AD 5a MUST-2)");
            Assert.AreEqual(UiColor.StateWarning, UiChips.ColourOf(UiChipState.Warning));
            Assert.AreEqual(UiColor.StateDanger, UiChips.ColourOf(UiChipState.Danger));

            UiTokens.TextScale = 1f;
            TMP_Text shortChip = UiChips.Create("Short", _root, UiChipKind.Value, "x8");
            TMP_Text longChip = UiChips.Create("Long", _root, UiChipKind.Value, "SURROUNDED BY THE CROWD");
            float shortWidth = ((RectTransform)shortChip.transform.parent).sizeDelta.x;
            float longWidth = ((RectTransform)longChip.transform.parent).sizeDelta.x;
            Assert.AreEqual(UiChips.MinWidth, shortWidth, 1e-3f, "asgari genişlik korunur");
            Assert.Greater(longWidth, shortWidth, "genişlik içerikten");
            AssertFits(longChip, "SURROUNDED BY THE CROWD", "çip metni");

            float h1 = ((RectTransform)shortChip.transform.parent).sizeDelta.y;
            UiTokens.TextScale = 1.25f;
            TMP_Text scaled = UiChips.Create("Scaled", _root, UiChipKind.Value, "LEVEL 3");
            Assert.Greater(((RectTransform)scaled.transform.parent).sizeDelta.y, h1, "yükseklik metin ölçeğiyle büyür");
            AssertFits(scaled, "LEVEL 3", "1,25 çip metni");
        }

        [Test]
        public void Bildirim_YukseklikMetinden_125teKesilmez()
        {
            const string text = "Another copy of the game is using this profile, so it is read-only here. Progress from this session will not be saved.";
            UiTokens.TextScale = 1f;
            RectTransform a = UiNotices.Notice("A", _root, UiNoticeKind.Warning, text, 640f);
            UiTokens.TextScale = 1.25f;
            RectTransform b = UiNotices.Notice("B", _root, UiNoticeKind.Warning, text, 640f);
            Assert.Greater(b.sizeDelta.y, a.sizeDelta.y, "1,25'te bildirim uzar");
            TMP_Text body = b.Find("Text").GetComponent<TMP_Text>();
            float bodyWidth = b.sizeDelta.x + body.rectTransform.offsetMax.x - body.rectTransform.offsetMin.x;
            float bodyHeight = b.sizeDelta.y + body.rectTransform.offsetMax.y - body.rectTransform.offsetMin.y;
            Assert.LessOrEqual(body.GetPreferredValues(text, bodyWidth, 0f).y, bodyHeight + 0.5f, "bildirim metni kutusuna sığar");
        }

        [Test]
        public void Modal_OdakTuzagi_GuvenliVarsayilan_IptalIkincil()
        {
            int primary = 0, secondary = 0;
            UiModal modal = UiModal.Create("EndRun", _root, UiModalSize.M, "END THIS RUN?", "Your run so far is recorded.",
                "END RUN", UiButtonVariant.Danger, () => primary++, "KEEP PLAYING", () => secondary++);
            Assert.IsFalse(modal.IsOpen);
            UnityEngine.UI.Navigation p = modal.Primary.navigation, s = modal.Secondary.navigation;
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, p.mode);
            Assert.AreSame(modal.Secondary, p.selectOnLeft);
            Assert.AreSame(modal.Secondary, p.selectOnRight);
            Assert.AreSame(modal.Primary, s.selectOnLeft);
            Assert.IsNull(p.selectOnUp, "modaldan yukarı kaçış yok");
            Assert.IsNull(p.selectOnDown, "modaldan aşağı kaçış yok");

            modal.Open();
            Assert.IsTrue(modal.IsOpen);
            Assert.IsTrue(modal.HandleCancel(), "iptal modalca alınır");
            Assert.AreEqual(1, secondary, "iptal = ikincil eylem");
            Assert.AreEqual(0, primary);
            Assert.IsFalse(modal.IsOpen, "iptal modalı kapatır");
            Assert.IsFalse(modal.HandleCancel(), "kapalı modal iptali almaz");
        }

        [Test]
        public void Onay_KucukBoy_YukseklikIcerikten_EnAz264()
        {
            UiModal shortOne = UiModal.Confirm("Short", _root, "END THIS RUN?", "The run cannot be resumed.", "END RUN", null, "KEEP PLAYING", null);
            Assert.AreEqual(UiModal.SizeOf(UiModalSize.S).x, shortOne.CardSize.x, 1e-3f, "S genişliği");
            Assert.AreEqual(264f, shortOne.CardSize.y, 1e-3f, "kısa gövde: S yüksekliği");
            UiModal longOne = UiModal.Confirm("Long", _root, "RESET ALL BINDINGS?",
                "Every action goes back to its default key and button: movement, aiming, firing, reloading, the Special Weapon, the camera and the menus. "
                + "Keyboard and gamepad are both reset. Your other settings - display, audio, accessibility and gameplay - stay as they are. "
                + "This cannot be undone from this screen; you can rebind any action again afterwards, one at a time.",
                "RESET", null, "KEEP BINDINGS", null);
            Assert.Greater(longOne.CardSize.y, 264f, "uzun gövde: kart içerikten uzar");
            Assert.IsFalse(longOne.IsOpen);

            // WP-081 (R1 AD SHOULD-9): the Elev2 soft shadow frames the card on every side and follows its height.
            float reach = UiElevationSpec.ShadowRadius(UiElevation.Elev2);
            foreach (UiModal modal in new[] { shortOne, longOne })
            {
                Assert.IsNotNull(modal.ShadowRect, "kartın gölgesi var");
                Assert.AreEqual(modal.CardSize.x + reach * 2f, modal.ShadowRect.sizeDelta.x, 1e-3f, "gölge kartı yanlardan çevreler");
                Assert.AreEqual(modal.CardSize.y + reach * 2f, modal.ShadowRect.sizeDelta.y, 1e-3f, "gölge kartın yüksekliğini izler");
                Assert.AreEqual(0, modal.ShadowRect.GetSiblingIndex(), "perdenin üstünde, kartın altında");
            }
        }

        [Test]
        public void Sayac_AzHareketteSonDeger_Zamanlayici()
        {
            UiCounter gold = UiCounter.Create("Gold", _root, UiCounterStyle.Currency, 10);
            Assert.AreEqual("10", gold.ValueText.text);
            gold.RollTo(40);
            Step(0.03f);
            int mid = int.Parse(gold.ValueText.text);
            Assert.Greater(mid, 10);
            Assert.Less(mid, 40);
            Step(1f);
            Assert.AreEqual("40", gold.ValueText.text);

            UiMotion.ReducedMotion = true;
            gold.RollTo(90);
            Assert.AreEqual("90", gold.ValueText.text, "RM: son değer anında (Delta)");

            UiCounter timer = UiCounter.Create("Timer", _root, UiCounterStyle.Timer, 65);
            Assert.AreEqual("1:05", timer.ValueText.text);
        }

        [Test]
        public void Halka_VeBoncuklar()
        {
            UiRing ring = UiRing.Create("Ring", _root, 64f, UiColor.BarReload);
            ring.Set(0.5f);
            Assert.AreEqual(0.5f, ring.Value, 1e-4f);
            ring.Set(0.5f + 0.5f / 256f);
            Assert.AreEqual(0.5f, ring.Value, 1e-4f, "görünmez değişim yazılmaz");

            UiPips pips = UiPips.Create("Pips", _root, 5, 2);
            Assert.AreEqual(2, pips.Filled);
            pips.Set(4, true);
            Assert.AreEqual(4, pips.Filled);
        }

        [Test]
        public void Belgeleme_HerBilesenKullanimYasakErisilebilirlik()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Kit", "Components");
            var missing = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs"))
            {
                string text = File.ReadAllText(file);
                foreach (string key in new[] { "USAGE:", "DON'T:", "ACCESSIBILITY:" })
                {
                    if (!text.Contains(key)) missing.Add(Path.GetFileName(file) + " " + key);
                }

                if (Regex.IsMatch(text, @"AddComponent<(UnityEngine\.UI\.)?(Outline|Shadow)>"))
                {
                    missing.Add(Path.GetFileName(file) + " uses a banned uGUI Outline/Shadow");
                }
            }

            Assert.IsEmpty(missing, "T7 / §3 kit kuralları:\n" + string.Join("\n", missing));
        }

        // ------------------------------------------------------------------ slice 5b

        [Test]
        public void T1_HerBilesenCiftleriniBeyanEder_IkiPalette()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Kit", "Components");
            var failures = new List<string>();
            var declaredNames = new HashSet<string>();
            foreach (Type t in UiKitContrast.Declared.Keys) declaredNames.Add(t.Name);
            foreach (string file in Directory.GetFiles(root, "*.cs"))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\b(?:public|internal)\s+(?:(?:sealed|static|partial|abstract)\s+)*class\s+(\w+)"))
                {
                    if (!declaredNames.Contains(m.Groups[1].Value)) failures.Add("beyansız bileşen: " + m.Groups[1].Value);
                }
            }

            UiPalette palette = UiTokens.Palette;
            try
            {
                foreach (UiPalette p in new[] { UiPalette.Default, UiPalette.ColorBlindSafe })
                {
                    UiTokens.Palette = p;
                    foreach (KeyValuePair<Type, UiContrastCase[]> entry in UiKitContrast.Declared)
                    {
                        foreach (UiContrastCase c in entry.Value)
                        {
                            float ratio = UiContrast.Measure(c);
                            if (ratio < c.Required) failures.Add(p + " " + entry.Key.Name + " · " + c.Description + " " + ratio.ToString("0.00") + " < " + c.Required);
                        }
                    }
                }
            }
            finally
            {
                UiTokens.Palette = palette;
            }

            Assert.IsEmpty(failures, "T1:\n" + string.Join("\n", failures));
        }

        private RectTransform NewCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            _created.Add(go);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(1920f, 1080f);
            return rect;
        }

        private static UiButton Place(UiButton button, Vector2 at, Vector2 size)
        {
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = at;
            return button;
        }

        [Test]
        public void OdakParantezleri_KontrolArasiUcar_AzHareketteAtlar()
        {
            RectTransform canvas = NewCanvas();
            UiButton a = Place(UiFactory.Button("A", canvas, "A", UiButtonVariant.Secondary, null), new Vector2(0f, 100f), new Vector2(400f, 48f));
            UiButton b = Place(UiFactory.Button("B", canvas, "B", UiButtonVariant.Secondary, null), new Vector2(0f, -100f), new Vector2(300f, 48f));
            UiFocusBrackets.HideNow();

            UiFocusBrackets.Follow((RectTransform)a.transform);
            RectTransform brackets = UiFocusBrackets.Rect;
            Assert.AreSame(a.transform, UiFocusBrackets.Target);
            Assert.AreEqual(100f, brackets.anchoredPosition.y, 0.5f, "ilk görünüş kesme (uçuş yok)");
            Assert.AreEqual(400f + UiFocusBrackets.Outset * 2f, brackets.sizeDelta.x, 0.5f);
            // WP-098A (KILAVUZ §1 C7, §7): cream arms on an ink halo, fully opaque on the cut frame - no fade-in.
            Assert.AreEqual(1f, UiFocusBrackets.Arms.canvasRenderer.GetAlpha(), 1e-4f, "kesmede krem kollar α 1 (ilk kare)");
            Assert.AreEqual(1f, UiFocusBrackets.Halo.canvasRenderer.GetAlpha(), 1e-4f, "kesmede mürekkep halesi α 1");
            Assert.AreEqual(UiTokens.Get(UiColor.TextPrimary), UiFocusBrackets.Arms.color, "kollar krem (TextPrimary), BorderFocus değil");
            Assert.AreEqual(UiTokens.Get(UiColor.BorderFocusHalo), UiFocusBrackets.Halo.color, "hale mürekkep (BorderFocusHalo)");

            UiFocusBrackets.Follow((RectTransform)b.transform);
            Step(0.07f);
            Assert.Less(brackets.anchoredPosition.y, 100f);
            Assert.Greater(brackets.anchoredPosition.y, -100f, "yolda");
            Step(0.2f);
            Assert.AreEqual(-100f, brackets.anchoredPosition.y, 0.5f, "vardı");
            Assert.AreEqual(300f + UiFocusBrackets.Outset * 2f, brackets.sizeDelta.x, 0.5f, "boyut da taşındı");

            UiMotion.ReducedMotion = true;
            UiFocusBrackets.Follow((RectTransform)a.transform);
            Assert.AreEqual(100f, brackets.anchoredPosition.y, 0.5f, "RM: anında");
            UiFocusBrackets.HideNow();
            Assert.IsNull(UiFocusBrackets.Target);
        }

        [Test]
        public void Tutma_ErkenBirakmaIptal_TamTutmaEylem_TiklamaHicbirSeyYapmaz()
        {
            int fired = 0;
            UiButton reroll = UiFactory.Button("Reroll", _root, "REROLL", UiButtonVariant.Secondary, () => fired++);
            reroll.ConfigureHold(0.5f);
            Assert.IsTrue(reroll.IsHoldButton);
            Assert.AreEqual(UiTokens.Get(UiColor.HoldFill), reroll.HoldFill.color, "tutma dolgusu HoldFill (krem 0,25; asla vermilyon)");

            reroll.OnPointerClick(null);
            Assert.AreEqual(0, fired, "tutma düğmesi tıklamayla çalışmaz");

            reroll.BeginHoldForTests(10f);
            Assert.AreEqual(UiHoldState.Running, reroll.TickHoldForTests(10.2f, true));
            Assert.AreEqual(UiHoldState.Cancelled, reroll.TickHoldForTests(10.3f, false), "erken bırakma");
            Assert.AreEqual(0, fired);

            reroll.BeginHoldForTests(20f);
            Assert.AreEqual(UiHoldState.Running, reroll.TickHoldForTests(20.49f, true));
            Assert.AreEqual(UiHoldState.Completed, reroll.TickHoldForTests(20.5f, true));
            Assert.AreEqual(1, fired, "tam tutma bir kez çalışır");
        }

        [Test]
        public void Hayalet_DinlenmedeYuzeySiz_DevreDisiNedenYazar()
        {
            UiButton ghost = UiFactory.Button("Back", _root, "BACK", UiButtonVariant.Ghost, null);
            Image fill = ghost.transform.Find("Fill").GetComponent<Image>();
            Image border = ghost.transform.Find("Border").GetComponent<Image>();
            Assert.AreEqual(0f, fill.canvasRenderer.GetColor().a, 1e-3f, "hayalet: dolgu yok");
            Assert.AreEqual(0f, border.canvasRenderer.GetColor().a, 1e-3f, "hayalet: kontur yok");
            Assert.AreSame(UiSprites.FocusRingHollow, ghost.transform.Find("FocusRing").GetComponent<Image>().sprite, "hayalet: odak halkası içi boş (dolgu merkezi örtmez)");
            Assert.AreSame(UiSprites.FocusHaloHollow, ghost.transform.Find("FocusHalo").GetComponent<Image>().sprite);
            Assert.AreEqual(UiTokens.Get(UiColor.TextPrimary), ghost.LabelText.color, "hayalet kelimesi TextPrimary (TextSecondary devre dışı rengi)");
            ghost.SetInteractable(false);
            Assert.AreEqual(UiTokens.Get(UiColor.TextMuted), ghost.LabelText.color, "devre dışı hayalet TextMuted");

            UiButton buy = UiFactory.Button("Buy", _root, "BUY", UiButtonVariant.Secondary, null);
            buy.SetDisabledReason("NOT ENOUGH GOLD");
            Assert.AreEqual(string.Empty, buy.DisabledReasonShown, "etkinken neden gösterilmez");
            buy.SetInteractable(false);
            Assert.AreEqual("NOT ENOUGH GOLD", buy.DisabledReasonShown);
            buy.SetInteractable(true);
            Assert.AreEqual(string.Empty, buy.DisabledReasonShown);
        }

        [Test]
        public void Kaydirici_AdimEtiketiAnindaBoncukTasinir_Surukleme1e1_UctaDurur()
        {
            float reported = -1f;
            UiSlider slider = UiSlider.Create("Master", _root, "MASTER VOLUME", 0f, 1f, 0.5f, 0.1f, null, v => reported = v);
            Assert.AreEqual("50%", slider.ValueText);
            float width = slider.RailWidth;
            Assert.Greater(width, 100f);
            Assert.AreEqual(0.5f * width, slider.BeadX, 0.5f);

            slider.Step(+1);
            Assert.AreEqual(0.6f, slider.Value, 1e-4f);
            Assert.AreEqual("60%", slider.ValueText, "etiket anında");
            Assert.AreEqual(0.6f, reported, 1e-4f);
            Assert.Less(slider.BeadX, 0.6f * width - 0.5f, "boncuk henüz yolda");
            Step(0.2f);
            Assert.AreEqual(0.6f * width, slider.BeadX, 0.5f);

            slider.DragToNormalisedForTests(0.23f);
            Assert.AreEqual(0.2f, slider.Value, 1e-4f, "adıma yuvarlanır");
            Assert.AreEqual(0.2f * width, slider.BeadX, 0.5f, "sürükleme 1:1, yumuşatma yok");

            slider.SetValue(1f, false);
            slider.Step(+1);
            Assert.AreEqual(1f, slider.Value, 1e-4f, "uçta durur");
        }

        [Test]
        public void Cubuk_Hayalet_Tutar_SonraDuser_KazancHayaletBirakmaz()
        {
            UiBar bar = UiFactory.Bar("Health", _root, UiColor.BarHealth, 18f);
            bar.EnableGhost();
            bar.EnableSegments();
            bar.SetValue(1f, true);
            Assert.AreEqual(1f, bar.GhostValue, 1e-4f);

            bar.SetValue(0.4f);
            Step(0.2f);
            Assert.AreEqual(1f, bar.GhostValue, 1e-3f, "350 ms tutar");
            Step(0.8f);
            Assert.AreEqual(0.4f, bar.GhostValue, 1e-3f, "sonra değere düşer");

            bar.SetValue(0.9f);
            Assert.AreEqual(0.9f, bar.GhostValue, 1e-4f, "kazanç hayalet bırakmaz");

            UiMotion.ReducedMotion = true;
            bar.SetValue(0.5f);
            Step(0.2f);
            Assert.AreEqual(0.9f, bar.GhostValue, 1e-3f, "RM: tutma sırasında durağan görünür");
            Step(0.3f);
            Assert.AreEqual(0.5f, bar.GhostValue, 1e-3f, "RM: tutmadan sonra kalkar");
        }

        [Test]
        public void Kart_OdaktaKalkar_KilitliKalkmaz()
        {
            UiCard card = UiCard.Create("Weapon", _root, new Vector2(420f, 160f));
            Assert.AreEqual(UiTokens.Get(UiColor.SurfacePanel), card.FillColour, "dinlenme dolgusu");
            card.SetLifted(true);
            Step(0.2f);
            Assert.IsTrue(card.Lifted);
            Assert.AreEqual(UiCard.LiftPx, card.Body.anchoredPosition.y, 0.1f);
            Assert.AreEqual(UiTokens.Get(UiColor.SurfaceRaised), card.FillColour, "kalkınca dolgu SurfaceRaised (AD 5b MUST-5)");
            Assert.IsNotNull(card.Body.Find("Sheen"), "Elev1 kabartması: üst parlaklık çizgisi");
            Assert.IsNotNull(card.Body.Find("Base"), "Elev1 kabartması: alt mürekkep çizgisi");

            card.SetLocked(true);
            Assert.IsFalse(card.Lifted, "kilitli kart iner");
            Step(0.2f);
            Assert.AreEqual(0f, card.Body.anchoredPosition.y, 0.1f);
            card.SetLifted(true);
            Assert.IsFalse(card.Lifted, "kilitli kart kalkmaz");

            UiButton surface = UiFactory.Button("Surface", card.Content, "SMG", UiButtonVariant.Card, null);
            UiCard other = UiCard.Create("Other", _root, new Vector2(420f, 160f));
            other.Bind(surface);
            surface.OnSelect(new UnityEngine.EventSystems.BaseEventData(null));
            Assert.IsTrue(other.Lifted, "bağlı düğme odakta: kart kalkar");
            surface.OnDeselect(new UnityEngine.EventSystems.BaseEventData(null));
            Assert.IsFalse(other.Lifted);
        }

        // ------------------------------------------------------------------ slice 6

        [Test]
        public void Kaydirma_OdaklananSatirGorunur_UclarKelepceli_AzHareketteAninda()
        {
            UiScrollView view = UiScrollView.Create("Scroll", _root);
            var rect = (RectTransform)view.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(600f, 300f);
            var stack = new UiStack(view.Content);
            var rows = new List<RectTransform>();
            for (int i = 0; i < 10; i++)
            {
                rows.Add(stack.Add(UiFactory.NewRect("Row" + i, view.Content), 56f));
            }

            view.SetContentHeight(stack.Cursor);
            Assert.IsTrue(view.Scrolls, "560 px içerik 300 px görünümde kayar");
            Assert.IsFalse(view.FadeTopVisible, "en üstte üst rampa yok");
            Assert.IsTrue(view.FadeBottomVisible, "altta içerik var: alt rampa");
            Assert.AreEqual(600f - UiScrollView.Gutter, view.Content.rect.width, 0.5f, "ray payı her zaman ayrılır");

            view.Reveal(rows[9], false);
            Assert.AreEqual(560f - 300f, view.Offset, 0.5f, "son satır: en alta, sınırda durur");
            Assert.IsTrue(view.FadeTopVisible, "yukarıda içerik var: üst rampa");
            Assert.IsFalse(view.FadeBottomVisible, "en altta alt rampa yok");
            view.Reveal(rows[0], false);
            Assert.AreEqual(0f, view.Offset, 0.5f, "ilk satır: en üste");

            view.Reveal(rows[5], true);   // 280..336 → 336 + 16 − 300
            Assert.AreEqual(0f, view.Offset, 0.5f, "animasyon t0'da başlar");
            Step(0.2f);
            Assert.AreEqual(336f + UiScrollView.RevealMargin - 300f, view.Offset, 0.5f, "en kısa yol + pay");
            view.Reveal(rows[4], true);   // 224..280 zaten görünür
            Step(0.2f);
            Assert.AreEqual(52f, view.Offset, 0.5f, "görünen satır kaydırmaz");

            view.Reveal(rows[8], false);  // 448..504: only 56 px left below → to the end (a caption must not stay hidden)
            Assert.AreEqual(560f - 300f, view.Offset, 0.5f, "kuyruk ≤ 96 px: sona kadar");
            view.Reveal(rows[1], false);  // 56..112: the top is ≤ 96 px away → to the top (its section head too)
            Assert.AreEqual(0f, view.Offset, 0.5f, "üste ≤ 96 px: en üste");

            view.Reveal(rows[9], false);
            UiMotion.ReducedMotion = true;
            view.Reveal(rows[0], true);
            Assert.AreEqual(0f, view.Offset, 0.5f, "RM: kaydırma anında");

            view.SetContentHeight(200f);
            Assert.IsFalse(view.Scrolls, "kısa içerik kaymaz");
        }

        /// <summary>
        /// 12b (AD R1-A SHOULD-7): LOOK SENSITIVITY 1–20 is a ruler - 1 px at every unit, 2 px at
        /// 5 / 10 / 15 (the health strong-tick rule); a slider with few stops (UI SCALE's six)
        /// keeps its 2 px detents (AD 5b MUST-3).
        /// </summary>
        [Test]
        public void Kaydirici_Cetvel_BirimTik1px_Her5te2px_AzDuraktaHep2px()
        {
            UiSlider look = UiSlider.Create("Look", _root, "LOOK SENSITIVITY", 1f, 20f, 10f, 1f, v => Mathf.RoundToInt(v).ToString(), null);
            int strong = 0;
            for (int i = 1; i < 19; i++)
            {
                int value = 1 + i;
                Transform tick = look.transform.Find("Rail/Tick" + i);
                Assert.IsNotNull(tick, "birim tiki " + value);
                float width = ((RectTransform)tick).sizeDelta.x;
                bool major = value % UiSlider.MajorTickEvery == 0;
                Assert.AreEqual(major ? UiSlider.MajorTickWidth : UiSlider.MinorTickWidth, width, 1e-4f, "tik " + value);
                if (major) strong++;
            }

            Assert.AreEqual(3, strong, "5 / 10 / 15 güçlü");
            Assert.AreEqual(1f, UiSlider.MinorTickWidth, 1e-4f);
            Assert.AreEqual(2f, UiSlider.MajorTickWidth, 1e-4f);

            UiSlider scale = UiSlider.Create("Scale", _root, "UI SCALE", 0.9f, 1.2f, 1f, 0.05f, null, null);
            for (int i = 1; i < 6; i++)
            {
                Transform tick = scale.transform.Find("Rail/Tick" + i);
                Assert.IsNotNull(tick, "UI SCALE durağı " + i);
                Assert.AreEqual(UiSlider.MajorTickWidth, ((RectTransform)tick).sizeDelta.x, 1e-4f, "az duraklı kaydırıcı 2 px");
            }
        }

        [Test]
        public void Kaydirici_EsitsizDuraklar_EnYakinaOturur_OkDuraktanDuraga_IcDuraklaraTik()
        {
            float reported = -1f;
            UiSlider slider = UiSlider.Create("TextScale", _root, "TEXT SCALE", 1f, 1.25f, 1.02f, 0f, null, v => reported = v);
            slider.SetStops(1f, 1.1f, 1.25f);
            Assert.AreEqual(1f, slider.Value, 1e-4f, "değer en yakın durağa oturur");
            Assert.IsNotNull(slider.transform.Find("Rail/Tick1"), "iç durak 1,10'a tik");
            Assert.IsNull(slider.transform.Find("Rail/Tick0"), "uç duraklara tik yok");

            slider.Step(+1);
            Assert.AreEqual(1.1f, slider.Value, 1e-4f, "→ bir sonraki durak");
            Assert.AreEqual(1.1f, reported, 1e-4f, "bildirildi");
            slider.Step(+1);
            Assert.AreEqual(1.25f, slider.Value, 1e-4f);
            slider.Step(+1);
            Assert.AreEqual(1.25f, slider.Value, 1e-4f, "uçta durur");
            slider.DragToNormalisedForTests(0.3f);   // 1.075 → the nearest stop is 1.10
            Assert.AreEqual(1.1f, slider.Value, 1e-4f, "sürükleme de durağa oturur");
        }
    }
}
