using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// The three population numbers one round is opened with, resolved in ONE place.
    ///
    /// THE EXTENSION POINT FOR REQ-013 / DEC-021 (hybrid round scaling)
    /// -----------------------------------------------------------------
    /// The approved long-term system is a formula baseline plus hand-authored per-round
    /// overrides (armour ratios, tier distributions, guaranteed debuts). Today only the
    /// formula half exists (user choice t-0002): <see cref="Resolve"/> reads
    /// <see cref="RoundConfigSection.TotalEnemyPool"/>, <see cref="RoundConfigSection.TargetActiveEnemies"/>
    /// and <see cref="RoundConfigSection.AbsoluteSafetyCeiling"/>, nothing else. If and
    /// when an authored per-round adjustment of these numbers is approved (DEC-021 today
    /// names armour ratios and tier distributions, not these), it is applied HERE, after
    /// the formula and before the clamp - the director, the ledger and the HUD all read
    /// the plan, so none of them has to learn about it. Nothing is authored yet.
    ///
    /// Nothing about tier weights or debuts lives here: those stay with
    /// <see cref="SpawnTierSelector"/> and <see cref="PrototypeConfig.TierSpawnWeight"/>.
    ///
    /// INVARIANT
    /// ---------
    /// TargetActive &lt;= Ceiling, always. The target is the tempo; the ceiling is the
    /// technical limit (REQ-072). They are two fields on purpose and no code path folds
    /// them into one.
    /// </summary>
    public readonly struct RoundPlan
    {
        public RoundPlan(int round, int poolTotal, int targetActive, int ceiling)
        {
            Round = Mathf.Max(1, round);
            Ceiling = Mathf.Max(1, ceiling);
            PoolTotal = Mathf.Max(0, poolTotal);
            TargetActive = Mathf.Clamp(targetActive, 0, Ceiling);
        }

        public int Round { get; }

        /// <summary>Enemies this round releases in total (REQ-019).</summary>
        public int PoolTotal { get; }

        /// <summary>Living enemies the refill aims for (REQ-072, tempo).</summary>
        public int TargetActive { get; }

        /// <summary>Bodies on the map the system never exceeds (REQ-072, safety).</summary>
        public int Ceiling { get; }

        public bool IsValid => Ceiling >= 1 && TargetActive <= Ceiling;

        /// <summary>
        /// The formula plan for a round. With no config the round is unplayable rather
        /// than silently sized at a made-up number: pool 0, target 0, ceiling 1.
        /// </summary>
        public static RoundPlan Resolve(PrototypeConfig config, int round)
        {
            if (config == null)
            {
                return new RoundPlan(round, 0, 0, 1);
            }

            int ceiling = config.AbsoluteSafetyCeiling;
            int pool = config.TotalEnemyPool(round);
            int target = config.TargetActiveEnemies(round);

            return new RoundPlan(round, pool, target, ceiling);
        }
    }
}
