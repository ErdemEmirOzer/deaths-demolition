using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>
    /// The persistent profile: level, statistics, achievements and unlocked content.
    ///
    /// It is a lookup for one piece of local save data, not a gameplay singleton. It
    /// holds nothing about the live run, nothing about the player character, and no
    /// tunable value - persistent progress in this prototype cannot grant starting
    /// damage, health or gold, and the only way it could would be for a combat system
    /// to read a number from here. There is none to read.
    ///
    /// WHERE THE XP NUMBERS COME FROM
    /// ------------------------------
    /// All of them from PrototypeConfig, never from a literal in this file:
    ///   kill XP           config.KillXp(tier)          = 1 + tier bonus (0/1/3/8)
    ///   completed round   config.RoundCompletionXp(r)  = 15 + 5r
    ///   level requirement config.LevelRequirement(L)   = 100 + 75(L-1)
    /// Gold and playtime appear nowhere in the arithmetic, which is the request's rule.
    ///
    /// KNOWN AND INTENTIONAL: the unlock ladder is short enough to be exhausted inside
    /// one long run. Level 4 sits at 525 cumulative XP and a round-8 run earns roughly
    /// 545-600, so the second run of a session will usually find every weapon and branch
    /// already unlocked and "Newly Unlocked Content" permanently empty. The numbers are
    /// frozen by decision S6; what this code guarantees is that the EMPTY case is a
    /// designed state everywhere it can appear, not a blank rectangle.
    /// </summary>
    public static class ProfileService
    {
        // ------------------------------------------------------------ request fallbacks
        // Used ONLY when PrototypeConfig is missing (a menu scene opened directly in the
        // editor without going through Boot). Each mirrors the ProgressionConfigSection /
        // WeaponConfigSection / EconomyConfigSection value exactly, so a missing config can
        // neither unlock everything nor flatten the level curve.

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback for ProgressionConfigSection.LevelRequirementBase. " +
            "Request value: level requirement = 100 + 75 * (Level - 1).")]
        private const int FallbackLevelRequirementBase = 100;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback for ProgressionConfigSection.LevelRequirementPerLevel. " +
            "Request value: level requirement = 100 + 75 * (Level - 1).")]
        private const int FallbackLevelRequirementPerLevel = 75;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback unlock level for the Auto SMG. Request value: Profile Level 2.")]
        private const int FallbackAutoSmgUnlockLevel = 2;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback unlock level for the Combat Shotgun. Request value: Profile Level 3.")]
        private const int FallbackCombatShotgunUnlockLevel = 3;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback unlock level for the Explosion upgrade branch. Request value: Profile Level 2.")]
        private const int FallbackExplosionBranchUnlockLevel = 2;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback unlock level for the Chain upgrade branch. Request value: Profile Level 3.")]
        private const int FallbackChainBranchUnlockLevel = 3;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Level everything else is unlocked at. Request: the Assault Rifle and the base " +
            "branches are open from the start.")]
        private const int DefaultUnlockLevel = 1;

        private static ProfileData s_data;
        private static RunRewardReport s_lastReport;

        /// <summary>Raised after any change that a screen might be showing.</summary>
        public static event Action Changed;

        /// <summary>The live profile. Loaded on first touch; never null.</summary>
        public static ProfileData Current
        {
            get
            {
                EnsureLoaded();
                return s_data;
            }
        }

        /// <summary>What the last finished run did to the profile. Null before any run.</summary>
        public static RunRewardReport LastReport
        {
            get { return s_lastReport; }
        }

        /// <summary>True when the save on disk was unreadable and defaults were used.</summary>
        public static bool RecoveredFromCorruptSave
        {
            get
            {
                EnsureLoaded();
                return ProfileStore.LastLoadFellBack;
            }
        }

        // =============================================================== level curve

        /// <summary>XP required to advance FROM the given level. Never zero or negative.</summary>
        public static int LevelRequirement(int level)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            int required = config != null
                ? config.LevelRequirement(level)
                : FallbackLevelRequirementBase
                  + FallbackLevelRequirementPerLevel * (Mathf.Max(1, level) - 1);

            // A misconfigured curve must not be able to spin the level-up loop forever.
            return Mathf.Max(1, required);
        }

        /// <summary>XP still needed for the next level.</summary>
        public static int XpToNextLevel
        {
            get { return Mathf.Max(0, LevelRequirement(Current.ProfileLevel) - Current.XpIntoLevel); }
        }

        public static float LevelProgress01
        {
            get
            {
                int required = LevelRequirement(Current.ProfileLevel);
                return Mathf.Clamp01(Current.XpIntoLevel / (float)required);
            }
        }

        // ==================================================================== unlocks

        public static int UnlockLevelFor(ClassicWeaponId id)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config == null)
            {
                // Request ladder, mirrored so a missing config cannot unlock everything.
                switch (id)
                {
                    case ClassicWeaponId.AutoSmg: return FallbackAutoSmgUnlockLevel;
                    case ClassicWeaponId.CombatShotgun: return FallbackCombatShotgunUnlockLevel;
                    default: return DefaultUnlockLevel;
                }
            }

            ClassicWeaponSettings settings = config.Weapons.Find(id);
            return settings != null ? Mathf.Max(1, settings.UnlockProfileLevel) : 1;
        }

        public static int UnlockLevelFor(UpgradeBranch branch)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config == null)
            {
                switch (branch)
                {
                    case UpgradeBranch.Explosion: return FallbackExplosionBranchUnlockLevel;
                    case UpgradeBranch.Chain: return FallbackChainBranchUnlockLevel;
                    default: return DefaultUnlockLevel;
                }
            }

            List<UpgradeBranchSettings> branches = config.Economy.Branches;
            for (int i = 0; i < branches.Count; i++)
            {
                if (branches[i] != null && branches[i].Branch == branch)
                {
                    return Mathf.Max(1, branches[i].UnlockProfileLevel);
                }
            }

            return 1;
        }

        public static bool IsWeaponUnlocked(ClassicWeaponId id)
        {
            return Current.ProfileLevel >= UnlockLevelFor(id);
        }

        public static bool IsBranchUnlocked(UpgradeBranch branch)
        {
            return Current.ProfileLevel >= UnlockLevelFor(branch);
        }

        /// <summary>The first weapon the player is allowed to take. Never returns a locked one.</summary>
        public static ClassicWeaponId FirstUnlockedWeapon()
        {
            var ids = (ClassicWeaponId[])Enum.GetValues(typeof(ClassicWeaponId));
            for (int i = 0; i < ids.Length; i++)
            {
                if (IsWeaponUnlocked(ids[i]))
                {
                    return ids[i];
                }
            }

            return ClassicWeaponId.AssaultRifle;
        }

        // =============================================================== achievements

        public static bool HasAchievement(string id)
        {
            return Current.Has(Current.Achievements, id);
        }

        /// <summary>Every achievement the build defines, earned or not. Order is stable.</summary>
        public static IReadOnlyList<AchievementSettings> AllAchievements
        {
            get
            {
                PrototypeConfig config = PrototypeConfig.Active;
                if (config != null && config.Progression.Achievements != null)
                {
                    return config.Progression.Achievements;
                }

                return s_fallbackAchievements;
            }
        }

        /// <summary>
        /// Used only when PrototypeConfig is missing, e.g. a menu scene opened directly
        /// in the editor without going through Boot. The four names are the request's.
        /// </summary>
        private static readonly AchievementSettings[] s_fallbackAchievements =
        {
            new AchievementSettings
            {
                Id = "first_blood", DisplayName = "First Blood",
                Description = "Defeat your first enemy.",
                Trigger = AchievementTrigger.FirstKill, Threshold = 1
            },
            new AchievementSettings
            {
                Id = "armor_breaker", DisplayName = "Armor Breaker",
                Description = "Break an armour piece off an enemy.",
                Trigger = AchievementTrigger.ArmorPieceBroken, Threshold = 1
            },
            new AchievementSettings
            {
                Id = "reach_round_5", DisplayName = "Reach Round 5",
                Description = "Survive until round 5.",
                Trigger = AchievementTrigger.ReachRound, Threshold = 5
            },
            new AchievementSettings
            {
                Id = "defeat_an_ultra", DisplayName = "Defeat an Ultra",
                Description = "Bring down an Executioner.",
                Trigger = AchievementTrigger.KillTier, Threshold = 1, TargetTier = EnemyTier.Ultra
            }
        };

        // ================================================================ apply a run

        /// <summary>
        /// Folds a finished run into the profile and writes it to disk.
        ///
        /// Called exactly once per run, by <c>PrototypeUiFlow</c> on RunEvents.RunEnded,
        /// BEFORE the results scene loads - so the screen only ever reads a report that
        /// has already been persisted, and quitting on the results screen cannot lose
        /// the run.
        /// </summary>
        public static RunRewardReport ApplyRun(in RunSummary summary)
        {
            EnsureLoaded();

            var report = new RunRewardReport
            {
                HasRun = true,
                Summary = summary,
                LevelBefore = s_data.ProfileLevel,
                XpIntoLevelBefore = s_data.XpIntoLevel
            };

            // ---- xp, itemised ------------------------------------------------
            // The itemised parts are authoritative when they are present: the results
            // screen prints round XP and kill XP as separate lines, and a total that
            // disagreed with the sum of the lines above it would be a bug the player
            // can see. Gold and playtime never enter the arithmetic.
            int roundXp = Mathf.Max(0, summary.CompletedRoundXp);
            int killXp = Mathf.Max(0, summary.KillXp);
            int parts = roundXp + killXp;
            int gained = parts > 0 ? parts : Mathf.Max(0, summary.TotalXpEarned);

            report.CompletedRoundXp = roundXp;
            report.KillXp = killXp;
            report.TotalXpEarned = gained;

            // ---- statistics --------------------------------------------------
            s_data.RunsPlayed += 1;
            s_data.RoundsCompleted += Mathf.Max(0, summary.CompletedRounds);
            s_data.HighestRound = Mathf.Max(s_data.HighestRound, Mathf.Max(0, summary.ReachedRound));

            s_data.StandardKills += Mathf.Max(0, summary.StandardKills);
            s_data.SpecialKills += Mathf.Max(0, summary.SpecialKills);
            s_data.EliteKills += Mathf.Max(0, summary.EliteKills);
            s_data.UltraKills += Mathf.Max(0, summary.UltraKills);
            s_data.TotalKills += Mathf.Max(0, summary.TotalKills);

            s_data.ArmorPiecesBroken += Mathf.Max(0, summary.ArmorPiecesBroken);
            s_data.BestRunXp = Mathf.Max(s_data.BestRunXp, gained);
            s_data.TotalXpEarned += gained;

            // ---- level -------------------------------------------------------
            s_data.XpIntoLevel += gained;

            // Bounded: LevelRequirement() is clamped to at least 1, so this terminates,
            // and the cap is a second belt against a pathological config.
            const int maxLevelUpsPerRun = 512;
            int levelUps = 0;
            while (levelUps < maxLevelUpsPerRun)
            {
                int required = LevelRequirement(s_data.ProfileLevel);
                if (s_data.XpIntoLevel < required)
                {
                    break;
                }

                s_data.XpIntoLevel -= required;
                s_data.ProfileLevel += 1;
                levelUps++;
            }

            report.LevelAfter = s_data.ProfileLevel;
            report.XpIntoLevelAfter = s_data.XpIntoLevel;
            report.LevelRequirementAfter = LevelRequirement(s_data.ProfileLevel);

            // ---- achievements, then unlocks ----------------------------------
            // Order matters: achievements read the merged statistics, and unlocks read
            // the level the XP just produced.
            EvaluateAchievements(summary, report.NewAchievements);
            SyncUnlocks(report.NewUnlocks);

            s_data.Sanitize();
            report.SaveFailed = !ProfileStore.Save(s_data);

            s_lastReport = report;
            RaiseChanged();
            return report;
        }

        /// <summary>
        /// Grants everything the current level entitles the profile to.
        ///
        /// <paramref name="newlyUnlocked"/> collects only what was NOT already granted,
        /// which is what makes "Newly Unlocked Content" honest. The lists are seeded
        /// silently on first load, so the level-1 content a player has always had never
        /// appears on a results screen as if it were a reward.
        /// </summary>
        private static void SyncUnlocks(List<ProfileUnlockEntry> newlyUnlocked)
        {
            int level = s_data.ProfileLevel;

            var weapons = (ClassicWeaponId[])Enum.GetValues(typeof(ClassicWeaponId));
            for (int i = 0; i < weapons.Length; i++)
            {
                ClassicWeaponId id = weapons[i];
                if (level < UnlockLevelFor(id))
                {
                    continue;
                }

                if (s_data.Grant(s_data.UnlockedWeapons, id.ToString()) && newlyUnlocked != null)
                {
                    newlyUnlocked.Add(new ProfileUnlockEntry(
                        WeaponDisplayName(id),
                        "Classic weapon - selectable before your next run"));
                }
            }

            var branches = (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch));
            for (int i = 0; i < branches.Length; i++)
            {
                UpgradeBranch branch = branches[i];
                if (level < UnlockLevelFor(branch))
                {
                    continue;
                }

                if (s_data.Grant(s_data.UnlockedBranches, branch.ToString()) && newlyUnlocked != null)
                {
                    newlyUnlocked.Add(new ProfileUnlockEntry(
                        BranchDisplayName(branch),
                        "Upgrade branch - can now be offered at the end of a round"));
                }
            }
        }

        private static void EvaluateAchievements(in RunSummary summary, List<ProfileUnlockEntry> earned)
        {
            IReadOnlyList<AchievementSettings> definitions = AllAchievements;
            for (int i = 0; i < definitions.Count; i++)
            {
                AchievementSettings definition = definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.Id))
                {
                    continue;
                }

                if (s_data.Has(s_data.Achievements, definition.Id))
                {
                    continue;
                }

                if (!IsSatisfied(definition, summary))
                {
                    continue;
                }

                if (s_data.Grant(s_data.Achievements, definition.Id) && earned != null)
                {
                    earned.Add(new ProfileUnlockEntry(definition.DisplayName, definition.Description));
                }
            }
        }

        /// <summary>
        /// Thresholds are checked against the MERGED lifetime totals, not against the
        /// run alone, so "Armor Breaker" is earned by the run that pushes the lifetime
        /// count over the line rather than requiring one run to do it all.
        /// </summary>
        private static bool IsSatisfied(AchievementSettings definition, in RunSummary summary)
        {
            int threshold = Mathf.Max(1, definition.Threshold);

            switch (definition.Trigger)
            {
                case AchievementTrigger.FirstKill:
                    return s_data.TotalKills >= threshold;

                case AchievementTrigger.ArmorPieceBroken:
                    return s_data.ArmorPiecesBroken >= threshold;

                case AchievementTrigger.ReachRound:
                    return s_data.HighestRound >= threshold;

                case AchievementTrigger.KillTier:
                    return s_data.KillsForTierName(definition.TargetTier.ToString()) >= threshold
                           || summary.KillsFor(definition.TargetTier) >= threshold;

                default:
                    return false;
            }
        }

        // ================================================================ display

        public static string WeaponDisplayName(ClassicWeaponId id)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config != null)
            {
                ClassicWeaponSettings settings = config.Weapons.Find(id);
                if (settings != null && !string.IsNullOrEmpty(settings.DisplayName))
                {
                    return settings.DisplayName;
                }
            }

            switch (id)
            {
                case ClassicWeaponId.AutoSmg: return "Auto SMG";
                case ClassicWeaponId.CombatShotgun: return "Combat Shotgun";
                default: return "Assault Rifle";
            }
        }

        public static string BranchDisplayName(UpgradeBranch branch)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config != null && config.Economy.Branches != null)
            {
                List<UpgradeBranchSettings> branches = config.Economy.Branches;
                for (int i = 0; i < branches.Count; i++)
                {
                    if (branches[i] != null && branches[i].Branch == branch &&
                        !string.IsNullOrEmpty(branches[i].DisplayName))
                    {
                        return branches[i].DisplayName;
                    }
                }
            }

            return branch.ToString();
        }

        // ================================================================ lifetime

        /// <summary>Loads the profile if it has not been loaded yet. Safe to call anywhere.</summary>
        public static void EnsureLoaded()
        {
            if (s_data != null)
            {
                return;
            }

            s_data = ProfileStore.Load();

            // Establish the unlock baseline WITHOUT reporting it. Everything the current
            // level already entitles the profile to is recorded here, so the first
            // results screen of a fresh profile does not present the starting weapon as
            // a reward the player just won.
            SyncUnlocks(null);
        }

        /// <summary>Writes the profile now. Called on quit and after a debug edit.</summary>
        public static bool Save()
        {
            EnsureLoaded();
            return ProfileStore.Save(s_data);
        }

        /// <summary>
        /// Throws the profile away and starts again. Debug affordance; nothing in the
        /// normal flow reaches it.
        /// </summary>
        public static void ResetProfile()
        {
            ProfileStore.Delete();
            s_data = null;
            s_lastReport = null;
            EnsureLoaded();
            ProfileStore.Save(s_data);
            RaiseChanged();
        }

        /// <summary>Clears the cached report, e.g. when a new run starts.</summary>
        public static void ClearLastReport()
        {
            s_lastReport = null;
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_data = null;
            s_lastReport = null;
            Changed = null;
        }
    }
}
