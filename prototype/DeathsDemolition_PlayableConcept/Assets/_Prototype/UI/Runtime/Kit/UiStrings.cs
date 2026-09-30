using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// M4-R1 · the R1 content pack at run time. The source of truth is planning/content/r1-strings.json, a flat
    /// {"key": {"en": "…", "tr": "…"}} table written by the UX writer. A byte-identical copy ships as
    /// UI/Resources/Strings/r1-strings.json, and UiStringsTests keeps the two equal. Screens ask by key: weapon.&lt;id&gt;.*,
    /// tree.* (S-04's StringKey stems), tips.*, camera.*, hud.*. The table is parsed once on first use, then every Get is a
    /// dictionary lookup.
    /// A missing key returns the caller's fallback, never the key itself, so a gap reads as the old copy and never as a
    /// code name on screen.
    /// </summary>
    public static class UiStrings
    {
        public const string ResourcePath = "Strings/r1-strings";

        public const string English = "en";
        public const string Turkish = "tr";

        /// <summary>
        /// The column read: "en" or "tr". <see cref="UiSettings.Language"/> (Settings › DISPLAY › LANGUAGE) owns it and sets it
        /// when the settings file loads, so the first screen already reads the saved language; screens read it when built.
        /// </summary>
        public static string Language = English;

        private static CultureInfo s_turkishCulture;
        private static bool s_turkishCultureTried;

        private static Dictionary<string, Dictionary<string, string>> s_table;

        /// <summary>Number of keys loaded (0 when the table is absent).</summary>
        public static int Count
        {
            get { return Table.Count; }
        }

        public static bool Has(string key)
        {
            return key != null && Table.ContainsKey(key);
        }

        /// <summary>The text for <paramref name="key"/> in <see cref="Language"/> (English when that column is empty), or <paramref name="fallback"/>.</summary>
        public static string Get(string key, string fallback = "")
        {
            Dictionary<string, string> row;
            if (key == null || !Table.TryGetValue(key, out row))
            {
                return fallback;
            }

            string text;
            if (row.TryGetValue(Language, out text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            return row.TryGetValue("en", out text) && !string.IsNullOrEmpty(text) ? text : fallback;
        }

        /// <summary>
        /// M-65 (content pack Q9): a number as the language on screen writes it - invariant digits, then under "tr" the
        /// decimal point and the group separator trade places ("1.34" → "1,34", "1,234.5" → "1.234,5"). Deterministic
        /// and CultureInfo-free: a Player may not carry the tr-TR culture. Built on demand, never per frame.
        /// </summary>
        public static string Number(float value, string format = "0.#")
        {
            string text = value.ToString(format, CultureInfo.InvariantCulture);
            return Language == Turkish ? SwapSeparators(text) : text;
        }

        /// <summary>
        /// AD d109 SHOULD-7: a percentage in the language's order. English "80%" (or "+8 %" where a screen spaces its units,
        /// <paramref name="spaced"/>); Turkish "%80", a sign staying in front ("+%8", "−%10"). <paramref name="number"/> is
        /// already formatted (Number, signed).
        /// </summary>
        public static string Percent(string number, bool spaced = false)
        {
            if (Language == Turkish)
            {
                bool signed = !string.IsNullOrEmpty(number) && (number[0] == '+' || number[0] == '\u2212' || number[0] == '-');
                return signed ? number[0] + "%" + number.Substring(1) : "%" + number;
            }

            return spaced ? number + " %" : number + "%";
        }

        /// <summary>A whole percentage in the language's order (EN "80%", TR "%80").</summary>
        public static string Percent(int value) => Percent(Number(value));

        /// <summary>An integer, invariant digits (no separator to trade).</summary>
        public static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string SwapSeparators(string text)
        {
            if (text.IndexOf('.') < 0 && text.IndexOf(',') < 0)
            {
                return text;
            }

            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] == '.' ? ',' : chars[i] == ',' ? '.' : chars[i];
            }

            return new string(chars);
        }

        /// <summary>Get with one named placeholder filled ("{count}" → value). Built on demand, never per frame.</summary>
        public static string Format(string key, string name, string value, string fallback = "")
        {
            string text = Get(key, fallback);
            return string.IsNullOrEmpty(text) ? text : text.Replace("{" + name + "}", value);
        }

        /// <summary>Get with two named placeholders filled. Built on demand, never per frame.</summary>
        public static string Format(string key, string name1, string value1, string name2, string value2, string fallback)
        {
            string text = Get(key, fallback);
            return string.IsNullOrEmpty(text) ? text : text.Replace("{" + name1 + "}", value1).Replace("{" + name2 + "}", value2);
        }

        /// <summary>
        /// Upper case for the language on screen: Turkish rules under "tr" (i → İ, ı → I), invariant otherwise. Every
        /// user-facing upper-casing in the UI goes through here; ids, keys and paths keep ToUpperInvariant.
        /// </summary>
        public static string Upper(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            if (Language != Turkish)
            {
                return text.ToUpperInvariant();
            }

            CultureInfo turkish = TurkishCulture;
            return turkish != null ? text.ToUpper(turkish) : text.Replace('i', '\u0130').ToUpperInvariant();
        }

        private static CultureInfo TurkishCulture
        {
            get
            {
                if (!s_turkishCultureTried)
                {
                    s_turkishCultureTried = true;
                    try
                    {
                        s_turkishCulture = new CultureInfo("tr-TR");
                    }
                    catch (CultureNotFoundException)
                    {
                        s_turkishCulture = null;   // a stripped player: the dotted-capital-I mapping below stands in
                    }
                }

                return s_turkishCulture;
            }
        }

        /// <summary>Tests: drop the parsed table so the next Get reads the resource again.</summary>
        public static void ResetForTests()
        {
            s_table = null;
        }

        private static Dictionary<string, Dictionary<string, string>> Table
        {
            get
            {
                if (s_table == null)
                {
                    TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
                    s_table = asset != null ? Parse(asset.text) : new Dictionary<string, Dictionary<string, string>>();
                }

                return s_table;
            }
        }

        /// <summary>
        /// The pack's own shape only: one object of objects of strings. Anything else stops the parse and keeps what was
        /// read so far; UiStringsTests requires the whole table.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> Parse(string json)
        {
            var table = new Dictionary<string, Dictionary<string, string>>(512);
            int i = 0;
            if (!Expect(json, ref i, '{'))
            {
                return table;
            }

            while (true)
            {
                Skip(json, ref i);
                if (i < json.Length && json[i] == '}')
                {
                    break;
                }

                string key = ReadString(json, ref i);
                if (key == null || !Expect(json, ref i, ':') || !Expect(json, ref i, '{'))
                {
                    break;
                }

                var row = new Dictionary<string, string>(2);
                while (true)
                {
                    Skip(json, ref i);
                    if (i < json.Length && json[i] == '}')
                    {
                        i++;
                        break;
                    }

                    string column = ReadString(json, ref i);
                    string value = column != null && Expect(json, ref i, ':') ? ReadString(json, ref i) : null;
                    if (value == null)
                    {
                        return table;
                    }

                    row[column] = value;
                    Skip(json, ref i);
                    if (i < json.Length && json[i] == ',')
                    {
                        i++;
                    }
                }

                table[key] = row;
                Skip(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                }
            }

            return table;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static bool Expect(string s, ref int i, char c)
        {
            Skip(s, ref i);
            if (i < s.Length && s[i] == c)
            {
                i++;
                return true;
            }

            return false;
        }

        private static string ReadString(string s, ref int i)
        {
            if (!Expect(s, ref i, '"'))
            {
                return null;
            }

            var sb = new StringBuilder(32);
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\' || i >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)System.Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }

                        break;
                    default: sb.Append(e); break;   // \" \\ \/
                }
            }

            return null;
        }
    }
}
