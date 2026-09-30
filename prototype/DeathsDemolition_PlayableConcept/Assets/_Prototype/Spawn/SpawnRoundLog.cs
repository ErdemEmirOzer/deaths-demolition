using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// WP-076 B8 · one Player-log line per completed round with the spawn v6 counters (WP-078 soak / bot summaries):
    /// world profile (v6 or AP005), camera mode, tier-gate reroutes / fallbacks, the rear share of the last 8 spawns and
    /// the inner-gate share of the last 24 (v6 only), the inner-quota relaxations and the quota-full no-point ticks (B9 Y5) and the round's spawns. Round deltas for the counters.
    /// </summary>
    public static class SpawnRoundLog
    {
        private static int s_reroutes, s_fallbacks, s_quotaRelax, s_noPointQuota, s_massN, s_picks, s_quotaFull;
        private static double s_massSum;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bind()
        {
            RunEvents.RoundStarted -= OnRoundStarted; RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted -= OnRoundCompleted; RunEvents.RoundCompleted += OnRoundCompleted;
        }

        private static void OnRoundStarted(int round)
        {
            EnemySpawnDirector d = EnemySpawnDirector.Instance;
            s_reroutes = d != null ? d.TierGateReroutes : 0;
            s_fallbacks = d != null ? d.TierGateFallbacks : 0;
            s_quotaRelax = d != null ? d.InnerQuotaRelaxes : 0;
            s_noPointQuota = d != null ? d.NoPointQuotaTicks : 0;
            s_massSum = d != null ? d.InnerMassSum : 0; s_massN = d != null ? d.InnerMassSamples : 0;
            s_picks = d != null ? d.V6Picks : 0; s_quotaFull = d != null ? d.QuotaFullPicks : 0;
        }

        private static void OnRoundCompleted(int round)
        {
            EnemySpawnDirector d = EnemySpawnDirector.Instance;
            int reroutes = d != null ? d.TierGateReroutes - s_reroutes : 0, fallbacks = d != null ? d.TierGateFallbacks - s_fallbacks : 0;
            Debug.Log("[SpawnV6] round=" + round + " world=" + (SpawnWorldProfile.IsV6 ? "v6" : "ap005") + " mode=" + SpawnModeTable.Mode
                      + " tierGateReroutes=" + reroutes + " tierGateFallbacks=" + fallbacks
                      + " rearShare8=" + RearSpawnShare.Share(RearSpawnShare.Window).ToString("0.00")
                      + " innerShare24=" + (SpawnWorldProfile.IsV6 ? SpawnWorldProfile.InnerShare.ToString("0.00") : "-")
                      + " innerQuotaRelax=" + (d != null ? d.InnerQuotaRelaxes - s_quotaRelax : 0)
                      + " noPointQuota=" + (d != null ? d.NoPointQuotaTicks - s_noPointQuota : 0)
                      + " innerMass=" + (d != null && d.InnerMassSamples > s_massN ? ((d.InnerMassSum - s_massSum) / (d.InnerMassSamples - s_massN)).ToString("0.00") : "-")
                      + " spawns=" + (d != null ? d.Population.SpawnedTotal : 0)
                      + " quotaFull=" + (d != null ? (d.QuotaFullPicks - s_quotaFull) + "/" + (d.V6Picks - s_picks) : "-"));
        }
    }
}
