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
        private static readonly bool[] s_repeat = new bool[Capacity];

        // WP-069 (review R1-A #8): a trigger pull's pellets now land over several frames (projectiles), and each
        // frame's flush would resolve the same group again. Groups already dispatched in the last
        // GroupWindowSeconds (unscaled) only dispatch again when STRONGER than before (e.g. a later pellet kills):
        // one shake / hit-stop per trigger pull, escalation allowed, repetition not.
        public const float GroupWindowSeconds = 0.6f;
        private const int RecentGroupCapacity = 32;
        private static readonly int[] s_recentGroup = new int[RecentGroupCapacity];
        private static readonly float[] s_recentTime = new float[RecentGroupCapacity];
        private static readonly int[] s_recentPriority = new int[RecentGroupCapacity];
        private static readonly float[] s_recentAmplitude = new float[RecentGroupCapacity];
        private static int s_recentNext;

        /// <summary>Group winners not dispatched because the same trigger pull already shook / stopped (review R1-A #8).</summary>
        public static int SuppressedRepeats { get; private set; }

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

            float now = Time.unscaledTime;
            for (int g = 0; g < groups; g++)
            {
                s_repeat[g] = IsRepeat(s_queue[s_winnerIndices[g]], now);
            }

            int globalStopWinner = -1;
            if (snapshot.Enabled && snapshot.HitStop.enabled)
            {
                for (int g = 0; g < groups; g++)
                {
                    int index = s_winnerIndices[g];
                    if (s_repeat[g] || s_queue[index].HitStopSeconds <= 0f) continue;
                    if (globalStopWinner < 0 || Stronger(s_queue[index], s_queue[globalStopWinner], snapshot))
                        globalStopWinner = index;
                }
            }

            for (int g = 0; g < groups; g++)
            {
                int index = s_winnerIndices[g];
                FeelEvent e = s_queue[index];
                if (s_repeat[g])
                {
                    SuppressedRepeats++;
                    FeelTelemetry.RecordResolved(e, false, snapshot.Revision);
                    continue;
                }

                if (e.ShakeAmplitudeMeters > 0f || e.HitStopSeconds > 0f)
                {
                    Remember(e, now);
                }

                if (snapshot.Enabled && e.ShakeAmplitudeMeters > 0f && e.ShakeSeconds > 0f)
                {
                    // WP-095 (R3): top-down camera response is K2 / K3 / K5 from FeelEvents (TopDownFeelCamera) — the bridge would
                    // shake a second time, so top-down does not forward it. CameraRequests stays the arbitration's decision (one
                    // per trigger group, escalation allowed); the hit-stop below is unchanged. First / third person keep the shake.
                    if (CameraModeService.Current != CameraMode.TopDown)
                    {
                        CameraShakeBridge.Shake(e.ShakeAmplitudeMeters, e.ShakeSeconds, Profile(e.Kind), e.Direction);
                    }

                    CameraRequests++;
                }
                // WP-095 K1: this is the LOCAL VFX envelope (FeelConfig rows; it never writes the clock). The top-down global
                // stop is FeelTimeScale's, asked from FeelEvents (FeelHitStopTD); top-down, the profile's hitStopLocal switch
                // turns this local layer off (his sözleşmesi S5). First / third person unchanged.
                bool localStop = index == globalStopWinner && LocalStopAllowed &&
                    HitStopService.RequestForGroup(e.GroupId, ModeHitStop(e.Kind, e.HitStopSeconds),
                        snapshot.HitStop.minIntervalSeconds, snapshot.HitStop.presentationScale);
                if (localStop) AcceptedLocalStops++;
                FeelTelemetry.RecordResolved(e, localStop, snapshot.Revision);
            }
            s_count = 0;
        }

        /// <summary>
        /// WP-073 B4 (direction A4.5 §5.1): in TP / FP the hit-stop comes from the camera-mode table — kill 18 / 15 ms, armour
        /// break 35 / 30, heavy death 40 / 35, player heavy hit 20 / 15 — and FP never stops longer than 35 ms. Top-down keeps
        /// the authored (live-editable) FeelConfig value; an event without a stop stays without one.
        /// </summary>
        public static float ModeHitStop(ImpactFeelId kind, float authored)
        {
            if (authored <= 0f) return 0f;
            CameraMode mode = CameraModeService.Current;
            if (mode == CameraMode.TopDown) return authored;
            HitStopEventId id;
            switch (kind)
            {
                case ImpactFeelId.Death: id = HitStopEventId.Kill; break;
                case ImpactFeelId.HeavyDeath: id = HitStopEventId.HeavyDeath; break;
                case ImpactFeelId.ArmorBreak: id = HitStopEventId.ArmorBreak; break;
                case ImpactFeelId.PlayerDamage: id = HitStopEventId.PlayerHeavyHit; break;
                default: return mode == CameraMode.FirstPerson ? Mathf.Min(authored, CameraModesFeel.HitStopFpCapSeconds) : authored;
            }

            return CameraModesFeel.HitStopSeconds(id, mode);
        }

        /// <summary>WP-095: top-down, the local envelope follows FeelProfile_TD.hitStopLocal; first / third person always allowed.</summary>
        private static bool LocalStopAllowed => CameraModeService.Current != CameraMode.TopDown || FeelProfileTD.Active.hitStopLocal;

        /// <summary>
        /// Production late-frame order: age prior tokens before resolving new requests. WP-095 K1: then the clock arbiter
        /// (FeelTimeScale) ages its slots and writes min(hit-stop, last zombie, death beat) once, after this frame's requests.
        /// </summary>
        public static void AdvanceLateFrame(float unscaledDeltaTime)
        {
            HitStopService.Tick(unscaledDeltaTime);
            Flush();
            FeelTimeScale.AdvanceLateFrame(unscaledDeltaTime);
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
            FeelTimeScale.ResetForTests();   // WP-095 K1: the stop path's clock arbiter starts clean too (no clock write)
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

        /// <summary>Review R1-A #8: this group already shook / stopped within the window at least as strongly.</summary>
        private static bool IsRepeat(in FeelEvent e, float now)
        {
            for (int i = 0; i < RecentGroupCapacity; i++)
            {
                if (s_recentGroup[i] != e.GroupId || e.GroupId == 0 || now - s_recentTime[i] > GroupWindowSeconds) continue;
                if (e.CameraPriority > s_recentPriority[i]) return false;
                if (e.CameraPriority == s_recentPriority[i] && e.ShakeAmplitudeMeters > s_recentAmplitude[i]) return false;
                return true;
            }

            return false;
        }

        private static void Remember(in FeelEvent e, float now)
        {
            for (int i = 0; i < RecentGroupCapacity; i++)
            {
                if (s_recentGroup[i] == e.GroupId)
                {
                    s_recentTime[i] = now;
                    s_recentPriority[i] = e.CameraPriority;
                    s_recentAmplitude[i] = e.ShakeAmplitudeMeters;
                    return;
                }
            }

            s_recentGroup[s_recentNext] = e.GroupId;
            s_recentTime[s_recentNext] = now;
            s_recentPriority[s_recentNext] = e.CameraPriority;
            s_recentAmplitude[s_recentNext] = e.ShakeAmplitudeMeters;
            s_recentNext = (s_recentNext + 1) % RecentGroupCapacity;
        }

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
            AcceptedLocalStops = 0; CameraRequests = 0; SuppressedRepeats = 0;
            for (int i = 0; i < RecentGroupCapacity; i++) { s_recentGroup[i] = 0; s_recentTime[i] = 0f; s_recentPriority[i] = 0; s_recentAmplitude[i] = 0f; }
            s_recentNext = 0;
        }
    }
}
