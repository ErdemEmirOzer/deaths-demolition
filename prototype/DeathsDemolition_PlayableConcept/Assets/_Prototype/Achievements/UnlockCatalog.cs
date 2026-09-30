using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using UnityEngine;

namespace DD.Prototype.Achievements
{
    /// <summary>What kind of content an unlock entry opens.</summary>
    public enum UnlockContentType
    {
        /// <summary>A cosmetic id (title, badge). Stored in <c>ProfileData.UnlockedContent</c>. Skins are NOT here: they are always open (DEC-018).</summary>
        Cosmetic = 0,

        /// <summary>A HUD / Results indicator variant id. Stored in <c>ProfileData.UnlockedContent</c>.</summary>
        Indicator = 1,

        /// <summary>A classic weapon (<c>ClassicWeaponId</c> name). Stored in <c>ProfileData.UnlockedWeapons</c>. Empty until the WP-024 weapon list.</summary>
        Weapon = 2,

        /// <summary>An upgrade branch (<c>UpgradeBranch</c> name). Stored in <c>ProfileData.UnlockedBranches</c>. Empty until the WP-024 weapon list.</summary>
        Branch = 3,

        /// <summary>WP-035: a profile title (cosmetic text shown next to the player's name). Stored in <c>ProfileData.UnlockedContent</c>; selectable via <c>ProfileService.SelectTitle</c>.</summary>
        Title = 4,

        /// <summary>WP-035: a profile badge (cosmetic icon id). Stored in <c>ProfileData.UnlockedContent</c>; selectable via <c>ProfileService.SelectBadge</c>.</summary>
        Badge = 5
    }

    /// <summary>What opens an unlock entry.</summary>
    public enum UnlockCondition
    {
        /// <summary><see cref="UnlockDefinition.Key"/> is an achievement id the profile has earned.</summary>
        Achievement = 0,

        /// <summary><see cref="UnlockDefinition.Key"/> is a profile level (integer).</summary>
        Level = 1,

        /// <summary><see cref="UnlockDefinition.Key"/> is a highest-round threshold (integer).</summary>
        Round = 2
    }

    /// <summary>One catalog entry. Data, not code (Resources/Achievements/unlock-catalog.json).</summary>
    [Serializable]
    public sealed class UnlockDefinition
    {
        /// <summary>Content id. For Weapon / Branch types it must be the enum NAME; stored as a string, never an ordinal.</summary>
        public string Id = string.Empty;

        public UnlockContentType Type = UnlockContentType.Cosmetic;

        public string DisplayName = string.Empty;

        /// <summary>One line for the Results "Newly Unlocked Content" list.</summary>
        public string Detail = string.Empty;

        public UnlockCondition Condition = UnlockCondition.Achievement;

        /// <summary>Achievement id, or the level / round number as text.</summary>
        public string Key = string.Empty;

        /// <summary>The numeric key for Level / Round conditions (0 when not numeric).</summary>
        public int NumericKey;
    }

    /// <summary>
    /// The unlockable-content catalog (WP-023, DEC-038): content opens AUTOMATICALLY in
    /// the run-end transaction when its condition holds, is listed on Run Results, and
    /// needs no Claim. Grants are additive (an earned entry is never taken back) and
    /// exactly-once by construction: they ride the profile transaction. Level-ladder
    /// weapon / branch unlocks (WP-009 entitlement, DEC-069..075) are a separate,
    /// unchanged mechanism; this catalog can ADD weapon / branch grants by name once the
    /// weapon list exists, and ships none until then.
    ///
    /// WP-035: the weapon roster (WP-034, <c>WeaponRoster</c>) is REFERENCED, not copied:
    /// every enabled profile-level weapon appears as a derived Weapon entry whose level is
    /// read from the roster's own <c>ClassicWeaponSettings.UnlockProfileLevel</c> at load, so
    /// the catalog can never disagree with the ladder (<c>ProfileService.UnlockLevelFor</c>)
    /// or change a roster value. The JSON file carries only cosmetic / indicator content.
    /// </summary>
    public static class UnlockCatalog
    {
        public const string ResourcePath = "Achievements/unlock-catalog";

        [Serializable]
        private sealed class EntryDto
        {
            public string Id;
            public string Type;
            public string DisplayName;
            public string Detail;
            public string Condition;
            public string Key;
        }

        [Serializable]
        private sealed class CatalogDto
        {
            public int Version = 1;
            public List<EntryDto> Entries = new List<EntryDto>();
        }

        private static IReadOnlyList<UnlockDefinition> s_entries;
        private static bool s_overridden;

        public static IReadOnlyList<UnlockDefinition> Entries
        {
            get
            {
                EnsureLoaded();
                return s_entries;
            }
        }

        public static UnlockDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            IReadOnlyList<UnlockDefinition> list = Entries;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.Ordinal))
                {
                    return list[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Grants every catalog entry the candidate now qualifies for and does not hold.
        /// Runs AFTER achievements and the level ladder, so an entry keyed on an
        /// achievement earned in this very run opens in the same transaction.
        /// </summary>
        public static void Grant(ProfileData candidate, IReadOnlyList<UnlockDefinition> entries, List<UnlockDefinition> newlyUnlocked)
        {
            if (candidate == null || entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                UnlockDefinition entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Id) || !IsConditionMet(candidate, entry))
                {
                    continue;
                }

                List<string> list = ListFor(candidate, entry.Type);
                if (list == null)
                {
                    continue;
                }

                if (candidate.Grant(list, entry.Id) && newlyUnlocked != null)
                {
                    newlyUnlocked.Add(entry);
                }
            }
        }

        public static bool IsConditionMet(ProfileData profile, UnlockDefinition entry)
        {
            if (profile == null || entry == null)
            {
                return false;
            }

            switch (entry.Condition)
            {
                case UnlockCondition.Achievement:
                    return profile.Has(profile.Achievements, entry.Key);
                case UnlockCondition.Level:
                    return entry.NumericKey >= 1 && profile.ProfileLevel >= entry.NumericKey;
                case UnlockCondition.Round:
                    return entry.NumericKey >= 1 && profile.HighestRound >= entry.NumericKey;
                default:
                    return false;
            }
        }

        /// <summary>The entitlement gate for catalog content: the EARNED LIST decides, never the condition.</summary>
        public static bool IsUnlocked(ProfileData profile, string id)
        {
            if (profile == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            UnlockDefinition entry = Find(id);
            List<string> list = ListFor(profile, entry != null ? entry.Type : UnlockContentType.Cosmetic);
            return list != null && profile.Has(list, id);
        }

        private static List<string> ListFor(ProfileData profile, UnlockContentType type)
        {
            switch (type)
            {
                case UnlockContentType.Cosmetic:
                case UnlockContentType.Indicator:
                case UnlockContentType.Title:
                case UnlockContentType.Badge:
                    return profile.UnlockedContent;
                case UnlockContentType.Weapon:
                    return profile.UnlockedWeapons;
                case UnlockContentType.Branch:
                    return profile.UnlockedBranches;
                default:
                    return null;
            }
        }

        /// <summary>Parses catalog JSON; invalid entries are dropped with a warning, the rest load.</summary>
        public static List<UnlockDefinition> Parse(string json, out int dropped)
        {
            dropped = 0;
            var result = new List<UnlockDefinition>();
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            CatalogDto dto;
            try
            {
                dto = JsonUtility.FromJson<CatalogDto>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unlocks] Catalog JSON could not be parsed; no catalog content loaded. " + e.Message);
                return result;
            }

            if (dto == null || dto.Entries == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dto.Entries.Count; i++)
            {
                string why;
                UnlockDefinition entry = Convert(dto.Entries[i], seen, out why);
                if (entry == null)
                {
                    dropped++;
                    Debug.LogWarning("[Unlocks] Catalog entry " + i + " dropped: " + why);
                    continue;
                }

                seen.Add(entry.Id);
                result.Add(entry);
            }

            return result;
        }

        private static UnlockDefinition Convert(EntryDto d, HashSet<string> seen, out string why)
        {
            why = null;
            if (d == null || string.IsNullOrEmpty(d.Id))
            {
                why = "missing id";
                return null;
            }

            UnlockContentType type;
            if (string.IsNullOrEmpty(d.Type) || !Enum.TryParse(d.Type, false, out type) || !Enum.IsDefined(typeof(UnlockContentType), type))
            {
                why = "unknown type '" + d.Type + "' for '" + d.Id + "'";
                return null;
            }

            // Weapon / branch ids must be names this build defines; an unknown name could
            // never open anything and would only pollute the earned list.
            // WP-077 v5: weapon ids are WP-069 keys ("smg_1"); an M3 name ("AutoSmg") is read and normalised.
            if (type == UnlockContentType.Weapon)
            {
                string key = DD.Prototype.Core.Weapons.WeaponIdMigration.FromLegacyName(d.Id);
                if (key == null || !DD.Prototype.Core.Weapons.WeaponIdMigration.TryGetRuntimeId(key, out _))
                {
                    why = "'" + d.Id + "' is not a classic weapon key";
                    return null;
                }

                d.Id = key;
            }

            if (type == UnlockContentType.Branch && !Enum.IsDefined(typeof(UpgradeBranch), d.Id))
            {
                why = "'" + d.Id + "' is not an UpgradeBranch name";
                return null;
            }

            // R1-B P5 (C-00 30.09): the duplicate check runs on the NORMALISED id, so an M3 name ("AutoSmg") and its
            // WP-069 key ("smg_1") are one entry, not two.
            if (seen.Contains(d.Id))
            {
                why = "duplicate id '" + d.Id + "'";
                return null;
            }

            UnlockCondition condition;
            if (string.IsNullOrEmpty(d.Condition) || !Enum.TryParse(d.Condition, false, out condition) || !Enum.IsDefined(typeof(UnlockCondition), condition))
            {
                why = "unknown condition '" + d.Condition + "' for '" + d.Id + "'";
                return null;
            }

            int numeric = 0;
            if (condition == UnlockCondition.Achievement)
            {
                if (string.IsNullOrEmpty(d.Key))
                {
                    why = "achievement key missing for '" + d.Id + "'";
                    return null;
                }
            }
            else if (!int.TryParse(d.Key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numeric) || numeric < 1)
            {
                why = "numeric key '" + d.Key + "' invalid for '" + d.Id + "'";
                return null;
            }

            return new UnlockDefinition
            {
                Id = d.Id,
                Type = type,
                DisplayName = string.IsNullOrEmpty(d.DisplayName) ? d.Id : d.DisplayName,
                Detail = d.Detail ?? string.Empty,
                Condition = condition,
                Key = d.Key ?? string.Empty,
                NumericKey = numeric
            };
        }

        private static void EnsureLoaded()
        {
            if (s_entries != null)
            {
                return;
            }

            var loaded = new List<UnlockDefinition>();
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[Unlocks] No catalog at Resources/" + ResourcePath + "; no catalog content can open.");
            }
            else
            {
                int dropped;
                loaded = Parse(asset.text, out dropped);
            }

            AppendRosterWeapons(loaded);
            s_entries = loaded.AsReadOnly();
        }

        /// <summary>
        /// WP-035: one derived Weapon entry per enabled, runtime-backed, profile-level roster
        /// weapon. Level and display name come from the roster / config at load; an id the
        /// JSON already names is not duplicated. Default-kind weapons are open from the first
        /// run (DEC-037/039) and need no entry.
        /// </summary>
        public static void AppendRosterWeapons(List<UnlockDefinition> into)
        {
            if (into == null)
            {
                return;
            }

            IReadOnlyList<DD.Prototype.Core.Weapons.ClassicWeaponDefinition> roster;
            try
            {
                roster = DD.Prototype.Core.Weapons.WeaponRoster.Enabled;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unlocks] Weapon roster unavailable; no weapon entries derived. " + e.Message);
                return;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            for (int i = 0; i < roster.Count; i++)
            {
                DD.Prototype.Core.Weapons.ClassicWeaponDefinition d = roster[i];
                if (d == null || !d.HasRuntimeId || d.UnlockKind != DD.Prototype.Core.Weapons.WeaponUnlockKind.ProfileLevel)
                {
                    continue;
                }

                string id = DD.Prototype.Profile.ProfileProgression.NameOf(d.Id) ?? d.Id.ToString();   // WP-077 v5: saved weapon key
                bool present = false;
                for (int j = 0; j < into.Count; j++)
                {
                    if (into[j] != null && string.Equals(into[j].Id, id, StringComparison.Ordinal))
                    {
                        present = true;
                        break;
                    }
                }

                if (present)
                {
                    continue;
                }

                int level = Math.Max(1, d.UnlockLevel(config));
                ClassicWeaponSettings settings = d.Settings(config);
                string name = settings != null && !string.IsNullOrEmpty(settings.DisplayName) ? settings.DisplayName : id;
                into.Add(new UnlockDefinition
                {
                    Id = id,
                    Type = UnlockContentType.Weapon,
                    DisplayName = name,
                    Detail = "Classic weapon - opens at profile level " + level.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (weapon roster)",
                    Condition = UnlockCondition.Level,
                    Key = level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NumericKey = level
                });
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (!s_overridden)
            {
                s_entries = null;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Editor / development seam: replace the catalog (null restores the Resources file).</summary>
        public static class TestSeam
        {
            public static void Override(IReadOnlyList<UnlockDefinition> entries)
            {
                if (entries == null)
                {
                    s_overridden = false;
                    s_entries = null;
                    return;
                }

                s_overridden = true;
                s_entries = new List<UnlockDefinition>(entries).AsReadOnly();
            }
        }
#endif
    }
}
