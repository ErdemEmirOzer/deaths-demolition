using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// R2 measurement contract (WP-091, S-10 · tools/qa/r2-measure-contract.md): a measuring test hands its result to the
    /// acceptance chain as r2-&lt;gate&gt;.json in DD_R2_MEASURE_DIR, stamped with DD_R2_COMMIT. Without the folder variable it
    /// writes nothing (ordinary CI) — the test still asserts the contract's threshold itself. Plain numbers only.
    /// </summary>
    internal static class R2Olcum
    {
        public const string Schema = "dd-r2-measure-v1";

        public static string Yaz(string gate, string source, IDictionary<string, double> values, int samples)
        {
            string dir = Environment.GetEnvironmentVariable("DD_R2_MEASURE_DIR");
            if (string.IsNullOrEmpty(dir)) return null;
            var sb = new StringBuilder();
            sb.Append("{\"schema\": \"").Append(Schema).Append("\", \"gate\": \"").Append(gate)
              .Append("\", \"commit\": \"").Append(Environment.GetEnvironmentVariable("DD_R2_COMMIT") ?? "")
              .Append("\", \"source\": \"").Append(source).Append("\", \"values\": {");
            bool first = true;
            foreach (KeyValuePair<string, double> kv in values)
            {
                if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value)) throw new ArgumentException(gate + "." + kv.Key + " is not a plain number");
                if (!first) sb.Append(", ");
                first = false;
                sb.Append('"').Append(kv.Key).Append("\": ").Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
            }

            sb.Append("}, \"samples\": ").Append(samples).Append('}');
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "r2-" + gate + ".json");
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
