using System;
using System.Collections.Generic;
using DD.Prototype.Profile;

namespace DD.Prototype.Achievements
{
    /// <summary>
    /// The achievement rule engine (WP-023). Stateless and Unity-free: it reads a
    /// CANDIDATE profile (lifetime totals already merged by the transaction), the run's
    /// reward input and feat counters, and a definition list; it grants into the
    /// candidate's earned list and reports what was new. It runs inside
    /// <see cref="ProfileService.ApplyRun"/>, so a grant reaches the disk in the same
    /// write as the run that earned it, exactly once (WP-009H): a replayed summary
    /// never reaches the engine, and a re-applied one after a failed save grants the
    /// same set again from the same base, which is the same profile.
    ///
    /// Lifetime scope compares the merged totals ("Armor Breaker ×100" is earned by the
    /// run that pushes the lifetime count over the line); SingleRun scope compares this
    /// run alone. An achievement is a record: nothing here returns a modifier.
    /// </summary>
    public static class AchievementEngine
    {
        /// <summary>Grants every unearned, satisfied definition. <paramref name="earned"/> receives only what was new.</summary>
        public static void Evaluate(ProfileData candidate, in RunRewardInput input, in RunFeatCounters feats,
            IReadOnlyList<AchievementDefinition> definitions, List<AchievementDefinition> earned)
        {
            if (candidate == null || definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                AchievementDefinition definition = definitions[i];
                if (definition == null || !definition.IsAvailable || candidate.Has(candidate.Achievements, definition.Id))
                {
                    continue;
                }

                if (!IsSatisfied(candidate, input, feats, definition))
                {
                    continue;
                }

                if (candidate.Grant(candidate.Achievements, definition.Id) && earned != null)
                {
                    earned.Add(definition);
                }
            }
        }

        public static bool IsSatisfied(ProfileData candidate, in RunRewardInput input, in RunFeatCounters feats, AchievementDefinition definition)
        {
            if (candidate == null || definition == null)
            {
                return false;
            }

            long value = definition.Scope == AchievementScope.SingleRun
                ? RunValue(input, feats, definition)
                : LifetimeValue(candidate, definition);

            // WP-077 A10: "round 10 with each weapon" counts this run's weapon now (its first-R10 record is written after).
            if (definition.Condition == AchievementCondition.Feat && definition.Feat == FeatKind.ArsenalRound10 && definition.Scope == AchievementScope.Lifetime &&
                input.ReachedRound >= 10 && !string.IsNullOrEmpty(input.WeaponKey) && !candidate.Has(candidate.AshMilestones, "r10:" + input.WeaponKey))
            {
                value++;
            }
            int threshold = Math.Max(1, definition.Threshold);
            if (definition.Condition == AchievementCondition.Feat && definition.Feat == FeatKind.ArsenalRound10)
            {
                // "each classic weapon": the weapons this build carries (rl_1 joins when the Arsenal enables it).
                int carried = DD.Prototype.Core.Weapons.WeaponRoster.Enabled.Count;
                if (carried > 0) threshold = Math.Min(threshold, carried);
            }

            return value >= threshold;
        }

        /// <summary>The lifetime counter a definition reads, for progress display (current / threshold).</summary>
        public static long LifetimeValue(ProfileData profile, AchievementDefinition definition)
        {
            if (profile == null || definition == null)
            {
                return 0;
            }

            switch (definition.Condition)
            {
                case AchievementCondition.TotalKills: return profile.TotalKills;
                case AchievementCondition.TierKills: return profile.KillsForTierName(definition.TargetTier.ToString());
                case AchievementCondition.ReachRound: return profile.HighestRoundAnyMode;   // WP-104: every mode counts
                case AchievementCondition.ReachRoundInMode: return profile.HighestRoundIn(definition.Mode);
                case AchievementCondition.RoundsCompleted: return profile.RoundsCompleted;
                case AchievementCondition.ArmorPiecesBroken: return profile.ArmorPiecesBroken;
                case AchievementCondition.SpecialWeaponPickups: return profile.SpecialWeaponPickups;
                case AchievementCondition.FlawlessRounds: return profile.FlawlessRounds;
                // Gold and purchases are not persisted (REQ-018): no lifetime value.
                case AchievementCondition.GoldEarned: return 0;
                case AchievementCondition.UpgradesPurchased: return 0;
                case AchievementCondition.RunsPlayed: return profile.RunsPlayed;
                case AchievementCondition.ProfileLevel: return profile.ProfileLevel;
                case AchievementCondition.CapstoneWeapons: return profile.CapstoneWeaponCount;
                case AchievementCondition.WeaponMastery: return MetaPathRules.MasteryLevel(profile.HighestMasteryKills);
                case AchievementCondition.Feat: return definition.Feat == FeatKind.ArsenalRound10 ? CountRound10Weapons(profile) : profile.FeatTotal(definition.Feat);
                default: return 0;
            }
        }

        /// <summary>WP-077 A10: classic weapons with a first-round-10 record (the profile's "r10:&lt;key&gt;" Kül milestones).</summary>
        public static int CountRound10Weapons(ProfileData profile)
        {
            int n = 0;
            if (profile == null || profile.AshMilestones == null) return 0;
            for (int i = 0; i < profile.AshMilestones.Count; i++)
            {
                string m = profile.AshMilestones[i];
                if (m != null && m.StartsWith("r10:", StringComparison.Ordinal)) n++;
            }

            return n;
        }

        /// <summary>The single-run counter a definition reads. RunsPlayed / ProfileLevel have no run value (0).</summary>
        public static long RunValue(in RunRewardInput input, in RunFeatCounters feats, AchievementDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            switch (definition.Condition)
            {
                case AchievementCondition.TotalKills: return input.TotalKills;
                case AchievementCondition.TierKills: return input.KillsFor(definition.TargetTier);
                case AchievementCondition.ReachRound: return input.ReachedRound;
                case AchievementCondition.ReachRoundInMode: return input.Mode == definition.Mode ? input.ReachedRound : 0;
                case AchievementCondition.RoundsCompleted: return input.CompletedRounds;
                case AchievementCondition.ArmorPiecesBroken: return input.ArmorPiecesBroken;
                case AchievementCondition.SpecialWeaponPickups: return Math.Max(0, feats.SpecialWeaponPickups);
                case AchievementCondition.FlawlessRounds: return Math.Max(0, feats.FlawlessRounds);
                case AchievementCondition.GoldEarned: return Math.Max(0, feats.GoldEarned);
                case AchievementCondition.UpgradesPurchased: return Math.Max(0, feats.UpgradesPurchased);
                case AchievementCondition.TreeCapstones: return input.TreeCapstones;
                case AchievementCondition.TreeKeystones: return input.TreeKeystones;
                case AchievementCondition.HearthStacks: return input.TreeHearthStacks;
                case AchievementCondition.TreeFullLanes: return input.TreeFullLanes;
                case AchievementCondition.SpecialLinkII: return input.TreeLinkII;
                case AchievementCondition.Feat: return feats.Feat(definition.Feat);
                default: return 0;
            }
        }
    }
}
