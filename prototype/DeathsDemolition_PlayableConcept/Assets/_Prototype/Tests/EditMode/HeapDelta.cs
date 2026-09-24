using System;
using UnityEngine.Profiling;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// The EditMode form of the WP-044 allocation referee. In EditMode no Player frames run,
    /// so the frame counter (ProfilerRecorder "GC Allocated In Frame") never advances. The
    /// reading here is the Mono heap delta over a window instead, made deterministic in three
    /// ways:
    /// <list type="bullet">
    /// <item>a full collection before every window, so no GC lands inside it;</item>
    /// <item>the MINIMUM over several windows, because the heap is process-wide and another
    /// Editor thread can allocate inside one window (k2: 22 MB);</item>
    /// <item>a 64 KB positive control read the same way, so a blind reading fails the test
    /// instead of passing it.</item>
    /// </list>
    /// </summary>
    internal static class HeapDelta
    {
        public const int PositiveControlBytes = 64 * 1024;

        /// <summary>The smallest heap growth <paramref name="body"/> caused over <paramref name="windows"/> windows.</summary>
        public static long Min(Action body, int windows = 3)
        {
            long best = long.MaxValue;
            for (int window = 0; window < windows; window++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = Profiler.GetMonoUsedSizeLong();
                body();
                best = Math.Min(best, Profiler.GetMonoUsedSizeLong() - before);
            }

            return best;
        }

        /// <summary>What the same reading sees for a known 64 KB allocation (must be ≥ <see cref="PositiveControlBytes"/>).</summary>
        public static long PositiveControl()
        {
            byte[] keep = null;
            long seen = Min(() => keep = new byte[PositiveControlBytes]);
            GC.KeepAlive(keep);
            return seen;
        }
    }
}
