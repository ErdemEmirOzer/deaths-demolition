using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Achievements
{
    /// <summary>
    /// The shipped achievement list, read once from
    /// <c>Resources/Achievements/achievement-catalog.json</c> (WP-023, ADR-023-1).
    ///
    /// The file is the authority: a definition that is missing an id, names an unknown
    /// condition, or repeats an id is dropped with one warning and the rest still load
    /// (a bad line must not take the whole system down). No file → an empty catalog,
    /// no error: the profile transaction simply earns nothing. The legacy
    /// <c>ProgressionConfigSection.Achievements</c> list (four WP-005 entries) is no
    /// longer read; its four ids are carried in the JSON so profiles that already
    /// earned them keep matching.
    /// </summary>
    public static class AchievementCatalog
    {
        public const string ResourcePath = "Achievements/achievement-catalog";

        [Serializable]
        private sealed class DefinitionDto
        {
            public string Id;
            public string DisplayName;
            public string Description;
            public string Condition;
            public int Threshold = 1;
            public string TargetTier;
            public string Scope;
            public string Rarity;
            public string RequiresEffect;
            public string Feat;
        }

        [Serializable]
        private sealed class CatalogDto
        {
            public int Version = 1;
            public List<DefinitionDto> Definitions = new List<DefinitionDto>();
        }

        private static List<AchievementDefinition> s_all;
        private static IReadOnlyList<AchievementDefinition> s_definitions;
        private static IReadOnlyList<AchievementSettings> s_settings;
        private static int s_revision = -1;
        private static bool s_overridden;

        /// <summary>WP-077 B4: every usable definition, tree-gated ones included (validation, tests).</summary>
        public static IReadOnlyList<AchievementDefinition> All
        {
            get
            {
                EnsureLoaded();
                return s_all;
            }
        }

        /// <summary>Every usable definition, in file order.</summary>
        public static IReadOnlyList<AchievementDefinition> Definitions
        {
            get
            {
                EnsureLoaded();
                return s_definitions;
            }
        }

        /// <summary>The same list in the WP-005 presentation shape (Achievements pane).</summary>
        public static IReadOnlyList<AchievementSettings> Settings
        {
            get
            {
                EnsureLoaded();
                return s_settings;
            }
        }

        public static AchievementDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            IReadOnlyList<AchievementDefinition> list = Definitions;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.Ordinal))
                {
                    return list[i];
                }
            }

            return null;
        }

        /// <summary>Parses catalog JSON. Public so a test can prove the validation rules without Resources.</summary>
        public static List<AchievementDefinition> Parse(string json, out int dropped)
        {
            dropped = 0;
            var result = new List<AchievementDefinition>();
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
                Debug.LogWarning("[Achievements] Catalog JSON could not be parsed; no achievements loaded. " + e.Message);
                return result;
            }

            if (dto == null || dto.Definitions == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dto.Definitions.Count; i++)
            {
                DefinitionDto d = dto.Definitions[i];
                string why = null;
                AchievementDefinition def = Convert(d, seen, out why);
                if (def == null)
                {
                    dropped++;
                    Debug.LogWarning("[Achievements] Catalog entry " + i + " dropped: " + why);
                    continue;
                }

                seen.Add(def.Id);
                result.Add(def);
            }

            return result;
        }

        private static AchievementDefinition Convert(DefinitionDto d, HashSet<string> seen, out string why)
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

            AchievementCondition condition;
            if (string.IsNullOrEmpty(d.Condition) || !Enum.TryParse(d.Condition, false, out condition) ||
                !Enum.IsDefined(typeof(AchievementCondition), condition))
            {
                why = "unknown condition '" + d.Condition + "' for '" + d.Id + "'";
                return null;
            }

            EnemyTier tier = EnemyTier.Standard;
            if (!string.IsNullOrEmpty(d.TargetTier) && (!Enum.TryParse(d.TargetTier, false, out tier) || !Enum.IsDefined(typeof(EnemyTier), tier)))
            {
                why = "unknown tier '" + d.TargetTier + "' for '" + d.Id + "'";
                return null;
            }

            AchievementScope scope = AchievementScope.Lifetime;
            if (!string.IsNullOrEmpty(d.Scope) && (!Enum.TryParse(d.Scope, false, out scope) || !Enum.IsDefined(typeof(AchievementScope), scope)))
            {
                why = "unknown scope '" + d.Scope + "' for '" + d.Id + "'";
                return null;
            }

            if (scope == AchievementScope.SingleRun &&
                (condition == AchievementCondition.RunsPlayed || condition == AchievementCondition.ProfileLevel))
            {
                why = "condition " + condition + " has no single-run meaning ('" + d.Id + "')";
                return null;
            }

            if (scope == AchievementScope.Lifetime &&
                (condition == AchievementCondition.GoldEarned || condition == AchievementCondition.UpgradesPurchased))
            {
                why = "condition " + condition + " is single-run only: gold and purchases are not persisted ('" + d.Id + "')";
                return null;
            }

            // WP-077 B4: the run-tree counters live only in one run; the weapon records only in the profile.
            bool runTree = condition == AchievementCondition.TreeCapstones || condition == AchievementCondition.TreeKeystones ||
                           condition == AchievementCondition.HearthStacks || condition == AchievementCondition.TreeFullLanes ||
                           condition == AchievementCondition.SpecialLinkII;
            if (runTree && scope != AchievementScope.SingleRun)
            {
                why = "condition " + condition + " is single-run only ('" + d.Id + "')";
                return null;
            }

            if ((condition == AchievementCondition.CapstoneWeapons || condition == AchievementCondition.WeaponMastery) && scope != AchievementScope.Lifetime)
            {
                why = "condition " + condition + " is lifetime only ('" + d.Id + "')";
                return null;
            }

            // WP-077 A10: a Feat rule names its feat; the arsenal count lives only in the profile.
            FeatKind feat = FeatKind.RaijuChainMax;
            if (condition == AchievementCondition.Feat)
            {
                if (string.IsNullOrEmpty(d.Feat) || !Enum.TryParse(d.Feat, false, out feat) || !Enum.IsDefined(typeof(FeatKind), feat))
                {
                    why = "unknown feat '" + d.Feat + "' for '" + d.Id + "'";
                    return null;
                }

                if (feat == FeatKind.ArsenalRound10 && scope != AchievementScope.Lifetime)
                {
                    why = "feat ArsenalRound10 is lifetime only ('" + d.Id + "')";
                    return null;
                }
            }

            AchievementRarity rarity = AchievementRarity.Common;
            if (!string.IsNullOrEmpty(d.Rarity) && (!Enum.TryParse(d.Rarity, false, out rarity) || !Enum.IsDefined(typeof(AchievementRarity), rarity)))
            {
                why = "unknown rarity '" + d.Rarity + "' for '" + d.Id + "'";
                return null;
            }

            return new AchievementDefinition
            {
                Id = d.Id,
                DisplayName = string.IsNullOrEmpty(d.DisplayName) ? d.Id : d.DisplayName,
                Description = d.Description ?? string.Empty,
                Condition = condition,
                Threshold = Math.Max(1, d.Threshold),
                TargetTier = tier,
                Scope = scope,
                Rarity = rarity,
                RequiresEffect = d.RequiresEffect ?? string.Empty,
                Feat = feat
            };
        }

        private static void EnsureLoaded()
        {
            if (s_all != null)
            {
                if (s_definitions == null || s_revision != DD.Prototype.Rounds.RunTreeEffects.Revision) Publish(s_all);
                return;
            }

            var loaded = new List<AchievementDefinition>();
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[Achievements] No catalog at Resources/" + ResourcePath + "; no achievements can be earned.");
            }
            else
            {
                int dropped;
                loaded = Parse(asset.text, out dropped);
            }

            Publish(loaded);
        }

        /// <summary>Keeps the full list and publishes the AVAILABLE view (a tree-gated rule appears once its effect is live).</summary>
        private static void Publish(List<AchievementDefinition> list)
        {
            s_all = list;
            s_revision = DD.Prototype.Rounds.RunTreeEffects.Revision;
            var available = new List<AchievementDefinition>(list.Count);
            var settings = new List<AchievementSettings>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsAvailable) continue;
                available.Add(list[i]);
                settings.Add(list[i].ToSettings());
            }

            s_definitions = available.AsReadOnly();
            s_settings = settings.AsReadOnly();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (!s_overridden)
            {
                s_all = null;
                s_definitions = null;
                s_settings = null;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Editor / development seam: replace the catalog (null restores the Resources file).</summary>
        public static class TestSeam
        {
            public static void Override(IReadOnlyList<AchievementDefinition> definitions)
            {
                if (definitions == null)
                {
                    s_overridden = false;
                    s_all = null;
                    s_definitions = null;
                    s_settings = null;
                    return;
                }

                s_overridden = true;
                Publish(new List<AchievementDefinition>(definitions));
            }
        }
#endif
    }
}
