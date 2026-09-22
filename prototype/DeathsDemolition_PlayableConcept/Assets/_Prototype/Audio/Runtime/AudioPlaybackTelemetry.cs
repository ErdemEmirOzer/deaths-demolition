#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>Development-only, fixed-capacity evidence for cue and voice lifecycle decisions.</summary>
    public enum AudioPlaybackOutcome
    {
        Requested = 0,
        MissingClip = 1,
        Started = 2,
        RetriggerBlocked = 3,
        BudgetDropped = 4,
        SameSoundRecycled = 5,
        LowerPriorityStolen = 6,
        NaturalEnd = 7,
        RequestedStop = 8,
        LoopStarted = 9,
        LoopStopRequested = 10,
        LoopEnded = 11,
    }

    public enum AudioPlaybackPool
    {
        None = 0,
        World = 1,
        Signal = 2,
        Loop = 3,
        Field = 4,
    }

    /// <summary>
    /// WP-065 evidence recorder. Record calls use fixed arrays only: no strings, LINQ,
    /// logging or collection growth occurs on the playback path. Text is materialised only
    /// when an armed capture closes.
    /// </summary>
    public static class AudioPlaybackTelemetry
    {
        private const int SoundCapacity = 384;
        private const int CombatEventCapacity = 128;
        private const int RingCapacity = 65536;
        private const int OutcomeCount = 12;

        private struct Entry
        {
            public int Sequence;
            public int Frame;
            public float Realtime;
            public double DspTime;
            public AudioPlaybackOutcome Outcome;
            public AudioPlaybackPool Pool;
            public AudioSoundId Sound;
            public AudioSoundId Victim;
            public int CombatEvent;
            public int Active;
            public int Capacity;
        }

        private static readonly int[] s_totals = new int[OutcomeCount];
        private static readonly int[,] s_soundTotals = new int[OutcomeCount, SoundCapacity];
        private static readonly int[] s_combatDelivered = new int[CombatEventCapacity];
        private static readonly int[] s_combatSuppressed = new int[CombatEventCapacity];
        private static readonly Entry[] s_ring = new Entry[RingCapacity];
        private static int s_sequence;
        private static int s_overflow;

        public static int Total(AudioPlaybackOutcome outcome) => s_totals[(int)outcome];
        public static int OverflowCount => s_overflow;

        public static void Reset()
        {
            Array.Clear(s_totals, 0, s_totals.Length);
            Array.Clear(s_soundTotals, 0, s_soundTotals.Length);
            Array.Clear(s_combatDelivered, 0, s_combatDelivered.Length);
            Array.Clear(s_combatSuppressed, 0, s_combatSuppressed.Length);
            Array.Clear(s_ring, 0, s_ring.Length);
            s_sequence = 0;
            s_overflow = 0;
        }

        public static void Record(
            AudioPlaybackOutcome outcome,
            AudioPlaybackPool pool,
            AudioSoundId sound,
            AudioSoundId victim = AudioSoundId.None,
            int active = 0,
            int capacity = 0,
            CombatAudioEvent? combatEvent = null)
        {
            int outcomeIndex = (int)outcome;
            s_totals[outcomeIndex]++;

            int soundIndex = (int)sound;
            if (soundIndex >= 0 && soundIndex < SoundCapacity)
            {
                s_soundTotals[outcomeIndex, soundIndex]++;
            }

            int sequence = s_sequence++;
            if (sequence >= RingCapacity) s_overflow++;
            s_ring[sequence % RingCapacity] = new Entry
            {
                Sequence = sequence,
                Frame = Time.frameCount,
                Realtime = Time.realtimeSinceStartup,
                DspTime = AudioSettings.dspTime,
                Outcome = outcome,
                Pool = pool,
                Sound = sound,
                Victim = victim,
                CombatEvent = combatEvent.HasValue ? (int)combatEvent.Value : -1,
                Active = active,
                Capacity = capacity,
            };
        }

        public static void RecordCombatCue(CombatAudioEvent evt, bool suppressed)
        {
            int index = (int)evt;
            if (index < 0 || index >= CombatEventCapacity)
            {
                return;
            }

            if (suppressed) s_combatSuppressed[index]++;
            else s_combatDelivered[index]++;
        }

        public static void WriteReport(string path)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# wp-065 audio playback telemetry v1");
                writer.WriteLine("# fixed_ring_capacity\t" + RingCapacity);
                writer.WriteLine("# ring_overflow\t" + s_overflow);
                writer.WriteLine("# outcome\tcount");
                for (int i = 0; i < OutcomeCount; i++)
                {
                    writer.WriteLine(((AudioPlaybackOutcome)i) + "\t" + s_totals[i]);
                }

                writer.WriteLine("# sound\trequested\tmissing_clip\tstarted\tretrigger_blocked\tbudget_dropped\trecycled\tstolen\tnatural_end\trequested_stop\tloop_started\tloop_stop_requested\tloop_ended");
                for (int sound = 0; sound < SoundCapacity; sound++)
                {
                    int sum = 0;
                    for (int outcome = 0; outcome < OutcomeCount; outcome++) sum += s_soundTotals[outcome, sound];
                    if (sum == 0) continue;

                    writer.Write(((AudioSoundId)sound).ToString());
                    for (int outcome = 0; outcome < OutcomeCount; outcome++) writer.Write("\t" + s_soundTotals[outcome, sound]);
                    writer.WriteLine();
                }

                writer.WriteLine("# combat_event\tdelivered\tsuppressed");
                for (int evt = 0; evt < CombatEventCapacity; evt++)
                {
                    if (s_combatDelivered[evt] == 0 && s_combatSuppressed[evt] == 0) continue;
                    writer.WriteLine(((CombatAudioEvent)evt) + "\t" + s_combatDelivered[evt] + "\t" + s_combatSuppressed[evt]);
                }

                writer.WriteLine("# sequence\tframe\trealtime\tdsp_time\toutcome\tpool\tsound\tvictim\tcombat_event\tactive\tcapacity");
                int first = Math.Max(0, s_sequence - RingCapacity);
                for (int sequence = first; sequence < s_sequence; sequence++)
                {
                    Entry entry = s_ring[sequence % RingCapacity];
                    if (entry.Sequence != sequence) continue;
                    writer.Write(entry.Sequence); writer.Write('\t');
                    writer.Write(entry.Frame); writer.Write('\t');
                    writer.Write(entry.Realtime.ToString("F6", CultureInfo.InvariantCulture)); writer.Write('\t');
                    writer.Write(entry.DspTime.ToString("F6", CultureInfo.InvariantCulture)); writer.Write('\t');
                    writer.Write(entry.Outcome); writer.Write('\t');
                    writer.Write(entry.Pool); writer.Write('\t');
                    writer.Write(entry.Sound); writer.Write('\t');
                    writer.Write(entry.Victim); writer.Write('\t');
                    writer.Write(entry.CombatEvent >= 0 ? ((CombatAudioEvent)entry.CombatEvent).ToString() : "-"); writer.Write('\t');
                    writer.Write(entry.Active); writer.Write('\t');
                    writer.WriteLine(entry.Capacity);
                }
            }
        }
    }
}
#endif
