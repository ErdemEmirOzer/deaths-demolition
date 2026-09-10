using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>One line in the "Newly Unlocked Content" or "Achievements Earned" list.</summary>
    public struct ProfileUnlockEntry
    {
        /// <summary>English name, e.g. "Combat Shotgun".</summary>
        public string Title;

        /// <summary>English one-liner, e.g. "Classic weapon - now selectable before a run".</summary>
        public string Detail;

        public ProfileUnlockEntry(string title, string detail)
        {
            Title = title;
            Detail = detail;
        }
    }

    /// <summary>
    /// Everything the Run Results screen needs that the run itself could not know.
    ///
    /// <c>RunSummary</c> answers "what happened in the run"; this answers "what that did
    /// to the profile" - result lines 10, 11 and 12. The split is deliberate and comes
    /// from the run contract: a run summary that already knew the player's level would
    /// have made the profile system unfalsifiable.
    ///
    /// The report is produced ONCE, when the run ends, and then only read. The results
    /// screen must not recompute anything, because the profile has already been written
    /// to disk by then and a second computation could disagree with it.
    /// </summary>
    public sealed class RunRewardReport
    {
        /// <summary>False when the screen was opened without a run behind it.</summary>
        public bool HasRun;

        public RunSummary Summary;

        // ------------------------------------------------------------------ xp

        /// <summary>Result line 7.</summary>
        public int CompletedRoundXp;

        /// <summary>Result line 8.</summary>
        public int KillXp;

        /// <summary>Result line 9. Round XP plus kill XP; gold and playtime contribute nothing.</summary>
        public int TotalXpEarned;

        // --------------------------------------------------------------- level

        public int LevelBefore = 1;
        public int LevelAfter = 1;

        public int XpIntoLevelBefore;
        public int XpIntoLevelAfter;

        /// <summary>XP the level AFTER the run requires to advance again.</summary>
        /// <remarks>
        /// The initialiser is the level-1 requirement, so an empty report shows a full,
        /// honest progress bar instead of dividing by zero. It is overwritten by
        /// ProfileService.ApplyRun for every real run.
        /// </remarks>
        [PrototypeOnly(PendingDecisions.ProfileXp,
            "Level-1 requirement used as the empty-report default. " +
            "Request value: level requirement = 100 + 75 * (Level - 1).")]
        public int LevelRequirementAfter = 100;

        public bool LeveledUp
        {
            get { return LevelAfter > LevelBefore; }
        }

        public int LevelsGained
        {
            get { return Mathf.Max(0, LevelAfter - LevelBefore); }
        }

        public float ProgressAfter01
        {
            get
            {
                return LevelRequirementAfter > 0
                    ? Mathf.Clamp01(XpIntoLevelAfter / (float)LevelRequirementAfter)
                    : 0f;
            }
        }

        // -------------------------------------------------------------- content

        /// <summary>Result line 11. Empty is a legitimate, common outcome - see the screen's empty state.</summary>
        public readonly List<ProfileUnlockEntry> NewUnlocks = new List<ProfileUnlockEntry>(4);

        /// <summary>Result line 12.</summary>
        public readonly List<ProfileUnlockEntry> NewAchievements = new List<ProfileUnlockEntry>(4);

        /// <summary>True when the profile could not be written. The screen says so rather than lying.</summary>
        public bool SaveFailed;

        /// <summary>An empty report, for a results screen opened with no run behind it.</summary>
        public static RunRewardReport Empty()
        {
            return new RunRewardReport { HasRun = false };
        }
    }
}
