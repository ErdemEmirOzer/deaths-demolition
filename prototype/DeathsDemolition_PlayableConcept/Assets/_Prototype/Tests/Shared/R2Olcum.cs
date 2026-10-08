using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DD.Prototype.Tests
{
    /// <summary>
    /// R2 measurement contract (WP-091, S-10 · tools/qa/r2-measure-contract.md) — shared by the EditMode and PlayMode test
    /// assemblies (DD.Prototype.Tests.Shared, C-00 05.10): a measuring test hands its result to the
    /// acceptance chain as r2-&lt;gate&gt;.json in DD_R2_MEASURE_DIR, stamped with DD_R2_COMMIT. Without the folder variable it
    /// writes nothing (ordinary CI) — the test still asserts the contract's threshold itself. Plain numbers only.
    /// </summary>
    public static class R2Olcum
    {
        public const string Schema = "dd-r2-measure-v1";

        /// <param name="field">Optional top-level string field of the artifact (S-10 08.10: L1 / L1t / L2 carry "l1Def"); the judge ignores
        /// unknown top-level fields. Null = the artifact as before.</param>
        public static string Yaz(string gate, string source, IDictionary<string, double> values, int samples, string field = null, string fieldValue = null)
        {
            var v = new List<KeyValuePair<string, double?>>(values.Count);
            foreach (KeyValuePair<string, double> kv in values) v.Add(new KeyValuePair<string, double?>(kv.Key, kv.Value));
            return Write(gate, source, v, samples, null, field, fieldValue);
        }

        /// <summary>
        /// As <see cref="Yaz(string, string, IDictionary{string, double}, int)"/>, for a gate with a key that cannot be measured
        /// yet: its value is null (written as JSON null — S-10's judge counts the gate BLOCKED, not PASS) and <paramref name="note"/>
        /// says why (C-00 05.10, H3 contactDeviationFramesMax until S-06's hand contact lands).
        /// </summary>
        public static string Yaz(string gate, string source, IDictionary<string, double?> values, int samples, string note)
        {
            var v = new List<KeyValuePair<string, double?>>(values.Count);
            foreach (KeyValuePair<string, double?> kv in values) v.Add(kv);
            return Write(gate, source, v, samples, note, null, null);
        }

        private static string Write(string gate, string source, List<KeyValuePair<string, double?>> values, int samples, string note, string field, string fieldValue)
        {
            string dir = Environment.GetEnvironmentVariable("DD_R2_MEASURE_DIR");
            if (string.IsNullOrEmpty(dir)) return null;
            var sb = new StringBuilder();
            sb.Append("{\"schema\": \"").Append(Schema).Append("\", \"gate\": \"").Append(gate)
              .Append("\", \"commit\": \"").Append(Environment.GetEnvironmentVariable("DD_R2_COMMIT") ?? "")
              .Append("\", \"source\": \"").Append(source).Append('"');
            if (!string.IsNullOrEmpty(field)) sb.Append(", \"").Append(field).Append("\": \"").Append((fieldValue ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            sb.Append(", \"values\": {");
            for (int i = 0; i < values.Count; i++)
            {
                KeyValuePair<string, double?> kv = values[i];
                if (kv.Value.HasValue && (double.IsNaN(kv.Value.Value) || double.IsInfinity(kv.Value.Value))) throw new ArgumentException(gate + "." + kv.Key + " is not a plain number");
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(kv.Key).Append("\": ").Append(kv.Value.HasValue ? kv.Value.Value.ToString("R", CultureInfo.InvariantCulture) : "null");
            }

            sb.Append("}, \"samples\": ").Append(samples);
            if (!string.IsNullOrEmpty(note)) sb.Append(", \"note\": \"").Append(note.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            sb.Append('}');
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "r2-" + gate + ".json");
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
