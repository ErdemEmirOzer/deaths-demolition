#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DD.Prototype.Qa
{
    /// <summary>
    /// WP-026 · Command-line helpers shared by the QA drivers. Every QA flag follows the
    /// project's <c>-dd&lt;Name&gt; &lt;value&gt;</c> convention (PilotPerfProbe pattern) so the
    /// same dev Player binary serves every gate; a missing flag means "driver not armed".
    /// </summary>
    public static class QaArgs
    {
        public static string Arg(string name)
        {
            return Arg(Environment.GetCommandLineArgs(), name);
        }

        public static string Arg(string[] args, string name)
        {
            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        public static int ArgInt(string name, int fallback, int min, int max)
        {
            string raw = Arg(name);
            int parsed;
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return Math.Max(min, Math.Min(max, parsed));
            }

            return fallback;
        }

        public static float ArgFloat(string name, float fallback, float min, float max)
        {
            string raw = Arg(name);
            float parsed;
            if (!string.IsNullOrEmpty(raw) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return Math.Max(min, Math.Min(max, parsed));
            }

            return fallback;
        }

        /// <summary>"1,4,8" → [1,4,8]; malformed entries are skipped; empty → fallback.</summary>
        public static int[] ArgIntList(string name, int[] fallback, int min, int max)
        {
            string raw = Arg(name);
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }

            var list = new List<int>();
            string[] parts = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int v;
                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= min && v <= max)
                {
                    list.Add(v);
                }
            }

            return list.Count > 0 ? list.ToArray() : fallback;
        }

        public static string F(float v, int digits = 2)
        {
            return v.ToString("F" + digits, CultureInfo.InvariantCulture);
        }
    }
}
#endif
