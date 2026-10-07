using System;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-094 HUD v4 (R3, C-00 07.10) · the round's Roman numerals: I V X L C only (the AP-032 brush set), the standard
    /// subtractive form up to 399, additive C past it, nothing for 0; a non-allocating writer and cached strings; the
    /// HudRoundNumeral metrics table (advance + pair kerning) that lays the brush letters out.
    /// </summary>
    [TestFixture]
    [Category("WP094")]
    public sealed class RomanNumeralsTests
    {
        [TestCase(1, "I")]
        [TestCase(4, "IV")]
        [TestCase(9, "IX")]
        [TestCase(14, "XIV")]
        [TestCase(23, "XXIII")]
        [TestCase(38, "XXXVIII")]
        [TestCase(40, "XL")]
        [TestCase(49, "XLIX")]
        [TestCase(88, "LXXXVIII")]
        [TestCase(90, "XC")]
        [TestCase(99, "XCIX")]
        [TestCase(100, "C")]
        [TestCase(388, "CCCLXXXVIII")]
        [TestCase(399, "CCCXCIX")]
        [TestCase(400, "CCCC")]
        [TestCase(444, "CCCCXLIV")]
        [TestCase(1000, "CCCCCCCCCC")]
        public void RomanNumerals_Bicim_YazarVeUzunluk(int value, string expected)
        {
            Assert.AreEqual(expected, RomanNumerals.Format(value), "Format");
            Assert.AreEqual(expected.Length, RomanNumerals.Length(value), "Length");
            var buffer = new char[expected.Length + 5];
            int written = RomanNumerals.Write(value, buffer, 3);
            Assert.AreEqual(expected.Length, written, "Write count");
            Assert.AreEqual(expected, new string(buffer, 3, written), "Write from an offset");
        }

        [Test]
        public void RomanNumerals_YalnizIVXLC_SifirBos_OnbellekAyirmaz()
        {
            for (int value = 1; value <= 2000; value++)
            {
                string text = RomanNumerals.Format(value);
                Assert.IsNotEmpty(text, value.ToString());
                foreach (char c in text)
                {
                    Assert.GreaterOrEqual(HudRoundNumeral.KindOf(c), 0, value + " → " + text + " uses '" + c + "' (only I V X L C are drawn)");
                }

                Assert.AreEqual(value, Parse(text), "round trip " + text);
            }

            Assert.AreEqual(string.Empty, RomanNumerals.Format(0), "no zero in Roman numerals");
            Assert.AreEqual(string.Empty, RomanNumerals.Format(-5));
            Assert.AreEqual(0, RomanNumerals.Write(0, new char[1], 0));
            Assert.Throws<ArgumentException>(() => RomanNumerals.Write(38, new char[6], 0), "XXXVIII needs 7");
            Assert.Throws<ArgumentException>(() => RomanNumerals.Write(4, new char[4], 3), "IV from index 3 of 4");

            // 1..399 come from the cache: the same instance every time, and a cached read allocates nothing.
            Assert.AreSame(RomanNumerals.Cached(23), RomanNumerals.Format(23));
            for (int value = 1; value <= RomanNumerals.CachedMax; value++)
            {
                RomanNumerals.Cached(value);
            }

            var buffer = new char[16];
            char[] probe = null;
            Assert.That(() => { probe = new char[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "positive control (WP-051C)");
            Assert.IsNotNull(probe);
            Assert.That(() =>
            {
                for (int value = 1; value <= RomanNumerals.CachedMax; value++)
                {
                    RomanNumerals.Cached(value);
                    RomanNumerals.Write(value, buffer, 0);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void HudRoundNumeral_Metrikler_IlerlemeVeCiftAraligi()
        {
            Assert.AreEqual(HudRoundNumeral.Letters.Length, HudRoundNumeral.Advance.Length);
            Assert.AreEqual(HudRoundNumeral.Letters.Length, HudRoundNumeral.GlyphKeys.Length);
            Assert.AreEqual(HudRoundNumeral.Letters.Length * HudRoundNumeral.Letters.Length, HudRoundNumeral.Kerning.Length);
            for (int i = 0; i < HudRoundNumeral.Letters.Length; i++)
            {
                Assert.AreEqual(i, HudRoundNumeral.KindOf(HudRoundNumeral.Letters[i]));
                Assert.Greater(HudRoundNumeral.Advance[i], 0.2f, "a letter has width");
                StringAssert.EndsWith("." + HudRoundNumeral.Letters[i], HudRoundNumeral.GlyphKeys[i], "the key names its letter");
            }

            Assert.AreEqual(-1, HudRoundNumeral.KindOf('D'), "no D in the set");
            Assert.AreEqual(-1, HudRoundNumeral.KindOf('M'), "no M in the set");

            // XIV at 44 px: X + I + V advances, plus the X|I and I|V kerning.
            const float h = 44f;
            float expected = (HudRoundNumeral.Advance[2] + HudRoundNumeral.Advance[0] + HudRoundNumeral.Advance[1]
                              + HudRoundNumeral.Kerning[2 * 5 + 0] + HudRoundNumeral.Kerning[0 * 5 + 1]) * h;
            Assert.AreEqual(expected, HudRoundNumeral.MeasureGlyphs("XIV".ToCharArray(), 3, h), 1e-3f);

            // Every numeral the acceptance frames show (I, IV, IX, XIV, XXIII) fits the glyph path; widths grow with length.
            var letters = new char[HudRoundNumeral.Capacity];
            float previous = 0f;
            foreach (int round in new[] { 1, 4, 23, 38 })
            {
                int n = RomanNumerals.Write(round, letters, 0);
                Assert.LessOrEqual(n, HudRoundNumeral.Capacity);
                float width = HudRoundNumeral.MeasureGlyphs(letters, n, h);
                Assert.Greater(width, previous, round + " is wider than the shorter numeral before it");
                previous = width;
            }

            Assert.LessOrEqual(RomanNumerals.Length(RomanNumerals.CachedMax), HudRoundNumeral.Capacity, "every cached numeral fits the glyph path");
        }

        private static int Parse(string roman)
        {
            int total = 0;
            for (int i = 0; i < roman.Length; i++)
            {
                int v = Value(roman[i]);
                int next = i + 1 < roman.Length ? Value(roman[i + 1]) : 0;
                total += v < next ? -v : v;
            }

            return total;
        }

        private static int Value(char c)
        {
            switch (c)
            {
                case 'I': return 1;
                case 'V': return 5;
                case 'X': return 10;
                case 'L': return 50;
                case 'C': return 100;
                default: return 0;
            }
        }
    }
}
