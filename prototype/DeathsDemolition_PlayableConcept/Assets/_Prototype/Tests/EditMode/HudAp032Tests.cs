using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-094 · AP-032 v001 in the HUD (P-07 → S-09, AD ACCEPT 08.10; K/wp-094/AP-032-034-SPEK.md §1, §3, §7), no scene:
    /// - the koban is bound by the §1 layer rule (64 at 1080, 128 on the Mac), the Roman atlases likewise (512 / 1024), the
    ///   smear plates carry their 3-slice borders from smear.json;
    /// - the theme keys Roman.I … Roman.C and Roman.Seal resolve to AP-032's cells;
    /// - HudRoundNumeral's tables ARE the metrics file (UI/Art/AP032/ap032_roman_metrics.json, the delivery's copy): advance,
    ///   origin, bearing and all 25 kerning pairs; I / IV / IX / XIV / XXIII measure from the file; the glyph path draws
    ///   cells - no TMP - with the alternates (round + position) mod n;
    /// - the plate's W' rule: whole periods, the dense zone holds the run and the seal.
    /// </summary>
    [TestFixture]
    [Category("WP094")]
    public sealed class HudAp032Tests
    {
        private static readonly int[] Rounds = { 1, 4, 9, 14, 23 };
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private static string MetricsJson
        {
            get { return File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype", "UI", "Art", "AP032", "ap032_roman_metrics.json")); }
        }

        private static float Number(string text, string pattern)
        {
            Match m = Regex.Match(text, pattern, RegexOptions.Singleline);
            Assert.IsTrue(m.Success, "metrics: " + pattern);
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>The glyph's own block in the metrics ("\"I\": { … \"alternates\"") - its first-level numbers.</summary>
        private static string GlyphBlock(string json, char letter)
        {
            Match m = Regex.Match(json, "\"" + letter + "\"\\s*:\\s*\\{(.*?)\"alternates\"", RegexOptions.Singleline);
            Assert.IsTrue(m.Success, "metrics glyph " + letter);
            return m.Groups[1].Value;
        }

        // ------------------------------------------------------------------ files and layers

        [Test]
        public void Altin_KatmanKurali_64Ve128_SpriteBagli()
        {
            Sprite small = HudArt.GoldCoin(40f);
            Sprite large = HudArt.GoldCoin(40f * 1.89f);
            Assert.IsNotNull(small, "ap032_gold_64_base in UI/Hud");
            Assert.IsNotNull(large, "ap032_gold_128_base in UI/Hud");
            Assert.AreEqual(HudArt.GoldSmall, small.name, "40 px at 1080 → the 64 canvas (body 48 ≥ 40)");
            Assert.AreEqual(64f, small.rect.width);
            Assert.AreEqual(HudArt.GoldLarge, large.name, "75.6 px on the Mac at UI 1.2 → the 128 canvas (body 96)");
            Assert.AreEqual(128f, large.rect.width);
            Assert.AreEqual(HudArt.GoldLarge, HudArt.GoldCoin(48.5f).name, "just past the small body: the next layer up");
            Assert.LessOrEqual(HudArt.GoldBodySmall / 40f, 2f, "≤ 2× down at 1080 (§1)");
        }

        [Test]
        public void Roma_AtlasKatmani_HucreAdlari_TemaAnahtarlari()
        {
            Assert.IsTrue(HudArt.RomanAuthored, "both Roman atlases are in UI/Hud");
            Sprite[] small = HudArt.RomanAlternates(2, 44f, null);
            Sprite[] large = HudArt.RomanAlternates(2, 44f * 1.89f, null);
            Assert.AreEqual(512, small[0].texture.width, "a 44 px cap at 1080 → the 512 atlas (cap 80)");
            Assert.AreEqual(1024, large[0].texture.width, "83 px on the Mac → the 1024 atlas (cap 160)");
            CollectionAssert.AreEqual(new[] { 3, 2, 3, 2, 2 }, HudArt.RomanAlternateCount, "I×3 V×2 X×3 L×2 C×2");
            string[] names = { "Roman.X_a", "Roman.X_b", "Roman.X_c" };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(names[i], small[i].name);
                Assert.AreEqual(128f, small[i].rect.width, "a 128 cell");
            }

            // The metrics' cell rects (Unity bottom-left): I_a (0, 384), V_b (0, 256), the seal (0, 0) in the 512 atlas.
            Assert.AreEqual(new Rect(0f, 384f, 128f, 128f), HudArt.RomanAlternates(0, 44f, null)[0].rect, "I_a");
            Assert.AreEqual(new Rect(0f, 256f, 128f, 128f), HudArt.RomanAlternates(1, 44f, null)[1].rect, "V_b");
            Assert.AreEqual(new Rect(0f, 0f, 128f, 128f), HudArt.RomanSeal(44f).rect, "the seal");

            UiTheme theme = UiTheme.Active;
            Assert.IsNotNull(theme, "the theme");
            foreach (string key in HudRoundNumeral.GlyphKeys)
            {
                Sprite sprite = theme.Icon(key);
                Assert.IsNotNull(sprite, key + " resolves");
                StringAssert.StartsWith(key + "_a", sprite.name, key + " → its first alternate");
            }

            Assert.AreEqual("Roman.Seal", theme.Icon(RunHudScreen.RoundSealKey).name);
            Assert.IsNull(HudArt.Icon("Roman.D"), "no D (SPEK §11)");
            Assert.IsNull(HudArt.Icon("Hud.Gold"), "HudArt answers only the Roman keys");
        }

        [Test]
        public void Surtme_UcDilim_KenarlarJsondan()
        {
            float native;
            Sprite small = HudArt.Smear(44f / HudArt.SmearCapRatio, out native);
            Assert.IsNotNull(small, "ap032_smear_512x128_mask");
            Assert.AreEqual(128f, native);
            Assert.AreEqual(new Vector4(96f, 0f, 160f, 0f), small.border, "[96,0,160,0] (smear.json, L B R T)");
            Sprite large = HudArt.Smear(44f / HudArt.SmearCapRatio * 1.89f, out native);
            Assert.AreEqual(256f, native);
            Assert.AreEqual(new Vector4(192f, 0f, 320f, 0f), large.border, "[192,0,320,0]");
        }

        // ------------------------------------------------------------------ metrics

        [Test]
        public void Metrikler_TablolarDosyaIleAyni_GenisliklerDosyadan()
        {
            string json = MetricsJson;
            for (int kind = 0; kind < 5; kind++)
            {
                char letter = HudRoundNumeral.Letters[kind];
                string block = GlyphBlock(json, letter);
                Assert.AreEqual(Number(block, "\"advance_em\":\\s*([-0-9.]+)"), HudRoundNumeral.Advance[kind], 1e-4f, letter + " advance");
                Assert.AreEqual(Number(block, "\"originX_em\":\\s*([-0-9.]+)"), HudRoundNumeral.OriginX[kind], 1e-4f, letter + " originX");
                Assert.AreEqual(Number(block, "\"lsb_em\":\\s*([-0-9.]+)"), HudRoundNumeral.LeftBearing[kind], 1e-4f, letter + " lsb");
            }

            string table = Regex.Match(json, "\"kerning_table_em\"\\s*:\\s*\\{(.*?)\\}", RegexOptions.Singleline).Groups[1].Value;
            Assert.IsNotEmpty(table, "kerning_table_em");
            for (int a = 0; a < 5; a++)
            {
                for (int b = 0; b < 5; b++)
                {
                    string pair = HudRoundNumeral.Letters[a].ToString() + HudRoundNumeral.Letters[b];
                    Assert.AreEqual(Number(table, "\"" + pair + "\":\\s*([-0-9.]+)"), HudRoundNumeral.Kerning[a * 5 + b], 1e-4f, pair);
                }
            }

            // I / IV / IX / XIV / XXIII: the widths from the file (advances + pair kerning) = the numeral's own measure.
            var letters = new char[16];
            foreach (int round in Rounds)
            {
                int n = RomanNumerals.Write(round, letters, 0);
                float fromFile = 0f;
                for (int i = 0; i < n; i++)
                {
                    fromFile += Number(GlyphBlock(json, letters[i]), "\"advance_em\":\\s*([-0-9.]+)");
                    if (i > 0)
                    {
                        string pair = letters[i - 1].ToString() + letters[i];
                        fromFile += Number(table, "\"" + pair + "\":\\s*([-0-9.]+)");
                    }
                }

                Assert.AreEqual(fromFile * 44f, HudRoundNumeral.MeasureGlyphs(letters, n, 44f), 0.01f, RomanNumerals.Cached(round));
            }
        }

        [Test]
        public void GlifYolu_TmpsizHucreler_AlternatifSirasi_GenislikMetrikten()
        {
            var host = new GameObject("Round", typeof(RectTransform));
            _created.Add(host);
            RectTransform box = UiFactory.NewRect("Numeral", (RectTransform)host.transform);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.sizeDelta = new Vector2(0f, 56f);
            TMP_Text text = UiFactory.Label("RoundValue", box, "—", UiType.H1, UiColor.TextPrimary, TextAnchor.MiddleRight);
            var numeral = new HudRoundNumeral(box, text, 44f, UiColor.TextPrimary);
            var alternates = new Sprite[5][];
            for (int kind = 0; kind < 5; kind++)
            {
                alternates[kind] = HudArt.RomanAlternates(kind, 44f, null);
            }

            numeral.SetGlyphAlternates(alternates);
            var letters = new char[16];
            foreach (int round in Rounds)
            {
                numeral.Set(round);
                int n = RomanNumerals.Write(round, letters, 0);
                Assert.IsTrue(numeral.DrawsGlyphs, RomanNumerals.Cached(round) + ": the brush cells draw it");
                Assert.IsFalse(text.enabled, "no TMP on the glyph path");
                Assert.AreEqual(RomanNumerals.Cached(round), text.text, "the line still carries the numeral (readers)");
                Assert.AreEqual(n, numeral.GlyphCount);
                Assert.AreEqual(Mathf.Ceil(HudRoundNumeral.MeasureGlyphs(letters, n, 44f)), numeral.Width, 0.01f, "the box is the metrics' width");
            }

            // XIV: X at 0 → (14 + 0) mod 3 = c, I at 1 → (15) mod 3 = a, V at 2 → (16) mod 2 = a.
            numeral.Set(14);
            Assert.AreEqual("Roman.X_c", Glyph(box, 0).sprite.name);
            Assert.AreEqual("Roman.I_a", Glyph(box, 1).sprite.name);
            Assert.AreEqual("Roman.V_a", Glyph(box, 2).sprite.name);

            // III never repeats a cell side by side: (3 + 0, 3 + 1, 3 + 2) mod 3 = a / b / c... in order.
            numeral.Set(3);
            Assert.AreNotEqual(Glyph(box, 0).sprite.name, Glyph(box, 1).sprite.name);
            Assert.AreNotEqual(Glyph(box, 1).sprite.name, Glyph(box, 2).sprite.name);

            // The cell: 1.6 em square, its left edge at pen − originX.
            numeral.Set(1);
            RectTransform cell = Glyph(box, 0).rectTransform;
            Assert.AreEqual(1.6f * 44f, cell.sizeDelta.x, 1e-3f);
            Assert.AreEqual(-HudRoundNumeral.OriginX[0] * 44f + 0.8f * 44f, cell.anchoredPosition.x, 1e-3f);
            Assert.AreEqual(0f, cell.anchoredPosition.y, 1e-3f, "the cap centred in the box (baseline 0.3 em over the cell's bottom)");
        }

        private static Image Glyph(RectTransform box, int index)
        {
            return box.Find("Glyph." + index).GetComponent<Image>();
        }

        // ------------------------------------------------------------------ the plate's W' rule

        [Test]
        public void Plaka_WUstKurali_TamDonem_YogunBolgeDiziVeMuhuruTasir()
        {
            var letters = new char[16];
            foreach (int round in new[] { 1, 4, 9, 14, 23, 38 })
            {
                int n = RomanNumerals.Write(round, letters, 0);
                float run = HudRoundNumeral.MeasureGlyphs(letters, n, 44f);
                float lsb = HudRoundNumeral.LeftBearing[HudRoundNumeral.KindOf(letters[0])];
                float width, height, runRight, sealCentre, seal;
                HudArt.LaySmear(44f, run, lsb, out width, out height, out runRight, out sealCentre, out seal);
                float texel = height / HudArt.SmearNativeHeight;
                float texels = width / texel;
                string name = RomanNumerals.Cached(round);
                Assert.AreEqual(44f / HudArt.SmearCapRatio, height, 1e-3f, name + ": cap / height");
                Assert.AreEqual(0f, Mathf.Repeat(texels - HudArt.SmearHead - HudArt.SmearTail, HudArt.SmearPeriod), 0.01f, name + ": whole periods (no seam)");
                float inkStart = width - runRight - run + lsb * 44f;
                Assert.GreaterOrEqual(inkStart, HudArt.SmearDenseLeft * texel - 0.01f, name + ": the first ink inside the dense zone");
                Assert.AreEqual((HudArt.SmearNativeWidth - HudArt.SmearDenseRight) * texel + HudArt.SealFromDenseRight * seal, sealCentre, 1e-3f, name + ": the seal's place");
                Assert.AreEqual(HudArt.SealEm * 44f, seal, 1e-3f, name + ": the seal 0.45 em");
                Assert.AreEqual(sealCentre + seal * 0.5f + HudArt.SealGapEm * 44f, runRight, 1e-3f, name + ": 0.22 em to the seal");
            }
        }
    }
}
