using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>What the HUD's gold counter does after <see cref="HudGoldReadout.Resolve"/>.</summary>
    public enum HudGoldCounterAction
    {
        /// <summary>The shown value did not change.</summary>
        None = 0,

        /// <summary>Show <see cref="HudGoldReadout.Shown"/> at once (the run's first value, or this frame's change undone before it was drawn).</summary>
        SetNow = 1,

        /// <summary>Roll to <see cref="HudGoldReadout.Shown"/> (UiCounter.RollTo: a retarget continues from the digit on screen).</summary>
        RollTo = 2
    }

    /// <summary>
    /// WP-094 HUD v4 (R3, C-00 07.10) · the gold counter's arithmetic, apart from the view so EditMode proves it: what the
    /// counter shows for a balance and the gold in flight (<see cref="HudGoldTarget.InFlight"/>), and what feedback the
    /// HUD plays this frame. No Unity object, no allocation.
    ///
    /// THE RULES
    /// - Shown = balance − in flight, never below 0.
    /// - Hold: while the balance did not drop, the counter never goes below where it stood at the start of this frame - a
    ///   coin that departed before its credit landed (a frame or more early) holds the number, it never counts down.
    /// - Same frame: a credit and its departure in one frame, in either order, cancel before anything is drawn; when the
    ///   frame's changes come back to where it started the counter is SET there (a roll begun this frame would start from
    ///   the wrong digit), unless a roll was already running from an earlier frame - then it is retargeted, which continues
    ///   from the digit on screen.
    /// - A dropping balance (a purchase, the run-end wipe) is shown as it is: the counter rolls down.
    /// - The gain: every rise adds to <see cref="GainPending"/> (every fall takes from it, never below 0); the HUD shows it
    ///   as "+N". An arrival (<see cref="NoteArrival"/>) asks for a punch; punches are at least
    ///   <see cref="HudGoldTarget.PunchMinSeconds"/> apart and the gain waits for the next punch slot, so a sweep of coins
    ///   reads as a beat of "+N"s, not a flicker. A rise with no arrival (no flight at all - the pre-WP-094 path, or a stale
    ///   release) shows its "+N" on the frame it happens.
    /// Without a flight every Resolve is the old HUD's: the first value of a run is set, each later balance rolls with its
    /// whole delta as the gain.
    /// </summary>
    public sealed class HudGoldReadout
    {
        private int _balanceSeen;
        private int _frame = -1;
        private int _frameBase;
        private bool _frameRolling;
        private float _frameRollEndsAt = -1f;
        private float _rollEndsAt = -1f;
        private float _punchAt = float.NegativeInfinity;

        /// <summary>The counter's value (its target); int.MinValue until the run's first Resolve.</summary>
        public int Shown { get; private set; } = int.MinValue;

        /// <summary>Gold risen and not yet shown as "+N".</summary>
        public int GainPending { get; private set; }

        /// <summary>An arrival waits for its punch slot.</summary>
        public bool PunchPending { get; private set; }

        /// <summary>A new run (or a rebuilt HUD): the next Resolve sets the counter without a gain.</summary>
        public void Reset()
        {
            Shown = int.MinValue;
            GainPending = 0;
            PunchPending = false;
            _balanceSeen = 0;
            _frame = -1;
            _frameBase = 0;
            _frameRolling = false;
            _frameRollEndsAt = -1f;
            _rollEndsAt = -1f;
            _punchAt = float.NegativeInfinity;
        }

        /// <summary>
        /// The counter for <paramref name="balance"/> with <paramref name="inFlight"/> still flying, on frame
        /// <paramref name="frame"/> at unscaled <paramref name="now"/>; <paramref name="rollSeconds"/> is the counter's roll.
        /// </summary>
        public HudGoldCounterAction Resolve(int balance, int inFlight, int frame, float now, float rollSeconds)
        {
            int raw = Mathf.Max(0, balance - Mathf.Max(0, inFlight));
            if (Shown == int.MinValue)
            {
                Shown = raw;
                _balanceSeen = balance;
                _frame = frame;
                _frameBase = raw;
                _frameRolling = false;
                return HudGoldCounterAction.SetNow;
            }

            if (frame != _frame)
            {
                _frame = frame;
                _frameBase = Shown;
                _frameRolling = now < _rollEndsAt;
                _frameRollEndsAt = _rollEndsAt;
            }

            int target = raw;
            if (balance >= _balanceSeen && target < _frameBase)
            {
                target = _frameBase;   // a flight never counts the number down
            }

            _balanceSeen = balance;
            if (target == Shown)
            {
                return HudGoldCounterAction.None;
            }

            GainPending = Mathf.Max(0, GainPending + (target - Shown));
            Shown = target;
            if (target == _frameBase && !_frameRolling)
            {
                _rollEndsAt = _frameRollEndsAt;   // the roll begun this frame never ran
                return HudGoldCounterAction.SetNow;
            }

            _rollEndsAt = now + rollSeconds;
            return HudGoldCounterAction.RollTo;
        }

        /// <summary>A coin landed: the next punch slot plays.</summary>
        public void NoteArrival()
        {
            PunchPending = true;
        }

        /// <summary>
        /// The feedback due at <paramref name="now"/>: a punch (with the gain gathered since the last one) once its slot is
        /// free, or a gain alone when no arrival is waiting. False when there is nothing to play this frame.
        /// </summary>
        public bool TakeFeedback(float now, out int gain, out bool punch)
        {
            gain = 0;
            punch = false;
            if (PunchPending)
            {
                if (now - _punchAt < HudGoldTarget.PunchMinSeconds)
                {
                    return false;   // the gain keeps gathering for the next slot
                }

                PunchPending = false;
                _punchAt = now;
                punch = true;
                gain = GainPending;
                GainPending = 0;
                return true;
            }

            if (GainPending <= 0)
            {
                return false;
            }

            gain = GainPending;
            GainPending = 0;
            return true;
        }
    }
}
