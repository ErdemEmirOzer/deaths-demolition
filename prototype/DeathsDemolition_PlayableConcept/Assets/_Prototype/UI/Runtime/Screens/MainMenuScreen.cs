using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Main Menu. Play, Profile, Statistics and Achievements, as the request requires.
    ///
    /// SHAPE OF THE SCREEN, AND WHY
    /// ----------------------------
    /// One hero action on the left and one reading pane on the right, rather than a
    /// stack of four buttons that each open a sub-screen.
    ///
    ///   * Play is the only thing on the screen that changes scene, so it is the only
    ///     thing styled as a primary action and it is alone above the rule. A player who
    ///     wants to play never reads anything else.
    ///   * Profile, Statistics and Achievements are READING, not navigation. Making them
    ///     panes of the same surface means the menu never goes blank between screens,
    ///     there is no back button to invent, and the profile pane is on screen by
    ///     default - so the menu shows the player's progress instead of a logo and four
    ///     buttons. That is the specific failure mode the request names: "must not look
    ///     cheap, careless, or like nothing but debug buttons".
    ///
    /// FIVE STATES. Empty: a fresh profile, which every pane states in words rather than
    /// printing a wall of zeros with no explanation. Loading: none - the profile is a
    /// local file read synchronously, so a spinner would be a lie. Error: a save that
    /// could not be read, surfaced as a warning row on the profile pane instead of a
    /// silent reset. Normal and dense: below.
    /// </summary>
    public sealed class MainMenuScreen : UiScreen
    {
        private enum Pane
        {
            Profile = 0,
            Statistics = 1,
            Achievements = 2
        }

        // Element measures. Every one is a multiple of the 8pt base, and every GAP on
        // this screen is a UiSpace member - the two together are the whole spacing spec.
        private const float NavButtonHeight = 56f;
        private const float PlayButtonHeight = 72f;
        private const float RowHeight = 40f;
        private const float AchievementRowHeight = 96f;
        private const float AchievementTitleBand = 40f;
        private const float ChipColumnWidth = 160f;
        private const float ValueColumnWidth = 200f;

        private RectTransform _paneHost;
        private Pane _pane = Pane.Profile;

        private UiButton _profileTab;
        private UiButton _statisticsTab;
        private UiButton _achievementsTab;

        protected override void Build()
        {
            float headerHeight = BuildHeader(
                "PLAYABLE CONCEPT · PROTOTYPE",
                "DEATHS DEMOLITION",
                "An old palace, a garden, and a crowd that never stops arriving.",
                UiType.Display);

            float top = headerHeight + (int)UiSpace.XXL;

            BuildNavigation(Column("Nav", 0f, 0.26f, top));
            BuildPaneHost(Column("Pane", 0.29f, 1f, top));

            ShowPane(_pane);

            ProfileService.Changed += OnProfileChanged;
        }

        private void OnDestroy()
        {
            ProfileService.Changed -= OnProfileChanged;
        }

        private void OnProfileChanged()
        {
            if (_paneHost != null)
            {
                ShowPane(_pane);
            }
        }

        // ============================================================== navigation

        private void BuildNavigation(RectTransform column)
        {
            var stack = new UiStack(column);

            UiButton play = UiFactory.Button("Play", column, "PLAY", UiButtonVariant.Primary,
                PrototypeUiFlow.GoToSelection);
            stack.Add((RectTransform)play.transform, PlayButtonHeight);
            SetInitialFocus(play);

            stack.Gap(UiSpace.S);

            Text playHint = UiFactory.Label(
                "PlayHint", column,
                "Choose a skin and a classic weapon, then fight for the highest round you can reach.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)playHint.transform, UiFactory.LineHeight(UiType.Caption) * 3f);

            stack.Gap(UiSpace.XL);

            Text sectionLabel = UiFactory.Label(
                "MenuLabel", column, "PROGRESS", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)sectionLabel.transform, UiFactory.LineHeight(UiType.Label));

            stack.Gap(UiSpace.S);

            _profileTab = AddTab(ref stack, column, "PROFILE", Pane.Profile);
            stack.Gap(UiSpace.S);
            _statisticsTab = AddTab(ref stack, column, "STATISTICS", Pane.Statistics);
            stack.Gap(UiSpace.S);
            _achievementsTab = AddTab(ref stack, column, "ACHIEVEMENTS", Pane.Achievements);

            // Quit is docked to the foot of the column rather than to the cursor: a
            // destructive action should not sit adjacent to the reading tabs.
            UiButton quit = UiFactory.Button("Quit", column, "QUIT", UiButtonVariant.Secondary,
                PrototypeUiFlow.Quit);
            RectTransform quitRect = (RectTransform)quit.transform;
            quitRect.anchorMin = new Vector2(0f, 0f);
            quitRect.anchorMax = new Vector2(1f, 0f);
            quitRect.pivot = new Vector2(0.5f, 0f);
            quitRect.sizeDelta = new Vector2(0f, UiTokens.TargetPreferred);
            quitRect.anchoredPosition = Vector2.zero;

            Text build = UiFactory.Label(
                "BuildNote", column,
                "Prototype build. Every number in it is placeholder content, not a product decision.",
                UiType.Caption, UiColor.TextMuted);
            RectTransform buildRect = (RectTransform)build.transform;
            buildRect.anchorMin = new Vector2(0f, 0f);
            buildRect.anchorMax = new Vector2(1f, 0f);
            buildRect.pivot = new Vector2(0.5f, 0f);
            buildRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Caption) * 3f);
            buildRect.anchoredPosition = new Vector2(0f, UiTokens.TargetPreferred + (int)UiSpace.M);
        }

        private UiButton AddTab(ref UiStack stack, RectTransform column, string label, Pane pane)
        {
            UiButton button = UiFactory.Button("Tab." + pane, column, label, UiButtonVariant.Nav,
                () => ShowPane(pane));
            stack.Add((RectTransform)button.transform, NavButtonHeight);
            return button;
        }

        // ==================================================================== panes

        private void BuildPaneHost(RectTransform column)
        {
            RectTransform card = UiFactory.Card("PaneCard", column, UiColor.BorderHairline, UiColor.SurfacePanel);
            UiFactory.Fill(card);

            // Clipped, so a pane that grows past the card is cut at the card edge rather
            // than spilling over the navigation column.
            _paneHost = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XL));
        }

        private void ShowPane(Pane pane)
        {
            _pane = pane;

            if (_profileTab != null)
            {
                _profileTab.SetSelectedChoice(pane == Pane.Profile);
                _statisticsTab.SetSelectedChoice(pane == Pane.Statistics);
                _achievementsTab.SetSelectedChoice(pane == Pane.Achievements);
            }

            ClearChildren(_paneHost);

            switch (pane)
            {
                case Pane.Statistics:
                    BuildStatisticsPane();
                    break;

                case Pane.Achievements:
                    BuildAchievementsPane();
                    break;

                default:
                    BuildProfilePane();
                    break;
            }
        }

        /// <summary>
        /// Detaches before destroying. Destroy is deferred to the end of the frame, so a
        /// pane rebuilt in the same frame would otherwise draw on top of the dying one.
        /// </summary>
        private static void ClearChildren(RectTransform host)
        {
            if (host == null)
            {
                return;
            }

            for (int i = host.childCount - 1; i >= 0; i--)
            {
                Transform child = host.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        // ------------------------------------------------------------- profile pane

        private void BuildProfilePane()
        {
            ProfileData profile = ProfileService.Current;
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading("PROFILE", _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XL);

            if (ProfileService.RecoveredFromCorruptSave)
            {
                // Error state, stated rather than hidden. The game is fully playable;
                // only the previous profile was lost, and it was kept on disk.
                stack.Add(BuildNotice(
                    "The previous profile file could not be read, so this profile started fresh. " +
                    "The unreadable file was kept next to the save.",
                    UiColor.StateWarning), 72f);
                stack.Gap(UiSpace.L);
            }

            int requirement = ProfileService.LevelRequirement(profile.ProfileLevel);

            Text level = UiFactory.Numeric(
                "Level", _paneHost, "LEVEL " + profile.ProfileLevel, UiType.NumericXL, UiColor.TextPrimary);
            stack.Add((RectTransform)level.transform, 52f);

            stack.Gap(UiSpace.M);

            UiBar xpBar = UiFactory.Bar("XpBar", _paneHost, UiColor.BarXp, 14f);
            xpBar.SetValue(ProfileService.LevelProgress01, true);
            stack.Add((RectTransform)xpBar.transform, 14f);

            stack.Gap(UiSpace.S);

            Text xpCaption = UiFactory.Label(
                "XpCaption", _paneHost,
                profile.XpIntoLevel + " / " + requirement + " XP towards level " + (profile.ProfileLevel + 1),
                UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)xpCaption.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D1", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            // Two headline numbers only. Runs played and lifetime XP live on the
            // statistics pane; repeating them here would cost the vertical budget this
            // pane needs for the unlock list, which is the part that answers "what does
            // levelling actually give me".
            AddRow(ref stack, "Highest Round", profile.HighestRound > 0 ? profile.HighestRound.ToString() : "—");
            AddRow(ref stack, "Total Kills", profile.TotalKills.ToString());

            stack.Gap(UiSpace.L);
            stack.Add((RectTransform)UiFactory.Divider("D2", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            Text unlockLabel = UiFactory.Label(
                "UnlockLabel", _paneHost, "UNLOCKED CONTENT", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)unlockLabel.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.M);

            var weapons = (ClassicWeaponId[])Enum.GetValues(typeof(ClassicWeaponId));
            for (int i = 0; i < weapons.Length; i++)
            {
                AddUnlockRow(
                    ref stack,
                    ProfileService.WeaponDisplayName(weapons[i]),
                    ProfileService.IsWeaponUnlocked(weapons[i]),
                    ProfileService.UnlockLevelFor(weapons[i]));
            }

            AddUnlockRow(
                ref stack,
                ProfileService.BranchDisplayName(UpgradeBranch.Explosion) + " branch",
                ProfileService.IsBranchUnlocked(UpgradeBranch.Explosion),
                ProfileService.UnlockLevelFor(UpgradeBranch.Explosion));

            AddUnlockRow(
                ref stack,
                ProfileService.BranchDisplayName(UpgradeBranch.Chain) + " branch",
                ProfileService.IsBranchUnlocked(UpgradeBranch.Chain),
                ProfileService.UnlockLevelFor(UpgradeBranch.Chain));

            stack.Gap(UiSpace.L);

            Text rule = UiFactory.Label(
                "Rule", _paneHost,
                "Persistent progress unlocks content only. It never grants starting damage, health or gold.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)rule.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
        }

        // ---------------------------------------------------------- statistics pane

        private void BuildStatisticsPane()
        {
            ProfileData profile = ProfileService.Current;
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading("STATISTICS", _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.L);

            if (profile.RunsPlayed <= 0)
            {
                // Empty state. Zeros with no explanation read as a broken screen.
                stack.Add(BuildNotice(
                    "No runs recorded yet. Finish a run and every line below starts filling in.",
                    UiColor.TextSecondary), 56f);
                stack.Gap(UiSpace.L);
            }

            AddRow(ref stack, "Profile Level", profile.ProfileLevel.ToString());
            AddRow(ref stack, "Lifetime XP", profile.TotalXpEarned.ToString());
            AddRow(ref stack, "Best Single Run XP", profile.BestRunXp.ToString());

            stack.Gap(UiSpace.S);
            stack.Add((RectTransform)UiFactory.Divider("D1", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.S);

            AddRow(ref stack, "Runs Played", profile.RunsPlayed.ToString());
            AddRow(ref stack, "Highest Round", profile.HighestRound.ToString());
            AddRow(ref stack, "Rounds Completed", profile.RoundsCompleted.ToString());

            stack.Gap(UiSpace.S);
            stack.Add((RectTransform)UiFactory.Divider("D2", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.S);

            AddRow(ref stack, "Total Kills", profile.TotalKills.ToString());
            AddRow(ref stack, "Standard Kills", profile.StandardKills.ToString());
            AddRow(ref stack, "Special Kills", profile.SpecialKills.ToString(), UiColor.AccentSakura);
            AddRow(ref stack, "Elite Kills", profile.EliteKills.ToString(), UiColor.AccentWisteria);
            AddRow(ref stack, "Ultra Kills", profile.UltraKills.ToString(), UiColor.StateGold);
            AddRow(ref stack, "Armour Pieces Broken", profile.ArmorPiecesBroken.ToString());

            stack.Gap(UiSpace.L);

            Text note = UiFactory.Label(
                "Note", _paneHost,
                "Gold and time played are recorded per run only. Neither contributes profile XP.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
        }

        // -------------------------------------------------------- achievements pane

        private void BuildAchievementsPane()
        {
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading("ACHIEVEMENTS", _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.M);

            System.Collections.Generic.IReadOnlyList<AchievementSettings> all = ProfileService.AllAchievements;

            int earned = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && ProfileService.HasAchievement(all[i].Id))
                {
                    earned++;
                }
            }

            Text progress = UiFactory.Label(
                "Progress", _paneHost,
                earned + " of " + all.Count + " earned",
                UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)progress.transform, UiFactory.LineHeight(UiType.Body));

            stack.Gap(UiSpace.L);

            for (int i = 0; i < all.Count; i++)
            {
                AchievementSettings definition = all[i];
                if (definition == null)
                {
                    continue;
                }

                bool has = ProfileService.HasAchievement(definition.Id);
                stack.Add(BuildAchievementRow(definition, has), AchievementRowHeight);
                stack.Gap(UiSpace.M);
            }
        }

        private RectTransform BuildAchievementRow(AchievementSettings definition, bool earned)
        {
            // StateSuccess and not AccentMoss for the earned border: moss measures
            // 2.37:1 against SurfacePanel, below the 3:1 floor for an interface
            // boundary. Jade measures 7.62:1 on the same surface.
            RectTransform row = UiFactory.Card(
                "Achievement." + definition.Id,
                _paneHost,
                earned ? UiColor.StateSuccess : UiColor.BorderHairline,
                earned ? UiColor.SurfaceRaised : UiColor.SurfaceBase);

            RectTransform content = UiFactory.CardContent(row, UiSpace.L);

            Text title = UiFactory.Label(
                "Title", content, definition.DisplayName, UiType.H3,
                earned ? UiColor.TextPrimary : UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMax = new Vector2(-ChipColumnWidth, 0f);
            titleRect.offsetMin = new Vector2(0f, AchievementTitleBand);

            // TextSecondary, not TextMuted: muted measures 4.27:1 on SurfaceRaised,
            // which is below the 4.5 floor, and an earned achievement's card IS raised.
            Text description = UiFactory.Label(
                "Description", content,
                UiFactory.Clip(definition.Description, 90),
                UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform descriptionRect = UiFactory.Fill((RectTransform)description.transform);
            descriptionRect.offsetMax = new Vector2(-ChipColumnWidth, -AchievementTitleBand);

            Text chip = UiFactory.Chip(
                "State", content,
                earned ? "EARNED" : "LOCKED",
                earned ? UiColor.StateSuccess : UiColor.SurfaceInset,
                earned ? UiColor.SurfaceInset : UiColor.TextSecondary,
                120f, 30f);

            RectTransform chipRect = (RectTransform)chip.transform.parent;
            chipRect.anchorMin = new Vector2(1f, 0.5f);
            chipRect.anchorMax = new Vector2(1f, 0.5f);
            chipRect.pivot = new Vector2(1f, 0.5f);
            chipRect.anchoredPosition = Vector2.zero;

            return row;
        }

        // =================================================================== helpers

        private void AddRow(ref UiStack stack, string key, string value, UiColor color = UiColor.TextPrimary)
        {
            Text valueText = UiFactory.StatRow(key, value, _paneHost, color);
            stack.Add((RectTransform)valueText.transform.parent, RowHeight);
        }

        private void AddUnlockRow(ref UiStack stack, string title, bool unlocked, int requiredLevel)
        {
            RectTransform row = UiFactory.NewRect("Unlock", _paneHost);

            Text label = UiFactory.Label(
                "Label", row, title, UiType.Body,
                unlocked ? UiColor.TextPrimary : UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.offsetMax = new Vector2(-ValueColumnWidth, 0f);

            Text chip = UiFactory.Chip(
                "State", row,
                unlocked ? "UNLOCKED" : "LEVEL " + requiredLevel,
                UiColor.SurfaceInset,
                unlocked ? UiColor.StateSuccess : UiColor.TextSecondary,
                144f, 28f);

            RectTransform chipRect = (RectTransform)chip.transform.parent;
            chipRect.anchorMin = new Vector2(1f, 0.5f);
            chipRect.anchorMax = new Vector2(1f, 0.5f);
            chipRect.pivot = new Vector2(1f, 0.5f);
            chipRect.anchoredPosition = Vector2.zero;

            stack.Add(row, RowHeight);
        }

        /// <summary>An inline notice: accent rule on the left, wrapped text beside it.</summary>
        private RectTransform BuildNotice(string message, UiColor accent)
        {
            RectTransform row = UiFactory.NewRect("Notice", _paneHost);

            Image rule = UiFactory.NewImage("Rule", row, accent, UiSprites.Chip);
            RectTransform ruleRect = UiFactory.Fill((RectTransform)rule.transform);
            ruleRect.anchorMax = new Vector2(0f, 1f);
            ruleRect.sizeDelta = new Vector2(UiTokens.HeadingTickWidth, 0f);
            ruleRect.offsetMin = new Vector2(0f, 0f);
            ruleRect.offsetMax = new Vector2(UiTokens.HeadingTickWidth, 0f);

            Text text = UiFactory.Label("Text", row, message, UiType.Body, accent, TextAnchor.UpperLeft);
            RectTransform textRect = UiFactory.Fill((RectTransform)text.transform);
            textRect.offsetMin = new Vector2(UiTokens.HeadingTickWidth + (int)UiSpace.M, 0f);

            return row;
        }
    }
}
