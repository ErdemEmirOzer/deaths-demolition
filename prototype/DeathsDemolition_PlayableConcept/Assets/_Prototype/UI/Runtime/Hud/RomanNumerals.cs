using System;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-094 HUD v4 (R3, C-00 07.10; the user: "round sayısı ... romen rakamlarıyla ... sınır yok") · Roman numerals in
    /// the five letters the AP-032 brush set draws: I V X L C - no D, no M.
    /// - 1..399: the standard subtractive form (IV, IX, XL, XC; 399 = CCCXCIX).
    /// - 400 and up: the hundreds continue additively with C (400 = CCCC, 1000 = ten C), the rest as above. A round never
    ///   realistically gets there; it is written all the same, because the round counter has no ceiling.
    /// - 0 and below: nothing (an empty numeral; the HUD shows its own placeholder before round 1).
    /// <see cref="Write"/> fills a caller's buffer (no allocation); <see cref="Cached"/> keeps one string per value for
    /// 1..<see cref="CachedMax"/>, built the first time it is asked for, so a round change after the first run allocates
    /// nothing. Main thread only (the cache and the scratch buffer are shared).
    /// </summary>
    public static class RomanNumerals
    {
        /// <summary>The cached range's top (the last value written in the standard form).</summary>
        public const int CachedMax = 399;

        /// <summary>The longest numeral below 100 (LXXXVIII).</summary>
        public const int MaxLengthBelowHundred = 8;

        // The tens and units, largest first; the hundreds are C repeated (see the summary).
        private static readonly int[] Values = { 90, 50, 40, 10, 9, 5, 4, 1 };
        private static readonly char[] First = { 'X', 'L', 'X', 'X', 'I', 'V', 'I', 'I' };
        private static readonly char[] Second = { 'C', '\0', 'L', '\0', 'X', '\0', 'V', '\0' };

        private static readonly string[] s_cache = new string[CachedMax + 1];
        private static readonly char[] s_scratch = new char[CachedMax / 100 + MaxLengthBelowHundred];

        /// <summary>Letters <paramref name="value"/> takes (0 for value ≤ 0).</summary>
        public static int Length(int value)
        {
            if (value <= 0)
            {
                return 0;
            }

            int length = value / 100;
            int rest = value % 100;
            for (int i = 0; i < Values.Length; i++)
            {
                while (rest >= Values[i])
                {
                    length += Second[i] != '\0' ? 2 : 1;
                    rest -= Values[i];
                }
            }

            return length;
        }

        /// <summary>
        /// Writes <paramref name="value"/> into <paramref name="buffer"/> from <paramref name="start"/>; returns the letters
        /// written (0 for value ≤ 0). Throws when the buffer is shorter than <see cref="Length"/> from start.
        /// </summary>
        public static int Write(int value, char[] buffer, int start)
        {
            if (value <= 0)
            {
                return 0;
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            int length = Length(value);
            if (start < 0 || buffer.Length - start < length)
            {
                throw new ArgumentException("buffer too short for the numeral", nameof(buffer));
            }

            int k = start;
            for (int h = value / 100; h > 0; h--)
            {
                buffer[k++] = 'C';
            }

            int rest = value % 100;
            for (int i = 0; i < Values.Length; i++)
            {
                while (rest >= Values[i])
                {
                    buffer[k++] = First[i];
                    if (Second[i] != '\0')
                    {
                        buffer[k++] = Second[i];
                    }

                    rest -= Values[i];
                }
            }

            return k - start;
        }

        /// <summary>The numeral as a string: the cached one for 1..<see cref="CachedMax"/>, a new one above, empty for ≤ 0.</summary>
        public static string Format(int value)
        {
            if (value <= 0)
            {
                return string.Empty;
            }

            if (value <= CachedMax)
            {
                return Cached(value);
            }

            var buffer = new char[Length(value)];
            return new string(buffer, 0, Write(value, buffer, 0));
        }

        /// <summary>The cached string for 1..<see cref="CachedMax"/> (built on first use); <see cref="Format"/> outside the range.</summary>
        public static string Cached(int value)
        {
            if (value <= 0 || value > CachedMax)
            {
                return Format(value);
            }

            string text = s_cache[value];
            if (text == null)
            {
                text = new string(s_scratch, 0, Write(value, s_scratch, 0));
                s_cache[value] = text;
            }

            return text;
        }
    }
}
