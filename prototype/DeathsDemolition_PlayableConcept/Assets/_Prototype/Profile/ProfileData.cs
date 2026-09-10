using System;
using System.Collections.Generic;
using UnityEngine;

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
    ///     the wrong content in an existing save; an unrecognised string is simply
    ///     dropped by <see cref="Sanitize"/>.
    ///   * Nothing in here can grant a combat advantage. The request is explicit that
    ///     persistent progress gives no starting damage, health or gold - so the file
    ///     stores levels, counts and unlock ids, and no tunable number at all.
    ///
    /// The run-scoped data named in the request (current gold, current build, classic
    /// weapon upgrades, current round, run kills) is deliberately ABSENT: it lives in
    /// the run systems and dies with the run.
    /// </summary>
    [Serializable]
    public sealed class ProfileData
    {
        /// <summary>Bumped when the shape changes. 0 or negative means "not a real save".</summary>
        public const int CurrentVersion = 1;

        public int SaveVersion = CurrentVersion;

        // ------------------------------------------------------------------ level

        public int ProfileLevel = 1;

        /// <summary>XP banked towards the NEXT level. Always below the current requirement.</summary>
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

        /// <summary>Round-trip marker, shown nowhere. Useful when a save is inspected by hand.</summary>
        public string LastSavedUtc = string.Empty;

        /// <summary>
        /// Forces the object into a state the rest of the game can trust.
        ///
        /// Called after EVERY load, including a successful one: a file that parses is
        /// not the same thing as a file that makes sense. A hand-edited save with
        /// level 0, negative kills or a duplicated achievement id must not be able to
        /// produce a divide-by-zero in the level curve or a doubled results line.
        /// </summary>
        public void Sanitize()
        {
            SaveVersion = CurrentVersion;

            ProfileLevel = Mathf.Max(1, ProfileLevel);
            XpIntoLevel = Mathf.Max(0, XpIntoLevel);
            TotalXpEarned = Mathf.Max(0, TotalXpEarned);

            HighestRound = Mathf.Max(0, HighestRound);
            RunsPlayed = Mathf.Max(0, RunsPlayed);
            RoundsCompleted = Mathf.Max(0, RoundsCompleted);

            StandardKills = Mathf.Max(0, StandardKills);
            SpecialKills = Mathf.Max(0, SpecialKills);
            EliteKills = Mathf.Max(0, EliteKills);
            UltraKills = Mathf.Max(0, UltraKills);

            int tierSum = StandardKills + SpecialKills + EliteKills + UltraKills;
            TotalKills = Mathf.Max(Mathf.Max(0, TotalKills), tierSum);

            ArmorPiecesBroken = Mathf.Max(0, ArmorPiecesBroken);
            BestRunXp = Mathf.Max(0, BestRunXp);

            Achievements = Clean(Achievements);
            UnlockedWeapons = Clean(UnlockedWeapons);
            UnlockedBranches = Clean(UnlockedBranches);

            if (LastSavedUtc == null)
            {
                LastSavedUtc = string.Empty;
            }
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
