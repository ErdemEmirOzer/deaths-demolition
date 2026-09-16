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
        Branch = 3
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

            if (seen.Contains(d.Id))
            {
                why = "duplicate id '" + d.Id + "'";
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
            if (type == UnlockContentType.Weapon && !Enum.IsDefined(typeof(ClassicWeaponId), d.Id))
            {
                why = "'" + d.Id + "' is not a ClassicWeaponId name";
                return null;
            }

            if (type == UnlockContentType.Branch && !Enum.IsDefined(typeof(UpgradeBranch), d.Id))
            {
                why = "'" + d.Id + "' is not an UpgradeBranch name";
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

            s_entries = loaded.AsReadOnly();
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
