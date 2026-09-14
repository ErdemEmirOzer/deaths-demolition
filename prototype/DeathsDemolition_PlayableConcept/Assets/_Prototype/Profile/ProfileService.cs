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
    /// The arithmetic itself lives in <see cref="ProfileProgression"/>.
    ///
    /// THE TRANSACTION (WP-009) - <see cref="ApplyRun"/>
    /// ---------------------------------------------------
    ///   1 reduce the summary to <see cref="RunRewardInput"/> (clamped, two XP sources only);
    ///   2 refuse a summary with no completion identity, or one already applied;
    ///   3 compute XP, level, statistics, unlocks and achievements on a CANDIDATE copy;
    ///   4 sanitise and validate the candidate;
    ///   5 write it through the crash-safe store;
    ///   6 only then publish the candidate as the live profile, seal the receipt, and
    ///     raise <see cref="Changed"/> exactly once.
    /// A failed write leaves the live profile exactly equal to the disk, records no
    /// identity (so the same summary may be retried without a duplicate), and hands
    /// the screen a receipt that says so.
    ///
    /// ENTITLEMENT (WP-009): the earned lists in the profile are the access authority.
    /// The level and the config ladder decide only what is NEWLY granted; a ladder
    /// that later rises never re-locks content already in the list.
    ///
    /// KNOWN AND INTENTIONAL: the unlock ladder is short enough to be exhausted inside
    /// one long run. The numbers are PrototypeOnly placeholders; what this code
    /// guarantees is that the EMPTY case is a designed state everywhere it can appear.
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
        private static readonly ConfigLadder s_ladder = new ConfigLadder();

        /// <summary>Raised after any change that a screen might be showing. Once per committed transaction.</summary>
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

        /// <summary>The receipt of the last transaction attempt. Null before any run.</summary>
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

        /// <summary>True while nothing can be written (newer save file, or another instance owns the profile).</summary>
        public static bool SaveLocked
        {
            get
            {
                EnsureLoaded();
                return ProfileStore.SaveLocked;
            }
        }

        /// <summary>
        /// The one English sentence the profile pane shows about persistence, or empty:
        /// fresh after corruption, restored from backup, read-only (newer file / other
        /// instance). Never a timestamp, never a number.
        /// </summary>
        public static string PersistenceNotice
        {
            get
            {
                EnsureLoaded();
                if (ProfileStore.SaveLocked)
                {
                    return ProfileStore.SaveLockReason;
                }

                switch (ProfileStore.LastLoadRecovery)
                {
                    case ProfileLoadRecovery.FreshAfterCorruption:
                        return "The previous profile file could not be read, so this profile started fresh. " +
                               "The unreadable file was kept next to the save.";

                    case ProfileLoadRecovery.RestoredBackup:
                        return "The profile file could not be used, so the last verified backup was restored. " +
                               "The most recent run may not be in it.";

                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>The ladder the transaction reads: PrototypeConfig through this class.</summary>
        public static IUnlockLadder Ladder => s_ladder;

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

        /// <summary>The access gate for weapons: reads the EARNED list, not the level (WP-009).</summary>
        public static bool IsWeaponUnlocked(ClassicWeaponId id)
        {
            return ProfileProgression.IsWeaponEntitled(Current, id);
        }

        /// <summary>The access gate for upgrade branches: reads the EARNED list, not the level (WP-009).</summary>
        public static bool IsBranchUnlocked(UpgradeBranch branch)
        {
            return ProfileProgression.IsBranchEntitled(Current, branch);
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
        /// in the editor without going through Boot. The four names are the request's;
        /// they are PrototypeOnly content, not a final list.
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
        /// Folds a finished run into the profile as one transaction (see the class
        /// summary) and returns the sealed receipt.
        ///
        /// Exactly-once: the summary's <see cref="RunSummary.CompletionId"/> is recorded in
        /// the profile that is written, so the same completion can never be applied
        /// twice - not in this process, not after a reload, not after a relaunch. A
        /// second call with an already-applied id returns the original receipt when it
        /// is still in memory (so a re-opened Results screen shows what actually
        /// happened) and a <see cref="ProfileCommitOutcome.Duplicate"/> receipt otherwise;
        /// neither changes anything or raises <see cref="Changed"/>.
        /// </summary>
        public static RunRewardReport ApplyRun(in RunSummary summary)
        {
            EnsureLoaded();

            var input = new RunRewardInput(summary);
            var draft = new RunRewardReport.Draft
            {
                HasRun = true,
                Summary = summary,
                CompletionId = input.CompletionId ?? string.Empty,
                CompletedRoundXp = input.CompletedRoundXp,
                KillXp = input.KillXp,
                TotalXpEarned = input.TotalXp,
                LevelBefore = s_data.ProfileLevel,
                XpIntoLevelBefore = s_data.XpIntoLevel,
                LevelAfter = s_data.ProfileLevel,
                XpIntoLevelAfter = s_data.XpIntoLevel,
                LevelRequirementAfter = LevelRequirement(s_data.ProfileLevel)
            };

            // ---- 1/2 identity ------------------------------------------------
            if (!input.HasIdentity)
            {
                draft.Outcome = ProfileCommitOutcome.MissingIdentity;
                draft.FailureMessage = "This run carried no completion identity, so it was not recorded.";
                Debug.LogWarning("[Profile] " + draft.FailureMessage);
                s_lastReport = draft.Seal();
                return s_lastReport;
            }

            if (s_data.HasAppliedCompletion(input.CompletionId))
            {
                if (s_lastReport != null && s_lastReport.HasRun &&
                    string.Equals(s_lastReport.CompletionId, input.CompletionId, StringComparison.Ordinal))
                {
                    // The receipt of the transaction that applied it. Nothing changes.
                    return s_lastReport;
                }

                draft.Outcome = ProfileCommitOutcome.Duplicate;
                draft.FailureMessage = "This run had already been recorded in the profile.";
                RunRewardReport duplicate = draft.Seal();
                if (s_lastReport == null)
                {
                    // After a relaunch there is no receipt in memory; the honest "already
                    // recorded" one is better for a Results screen than the empty state.
                    // A receipt of a different run is never displaced by a stale replay.
                    s_lastReport = duplicate;
                }

                return duplicate;
            }

            // ---- 3 candidate -------------------------------------------------
            ProfileData candidate = s_data.Clone();

            ProfileProgression.ApplyStatistics(candidate, input);

            LevelProgress progress = ProfileProgression.ApplyXp(
                candidate.ProfileLevel, candidate.XpIntoLevel, input.TotalXp, LevelRequirement);
            candidate.ProfileLevel = progress.Level;
            candidate.XpIntoLevel = progress.XpIntoLevel;

            // Order matters: achievements read the merged statistics, and unlocks read
            // the level the XP just produced.
            ProfileProgression.EvaluateAchievements(candidate, input, AllAchievements, draft.NewAchievements);
            ProfileProgression.GrantUnlocksForLevel(candidate, s_ladder, draft.NewUnlocks);

            candidate.RecordAppliedCompletion(input.CompletionId);

            // ---- 4 validate --------------------------------------------------
            candidate.Sanitize();
            if (!candidate.IsValid())
            {
                draft.NewUnlocks.Clear();
                draft.NewAchievements.Clear();
                draft.Outcome = ProfileCommitOutcome.InvalidCandidate;
                draft.FailureMessage = "The updated profile failed validation, so this run was not recorded.";
                Debug.LogWarning("[Profile] " + draft.FailureMessage);
                s_lastReport = draft.Seal();
                return s_lastReport;
            }

            // ---- 5 persist ---------------------------------------------------
            ProfileSaveResult saved = ProfileStore.Save(candidate);
            if (!saved.Success)
            {
                // The live profile stays exactly what is on disk; the id is not recorded,
                // so a retry of the same summary is a fresh attempt, not a duplicate. The
                // rewards computed on the discarded candidate were never granted, so the
                // receipt does not list them.
                draft.NewUnlocks.Clear();
                draft.NewAchievements.Clear();
                draft.Outcome = ProfileCommitOutcome.SaveFailed;
                draft.FailureMessage = saved.Message;
                s_lastReport = draft.Seal();
                return s_lastReport;
            }

            // ---- 6 publish ---------------------------------------------------
            s_data = candidate;
            draft.LevelAfter = candidate.ProfileLevel;
            draft.XpIntoLevelAfter = candidate.XpIntoLevel;
            draft.LevelRequirementAfter = LevelRequirement(candidate.ProfileLevel);
            draft.Outcome = ProfileCommitOutcome.Applied;

            s_lastReport = draft.Seal();
            RaiseChanged();
            return s_lastReport;
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

        /// <summary>The config-backed ladder handed to <see cref="ProfileProgression"/>.</summary>
        private sealed class ConfigLadder : IUnlockLadder
        {
            public int UnlockLevelFor(ClassicWeaponId id) => ProfileService.UnlockLevelFor(id);
            public int UnlockLevelFor(UpgradeBranch branch) => ProfileService.UnlockLevelFor(branch);
            public string WeaponDisplayName(ClassicWeaponId id) => ProfileService.WeaponDisplayName(id);
            public string BranchDisplayName(UpgradeBranch branch) => ProfileService.BranchDisplayName(branch);
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
            // a reward the player just won. Additive only; persisted by the next save.
            ProfileProgression.GrantUnlocksForLevel(s_data, s_ladder, null);
        }

        /// <summary>Writes the live profile now. Called on quit and after a debug edit.</summary>
        public static bool Save()
        {
            EnsureLoaded();
            return ProfileStore.Save(s_data).Success;
        }

        /// <summary>
        /// Throws the profile away and starts again. Debug affordance; nothing in the
        /// normal flow reaches it.
        /// </summary>
        public static void ResetProfile()
        {
            EnsureLoaded();
            if (!ProfileStore.Delete())
            {
                // Read-only session (newer save file / other instance): nothing is deleted
                // and nothing changes.
                return;
            }

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
