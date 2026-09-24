using DD.Prototype.CameraSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 B8 · one Player-log line per completed round with the AI v3 counters (telemetry on or off; WP-078 soak /
    /// bot summaries read it): camera mode, rear beats, rear hits warned / unwarned + mean beat → hit lead (§6 fairness,
    /// FP target ≥ 800 ms), lead holds, grabs started / escaped, shocks, gel slides / falls / knockdowns, corpses buried
    /// early by the ceiling. Round deltas; nothing allocates on the gameplay path (the string is built once per round).
    /// </summary>
    public static class AiV3RoundLog
    {
        private struct Snap
        {
            public int rearBeats, warned, unwarned, holds, grabs, escapes, shocks, slides, falls, knockdowns, evicted;
            public double leadMs;
        }

        private static Snap s_base;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bind()
        {
            RunEvents.RoundStarted -= OnRoundStarted; RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted -= OnRoundCompleted; RunEvents.RoundCompleted += OnRoundCompleted;
        }

        private static Snap Take() => new Snap
        {
            rearBeats = ZombieEnemy.RearBeats, warned = ZombieEnemy.RearHitsWarned, unwarned = ZombieEnemy.RearHitsUnwarned, holds = ZombieEnemy.RearLeadHolds,
            leadMs = ZombieEnemy.RearLeadMsSum, grabs = GrabState.StartedCount, escapes = GrabState.EscapedCount, shocks = ZombieEnemy.ShocksApplied,
            slides = ZombieEnemy.SlidesStarted, falls = ZombieEnemy.SlipFalls, knockdowns = ZombieEnemy.Knockdowns, evicted = ZombieEnemy.CorpsesEvicted
        };

        private static void OnRoundStarted(int round) => s_base = Take();

        private static void OnRoundCompleted(int round)
        {
            Snap now = Take();
            int warned = now.warned - s_base.warned;
            double lead = warned > 0 ? (now.leadMs - s_base.leadMs) / warned : 0.0;
            Debug.Log("[AiV3] round=" + round + " mode=" + CameraModeService.Current
                      + " rearBeats=" + (now.rearBeats - s_base.rearBeats) + " rearHitsWarned=" + warned + " rearHitsUnwarned=" + (now.unwarned - s_base.unwarned)
                      + " rearLeadMs=" + lead.ToString("0") + " leadHolds=" + (now.holds - s_base.holds)
                      + " grabs=" + (now.grabs - s_base.grabs) + " escapes=" + (now.escapes - s_base.escapes) + " shocks=" + (now.shocks - s_base.shocks)
                      + " gelSlides=" + (now.slides - s_base.slides) + " gelFalls=" + (now.falls - s_base.falls) + " knockdowns=" + (now.knockdowns - s_base.knockdowns)
                      + " corpsesEvicted=" + (now.evicted - s_base.evicted));
        }

        /// <summary>Tests: the line for the current deltas (same text the log gets).</summary>
        public static void ResetBase() => s_base = Take();
    }
}
