using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>WP-068 R1-E: what a <see cref="ProfileUnlockEntry"/> granted, so a reader resolves it by identity, never by its English copy.</summary>
    public enum ProfileUnlockKind
    {
        /// <summary>A hand-built entry with no identity (tests, older callers): readers show its copy as is.</summary>
        Unknown = 0,
        Weapon = 1,
        Branch = 2,
        Content = 3,
        Achievement = 4
    }

    /// <summary>One line in the "Newly Unlocked Content" or "Achievements Earned" list.</summary>
    public struct ProfileUnlockEntry
    {
        /// <summary>English name, e.g. "Combat Shotgun".</summary>
        public string Title;

        /// <summary>English one-liner, e.g. "Classic weapon - now selectable before a run".</summary>
        public string Detail;

        /// <summary>WP-068 R1-E: the kind of grant; <see cref="ProfileUnlockKind.Unknown"/> for the two-argument form.</summary>
        public ProfileUnlockKind Kind;

        /// <summary>
        /// WP-068 R1-E: the granted id - the weapon's saved key ("ar_1"), the branch's enum name, the unlock catalog id or
        /// the achievement id; empty for <see cref="ProfileUnlockKind.Unknown"/>.
        /// </summary>
        public string Id;

        public ProfileUnlockEntry(string title, string detail)
            : this(title, detail, ProfileUnlockKind.Unknown, string.Empty)
        {
        }

        public ProfileUnlockEntry(string title, string detail, ProfileUnlockKind kind, string id)
        {
            Title = title;
            Detail = detail;
            Kind = kind;
            Id = id ?? string.Empty;
        }
    }

    /// <summary>How a profile transaction ended (WP-009).</summary>
    public enum ProfileCommitOutcome
    {
        /// <summary>The run was folded into the profile and the profile is on disk.</summary>
        Applied = 0,

        /// <summary>The summary carried no completion identity; nothing was changed.</summary>
        MissingIdentity = 1,

        /// <summary>This completion identity had already been applied; nothing was changed.</summary>
        Duplicate = 2,

        /// <summary>The candidate profile could not be written; the live profile is unchanged.</summary>
        SaveFailed = 3,

        /// <summary>The candidate failed validation before the write; the live profile is unchanged.</summary>
        InvalidCandidate = 4,

        /// <summary>
        /// WP-009H: the completion identity was refused fail-closed (malformed, legacy
        /// GUID, foreign lineage, out-of-sequence ordinal, or not the live capability -
        /// stale, aborted, forged, or from a previous process); nothing was changed.
        /// </summary>
        InvalidIdentity = 5
    }

    /// <summary>
    /// The receipt of one profile transaction: everything the Run Results screen needs
    /// that the run itself could not know.
    ///
    /// <c>RunSummary</c> answers "what happened in the run"; this answers "what that did
    /// to the profile" - result lines 10, 11 and 12. The split is deliberate and comes
    /// from the run contract: a run summary that already knew the player's level would
    /// have made the profile system unfalsifiable.
    ///
    /// IMMUTABLE (WP-009): produced once by <see cref="ProfileService.ApplyRun"/> and
    /// then only read. Every member is get-only, the lists are read-only views, and
    /// the summary is a struct copy. The results screen cannot recompute anything,
    /// because the profile has already been written to disk by then and a second
    /// computation could disagree with it. When the transaction did NOT apply
    /// (<see cref="Applied"/> false) the receipt still describes the run, but the
    /// "after" values equal the "before" values and the reward lists are empty: the
    /// screen says the run was not recorded rather than pretending it was.
    /// </summary>
    public sealed class RunRewardReport
    {
        /// <summary>The mutable working set a transaction fills in before sealing it.</summary>
        public sealed class Draft
        {
            public bool HasRun;
            public RunSummary Summary;
            public string CompletionId = string.Empty;
            public ProfileCommitOutcome Outcome = ProfileCommitOutcome.Applied;
            public string FailureMessage = string.Empty;

            public int CompletedRoundXp;
            public int KillXp;
            public int TotalXpEarned;

            public int LevelBefore = 1;
            public int LevelAfter = 1;
            public int XpIntoLevelBefore;
            public int XpIntoLevelAfter;

            [PrototypeOnly(PendingDecisions.ProfileXp,
                "Level-1 requirement used as the empty-report default. " +
                "DEC-098 K4 curve: level requirement = 100·L + 10·L² → 110 at L1 (R1-B P3; was the old 100 + 75·(L − 1)).")]
            public int LevelRequirementAfter = 110;
            /// <summary>WP-077: Ash granted by this transaction (0 unless Applied).</summary>
            public int AshEarned;

            public readonly List<ProfileUnlockEntry> NewUnlocks = new List<ProfileUnlockEntry>(4);
            public readonly List<ProfileUnlockEntry> NewAchievements = new List<ProfileUnlockEntry>(4);

            /// <summary>WP-023: the definitions behind <see cref="NewAchievements"/> (id, rarity), same order.</summary>
            public readonly List<DD.Prototype.Achievements.AchievementDefinition> EarnedDefinitions = new List<DD.Prototype.Achievements.AchievementDefinition>(4);

            /// <summary>WP-023: catalog entries opened by this transaction (also listed in <see cref="NewUnlocks"/>).</summary>
            public readonly List<DD.Prototype.Achievements.UnlockDefinition> OpenedContent = new List<DD.Prototype.Achievements.UnlockDefinition>(4);

            /// <summary>WP-023: the feat counters this transaction folded in.</summary>
            public DD.Prototype.Achievements.RunFeatCounters Feats;

            /// <summary>WP-023: lifetime bests BEFORE this transaction, so Results can say "new best" honestly.</summary>
            public int HighestRoundBefore;
            public int BestRunXpBefore;

            public RunRewardReport Seal()
            {
                return new RunRewardReport(this);
            }
        }

        private readonly IReadOnlyList<ProfileUnlockEntry> _newUnlocks;
        private readonly IReadOnlyList<ProfileUnlockEntry> _newAchievements;
        private readonly IReadOnlyList<DD.Prototype.Achievements.AchievementDefinition> _earnedDefinitions;
        private readonly IReadOnlyList<DD.Prototype.Achievements.UnlockDefinition> _openedContent;

        private RunRewardReport(Draft draft)
        {
            HasRun = draft.HasRun;
            Summary = draft.Summary;
            CompletionId = draft.CompletionId ?? string.Empty;
            Outcome = draft.Outcome;
            FailureMessage = draft.FailureMessage ?? string.Empty;
            CompletedRoundXp = draft.CompletedRoundXp;
            KillXp = draft.KillXp;
            TotalXpEarned = draft.TotalXpEarned;
            LevelBefore = draft.LevelBefore;
            LevelAfter = draft.LevelAfter;
            XpIntoLevelBefore = draft.XpIntoLevelBefore;
            XpIntoLevelAfter = draft.XpIntoLevelAfter;
            LevelRequirementAfter = draft.LevelRequirementAfter;
            AshEarned = draft.AshEarned;
            _newUnlocks = new List<ProfileUnlockEntry>(draft.NewUnlocks).AsReadOnly();
            _newAchievements = new List<ProfileUnlockEntry>(draft.NewAchievements).AsReadOnly();
            _earnedDefinitions = new List<DD.Prototype.Achievements.AchievementDefinition>(draft.EarnedDefinitions).AsReadOnly();
            _openedContent = new List<DD.Prototype.Achievements.UnlockDefinition>(draft.OpenedContent).AsReadOnly();
            Feats = draft.Feats;
            HighestRoundBefore = draft.HighestRoundBefore;
            BestRunXpBefore = draft.BestRunXpBefore;
        }

        /// <summary>False when the screen was opened without a run behind it.</summary>
        public bool HasRun { get; }

        /// <summary>The run, as the director summarised it. A struct copy; nothing downstream can alter it.</summary>
        public RunSummary Summary { get; }

        /// <summary>The durable completion identity this receipt belongs to.</summary>
        public string CompletionId { get; }

        public ProfileCommitOutcome Outcome { get; }

        /// <summary>English reason when <see cref="Applied"/> is false.</summary>
        public string FailureMessage { get; }

        /// <summary>True only when the profile on disk now contains this run.</summary>
        public bool Applied => Outcome == ProfileCommitOutcome.Applied;

        // ------------------------------------------------------------------ xp

        /// <summary>Result line 7.</summary>
        public int CompletedRoundXp { get; }

        /// <summary>Result line 8.</summary>
        public int KillXp { get; }

        /// <summary>Result line 9. Round XP plus kill XP; gold and playtime contribute nothing.</summary>
        public int TotalXpEarned { get; }

        // --------------------------------------------------------------- level

        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public int XpIntoLevelBefore { get; }
        public int XpIntoLevelAfter { get; }

        /// <summary>XP the level AFTER the run requires to advance again.</summary>
        public int LevelRequirementAfter { get; }

        /// <summary>WP-077: Ash this run added to the meta PATH balance (Results "+N" chip).</summary>
        public int AshEarned { get; }

        public bool LeveledUp => LevelAfter > LevelBefore;

        public int LevelsGained => Mathf.Max(0, LevelAfter - LevelBefore);

        public float ProgressAfter01 => LevelRequirementAfter > 0
            ? Mathf.Clamp01(XpIntoLevelAfter / (float)LevelRequirementAfter)
            : 0f;

        // -------------------------------------------------------------- content

        /// <summary>Result line 11. Empty is a legitimate, common outcome - see the screen's empty state.</summary>
        public IReadOnlyList<ProfileUnlockEntry> NewUnlocks => _newUnlocks;

        /// <summary>Result line 12.</summary>
        public IReadOnlyList<ProfileUnlockEntry> NewAchievements => _newAchievements;

        /// <summary>WP-023: the achievement definitions earned by this transaction (id / rarity for Results).</summary>
        public IReadOnlyList<DD.Prototype.Achievements.AchievementDefinition> EarnedDefinitions => _earnedDefinitions;

        /// <summary>WP-023: catalog content opened by this transaction.</summary>
        public IReadOnlyList<DD.Prototype.Achievements.UnlockDefinition> OpenedContent => _openedContent;

        /// <summary>WP-023: the feat counters folded in (zero when the run was not recorded).</summary>
        public DD.Prototype.Achievements.RunFeatCounters Feats { get; }

        /// <summary>WP-023: the profile's highest round before this transaction.</summary>
        public int HighestRoundBefore { get; }

        /// <summary>WP-023: the profile's best run XP before this transaction.</summary>
        public int BestRunXpBefore { get; }

        /// <summary>True when the profile could not be written. The screen says so rather than lying.</summary>
        public bool SaveFailed => Outcome == ProfileCommitOutcome.SaveFailed;

        /// <summary>An empty report, for a results screen opened with no run behind it.</summary>
        public static RunRewardReport Empty()
        {
            return new Draft { HasRun = false }.Seal();
        }
    }
}
