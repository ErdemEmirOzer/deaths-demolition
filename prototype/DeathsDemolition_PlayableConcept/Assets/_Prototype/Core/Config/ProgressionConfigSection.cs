using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>PROTOTYPE-ONLY achievement definition.</summary>
    [Serializable]
    public sealed class AchievementSettings
    {
        [PrototypeOnly(PendingDecisions.None, "Stable id used in the local profile save.")]
        public string Id = "first_blood";

        [PrototypeOnly(PendingDecisions.None, "Title shown on the Run Results screen.")]
        public string DisplayName = "First Blood";

        [PrototypeOnly(PendingDecisions.None, "One-line English description.", true)]
        public string Description = "Kill your first enemy.";

        [PrototypeOnly(PendingDecisions.None, "How the achievement is evaluated. Prototype achievement set.")]
        public AchievementTrigger Trigger = AchievementTrigger.FirstKill;

        [PrototypeOnly(PendingDecisions.None, "Threshold for ReachRound / KillTier triggers.")]
        public int Threshold = 1;

        [PrototypeOnly(PendingDecisions.None, "Tier the KillTier trigger looks for.")]
        public EnemyTier TargetTier = EnemyTier.Standard;
    }

    /// <summary>PROTOTYPE-ONLY cosmetic skin. Visual only, zero gameplay effect.</summary>
    [Serializable]
    public sealed class SkinSettings
    {
        [PrototypeOnly(PendingDecisions.None, "Identifier of a cosmetic skin, not a tunable value.")]
        public SkinId Id = SkinId.IvoryCrimson;

        [PrototypeOnly(PendingDecisions.None, "Request names the three skins: Ivory/Crimson, Indigo/Black, Moss/Cream.")]
        public string DisplayName = "Ivory / Crimson";

        [PrototypeOnly(PendingDecisions.None, "Exact hex is a prototype art choice.", true)]
        public Color PrimaryColor = new Color(0.93f, 0.90f, 0.83f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Exact hex is a prototype art choice.", true)]
        public Color SecondaryColor = new Color(0.60f, 0.11f, 0.13f, 1f);
    }

    /// <summary>
    /// PROTOTYPE-ONLY profile XP, level curve, unlock ladder and achievements.
    /// Persistent progress never grants starting damage, health or gold advantage.
    /// </summary>
    [Serializable]
    public sealed class ProgressionConfigSection
    {
        [Header("XP")]
        [PrototypeOnly(PendingDecisions.ProfileXp, "Request value: 1 XP per kill")]
        public int XpPerKill = 1;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Request value: completed round XP = 15 + 5 * round")]
        public int RoundXpBase = 15;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Request value: completed round XP = 15 + 5 * round")]
        public int RoundXpPerRound = 5;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Request value: level requirement = 100 + 75 * (Level - 1)")]
        public int LevelRequirementBase = 100;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Request value: level requirement = 100 + 75 * (Level - 1)")]
        public int LevelRequirementPerLevel = 75;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Request: gold earned or spent gives no XP.")]
        public bool GoldGrantsXp = false;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Request: playtime gives no XP.")]
        public bool PlaytimeGrantsXp = false;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Request: an unfinished round grants no round XP.")]
        public bool IncompleteRoundGrantsRoundXp = false;

        [Header("Skins - cosmetic only, all three unlocked from the start")]
        public List<SkinSettings> Skins = new List<SkinSettings>
        {
            new SkinSettings
            {
                Id = SkinId.IvoryCrimson, DisplayName = "Ivory / Crimson",
                PrimaryColor = new Color(0.93f, 0.90f, 0.83f, 1f),
                SecondaryColor = new Color(0.60f, 0.11f, 0.13f, 1f)
            },
            new SkinSettings
            {
                Id = SkinId.IndigoBlack, DisplayName = "Indigo / Black",
                PrimaryColor = new Color(0.16f, 0.19f, 0.34f, 1f),
                SecondaryColor = new Color(0.07f, 0.07f, 0.09f, 1f)
            },
            new SkinSettings
            {
                Id = SkinId.MossCream, DisplayName = "Moss / Cream",
                PrimaryColor = new Color(0.33f, 0.40f, 0.25f, 1f),
                SecondaryColor = new Color(0.90f, 0.87f, 0.76f, 1f)
            }
        };

        [PrototypeOnly(PendingDecisions.None, "Default selection after an application restart.")]
        public SkinId DefaultSkin = SkinId.IvoryCrimson;

        [PrototypeOnly(PendingDecisions.None, "Default selection after an application restart.")]
        public ClassicWeaponId DefaultWeapon = ClassicWeaponId.AssaultRifle;

        [Header("Achievements")]
        public List<AchievementSettings> Achievements = new List<AchievementSettings>
        {
            new AchievementSettings
            {
                Id = "first_blood", DisplayName = "First Blood",
                Description = "Defeat your first enemy.",
                Trigger = AchievementTrigger.FirstKill, Threshold = 1
            },
            new AchievementSettings
            {
                Id = "armor_breaker", DisplayName = "Armor Breaker",
                Description = "Break an armour piece off an enemy.",
                Trigger = AchievementTrigger.ArmorPieceBroken, Threshold = 1
            },
            new AchievementSettings
            {
                Id = "reach_round_5", DisplayName = "Reach Round 5",
                Description = "Survive until round 5.",
                Trigger = AchievementTrigger.ReachRound, Threshold = 5
            },
            new AchievementSettings
            {
                Id = "defeat_an_ultra", DisplayName = "Defeat an Ultra",
                Description = "Bring down an Executioner.",
                Trigger = AchievementTrigger.KillTier, Threshold = 1, TargetTier = EnemyTier.Ultra
            }
        };

        /// <summary>XP needed to advance from <paramref name="level"/> to the next one.</summary>
        public int LevelRequirement(int level)
        {
            int l = Mathf.Max(1, level);
            return LevelRequirementBase + LevelRequirementPerLevel * (l - 1);
        }

        /// <summary>XP awarded for finishing a round.</summary>
        public int RoundCompletionXp(int round)
        {
            return RoundXpBase + RoundXpPerRound * Mathf.Max(1, round);
        }
    }
}
