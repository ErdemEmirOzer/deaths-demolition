using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Diagnostics.Telemetry
{
    /// <summary>Bounded v1 feel sidecar; it does not alter the existing RunTelemetry v1 document.</summary>
    public static class FeelTelemetry
    {
        public const int SidecarSchemaVersion = 1;
        private const int MaxTraceCapacity = 2048;

        public readonly struct Trace
        {
            public Trace(FeelEvent e, bool localStop, bool mixedRevision)
            {
                Sequence = e.Sequence; FrameId = e.FrameId; GroupId = e.GroupId; Kind = e.Kind;
                SnapshotRevision = e.SnapshotRevision; LocalStop = localStop; MixedRevision = mixedRevision;
            }

            public readonly ulong Sequence;
            public readonly int FrameId;
            public readonly int GroupId;
            public readonly ImpactFeelId Kind;
            public readonly ulong SnapshotRevision;
            public readonly bool LocalStop;
            public readonly bool MixedRevision;
        }

        private static readonly Trace[] s_trace = new Trace[MaxTraceCapacity];
        private static int s_write;

        public static int ResolvedGroups { get; private set; }
        public static int AcceptedLocalStops { get; private set; }
        public static int MixedRevisionEvents { get; private set; }
        public static int StoredTraceCount { get; private set; }

        public static void RecordResolved(FeelEvent e, bool localStop, ulong activeRevision)
        {
            ResolvedGroups++;
            if (localStop) AcceptedLocalStops++;
            bool mixed = e.SnapshotRevision != activeRevision;
            if (mixed) MixedRevisionEvents++;
            TelemetryFeel settings = FeelRuntime.Current.Telemetry;
            if (!settings.captureEventTrace) return;
            int capacity = Mathf.Clamp(settings.eventTraceCapacity, 128, MaxTraceCapacity);
            s_trace[s_write % capacity] = new Trace(e, localStop, mixed);
            s_write = (s_write + 1) % capacity;
            StoredTraceCount = Mathf.Min(StoredTraceCount + 1, capacity);
        }

        public static Trace ReadOldest(int index)
        {
            if (index < 0 || index >= StoredTraceCount) return default;
            int capacity = Mathf.Clamp(FeelRuntime.Current.Telemetry.eventTraceCapacity, 128, MaxTraceCapacity);
            int start = StoredTraceCount == capacity ? s_write : 0;
            return s_trace[(start + index) % capacity];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            s_write = 0; ResolvedGroups = 0; AcceptedLocalStops = 0;
            MixedRevisionEvents = 0; StoredTraceCount = 0;
        }
    }
}
