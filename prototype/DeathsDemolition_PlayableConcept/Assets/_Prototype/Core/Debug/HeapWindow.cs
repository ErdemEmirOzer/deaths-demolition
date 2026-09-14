using System;
using UnityEngine.Profiling;

namespace DD.Prototype.Core.DebugTools
{
    /// <summary>
    /// WP-010 (D-1): the heap-delta measurement the development probes and the allocation
    /// tests share, made robust against a loaded machine. Two counters are read together
    /// (<see cref="GC.GetTotalMemory"/> and <see cref="Profiler.GetMonoUsedSizeLong"/> - the
    /// second reports 0 in a release Player); <see cref="Prepare"/> empties the nursery so a
    /// collection that was already due does not land inside the next window; a window that
    /// still sees a gen0 sweep is re-measured once after another collection. The verdict
    /// belongs to the caller: read the delta together with the gen0 count and a positive
    /// control (a known allocation must move the counters, or the measurement is blind).
    /// Diagnostics only - never on a gameplay path.
    /// </summary>
    public static class HeapWindow
    {
        public readonly struct Result
        {
            public Result(long gcDelta, long monoDelta, int gen0, int retries)
            {
                GcDelta = gcDelta;
                MonoDelta = monoDelta;
                Gen0 = gen0;
                Retries = retries;
            }

            /// <summary>Change of <see cref="GC.GetTotalMemory"/>(false) across the window, bytes.</summary>
            public long GcDelta { get; }

            /// <summary>Change of <see cref="Profiler.GetMonoUsedSizeLong"/> across the window, bytes (0 in a release Player).</summary>
            public long MonoDelta { get; }

            /// <summary>Gen0 collections that ran inside the (last) window.</summary>
            public int Gen0 { get; }

            /// <summary>How many times the window was re-measured because a gen0 landed inside it.</summary>
            public int Retries { get; }

            /// <summary>The larger of the two deltas - the number a threshold is applied to.</summary>
            public long Delta => Math.Max(GcDelta, MonoDelta);
        }

        /// <summary>Full collection so the next window starts with an empty nursery.</summary>
        public static void Prepare()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Measure <paramref name="work"/>. If a gen0 collection lands inside the window and
        /// <paramref name="maxRetries"/> allows, collect and measure the same work again; the
        /// result is the last measurement, with the retry count.
        /// </summary>
        public static Result Measure(Action work, int maxRetries = 1)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            int retries = 0;
            while (true)
            {
                int gen0Before = GC.CollectionCount(0);
                long gcBefore = GC.GetTotalMemory(false);
                long monoBefore = Profiler.GetMonoUsedSizeLong();

                work();

                long gcDelta = GC.GetTotalMemory(false) - gcBefore;
                long monoDelta = Profiler.GetMonoUsedSizeLong() - monoBefore;
                int gen0 = GC.CollectionCount(0) - gen0Before;

                if (gen0 == 0 || retries >= maxRetries)
                {
                    return new Result(gcDelta, monoDelta, gen0, retries);
                }

                retries++;
                GC.Collect();
            }
        }
    }
}
