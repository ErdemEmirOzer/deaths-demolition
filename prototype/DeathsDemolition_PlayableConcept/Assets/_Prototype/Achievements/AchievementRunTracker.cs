using System;
using DD.Prototype.Damage;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Achievements
{
    /// <summary>
    /// Listens to the run for the feats the achievement rules need and that the run
    /// summary does not already count (special-weapon pickups, flawless rounds). Pure
    /// observer: it subscribes to existing events and changes nothing in the systems it
    /// watches.
    ///
    /// The counters are BOUND to the run's completion token: they start at zero when a
    /// run starts and are handed out only for that token, so a summary of another run
    /// (a replay, a hand-built test summary) never picks up a live run's feats. The
    /// tracker is not cleared when the profile commits - a failed save may retry the
    /// same token (WP-009H) and must see the same counters; the next run start resets.
    /// </summary>
    public static class AchievementRunTracker
    {
        private static bool s_installed;
        private static string s_token;
        private static RunFeatCounters s_counters;
        private static bool s_damagedThisRound;
        private static bool s_roundOpen;

        /// <summary>The token the current counters belong to; null outside a run.</summary>
        public static string BoundToken => s_token;

        /// <summary>The live counters, for the F1 panel and tests.</summary>
        public static RunFeatCounters Current => s_counters;

        public static bool DamagedThisRound => s_damagedThisRound;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;
            WeaponEvents.SpecialWeaponPickedUp += OnSpecialPickedUp;
            DamageEvents.PlayerAttacked += OnPlayerAttacked;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_installed = false;
            Clear();
        }

        /// <summary>
        /// The counters for <paramref name="completionId"/>, or none when the tracker is
        /// bound to a different run (or to no run at all).
        /// </summary>
        public static RunFeatCounters CountersFor(string completionId)
        {
            if (string.IsNullOrEmpty(completionId) || s_token == null ||
                !string.Equals(s_token, completionId, StringComparison.Ordinal))
            {
                return RunFeatCounters.None;
            }

            return s_counters.Clamped();
        }

        /// <summary>Starts counting for the run that holds the live completion capability.</summary>
        public static void Begin(string completionId)
        {
            s_token = string.IsNullOrEmpty(completionId) ? null : completionId;
            s_counters = RunFeatCounters.None;
            s_damagedThisRound = false;
            s_roundOpen = false;
        }

        public static void Clear()
        {
            s_token = null;
            s_counters = RunFeatCounters.None;
            s_damagedThisRound = false;
            s_roundOpen = false;
        }

        // ------------------------------------------------------------- observers

        private static void OnRunStarted(int startingRound)
        {
            Begin(CompletionAuthority.LiveToken);
        }

        private static void OnRoundStarted(int round)
        {
            s_damagedThisRound = false;
            s_roundOpen = true;
        }

        private static void OnRoundCompleted(int round)
        {
            if (s_token == null)
            {
                return;
            }

            if (s_roundOpen && !s_damagedThisRound)
            {
                s_counters.FlawlessRounds = ProfileProgression.SaturatingAdd(s_counters.FlawlessRounds, 1);
            }

            s_roundOpen = false;
        }

        private static void OnSpecialPickedUp(float activeSeconds)
        {
            if (s_token == null)
            {
                return;
            }

            s_counters.SpecialWeaponPickups = ProfileProgression.SaturatingAdd(s_counters.SpecialWeaponPickups, 1);
        }

        private static void OnPlayerAttacked(EnemyAttackLandedInfo info)
        {
            s_damagedThisRound = true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Editor / development seam: feed counters directly (tests, probes).</summary>
        public static class TestSeam
        {
            public static void ArmFor(string completionId, RunFeatCounters counters)
            {
                Begin(completionId);
                s_counters = counters;
            }

            public static void MarkDamaged()
            {
                s_damagedThisRound = true;
            }
        }
#endif
    }
}
