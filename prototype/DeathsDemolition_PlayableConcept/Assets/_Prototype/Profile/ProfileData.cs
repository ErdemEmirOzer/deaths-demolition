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
    ///   2  WP-009: + AppliedCompletionIds (a ring of the last 8 completion ids),
    ///      + UnattributedKills (lossless legacy kill invariant).
    ///   3  WP-009H: the ring is RETIRED - it only protected the last eight runs, which
    ///      contradicted the exactly-once contract (a ninth run let the first summary
    ///      apply again). Replay authority is now O(1): ProfileLineageId (fixed for the
    ///      life of the profile) + LastAppliedCompletionOrdinal (a monotonic high-water
    ///      mark). Legacy ids are dropped at migration; legacy raw GUIDs are refused by
    ///      the token parser. Version 1 and 2 files are read without progression loss.
    ///   4  WP-023: + SpecialWeaponPickups, FlawlessRounds (lifetime feat statistics the
    ///      achievement rules read; DEC-007 statistics, never modifiers) + UnlockedContent
    ///      (catalog cosmetic / indicator ids, DEC-038). Gold and purchases stay OUT of the
    ///      file (REQ-018: run economy dies with the run; gold achievements are single-run).
    ///      A v3 file gains the new fields at their zero / empty defaults and keeps its
    ///      lineage and ordinal untouched, so nothing already recorded can replay.
    ///      WP-035 adds SelectedTitle / SelectedBadge / AchievementsSeen / ContentSeen to
    ///      the SAME version: absent keys read as defaults, an older v4 build ignores them,
    ///      and none of them changes what the profile has earned.
    /// </summary>
    [Serializable]
    public sealed class ProfileData
    {
        /// <summary>Bumped when the shape changes. 0 or negative means "not a real save".</summary>
        public const int CurrentVersion = 5;   // WP-077: v5 = meta PATH (Ash, meta nodes, mastery) + WP-069 weapon keys

        /// <summary>The first version that ever existed on disk (WP-005).</summary>
        public const int OldestReadableVersion = 1;

        /// <summary>Length of <see cref="ProfileLineageId"/>: a 128-bit value as lower-case hex.</summary>
        public const int LineageIdLength = 32;

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

        // -------------------------------------------------------- statistics (v4)

        /// <summary>WP-023: special weapons picked up over the profile's life.</summary>
        public int SpecialWeaponPickups;

        /// <summary>WP-023: rounds completed without an enemy attack landing on the player.</summary>
        public int FlawlessRounds;

        // ----------------------------------------------------------------- content

        /// <summary>Achievement ids, matching <c>AchievementSettings.Id</c>.</summary>
        public List<string> Achievements = new List<string>();

        /// <summary>WP-069 weapon keys already granted ("ar_1", "smg_1" …; v4 and older stored the ClassicWeaponId names — migrated in v5).</summary>
        public List<string> UnlockedWeapons = new List<string>();

        /// <summary>Names of <c>UpgradeBranch</c> values already granted.</summary>
        public List<string> UnlockedBranches = new List<string>();

        /// <summary>WP-023: catalog content ids (cosmetic / indicator) already opened, matching <c>UnlockDefinition.Id</c>.</summary>
        public List<string> UnlockedContent = new List<string>();

        // ------------------------------------------------------- cosmetics (WP-035)

        /// <summary>WP-035: the selected profile title (an <c>UnlockedContent</c> id of type Title), or empty for none. Skins are NOT here (DEC-039: session-only).</summary>
        public string SelectedTitle = string.Empty;

        /// <summary>WP-035: the selected profile badge (an <c>UnlockedContent</c> id of type Badge), or empty for none.</summary>
        public string SelectedBadge = string.Empty;

        /// <summary>WP-035: how many earned achievements the player has seen in the Achievements pane ("new" = earned beyond this).</summary>
        public int AchievementsSeen;

        /// <summary>WP-035: how many opened content ids the player has seen ("new" = opened beyond this).</summary>
        public int ContentSeen;

        // ------------------------------------------------------- meta PATH (v5, WP-077)

        /// <summary>WP-077 Ash ("Kül") available to spend on the meta PATH. Always <see cref="AshEarnedTotal"/> − <see cref="AshSpentTotal"/>.</summary>
        public int AshBalance;

        /// <summary>WP-077: Ash earned over the profile's life (runs, first-time milestones, achievements, the v5 back-grant).</summary>
        public int AshEarnedTotal;

        /// <summary>WP-077: Ash spent on meta nodes (net of respec refunds).</summary>
        public int AshSpentTotal;

        /// <summary>WP-077: meta PATH node ids owned.</summary>
        public List<string> MetaNodes = new List<string>();

        /// <summary>WP-077: meta respecs used (the first is free).</summary>
        public int MetaRespecCount;

        /// <summary>WP-077: pinned meta node id (NEXT strip), or empty.</summary>
        public string MetaPinnedNode = string.Empty;

        /// <summary>WP-077: how many meta nodes the player has seen on the PATH pane ("new" = owned/affordable beyond this).</summary>
        public int MetaSeen;

        /// <summary>WP-077: one-time Ash milestones already paid ("r10:ar_1", "capstone", "link2").</summary>
        public List<string> AshMilestones = new List<string>();

        /// <summary>WP-077: kills per weapon key (mastery M1–M5 gates).</summary>
        public List<WeaponMasteryEntry> WeaponMastery = new List<WeaponMasteryEntry>();

        // ---------------------------------------------------- replay authority (v3)

        /// <summary>
        /// WP-009H: random 128-bit identity minted once per profile life (fresh profile,
        /// reset, or the v1/v2 → v3 migration) and never changed afterwards. A completion
        /// token names the lineage it was issued for; one from another lineage (an older
        /// profile, a reset profile, a foreign file) is refused. Shown nowhere.
        /// </summary>
        public string ProfileLineageId = string.Empty;

        /// <summary>
        /// WP-009H: ordinal of the last transaction successfully folded into this profile
        /// (0 = none). Monotonic and saturating. Together with the lineage this is the
        /// whole anti-replay state - constant size, no list, no history: a completion is
        /// either at most this ordinal (already applied → refused) or exactly the next
        /// one (the single live run). Written in the same transaction as the rewards.
        /// </summary>
        public long LastAppliedCompletionOrdinal;

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
            SpecialWeaponPickups = Math.Max(0, SpecialWeaponPickups);
            FlawlessRounds = Math.Max(0, FlawlessRounds);

            Achievements = Clean(Achievements);
            UnlockedWeapons = Clean(UnlockedWeapons);
            UnlockedBranches = Clean(UnlockedBranches);
            UnlockedContent = Clean(UnlockedContent);

            // WP-035: a selection must name opened content; anything else is cleared, never
            // invented. Seen counters cannot exceed what exists.
            if (SelectedTitle == null || !UnlockedContent.Contains(SelectedTitle))
            {
                SelectedTitle = string.Empty;
            }

            if (SelectedBadge == null || !UnlockedContent.Contains(SelectedBadge))
            {
                SelectedBadge = string.Empty;
            }

            AchievementsSeen = Math.Min(Math.Max(0, AchievementsSeen), Achievements.Count);
            ContentSeen = Math.Min(Math.Max(0, ContentSeen), UnlockedContent.Count);

            // WP-077 v5: weapon keys are WP-069 slot keys; an M3 name found in memory or in an old
            // file is mapped (lossless, WeaponIdMigration), an unknown string is kept as data.
            for (int i = 0; i < UnlockedWeapons.Count; i++)
            {
                string key = DD.Prototype.Core.Weapons.WeaponIdMigration.FromLegacyName(UnlockedWeapons[i]);
                if (key != null) UnlockedWeapons[i] = key;
            }

            UnlockedWeapons = Clean(UnlockedWeapons);

            AshEarnedTotal = Math.Max(0, AshEarnedTotal);
            AshSpentTotal = Math.Min(Math.Max(0, AshSpentTotal), AshEarnedTotal);
            AshBalance = AshEarnedTotal - AshSpentTotal;
            MetaNodes = Clean(MetaNodes);
            MetaRespecCount = Math.Max(0, MetaRespecCount);
            MetaPinnedNode = MetaPinnedNode ?? string.Empty;
            MetaSeen = Math.Min(Math.Max(0, MetaSeen), MetaNodes.Count);
            AshMilestones = Clean(AshMilestones);
            WeaponMastery = CleanMastery(WeaponMastery);

            // Replay authority is never "repaired" here: a missing or malformed lineage is
            // an invalid document (ProfileMigration reports it), not something to invent.
            if (ProfileLineageId == null)
            {
                ProfileLineageId = string.Empty;
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
                UnattributedKills < 0 || TotalKills < 0 || ArmorPiecesBroken < 0 || BestRunXp < 0 ||
                SpecialWeaponPickups < 0 || FlawlessRounds < 0)
            {
                return false;
            }

            int attributed = ProfileProgression.SaturatingSum(StandardKills, SpecialKills, EliteKills, UltraKills);
            if (TotalKills != ProfileProgression.SaturatingAdd(attributed, UnattributedKills))
            {
                return false;
            }

            if (SelectedTitle == null || SelectedBadge == null ||
                (SelectedTitle.Length > 0 && !Has(UnlockedContent, SelectedTitle)) ||
                (SelectedBadge.Length > 0 && !Has(UnlockedContent, SelectedBadge)) ||
                AchievementsSeen < 0 || AchievementsSeen > (Achievements != null ? Achievements.Count : 0) ||
                ContentSeen < 0 || ContentSeen > (UnlockedContent != null ? UnlockedContent.Count : 0))
            {
                return false;
            }

            if (AshEarnedTotal < 0 || AshSpentTotal < 0 || AshSpentTotal > AshEarnedTotal || AshBalance != AshEarnedTotal - AshSpentTotal ||
                MetaRespecCount < 0 || MetaPinnedNode == null || MetaSeen < 0 || MetaSeen > (MetaNodes != null ? MetaNodes.Count : 0) ||
                !IsClean(MetaNodes) || !IsClean(AshMilestones) || !IsCleanMastery(WeaponMastery))
            {
                return false;
            }

            return IsClean(Achievements) && IsClean(UnlockedWeapons) && IsClean(UnlockedBranches) && IsClean(UnlockedContent) &&
                   IsLineageId(ProfileLineageId) && LastAppliedCompletionOrdinal >= 0 &&
                   LastSavedUtc != null;
        }

        /// <summary>True for exactly 32 lower-case hex characters.</summary>
        public static bool IsLineageId(string value)
        {
            if (value == null || value.Length != LineageIdLength)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Mints a new lineage id (random 128-bit, lower-case hex).</summary>
        public static string NewLineageId()
        {
            return Guid.NewGuid().ToString("N");
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
                SpecialWeaponPickups = SpecialWeaponPickups,
                FlawlessRounds = FlawlessRounds,
                Achievements = new List<string>(Achievements ?? new List<string>()),
                UnlockedWeapons = new List<string>(UnlockedWeapons ?? new List<string>()),
                UnlockedBranches = new List<string>(UnlockedBranches ?? new List<string>()),
                UnlockedContent = new List<string>(UnlockedContent ?? new List<string>()),
                SelectedTitle = SelectedTitle ?? string.Empty,
                SelectedBadge = SelectedBadge ?? string.Empty,
                AchievementsSeen = AchievementsSeen,
                ContentSeen = ContentSeen,
                AshBalance = AshBalance,
                AshEarnedTotal = AshEarnedTotal,
                AshSpentTotal = AshSpentTotal,
                MetaNodes = new List<string>(MetaNodes ?? new List<string>()),
                MetaRespecCount = MetaRespecCount,
                MetaPinnedNode = MetaPinnedNode ?? string.Empty,
                MetaSeen = MetaSeen,
                AshMilestones = new List<string>(AshMilestones ?? new List<string>()),
                WeaponMastery = CopyMastery(WeaponMastery),
                ProfileLineageId = ProfileLineageId ?? string.Empty,
                LastAppliedCompletionOrdinal = LastAppliedCompletionOrdinal,
                LastSavedUtc = LastSavedUtc ?? string.Empty
            };
        }

        /// <summary>WP-077: kills recorded for a weapon key (0 when none).</summary>
        public int MasteryKills(string weaponKey)
        {
            if (WeaponMastery == null || string.IsNullOrEmpty(weaponKey)) return 0;
            for (int i = 0; i < WeaponMastery.Count; i++)
            {
                if (WeaponMastery[i] != null && WeaponMastery[i].Weapon == weaponKey) return WeaponMastery[i].Kills;
            }

            return 0;
        }

        /// <summary>WP-077 B4: weapons with at least one capstone run (capstones_5_weapons).</summary>
        public int CapstoneWeaponCount
        {
            get
            {
                int n = 0;
                if (WeaponMastery != null) for (int i = 0; i < WeaponMastery.Count; i++) if (WeaponMastery[i] != null && WeaponMastery[i].Capstones > 0) n++;
                return n;
            }
        }

        /// <summary>WP-077 B4: the most kills any one weapon has (mastery_5_any reads its level).</summary>
        public int HighestMasteryKills
        {
            get
            {
                int best = 0;
                if (WeaponMastery != null) for (int i = 0; i < WeaponMastery.Count; i++) if (WeaponMastery[i] != null && WeaponMastery[i].Kills > best) best = WeaponMastery[i].Kills;
                return best;
            }
        }

        /// <summary>WP-077 B4: records a run that ended owning the weapon's capstone.</summary>
        public void AddWeaponCapstone(string weaponKey)
        {
            if (string.IsNullOrEmpty(weaponKey)) return;
            AddMasteryKills(weaponKey, 0);
            for (int i = 0; i < WeaponMastery.Count; i++)
            {
                if (WeaponMastery[i] != null && WeaponMastery[i].Weapon == weaponKey)
                {
                    WeaponMastery[i].Capstones = ProfileProgression.SaturatingAdd(WeaponMastery[i].Capstones, 1);
                    return;
                }
            }
        }

        /// <summary>WP-077: adds kills to a weapon key's mastery (saturating).</summary>
        public void AddMasteryKills(string weaponKey, int kills)
        {
            if (string.IsNullOrEmpty(weaponKey) || kills < 0) return;
            if (WeaponMastery == null) WeaponMastery = new List<WeaponMasteryEntry>();
            for (int i = 0; i < WeaponMastery.Count; i++)
            {
                if (WeaponMastery[i] != null && WeaponMastery[i].Weapon == weaponKey)
                {
                    WeaponMastery[i].Kills = ProfileProgression.SaturatingAdd(WeaponMastery[i].Kills, kills);
                    return;
                }
            }

            WeaponMastery.Add(new WeaponMasteryEntry { Weapon = weaponKey, Kills = kills });
        }

        private static List<WeaponMasteryEntry> CopyMastery(List<WeaponMasteryEntry> source)
        {
            var result = new List<WeaponMasteryEntry>(source != null ? source.Count : 0);
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null) result.Add(new WeaponMasteryEntry { Weapon = source[i].Weapon, Kills = source[i].Kills, Capstones = source[i].Capstones });
            }

            return result;
        }

        private static List<WeaponMasteryEntry> CleanMastery(List<WeaponMasteryEntry> source)
        {
            var result = new List<WeaponMasteryEntry>(source != null ? source.Count : 0);
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                WeaponMasteryEntry e = source[i];
                if (e == null || string.IsNullOrEmpty(e.Weapon)) continue;
                string key = DD.Prototype.Core.Weapons.WeaponIdMigration.FromLegacyName(e.Weapon) ?? e.Weapon;
                int kills = Math.Max(0, e.Kills);
                int capstones = Math.Max(0, e.Capstones);
                bool merged = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (result[j].Weapon == key)
                    {
                        result[j].Kills = ProfileProgression.SaturatingAdd(result[j].Kills, kills);
                        result[j].Capstones = ProfileProgression.SaturatingAdd(result[j].Capstones, capstones);
                        merged = true;
                        break;
                    }
                }

                if (!merged) result.Add(new WeaponMasteryEntry { Weapon = key, Kills = kills, Capstones = capstones });
            }

            return result;
        }

        private static bool IsCleanMastery(List<WeaponMasteryEntry> list)
        {
            if (list == null) return false;
            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || string.IsNullOrEmpty(list[i].Weapon) || list[i].Kills < 0 || list[i].Capstones < 0 || !seen.Add(list[i].Weapon)) return false;
            }

            return true;
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

    /// <summary>WP-077 v5: kills recorded with one weapon (mastery gates on the meta PATH).</summary>
    [Serializable]
    public sealed class WeaponMasteryEntry
    {
        public string Weapon = string.Empty;
        public int Kills;
        /// <summary>WP-077 B4: runs with this weapon that ended owning its capstone (capstones_5_weapons).</summary>
        public int Capstones;
    }
}
