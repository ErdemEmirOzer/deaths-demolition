using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-101b (R4, S-02 measure 12.10: the run's start carried one 157 ms / 3.4 MB frame ~22 frames into Fighting) · the VFX
    /// settle scans behind the loading curtain. A world finishes building over a few frames, so <see cref="AmbientVfxSystem"/> and
    /// <see cref="LanternFlames"/> scan once more <c>SettleFrames</c> after the first scan - the FindObjectsOfType pass the player
    /// saw as a hitch. Each system announces the frame its settle scan runs on (<see cref="HoldUntil"/>); the loading flow keeps
    /// the doors shut while <see cref="Holding"/> (capped). A frame ceiling, not a counter: nothing has to be released, so a system
    /// torn down mid-wait cannot keep the curtain shut.
    /// </summary>
    public static class VfxSettleGate
    {
        private static int s_untilFrame = -1;

        /// <summary>The last frame a pending settle scan runs on (-1 = none).</summary>
        public static int UntilFrame => s_untilFrame;

        /// <summary>True while a settle scan is still to run (this frame included: the scan runs in Update, before a coroutine).</summary>
        public static bool Holding => s_untilFrame >= 0 && Time.frameCount <= s_untilFrame;

        /// <summary>A settle scan runs on <paramref name="frame"/>: hold the reveal until then (the latest of all holders wins).</summary>
        public static void HoldUntil(int frame)
        {
            if (frame > s_untilFrame) s_untilFrame = frame;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_untilFrame = -1;
    }
}
