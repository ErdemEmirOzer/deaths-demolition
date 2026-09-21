using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.Telemetry;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>Fixed, allocation-free presentation request. Gameplay ledgers never enter this queue.</summary>
    public readonly struct FeelEvent
    {
        public FeelEvent(ulong sequence, int frameId, int groupId, ImpactFeelId kind,
            Vector3 point, Vector3 direction, ulong snapshotRevision, ImpactFeel row, float amplitudeScale)
        {
            Sequence = sequence; FrameId = frameId; GroupId = groupId; Kind = kind;
            Point = point; Direction = direction; SnapshotRevision = snapshotRevision;
            CameraPriority = row.cameraPriority; ShakeAmplitudeMeters = row.shakeAmplitudeMeters * Mathf.Clamp01(amplitudeScale);
            ShakeSeconds = row.shakeSeconds; HitStopSeconds = row.hitStopSeconds;
        }

        public readonly ulong Sequence;
        public readonly int FrameId;
        public readonly int GroupId;
        public readonly ImpactFeelId Kind;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly ulong SnapshotRevision;
        public readonly int CameraPriority;
        public readonly float ShakeAmplitudeMeters;
        public readonly float ShakeSeconds;
        public readonly float HitStopSeconds;
    }

    /// <summary>Single same-frame camera/local-stop arbiter.</summary>
    public static class FeelEventRouter
    {
        public const int Capacity = 256;
        private static readonly FeelEvent[] s_queue = new FeelEvent[Capacity];
        private static readonly int[] s_groupIds = new int[Capacity];
        private static readonly int[] s_winnerIndices = new int[Capacity];
        private static int s_count;
        private static int s_nextGroup;
        private static ulong s_sequence;

        public static int QueueOverflow { get; private set; }
        public static int AcceptedLocalStops { get; private set; }
        public static int CameraRequests { get; private set; }

        public static int NextGroupId()
        {
            s_nextGroup++; if (s_nextGroup <= 0) s_nextGroup = 1; return s_nextGroup;
        }

        public static void Enqueue(ImpactFeelId kind, int groupId, Vector3 point, Vector3 direction, float amplitudeScale = 1f)
        {
            FeelSnapshot snapshot = FeelRuntime.Current;
            s_sequence++;
            int resolvedGroup = groupId != 0 ? groupId : unchecked((int)(s_sequence & 0x7fffffff));
            var incoming = new FeelEvent(s_sequence, Time.frameCount, resolvedGroup, kind, point, direction,
                snapshot.Revision, snapshot.Impact(kind), amplitudeScale);
            if (s_count < Capacity) { s_queue[s_count++] = incoming; return; }

            int weakest = 0;
            for (int i = 1; i < s_count; i++) if (Weaker(s_queue[i], s_queue[weakest], snapshot)) weakest = i;
            if (Weaker(incoming, s_queue[weakest], snapshot)) { QueueOverflow++; return; }
            s_queue[weakest] = incoming; QueueOverflow++;
        }

        public static void Flush()
        {
            if (s_count == 0) return;
            FeelSnapshot snapshot = FeelRuntime.Current;
            int groups = 0;
            for (int i = 0; i < s_count; i++)
            {
                FeelEvent candidate = s_queue[i]; int slot = -1;
                for (int g = 0; g < groups; g++) if (s_groupIds[g] == candidate.GroupId) { slot = g; break; }
                if (slot < 0) { slot = groups++; s_groupIds[slot] = candidate.GroupId; s_winnerIndices[slot] = i; }
                else if (Stronger(candidate, s_queue[s_winnerIndices[slot]], snapshot)) s_winnerIndices[slot] = i;
            }

            int globalStopWinner = -1;
            if (snapshot.Enabled && snapshot.HitStop.enabled)
            {
                for (int g = 0; g < groups; g++)
                {
                    int index = s_winnerIndices[g];
                    if (s_queue[index].HitStopSeconds <= 0f) continue;
                    if (globalStopWinner < 0 || Stronger(s_queue[index], s_queue[globalStopWinner], snapshot))
                        globalStopWinner = index;
                }
            }

            for (int g = 0; g < groups; g++)
            {
                int index = s_winnerIndices[g];
                FeelEvent e = s_queue[index];
                if (snapshot.Enabled && e.ShakeAmplitudeMeters > 0f && e.ShakeSeconds > 0f)
                {
                    CameraShakeBridge.Shake(e.ShakeAmplitudeMeters, e.ShakeSeconds, Profile(e.Kind), e.Direction);
                    CameraRequests++;
                }
                bool localStop = index == globalStopWinner &&
                    HitStopService.RequestForGroup(e.GroupId, e.HitStopSeconds,
                        snapshot.HitStop.minIntervalSeconds, snapshot.HitStop.presentationScale);
                if (localStop) AcceptedLocalStops++;
                FeelTelemetry.RecordResolved(e, localStop, snapshot.Revision);
            }
            s_count = 0;
        }

        /// <summary>Production late-frame order: age prior tokens before resolving new requests.</summary>
        public static void AdvanceLateFrame(float unscaledDeltaTime)
        {
            HitStopService.Tick(unscaledDeltaTime);
            Flush();
        }

        public static void Clear()
        {
            s_count = 0;
            HitStopService.Cancel();
            CameraShakeService.StopAll();
        }

#if UNITY_EDITOR
        public static void ResetForTests()
        {
            Reset();
            HitStopService.ResetForTests();
        }
#endif

        private static bool Stronger(in FeelEvent a, in FeelEvent b, FeelSnapshot snapshot)
        {
            if (a.CameraPriority != b.CameraPriority) return a.CameraPriority > b.CameraPriority;
            if (a.ShakeAmplitudeMeters != b.ShakeAmplitudeMeters) return a.ShakeAmplitudeMeters > b.ShakeAmplitudeMeters;
            if (a.ShakeSeconds != b.ShakeSeconds) return a.ShakeSeconds > b.ShakeSeconds;
            return a.Sequence < b.Sequence;
        }

        private static bool Weaker(in FeelEvent a, in FeelEvent b, FeelSnapshot snapshot) => Stronger(b, a, snapshot);

        private static ShakeProfile Profile(ImpactFeelId id)
        {
            switch (id)
            {
                case ImpactFeelId.HeavyDeath: return ShakeProfile.HeavyDeath;
                case ImpactFeelId.Explosion: return ShakeProfile.Explosion;
                case ImpactFeelId.PlayerDamage: return ShakeProfile.PlayerDamage;
                default: return ShakeProfile.ArmorBreak;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_count = 0; s_nextGroup = 0; s_sequence = 0; QueueOverflow = 0;
            AcceptedLocalStops = 0; CameraRequests = 0;
        }
    }
}
