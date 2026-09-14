using System;
using System.Collections.Generic;

namespace DD.Prototype.Profile
{
    /// <summary>
    /// The whole persistent profile, exactly as it is written to disk.
    ///
    /// SHAPE RULES - these are what make a corrupt save survivable
    /// -----------------------------------------------------------
    ///   * Public fields only, all of them JsonUtility-serialisable. No properties, no
    ///     dictionaries, no nested polymorphism.
    ///   * Every field has a valid default, so a JSON document that is missing half its
    ///     keys still deserialises into a usable profile.
    ///   * Ids are stored as STRINGS, not as enum ordinals. Renumbering
    ///     <c>ClassicWeaponId</c> or <c>UpgradeBranch</c> later must not silently unlock
    ///     the wrong content in an existing save. An unrecognised string is RETAINED
    ///     (WP-009 quarantine policy: it is the player's data and a newer build may
    ///     define it) but it can never open anything, because every access gate
    ///     compares against the ids this build defines.
    ///   * Nothing in here can grant a combat advantage. The request is explicit that
    ///     persistent progress gives no starting damage, health or gold - so the file
    ///     stores levels, counts and unlock ids, and no tunable number at all.
    ///
    /// The run-scoped data named in the request (current gold, current build, classic
    /// weapon upgrades, current round, run kills) is deliberately ABSENT: it lives in
    /// the run systems and dies with the run.
    ///
    /// SCHEMA HISTORY (see <see cref="ProfileMigration"/>)
    ///   1  WP-005: level, statistics, achievements, unlock lists, LastSavedUtc.
    ///   2  WP-009: + AppliedCompletionIds (anti-replay metadata), + UnattributedKills
    ///      (lossless legacy kill invariant). Version 1 files are read without loss.
    /// </summary>
    [Serializable]
    public sealed class ProfileData
    {
        /// <summary>Bumped when the shape changes. 0 or negative means "not a real save".</summary>
        public const int CurrentVersion = 2;

        /// <summary>The first version that ever existed on disk (WP-005).</summary>
        public const int OldestReadableVersion = 1;

        /// <summary>
        /// How many completion ids are remembered for replay protection. The single-run
        /// model needs one; the ring absorbs a stale duplicate that arrives after a
        /// later run has already been committed. It is anti-replay metadata, not a run
        /// history: no run data is kept, only opaque ids, and nothing shows them.
        /// </summary>
        public const int AppliedCompletionIdCapacity = 8;

        /// <summary>
        /// Current for an object created in memory. A DOCUMENT that lacks the key is
        /// refused by <see cref="ProfileMigration.Parse"/> (it checks the text, because
        /// JsonUtility cannot report a missing key), so a foreign JSON object can never
        /// pass for a current profile; a loaded document is stamped by the migration.
        /// </summary>
        public int SaveVersion = CurrentVersion;

        // ------------------------------------------------------------------ level

        public int ProfileLevel = 1;

        /// <summary>XP banked towards the NEXT level. Below the requirement except when the level-up cap held it back.</summary>
        public int XpIntoLevel;

        /// <summary>Lifetime XP. Informational; the level is authoritative.</summary>
        public int TotalXpEarned;

        // ------------------------------------------------------------- statistics

        public int HighestRound;
        public int RunsPlayed;
        public int RoundsCompleted;

        public int TotalKills;
        public int StandardKills;
        public int SpecialKills;
        public int EliteKills;
        public int UltraKills;

        /// <summary>
        /// WP-009: kills that a legacy or damaged file counted in <see cref="TotalKills"/>
        /// but attributed to no tier. Recorded, never invented: the invariant is
        /// <c>TotalKills == Standard + Special + Elite + Ultra + UnattributedKills</c>, and
        /// a total that exceeds the tier sum lands here instead of being spread over
        /// tiers at random. A profile written by this build keeps it at 0.
        /// </summary>
        public int UnattributedKills;

        public int ArmorPiecesBroken;

        /// <summary>Best single-run XP. The statistics pane's one "personal best" line.</summary>
        public int BestRunXp;

        // ----------------------------------------------------------------- content

        /// <summary>Achievement ids, matching <c>AchievementSettings.Id</c>.</summary>
        public List<string> Achievements = new List<string>();

        /// <summary>Names of <c>ClassicWeaponId</c> values already granted.</summary>
        public List<string> UnlockedWeapons = new List<string>();

        /// <summary>Names of <c>UpgradeBranch</c> values already granted.</summary>
        public List<string> UnlockedBranches = new List<string>();

        /// <summary>
        /// WP-009: opaque completion ids of the runs most recently folded into this
        /// profile (newest last, at most <see cref="AppliedCompletionIdCapacity"/>). A
        /// summary whose id is listed here is a replay and is refused. Not a history:
        /// shown nowhere, carries no run data.
        /// </summary>
        public List<string> AppliedCompletionIds = new List<string>();

        /// <summary>Round-trip marker, shown nowhere. Never a transaction identity.</summary>
        public string LastSavedUtc = string.Empty;

        /// <summary>
        /// Forces the object into a state the rest of the game can trust.
        ///
        /// Called after EVERY load, including a successful one: a file that parses is
        /// not the same thing as a file that makes sense. A hand-edited save with
        /// level 0, negative kills or a duplicated achievement id must not be able to
        /// produce a divide-by-zero in the level curve or a doubled results line.
        ///
        /// Repairs are LOSSLESS in the sense the request demands: a total that exceeds
        /// the tier sum is preserved as <see cref="UnattributedKills"/>, never spread
        /// over tiers; unknown ids are kept. Only impossible values (negatives) are
        /// clamped, and duplicates / blanks are dropped.
        /// </summary>
        public void Sanitize()
        {
            if (SaveVersion <= 0)
            {
                SaveVersion = CurrentVersion;
            }

            ProfileLevel = Math.Max(1, ProfileLevel);
            XpIntoLevel = Math.Max(0, XpIntoLevel);
            TotalXpEarned = Math.Max(0, TotalXpEarned);

            HighestRound = Math.Max(0, HighestRound);
            RunsPlayed = Math.Max(0, RunsPlayed);
            RoundsCompleted = Math.Max(0, RoundsCompleted);

            StandardKills = Math.Max(0, StandardKills);
            SpecialKills = Math.Max(0, SpecialKills);
            EliteKills = Math.Max(0, EliteKills);
            UltraKills = Math.Max(0, UltraKills);
            UnattributedKills = Math.Max(0, UnattributedKills);
            TotalKills = Math.Max(0, TotalKills);

            // Kill invariant: TotalKills == tier sum + unattributed. A larger total is
            // kept by growing the unattributed bucket; a smaller one is corrected upward
            // (a tier count is a witnessed kill and cannot be un-counted).
            int attributed = ProfileProgression.SaturatingSum(StandardKills, SpecialKills, EliteKills, UltraKills);
            int explained = ProfileProgression.SaturatingAdd(attributed, UnattributedKills);
            if (TotalKills > explained)
            {
                UnattributedKills = ProfileProgression.SaturatingAdd(UnattributedKills, TotalKills - explained);
            }

            TotalKills = ProfileProgression.SaturatingAdd(attributed, UnattributedKills);

            ArmorPiecesBroken = Math.Max(0, ArmorPiecesBroken);
            BestRunXp = Math.Max(0, BestRunXp);

            Achievements = Clean(Achievements);
            UnlockedWeapons = Clean(UnlockedWeapons);
            UnlockedBranches = Clean(UnlockedBranches);
            AppliedCompletionIds = Clean(AppliedCompletionIds);
            while (AppliedCompletionIds.Count > AppliedCompletionIdCapacity)
            {
                AppliedCompletionIds.RemoveAt(0);
            }

            if (LastSavedUtc == null)
            {
                LastSavedUtc = string.Empty;
            }
        }

        /// <summary>
        /// True when the object satisfies every invariant <see cref="Sanitize"/> enforces.
        /// The transaction validates its candidate with this before writing it, and the
        /// store validates what it read back; a value that fails here is a bug, not data.
        /// </summary>
        public bool IsValid()
        {
            if (SaveVersion != CurrentVersion || ProfileLevel < 1 || XpIntoLevel < 0 || TotalXpEarned < 0 ||
                HighestRound < 0 || RunsPlayed < 0 || RoundsCompleted < 0 ||
                StandardKills < 0 || SpecialKills < 0 || EliteKills < 0 || UltraKills < 0 ||
                UnattributedKills < 0 || TotalKills < 0 || ArmorPiecesBroken < 0 || BestRunXp < 0)
            {
                return false;
            }

            int attributed = ProfileProgression.SaturatingSum(StandardKills, SpecialKills, EliteKills, UltraKills);
            if (TotalKills != ProfileProgression.SaturatingAdd(attributed, UnattributedKills))
            {
                return false;
            }

            return IsClean(Achievements) && IsClean(UnlockedWeapons) && IsClean(UnlockedBranches) &&
                   IsClean(AppliedCompletionIds) &&
                   AppliedCompletionIds.Count <= AppliedCompletionIdCapacity &&
                   LastSavedUtc != null;
        }

        /// <summary>
        /// A deep copy. The transaction works on a copy and publishes it only after the
        /// disk write succeeded, so a failed save leaves the live profile untouched.
        /// </summary>
        public ProfileData Clone()
        {
            return new ProfileData
            {
                SaveVersion = SaveVersion,
                ProfileLevel = ProfileLevel,
                XpIntoLevel = XpIntoLevel,
                TotalXpEarned = TotalXpEarned,
                HighestRound = HighestRound,
                RunsPlayed = RunsPlayed,
                RoundsCompleted = RoundsCompleted,
                TotalKills = TotalKills,
                StandardKills = StandardKills,
                SpecialKills = SpecialKills,
                EliteKills = EliteKills,
                UltraKills = UltraKills,
                UnattributedKills = UnattributedKills,
                ArmorPiecesBroken = ArmorPiecesBroken,
                BestRunXp = BestRunXp,
                Achievements = new List<string>(Achievements ?? new List<string>()),
                UnlockedWeapons = new List<string>(UnlockedWeapons ?? new List<string>()),
                UnlockedBranches = new List<string>(UnlockedBranches ?? new List<string>()),
                AppliedCompletionIds = new List<string>(AppliedCompletionIds ?? new List<string>()),
                LastSavedUtc = LastSavedUtc ?? string.Empty
            };
        }

        /// <summary>Drops nulls, blanks and duplicates while preserving order.</summary>
        private static List<string> Clean(List<string> source)
        {
            var result = new List<string>(source != null ? source.Count : 0);
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                string value = source[i];
                if (string.IsNullOrEmpty(value) || result.Contains(value))
                {
                    continue;
                }

                result.Add(value);
            }

            return result;
        }

        private static bool IsClean(List<string> list)
        {
            if (list == null)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrEmpty(list[i]))
                {
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(list[j], list[i], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool Has(List<string> list, string id)
        {
            return list != null && !string.IsNullOrEmpty(id) && list.Contains(id);
        }

        public bool Grant(List<string> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id) || list.Contains(id))
            {
                return false;
            }

            list.Add(id);
            return true;
        }

        /// <summary>True when the run with this completion id has already been folded in.</summary>
        public bool HasAppliedCompletion(string completionId)
        {
            return Has(AppliedCompletionIds, completionId);
        }

        /// <summary>Records a completion id, evicting the oldest beyond the capacity.</summary>
        public void RecordAppliedCompletion(string completionId)
        {
            if (!Grant(AppliedCompletionIds, completionId))
            {
                return;
            }

            while (AppliedCompletionIds.Count > AppliedCompletionIdCapacity)
            {
                AppliedCompletionIds.RemoveAt(0);
            }
        }

        public int KillsForTierName(string tierName)
        {
            switch (tierName)
            {
                case "Standard": return StandardKills;
                case "Special": return SpecialKills;
                case "Elite": return EliteKills;
                case "Ultra": return UltraKills;
                default: return 0;
            }
        }
    }
}
