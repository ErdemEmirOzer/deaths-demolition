using System.Collections.Generic;
using System.IO;
using DD.Prototype.UI;
using DD.Prototype.UI.EditorTools;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 3 · T9 (direction §2.2, §12): the three role faces are placed, static,
    /// cover their character sets, the Numeric digits share one advance, the atlases stay
    /// under the 1.5 MB ceiling, the OFL licence sits beside every TTF, and caps kerning is
    /// applied (AV on Display, To on Text).
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiFontsV3Tests
    {
        private static TMP_FontAsset Load(string resource)
        {
            var asset = Resources.Load<TMP_FontAsset>(resource);
            Assert.IsNotNull(asset, resource + " yerinde değil (Tools > Deaths Demolition > UI > Build Font Assets)");
            return asset;
        }

        private static IEnumerable<object[]> Roles()
        {
            yield return new object[] { UiFonts.DisplayFontResource, UiFontBuilder.DisplayCharset };
            yield return new object[] { UiFonts.TextFontResource, UiFontBuilder.TextCharset };
            yield return new object[] { UiFonts.NumericFontResource, UiFontBuilder.NumericCharset };
        }

        [Test]
        public void UcYuz_Statik_KarakterKumesiTam()
        {
            var failures = new List<string>();
            foreach (object[] role in Roles())
            {
                TMP_FontAsset asset = Load((string)role[0]);
                if (asset.atlasPopulationMode != AtlasPopulationMode.Static)
                {
                    failures.Add(asset.name + " statik değil (Player'da rasterleştirmez)");
                }

                List<char> missing;
                if (!asset.HasCharacters((string)role[1], out missing))
                {
                    failures.Add(asset.name + " eksik: " + new string(missing.ToArray()));
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void Numeric_RakamIlerlemeleriEsit()
        {
            TMP_FontAsset numeric = Load(UiFonts.NumericFontResource);
            float first = -1f;
            foreach (char c in UiFontBuilder.Digits)
            {
                TMP_Character ch;
                Assert.IsTrue(numeric.characterLookupTable.TryGetValue(c, out ch), "rakam yok: " + c);
                float advance = ch.glyph.metrics.horizontalAdvance;
                if (first < 0f) first = advance;
                Assert.AreEqual(first, advance, 1e-3f, "rakam ilerlemesi eşit değil: " + c);
            }
        }

        /// <summary>
        /// 12b (AD R1-A SHOULD-7): STICK LOOK SPEED "180°/s", the FOV rows and the ACCURACY stat
        /// print "°" in the Numeric face; the sign is the atlas's own glyph (Oswald), not a
        /// fallback to the Text face, and it is not tabular (only digits share one advance).
        /// </summary>
        [Test]
        public void Numeric_DereceIsareti_KendiAtlasinda()
        {
            StringAssert.Contains(UiFontBuilder.DegreeSign.ToString(), UiFontBuilder.NumericCharset, "Numeric karakter kümesi ° içerir");
            TMP_FontAsset numeric = Load(UiFonts.NumericFontResource);
            TMP_Character degree;
            Assert.IsTrue(numeric.characterLookupTable.TryGetValue(UiFontBuilder.DegreeSign, out degree),
                "Numeric SDF ° glifini kendi atlasında taşır (Build Font Assets yeniden koşulmalı)");
            Assert.Greater(degree.glyph.glyphRect.width, 0, "° atlasta rasterleştirilmiş");
            Assert.Greater(degree.glyph.metrics.horizontalAdvance, 0f, "° ilerlemesi var");
        }

        [Test]
        public void Atlaslar_Tavan15MB_Alpha8()
        {
            long bytes = 0;
            foreach (object[] role in Roles())
            {
                TMP_FontAsset asset = Load((string)role[0]);
                Texture2D atlas = asset.atlasTexture;
                Assert.AreEqual(TextureFormat.Alpha8, atlas.format, asset.name + " Alpha8");
                bytes += (long)atlas.width * atlas.height;
            }

            Debug.Log("[DD-TEST] wp068.fontAtlas " + (bytes / (1024f * 1024f)).ToString("0.00") + " MB");
            Assert.LessOrEqual(bytes, (long)(1.5f * 1024 * 1024), "T9 atlas tavanı 1,5 MB");
        }

        [Test]
        public void OflLisansi_HerTtfYaninda()
        {
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), UiFontBuilder.SourceRoot);
            var missing = new List<string>();
            foreach (string ttf in Directory.GetFiles(root, "*.ttf", SearchOption.AllDirectories))
            {
                string folder = Path.GetDirectoryName(ttf);
                if (Directory.GetFiles(folder, "*OFL*.txt").Length == 0)
                {
                    missing.Add(Path.GetFileName(ttf));
                }
            }

            Assert.IsEmpty(missing, "OFL lisansı yanında değil:\n" + string.Join("\n", missing));
        }

        private static float PairAdvance(TMP_FontAsset asset, char first, char second)
        {
            TMP_Character a, b;
            Assert.IsTrue(asset.characterLookupTable.TryGetValue(first, out a));
            Assert.IsTrue(asset.characterLookupTable.TryGetValue(second, out b));
            foreach (TMP_GlyphPairAdjustmentRecord record in asset.fontFeatureTable.glyphPairAdjustmentRecords)
            {
                if (record.firstAdjustmentRecord.glyphIndex == a.glyphIndex && record.secondAdjustmentRecord.glyphIndex == b.glyphIndex)
                {
                    return record.firstAdjustmentRecord.glyphValueRecord.xAdvance;
                }
            }

            return 0f;
        }

        [Test]
        public void DisplayVeNumeric_TextYuzuneDuser()
        {
            TMP_FontAsset text = Load(UiFonts.TextFontResource);
            foreach (string role in new[] { UiFonts.DisplayFontResource, UiFonts.NumericFontResource })
            {
                TMP_FontAsset asset = Load(role);
                Assert.IsNotNull(asset.fallbackFontAssetTable);
                Assert.Contains(text, asset.fallbackFontAssetTable, asset.name + " → Text SDF (LiberationSans değil)");
            }
        }

        [Test]
        public void SatirKutusu_BantaSigar()
        {
            // The HUD's fixed numeric band (NumericL 32 in 36 px) and the face-read LineHeight.
            float numeric = UiFactory.FaceLineRatio(UiType.NumericL) * UiTokens.FontSize(UiType.NumericL);
            Assert.LessOrEqual(numeric, 36f, "NumericL satır kutusu HUD bandına sığar");
            Assert.AreEqual(1f, UiFactory.FaceLineRatio(UiType.Display), 0.01f, "Display: Latin typo kutusu (1,0 em)");
        }

        [Test]
        public void Kerning_Display_AV_Text_To()
        {
            Assert.Less(PairAdvance(Load(UiFonts.DisplayFontResource), 'A', 'V'), 0f, "Display AV (el yazımı büyük harf çiftleri)");
            Assert.Less(PairAdvance(Load(UiFonts.DisplayFontResource), 'T', 'A'), 0f, "Display TA");
            Assert.Less(PairAdvance(Load(UiFonts.TextFontResource), 'T', 'o'), 0f, "Text To (GPOS'tan)");
            Assert.AreEqual(0f, PairAdvance(Load(UiFonts.NumericFontResource), '1', '1'), 1e-4f, "Numeric rakam çifti yok (tablo rakamları)");
        }
    }
}
