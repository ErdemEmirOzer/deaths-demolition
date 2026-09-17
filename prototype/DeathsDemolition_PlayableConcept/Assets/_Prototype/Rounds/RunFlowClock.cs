using System;
using System.Collections.Generic;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The one owner of "the simulation is stopped".
    ///
    /// Three different systems want to freeze the world in this prototype - the
    /// round-end upgrade screen, the pause menu and the results screen - and they can
    /// overlap. Pausing while the upgrade screen is open and then resuming must NOT
    /// start the enemies moving under an open shop. A single boolean cannot express
    /// that; a set of named holds can.
    ///
    ///     RunFlowClock.AddHold(RunFlowClock.PauseHold);      // pause menu opens
    ///     RunFlowClock.RemoveHold(RunFlowClock.PauseHold);   // pause menu closes
    ///
    /// The clock is frozen while at least one hold is registered, and only the last
    /// release gives the time scale back.
    ///
    /// G-08: THE PAUSE MENU MUST USE THIS INSTEAD OF Time.timeScale. Writing the time
    /// scale directly is not a compile error and not a visible bug until the day a
    /// pause overlaps a round end, which is exactly the case a player will find first.
    ///
    /// Hit-stop safety: <see cref="HitStopService"/> also owns the time scale for a few
    /// milliseconds at a time, <see cref="DeathSequence"/> for the death beat. Freezing
    /// cancels either first and restores its base scale rather than the 0.06 / 0.3 it
    /// happened to be showing.
    /// </summary>
    public static class RunFlowClock
    {
        /// <summary>Hold id used by the round-end freeze (round complete + upgrade screen).</summary>
        public const string RoundEndHold = "round-end";

        /// <summary>Hold id the pause menu should use.</summary>
        public const string PauseHold = "pause";

        /// <summary>Hold id the Run Results screen should use if it wants the world still.</summary>
        public const string ResultsHold = "results";

        /// <summary>Hold id the F1 panel uses for its manual freeze toggle.</summary>
        public const string DebugHold = "debug";

        private static readonly List<string> s_holds = new List<string>(4);

        private static float s_restoreScale = 1f;

        /// <summary>True while at least one hold is registered and the time scale is 0.</summary>
        public static bool IsFrozen => s_holds.Count > 0;

        /// <summary>Number of holds currently registered. F1 readout.</summary>
        public static int HoldCount => s_holds.Count;

        /// <summary>Raised when the frozen state actually flips. Not raised per hold.</summary>
        public static event Action<bool> FrozenChanged;

        public static bool HasHold(string id)
        {
            return !string.IsNullOrEmpty(id) && s_holds.Contains(id);
        }

        /// <summary>Register a hold. Repeating the same id is a no-op, so this is safe to call twice.</summary>
        public static void AddHold(string id)
        {
            if (string.IsNullOrEmpty(id) || s_holds.Contains(id))
            {
                return;
            }

            bool wasFrozen = IsFrozen;
            s_holds.Add(id);

            if (wasFrozen)
            {
                return;
            }

            // A hit stop owns the clock for ~45 ms at a time. Ending it first means the
            // scale we remember is the real one, not the 0.06 of the freeze frame. The
            // WP-059 death beat is the same story with 0.3 for a second and a half.
            HitStopService.Cancel();
            DeathSequence.Cancel();

            s_restoreScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
            Time.timeScale = 0f;

            Raise(true);
        }

        /// <summary>Release a hold. The clock only restarts when the last one goes.</summary>
        public static void RemoveHold(string id)
        {
            if (string.IsNullOrEmpty(id) || !s_holds.Remove(id))
            {
                return;
            }

            if (s_holds.Count > 0)
            {
                return;
            }

            Restore();
            Raise(false);
        }

        /// <summary>Drop every hold. Used on run teardown and scene changes.</summary>
        public static void ClearHolds()
        {
            if (s_holds.Count == 0)
            {
                return;
            }

            s_holds.Clear();
            Restore();
            Raise(false);
        }

        /// <summary>Comma separated hold list. F1 readout.</summary>
        public static string DescribeHolds()
        {
            return s_holds.Count == 0 ? "running" : string.Join(", ", s_holds);
        }

        private static void Restore()
        {
            // Only give the clock back if it is still ours. If something else took it
            // while we were frozen, writing 1 here would be a silent un-pause.
            if (Time.timeScale <= 0.0001f)
            {
                Time.timeScale = s_restoreScale > 0.01f ? s_restoreScale : 1f;
            }
        }

        private static void Raise(bool frozen)
        {
            Action<bool> handler = FrozenChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(frozen);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_holds.Clear();
            s_restoreScale = 1f;
            FrozenChanged = null;

            // Entering play mode with a time scale left at 0 by a previous session is a
            // dead editor, not a paused game.
            if (Time.timeScale <= 0.0001f)
            {
                Time.timeScale = 1f;
            }
        }
    }
}
