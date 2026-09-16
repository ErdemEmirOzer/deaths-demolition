using System;
using System.Collections.Generic;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;

namespace DD.Prototype.Achievements
{
    /// <summary>What kind of cosmetic an entry is. Skins are listed for completeness; they are always open (DEC-018) and their selection is session-only (DEC-039).</summary>
    public enum CosmeticKind
    {
        Title = 0,
        Badge = 1,
        Skin = 2
    }

    /// <summary>One cosmetic as the profile / selection screens show it (WP-035 contract).</summary>
    public readonly struct CosmeticEntry
    {
        public readonly string Id;
        public readonly CosmeticKind Kind;
        public readonly string Title;
        public readonly string Detail;

        /// <summary>True when the profile may select it (skins: always).</summary>
        public readonly bool Unlocked;

        /// <summary>True when it was opened after the player last looked (see <see cref="ICosmeticsSource.MarkContentSeen"/>).</summary>
        public readonly bool IsNew;

        /// <summary>True when it is the current selection for its kind.</summary>
        public readonly bool Selected;

        public CosmeticEntry(string id, CosmeticKind kind, string title, string detail, bool unlocked, bool isNew, bool selected)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            Unlocked = unlocked;
            IsNew = isNew;
            Selected = selected;
        }
    }

    /// <summary>One achievement with its progress (WP-035 contract for the Achievements pane / Results).</summary>
    public readonly struct AchievementProgressEntry
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;
        public readonly AchievementRarity Rarity;
        public readonly AchievementScope Scope;
        public readonly bool Earned;

        /// <summary>True when earned after the player last looked (see <see cref="IAchievementProgressSource.MarkAchievementsSeen"/>).</summary>
        public readonly bool IsNew;

        /// <summary>Current counter (lifetime scope; earned single-run entries read as the threshold), clamped to the threshold.</summary>
        public readonly long Current;
        public readonly int Threshold;

        /// <summary>Current / Threshold in [0, 1]; 1 when earned.</summary>
        public readonly float Progress01;

        public AchievementProgressEntry(string id, string title, string description, AchievementRarity rarity, AchievementScope scope,
            bool earned, bool isNew, long current, int threshold)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Rarity = rarity;
            Scope = scope;
            Earned = earned;
            IsNew = isNew;
            Threshold = Math.Max(1, threshold);
            Current = earned ? Threshold : Math.Min(Math.Max(0, current), Threshold);
            Progress01 = earned ? 1f : (float)Current / Threshold;
        }
    }

    /// <summary>
    /// ADR-035-1: the one door the Achievements pane and Results read for achievement
    /// progress. Implementation: <see cref="ProfileService.AchievementProgressSource"/>.
    /// Lists are rebuilt on each access from the live profile; hold the entries, not the source.
    /// </summary>
    public interface IAchievementProgressSource
    {
        /// <summary>Every catalog achievement in catalog order.</summary>
        IReadOnlyList<AchievementProgressEntry> Entries { get; }

        int EarnedCount { get; }
        int TotalCount { get; }

        /// <summary>Earned entries the player has not looked at yet.</summary>
        int NewCount { get; }

        /// <summary>The most recently earned achievement id (last of the profile's earned list), or empty.</summary>
        string LastEarnedId { get; }

        /// <summary>Clears the "new" flags (persists with the next save). Call when the pane is shown.</summary>
        void MarkAchievementsSeen();
    }

    /// <summary>
    /// ADR-035-1: the one door the profile / selection screens read for cosmetics.
    /// Implementation: <see cref="ProfileService.CosmeticsSource"/>.
    /// </summary>
    public interface ICosmeticsSource
    {
        IReadOnlyList<CosmeticEntry> Titles { get; }
        IReadOnlyList<CosmeticEntry> Badges { get; }

        /// <summary>The player skins (AP-006). Always unlocked; <c>Selected</c> mirrors the session choice (DEC-039), never the profile.</summary>
        IReadOnlyList<CosmeticEntry> Skins { get; }

        /// <summary>Selected title id or empty.</summary>
        string SelectedTitle { get; }

        /// <summary>Selected badge id or empty.</summary>
        string SelectedBadge { get; }

        /// <summary>Opened content the player has not looked at yet (titles, badges and indicators).</summary>
        int NewCount { get; }

        /// <summary>Selects an opened title (empty clears). False when the id is not an opened title or the profile is read-only.</summary>
        bool SelectTitle(string id);

        /// <summary>Selects an opened badge (empty clears). False when the id is not an opened badge or the profile is read-only.</summary>
        bool SelectBadge(string id);

        /// <summary>Clears the "new" flags on content (persists with the next save).</summary>
        void MarkContentSeen();
    }

    /// <summary>
    /// WP-035: the cosmetics data layer. Titles and badges are catalog content
    /// (<see cref="UnlockCatalog"/>, types Title / Badge) that the run-end transaction opened
    /// into <c>ProfileData.UnlockedContent</c>; the profile remembers the selected one per
    /// kind. Nothing here touches gameplay; no screen is built here (S-09 owns the UI).
    /// </summary>
    public static class ProfileCosmetics
    {
        public static bool IsTitle(UnlockDefinition d)
        {
            return d != null && d.Type == UnlockContentType.Title;
        }

        public static bool IsBadge(UnlockDefinition d)
        {
            return d != null && d.Type == UnlockContentType.Badge;
        }

        /// <summary>The catalog entries of one kind, decorated with the profile's state.</summary>
        public static List<CosmeticEntry> Build(ProfileData profile, UnlockContentType type, string selectedId, PlayerSkin sessionSkin)
        {
            var list = new List<CosmeticEntry>();
            if (type == UnlockContentType.Cosmetic)
            {
                // Skins: AP-006 palettes, always open, session selection only.
                for (int i = 0; i < PlayerSkins.All.Length; i++)
                {
                    var skin = (PlayerSkin)i;
                    list.Add(new CosmeticEntry(skin.ToString(), CosmeticKind.Skin, PlayerSkins.All[i].DisplayName,
                        "Player skin - always available", true, false, skin == sessionSkin));
                }

                return list;
            }

            IReadOnlyList<UnlockDefinition> entries = UnlockCatalog.Entries;
            CosmeticKind kind = type == UnlockContentType.Badge ? CosmeticKind.Badge : CosmeticKind.Title;
            for (int i = 0; i < entries.Count; i++)
            {
                UnlockDefinition d = entries[i];
                if (d == null || d.Type != type)
                {
                    continue;
                }

                bool unlocked = profile != null && profile.Has(profile.UnlockedContent, d.Id);
                bool isNew = unlocked && IsNewContent(profile, d.Id);
                list.Add(new CosmeticEntry(d.Id, kind, d.DisplayName, d.Detail, unlocked, isNew,
                    unlocked && string.Equals(selectedId, d.Id, StringComparison.Ordinal)));
            }

            return list;
        }

        /// <summary>True when <paramref name="id"/> sits beyond the profile's seen count in the opened list.</summary>
        public static bool IsNewContent(ProfileData profile, string id)
        {
            if (profile == null || profile.UnlockedContent == null)
            {
                return false;
            }

            int index = profile.UnlockedContent.IndexOf(id);
            return index >= Math.Max(0, profile.ContentSeen);
        }

        /// <summary>True when <paramref name="id"/> sits beyond the profile's seen count in the earned list.</summary>
        public static bool IsNewAchievement(ProfileData profile, string id)
        {
            if (profile == null || profile.Achievements == null)
            {
                return false;
            }

            int index = profile.Achievements.IndexOf(id);
            return index >= Math.Max(0, profile.AchievementsSeen);
        }

        /// <summary>The achievement catalog decorated with the profile's earned / progress state.</summary>
        public static List<AchievementProgressEntry> BuildProgress(ProfileData profile)
        {
            IReadOnlyList<AchievementDefinition> defs = AchievementCatalog.Definitions;
            var list = new List<AchievementProgressEntry>(defs.Count);
            for (int i = 0; i < defs.Count; i++)
            {
                AchievementDefinition d = defs[i];
                bool earned = profile != null && profile.Has(profile.Achievements, d.Id);
                long current = d.Scope == AchievementScope.Lifetime ? AchievementEngine.LifetimeValue(profile, d) : 0;
                list.Add(new AchievementProgressEntry(d.Id, d.DisplayName, d.Description, d.Rarity, d.Scope, earned,
                    earned && IsNewAchievement(profile, d.Id), current, d.Threshold));
            }

            return list;
        }

        /// <summary>Whether <paramref name="id"/> is an opened catalog entry of the given type on this profile.</summary>
        public static bool CanSelect(ProfileData profile, UnlockContentType type, string id)
        {
            if (profile == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            UnlockDefinition d = UnlockCatalog.Find(id);
            return d != null && d.Type == type && profile.Has(profile.UnlockedContent, id);
        }
    }
}
