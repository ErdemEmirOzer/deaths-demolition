using System;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The run-flow and economy sounds. Deliberately a second enum rather than more
    /// values on <c>CombatAudioEvent</c>: that enum is the weapon/damage contract and
    /// says in its own header that gold, round countdown and UI belong to other tasks.
    /// This is that other task's half of the same contract.
    ///
    /// The request's "at least these events must be separable" list maps as:
    ///   Gold collection        -> GoldCollected
    ///   Round start countdown  -> CountdownTick / RoundStart
    ///   Round complete         -> RoundComplete
    /// plus the two the upgrade screen needs to feel like a purchase and not a click.
    /// </summary>
    public enum RunAudioEvent
    {
        /// <summary>One gold pickup landed in the wallet. Pitch may rise with a collection streak.</summary>
        GoldCollected = 0,

        /// <summary>An orb appeared on the ground (magnet / contact modes only).</summary>
        GoldSpawned = 1,

        /// <summary>The pool ran dry; only stragglers are left.</summary>
        RoundPoolExhausted = 10,

        /// <summary>The round is over. The tension layer should drop here.</summary>
        RoundComplete = 11,

        /// <summary>One second boundary of the pre-round countdown.</summary>
        CountdownTick = 12,

        /// <summary>The countdown finished and the next round is live.</summary>
        RoundStart = 13,

        /// <summary>An upgrade card was bought.</summary>
        UpgradePurchased = 20,

        /// <summary>The player pressed buy without enough gold.</summary>
        UpgradeRefused = 21,

        /// <summary>The upgrade screen was closed without buying.</summary>
        UpgradeSkipped = 22,

        /// <summary>The run ended. Played once, over the death beat.</summary>
        RunEnded = 30
    }

    /// <summary>One request for a run-flow sound. Passed by value; the listener may ignore it.</summary>
    public readonly struct RunAudioCue
    {
        public readonly RunAudioEvent Event;

        /// <summary><see cref="Vector3.zero"/> means non-positional.</summary>
        public readonly Vector3 Position;

        /// <summary>0..1-ish loudness / variation hint. 1 is the nominal presentation.</summary>
        public readonly float Intensity;

        /// <summary>
        /// Rises with consecutive pickups inside the streak window, so a magnet sweep
        /// through six orbs reads as one rising arpeggio instead of six identical dings.
        /// Zero for everything that is not gold.
        /// </summary>
        public readonly int StreakIndex;

        public RunAudioCue(RunAudioEvent evt, Vector3 position, float intensity, int streakIndex)
        {
            Event = evt;
            Position = position;
            Intensity = intensity;
            StreakIndex = streakIndex;
        }
    }

    /// <summary>
    /// One-way audio surface for the run flow. Producers raise, the audio task (G-09)
    /// subscribes once and owns everything else.
    /// </summary>
    public static class RunAudioEvents
    {
        public static event Action<RunAudioCue> Cue;

        public static void Raise(RunAudioEvent evt, Vector3 position = default, float intensity = 1f,
            int streakIndex = 0)
        {
            Action<RunAudioCue> handler = Cue;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(new RunAudioCue(evt, position, intensity, streakIndex));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cue = null;
        }
    }
}
