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
                Assert.AreEqual(HudArt.SealEm * 44f, seal, 1e-3f, name + ": the seal 0.60 em (AD MUST-2, S-09 08.10)");
                Assert.AreEqual(sealCentre + seal * 0.5f + HudArt.SealGap(44f), runRight, 1e-3f, name + ": 0.22 em to the seal");
                Assert.AreEqual(HudArt.SealGapEm * 44f, HudArt.SealGap(44f), 1e-4f, name + ": at cap 44 the 0.22 em rules (9.68 ≥ 9 px)");
            }
        }

        /// <summary>
        /// 08.10 (S-09, plate fit): the plate is at most <see cref="HudArt.PlateMaxWidth"/> (288, inside S-09's 320 ceiling); a
        /// numeral that would pass it shrinks the cap in proportion, floor <see cref="HudArt.CapFloor"/> 32. I and XIV keep the
        /// HUD's cap 40; XXXVIII (the longest under 40) and LXXXVIII (the longest under 100) fit at a smaller cap; no round
        /// 1…100 lays a plate past the ceiling. r3-w2 (08.10): the plate holds seal + gap + numeral with the gap ≥ 9 px, so the
        /// numeral's clip box (8 px) never reaches the ring at a fitted cap (0.22 em is 7.3 px at XXXVIII's 33).
        /// </summary>
        [Test]
        public void Plaka_Sigdirma_EnCok288_UzunRakamCapiKuculur_Taban32()
        {
            const float cap = RunHudScreen.RoundGlyphHeight;
            Assert.AreEqual(40f, cap, "the round's cap 44 → 40 (coverage lever, 08.10)");
            Assert.LessOrEqual(HudArt.PlateMaxWidth, HudArt.PlateCeiling, "S-09: the plate ≤ 320 ref px");
            float lsb;
            float em = HudRoundNumeral.EmWidth(1, out lsb);
            Assert.AreEqual(cap, HudArt.FitCap(cap, em, lsb), 1e-4f, "I keeps the cap");
            em = HudRoundNumeral.EmWidth(14, out lsb);
            Assert.AreEqual(cap, HudArt.FitCap(cap, em, lsb), 1e-4f, "XIV keeps the cap");
            em = HudRoundNumeral.EmWidth(38, out lsb);
            float fitted = HudArt.FitCap(cap, em, lsb);
            Assert.Less(fitted, cap, "XXXVIII shrinks the cap");
            Assert.GreaterOrEqual(fitted, HudArt.CapFloor, "never under 32 px");
            Assert.LessOrEqual(fitted, 34f, "XXXVIII: about 33 px");
            Assert.AreEqual(HudArt.CapFloor, HudArt.FitCap(cap, 40f, 0f), 1e-4f, "an absurd run stops at the floor");

            float worst = 0f;
            for (int round = 1; round <= 100; round++)
            {
                em = HudRoundNumeral.EmWidth(round, out lsb);
                fitted = HudArt.FitCap(cap, em, lsb);
                float width, height, runRight, sealCentre, seal;
                HudArt.LaySmear(fitted, em * fitted, lsb, out width, out height, out runRight, out sealCentre, out seal);
                Assert.LessOrEqual(width, HudArt.PlateMaxWidth + 0.01f, RomanNumerals.Cached(round) + ": the plate ≤ 288");
                // r3-w2 (08.10): the numeral never touches the seal - its clip box (8 px round the letters) ends before the ring.
                Assert.Greater(runRight - HudArt.NumeralClipPad, sealCentre + seal * 0.5f + 0.5f,
                    RomanNumerals.Cached(round) + ": the letters' clip box clear of the seal ring at cap " + fitted);
                Assert.GreaterOrEqual(runRight - (sealCentre + seal * 0.5f), HudArt.SealGapMinPx - 1e-3f, RomanNumerals.Cached(round) + ": the gap ≥ 9 px");
                Assert.AreEqual(fitted / HudArt.SmearCapRatio, height, 1e-3f, RomanNumerals.Cached(round) + ": height follows the cap");
                worst = Mathf.Max(worst, width);
            }

            Debug.Log("[DD-TEST] wp094.plateFit worst=" + worst.ToString("F1", CultureInfo.InvariantCulture) + " xxxviiiCap="
                + HudArt.FitCap(cap, HudRoundNumeral.EmWidth(38, out lsb), lsb).ToString("F2", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ 08.10 AD MUST-1 / MUST-2

        private static Texture2D LoadAtlas1024()
        {
            string path = Path.Combine(Application.dataPath, "_Prototype", "UI", "Art", "Resources", "UI", "Hud", HudArt.RomanLarge + ".png");
            Assert.IsTrue(File.Exists(path), path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), "the 1024 atlas decodes");
            return texture;
        }

        /// <summary>A 256 cell's alpha at (x, y from the cell's TOP-left) - the atlas is read bottom-up.</summary>
        private static float CellAlpha(Color32[] pixels, int width, int cell, int x, int y)
        {
            int row = cell / 4, col = cell % 4;
            int px = col * 256 + x, py = width - 1 - (row * 256 + y);
            return pixels[py * width + px].a / 255f;
        }

        /// <summary>
        /// AD MUST-2 (08.10): the seal reads as a hanko, not an "o" - 0.60 em across (24 ref px at cap 40; S-09 08.10: 0.55 left the ring under its floor), its ring ≥ 0.08 em
        /// thick. The ring is AP-032's own (the atlas' seal cell, measured here): its outer diameter is SealRingShareOfCell of the
        /// cell and its stroke, at its chipped thinnest over 24 rays, keeps the floor at 0.60 em; the plate still fits ≤ 288
        /// with cap 40 kept through XXIII.
        /// </summary>
        [Test]
        public void Muhur_060em_HalkaKalinligi008emUstu_AtlastanOlculur_Plaka288()
        {
            Assert.AreEqual(0.60f, HudArt.SealEm, 1e-6f, "MUST-2: 0.60 em (S-09 08.10: the 0.08 em ring floor wins over 0.55)");
            Texture2D atlas = LoadAtlas1024();
            try
            {
                Color32[] pixels = atlas.GetPixels32();
                int w = atlas.width;
                int minX = 256, maxX = -1, minY = 256, maxY = -1;
                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        if (CellAlpha(pixels, w, HudArt.RomanSealCell, x, y) > 0.5f)
                        {
                            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                        }
                    }
                }

                float outer = 0.5f * ((maxX - minX + 1) + (maxY - minY + 1));
                Assert.AreEqual(HudArt.SealRingShareOfCell * 256f, outer, 3f, "the ring's outer diameter is SealRingShareOfCell of the cell");
                float cx = 0.5f * (minX + maxX + 1), cy = 0.5f * (minY + maxY + 1);
                float thinnest = float.MaxValue, sum = 0f;
                for (int ray = 0; ray < 24; ray++)
                {
                    float angle = ray * Mathf.PI / 12f, inside = 0f;
                    for (float r = 0f; r < outer * 0.6f; r += 0.25f)
                    {
                        int x = Mathf.FloorToInt(cx + r * Mathf.Cos(angle)), y = Mathf.FloorToInt(cy + r * Mathf.Sin(angle));
                        if (x >= 0 && x < 256 && y >= 0 && y < 256 && CellAlpha(pixels, w, HudArt.RomanSealCell, x, y) > 0.5f) inside += 0.25f;
                    }

                    thinnest = Mathf.Min(thinnest, inside);
                    sum += inside;
                }

                float thinEm = thinnest / outer * HudArt.SealEm, meanEm = sum / 24f / outer * HudArt.SealEm;
                Assert.GreaterOrEqual(thinEm, 0.08f, "MUST-2: the ring ≥ 0.08 em at its thinnest (" + thinnest + " of " + outer + " px)");
                Assert.GreaterOrEqual(meanEm * RunHudScreen.RoundGlyphHeight, 3f, "≥ 3 px at 1080 (cap 40)");
                Assert.AreEqual(24f, HudArt.SealEm * RunHudScreen.RoundGlyphHeight, 1e-3f, "24 ref px at cap 40");

                var letters = new char[16];
                foreach (int round in new[] { 1, 4, 9, 14, 23, 38, 88 })
                {
                    float lsb;
                    float em = HudRoundNumeral.EmWidth(round, out lsb);
                    float cap = HudArt.FitCap(RunHudScreen.RoundGlyphHeight, em, lsb);
                    float width, height, runRight, sealCentre, seal;
                    HudArt.LaySmear(cap, em * cap, lsb, out width, out height, out runRight, out sealCentre, out seal);
                    Assert.LessOrEqual(width, HudArt.PlateMaxWidth + 0.01f, round + ": ≤ 288");
                    Assert.GreaterOrEqual(runRight - (sealCentre + seal * 0.5f), HudArt.SealGapMinPx - 1e-3f, round + ": the gap ≥ 9 px");
                    if (round <= 23)
                    {
                        Assert.AreEqual(RunHudScreen.RoundGlyphHeight, cap, 1e-4f, round + ": cap 40 kept through XXIII");
                    }

                    Debug.Log("[DD-TEST] wp094.seal round=" + RomanNumerals.Cached(round) + " cap=" + cap.ToString("0.00", CultureInfo.InvariantCulture)
                        + " plate=" + width.ToString("0.0", CultureInfo.InvariantCulture) + " seal=" + seal.ToString("0.0", CultureInfo.InvariantCulture)
                        + " stroke=" + (meanEm * cap).ToString("0.0", CultureInfo.InvariantCulture) + "px thinEm=" + thinEm.ToString("0.000", CultureInfo.InvariantCulture));
                }
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }

        /// <summary>
        /// AD MUST-1 (08.10): the letters' ink extents the core is laid to ARE the atlas' - per letter, the widest of its
        /// alternates: the top over the baseline, the foot under it and the reach past the advance (α &gt; 0.1, 160 px per em,
        /// the baseline 48 px over the cell's bottom). A table under the measure would leave ink off the core.
        /// </summary>
        [Test]
        public void MurekkepBandi_TablolarAtlasinMurekkebiniKapsar()
        {
            Texture2D atlas = LoadAtlas1024();
            try
            {
                Color32[] pixels = atlas.GetPixels32();
                int w = atlas.width;
                int[] kinds = { 0, 0, 0, 1, 1, 2, 2, 2, 3, 3, 4, 4 };
                var top = new float[5];
                var below = new float[5];
                var past = new float[5];
                for (int cell = 0; cell < 12; cell++)
                {
                    int kind = kinds[cell];
                    int minY = 256, maxY = -1, maxX = -1;
                    for (int y = 0; y < 256; y++)
                    {
                        for (int x = 0; x < 256; x++)
                        {
                            if (CellAlpha(pixels, w, cell, x, y) > 0.1f)
                            {
                                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); maxX = Mathf.Max(maxX, x);
                            }
                        }
                    }

                    top[kind] = Mathf.Max(top[kind], (208f - minY) / 160f);
                    below[kind] = Mathf.Max(below[kind], (maxY - 208f) / 160f);
                    past[kind] = Mathf.Max(past[kind], (maxX + 1) / 160f - HudRoundNumeral.OriginX[kind] - HudRoundNumeral.Advance[kind]);
                }

                for (int kind = 0; kind < 5; kind++)
                {
                    string letter = HudRoundNumeral.Letters[kind].ToString();
                    Assert.GreaterOrEqual(HudRoundNumeral.InkTopEm[kind], top[kind] - 0.002f, letter + ": the ink's top");
                    Assert.GreaterOrEqual(HudRoundNumeral.InkBelowEm[kind], below[kind] - 0.002f, letter + ": the foot under the baseline");
                    Assert.GreaterOrEqual(HudRoundNumeral.InkPastAdvanceEm[kind], past[kind] - 0.002f, letter + ": the reach past the advance");
                    Assert.Less(HudRoundNumeral.InkTopEm[kind], top[kind] + 0.01f, letter + ": the table no taller than the ink (the core stays in the plate)");
                }

                float t, b, e;
                HudRoundNumeral.InkBand(14, out t, out b, out e);
                Assert.AreEqual(1.07f - 0.5f, t, 1e-4f, "XIV: V's top");
                Assert.AreEqual(0.5f + 0.019f, b, 1e-4f, "XIV: X's foot");
                Assert.AreEqual(HudRoundNumeral.InkPastAdvanceEm[1], e, 1e-4f, "XIV ends on V");
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }

        /// <summary>
        /// AD MUST-1 (08.10) · the composite under the letters, in linear light: the solid ink core (0.92) with the plate (0.9 ×
        /// mask) over it holds ≥ 0.87 at every mask value - the dry plate's gaps included - so a cream glyph pixel, at its full
        /// face and at a soft core α 0.8, reads ≥ 4.5:1 over a pure white world and over B1 #D8D2C4. The model reproduces the
        /// AD's arithmetic (ink 0.85 over white: under 4.5; 0.87: over), and the core's soft edge stays inside the round's
        /// cluster (the plate's top margin ≥ 32).
        /// </summary>
        [Test]
        public void MurekkepCekirdegi_BilesikAlfa_DogrusalIsikta_BeyazDunyadaKontrast45Ustu()
        {
            Color cream = UiTokens.Get(UiColor.TextPrimary);
            Color ink = UiTokens.Get(UiColor.HudInk);
            ink.a = 1f;
            Color white = Color.white;
            Color b1 = new Color(0xD8 / 255f, 0xD2 / 255f, 0xC4 / 255f, 1f);
            Assert.AreEqual(0xF3, Mathf.RoundToInt(cream.r * 255f), "cream #F3EEE3");
            Assert.AreEqual(0x12, Mathf.RoundToInt(ink.r * 255f), "ink #12100F");

            // The AD's anchors: 0.85 fails 4.5 over white, 0.87 clears it.
            Assert.Less(HudArt.GlyphContrast(cream, 1f, ink, 0.85f, white), 4.5f, "ink 0.85 over white < 4.5 (AD: 4.39)");
            Assert.GreaterOrEqual(HudArt.GlyphContrast(cream, 1f, ink, 0.87f, white), 4.5f, "ink 0.87 over white ≥ 4.5 (AD: ≈ 4.86)");

            var opacities = new List<float>();
            float worst = float.MaxValue;
            for (int i = 0; i <= 20; i++)
            {
                float mask = i / 20f;
                float opacity = HudArt.UnderGlyphOpacity(mask);
                opacities.Add(opacity);
                Assert.GreaterOrEqual(opacity, 0.87f, "under the letters ≥ 0.87 at plate mask " + mask);
                foreach (Color world in new[] { white, b1 })
                {
                    foreach (float glyphAlpha in new[] { 1f, 0.8f })
                    {
                        float ratio = HudArt.GlyphContrast(cream, glyphAlpha, ink, opacity, world);
                        worst = Mathf.Min(worst, ratio);
                        Assert.GreaterOrEqual(ratio, 4.5f, "mask " + mask + " glyph α " + glyphAlpha + " world " + world);
                    }
                }
            }

            opacities.Sort();
            float median = opacities[opacities.Count / 2];
            Assert.GreaterOrEqual(median, 0.85f, "the effective opacity's median under the letters ≥ 0.85");
            Assert.AreEqual(HudArt.CoreAlpha, HudArt.UnderGlyphOpacity(0f), 1e-6f, "a dry gap: the core alone");

            // The core's soft edge stays in the cluster (cap 40, the tallest letter): the plate's top margin holds 32.
            float reach = HudArt.CorePadPx + HudArt.CoreFalloffPx;
            float coreTop = (HudRoundNumeral.InkTopEm[1] - 0.5f) * RunHudScreen.RoundGlyphHeight + reach;
            Assert.LessOrEqual(coreTop, RunHudScreen.RoundClusterHeight * 0.5f, "the core's top inside the cluster (margin ≥ 32 ref)");
            Debug.Log("[DD-TEST] wp094.plateInk core=" + HudArt.CoreAlpha.ToString("0.00", CultureInfo.InvariantCulture)
                + " white1=" + HudArt.GlyphContrast(cream, 1f, ink, HudArt.CoreAlpha, white).ToString("0.00", CultureInfo.InvariantCulture)
                + " white08=" + HudArt.GlyphContrast(cream, 0.8f, ink, HudArt.CoreAlpha, white).ToString("0.00", CultureInfo.InvariantCulture)
                + " b1=" + HudArt.GlyphContrast(cream, 0.8f, ink, HudArt.CoreAlpha, b1).ToString("0.00", CultureInfo.InvariantCulture)
                + " worst=" + worst.ToString("0.00", CultureInfo.InvariantCulture) + " opacityMedian=" + median.ToString("0.000", CultureInfo.InvariantCulture)
                + " coreTop=" + coreTop.ToString("0.0", CultureInfo.InvariantCulture));
        }
    }
}
