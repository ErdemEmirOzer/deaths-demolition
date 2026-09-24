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
    }
}
