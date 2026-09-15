using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-012 · Reads a Profiler binary log written by PilotPerfProbe (<c>-ddPilotProfilerLog</c>)
    /// and answers one question: what is inside the hitch frames that is not inside the
    /// normal frames? For every frame it takes the CPU main-thread hierarchy, splits the
    /// frames at the 16.667 ms line and reports, per marker, the mean total time in hitch
    /// frames versus normal frames (the difference is the hitch). Batch:
    ///
    ///   -executeMethod DD.Art.Pipeline.EditorTools.PilotProfilerAnalysisCli.Run -ddIn &lt;log.raw&gt; -ddOut &lt;report.json&gt; [-ddDepth 3]
    ///
    /// The Profiler window is not opened; ProfilerDriver.LoadProfile works in batchmode.
    /// </summary>
    public static class PilotProfilerAnalysisCli
    {
        [Serializable]
        public sealed class MarkerRow
        {
            public string marker;
            public int depth;
            public float hitchMeanMs;
            public float normalMeanMs;
            public float deltaMs;
            public int hitchFramesPresent;
        }

        [Serializable]
        public sealed class FrameRow
        {
            public int frame;
            public float frameMs;
            public string top;
        }

        [Serializable]
        public sealed class Report
        {
            public string input;
            public int frames;
            public int hitchFrames;
            public float thresholdMs;
            public float meanMs;
            public float hitchMeanMs;
            public float normalMeanMs;
            public List<MarkerRow> markers = new List<MarkerRow>();
            public List<FrameRow> worstFrames = new List<FrameRow>();
        }

        public const float HitchThresholdMs = 16.667f;

        public static void Run()
        {
            string input = Arg("-ddIn");
            string output = Arg("-ddOut");
            int depth = 3;
            int.TryParse(Arg("-ddDepth") ?? "3", NumberStyles.Integer, CultureInfo.InvariantCulture, out depth);
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
            {
                throw new ArgumentException("[PilotProfilerAnalysis] -ddIn <log.raw> and -ddOut <report.json> are required.");
            }

            Report report = Analyse(input, depth);
            File.WriteAllText(output, JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
            Debug.Log(Describe(report));
        }

        public static Report Analyse(string input, int maxDepth)
        {
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("[PilotProfilerAnalysis] Profiler log missing", input);
            }

            // The driver keeps a ring of 300 frames by default; the history length setter is
            // internal, so it is reached by reflection (best effort — the log says what applied).
            TryRaiseFrameHistory(2000);

            if (!ProfilerDriver.LoadProfile(input, false))
            {
                throw new InvalidOperationException("[PilotProfilerAnalysis] ProfilerDriver.LoadProfile failed for " + input);
            }

            int first = ProfilerDriver.firstFrameIndex;
            int last = ProfilerDriver.lastFrameIndex;
            var report = new Report { input = input, thresholdMs = HitchThresholdMs };
            if (first < 0 || last < first)
            {
                throw new InvalidOperationException("[PilotProfilerAnalysis] No frames in " + input);
            }

            var hitchSums = new Dictionary<string, double>(StringComparer.Ordinal);
            var normalSums = new Dictionary<string, double>(StringComparer.Ordinal);
            var hitchPresence = new Dictionary<string, int>(StringComparer.Ordinal);
            var depthOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var frameTimes = new List<(int frame, float ms, string top)>();
            double sumAll = 0d, sumHitch = 0d, sumNormal = 0d;
            int hitch = 0, normal = 0;

            for (int frame = first; frame <= last; frame++)
            {
                using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (view == null || !view.valid)
                    {
                        continue;
                    }

                    float ms = view.frameTimeMs;
                    bool isHitch = ms > HitchThresholdMs;
                    sumAll += ms;
                    if (isHitch) { hitch++; sumHitch += ms; } else { normal++; sumNormal += ms; }

                    var perFrame = new Dictionary<string, double>(StringComparer.Ordinal);
                    Walk(view, view.GetRootItemID(), 0, maxDepth, string.Empty, perFrame, depthOf);
                    Dictionary<string, double> sink = isHitch ? hitchSums : normalSums;
                    string top = null;
                    double topMs = -1d;
                    foreach (KeyValuePair<string, double> kv in perFrame)
                    {
                        sink[kv.Key] = (sink.TryGetValue(kv.Key, out double acc) ? acc : 0d) + kv.Value;
                        if (isHitch)
                        {
                            hitchPresence[kv.Key] = (hitchPresence.TryGetValue(kv.Key, out int n) ? n : 0) + 1;
                        }

                        if (depthOf[kv.Key] == 1 && kv.Value > topMs)
                        {
                            topMs = kv.Value;
                            top = kv.Key;
                        }
                    }

                    frameTimes.Add((frame, ms, top + " " + topMs.ToString("F2", CultureInfo.InvariantCulture) + " ms"));
                }
            }

            report.frames = hitch + normal;
            report.hitchFrames = hitch;
            report.meanMs = report.frames > 0 ? (float)(sumAll / report.frames) : 0f;
            report.hitchMeanMs = hitch > 0 ? (float)(sumHitch / hitch) : 0f;
            report.normalMeanMs = normal > 0 ? (float)(sumNormal / normal) : 0f;

            var keys = new HashSet<string>(hitchSums.Keys, StringComparer.Ordinal);
            keys.UnionWith(normalSums.Keys);
            foreach (string key in keys)
            {
                float h = hitch > 0 && hitchSums.TryGetValue(key, out double hs) ? (float)(hs / hitch) : 0f;
                float n = normal > 0 && normalSums.TryGetValue(key, out double ns) ? (float)(ns / normal) : 0f;
                report.markers.Add(new MarkerRow
                {
                    marker = key,
                    depth = depthOf[key],
                    hitchMeanMs = h,
                    normalMeanMs = n,
                    deltaMs = h - n,
                    hitchFramesPresent = hitchPresence.TryGetValue(key, out int present) ? present : 0
                });
            }

            report.markers.Sort((a, b) => b.deltaMs.CompareTo(a.deltaMs));
            if (report.markers.Count > 40)
            {
                report.markers.RemoveRange(40, report.markers.Count - 40);
            }

            frameTimes.Sort((a, b) => b.ms.CompareTo(a.ms));
            for (int i = 0; i < frameTimes.Count && i < 12; i++)
            {
                report.worstFrames.Add(new FrameRow { frame = frameTimes[i].frame, frameMs = frameTimes[i].ms, top = frameTimes[i].top });
            }

            return report;
        }

        private static void TryRaiseFrameHistory(int frames)
        {
            try
            {
                Type driver = typeof(ProfilerDriver);
                System.Reflection.MethodInfo setter = driver.GetMethod("SetMaxFrameHistoryLength",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (setter != null)
                {
                    setter.Invoke(null, new object[] { frames });
                    Debug.Log("[PilotProfilerAnalysis] frame history length -> " + frames);
                    return;
                }

                System.Reflection.PropertyInfo prop = driver.GetProperty("maxHistoryLength",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(null, frames);
                    Debug.Log("[PilotProfilerAnalysis] maxHistoryLength -> " + frames);
                    return;
                }

                Debug.LogWarning("[PilotProfilerAnalysis] no frame-history setter found; default ring (300 frames) applies.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PilotProfilerAnalysis] frame history setter failed: " + e.Message);
            }
        }

        private static void Walk(HierarchyFrameDataView view, int itemId, int depth, int maxDepth, string prefix, Dictionary<string, double> perFrame, Dictionary<string, int> depthOf)
        {
            if (depth >= maxDepth)
            {
                return;
            }

            var children = new List<int>();
            view.GetItemChildren(itemId, children);
            for (int i = 0; i < children.Count; i++)
            {
                int child = children[i];
                string name = prefix + view.GetItemName(child);
                float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                perFrame[name] = (perFrame.TryGetValue(name, out double acc) ? acc : 0d) + total;
                depthOf[name] = depth + 1;
                Walk(view, child, depth + 1, maxDepth, name + " > ", perFrame, depthOf);
            }
        }

        public static string Describe(Report r)
        {
            var sb = new StringBuilder();
            sb.Append("[PilotProfilerAnalysis] ").Append(Path.GetFileName(r.input)).Append(": frames ").Append(r.frames)
              .Append(" hitch(>").Append(r.thresholdMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms) ").Append(r.hitchFrames)
              .Append(" mean ").Append(r.meanMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms, hitch mean ").Append(r.hitchMeanMs.ToString("F2", CultureInfo.InvariantCulture))
              .Append(" ms, normal mean ").Append(r.normalMeanMs.ToString("F2", CultureInfo.InvariantCulture)).AppendLine(" ms");
            sb.AppendLine("  marker (depth) : hitch mean ms / normal mean ms / delta ms / present in hitch frames");
            for (int i = 0; i < r.markers.Count && i < 25; i++)
            {
                MarkerRow m = r.markers[i];
                sb.Append("  ").Append(m.marker).Append(" (").Append(m.depth).Append(") : ")
                  .Append(m.hitchMeanMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" / ")
                  .Append(m.normalMeanMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" / ")
                  .Append(m.deltaMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" / ")
                  .Append(m.hitchFramesPresent).Append('/').Append(r.hitchFrames).AppendLine();
            }

            sb.AppendLine("  worst frames:");
            for (int i = 0; i < r.worstFrames.Count; i++)
            {
                sb.Append("  #").Append(r.worstFrames[i].frame).Append(' ').Append(r.worstFrames[i].frameMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms top: ").AppendLine(r.worstFrames[i].top);
            }

            return sb.ToString();
        }

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
