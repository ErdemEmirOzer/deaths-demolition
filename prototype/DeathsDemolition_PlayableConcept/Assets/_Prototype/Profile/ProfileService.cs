using System;
using System.Collections.Generic;
using DD.Prototype.Achievements;
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
    ///   level requirement config.LevelRequirement(L)   = 100·L + 10·L² (WP-077 K4, cap L50)
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
            "WP-077 K4: level requirement = 100·L + 10·L².")]
        private const int FallbackLevelRequirementBase = 100;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback for ProgressionConfigSection.LevelRequirementQuadratic. " +
            "WP-077 K4: level requirement = 100·L + 10·L².")]
        private const int FallbackLevelRequirementQuadratic = 10;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Fallback for ProgressionConfigSection.LevelCap (WP-077 K4).")]
        private const int FallbackLevelCap = 50;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback unlock level for the Auto SMG. Request value: Profile Level 2; DEC-085 Ö-3 (user, 2026-09-17): 3.")]
        private const int FallbackAutoSmgUnlockLevel = 3;

        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Fallback unlock level for the Combat Shotgun. Request value: Profile Level 3; DEC-085 Ö-3 (user, 2026-09-17): 5.")]
        private const int FallbackCombatShotgunUnlockLevel = 5;

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

        /// <summary>How the last <see cref="ApplyRun"/> judged its token (F1 panel, tests).</summary>
        public static CompletionTokenVerdict LastVerdict { get; private set; }

        /// <summary>
        /// WP-023 (ADR-023-1): the Run Results data model of the last transaction attempt;
        /// an empty model (HasRun false) before any run or after <see cref="ClearLastReport"/>.
        /// </summary>
        public static RunResultsData LastResults => s_lastResults ?? s_emptyResults;

        /// <summary>The <see cref="IRunResultsSource"/> the Results screen binds to.</summary>
        public static IRunResultsSource ResultsSource => s_resultsSource;

        private static readonly RunResultsData s_emptyResults = RunResultsData.Empty();
        private static RunResultsData s_lastResults;
        private static readonly IRunResultsSource s_resultsSource = new ServiceResultsSource();

        private sealed class ServiceResultsSource : IRunResultsSource
        {
            public RunResultsData Current => LastResults;
        }

        // ---------------------------------------------------------- WP-035 sources

        /// <summary>ADR-035-1: achievement progress for the Achievements pane / Results.</summary>
        public static IAchievementProgressSource AchievementProgressSource => s_progressSource;

        /// <summary>ADR-035-1: titles / badges / skins with selection state for the profile and selection screens.</summary>
        public static ICosmeticsSource CosmeticsSource => s_cosmeticsSource;

        /// <summary>
        /// The session's skin choice (DEC-039: not persisted). The UI flow installs its
        /// getter; without one the default palette is reported as selected.
        /// </summary>
        public static Func<DD.Prototype.PlayerSystem.PlayerSkin> SessionSkinProvider;

        private static readonly IAchievementProgressSource s_progressSource = new ServiceProgressSource();
        private static readonly ICosmeticsSource s_cosmeticsSource = new ServiceCosmeticsSource();

        private sealed class ServiceProgressSource : IAchievementProgressSource
        {
            public IReadOnlyList<AchievementProgressEntry> Entries
            {
                get { EnsureLoaded(); return ProfileCosmetics.BuildProgress(s_data).AsReadOnly(); }
            }

            public int EarnedCount
            {
                get { EnsureLoaded(); return s_data.Achievements.Count; }
            }

            public int TotalCount => AchievementCatalog.Definitions.Count;

            public int NewCount
            {
                get { EnsureLoaded(); return Math.Max(0, s_data.Achievements.Count - Math.Min(s_data.AchievementsSeen, s_data.Achievements.Count)); }
            }

            public string LastEarnedId
            {
                get { EnsureLoaded(); return s_data.Achievements.Count > 0 ? s_data.Achievements[s_data.Achievements.Count - 1] : string.Empty; }
            }

            public void MarkAchievementsSeen()
            {
                EnsureLoaded();
                if (s_data.AchievementsSeen == s_data.Achievements.Count)
                {
                    return;
                }

                s_data.AchievementsSeen = s_data.Achievements.Count;
                PersistCosmeticState();
            }
        }

        private sealed class ServiceCosmeticsSource : ICosmeticsSource
        {
            public IReadOnlyList<CosmeticEntry> Titles
            {
                get { EnsureLoaded(); return ProfileCosmetics.Build(s_data, UnlockContentType.Title, s_data.SelectedTitle, SessionSkin()).AsReadOnly(); }
            }

            public IReadOnlyList<CosmeticEntry> Badges
            {
                get { EnsureLoaded(); return ProfileCosmetics.Build(s_data, UnlockContentType.Badge, s_data.SelectedBadge, SessionSkin()).AsReadOnly(); }
            }

            public IReadOnlyList<CosmeticEntry> Skins
            {
                get { EnsureLoaded(); return ProfileCosmetics.Build(s_data, UnlockContentType.Cosmetic, string.Empty, SessionSkin()).AsReadOnly(); }
            }

            public string SelectedTitle
            {
                get { EnsureLoaded(); return s_data.SelectedTitle ?? string.Empty; }
            }

            public string SelectedBadge
            {
                get { EnsureLoaded(); return s_data.SelectedBadge ?? string.Empty; }
            }

            public int NewCount
            {
                get { EnsureLoaded(); return Math.Max(0, s_data.UnlockedContent.Count - Math.Min(s_data.ContentSeen, s_data.UnlockedContent.Count)); }
            }

            public bool SelectTitle(string id)
            {
                return SelectCosmetic(UnlockContentType.Title, id);
            }

            public bool SelectBadge(string id)
            {
                return SelectCosmetic(UnlockContentType.Badge, id);
            }

            public void MarkContentSeen()
            {
                EnsureLoaded();
                if (s_data.ContentSeen == s_data.UnlockedContent.Count)
                {
                    return;
                }

                s_data.ContentSeen = s_data.UnlockedContent.Count;
                PersistCosmeticState();
            }

            private static DD.Prototype.PlayerSystem.PlayerSkin SessionSkin()
            {
                Func<DD.Prototype.PlayerSystem.PlayerSkin> provider = SessionSkinProvider;
                return provider != null ? provider() : DD.Prototype.PlayerSystem.PlayerSkins.Default;
            }
        }

        /// <summary>
        /// WP-035: selects an opened title / badge (empty clears) and saves. Cosmetic state
        /// is written OUTSIDE the run transaction (no completion identity involved): it
        /// changes nothing the profile has earned, and a refused write (read-only session)
        /// leaves the in-memory choice untouched and reports false.
        /// </summary>
        public static bool SelectCosmetic(UnlockContentType type, string id)
        {
            EnsureLoaded();
            if (type != UnlockContentType.Title && type != UnlockContentType.Badge)
            {
                return false;
            }

            string value = id ?? string.Empty;
            if (value.Length > 0 && !ProfileCosmetics.CanSelect(s_data, type, value))
            {
                return false;
            }

            if (ProfileStore.SaveLocked)
            {
                return false;
            }

            string before = type == UnlockContentType.Title ? s_data.SelectedTitle : s_data.SelectedBadge;
            if (string.Equals(before, value, StringComparison.Ordinal))
            {
                return true;
            }

            if (type == UnlockContentType.Title)
            {
                s_data.SelectedTitle = value;
            }
            else
            {
                s_data.SelectedBadge = value;
            }

            if (!PersistCosmeticState())
            {
                if (type == UnlockContentType.Title)
                {
                    s_data.SelectedTitle = before;
                }
                else
                {
                    s_data.SelectedBadge = before;
                }

                return false;
            }

            RaiseChanged();
            return true;
        }

        private static bool PersistCosmeticState()
        {
            if (ProfileStore.SaveLocked)
            {
                return false;
            }

            return ProfileStore.Save(s_data).Success;
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
            int cap = config != null && config.Progression != null ? config.Progression.LevelCap : FallbackLevelCap;
            if (cap > 0 && level >= cap)
            {
                // WP-077 K4: the cap. XP keeps counting (TotalXpEarned, Ash); the level stops.
                return int.MaxValue;
            }

            int l = Mathf.Max(1, level);
            int required = config != null
                ? config.LevelRequirement(level)
                : FallbackLevelRequirementBase * l + FallbackLevelRequirementQuadratic * l * l;

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

        /// <summary>
        /// WP-069 B2: the unlock level of a weapon this build does not carry (a disabled Arsenal entry —
        /// rl_1 until B3). No profile level reaches it, so the weapon is never granted; lists skip it.
        /// </summary>
        public const int NotInBuildUnlockLevel = ClassicWeaponSettings.NotOnProfileLadder;

        public static int UnlockLevelFor(ClassicWeaponId id)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config == null)
            {
                // Request ladder, mirrored so a missing config cannot unlock everything.
                switch (id)
                {
                    case ClassicWeaponId.AssaultRifle: return DefaultUnlockLevel;
                    case ClassicWeaponId.AutoSmg: return FallbackAutoSmgUnlockLevel;
                    case ClassicWeaponId.CombatShotgun: return FallbackCombatShotgunUnlockLevel;
                    default: return NotInBuildUnlockLevel;   // WP-069 v3 ids need the Arsenal
                }
            }

            ClassicWeaponSettings settings = config.Weapons.FindExact(id);   // Find falls back to the first weapon
            return settings != null ? Mathf.Max(1, settings.UnlockProfileLevel) : NotInBuildUnlockLevel;
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

        /// <summary>
        /// Every achievement the build defines, earned or not, in catalog order (WP-023:
        /// read from Resources/Achievements/achievement-catalog.json; the legacy config
        /// list is no longer the source). The WP-005 presentation shape is kept.
        /// </summary>
        public static IReadOnlyList<AchievementSettings> AllAchievements => AchievementCatalog.Settings;

        /// <summary>WP-023: every catalog unlock entry, opened or not.</summary>
        public static IReadOnlyList<UnlockDefinition> AllContent => UnlockCatalog.Entries;

        /// <summary>WP-023: the entitlement gate for catalog content (cosmetic / indicator ids). The earned list decides.</summary>
        public static bool IsContentUnlocked(string id)
        {
            EnsureLoaded();
            return UnlockCatalog.IsUnlocked(s_data, id);
        }

        /// <summary>WP-023: current / threshold of an achievement for a progress readout (0 when unknown).</summary>
        public static long AchievementProgress(string id, out int threshold)
        {
            EnsureLoaded();
            AchievementDefinition definition = AchievementCatalog.Find(id);
            threshold = definition != null ? Math.Max(1, definition.Threshold) : 0;
            return definition != null && definition.Scope == AchievementScope.Lifetime
                ? Math.Min(AchievementEngine.LifetimeValue(s_data, definition), threshold)
                : (definition != null && s_data.Has(s_data.Achievements, id) ? threshold : 0);
        }

        /// <summary>
        /// Folds a finished run into the profile as one transaction (see the class
        /// summary) and returns the sealed receipt.
        ///
        /// Exactly-once (WP-009H): the summary's <see cref="RunSummary.CompletionId"/> is a
        /// token from <see cref="CompletionAuthority"/> - this profile's lineage, the next
        /// ordinal after <see cref="ProfileData.LastAppliedCompletionOrdinal"/>, and the
        /// capability this process issued for the run. The ordinal is written into the
        /// same candidate as the rewards, so once the save is on disk every token at or
        /// below it is refused as already applied - in this process, after a reload,
        /// after a relaunch, and however many runs later. There is no list to fall out
        /// of. A refused token changes nothing and raises no <see cref="Changed"/>; an
        /// already-applied one returns the original receipt when it is still in memory
        /// (a re-opened Results screen shows what actually happened).
        /// </summary>
        public static RunRewardReport ApplyRun(in RunSummary summary)
        {
            // The feats belong to the run whose token this is; any other summary gets none.
            return ApplyRun(summary, AchievementRunTracker.CountersFor(summary.CompletionId));
        }

        /// <summary>
        /// WP-023: the transaction with explicit feat counters (tests, probes). The
        /// counters ride the same candidate as the statistics, XP, achievements and
        /// unlocks: one write, exactly once.
        /// </summary>
        public static RunRewardReport ApplyRun(in RunSummary summary, in RunFeatCounters feats)
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
                LevelRequirementAfter = LevelRequirement(s_data.ProfileLevel),
                Feats = WithRunEconomy(feats, summary),
                HighestRoundBefore = s_data.HighestRound,
                BestRunXpBefore = s_data.BestRunXp
            };

            // ---- 1/2 identity: format → schema → lineage → ordinal → mark → sequence → capability
            CompletionToken token;
            CompletionTokenVerdict verdict = CompletionAuthority.Validate(input.CompletionId, s_data, out token);
            LastVerdict = verdict;

            switch (verdict)
            {
                case CompletionTokenVerdict.Valid:
                    break;

                case CompletionTokenVerdict.Missing:
                    draft.Outcome = ProfileCommitOutcome.MissingIdentity;
                    draft.FailureMessage = "This run carried no completion identity, so it was not recorded.";
                    Debug.LogWarning("[Profile] " + draft.FailureMessage);
                    return Publish(draft.Seal());

                case CompletionTokenVerdict.AlreadyApplied:
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
                        Publish(duplicate);
                    }

                    return duplicate;
                }

                default:
                    // Malformed / legacy GUID / unsupported schema / foreign lineage /
                    // bad ordinal / gap / not the live capability: fail-closed, no mutation.
                    draft.Outcome = ProfileCommitOutcome.InvalidIdentity;
                    draft.FailureMessage = "This run's completion identity was not valid for this profile (" + verdict + "), so it was not recorded.";
                    Debug.LogWarning("[Profile] " + draft.FailureMessage);
                    if (s_lastReport == null)
                    {
                        return Publish(draft.Seal());
                    }

                    return draft.Seal();
            }

            // ---- 3 candidate -------------------------------------------------
            ProfileData candidate = s_data.Clone();

            ProfileProgression.ApplyStatistics(candidate, input);
            ProfileProgression.ApplyFeats(candidate, draft.Feats);

            LevelProgress progress = ProfileProgression.ApplyXp(
                candidate.ProfileLevel, candidate.XpIntoLevel, input.TotalXp, LevelRequirement);
            candidate.ProfileLevel = progress.Level;
            candidate.XpIntoLevel = progress.XpIntoLevel;

            // Order matters: achievements read the merged statistics, level unlocks read
            // the level the XP just produced, and the content catalog reads both (an entry
            // keyed on an achievement earned by this very run opens in this transaction).
            AchievementEngine.Evaluate(candidate, input, draft.Feats, AchievementCatalog.Definitions, draft.EarnedDefinitions);
            for (int i = 0; i < draft.EarnedDefinitions.Count; i++)
            {
                AchievementDefinition d = draft.EarnedDefinitions[i];
                draft.NewAchievements.Add(new ProfileUnlockEntry(d.DisplayName, d.Description));
            }

            // WP-077: Ash + mastery, after the achievements this run earned (their rarity Ash counts).
            draft.AshEarned = ProfileProgression.ApplyAsh(candidate, input, draft.EarnedDefinitions,
                PrototypeConfig.Active != null ? PrototypeConfig.Active.Progression : null);

            ProfileProgression.GrantUnlocksForLevel(candidate, s_ladder, draft.NewUnlocks);
            UnlockCatalog.Grant(candidate, UnlockCatalog.Entries, draft.OpenedContent);
            for (int i = 0; i < draft.OpenedContent.Count; i++)
            {
                UnlockDefinition u = draft.OpenedContent[i];
                draft.NewUnlocks.Add(new ProfileUnlockEntry(u.DisplayName, u.Detail));
            }

            // The high-water mark advances in the SAME candidate as the rewards: they reach
            // the disk together or not at all.
            candidate.LastAppliedCompletionOrdinal = token.Ordinal;

            // ---- 4 validate --------------------------------------------------
            candidate.Sanitize();
            if (!candidate.IsValid())
            {
                ClearRewards(draft);
                draft.Outcome = ProfileCommitOutcome.InvalidCandidate;
                draft.FailureMessage = "The updated profile failed validation, so this run was not recorded.";
                Debug.LogWarning("[Profile] " + draft.FailureMessage);
                return Publish(draft.Seal());
            }

            // ---- 5 persist ---------------------------------------------------
            ProfileSaveResult saved = ProfileStore.Save(candidate);
            if (!saved.Success)
            {
                // The live profile stays exactly what is on disk; the id is not recorded,
                // so a retry of the same summary is a fresh attempt, not a duplicate. The
                // rewards computed on the discarded candidate were never granted, so the
                // receipt does not list them.
                ClearRewards(draft);
                draft.Outcome = ProfileCommitOutcome.SaveFailed;
                draft.FailureMessage = saved.Message;
                return Publish(draft.Seal());
            }

            // ---- 6 publish ---------------------------------------------------
            s_data = candidate;
            CompletionAuthority.Consume(input.CompletionId);
            draft.LevelAfter = candidate.ProfileLevel;
            draft.XpIntoLevelAfter = candidate.XpIntoLevel;
            draft.LevelRequirementAfter = LevelRequirement(candidate.ProfileLevel);
            draft.Outcome = ProfileCommitOutcome.Applied;

            // WP-078 (S-10) hook: one line per applied run for the progression tempo report.
            Debug.Log("[Progress] xp=" + input.TotalXp + " ash=" + draft.AshEarned + " ashBalance=" + candidate.AshBalance +
                      " meta=" + candidate.MetaNodes.Count + " level=" + draft.LevelBefore + "->" + candidate.ProfileLevel +
                      " round=" + input.ReachedRound + " weapon=" + input.WeaponKey +
                      " keystones=" + summary.TreeKeystones + " capstones=" + summary.TreeCapstones + " hearth=" + summary.TreeHearthStacks +
                      " upgrades=" + summary.UpgradesPurchased + " spendShare=" + (summary.GoldEarned > 0 ? (summary.GoldSpent / (float)summary.GoldEarned).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "0"));

            Publish(draft.Seal());
            RaiseChanged();
            return s_lastReport;
        }

        /// <summary>Single-run gold / purchase counters come from the summary; they are read by rules and Results only, never persisted.</summary>
        private static RunFeatCounters WithRunEconomy(in RunFeatCounters feats, in RunSummary summary)
        {
            RunFeatCounters f = feats.Clamped();
            f.GoldEarned = Math.Max(0, summary.GoldEarned);
            f.UpgradesPurchased = Math.Max(0, summary.UpgradesPurchased);
            return f;
        }

        private static void ClearRewards(RunRewardReport.Draft draft)
        {
            draft.NewUnlocks.Clear();
            draft.NewAchievements.Clear();
            draft.EarnedDefinitions.Clear();
            draft.OpenedContent.Clear();
            draft.AshEarned = 0;
        }

        /// <summary>Stores the receipt and builds its Run Results model from the profile as it now stands.</summary>
        private static RunRewardReport Publish(RunRewardReport report)
        {
            s_lastReport = report;
            s_lastResults = RunResultsData.From(report, s_data);
            return report;
        }

        // ================================================================ display

        public static string WeaponDisplayName(ClassicWeaponId id)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config != null)
            {
                ClassicWeaponSettings settings = config.Weapons.FindExact(id);
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
        ///
        /// WP-009H: a fresh profile (new lineage, ordinal 0) is written OVER the primary
        /// through the normal chain - there is no moment with the primary deleted and
        /// the fresh file not yet written - and the backup is then aligned with the new
        /// lineage so recovery cannot bring the old one back. Every token of the old
        /// lineage is refused from here on. Refused in a read-only session; on a failed
        /// write the old profile stays in memory and on disk.
        /// </summary>
        public static void ResetProfile()
        {
            EnsureLoaded();
            ProfileData fresh = ProfileStore.Reset();
            if (fresh == null)
            {
                return;
            }

            s_data = fresh;
            s_lastReport = null;
            s_lastResults = null;
            CompletionAuthority.InvalidateLive();
            ProfileProgression.GrantUnlocksForLevel(s_data, s_ladder, null);
            RaiseChanged();
        }

        /// <summary>Clears the cached report, e.g. when a new run starts.</summary>
        public static void ClearLastReport()
        {
            s_lastReport = null;
            s_lastResults = null;
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
            s_lastResults = null;
            LastVerdict = CompletionTokenVerdict.Missing;
            Changed = null;
        }
    }
}
