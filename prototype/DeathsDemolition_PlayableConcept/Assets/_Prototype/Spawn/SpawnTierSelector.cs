using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// REQ-071 - controlled introduction of the enemy versions.
    ///
    /// Runner debuts in round 3, Guard in round 5, Executioner in round 8 (the rounds are
    /// config data: EnemyTierSettings.IntroductionRound). In its debut round a tier is
    /// GUARANTEED at least once; from then on it joins the weighted pool and is drawn
    /// through config.TierSpawnWeight(tier, round), which is the single source of truth for
    /// the weights.
    ///
    /// The debut is deliberately not the round's first enemy: it is released once
    /// GuaranteedIntroPoolFraction of the round pool has been spent, so the round still
    /// opens with the familiar silhouette and the new one arrives as a change of pressure.
    /// If the round is about to run out of pool, the debt is forced immediately - the
    /// guarantee outranks the pacing.
    /// </summary>
    public sealed class SpawnTierSelector
    {
        private readonly List<EnemyTier> _guaranteed = new List<EnemyTier>(4);
        private readonly List<EnemyTier> _candidates = new List<EnemyTier>(8);
        private readonly List<float> _weights = new List<float>(8);

        private int _poolTotal;
        private float _releaseAfterSpawns;

        /// <summary>Tiers still owed a guaranteed appearance this round.</summary>
        public int PendingGuaranteed => _guaranteed.Count;

        public void BeginRound(int round, PrototypeConfig config, int poolTotal, float releaseFraction)
        {
            _guaranteed.Clear();
            _poolTotal = Mathf.Max(1, poolTotal);
            _releaseAfterSpawns = _poolTotal * Mathf.Clamp01(releaseFraction);

            if (config == null)
            {
                return;
            }

            List<EnemyTierSettings> tiers = config.Enemies.Tiers;
            for (int i = 0; i < tiers.Count; i++)
            {
                EnemyTierSettings t = tiers[i];
                if (t != null && t.IntroductionRound == round && t.Tier != EnemyTier.Standard)
                {
                    _guaranteed.Add(t.Tier);
                }
            }
        }

        /// <summary>
        /// Picks the tier of the next enemy.
        /// <paramref name="spawnedThisRound"/> and <paramref name="remainingInPool"/> decide
        /// when an owed debut can no longer wait.
        /// </summary>
        public EnemyTier Next(int round, PrototypeConfig config, int spawnedThisRound, int remainingInPool,
            out bool guaranteed)
        {
            guaranteed = false;

            if (_guaranteed.Count > 0)
            {
                bool pacingReached = spawnedThisRound >= _releaseAfterSpawns;
                bool runningOut = remainingInPool <= _guaranteed.Count;

                if (pacingReached || runningOut)
                {
                    EnemyTier tier = _guaranteed[0];
                    _guaranteed.RemoveAt(0);
                    guaranteed = true;
                    return tier;
                }
            }

            return Weighted(round, config);
        }

        private EnemyTier Weighted(int round, PrototypeConfig config)
        {
            if (config == null)
            {
                return EnemyTier.Standard;
            }

            _candidates.Clear();
            _weights.Clear();

            float total = 0f;
            List<EnemyTierSettings> tiers = config.Enemies.Tiers;

            for (int i = 0; i < tiers.Count; i++)
            {
                EnemyTierSettings t = tiers[i];
                if (t == null || round < t.IntroductionRound)
                {
                    continue;
                }

                float weight = config.TierSpawnWeight(t.Tier, round);
                if (weight <= 0f)
                {
                    continue;
                }

                _candidates.Add(t.Tier);
                _weights.Add(weight);
                total += weight;
            }

            if (_candidates.Count == 0 || total <= 0f)
            {
                return EnemyTier.Standard;
            }

            float roll = Random.value * total;
            for (int i = 0; i < _candidates.Count; i++)
            {
                roll -= _weights[i];
                if (roll <= 0f)
                {
                    return _candidates[i];
                }
            }

            return _candidates[_candidates.Count - 1];
        }

        /// <summary>Debug readout: which debuts are still owed.</summary>
        public string DescribePending()
        {
            if (_guaranteed.Count == 0)
            {
                return "none";
            }

            string result = "";
            for (int i = 0; i < _guaranteed.Count; i++)
            {
                result += (i > 0 ? ", " : "") + _guaranteed[i];
            }

            return result;
        }
    }
}
