using System;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The two facts the Special Weapon director needs from the round system, and
    /// nothing else: which round it is, and how far through it the player is.
    ///
    /// Kept as a bridge rather than a direct reference because the round director is
    /// another task's file, written in the same parallel wave. Two providers and two
    /// notifications is a smaller contract than a type dependency, and it means an
    /// unbound bridge degrades to "no Special Weapon spawns on its own, the debug button
    /// still works" instead of a compile error.
    ///
    /// WIRING - three lines, owned by the round director:
    ///
    ///     SpecialWeaponRoundBridge.CurrentRoundProvider = () =&gt; _round;
    ///     SpecialWeaponRoundBridge.RoundProgressProvider = () =&gt; _killedThisRound / (float)_poolTotal;
    ///     ... and call NotifyRoundStarted / NotifyRoundEnded, plus (WP-007) NotifyRunStarted /
    ///     NotifyRunEnded so the Special Weapon process is bounded by the run, not by the scene.
    ///
    /// "Progress" is deliberately defined as pool progress, not elapsed time: the request
    /// says the drop lands between 30% and 70% "of round progress", and a round ends when
    /// the pool is exhausted, not when a clock runs out.
    /// </summary>
    public static class SpecialWeaponRoundBridge
    {
        /// <summary>Returns the current round number, 1-based. Null when no round system is running.</summary>
        public static Func<int> CurrentRoundProvider;

        /// <summary>Returns 0..1 progress through the current round's enemy pool.</summary>
        public static Func<float> RoundProgressProvider;

        /// <summary>Raised by the round director when a round begins.</summary>
        public static event Action<int> RoundStarted;

        /// <summary>Raised by the round director when a round is cleared.</summary>
        public static event Action<int> RoundEnded;

        /// <summary>
        /// WP-007 (AP-10): raised by the run director when a run starts. Argument: run
        /// serial. The Special Weapon director wipes every trace of a previous process here,
        /// so a same-scene restart cannot inherit a pickup, a runtime, a timer or a schedule.
        /// </summary>
        public static event Action<int> RunStarted;

        /// <summary>
        /// WP-007 (AP-10): raised by the run director the moment a run ends (death or run
        /// end), before the scene is torn down. The Special Weapon director cancels whatever
        /// is scheduled, on the ground or in hand right here rather than waiting for the
        /// scene load.
        /// </summary>
        public static event Action RunEnded;

        public static bool IsBound => CurrentRoundProvider != null && RoundProgressProvider != null;

        public static int CurrentRound
        {
            get
            {
                Func<int> provider = CurrentRoundProvider;
                return provider != null ? provider.Invoke() : 0;
            }
        }

        public static float RoundProgress01
        {
            get
            {
                Func<float> provider = RoundProgressProvider;
                return provider != null ? Mathf.Clamp01(provider.Invoke()) : 0f;
            }
        }

        public static void NotifyRoundStarted(int round) => RoundStarted?.Invoke(round);

        public static void NotifyRoundEnded(int round) => RoundEnded?.Invoke(round);

        public static void NotifyRunStarted(int runId) => RunStarted?.Invoke(runId);

        public static void NotifyRunEnded() => RunEnded?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CurrentRoundProvider = null;
            RoundProgressProvider = null;
            RoundStarted = null;
            RoundEnded = null;
            RunStarted = null;
            RunEnded = null;
        }
    }
}
