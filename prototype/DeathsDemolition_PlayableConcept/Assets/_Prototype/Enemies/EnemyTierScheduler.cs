using UnityEngine;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Decides which version of the zombie comes out of the next spawn point - REQ-071.
    ///
    /// TWO RULES, IN THIS ORDER
    ///   1. Guaranteed introduction. A tier whose IntroductionRound equals this round MUST
    ///      appear at least once. Runner in round 3, Guard in round 5, Executioner in
    ///      round 8. The guarantee is placed at a RANDOM index inside the round's pool, not
    ///      at the front: a player should meet their first Executioner somewhere in the
    ///      middle of round 8, not as its opening move. If the pool is about to run out and
    ///      a guarantee is still owed, the remaining slots are forced.
    ///   2. Weighted pool. Everything else is a weighted draw over the tiers whose
    ///      introduction round has passed, using PrototypeConfig.TierSpawnWeight - which
    ///      consults the DEC-021 authored override table first, then the formula. The
    ///      override table is empty by design and this class must not work around that.
    ///
    /// The spawner (G-06) owns WHERE and WHEN; this owns WHAT.
    /// </summary>
    public sealed class EnemyTierScheduler
    {
        private const int MaxTiers = 4;

        [PrototypeOnly(PendingDecisions.None,
            "Earliest point in a round's pool where a tier's guaranteed debut may be placed. " +
            "REQ-071 only requires the debut to happen; where inside the round is invented.", true)]
        private const float DebutWindowStart = 0.2f;

        [PrototypeOnly(PendingDecisions.None,
            "Latest point in a round's pool where a guaranteed debut may be placed, so a " +
            "player never meets their first Executioner as round 8's opening move.", true)]
        private const float DebutWindowEnd = 0.8f;

        private static readonly EnemyTier[] s_allTiers =
        {
            EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra
        };

        private readonly EnemyTier[] _guaranteedTiers = new EnemyTier[MaxTiers];
        private readonly int[] _guaranteedIndices = new int[MaxTiers];
        private readonly bool[] _guaranteedDone = new bool[MaxTiers];
        private readonly float[] _weights = new float[MaxTiers];

        private int _guaranteedCount;
        private int _round = 1;
        private int _poolSize;
        private int _emitted;

        /// <summary>Round this scheduler is currently serving.</summary>
        public int Round => _round;

        /// <summary>How many enemies have been handed out this round.</summary>
        public int Emitted => _emitted;

        /// <summary>Guaranteed introductions still owed this round.</summary>
        public int PendingGuarantees
        {
            get
            {
                int pending = 0;
                for (int i = 0; i < _guaranteedCount; i++)
                {
                    if (!_guaranteedDone[i])
                    {
                        pending++;
                    }
                }

                return pending;
            }
        }

        /// <summary>
        /// Start a round. <paramref name="poolSize"/> should be PrototypeConfig.TotalEnemyPool(round)
        /// so the guaranteed introductions can be spread across the real pool.
        /// </summary>
        public void BeginRound(int round, int poolSize, PrototypeConfig config, ref EnemyRandom random)
        {
            _round = Mathf.Max(1, round);
            _poolSize = Mathf.Max(1, poolSize);
            _emitted = 0;
            _guaranteedCount = 0;

            if (config == null)
            {
                return;
            }

            for (int i = 0; i < s_allTiers.Length; i++)
            {
                EnemyTierSettings settings = config.Enemies.Find(s_allTiers[i]);
                if (settings == null || settings.Tier != s_allTiers[i])
                {
                    continue;
                }

                if (settings.IntroductionRound != _round)
                {
                    continue;
                }

                _guaranteedTiers[_guaranteedCount] = s_allTiers[i];

                // Place the debut in the middle 60% of the round so it is neither the
                // opening enemy nor the last one the player might never reach.
                int low = Mathf.Max(1, Mathf.RoundToInt(_poolSize * DebutWindowStart));
                int high = Mathf.Max(low + 1, Mathf.RoundToInt(_poolSize * DebutWindowEnd));
                _guaranteedIndices[_guaranteedCount] = random.Range(low, high);
                _guaranteedDone[_guaranteedCount] = false;
                _guaranteedCount++;
            }
        }

        /// <summary>Pick the tier for the next spawn and advance the round counter.</summary>
        public EnemyTier Next(PrototypeConfig config, ref EnemyRandom random)
        {
            EnemyTier tier = Pick(config, ref random);
            _emitted++;
            return tier;
        }

        private EnemyTier Pick(PrototypeConfig config, ref EnemyRandom random)
        {
            // --- rule 1: guaranteed introductions
            int pending = PendingGuarantees;
            if (pending > 0)
            {
                int remainingSlots = Mathf.Max(0, _poolSize - _emitted);

                for (int i = 0; i < _guaranteedCount; i++)
                {
                    if (_guaranteedDone[i])
                    {
                        continue;
                    }

                    // Either we reached its scheduled index, or the round is running out of
                    // room and the debut has to be forced now.
                    if (_emitted >= _guaranteedIndices[i] || remainingSlots <= pending)
                    {
                        _guaranteedDone[i] = true;
                        return _guaranteedTiers[i];
                    }
                }
            }

            // --- rule 2: weighted pool
            if (config == null)
            {
                return EnemyTier.Standard;
            }

            float total = 0f;
            for (int i = 0; i < s_allTiers.Length; i++)
            {
                // A tier is only in the pool from its introduction round onward; the
                // config's own ResolveSpawnWeight returns 0 before that.
                float w = Mathf.Max(0f, config.TierSpawnWeight(s_allTiers[i], _round));
                _weights[i] = w;
                total += w;
            }

            if (total <= 0f)
            {
                return EnemyTier.Standard;
            }

            float roll = random.Next01() * total;
            for (int i = 0; i < s_allTiers.Length; i++)
            {
                roll -= _weights[i];
                if (roll <= 0f)
                {
                    return s_allTiers[i];
                }
            }

            return EnemyTier.Standard;
        }

        /// <summary>One-line state for the F1 readout.</summary>
        public string Describe()
        {
            return string.Format("round {0}  emitted {1}/{2}  guarantees pending {3}",
                _round, _emitted, _poolSize, PendingGuarantees);
        }
    }
}
