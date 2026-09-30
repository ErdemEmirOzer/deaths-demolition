using System.Collections.Generic;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>One earned achievement as Run Results shows it (ADR-023-1).</summary>
    public readonly struct RunResultsAchievement
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;
        public readonly AchievementRarity Rarity;

        public RunResultsAchievement(string id, string title, string description, AchievementRarity rarity)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Rarity = rarity;
        }
    }

    /// <summary>One newly opened piece of content as Run Results shows it (ADR-023-1). DEC-038: no Claim; it is already open.</summary>
    public readonly struct RunResultsUnlock
    {
        public readonly string Id;

        /// <summary>Weapon / branch entries come from the level ladder OR the catalog; cosmetic / indicator only from the catalog.</summary>
        public readonly UnlockContentType Type;
        public readonly string Title;
        public readonly string Detail;

        public RunResultsUnlock(string id, UnlockContentType type, string title, string detail)
        {
            Id = id ?? string.Empty;
            Type = type;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    /// <summary>
    /// The complete, immutable Run Results data model (WP-023, ADR-023-1; DEC-036 fields):
    /// the run, the kill breakdown per enemy version, the XP breakdown, the level
    /// progress, and everything opened or earned by the run's profile transaction. Built
    /// once per transaction by <see cref="ProfileService"/>; the UI (S-09, WP-022) reads
    /// it through <see cref="IRunResultsSource"/> and never recomputes a number.
    ///
    /// Every field is a plain value or a read-only list; there is no reference back into
    /// the profile, the run or the config, so a screen can hold it across scene changes.
    /// A run that was NOT recorded (duplicate, invalid identity, save failure) still
    /// produces the model: the run happened and is shown; <see cref="Applied"/> and
    /// <see cref="RecordNotice"/> say what the profile did with it, and the reward lists
    /// are then empty (nothing was granted).
    /// </summary>
    public sealed class RunResultsData
    {
        private static readonly IReadOnlyList<RunResultsAchievement> s_noAchievements = new List<RunResultsAchievement>(0).AsReadOnly();
        private static readonly IReadOnlyList<RunResultsUnlock> s_noUnlocks = new List<RunResultsUnlock>(0).AsReadOnly();

        // ------------------------------------------------------------------ run

        public bool HasRun { get; private set; }
        public string CompletionId { get; private set; } = string.Empty;
        public RunEndReason EndReason { get; private set; }
        public string KilledBy { get; private set; } = string.Empty;
        public ClassicWeaponId Weapon { get; private set; }
        public string BuildDescription { get; private set; } = string.Empty;
        public float RunDurationSeconds { get; private set; }

        public int ReachedRound { get; private set; }
        public int CompletedRounds { get; private set; }

        // ---------------------------------------------------------------- kills

        public int TotalKills { get; private set; }
        public int StandardKills { get; private set; }
        public int SpecialKills { get; private set; }
        public int EliteKills { get; private set; }
        public int UltraKills { get; private set; }
        public EnemyTier HighestTierKilled { get; private set; }
        public int ArmorPiecesBroken { get; private set; }

        // ---------------------------------------------------------------- feats

        public int SpecialWeaponPickups { get; private set; }
        public int FlawlessRounds { get; private set; }

        // ----------------------------------------------------------------- gold

        public int GoldEarned { get; private set; }
        public int GoldSpent { get; private set; }

        /// <summary>WP-035: the parts of <see cref="GoldSpent"/> (WP-008: cards + paid rerolls).</summary>
        public int GoldSpentOnUpgrades { get; private set; }
        public int GoldSpentOnRerolls { get; private set; }
        public int GoldUnspent { get; private set; }
        public int UpgradesPurchased { get; private set; }
        public int RerollsUsed { get; private set; }

        // ------------------------------------------------------------------- xp

        public int CompletedRoundXp { get; private set; }
        public int KillXp { get; private set; }
        public int TotalXpEarned { get; private set; }

        // ---------------------------------------------------------------- level

        public int LevelBefore { get; private set; } = 1;
        public int LevelAfter { get; private set; } = 1;
        public int XpIntoLevelBefore { get; private set; }
        public int XpIntoLevelAfter { get; private set; }
        public int LevelRequirementAfter { get; private set; }
        public bool LeveledUp => LevelAfter > LevelBefore;
        public int LevelsGained => Mathf.Max(0, LevelAfter - LevelBefore);
        public float ProgressAfter01 => LevelRequirementAfter > 0 ? Mathf.Clamp01(XpIntoLevelAfter / (float)LevelRequirementAfter) : 0f;

        // ------------------------------------------------------------- lifetime

        public int LifetimeRunsPlayed { get; private set; }
        public int LifetimeHighestRound { get; private set; }
        public int LifetimeTotalKills { get; private set; }
        public bool IsNewHighestRound { get; private set; }
        public bool IsBestRunXp { get; private set; }

        // -------------------------------------------------------------- profile

        /// <summary>True when the profile transaction applied this run (rewards below were granted).</summary>
        public bool Applied { get; private set; }
        public ProfileCommitOutcome Outcome { get; private set; }

        /// <summary>The store's own sentence when the run was not recorded; empty when it was.</summary>
        public string RecordNotice { get; private set; } = string.Empty;

        public IReadOnlyList<RunResultsAchievement> NewAchievements { get; private set; } = s_noAchievements;
        public IReadOnlyList<RunResultsUnlock> NewUnlocks { get; private set; } = s_noUnlocks;

        public bool HasRewards => NewAchievements.Count > 0 || NewUnlocks.Count > 0 || LeveledUp;

        public static RunResultsData Empty()
        {
            return new RunResultsData();
        }

        /// <summary>Builds the model from a sealed receipt and the profile the transaction published (or left).</summary>
        public static RunResultsData From(RunRewardReport report, ProfileData profileAfter)
        {
            var data = new RunResultsData();
            if (report == null || !report.HasRun)
            {
                return data;
            }

            RunSummary s = report.Summary;
            var input = new RunRewardInput(s);

            data.HasRun = true;
            data.CompletionId = report.CompletionId ?? string.Empty;
            data.EndReason = s.EndReason;
            data.KilledBy = s.KilledBy ?? string.Empty;
            data.Weapon = s.Weapon;
            data.BuildDescription = s.BuildDescription ?? string.Empty;
            data.RunDurationSeconds = Mathf.Max(0f, s.RunDuration);
            data.ReachedRound = input.ReachedRound;
            data.CompletedRounds = input.CompletedRounds;

            data.TotalKills = input.TotalKills;
            data.StandardKills = input.StandardKills;
            data.SpecialKills = input.SpecialKills;
            data.EliteKills = input.EliteKills;
            data.UltraKills = input.UltraKills;
            data.HighestTierKilled = s.HighestTierKilled;
            data.ArmorPiecesBroken = input.ArmorPiecesBroken;

            data.SpecialWeaponPickups = Mathf.Max(0, report.Feats.SpecialWeaponPickups);
            data.FlawlessRounds = Mathf.Max(0, report.Feats.FlawlessRounds);

            data.GoldEarned = Mathf.Max(0, s.GoldEarned);
            data.GoldSpent = Mathf.Max(0, s.GoldSpent);
            data.GoldSpentOnUpgrades = Mathf.Max(0, s.GoldSpentOnUpgrades);
            data.GoldSpentOnRerolls = Mathf.Max(0, s.GoldSpentOnRerolls);
            data.GoldUnspent = Mathf.Max(0, s.GoldUnspent);
            data.UpgradesPurchased = Mathf.Max(0, s.UpgradesPurchased);
            data.RerollsUsed = Mathf.Max(0, s.RerollsUsed);

            data.CompletedRoundXp = report.CompletedRoundXp;
            data.KillXp = report.KillXp;
            data.TotalXpEarned = report.TotalXpEarned;

            data.LevelBefore = report.LevelBefore;
            data.LevelAfter = report.LevelAfter;
            data.XpIntoLevelBefore = report.XpIntoLevelBefore;
            data.XpIntoLevelAfter = report.XpIntoLevelAfter;
            data.LevelRequirementAfter = report.LevelRequirementAfter;

            data.Applied = report.Applied;
            data.Outcome = report.Outcome;
            data.RecordNotice = report.Applied ? string.Empty : (report.FailureMessage ?? string.Empty);

            if (profileAfter != null)
            {
                data.LifetimeRunsPlayed = profileAfter.RunsPlayed;
                data.LifetimeHighestRound = profileAfter.HighestRound;
                data.LifetimeTotalKills = profileAfter.TotalKills;
            }

            data.IsNewHighestRound = report.Applied && input.ReachedRound > report.HighestRoundBefore;
            data.IsBestRunXp = report.Applied && input.TotalXp > 0 && input.TotalXp > report.BestRunXpBefore;

            if (report.Applied)
            {
                var achievements = new List<RunResultsAchievement>(report.EarnedDefinitions.Count);
                for (int i = 0; i < report.EarnedDefinitions.Count; i++)
                {
                    AchievementDefinition d = report.EarnedDefinitions[i];
                    achievements.Add(new RunResultsAchievement(d.Id, d.DisplayName, d.Description, d.Rarity));
                }

                var unlocks = new List<RunResultsUnlock>(report.NewUnlocks.Count);
                // Level-ladder grants (weapon / branch) are listed first, as ProfileUnlockEntry
                // titles; catalog content follows with its id and type.
                int ladderCount = report.NewUnlocks.Count - report.OpenedContent.Count;
                for (int i = 0; i < ladderCount && i < report.NewUnlocks.Count; i++)
                {
                    ProfileUnlockEntry e = report.NewUnlocks[i];
                    // C-00 30.09 (after WP-068 R1-E 35f10261): the grant's identity decides, not its English copy. Only an
                    // entry without identity (ProfileUnlockKind.Unknown: tests, older callers) falls back to the text prefix.
                    bool branch = e.Kind == ProfileUnlockKind.Branch ||
                                  (e.Kind == ProfileUnlockKind.Unknown && e.Detail != null && e.Detail.StartsWith("Upgrade branch", System.StringComparison.Ordinal));
                    // WP-049: the id is the enum NAME / WP-069 weapon key. With an identity it is the entry's own id; otherwise
                    // it is resolved from the ladder's display name (an unresolvable title keeps the title, never invented).
                    string id = e.Kind != ProfileUnlockKind.Unknown && !string.IsNullOrEmpty(e.Id) ? e.Id : LadderId(e.Title, branch);
                    unlocks.Add(new RunResultsUnlock(id, branch ? UnlockContentType.Branch : UnlockContentType.Weapon, e.Title, e.Detail));
                }

                for (int i = 0; i < report.OpenedContent.Count; i++)
                {
                    UnlockDefinition u = report.OpenedContent[i];
                    unlocks.Add(new RunResultsUnlock(u.Id, u.Type, u.DisplayName, u.Detail));
                }

                data.NewAchievements = achievements.AsReadOnly();
                data.NewUnlocks = unlocks.AsReadOnly();
            }

            return data;
        }

        /// <summary>Resolves a ladder display name back to its enum name (weapon or branch) through the service's ladder.</summary>
        private static string LadderId(string title, bool branch)
        {
            IUnlockLadder ladder = ProfileService.Ladder;
            if (ladder == null || string.IsNullOrEmpty(title))
            {
                return title ?? string.Empty;
            }

            if (branch)
            {
                foreach (UpgradeBranch b in (UpgradeBranch[])System.Enum.GetValues(typeof(UpgradeBranch)))
                {
                    if (string.Equals(ladder.BranchDisplayName(b), title, System.StringComparison.Ordinal)) return b.ToString();
                }
            }
            else
            {
                foreach (ClassicWeaponId w in (ClassicWeaponId[])System.Enum.GetValues(typeof(ClassicWeaponId)))
                {
                    if (string.Equals(ladder.WeaponDisplayName(w), title, System.StringComparison.Ordinal)) return ProfileProgression.NameOf(w) ?? w.ToString();   // WP-077 v5 key
                }
            }

            return title;
        }
    }

    /// <summary>
    /// ADR-023-1: the one door Run Results reads. <see cref="ProfileService.ResultsSource"/>
    /// is the implementation; a test hands the UI its own.
    /// </summary>
    public interface IRunResultsSource
    {
        /// <summary>The last run's results, or an empty model (HasRun false) when there is none.</summary>
        RunResultsData Current { get; }
    }
}
