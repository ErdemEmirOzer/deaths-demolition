using System;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-107 (R4): the wall buy bus. <see cref="PromptChanged"/> fires when the station in reach (or its offer) changes -
    /// S-09's HUD prompt; <see cref="Resolved"/> fires once per press - S-08's purchase / refill / denied sounds at the
    /// station. Static like <c>WeaponEvents</c>; the domain reload clears the listeners, so a scene listener subscribes in
    /// its OnEnable (static bus rearm lesson).
    /// </summary>
    public static class WallBuyEvents
    {
        public static event Action<WallBuyPrompt> PromptChanged;
        public static event Action<WallBuyOutcome> Resolved;

        /// <summary>The prompt last published (<c>Visible</c> false when no station is in reach).</summary>
        public static WallBuyPrompt CurrentPrompt { get; private set; }

        /// <summary>Presses resolved since the last reset (test and probe readout).</summary>
        public static int ResolvedCount { get; private set; }

        public static void PublishPrompt(in WallBuyPrompt prompt)
        {
            if (prompt.SameAs(CurrentPrompt)) return;
            CurrentPrompt = prompt;
            PromptChanged?.Invoke(prompt);
        }

        public static void PublishResolved(in WallBuyOutcome outcome)
        {
            ResolvedCount++;
            Resolved?.Invoke(outcome);
        }

        /// <summary>Clear the published state (a run ends, a test starts). Listeners stay subscribed.</summary>
        public static void ClearState()
        {
            if (CurrentPrompt.Visible)
            {
                CurrentPrompt = default;
                PromptChanged?.Invoke(default);
            }

            CurrentPrompt = default;
            ResolvedCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PromptChanged = null;
            Resolved = null;
            CurrentPrompt = default;
            ResolvedCount = 0;
        }
    }
}
