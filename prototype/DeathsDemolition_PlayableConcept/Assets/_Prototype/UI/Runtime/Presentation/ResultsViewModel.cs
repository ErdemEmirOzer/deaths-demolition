using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Achievements;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>One label / value line on a report screen.</summary>
    public struct UiStatLine
    {
        public string Label;
        public string Value;
        public UiColor Color;

        /// <summary>True for the line a section is about (drawn one type step larger).</summary>
        public bool Emphasis;

        public UiStatLine(string label, string value, UiColor color = UiColor.TextPrimary, bool emphasis = false)
        {
            Label = label;
            Value = value;
            Color = color;
            Emphasis = emphasis;
        }
    }

    /// <summary>
    /// Everything the Run Results screen draws, computed ONCE from the immutable
    /// <see cref="RunRewardReport"/> (WP-005).
    ///
    /// The screen used to read the report field by field while building. Moving the
    /// reading here gives two things: every rule about what is shown - which enemy
    /// tiers get a line, what the ending is called, what an empty reward list says - is
    /// provable in EditMode against a hand-built report; and the view is left with
    /// nothing to decide, so it cannot disagree with the profile that was already written.
    ///
    /// NOTHING HERE IS LIVE. The model takes the report, not <c>RunEvents.Current</c> or
    /// the director: by the time the Results scene exists the run has been reset and
    /// the director is gone, and the numbers must survive that.
    /// </summary>
    public sealed class ResultsViewModel
    {
        public const string Title = "RUN RESULTS";
        public const string ContinueLabel = "MAIN MENU";

        /// <summary>Hard cap on each reward list. The overflow is summarised, never scrolled.</summary>
        public const int MaxRewardEntries = 3;

        /// <summary>Longest build line the header can hold before it is clipped.</summary>
        public const int BuildMaxCharacters = 56;

        public bool HasRun;

        // ---- header
        public string Eyebrow = "NO RUN RECORDED";
        public string Loadout = string.Empty;

        // ---- run result (hero)
        public string ReachedRoundLabel = "REACHED ROUND";
        public string ReachedRound = "—";
        public string RoundsCompletedCaption = string.Empty;

        // ---- combat
        public readonly List<UiStatLine> Combat = new List<UiStatLine>(6);
        public string CombatNote = string.Empty;

        // ---- run economy (informational, no XP)
        public readonly List<UiStatLine> Economy = new List<UiStatLine>(4);

        // ---- experience
        public readonly List<UiStatLine> XpBreakdown = new List<UiStatLine>(3);
        public string TotalXp = "+0";
        public string LevelLine = "LEVEL 1";
        public bool LeveledUp;
        public int LevelsGained;

        /// <summary>12b (AD R1-D SHOULD-1, E26): "Next unlock: Hinawa at level 3 · 2 levels away", or empty when none is left.</summary>
        public string NextUnlockLine = string.Empty;
        public float ProgressAfter01;
        public string ProgressCaption = string.Empty;
        public string LevelUpChip = string.Empty;
        public string XpNote = string.Empty;

        // ---- rewards
        public readonly List<ProfileUnlockEntry> NewUnlocks = new List<ProfileUnlockEntry>(4);
        public readonly List<ProfileUnlockEntry> NewAchievements = new List<ProfileUnlockEntry>(4);
        public string UnlocksEmpty = "No new content this run.";
        public string AchievementsEmpty = "No new achievements this run.";

        /// <summary>True when something on the screen deserves the reveal sound.</summary>
        public bool Celebrate;

        // ---- footer
        /// <summary>True when the run is NOT in the profile (save failed, duplicate, no identity). Drawn in the warning colour.</summary>
        public bool SaveFailed;

        /// <summary>WP-009: false when the receipt says the profile was not changed by this run.</summary>
        public bool Applied = true;

        /// <summary>WP-022B (WP-023 data): chip text when this run set a new highest round, else empty.</summary>
        public string BestRoundChip = string.Empty;

        /// <summary>WP-022B: what ended the run, for the ending line; empty when unknown.</summary>
        public string KilledBy = string.Empty;

        /// <summary>
        /// WP-022B: achievement rarity by title (WP-023 <c>RunResultsData.NewAchievements</c>),
        /// drives the medallion ring. Missing = Common.
        /// </summary>
        public readonly Dictionary<string, AchievementRarity> AchievementRarities =
            new Dictionary<string, AchievementRarity>(4, StringComparer.Ordinal);

        /// <summary>Folds the WP-023 results record into the model. Null-safe; nothing recomputed.</summary>
        public void ApplyResultsData(RunResultsData data)
        {
            if (data == null || !data.HasRun)
            {
                return;
            }

            BestRoundChip = data.IsNewHighestRound ? "NEW BEST ROUND" : string.Empty;
            KilledBy = data.KilledBy ?? string.Empty;
            AchievementRarities.Clear();
            for (int i = 0; i < data.NewAchievements.Count; i++)
            {
                RunResultsAchievement a = data.NewAchievements[i];
                if (!string.IsNullOrEmpty(a.Title))
                {
                    AchievementRarities[a.Title] = a.Rarity;
                }
            }

            // WP-022D (WP-035): the unlock's type and id drive its glyph on the Results card.
            UnlockTypes.Clear();
            for (int i = 0; i < data.NewUnlocks.Count; i++)
            {
                RunResultsUnlock u = data.NewUnlocks[i];
                if (!string.IsNullOrEmpty(u.Title))
                {
                    UnlockTypes[u.Title] = new UnlockGlyph(u.Type, u.Id);
                }
            }
        }

        public readonly struct UnlockGlyph
        {
            public readonly UnlockContentType Type;
            public readonly string Id;

            public UnlockGlyph(UnlockContentType type, string id)
            {
                Type = type;
                Id = id ?? string.Empty;
            }
        }

        /// <summary>Unlock title → type/id (WP-035 <c>NewUnlocks</c>); ladder-only rows read as Weapon by their title.</summary>
        public readonly Dictionary<string, UnlockGlyph> UnlockTypes =
            new Dictionary<string, UnlockGlyph>(4, StringComparer.Ordinal);

        /// <summary>
        /// The glyph for an unlock row: a weapon's own icon, an upgrade branch's icon, a
        /// wreath for a title, the sakura medallion for a badge, the reticle ring for a
        /// HUD indicator; the rare wreath when the type is unknown (WP-005 report path).
        /// </summary>
        public Sprite UnlockSpriteFor(string title, out string typeLabel)
        {
            UnlockGlyph glyph;
            if (title == null || !UnlockTypes.TryGetValue(title, out glyph))
            {
                typeLabel = string.Empty;
                return UiArt.Medal(UiMedal.Rare);
            }

            switch (glyph.Type)
            {
                case UnlockContentType.Weapon:
                    typeLabel = "WEAPON";
                    return UiArt.Icon("Weapon." + glyph.Id) ?? UiArt.Medal(UiMedal.Rare);
                case UnlockContentType.Branch:
                    typeLabel = "BRANCH";
                    return UiArt.Icon("Upgrade." + glyph.Id) ?? UiArt.Medal(UiMedal.Rare);
                case UnlockContentType.Title:
                    typeLabel = "TITLE";
                    return UiArt.Icon("Unlock.Title") ?? UiArt.Medal(UiMedal.Rare);   // 12b (AD G1 SHOULD-3): its own G1 cell
                case UnlockContentType.Badge:
                    typeLabel = "BADGE";
                    return UiArt.Icon("Unlock.Badge") ?? UiArt.Medal(UiMedal.Rare);   // the sakura wreath (Epic) is the Epic medal's alone
                case UnlockContentType.Indicator:
                    typeLabel = "HUD";
                    return UiArt.Reticle(true) ?? UiArt.Medal(UiMedal.Rare);
                default:
                    typeLabel = "COSMETIC";
                    return UiArt.Medal(UiMedal.Rare);
            }
        }

        /// <summary>
        /// 12b: the killing blow in words for the Results H1 - "Zombie.&lt;Tier&gt;.&lt;Style&gt;" (EnemyAttackState's source ids)
        /// becomes the tier's name from the R1 pack ("STANDARD ZOMBIE"); any other id prints as it came, upper case, clipped.
        /// </summary>
        public static string KillerName(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return string.Empty;
            }

            const string Zombie = "Zombie.";
            if (sourceId.StartsWith(Zombie, StringComparison.Ordinal))
            {
                int end = sourceId.IndexOf('.', Zombie.Length);
                string tier = end > 0 ? sourceId.Substring(Zombie.Length, end - Zombie.Length) : sourceId.Substring(Zombie.Length);
                switch (tier)
                {
                    case "Standard": return UiStrings.Get("results.killer.standard", "STANDARD ZOMBIE");
                    case "Special": return UiStrings.Get("results.killer.special", "SPECIAL ZOMBIE");
                    case "Elite": return UiStrings.Get("results.killer.elite", "ELITE ZOMBIE");
                    case "Ultra": return UiStrings.Get("results.killer.ultra", "ULTRA ZOMBIE");
                }
            }

            return UiFactory.Clip(sourceId, 32).ToUpperInvariant();
        }

        /// <summary>The lowest weapon or branch unlock above <paramref name="level"/> (the profile's ladder), as the forward-look line.</summary>
        public static string NextUnlockFor(int level, IProfileLookup lookup)
        {
            if (lookup == null)
            {
                return string.Empty;
            }

            int best = int.MaxValue;
            string name = null;
            foreach (ClassicWeaponId id in (ClassicWeaponId[])Enum.GetValues(typeof(ClassicWeaponId)))
            {
                int at = lookup.UnlockLevelFor(id);
                if (at > level && at < best && at < ProfileService.NotInBuildUnlockLevel)
                {
                    best = at;
                    name = lookup.WeaponDisplayName(id);
                }
            }

            foreach (UpgradeBranch branch in (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch)))
            {
                int at = lookup.UnlockLevelFor(branch);
                if (at > level && at < best && at < ProfileService.NotInBuildUnlockLevel)
                {
                    best = at;
                    name = lookup.BranchDisplayName(branch);
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            int away = best - level;
            string line = away == 1
                ? UiStrings.Get("results.next_unlock_one", "Next unlock: {name} at level {level} · 1 level away")
                : UiStrings.Get("results.next_unlock", "Next unlock: {name} at level {level} · {away} levels away").Replace("{away}", away.ToString());
            return line.Replace("{name}", name).Replace("{level}", best.ToString());
        }

        public UiMedal MedalFor(string achievementTitle)
        {
            AchievementRarity rarity;
            if (achievementTitle != null && AchievementRarities.TryGetValue(achievementTitle, out rarity))
            {
                switch (rarity)
                {
                    case AchievementRarity.Epic: return UiMedal.Epic;
                    case AchievementRarity.Rare:
                    case AchievementRarity.Uncommon: return UiMedal.Rare;
                    default: return UiMedal.Common;
                }
            }

            return UiMedal.Epic;
        }

        public string FooterNote = string.Empty;

        // ---- WP-068 R1-D · E26 v3: the hero tiles, the report's weapon row, the reward reveal ----------------------

        /// <summary>The four hero tiles (Round · Kills · Gold · Time), as numbers the counters roll to.</summary>
        public int HeroRound;
        public int HeroKills;
        public int HeroGold;
        public int HeroSeconds;

        public ClassicWeaponId Weapon;
        public string WeaponName = string.Empty;

        /// <summary>The build as the HUD prints it ("P2 / FR1"), or "no upgrades".</summary>
        public string BuildLine = string.Empty;

        /// <summary>WP-077: Ash the run added to the PATH (0 unless the profile applied it).</summary>
        public int AshEarned;

        /// <summary>
        /// The XP bar's value before the run, 0..1 of the same level; -1 when the run crossed a level (the "before" belongs
        /// to another requirement, so the bar does not animate from it - E26: no fill animation without an honest start).
        /// </summary>
        public float ProgressBefore01 = -1f;

        // ---- empty state
        public string EmptyTitle = "There is no finished run to report.";

        public string EmptyBody =
            "This screen is filled in the moment a run ends. Start one from the main menu and it will " +
            "come back here by itself.";

        /// <summary>Builds the model with the profile's own weapon names.</summary>
        public static ResultsViewModel From(RunRewardReport report)
        {
            return From(report, ProfileService.WeaponDisplayName);
        }

        /// <summary>
        /// Builds the model. <paramref name="weaponName"/> is injected so the model can be
        /// tested without a loaded config.
        /// </summary>
        public static ResultsViewModel From(RunRewardReport report, Func<ClassicWeaponId, string> weaponName)
        {
            var model = new ResultsViewModel();

            if (report == null || !report.HasRun)
            {
                model.HasRun = false;
                model.FooterNote = "Rewards are granted automatically. There is nothing to claim.";
                return model;
            }

            model.HasRun = true;
            RunSummary summary = report.Summary;
            model.HeroRound = Mathf.Max(0, summary.ReachedRound);
            model.HeroKills = Mathf.Max(0, summary.TotalKills);
            model.HeroGold = Mathf.Max(0, summary.GoldEarned);
            model.HeroSeconds = Mathf.Max(0, Mathf.FloorToInt(summary.RunDuration));
            model.Weapon = summary.Weapon;
            model.WeaponName = weaponName != null ? weaponName(summary.Weapon) : summary.Weapon.ToString();
            model.BuildLine = string.IsNullOrEmpty(summary.BuildDescription) || summary.BuildDescription == "Stock"
                ? "no upgrades"
                : UiFactory.Clip(summary.BuildDescription, BuildMaxCharacters);
            model.AshEarned = Mathf.Max(0, report.AshEarned);
            model.ProgressBefore01 = report.LevelBefore == report.LevelAfter && report.LevelRequirementAfter > 0
                ? Mathf.Clamp01(report.XpIntoLevelBefore / (float)report.LevelRequirementAfter)
                : -1f;

            // ---- header ------------------------------------------------------
            model.Eyebrow = DescribeEnding(summary);
            model.Loadout = DescribeLoadout(summary, weaponName);

            // ---- run result --------------------------------------------------
            model.ReachedRound = summary.ReachedRound > 0 ? summary.ReachedRound.ToString() : "—";
            model.RoundsCompletedCaption = summary.CompletedRounds == 1
                ? "1 round completed"
                : summary.CompletedRounds + " rounds completed";

            // ---- combat ------------------------------------------------------
            // Standard is always listed - it is the only tier in this build. The other
            // tiers appear ONLY when the run actually recorded one: a line reading
            // "Elite Kills 0" would present content that does not exist yet as if it
            // had merely been missed (WP-005: never fabricate variant data).
            model.Combat.Add(new UiStatLine("Total Kills", summary.TotalKills.ToString(), UiColor.TextPrimary, true));
            model.Combat.Add(new UiStatLine("Standard", summary.StandardKills.ToString()));
            AddTierIfAny(model.Combat, "Special", summary.SpecialKills, UiColor.AccentSakura);
            AddTierIfAny(model.Combat, "Elite", summary.EliteKills, UiColor.AccentWisteria);
            AddTierIfAny(model.Combat, "Ultra", summary.UltraKills, UiColor.SilverLeaf); // WP-068: gold is currency/Special only; ceremony is ginpaku

            if (summary.ArmorPiecesBroken > 0)
            {
                model.Combat.Add(new UiStatLine("Armour Broken", summary.ArmorPiecesBroken.ToString(), UiColor.TextSecondary));
            }

            model.CombatNote = summary.CompletedRounds < summary.ReachedRound
                ? "Kills in your final round count. Round XP is granted only for completed rounds."
                : "Every kill counts towards kill XP and statistics.";

            // ---- run economy -------------------------------------------------
            // AD R1-D SHOULD (one rule for currency on Results): the tally is cream, like the GOLD hero tile.
            model.Economy.Add(new UiStatLine("Gold Collected", summary.GoldEarned.ToString(), UiColor.TextPrimary));
            model.Economy.Add(new UiStatLine("Gold Spent", summary.GoldSpent.ToString(), UiColor.TextSecondary));
            model.Economy.Add(new UiStatLine("Upgrades Bought", summary.UpgradesPurchased.ToString(), UiColor.TextSecondary));

            // ---- experience --------------------------------------------------
            model.XpBreakdown.Add(new UiStatLine(
                "Completed Rounds" + Count(summary.CompletedRounds),
                "+" + report.CompletedRoundXp));
            model.XpBreakdown.Add(new UiStatLine(
                "Kills" + Count(summary.TotalKills),
                "+" + report.KillXp));
            model.XpBreakdown.Add(new UiStatLine("Gold, Upgrades, Time", "+0", UiColor.TextMuted));

            model.TotalXp = "+" + report.TotalXpEarned;
            model.Applied = report.Applied;

            // The receipt is the only source (WP-009): when the transaction did not apply,
            // its "after" values equal its "before" values, so the lines below already
            // read as "unchanged". The model repeats nothing and recomputes nothing.
            model.LeveledUp = report.LeveledUp;
            model.LevelsGained = report.LevelsGained;
            model.ProgressAfter01 = report.ProgressAfter01;

            model.LevelLine = report.LeveledUp
                ? "LEVEL " + report.LevelBefore + "  →  LEVEL " + report.LevelAfter
                : "LEVEL " + report.LevelAfter;

            model.ProgressCaption = report.XpIntoLevelAfter + " / " + report.LevelRequirementAfter +
                                    " XP towards level " + (report.LevelAfter + 1);
            model.NextUnlockLine = NextUnlockFor(report.LevelAfter, ProfileServiceLookup.Instance);

            model.LevelUpChip = report.LeveledUp
                ? report.LevelsGained > 1 ? "LEVEL UP  x" + report.LevelsGained : "LEVEL UP"
                : string.Empty;

            model.XpNote = report.Applied
                ? "Profile progress unlocks content only. It never raises your starting damage, " +
                  "health or gold."
                : "This XP was not added to your profile. Profile progress unlocks content only; " +
                  "it never raises your starting damage, health or gold.";

            // ---- rewards -----------------------------------------------------
            model.NewUnlocks.AddRange(report.NewUnlocks);
            model.NewAchievements.AddRange(report.NewAchievements);

            model.Celebrate = report.Applied &&
                              (report.LeveledUp || model.NewUnlocks.Count > 0 || model.NewAchievements.Count > 0);

            // ---- footer ------------------------------------------------------
            model.SaveFailed = !report.Applied;
            model.FooterNote = DescribeOutcome(report);

            return model;
        }

        /// <summary>The footer sentence for each transaction outcome. English, one line.</summary>
        public static string DescribeOutcome(RunRewardReport report)
        {
            switch (report.Outcome)
            {
                case ProfileCommitOutcome.Applied:
                    return "Everything above is already saved to your profile. There is nothing to claim.";

                case ProfileCommitOutcome.Duplicate:
                    return "This run was already recorded in your profile. Nothing was added twice.";

                case ProfileCommitOutcome.MissingIdentity:
                    return "This run could not be recorded in your profile: it carried no run identity.";

                case ProfileCommitOutcome.InvalidIdentity:
                    // Same honest sentence as an invalid candidate: no new player-facing copy
                    // for a fail-closed internal refusal.
                    return "This run could not be recorded in your profile. Your saved profile is unchanged.";

                case ProfileCommitOutcome.InvalidCandidate:
                    return "This run could not be recorded in your profile. Your saved profile is unchanged.";

                default:
                    return "This run could not be written to your profile file. Your saved profile is unchanged.";
            }
        }

        /// <summary>Entries beyond <see cref="MaxRewardEntries"/>, as the one-line summary.</summary>
        public static string Overflow(int total)
        {
            int hidden = total - MaxRewardEntries;
            return hidden <= 0
                ? string.Empty
                : "and " + hidden + " more, already added to your profile";
        }

        private static void AddTierIfAny(List<UiStatLine> lines, string tier, int kills, UiColor color)
        {
            if (kills > 0)
            {
                lines.Add(new UiStatLine(tier, kills.ToString(), color));
            }
        }

        private static string Count(int n)
        {
            return "  ·  " + n;
        }

        public static string DescribeEnding(in RunSummary summary)
        {
            switch (summary.EndReason)
            {
                case RunEndReason.Aborted:
                    return "RUN ABANDONED IN ROUND " + summary.ReachedRound;

                case RunEndReason.Debug:
                    return "RUN ENDED IN ROUND " + summary.ReachedRound;

                default:
                    return "DEFEATED IN ROUND " + summary.ReachedRound;
            }
        }

        public static string DescribeLoadout(in RunSummary summary, Func<ClassicWeaponId, string> weaponName)
        {
            string weapon = weaponName != null ? weaponName(summary.Weapon) : summary.Weapon.ToString();
            string build = string.IsNullOrEmpty(summary.BuildDescription) || summary.BuildDescription == "Stock"
                ? "no upgrades"
                : UiFactory.Clip(summary.BuildDescription, BuildMaxCharacters);

            return weapon + "   ·   " + build + "   ·   " + FormatDuration(summary.RunDuration) + " played";
        }

        public static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
