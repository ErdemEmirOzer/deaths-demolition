#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DD.Prototype.Qa
{
    /// <summary>One gate line of the closure report. <c>value</c>/<c>threshold</c> are strings so the schema stays flat for JsonUtility.</summary>
    [Serializable]
    public sealed class QaGateResult
    {
        public string id;
        public bool pass;
        public string value;
        public string threshold;
        public string note;
        public string source;   // file the number came from (relative to the report directory)

        public static QaGateResult Of(string id, bool pass, string value, string threshold, string note, string source = null)
        {
            return new QaGateResult { id = id, pass = pass, value = value ?? "", threshold = threshold ?? "", note = note ?? "", source = source ?? "" };
        }
    }

    /// <summary>
    /// WP-026 · <c>qa-report.json</c> schema (ADR-026-1). Written by every QA driver for its own
    /// slice and merged by <c>tools/qa/aggregate.py</c> into the closure report. Flat on purpose:
    /// JsonUtility-serialisable, greppable, ≤ 20-line text summary alongside.
    /// </summary>
    [Serializable]
    public sealed class QaReport
    {
        public const string Schema = "dd-qa-report-v1";

        public string schema = Schema;
        public string package = "WP-026";
        public string driver;          // QaRunBot | QaReadabilityGate | PilotPerfProbe | SpawnMix | aggregate
        public string utc;
        public string buildInfo;       // WP-025 build-info.json contents/summary when present, else "n/a"
        public string machine;
        public string window;          // clean-window statement (perf-clean.sh) or "n/a"
        public string load1Before;
        public string load1After;
        public int seed;
        public bool pass;
        public string summary;
        public List<QaGateResult> gates = new List<QaGateResult>();

        public static QaReport Create(string driver, int seed)
        {
            return new QaReport
            {
                driver = driver,
                seed = seed,
                utc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                machine = SystemInfo.deviceModel + " / " + SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName + " / " + Application.unityVersion,
                buildInfo = ReadBuildInfo(),
                window = "n/a",
                load1Before = "",
                load1After = ""
            };
        }

        public QaGateResult Add(string id, bool gatePass, string value, string threshold, string note, string source = null)
        {
            var g = QaGateResult.Of(id, gatePass, value, threshold, note, source);
            gates.Add(g);
            return g;
        }

        public void Finalise()
        {
            bool all = true;
            for (int i = 0; i < gates.Count; i++)
            {
                all &= gates[i].pass;
            }

            pass = all && gates.Count > 0;
        }

        public string ToJson()
        {
            Finalise();
            return JsonUtility.ToJson(this, true);
        }

        public static QaReport FromJson(string json)
        {
            return JsonUtility.FromJson<QaReport>(json);
        }

        /// <summary>≤ 20 lines: verdict, environment, one line per gate.</summary>
        public string Summarize(int maxGateLines = 16)
        {
            Finalise();
            var sb = new StringBuilder();
            sb.Append(driver).Append(' ').Append(pass ? "PASS" : "FAIL").Append(" · ").Append(utc).Append(" · seed ").Append(seed).Append('\n');
            sb.Append("build: ").Append(string.IsNullOrEmpty(buildInfo) ? "n/a" : buildInfo).Append('\n');
            sb.Append("window: ").Append(window).Append(" · load1 ").Append(load1Before).Append(" → ").Append(load1After).Append('\n');
            int lines = 0;
            for (int i = 0; i < gates.Count && lines < maxGateLines; i++, lines++)
            {
                QaGateResult g = gates[i];
                sb.Append(g.pass ? "PASS " : "FAIL ").Append(g.id).Append(" = ").Append(g.value);
                if (!string.IsNullOrEmpty(g.threshold)) sb.Append(" (").Append(g.threshold).Append(')');
                if (!string.IsNullOrEmpty(g.note)) sb.Append(" — ").Append(g.note);
                sb.Append('\n');
            }

            if (gates.Count > maxGateLines)
            {
                sb.Append("… +").Append(gates.Count - maxGateLines).Append(" gates in qa-report.json\n");
            }

            if (!string.IsNullOrEmpty(summary))
            {
                sb.Append(summary).Append('\n');
            }

            return sb.ToString();
        }

        public void Write(string directory, string baseName = "qa-report")
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, baseName + ".json"), ToJson());
            File.WriteAllText(Path.Combine(directory, baseName + ".txt"), Summarize());
        }

        /// <summary>WP-025 build stamp: <c>Resources/build-info</c> (BuildStamp, in the player), else a file next to Data or <c>-ddBuildInfo</c>; absent → "n/a".</summary>
        public static string ReadBuildInfo()
        {
            try
            {
                TextAsset stamp = Resources.Load<TextAsset>("build-info");
                if (stamp != null && !string.IsNullOrEmpty(stamp.text))
                {
                    string t = stamp.text.Replace("\r", "").Replace("\n", " ");
                    return t.Length > 240 ? t.Substring(0, 240) + "…" : t;
                }

                string flag = QaArgs.Arg("-ddBuildInfo");
                string[] candidates =
                {
                    flag,
                    Path.Combine(Application.dataPath, "build-info.json"),
                    Path.Combine(Application.dataPath, "..", "build-info.json"),
                    Path.Combine(Application.streamingAssetsPath, "build-info.json")
                };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i]))
                    {
                        string text = File.ReadAllText(candidates[i]).Replace("\r", "").Replace("\n", " ");
                        return text.Length > 240 ? text.Substring(0, 240) + "…" : text;
                    }
                }
            }
            catch (Exception)
            {
                // best effort only
            }

            return "n/a";
        }
    }
}
#endif
