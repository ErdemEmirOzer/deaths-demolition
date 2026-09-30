using System;
using System.Collections.Generic;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// The profile facts a presentation model needs that are NOT in the save file:
    /// the level curve, unlock ladder, display names and achievement definitions.
    /// All of them come from PrototypeConfig via <see cref="ProfileService"/> in the
    /// game; a test hands in its own so the models can be proven without a config.
    /// </summary>
    public interface IProfileLookup
    {
        int LevelRequirement(int level);
        string WeaponDisplayName(ClassicWeaponId id);
        int UnlockLevelFor(ClassicWeaponId id);
        string BranchDisplayName(UpgradeBranch branch);
        int UnlockLevelFor(UpgradeBranch branch);
        IReadOnlyList<AchievementSettings> Achievements { get; }
        bool RecoveredFromCorruptSave { get; }

        /// <summary>WP-009: the one sentence about persistence to show, or empty (see ProfileService.PersistenceNotice).</summary>
        string PersistenceNotice { get; }
    }

    /// <summary>The game's lookup: everything answered by <see cref="ProfileService"/>.</summary>
    public sealed class ProfileServiceLookup : IProfileLookup
    {
        public static readonly ProfileServiceLookup Instance = new ProfileServiceLookup();

        public int LevelRequirement(int level) => ProfileService.LevelRequirement(level);
        public string WeaponDisplayName(ClassicWeaponId id) => ProfileService.WeaponDisplayName(id);
        public int UnlockLevelFor(ClassicWeaponId id) => ProfileService.UnlockLevelFor(id);
        public string BranchDisplayName(UpgradeBranch branch) => ProfileService.BranchDisplayName(branch);
        public int UnlockLevelFor(UpgradeBranch branch) => ProfileService.UnlockLevelFor(branch);
        public IReadOnlyList<AchievementSettings> Achievements => ProfileService.AllAchievements;
        public bool RecoveredFromCorruptSave => ProfileService.RecoveredFromCorruptSave;
        public string PersistenceNotice => ProfileService.PersistenceNotice;
    }

    /// <summary>One row of the unlocked-content list.</summary>
    public struct UiUnlockLine
    {
        public string Title;
        public bool Unlocked;

        /// <summary>PROTOTYPE-ONLY ladder value, shown on a locked row. Not a product decision.</summary>
        public int RequiredLevel;

        public UiUnlockLine(string title, bool unlocked, int requiredLevel)
        {
            Title = title;
            Unlocked = unlocked;
            RequiredLevel = requiredLevel;
        }
    }

    /// <summary>One achievement row.</summary>
    public struct UiAchievementLine
    {
        public string Id;
        public string Title;
        public string Description;
        public bool Earned;

        // WP-022D (WP-035 contract): progress and rarity, read from the profile source.
        public AchievementRarity Rarity;
        public bool IsNew;
        public long Current;
        public int Threshold;
        public float Progress01;

        public UiAchievementLine(string id, string title, string description, bool earned)
            : this(id, title, description, earned, AchievementRarity.Common, false, earned ? 1 : 0, 1)
        {
        }

        public UiAchievementLine(string id, string title, string description, bool earned,
            AchievementRarity rarity, bool isNew, long current, int threshold)
        {
            Id = id;
            Title = title;
            Description = description;
            Earned = earned;
            Rarity = rarity;
            IsNew = isNew;
            Threshold = Math.Max(1, threshold);
            Current = earned ? Threshold : Math.Min(Math.Max(0, current), Threshold);
            Progress01 = earned ? 1f : (float)Current / Threshold;
        }

        /// <summary>"7 / 10" for an unearned counter; empty when earned (the chip says it).</summary>
        public string ProgressCaption => Earned ? string.Empty : Current + " / " + Threshold;
    }

    /// <summary>One selectable cosmetic (title / badge) as the Profile pane shows it (WP-035).</summary>
    public struct UiCosmeticLine
    {
        public string Id;
        public CosmeticKind Kind;
        public string Title;
        public string Detail;
        public bool Unlocked;
        public bool IsNew;
        public bool Selected;

        public UiCosmeticLine(CosmeticEntry entry)
        {
            Id = entry.Id;
            Kind = entry.Kind;
            Title = ProfileContentCopy.CosmeticTitle(entry);
            Detail = ProfileContentCopy.UnlockDetail(entry.Id, entry.Detail);
            Unlocked = entry.Unlocked;
            IsNew = entry.IsNew;
            Selected = entry.Selected;
        }
    }

    /// <summary>
    /// The Profile pane's title / badge picker (WP-022D). Read from
    /// <see cref="ICosmeticsSource"/>; the pane never decides what is unlocked.
    /// </summary>
    public sealed class CosmeticsViewModel
    {
        public static string TitlesLabel => UiStrings.Get("profile.titles_label", "PROFILE TITLE");
        public static string BadgesLabel => UiStrings.Get("profile.badges_label", "PROFILE BADGE");
        public static string NoneOption => UiStrings.Get("profile.none", "None");
        public static string PickNote => UiStrings.Get("profile.pick_note", "Locked titles and badges open through achievements and unlocks.");

        public readonly List<UiCosmeticLine> Titles = new List<UiCosmeticLine>(6);
        public readonly List<UiCosmeticLine> Badges = new List<UiCosmeticLine>(6);
        public int NewCount;
        public string SelectedTitle = string.Empty;
        public string SelectedBadge = string.Empty;

        public bool IsEmpty => Titles.Count == 0 && Badges.Count == 0;

        /// <summary>
        /// WP-022E name plate: "Blooded  ·  Ghost" from the selected title and badge (names
        /// without their "Title: " / "Badge: " data prefix); empty when nothing is selected.
        /// </summary>
        public static string NamePlate(ICosmeticsSource source)
        {
            if (source == null)
            {
                return string.Empty;
            }

            string title = SelectedName(source.Titles);
            string badge = SelectedName(source.Badges);
            if (string.IsNullOrEmpty(title))
            {
                return badge;
            }

            return string.IsNullOrEmpty(badge) ? title : title + "  ·  " + badge;
        }

        private static string SelectedName(IReadOnlyList<CosmeticEntry> entries)
        {
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                if (entries[i].Selected && entries[i].Unlocked)
                {
                    string name = entries[i].Title ?? string.Empty;
                    int colon = name.IndexOf(": ", StringComparison.Ordinal);
                    return colon > 0 && colon < 12 ? name.Substring(colon + 2) : name;
                }
            }

            return string.Empty;
        }

        public static CosmeticsViewModel From(ICosmeticsSource source)
        {
            var model = new CosmeticsViewModel();
            if (source == null)
            {
                return model;
            }

            IReadOnlyList<CosmeticEntry> titles = source.Titles;
            for (int i = 0; titles != null && i < titles.Count; i++)
            {
                model.Titles.Add(new UiCosmeticLine(titles[i]));
            }

            IReadOnlyList<CosmeticEntry> badges = source.Badges;
            for (int i = 0; badges != null && i < badges.Count; i++)
            {
                model.Badges.Add(new UiCosmeticLine(badges[i]));
            }

            model.NewCount = source.NewCount;
            model.SelectedTitle = source.SelectedTitle ?? string.Empty;
            model.SelectedBadge = source.SelectedBadge ?? string.Empty;
            return model;
        }
    }

    /// <summary>
    /// The Profile pane (WP-005): level, progress, the three headline numbers, the
    /// unlock ladder and the achievement tally. Read from a <see cref="ProfileData"/>
    /// once; never from a live run.
    ///
    /// The level is presented as EXPERIENCE, never as power: the model carries no
    /// damage, health or gold figure and the rule line says so in words.
    /// </summary>
    public sealed class ProfileViewModel
    {
        public static string Title => UiStrings.Get("menu.tab.profile", "PROFILE");

        public static string RuleNote =>
            UiStrings.Get("profile.rule", "Persistent progress unlocks content only. It never grants starting damage, health or gold.");

        public string LevelText = UiStrings.Format("profile.level", "value", "1", "LEVEL {value}");
        public float Progress01;
        public string ProgressCaption = string.Empty;

        public readonly List<UiStatLine> Headline = new List<UiStatLine>(3);
        public readonly List<UiUnlockLine> Unlocks = new List<UiUnlockLine>(6);

        public string AchievementSummary = string.Empty;

        /// <summary>Set when the save on disk was unreadable and this profile started fresh.</summary>
        public string Notice = string.Empty;

        public static ProfileViewModel From(ProfileData profile, IProfileLookup lookup)
        {
            var model = new ProfileViewModel();
            if (profile == null || lookup == null)
            {
                return model;
            }

            int requirement = Mathf.Max(1, lookup.LevelRequirement(profile.ProfileLevel));

            model.LevelText = UiStrings.Format("profile.level", "value", profile.ProfileLevel.ToString(), "LEVEL {value}");
            model.Progress01 = Mathf.Clamp01(profile.XpIntoLevel / (float)requirement);
            model.ProgressCaption = UiStrings.Format("profile.progress", "value", profile.XpIntoLevel + " / " + requirement,
                "next", (profile.ProfileLevel + 1).ToString(), "{value} XP towards level {next}");

            model.Headline.Add(new UiStatLine(
                UiStrings.Get("stats.highest_round", "Highest Round"), profile.HighestRound > 0 ? profile.HighestRound.ToString() : "—"));
            model.Headline.Add(new UiStatLine(UiStrings.Get("stats.rounds_completed", "Rounds Completed"), profile.RoundsCompleted.ToString()));
            model.Headline.Add(new UiStatLine(UiStrings.Get("stats.total_kills", "Total Kills"), profile.TotalKills.ToString()));

            // WP-009: "unlocked" is the EARNED entitlement in the profile, not a level
            // comparison - the same authority the selection screen and the offer gate
            // read. The ladder value is shown on a locked row only.
            var weapons = (ClassicWeaponId[])Enum.GetValues(typeof(ClassicWeaponId));
            for (int i = 0; i < weapons.Length; i++)
            {
                int required = lookup.UnlockLevelFor(weapons[i]);
                if (required >= ProfileService.NotInBuildUnlockLevel)
                {
                    continue;   // WP-069 B2: a weapon this build does not carry (rl_1 until B3)
                }

                model.Unlocks.Add(new UiUnlockLine(
                    lookup.WeaponDisplayName(weapons[i]),
                    ProfileProgression.IsWeaponEntitled(profile, weapons[i]),
                    required));
            }

            // Only the branches the ladder actually gates are listed (unlock level above
            // 1); the base branches are open from level 1 and would read as noise. Read
            // from the lookup, so a re-tuned ladder shows up here without a code change.
            var branches = (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch));
            for (int i = 0; i < branches.Length; i++)
            {
                if (lookup.UnlockLevelFor(branches[i]) > 1)
                {
                    AddBranch(model, profile, lookup, branches[i]);
                }
            }

            IReadOnlyList<AchievementSettings> definitions = lookup.Achievements;
            int defined = 0;
            int earned = 0;
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    AchievementSettings definition = definitions[i];
                    if (definition == null || string.IsNullOrEmpty(definition.Id))
                    {
                        continue;
                    }

                    defined++;
                    if (profile.Has(profile.Achievements, definition.Id))
                    {
                        earned++;
                    }
                }
            }

            model.AchievementSummary = defined == 0
                ? UiStrings.Get("ach.none_defined", "No achievements are defined in this build yet.")
                : UiStrings.Format("profile.ach_summary", "value", earned.ToString(), "total", defined.ToString(), "{value} of {total} achievements earned");

            string notice = lookup.PersistenceNotice;
            if (!string.IsNullOrEmpty(notice))
            {
                model.Notice = notice;
            }
            else if (lookup.RecoveredFromCorruptSave)
            {
                model.Notice =
                    UiStrings.Get("profile.notice.recovered", "The previous profile file could not be read, so this profile started fresh. " +
                    "The unreadable file was kept next to the save.");
            }

            return model;
        }

        private static void AddBranch(ProfileViewModel model, ProfileData profile, IProfileLookup lookup, UpgradeBranch branch)
        {
            int required = lookup.UnlockLevelFor(branch);
            model.Unlocks.Add(new UiUnlockLine(
                UiStrings.Format("profile.branch_line", "value", ProfileContentCopy.BranchName(branch, lookup.BranchDisplayName(branch)), "{value} branch"),
                ProfileProgression.IsBranchEntitled(profile, branch),
                required));
        }
    }

    /// <summary>
    /// The Statistics pane (WP-005). Three groups - progress, runs, kills - and one rule:
    /// a tier the build does not spawn is shown as a plain, muted zero with a caption
    /// that says why, never with the accent colour that would present it as content
    /// the player has merely not reached. A recorded zero is a zero; the model never
    /// prints "unknown" for a value the save file holds.
    /// </summary>
    public sealed class StatisticsViewModel
    {
        public static string Title => UiStrings.Get("menu.tab.statistics", "STATISTICS");

        public static string Note =>
            UiStrings.Get("stats.note", "Gold and time played are recorded per run only. Neither contributes profile XP.");

        /// <summary>Set on a profile with no runs; the groups are still filled with their zeros.</summary>
        public string EmptyNotice = string.Empty;

        public readonly List<UiStatLine> Progress = new List<UiStatLine>(3);
        public readonly List<UiStatLine> Runs = new List<UiStatLine>(3);
        public readonly List<UiStatLine> Kills = new List<UiStatLine>(6);

        /// <summary>Caption under the per-tier rows. Empty when every tier has been seen.</summary>
        public string TierCaption = string.Empty;

        public static StatisticsViewModel From(ProfileData profile)
        {
            var model = new StatisticsViewModel();
            if (profile == null)
            {
                return model;
            }

            if (profile.RunsPlayed <= 0)
            {
                model.EmptyNotice = UiStrings.Get("stats.empty", "No runs recorded yet. Finish a run and every line below starts filling in.");
            }

            model.Progress.Add(new UiStatLine(UiStrings.Get("stats.profile_level", "Profile Level"), profile.ProfileLevel.ToString()));
            model.Progress.Add(new UiStatLine(UiStrings.Get("stats.lifetime_xp", "Lifetime XP"), profile.TotalXpEarned.ToString()));
            model.Progress.Add(new UiStatLine(UiStrings.Get("stats.best_run_xp", "Best Single Run XP"), profile.BestRunXp.ToString()));

            model.Runs.Add(new UiStatLine(UiStrings.Get("stats.runs_played", "Runs Played"), profile.RunsPlayed.ToString()));
            model.Runs.Add(new UiStatLine(UiStrings.Get("stats.highest_round", "Highest Round"), profile.HighestRound.ToString()));
            model.Runs.Add(new UiStatLine(UiStrings.Get("stats.rounds_completed", "Rounds Completed"), profile.RoundsCompleted.ToString()));

            model.Kills.Add(new UiStatLine(UiStrings.Get("stats.total_kills", "Total Kills"), profile.TotalKills.ToString(), UiColor.TextPrimary, true));
            model.Kills.Add(new UiStatLine(UiStrings.Get("stats.tier.standard", "Standard"), profile.StandardKills.ToString()));
            AddTier(model, UiStrings.Get("stats.tier.special", "Special"), profile.SpecialKills, UiColor.AccentSakura);
            AddTier(model, UiStrings.Get("stats.tier.elite", "Elite"), profile.EliteKills, UiColor.AccentWisteria);
            AddTier(model, UiStrings.Get("stats.tier.ultra", "Ultra"), profile.UltraKills, UiColor.SilverLeaf); // WP-068: gold is currency/Special only; ceremony is ginpaku
            if (profile.UnattributedKills > 0)
            {
                // Only a legacy file can carry these (WP-009 kill invariant); shown, never hidden.
                model.Kills.Add(new UiStatLine(UiStrings.Get("stats.unattributed", "Recorded Before Tier Tracking"), profile.UnattributedKills.ToString(), UiColor.TextMuted));
            }

            model.Kills.Add(new UiStatLine(UiStrings.Get("stats.armour_broken", "Armour Pieces Broken"), profile.ArmorPiecesBroken.ToString(), UiColor.TextSecondary));

            bool anyUnseen = profile.SpecialKills <= 0 || profile.EliteKills <= 0 || profile.UltraKills <= 0;
            model.TierCaption = anyUnseen
                ? UiStrings.Get("stats.tier_caption", "Tiers recorded at zero have not appeared in a run yet.")
                : string.Empty;

            return model;
        }

        private static void AddTier(StatisticsViewModel model, string tier, int kills, UiColor accent)
        {
            // Accent only for a tier that has actually been fought. A zero is muted so
            // three coloured rows cannot advertise enemies this build does not have.
            model.Kills.Add(kills > 0
                ? new UiStatLine(tier, kills.ToString(), accent)
                : new UiStatLine(tier, "0", UiColor.TextMuted));
        }
    }

    /// <summary>
    /// The Achievements pane (WP-005). Every definition the build has, earned or not, in
    /// definition order; or the empty state when the build defines none. Nothing is
    /// invented: a build with no definitions shows a sentence, not placeholder rows.
    /// </summary>
    public sealed class AchievementsViewModel
    {
        public static string Title => UiStrings.Get("menu.tab.achievements", "ACHIEVEMENTS");

        public static string EmptyTitle => UiStrings.Get("ach.empty.title", "No achievements yet");

        public static string EmptyBody =>
            UiStrings.Get("ach.empty.body", "This build does not define any achievements. When it does, they will unlock " +
            "automatically at the end of a run and appear here.");

        public static string AutoNote => UiStrings.Get("ach.auto_note", "Achievements unlock by themselves at the end of a run. There is nothing to claim.");

        public readonly List<UiAchievementLine> Lines = new List<UiAchievementLine>(8);

        public int Earned;
        public int Defined;

        /// <summary>Earned since the player last opened the pane (WP-035).</summary>
        public int NewCount;

        public string Progress = string.Empty;

        public bool IsEmpty => Defined == 0;

        /// <summary>
        /// WP-022D: the pane from the WP-035 progress source - catalog order, lifetime
        /// counters, rarity and "new" flags. Nothing is recomputed here.
        /// </summary>
        public static AchievementsViewModel From(IAchievementProgressSource source)
        {
            var model = new AchievementsViewModel();
            if (source == null)
            {
                return model;
            }

            IReadOnlyList<AchievementProgressEntry> entries = source.Entries;
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                AchievementProgressEntry e = entries[i];
                if (string.IsNullOrEmpty(e.Id))
                {
                    continue;
                }

                model.Lines.Add(new UiAchievementLine(
                    e.Id, string.IsNullOrEmpty(e.Title) ? e.Id : e.Title, e.Description ?? string.Empty,
                    e.Earned, e.Rarity, e.IsNew, e.Current, e.Threshold));
                model.Defined++;
                if (e.Earned)
                {
                    model.Earned++;
                }
            }

            model.NewCount = source.NewCount;
            model.Progress = model.Defined == 0
                ? string.Empty
                : UiStrings.Format("ach.progress", "value", model.Earned.ToString(), "total", model.Defined.ToString(), "{value} of {total} earned")
                  + (model.NewCount > 0 ? UiStrings.Format("ach.progress_new", "value", model.NewCount.ToString(), "  ·  {value} new") : string.Empty);
            return model;
        }

        public static AchievementsViewModel From(ProfileData profile, IReadOnlyList<AchievementSettings> definitions)
        {
            var model = new AchievementsViewModel();

            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    AchievementSettings definition = definitions[i];
                    if (definition == null || string.IsNullOrEmpty(definition.Id))
                    {
                        continue;
                    }

                    bool earned = profile != null && profile.Has(profile.Achievements, definition.Id);
                    model.Lines.Add(new UiAchievementLine(
                        definition.Id,
                        string.IsNullOrEmpty(definition.DisplayName) ? definition.Id : definition.DisplayName,
                        definition.Description ?? string.Empty,
                        earned));

                    model.Defined++;
                    if (earned)
                    {
                        model.Earned++;
                    }
                }
            }

            model.Progress = model.Defined == 0
                ? string.Empty
                : UiStrings.Format("ach.progress", "value", model.Earned.ToString(), "total", model.Defined.ToString(), "{value} of {total} earned");

            return model;
        }
    }

    /// <summary>Localizes existing profile/catalog copy at the UI boundary; never changes identity or grants.</summary>
    internal static class ProfileContentCopy
    {
        public static string BranchName(UpgradeBranch branch, string fallback)
        {
            if (UiStrings.Language != UiStrings.Turkish) return fallback;
            string key;
            switch (branch)
            {
                case UpgradeBranch.Piercing: key = "tree.run.branch.piercing.name"; break;
                case UpgradeBranch.Ricochet: key = "tree.run.branch.ricochet.name"; break;
                case UpgradeBranch.Explosion: key = "tree.run.branch.explosion.name"; break;
                case UpgradeBranch.Chain: key = "tree.run.branch.chain.name"; break;
                case UpgradeBranch.FireRate: key = "tree.run.branch.fire_rate.name"; break;
                case UpgradeBranch.ReloadSpeed: key = "tree.run.branch.reload_speed.name"; break;
                case UpgradeBranch.MovementSpeed: key = "tree.run.body.move.name"; break;
                case UpgradeBranch.MaximumHealth: key = "tree.run.body.max_health.name"; break;
                case UpgradeBranch.RegenerationDelay: key = "tree.run.body.regen_delay.name"; break;
                case UpgradeBranch.RegenerationRate: key = "tree.run.body.regen_rate.name"; break;
                case UpgradeBranch.ArmorBreak: key = "tree.run.branch.armor_break.name"; break;
                case UpgradeBranch.Impact: key = "tree.run.branch.impact.name"; break;
                case UpgradeBranch.Cluster: key = "tree.run.branch.cluster.name"; break;
                default: return fallback;
            }
            return UiStrings.Get(key, fallback);
        }

        // WP-068 R1-E: every lookup below goes by identity (a cosmetic's kind, a catalog id and its type/condition, a reward's
        // ProfileUnlockKind + id), never by comparing English copy; English shows the data as is, and anything without an
        // identity or a key keeps its English text.

        public static string CosmeticTitle(CosmeticEntry entry)
        {
            string title = entry.Title ?? string.Empty;
            if (UiStrings.Language != UiStrings.Turkish || entry.Kind == CosmeticKind.Skin) return title;
            string name = CosmeticName(title);
            if (name.Length == title.Length) return title;
            return UiStrings.Format(entry.Kind == CosmeticKind.Title ? "profile.cosmetic.title_format" : "profile.cosmetic.badge_format",
                "name", name, title);
        }

        /// <summary>A title / badge name without its data kind label (Title: Blooded → Blooded), the way the pickers show it.</summary>
        public static string CosmeticName(string title)
        {
            title = title ?? string.Empty;
            int colon = title.IndexOf(": ", StringComparison.Ordinal);
            return colon > 0 && colon < 12 ? title.Substring(colon + 2) : title;
        }

        public static string UnlockDetail(string id, string fallback)
        {
            if (UiStrings.Language != UiStrings.Turkish) return fallback;
            UnlockDefinition definition = UnlockCatalog.Find(id);
            if (definition == null) return fallback;
            // A roster weapon is the one catalog weapon kind: a level condition whose level is its numeric key.
            if (definition.Type == UnlockContentType.Weapon && definition.Condition == UnlockCondition.Level)
            {
                return UiStrings.Format("results.reward.weapon_roster", "level", definition.NumericKey.ToString(), fallback);
            }
            string count = definition.Condition == UnlockCondition.Achievement
                ? AchievementCatalog.Find(definition.Key)?.Threshold.ToString() ?? string.Empty
                : definition.NumericKey.ToString();
            return UiStrings.Format("profile.content." + id + ".detail", "count", count, fallback);
        }

        /// <summary>A Results reward card's name: a branch takes its language name; weapons and achievements keep theirs.</summary>
        public static string RewardTitle(ProfileUnlockEntry entry)
        {
            string title = entry.Title ?? string.Empty;
            if (UiStrings.Language != UiStrings.Turkish || entry.Kind != ProfileUnlockKind.Branch) return title;
            return Enum.TryParse(entry.Id, false, out UpgradeBranch branch) && Enum.IsDefined(typeof(UpgradeBranch), branch)
                ? BranchName(branch, title)
                : title;
        }

        public static string RewardDetail(ProfileUnlockEntry entry)
        {
            string fallback = entry.Detail ?? string.Empty;
            if (UiStrings.Language != UiStrings.Turkish) return fallback;
            switch (entry.Kind)
            {
                case ProfileUnlockKind.Weapon:
                    return UiStrings.Get("results.reward.weapon_selectable", fallback);
                case ProfileUnlockKind.Branch:
                    return UiStrings.Get("results.reward.branch_offered", fallback);
                case ProfileUnlockKind.Content:
                    return UnlockDetail(entry.Id, fallback);
                case ProfileUnlockKind.Achievement:
                    return AchievementDetail(entry.Id, fallback);
                default:
                    return fallback;
            }
        }

        private static string AchievementDetail(string id, string fallback)
        {
            if (string.IsNullOrEmpty(id)) return fallback;
            // The old Namekuji key describes the player sliding; this catalog reward describes enemies sliding.
            string key = id == "namekuji_skater" ? "results.reward.achievement.namekuji_skater.desc" : "achievement." + id + ".desc";
            string copy = UiStrings.Get(key, fallback);
            AchievementDefinition definition = AchievementCatalog.Find(id);
            if (definition != null)
            {
                string threshold = definition.Threshold.ToString();
                copy = copy.Replace("{count}", threshold).Replace("{round}", threshold);
            }
            // A threshold the catalog cannot supply never reaches the card as a raw token.
            return copy.IndexOf('{') >= 0 ? fallback : copy;
        }
    }
}
