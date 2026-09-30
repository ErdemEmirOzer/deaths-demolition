using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;

namespace DD.Prototype.Profile
{
    /// <summary>
    /// The unlock ladder and display names a profile transaction needs. The game's
    /// implementation reads PrototypeConfig through <see cref="ProfileService"/>; a
    /// test hands in its own, so every rule below can be proven without a config and
    /// with a ladder that changes between two calls (the entitlement tests).
    /// </summary>
    public interface IUnlockLadder
    {
        int UnlockLevelFor(ClassicWeaponId id);
        int UnlockLevelFor(UpgradeBranch branch);
        string WeaponDisplayName(ClassicWeaponId id);
        string BranchDisplayName(UpgradeBranch branch);
    }

    /// <summary>
    /// A run summary reduced to the numbers the profile is allowed to consume.
    ///
    /// This is the ONLY door between a run and the persistent profile. Everything in
    /// it is clamped to be non-negative, the two XP sources are read from their own
    /// fields, and <c>RunSummary.TotalXpEarned</c> is never read: the awarded total is
    /// recomputed as completed-round XP + kill XP (DEC-008, REQ-021..024). Gold,
    /// spending, purchases, rerolls and playtime do not appear here at all, so they
    /// cannot leak into the arithmetic by accident (WP-023: single-run gold / purchase
    /// achievements read the summary through <c>RunFeatCounters</c>, never through this).
    /// </summary>
    public readonly struct RunRewardInput
    {
        public readonly string CompletionId;
        public readonly int ReachedRound;
        public readonly int CompletedRounds;
        public readonly int StandardKills;
        public readonly int SpecialKills;
        public readonly int EliteKills;
        public readonly int UltraKills;

        /// <summary>Kills the summary counted in its total but attributed to no tier (kept, never spread).</summary>
        public readonly int UnattributedKills;

        public readonly int ArmorPiecesBroken;
        public readonly int CompletedRoundXp;
        public readonly int KillXp;
        public readonly EnemyTier HighestTierKilled;

        /// <summary>WP-077: the run's weapon as its saved key ("ar_1" …); mastery and the round-10 Ash milestone.</summary>
        public readonly string WeaponKey;

        /// <summary>WP-077: tree capstones / Special Link II owned at the end (first-time Ash milestones).</summary>
        public readonly int TreeCapstones;
        public readonly int TreeLinkII;

        /// <summary>WP-077 B4: tree counters the achievement rules read (keystones incl. the seal, Hearth stacks, weapon capstone 0/1, full lanes).</summary>
        public readonly int TreeKeystones;
        public readonly int TreeHearthStacks;
        public readonly int TreeWeaponCapstone;
        public readonly int TreeFullLanes;

        /// <summary>Standard + Special + Elite + Ultra + unattributed, saturating.</summary>
        public int TotalKills => ProfileProgression.SaturatingAdd(
            ProfileProgression.SaturatingSum(StandardKills, SpecialKills, EliteKills, UltraKills), UnattributedKills);

        /// <summary>The awarded XP: completed-round XP + kill XP, saturating. Nothing else.</summary>
        public int TotalXp => ProfileProgression.SaturatingAdd(CompletedRoundXp, KillXp);

        public bool HasIdentity => !string.IsNullOrEmpty(CompletionId);

        public RunRewardInput(in RunSummary summary)
        {
            CompletionId = summary.CompletionId;
            ReachedRound = Math.Max(0, summary.ReachedRound);

            // A run cannot have completed more rounds than it reached.
            CompletedRounds = Math.Min(Math.Max(0, summary.CompletedRounds), ReachedRound);

            StandardKills = Math.Max(0, summary.StandardKills);
            SpecialKills = Math.Max(0, summary.SpecialKills);
            EliteKills = Math.Max(0, summary.EliteKills);
            UltraKills = Math.Max(0, summary.UltraKills);

            int attributed = ProfileProgression.SaturatingSum(StandardKills, SpecialKills, EliteKills, UltraKills);
            int claimed = Math.Max(0, summary.TotalKills);
            UnattributedKills = claimed > attributed ? claimed - attributed : 0;

            ArmorPiecesBroken = Math.Max(0, summary.ArmorPiecesBroken);
            CompletedRoundXp = Math.Max(0, summary.CompletedRoundXp);
            KillXp = Math.Max(0, summary.KillXp);
            HighestTierKilled = summary.HighestTierKilled;
            WeaponKey = ProfileProgression.NameOf(summary.Weapon);
            TreeCapstones = Math.Max(0, summary.TreeCapstones);
            TreeLinkII = Math.Max(0, summary.TreeLinkII);
            TreeKeystones = Math.Max(0, summary.TreeKeystones);
            TreeHearthStacks = Math.Max(0, summary.TreeHearthStacks);
            TreeWeaponCapstone = Math.Min(1, Math.Max(0, summary.TreeWeaponCapstone));
            TreeFullLanes = Math.Max(0, summary.TreeFullLanes);
        }

        public int KillsFor(EnemyTier tier)
        {
            switch (tier)
            {
                case EnemyTier.Standard: return StandardKills;
                case EnemyTier.Special: return SpecialKills;
                case EnemyTier.Elite: return EliteKills;
                case EnemyTier.Ultra: return UltraKills;
                default: return 0;
            }
        }
    }

    /// <summary>Where the level arithmetic landed.</summary>
    public readonly struct LevelProgress
    {
        public readonly int Level;
        public readonly int XpIntoLevel;
        public readonly int LevelUps;

        /// <summary>True when the level-up cap stopped the loop (pathological config). Bounded, not wrong.</summary>
        public readonly bool Capped;

        public LevelProgress(int level, int xpIntoLevel, int levelUps, bool capped)
        {
            Level = level;
            XpIntoLevel = xpIntoLevel;
            LevelUps = levelUps;
            Capped = capped;
        }
    }

    /// <summary>
    /// The profile arithmetic, with no state and no Unity dependency: XP sources,
    /// level-ups, lifetime statistics, unlock grants and achievement evaluation.
    ///
    /// <see cref="ProfileService"/> runs these over a CANDIDATE copy of the profile
    /// inside a transaction; nothing here touches the live profile or the disk. Every
    /// counter add saturates at <c>int.MaxValue</c>, so no lifetime value can wrap to
    /// a negative, and the level loop is bounded, so no config can spin it forever.
    ///
    /// Numbers (kill XP, round XP, the level curve, unlock levels) are NOT here: they
    /// come from PrototypeConfig through the ladder and requirement callbacks, and stay
    /// PrototypeOnly placeholders.
    /// </summary>
    public static class ProfileProgression
    {
        /// <summary>Upper bound on level-ups in one transaction. A belt against a 0/negative requirement.</summary>
        public const int MaxLevelUpsPerTransaction = 512;

        // ------------------------------------------------------------ arithmetic

        public static int SaturatingAdd(int a, int b)
        {
            long sum = (long)a + b;
            if (sum > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (sum < int.MinValue)
            {
                return int.MinValue;
            }

            return (int)sum;
        }

        public static int SaturatingSum(int a, int b, int c, int d)
        {
            return SaturatingAdd(SaturatingAdd(a, b), SaturatingAdd(c, d));
        }

        // ------------------------------------------------------------------ level

        /// <summary>
        /// Banks <paramref name="gainedXp"/> and converts it into level-ups.
        ///
        /// <paramref name="requirement"/> is clamped to at least 1 so a zero or negative
        /// curve cannot loop forever; the loop is additionally capped at
        /// <see cref="MaxLevelUpsPerTransaction"/>, and it stops at <c>int.MaxValue</c>
        /// levels. When the cap stops the loop the leftover XP stays banked (progress
        /// may read above 100% until the next run) - bounded and honest rather than lost.
        /// </summary>
        public static LevelProgress ApplyXp(int level, int xpIntoLevel, int gainedXp, Func<int, int> requirement)
        {
            level = Math.Max(1, level);
            int into = SaturatingAdd(Math.Max(0, xpIntoLevel), Math.Max(0, gainedXp));
            int levelUps = 0;

            while (levelUps < MaxLevelUpsPerTransaction && level < int.MaxValue)
            {
                int required = Math.Max(1, requirement != null ? requirement(level) : 1);
                if (required == int.MaxValue)
                {
                    // R1-B P2 (C-00 30.09): at the level cap nothing banks into the level — TotalXpEarned and Ash keep
                    // counting elsewhere; a later cap raise then starts from 0 instead of hundreds of level-ups at once.
                    return new LevelProgress(level, 0, levelUps, false);
                }
                if (into < required)
                {
                    return new LevelProgress(level, into, levelUps, false);
                }

                into -= required;
                level++;
                levelUps++;
            }

            return new LevelProgress(level, into, levelUps, true);
        }

        // ------------------------------------------------------------- Ash (WP-077)

        /// <summary>
        /// WP-077 §3.1: the run's Ash — floor(run XP / XpPerAsh) + first-time milestones (a weapon's
        /// first round 10, the first capstone, the first Special Link II) + each achievement earned by
        /// this run by rarity — added to the candidate (earned total and balance), plus the run's kills
        /// to the weapon's mastery. Runs inside the ApplyRun transaction: reaches disk with the rest
        /// of the rewards or not at all (WP-009H exactly-once).
        /// </summary>
        public static int ApplyAsh(ProfileData candidate, in RunRewardInput input,
            IReadOnlyList<DD.Prototype.Achievements.AchievementDefinition> earnedThisRun, ProgressionConfigSection prog)
        {
            if (candidate == null)
            {
                return 0;
            }

            int perAsh = prog != null ? Math.Max(1, prog.XpPerAsh) : 40;
            long ash = input.TotalXp / perAsh;

            if (input.ReachedRound >= 10 && !string.IsNullOrEmpty(input.WeaponKey) && candidate.Grant(candidate.AshMilestones, "r10:" + input.WeaponKey))
            {
                ash += prog != null ? Math.Max(0, prog.AshFirstRound10PerWeapon) : 30;
            }

            if (input.TreeCapstones > 0 && candidate.Grant(candidate.AshMilestones, "capstone"))
            {
                ash += prog != null ? Math.Max(0, prog.AshFirstCapstone) : 25;
            }

            if (input.TreeLinkII > 0 && candidate.Grant(candidate.AshMilestones, "link2"))
            {
                ash += prog != null ? Math.Max(0, prog.AshFirstLinkII) : 15;
            }

            if (earnedThisRun != null && prog != null)
            {
                for (int i = 0; i < earnedThisRun.Count; i++)
                {
                    if (earnedThisRun[i] != null) ash += prog.AshForRarity((int)earnedThisRun[i].Rarity);
                }
            }

            int gained = (int)Math.Min(ash, int.MaxValue);
            candidate.AshEarnedTotal = SaturatingAdd(candidate.AshEarnedTotal, gained);
            candidate.AshBalance = candidate.AshEarnedTotal - candidate.AshSpentTotal;
            return gained;
        }

        // ------------------------------------------------------------- statistics

        /// <summary>WP-023: folds the tracked feats (special pickups, flawless rounds) into the candidate. Saturating.</summary>
        public static void ApplyFeats(ProfileData candidate, in DD.Prototype.Achievements.RunFeatCounters feats)
        {
            if (candidate == null)
            {
                return;
            }

            candidate.SpecialWeaponPickups = SaturatingAdd(candidate.SpecialWeaponPickups, Math.Max(0, feats.SpecialWeaponPickups));
            candidate.FlawlessRounds = SaturatingAdd(candidate.FlawlessRounds, Math.Max(0, feats.FlawlessRounds));

            // WP-077 A10: the run's weapon / Special feats — totals add up, "max" feats keep the best single event.
            if (candidate.FeatLifetime == null || candidate.FeatLifetime.Length != DD.Prototype.Achievements.FeatKinds.Count) candidate.Sanitize();
            for (int k = 0; k < DD.Prototype.Achievements.FeatKinds.Count; k++)
            {
                var kind = (DD.Prototype.Achievements.FeatKind)k;
                int run = feats.Feat(kind);
                candidate.FeatLifetime[k] = DD.Prototype.Achievements.FeatKinds.IsMax(kind)
                    ? Math.Max(candidate.FeatLifetime[k], run)
                    : SaturatingAdd(candidate.FeatLifetime[k], run);
            }
        }

        /// <summary>Folds the run's counters into the candidate. Saturating everywhere.</summary>
        public static void ApplyStatistics(ProfileData candidate, in RunRewardInput input)
        {
            candidate.RunsPlayed = SaturatingAdd(candidate.RunsPlayed, 1);

            // WP-077: mastery and the per-weapon capstone record merge with the statistics, so the
            // achievement rules that read them (mastery_5_any, capstones_5_weapons) see this run.
            candidate.AddMasteryKills(input.WeaponKey, input.TotalKills);
            if (input.TreeWeaponCapstone > 0) candidate.AddWeaponCapstone(input.WeaponKey);
            candidate.RoundsCompleted = SaturatingAdd(candidate.RoundsCompleted, input.CompletedRounds);
            candidate.HighestRound = Math.Max(candidate.HighestRound, input.ReachedRound);

            candidate.StandardKills = SaturatingAdd(candidate.StandardKills, input.StandardKills);
            candidate.SpecialKills = SaturatingAdd(candidate.SpecialKills, input.SpecialKills);
            candidate.EliteKills = SaturatingAdd(candidate.EliteKills, input.EliteKills);
            candidate.UltraKills = SaturatingAdd(candidate.UltraKills, input.UltraKills);
            candidate.UnattributedKills = SaturatingAdd(candidate.UnattributedKills, input.UnattributedKills);

            // The total is DERIVED from the parts, never added independently: that is the
            // whole of the kill invariant (TotalKills == tier sum + unattributed).
            candidate.TotalKills = SaturatingAdd(
                SaturatingSum(candidate.StandardKills, candidate.SpecialKills, candidate.EliteKills, candidate.UltraKills),
                candidate.UnattributedKills);

            candidate.ArmorPiecesBroken = SaturatingAdd(candidate.ArmorPiecesBroken, input.ArmorPiecesBroken);

            int gained = input.TotalXp;
            candidate.BestRunXp = Math.Max(candidate.BestRunXp, gained);
            candidate.TotalXpEarned = SaturatingAdd(candidate.TotalXpEarned, gained);
        }

        // ---------------------------------------------------------------- unlocks

        /// <summary>
        /// Grants everything the candidate's level entitles it to that it does not
        /// already hold. Grants are ADDITIVE ONLY: a ladder that has risen since an
        /// entitlement was earned never takes it back (WP-009 entitlement rule), because
        /// the earned list, not the ladder, is what the access gates read.
        ///
        /// <paramref name="newlyUnlocked"/> receives only what was not already held,
        /// which is what makes "Newly Unlocked Content" honest; pass null to seed
        /// silently (first load).
        /// </summary>
        public static void GrantUnlocksForLevel(ProfileData candidate, IUnlockLadder ladder, List<ProfileUnlockEntry> newlyUnlocked)
        {
            if (candidate == null || ladder == null)
            {
                return;
            }

            int level = candidate.ProfileLevel;

            var weapons = (ClassicWeaponId[])Enum.GetValues(typeof(ClassicWeaponId));
            for (int i = 0; i < weapons.Length; i++)
            {
                ClassicWeaponId id = weapons[i];
                if (level < Math.Max(1, ladder.UnlockLevelFor(id)))
                {
                    continue;
                }

                if (candidate.Grant(candidate.UnlockedWeapons, NameOf(id)) && newlyUnlocked != null)
                {
                    newlyUnlocked.Add(new ProfileUnlockEntry(
                        ladder.WeaponDisplayName(id),
                        "Classic weapon - selectable before your next run",
                        ProfileUnlockKind.Weapon, NameOf(id)));
                }
            }

            var branches = (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch));
            for (int i = 0; i < branches.Length; i++)
            {
                UpgradeBranch branch = branches[i];
                if (level < Math.Max(1, ladder.UnlockLevelFor(branch)))
                {
                    continue;
                }

                if (candidate.Grant(candidate.UnlockedBranches, NameOf(branch)) && newlyUnlocked != null)
                {
                    newlyUnlocked.Add(new ProfileUnlockEntry(
                        ladder.BranchDisplayName(branch),
                        "Upgrade branch - can now be offered at the end of a round",
                        ProfileUnlockKind.Branch, NameOf(branch)));
                }
            }
        }

        /// <summary>
        /// The entitlement gate: the EARNED LIST is authoritative, not the level. Unknown
        /// strings in the list can never match a defined id, so they open nothing.
        /// </summary>
        public static bool IsWeaponEntitled(ProfileData profile, ClassicWeaponId id)
        {
            string name = NameOf(id);
            // WP-077 v5: the saved key; an unsanitised in-memory profile may still carry the M3 enum name.
            return profile != null && name != null &&
                   (profile.Has(profile.UnlockedWeapons, name) || profile.Has(profile.UnlockedWeapons, id.ToString()));
        }

        public static bool IsBranchEntitled(ProfileData profile, UpgradeBranch branch)
        {
            string name = NameOf(branch);
            return profile != null && name != null && profile.Has(profile.UnlockedBranches, name);
        }

        // Enum names, resolved once: the gates are called from selection clicks and offer
        // builds, and a per-call ToString()/IsDefined would allocate for nothing.
        private static readonly UpgradeBranch[] s_branchIds = (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch));
        private static readonly string[] s_branchNames = Enum.GetNames(typeof(UpgradeBranch));

        /// <summary>
        /// WP-077 v5: the saved key of a weapon — its WP-069 slot key ("ar_1", "smg_1" …), independent of
        /// the enum member names (S-02 may rename them). Null for a value outside the arsenal.
        /// </summary>
        public static string NameOf(ClassicWeaponId id)
        {
            int i = (int)id;
            return i >= 0 && i < s_weaponKeys.Length ? s_weaponKeys[i] : null;
        }

        // Runtime index → WP-069 key (WeaponIdMigration.TryGetRuntimeId is the inverse; the M3 0/1/2 kept).
        private static readonly string[] s_weaponKeys = { "ar_1", "smg_1", "sg_1", "ar_2", "smg_2", "sg_2", "lmg_1", "lmg_2", "sr_1", "rl_1" };

        /// <summary>The defined name of a branch, or null for a value the build does not define.</summary>
        public static string NameOf(UpgradeBranch branch)
        {
            for (int i = 0; i < s_branchIds.Length; i++)
            {
                if (s_branchIds[i] == branch)
                {
                    return s_branchNames[i];
                }
            }

            return null;
        }

        // ----------------------------------------------------------- achievements

        /// <summary>
        /// Grants every defined, not-yet-earned achievement whose trigger the candidate's
        /// MERGED lifetime totals now satisfy. Definitions are injected, so the
        /// infrastructure is proven with synthetic fixtures and the shipped list stays
        /// PrototypeOnly content.
        /// </summary>
        public static void EvaluateAchievements(ProfileData candidate, in RunRewardInput input,
            IReadOnlyList<AchievementSettings> definitions, List<ProfileUnlockEntry> earned)
        {
            if (candidate == null || definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                AchievementSettings definition = definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.Id))
                {
                    continue;
                }

                if (candidate.Has(candidate.Achievements, definition.Id))
                {
                    continue;
                }

                if (!IsSatisfied(candidate, input, definition))
                {
                    continue;
                }

                if (candidate.Grant(candidate.Achievements, definition.Id) && earned != null)
                {
                    earned.Add(new ProfileUnlockEntry(
                        string.IsNullOrEmpty(definition.DisplayName) ? definition.Id : definition.DisplayName,
                        definition.Description ?? string.Empty,
                        ProfileUnlockKind.Achievement, definition.Id));
                }
            }
        }

        /// <summary>
        /// Thresholds are checked against the MERGED lifetime totals, so "Armor Breaker"
        /// is earned by the run that pushes the lifetime count over the line rather than
        /// requiring one run to do it all. A threshold below 1 reads as 1.
        /// </summary>
        public static bool IsSatisfied(ProfileData candidate, in RunRewardInput input, AchievementSettings definition)
        {
            int threshold = Math.Max(1, definition.Threshold);

            switch (definition.Trigger)
            {
                case AchievementTrigger.FirstKill:
                    return candidate.TotalKills >= threshold;

                case AchievementTrigger.ArmorPieceBroken:
                    return candidate.ArmorPiecesBroken >= threshold;

                case AchievementTrigger.ReachRound:
                    return candidate.HighestRound >= threshold;

                case AchievementTrigger.KillTier:
                    return candidate.KillsForTierName(definition.TargetTier.ToString()) >= threshold
                           || input.KillsFor(definition.TargetTier) >= threshold;

                default:
                    return false;
            }
        }
    }
}
