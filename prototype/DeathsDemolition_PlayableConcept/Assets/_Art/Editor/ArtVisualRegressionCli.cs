using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Headless front end for <see cref="ArtVisualRegression.ComparePng"/>: compares
    /// every same-named PNG in two folders and writes one JSON summary. This is the "fark
    /// ölçümü" half of the visual regression tool; the captures themselves come from the
    /// PlayMode test (<c>PilotSahne_StandartPozlar_1080pYakalama</c>) or the Player probe.
    ///
    /// <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtVisualRegressionCli.CompareDirs
    /// -ddA &lt;reference dir&gt; -ddB &lt;candidate dir&gt; -ddOut &lt;summary.json&gt; [-ddThreshold 8]
    /// [-ddMaxChanged 0.02]</c>. Exit code is non-zero (an exception) when a pair exceeds
    /// <c>-ddMaxChanged</c> so a gate script can read the result without parsing.
    /// </summary>
    public static class ArtVisualRegressionCli
    {
        [Serializable]
        public sealed class Pair
        {
            public string name;
            public string a;
            public string b;
            public bool missing;
            public int width;
            public int height;
            public double meanAbs;
            public double maxAbs;
            public double changedFraction;
            public bool withinLimit;
        }

        [Serializable]
        public sealed class Summary
        {
            public string utc;
            public string dirA;
            public string dirB;
            public int threshold;
            public double maxChangedAllowed;
            public int pairs;
            public int compared;
            public int exceeded;
            public List<Pair> results = new List<Pair>();
        }

        public static void CompareDirs()
        {
            string a = Arg("-ddA");
            string b = Arg("-ddB");
            string outPath = Arg("-ddOut");
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || string.IsNullOrEmpty(outPath))
            {
                throw new InvalidOperationException("[ArtPipeline] -ddA <dir> -ddB <dir> -ddOut <json> are required.");
            }

            int threshold = 8;
            string t = Arg("-ddThreshold");
            if (!string.IsNullOrEmpty(t))
            {
                threshold = int.Parse(t, CultureInfo.InvariantCulture);
            }

            double maxChanged = 0.02;
            string m = Arg("-ddMaxChanged");
            if (!string.IsNullOrEmpty(m))
            {
                maxChanged = double.Parse(m, CultureInfo.InvariantCulture);
            }

            Summary summary = Compare(a, b, (byte)Mathf.Clamp(threshold, 0, 255), maxChanged);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, JsonUtility.ToJson(summary, true));

            var sb = new StringBuilder();
            sb.Append("[ArtPipeline] Visual regression: ").Append(summary.compared).Append('/').Append(summary.pairs)
              .Append(" compared, ").Append(summary.exceeded).Append(" exceeded ").Append(maxChanged.ToString("0.####", CultureInfo.InvariantCulture))
              .Append(" (threshold ").Append(threshold).Append(")\n");
            foreach (Pair p in summary.results)
            {
                sb.Append("  ").Append(p.name).Append(p.missing ? "  MISSING" : "  changed=" + p.changedFraction.ToString("0.0000", CultureInfo.InvariantCulture) +
                          " mean=" + p.meanAbs.ToString("0.00", CultureInfo.InvariantCulture) + " max=" + p.maxAbs.ToString("0", CultureInfo.InvariantCulture)).Append('\n');
            }

            Debug.Log(sb.ToString());
            if (summary.exceeded > 0)
            {
                throw new InvalidOperationException("[ArtPipeline] Visual regression exceeded on " + summary.exceeded + " pair(s); see " + outPath);
            }
        }

        public static Summary Compare(string dirA, string dirB, byte threshold, double maxChanged)
        {
            var summary = new Summary
            {
                utc = DateTime.UtcNow.ToString("o"),
                dirA = dirA,
                dirB = dirB,
                threshold = threshold,
                maxChangedAllowed = maxChanged
            };

            string[] files = Directory.Exists(dirA) ? Directory.GetFiles(dirA, "*.png") : new string[0];
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string fa in files)
            {
                string name = Path.GetFileName(fa);
                string fb = Path.Combine(dirB, name);
                var pair = new Pair { name = name, a = fa, b = fb };
                summary.pairs++;
                if (!File.Exists(fb))
                {
                    pair.missing = true;
                    pair.withinLimit = false;
                    summary.exceeded++;
                    summary.results.Add(pair);
                    continue;
                }

                ArtVisualRegression.Diff d = ArtVisualRegression.ComparePng(fa, fb, threshold);
                pair.width = d.Width;
                pair.height = d.Height;
                pair.meanAbs = d.MeanAbs;
                pair.maxAbs = d.MaxAbs;
                pair.changedFraction = d.ChangedFraction;
                pair.withinLimit = d.ChangedFraction <= maxChanged;
                summary.compared++;
                if (!pair.withinLimit)
                {
                    summary.exceeded++;
                }

                summary.results.Add(pair);
            }

            return summary;
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
