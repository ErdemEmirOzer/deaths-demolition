using DD.Prototype.Core.Config;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The run's counters. A plain class, not a MonoBehaviour and not a static: the
    /// director owns exactly one and a test can build its own.
    ///
    /// XP RULES IT ENFORCES (all from the request, all read from PrototypeConfig):
    ///   * every kill scores kill XP, including kills in the half round the player died
    ///     in - <see cref="RecordKill"/> is never gated on the round finishing;
    ///   * an unfinished round grants no round XP - <see cref="RecordRoundCompleted"/>
    ///     is only called from the round-cleared path;
    ///   * gold grants no XP - this class never sees a gold amount as XP, and
    ///     <see cref="GoldEarned"/> is carried for the results screen only;
    ///   * playtime grants no XP - <see cref="RunTime"/> feeds nothing but the readout.
    /// </summary>
    public sealed class RunStatsTracker
    {
        private readonly int[] _killsPerTier = new int[4];

        // ------------------------------------------------------------------ kills

        public int TotalKills { get; private set; }

        public int RoundKills { get; private set; }

        public EnemyTier HighestTierKilled { get; private set; }

        public bool AnyKill => TotalKills > 0;

        public int KillsFor(EnemyTier tier)
        {
            int index = (int)tier;
            return index >= 0 && index < _killsPerTier.Length ? _killsPerTier[index] : 0;
        }

        // ------------------------------------------------------------------ xp

        public int KillXp { get; private set; }

        public int CompletedRoundXp { get; private set; }

        public int TotalXp => KillXp + CompletedRoundXp;

        // ------------------------------------------------------------------ misc

        public int CompletedRounds { get; private set; }

        public int ArmorPiecesBroken { get; private set; }

        public int GoldEarned { get; private set; }

        /// <summary>Gold spent this run on upgrade cards AND paid rerolls (WP-008: one number, Results "Gold Spent").</summary>
        public int GoldSpent { get; private set; }

        /// <summary>WP-008 split of <see cref="GoldSpent"/>: upgrade cards.</summary>
        public int GoldSpentOnUpgrades { get; private set; }

        /// <summary>WP-008 split of <see cref="GoldSpent"/>: paid offer rerolls.</summary>
        public int GoldSpentOnRerolls { get; private set; }

        public int UpgradesPurchased { get; private set; }

        /// <summary>WP-008: paid rerolls used this run.</summary>
        public int RerollsUsed { get; private set; }

        /// <summary>Seconds of run time. Frozen and paused time never reaches this.</summary>
        public float RunTime { get; private set; }

        // ------------------------------------------------------------------ writes

        /// <summary>
        /// One enemy died. <paramref name="killXp"/> comes from
        /// PrototypeConfig.KillXp(tier) - the arithmetic is never redone here.
        /// </summary>
        public void RecordKill(EnemyTier tier, int killXp)
        {
            int index = (int)tier;
            if (index >= 0 && index < _killsPerTier.Length)
            {
                _killsPerTier[index]++;
            }

            if (TotalKills == 0 || tier > HighestTierKilled)
            {
                HighestTierKilled = tier;
            }

            TotalKills++;
            RoundKills++;
            KillXp += killXp > 0 ? killXp : 0;
        }

        public void RecordArmorBreak()
        {
            ArmorPiecesBroken++;
        }

        /// <summary>
        /// A round was finished. <paramref name="roundXp"/> comes from
        /// PrototypeConfig.RoundCompletionXp(round).
        /// </summary>
        public void RecordRoundCompleted(int roundXp)
        {
            CompletedRounds++;
            CompletedRoundXp += roundXp > 0 ? roundXp : 0;
        }

        public void BeginRound()
        {
            RoundKills = 0;
        }

        public void RecordGoldEarned(int amount)
        {
            if (amount > 0)
            {
                GoldEarned += amount;
            }
        }

        public void RecordGoldSpent(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            GoldSpent += amount;
            GoldSpentOnUpgrades += amount;
            UpgradesPurchased++;
        }

        /// <summary>WP-008: a paid reroll. Counts into <see cref="GoldSpent"/>, not into <see cref="UpgradesPurchased"/>.</summary>
        public void RecordRerollSpent(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            GoldSpent += amount;
            GoldSpentOnRerolls += amount;
            RerollsUsed++;
        }

        /// <summary>Advance the run clock. The director feeds it scaled delta time only.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                RunTime += deltaTime;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _killsPerTier.Length; i++)
            {
                _killsPerTier[i] = 0;
            }

            TotalKills = 0;
            RoundKills = 0;
            HighestTierKilled = EnemyTier.Standard;
            KillXp = 0;
            CompletedRoundXp = 0;
            CompletedRounds = 0;
            ArmorPiecesBroken = 0;
            GoldEarned = 0;
            GoldSpent = 0;
            GoldSpentOnUpgrades = 0;
            GoldSpentOnRerolls = 0;
            UpgradesPurchased = 0;
            RerollsUsed = 0;
            RunTime = 0f;
        }
    }
}
