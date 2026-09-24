using System;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Every player-facing UI interaction, named (WP-022B, directive §12 "everything has
    /// its own sound and effect"). One row per member in
    /// <c>planning/work-packages/EVENT-COVERAGE-MATRIX-UI.md</c> (SFX event name, VFX /
    /// micro-animation, source); <c>UiEventsTests</c> keeps the enum and the matrix equal.
    /// </summary>
    public enum UiEvent
    {
        // ---- navigation & controls
        FocusMoved = 0,
        ButtonHover = 1,
        ButtonPress = 2,
        Confirm = 3,
        Back = 4,
        Denied = 5,
        ScreenEntered = 6,
        ScreenLeft = 7,
        PaneOpened = 8,
        ChoiceSelected = 9,
        SliderChanged = 10,
        ToggleChanged = 11,
        DialogOpened = 12,
        DialogClosed = 13,

        // ---- WP-068 UI v3 (slice 11, S-08 contract; raised by the v3 kit, UiCurtain and the cue timelines)
        /// <summary>A screen starts its exit (Exit 140 ms), before the next ScreenEntered.</summary>
        ScreenExiting = 14,
        /// <summary>The curtain doors have shut - raised at least one frame BEFORE the synchronous LoadScene, so the tail sounds through the freeze.</summary>
        CurtainClosed = 15,
        /// <summary>The curtain opens on the new scene's first rendered frame.</summary>
        CurtainOpened = 16,
        /// <summary>Pointer-down on a Primary / Secondary button (the lacquer tick; Confirm follows on release).</summary>
        PressDown = 17,
        /// <summary>Boot seal contact (ink spread → seal → one rin strike, U7). Every launch; skippable.</summary>
        BootLogo = 18,
        /// <summary>A hero timeline was skipped (Complete()): UI loops fade in 30 ms, the boot seal in 120 ms (U7).</summary>
        TimelineSkipped = 19,

        // ---- HUD
        HudRoundStarted = 20,
        HudRoundCompleted = 21,
        HudPoolExhausted = 22,
        HudLowHealthEntered = 23,
        HudLowHealthLeft = 24,
        HudMagazineEmpty = 25,
        HudSpecialSpawned = 26,
        HudSpecialPickedUp = 27,
        HudSpecialFinalSeconds = 28,
        HudSpecialEnded = 29,
        HudSpecialExpired = 30,
        HudThreatEncircled = 31,
        HudThreatRearAttack = 32,
        HudThreatStrongVersion = 33,
        HudThreatWindup = 34,
        HudDamageEdge = 35,
        /// <summary>WP-068 U6: the NEW BEST ROUND ribbon in the banner slot during the run.</summary>
        HudNewBestRound = 36,
        /// <summary>"ELITE DOWN" stamp (detail-free); lacquer tok + short dark bell, no duck.</summary>
        HudEliteDown = 37,
        /// <summary>"ULTRA DOWN" stamp; the elite voice + gold-leaf shimmer.</summary>
        HudUltraDown = 38,

        // ---- shop & results
        UpgradePurchased = 40,
        UpgradeRerolled = 41,
        UpgradeOfferLocked = 42,
        ResultsRevealed = 43,
        LevelUp = 44,
        UnlockRevealed = 45,
        AchievementRevealed = 46,
        NewBestRound = 47,
        SettingsChanged = 48,

        // ---- WP-068 UI v3 · cards, stamps, counters, hold (slice 11)
        /// <summary>One card dealt (detail = deal index "0"/"1"/"2"; "last" adds the settle tok). 60 ms stagger.</summary>
        CardDealt = 49,
        /// <summary>A changed card turns on reroll (at scaleX = 0), one rustle per card.</summary>
        CardFlipped = 50,
        /// <summary>Hanko contact frame (purchase, medal, profile seal, level). Detail "rare" / "epic" adds the tier layer.</summary>
        StampLanded = 51,
        /// <summary>A counter starts rolling (≤ 0.6 s; one per group).</summary>
        CounterRolling = 52,
        /// <summary>The counter settles (stops the roll, one tok).</summary>
        CounterSettled = 53,
        /// <summary>Hold-to-confirm started (REROLL U5; also RESET ALL BINDINGS). Completion raises Confirm, early release Back.</summary>
        RerollHoldStarted = 54
    }

    /// <summary>
    /// The UI's outbound event contract. Screens raise; the audio owner (S-08) and any
    /// effect owner subscribe. Raising never throws into the caller and never allocates
    /// beyond the delegate call; the optional detail is a short, already-existing string
    /// (a screen name, a weapon id) - never formatted per frame.
    /// </summary>
    public static class UiEvents
    {
        public static event Action<UiEvent, string> Raised;

        /// <summary>Count of raises since the last reset - tests read it; nothing else does.</summary>
        public static int RaisedCount { get; private set; }

        /// <summary>The last raised event and detail (tests, F1 readout).</summary>
        public static UiEvent Last { get; private set; }
        public static string LastDetail { get; private set; } = string.Empty;

        /// <summary>WP-068 slice 11: the normalised value of the last SliderChanged (0..1), −1 when the raise carried none. The audio maps it to the tick's pitch.</summary>
        public static float LastValue01 { get; private set; } = -1f;

        /// <summary>WP-068 slice 11: a raise that also carries a normalised value (sliders: the tick's pitch follows the value).</summary>
        public static void Raise(UiEvent uiEvent, string detail, float value01)
        {
            LastValue01 = Mathf.Clamp01(value01);
            RaiseCore(uiEvent, detail);
        }

        public static void Raise(UiEvent uiEvent, string detail = null)
        {
            LastValue01 = -1f;
            RaiseCore(uiEvent, detail);
        }

        private static void RaiseCore(UiEvent uiEvent, string detail)
        {
            RaisedCount++;
            Last = uiEvent;
            LastDetail = detail ?? string.Empty;

            Action<UiEvent, string> handlers = Raised;
            if (handlers == null)
            {
                return;
            }

            try
            {
                handlers(uiEvent, LastDetail);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Test seam.</summary>
        public static void ResetCounters()
        {
            RaisedCount = 0;
            LastValue01 = -1f;
            Last = UiEvent.FocusMoved;
            LastDetail = string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Raised = null;
            ResetCounters();
        }
    }
}
