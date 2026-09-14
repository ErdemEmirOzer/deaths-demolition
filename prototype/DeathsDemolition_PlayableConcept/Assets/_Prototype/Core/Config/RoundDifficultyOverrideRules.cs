using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// WP-010 (ADR-010-3): the validity rules of the DEC-021 hybrid override table. Pure
    /// (allocates only its failure list; an Editor/test/probe path, never per frame) and
    /// the single definition shared by the Editor
    /// validation menu, the EditMode tests and the development probe.
    ///
    /// The contract the rules enforce (the shipped asset carries an EMPTY table in this
    /// package; the mechanism is proven with rows injected into a test-only clone):
    ///   * precedence: an authored row beats the formula for the fields it overrides;
    ///     a field the row does not override falls back to the formula (partial rows);
    ///   * one row per round - two rows naming the same round is a configuration error;
    ///   * armour chances are probabilities in [0, 1]; growth is not negative;
    ///   * a tier weight or a forced count may only name a tier that HAS been introduced
    ///     by that round (<c>IntroductionRound ≤ round</c>) - naming it earlier is an error,
    ///     never an early spawn;
    ///   * the forced counts of a round (plus the debuts they do not already cover) must
    ///     fit the round's total pool.
    /// </summary>
    public static class RoundDifficultyOverrideRules
    {
        /// <summary>
        /// Validate the table of <paramref name="config"/>. Returns the failures (empty when
        /// the table is valid, including when it is empty).
        /// </summary>
        public static List<string> Validate(PrototypeConfig config)
        {
            var failures = new List<string>();
            if (config == null)
            {
                failures.Add("config is null");
                return failures;
            }

            IReadOnlyList<RoundDifficultyOverride> rows = config.RoundDifficultyOverrides;
            for (int i = 0; i < rows.Count; i++)
            {
                RoundDifficultyOverride row = rows[i];
                if (row == null)
                {
                    failures.Add("row " + i + ": null entry");
                    continue;
                }

                string label = "round " + row.Round + " (row " + i + ")";

                if (row.Round < 1)
                {
                    failures.Add(label + ": Round must be >= 1");
                }

                for (int j = 0; j < i; j++)
                {
                    if (rows[j] != null && rows[j].Round == row.Round)
                    {
                        failures.Add(label + ": duplicate row for the same round (row " + j + ")");
                        break;
                    }
                }

                if (row.OverrideArmorChances)
                {
                    ValidateArmor(row.ArmorChances, label, failures);
                }

                if (row.OverrideTierWeights)
                {
                    ValidateWeights(config, row, label, failures);
                }

                if (row.OverrideForcedCounts)
                {
                    ValidateForcedCounts(config, row, label, failures);
                }
            }

            return failures;
        }

        /// <summary>True when <see cref="Validate"/> reports nothing.</summary>
        public static bool IsValid(PrototypeConfig config)
        {
            return Validate(config).Count == 0;
        }

        /// <summary>Round in which <paramref name="tier"/> joins the pool (first matching tier entry, as <c>EnemyConfigSection.Find</c>); int.MaxValue when the tier is not defined.</summary>
        public static int IntroductionRoundOf(PrototypeConfig config, EnemyTier tier)
        {
            EnemyTierSettings t = config != null ? config.Enemies.Find(tier) : null;
            return t != null ? t.IntroductionRound : int.MaxValue;
        }

        private static void ValidateArmor(ArmorChanceSettings armor, string label, List<string> failures)
        {
            if (armor == null)
            {
                failures.Add(label + ": OverrideArmorChances set but ArmorChances is null");
                return;
            }

            Probability(armor.HelmetChance, label + " HelmetChance", failures);
            Probability(armor.BodyChance, label + " BodyChance", failures);
            Probability(armor.LegChance, label + " LegChance", failures);
            Probability(armor.MaxChance, label + " MaxChance", failures);
            if (float.IsNaN(armor.ChanceGrowthPerRound) || float.IsInfinity(armor.ChanceGrowthPerRound) || armor.ChanceGrowthPerRound < 0f)
            {
                failures.Add(label + " ChanceGrowthPerRound must be a finite number >= 0 (was " + armor.ChanceGrowthPerRound + ")");
            }
        }

        private static void ValidateWeights(PrototypeConfig config, RoundDifficultyOverride row, string label, List<string> failures)
        {
            if (row.TierWeights == null)
            {
                return;   // an empty weight override = "every tier from the formula"
            }

            for (int i = 0; i < row.TierWeights.Count; i++)
            {
                TierWeightOverride w = row.TierWeights[i];
                if (w == null)
                {
                    failures.Add(label + ": TierWeights[" + i + "] is null");
                    continue;
                }

                if (float.IsNaN(w.Weight) || float.IsInfinity(w.Weight) || w.Weight < 0f)
                {
                    failures.Add(label + ": weight of " + w.Tier + " must be a finite number >= 0 (was " + w.Weight + ")");
                }

                if (w.Weight > 0f && IntroductionRoundOf(config, w.Tier) > row.Round)
                {
                    failures.Add(label + ": " + w.Tier + " is weighted before its introduction round (" + IntroductionRoundOf(config, w.Tier) + ")");
                }

                for (int j = 0; j < i; j++)
                {
                    if (row.TierWeights[j] != null && row.TierWeights[j].Tier == w.Tier)
                    {
                        failures.Add(label + ": " + w.Tier + " weighted twice in one row");
                        break;
                    }
                }
            }
        }

        private static void ValidateForcedCounts(PrototypeConfig config, RoundDifficultyOverride row, string label, List<string> failures)
        {
            if (row.ForcedTierCounts == null)
            {
                return;
            }

            int pool = config.TotalEnemyPool(row.Round);
            int owed = 0;

            for (int i = 0; i < row.ForcedTierCounts.Count; i++)
            {
                TierCountOverride c = row.ForcedTierCounts[i];
                if (c == null)
                {
                    failures.Add(label + ": ForcedTierCounts[" + i + "] is null");
                    continue;
                }

                if (c.Count < 0)
                {
                    failures.Add(label + ": forced count of " + c.Tier + " must be >= 0 (was " + c.Count + ")");
                }

                if (c.Count > 0 && IntroductionRoundOf(config, c.Tier) > row.Round)
                {
                    failures.Add(label + ": " + c.Tier + " is forced before its introduction round (" + IntroductionRoundOf(config, c.Tier) + ")");
                }

                for (int j = 0; j < i; j++)
                {
                    if (row.ForcedTierCounts[j] != null && row.ForcedTierCounts[j].Tier == c.Tier)
                    {
                        failures.Add(label + ": " + c.Tier + " forced twice in one row");
                        break;
                    }
                }

                owed += Mathf.Max(0, c.Count);
            }

            // Debuts of this round that the forced counts do not already cover are owed too.
            List<EnemyTierSettings> tiers = config.Enemies.Tiers;
            for (int i = 0; i < tiers.Count; i++)
            {
                EnemyTierSettings t = tiers[i];
                if (t == null || t.Tier == EnemyTier.Standard || t.IntroductionRound != row.Round)
                {
                    continue;
                }

                bool covered = false;
                for (int j = 0; j < row.ForcedTierCounts.Count; j++)
                {
                    if (row.ForcedTierCounts[j] != null && row.ForcedTierCounts[j].Tier == t.Tier && row.ForcedTierCounts[j].Count > 0)
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    owed++;
                }
            }

            if (owed > pool)
            {
                failures.Add(label + ": forced counts + debuts (" + owed + ") exceed the round's pool (" + pool + ")");
            }
        }

        private static void Probability(float value, string label, List<string> failures)
        {
            if (float.IsNaN(value) || value < 0f || value > 1f)
            {
                failures.Add(label + " must be within [0, 1] (was " + value + ")");
            }
        }
    }
}
