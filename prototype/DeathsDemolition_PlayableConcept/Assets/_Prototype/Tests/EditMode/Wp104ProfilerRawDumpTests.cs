#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// S-02 12.10 (C-00, the run-start burst): opens the Profiler binary log the PlayMode probe wrote (DD_PROBE_RAW,
    /// Wp104SpecialPickupAllocProbeTests.Bosta_ProfilLogu_KosuBasi) offline and, for the frames with the most GC.Alloc samples, groups
    /// them by the markers above them (the scripts' Update / event / coroutine samples). [DD-PROBE] lines. Explicit: by name only.
    /// </summary>
    [TestFixture]
    [Explicit]
    [Category("Diagnostics")]
    public sealed class Wp104ProfilerRawDumpTests
    {
        [Test]
        public void ProfilLogu_EnCokAyiranKareler_KimAyiriyor()
        {
            string raw = System.Environment.GetEnvironmentVariable("DD_PROBE_RAW");
            Assert.IsTrue(!string.IsNullOrEmpty(raw) && System.IO.File.Exists(raw), "DD_PROBE_RAW yok: " + raw);
            Assert.IsTrue(ProfilerDriver.LoadProfile(raw, false), "LoadProfile başarısız: " + raw);
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var kareler = new List<KeyValuePair<int, int>>();
            for (int f = first; f <= last; f++)
            {
                using (RawFrameDataView v = ProfilerDriver.GetRawFrameDataView(f, 0))
                {
                    if (v == null || !v.valid) continue;
                    int gc = v.GetMarkerId("GC.Alloc");
                    int n = 0;
                    for (int i = 0; i < v.sampleCount; i++) if (v.GetSampleMarkerId(i) == gc) n++;
                    kareler.Add(new KeyValuePair<int, int>(f, n));
                }
            }

            var rapor = new StringBuilder();
            rapor.Append("[DD-PROBE] wp104.rawDump frames=").Append(first).Append("..").Append(last).Append(" counts=")
                 .Append(string.Join(",", kareler.Select(k => k.Value)));
            foreach (KeyValuePair<int, int> k in kareler.OrderByDescending(x => x.Value).Take(3))
            {
                rapor.Append("\n  KARE ").Append(k.Key).Append(" GC.Alloc ").Append(k.Value);
                using (RawFrameDataView v = ProfilerDriver.GetRawFrameDataView(k.Key, 0))
                {
                    int gc = v.GetMarkerId("GC.Alloc");
                    var yollar = new Dictionary<string, long[]>();
                    var stack = new List<int>();
                    var kalan = new List<int>();
                    for (int i = 0; i < v.sampleCount; i++)
                    {
                        if (v.GetSampleMarkerId(i) == gc)
                        {
                            long b = 0;
                            try { b = v.GetSampleMetadataAsLong(i, 0); } catch { }
                            var yol = new StringBuilder();
                            int from = Mathf.Max(0, stack.Count - 5);
                            for (int s = from; s < stack.Count; s++) yol.Append(s == from ? "" : " > ").Append(v.GetSampleName(stack[s]));
                            string key = yol.ToString();
                            if (!yollar.TryGetValue(key, out long[] c)) yollar[key] = c = new long[2];
                            c[0]++;
                            c[1] += b;
                        }

                        if (stack.Count > 0) kalan[kalan.Count - 1]--;
                        int ch = v.GetSampleChildrenCount(i);
                        if (ch > 0)
                        {
                            stack.Add(i);
                            kalan.Add(ch);
                        }
                        else
                        {
                            while (stack.Count > 0 && kalan[kalan.Count - 1] <= 0)
                            {
                                stack.RemoveAt(stack.Count - 1);
                                kalan.RemoveAt(kalan.Count - 1);
                            }
                        }
                    }

                    foreach (var y in yollar.OrderByDescending(x => x.Value[0]).Take(12))
                        rapor.Append("\n    ").Append(y.Value[0]).Append(" / ").Append(y.Value[1]).Append(" B  ").Append(y.Key);
                }
            }

            Debug.Log(rapor.ToString());
        }
    }
}
#endif
