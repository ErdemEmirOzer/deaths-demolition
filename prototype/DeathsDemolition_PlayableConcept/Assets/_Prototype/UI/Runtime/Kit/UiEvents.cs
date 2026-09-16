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

        // ---- shop & results
        UpgradePurchased = 40,
        UpgradeRerolled = 41,
        UpgradeOfferLocked = 42,
        ResultsRevealed = 43,
        LevelUp = 44,
        UnlockRevealed = 45,
        AchievementRevealed = 46,
        NewBestRound = 47,
        SettingsChanged = 48
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

        public static void Raise(UiEvent uiEvent, string detail = null)
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
