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
    /// NAVIGATION STATE (WP-005). Each pane is a <see cref="UiContext"/> layer on the
    /// <see cref="UiContext.MainMenu"/> base, reported to <see cref="UiNavigation"/>, so
    /// "Main Menu -> Profile and back" is a state a test can read rather than a private
    /// enum. Everything the panes print comes from a view model built once from the
    /// persisted <see cref="ProfileData"/>; the screen decides nothing about the data.
    ///
    /// FIVE STATES. Empty: a fresh profile, which every pane states in words rather than
    /// printing a wall of zeros with no explanation. Loading: none - the profile is a
    /// local file read synchronously, so a spinner would be a lie. Error: a save that
    /// could not be read, surfaced as a warning row on the profile pane instead of a
    /// silent reset. Normal and dense: below.
    /// </summary>
    public sealed class MainMenuScreen : UiScreen
    {
        // Element measures. Every one is a multiple of the 8pt base, and every GAP on
        // this screen is a UiSpace member - the two together are the whole spacing spec.
        private const float NavButtonHeight = 56f;
        private const float PlayButtonHeight = 72f;
        private const float RowHeight = 40f;
        private const float AchievementRowHeight = 96f;
        private const float AchievementTitleBand = 40f;
        private const float ChipColumnWidth = 160f;
        private const float ValueColumnWidth = 200f;

        /// <summary>Hard cap on achievement cards before the list summarises the rest.</summary>
        private const int MaxAchievementRows = 5;

        private RectTransform _paneHost;
        private UiContext _pane = UiContext.Profile;

        private UiButton _profileTab;
        private UiButton _statisticsTab;
        private UiButton _achievementsTab;

        /// <summary>The pane on screen. Exposed for tests; the source of truth is <see cref="UiNavigation"/>.</summary>
        public UiContext Pane
        {
            get { return _pane; }
        }

        protected override UiContext BaseContext
        {
            get { return UiContext.MainMenu; }
        }

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
                RedrawPane();
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

            _profileTab = AddTab(ref stack, column, "PROFILE", UiContext.Profile);
            stack.Gap(UiSpace.S);
            _statisticsTab = AddTab(ref stack, column, "STATISTICS", UiContext.Statistics);
            stack.Gap(UiSpace.S);
            _achievementsTab = AddTab(ref stack, column, "ACHIEVEMENTS", UiContext.Achievements);

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
                "Prototype build.",
                UiType.Caption, UiColor.TextMuted);
            RectTransform buildRect = (RectTransform)build.transform;
            buildRect.anchorMin = new Vector2(0f, 0f);
            buildRect.anchorMax = new Vector2(1f, 0f);
            buildRect.pivot = new Vector2(0.5f, 0f);
            buildRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Caption) * 2f);
            buildRect.anchoredPosition = new Vector2(0f, UiTokens.TargetPreferred + (int)UiSpace.M);
        }

        private UiButton AddTab(ref UiStack stack, RectTransform column, string label, UiContext pane)
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

        /// <summary>
        /// Opens a pane. Idempotent through <see cref="UiNavigation"/>: a second click on
        /// the tab that is already open changes nothing and rebuilds nothing.
        /// </summary>
        public void ShowPane(UiContext pane)
        {
            if (!UiNavigationModel.IsLayer(pane) || UiNavigationModel.BaseOf(pane) != UiContext.MainMenu)
            {
                return;
            }

            UiNavigationResult result = UiNavigation.EnterLayer(pane);

            if (result == UiNavigationResult.AlreadyThere && _pane == pane && _paneHost.childCount > 0)
            {
                return;
            }

            // Refused only means the base is not the main menu (a test built this screen
            // in another scene). The pane is drawn anyway; the model is simply not
            // tracking this screen.
            _pane = pane;
            RedrawPane();
        }

        private void RedrawPane()
        {
            if (_profileTab != null)
            {
                _profileTab.SetSelectedChoice(_pane == UiContext.Profile);
                _statisticsTab.SetSelectedChoice(_pane == UiContext.Statistics);
                _achievementsTab.SetSelectedChoice(_pane == UiContext.Achievements);
            }

            ClearChildren(_paneHost);

            ProfileData profile = ProfileService.Current;

            switch (_pane)
            {
                case UiContext.Statistics:
                    BuildStatisticsPane(StatisticsViewModel.From(profile));
                    break;

                case UiContext.Achievements:
                    BuildAchievementsPane(AchievementsViewModel.From(profile, ProfileService.AllAchievements));
                    break;

                default:
                    BuildProfilePane(ProfileViewModel.From(profile, ProfileServiceLookup.Instance));
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

        private void BuildProfilePane(ProfileViewModel model)
        {
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading(ProfileViewModel.Title, _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XL);

            if (!string.IsNullOrEmpty(model.Notice))
            {
                // Error state, stated rather than hidden. The game is fully playable;
                // only the previous profile was lost, and it was kept on disk.
                stack.Add(BuildNotice(model.Notice, UiColor.StateWarning), 72f);
                stack.Gap(UiSpace.L);
            }

            // "PROFILE LEVEL", not "LEVEL": the eyebrow says at first glance that this
            // number is the profile's experience, not a character's power.
            Text levelLabel = UiFactory.Label(
                "LevelLabel", _paneHost, "PROFILE LEVEL", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)levelLabel.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.XS);

            Text level = UiFactory.Numeric(
                "Level", _paneHost, model.LevelText, UiType.NumericXL, UiColor.TextPrimary);
            stack.Add((RectTransform)level.transform, UiFactory.LineHeight(UiType.NumericXL));

            stack.Gap(UiSpace.M);

            UiBar xpBar = UiFactory.Bar("XpBar", _paneHost, UiColor.BarXp, 14f);
            xpBar.SetValue(model.Progress01, true);
            stack.Add((RectTransform)xpBar.transform, 14f);

            stack.Gap(UiSpace.S);

            Text xpCaption = UiFactory.Label(
                "XpCaption", _paneHost, model.ProgressCaption, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)xpCaption.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D1", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            for (int i = 0; i < model.Headline.Count; i++)
            {
                AddRow(ref stack, model.Headline[i]);
            }

            stack.Gap(UiSpace.L);
            stack.Add((RectTransform)UiFactory.Divider("D2", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            Text unlockLabel = UiFactory.Label(
                "UnlockLabel", _paneHost, "UNLOCKED CONTENT", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)unlockLabel.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.M);

            for (int i = 0; i < model.Unlocks.Count; i++)
            {
                AddUnlockRow(ref stack, model.Unlocks[i]);
            }

            stack.Gap(UiSpace.M);

            Text achievements = UiFactory.Label(
                "AchievementSummary", _paneHost, model.AchievementSummary, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)achievements.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.M);

            Text rule = UiFactory.Label(
                "Rule", _paneHost, ProfileViewModel.RuleNote, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)rule.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
        }

        // ---------------------------------------------------------- statistics pane

        private void BuildStatisticsPane(StatisticsViewModel model)
        {
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading(StatisticsViewModel.Title, _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.L);

            if (!string.IsNullOrEmpty(model.EmptyNotice))
            {
                // Empty state. Zeros with no explanation read as a broken screen.
                stack.Add(BuildNotice(model.EmptyNotice, UiColor.TextSecondary), 56f);
                stack.Gap(UiSpace.L);
            }

            // Run facts first, kills second, XP last: the numbers a player made with
            // their hands come before the numbers the profile derived from them.
            for (int i = 0; i < model.Runs.Count; i++)
            {
                AddRow(ref stack, model.Runs[i]);
            }

            stack.Gap(UiSpace.S);
            stack.Add((RectTransform)UiFactory.Divider("D1", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.S);

            for (int i = 0; i < model.Kills.Count; i++)
            {
                AddRow(ref stack, model.Kills[i]);
            }

            if (!string.IsNullOrEmpty(model.TierCaption))
            {
                Text caption = UiFactory.Label(
                    "TierCaption", _paneHost, model.TierCaption, UiType.Caption, UiColor.TextMuted);
                stack.Add((RectTransform)caption.transform, UiFactory.LineHeight(UiType.Caption));
            }

            stack.Gap(UiSpace.S);
            stack.Add((RectTransform)UiFactory.Divider("D2", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.S);

            for (int i = 0; i < model.Progress.Count; i++)
            {
                AddRow(ref stack, model.Progress[i]);
            }

            stack.Gap(UiSpace.L);

            Text note = UiFactory.Label(
                "Note", _paneHost, StatisticsViewModel.Note, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
        }

        // -------------------------------------------------------- achievements pane

        private void BuildAchievementsPane(AchievementsViewModel model)
        {
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading(AchievementsViewModel.Title, _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.M);

            if (model.IsEmpty)
            {
                // A build with no definitions is a legitimate state, not an error. It is
                // written as a finished sentence, and the pane still works.
                Text title = UiFactory.Label(
                    "EmptyTitle", _paneHost, AchievementsViewModel.EmptyTitle, UiType.H3, UiColor.TextPrimary);
                stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H3));
                stack.Gap(UiSpace.S);
                stack.Add(BuildNotice(AchievementsViewModel.EmptyBody, UiColor.TextSecondary), 72f);
                return;
            }

            Text progress = UiFactory.Label(
                "Progress", _paneHost, model.Progress, UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)progress.transform, UiFactory.LineHeight(UiType.Body));

            stack.Gap(UiSpace.L);

            int shown = Mathf.Min(model.Lines.Count, MaxAchievementRows);
            for (int i = 0; i < shown; i++)
            {
                stack.Add(BuildAchievementRow(model.Lines[i]), AchievementRowHeight);
                stack.Gap(UiSpace.M);
            }

            if (model.Lines.Count > shown)
            {
                Text more = UiFactory.Label(
                    "More", _paneHost,
                    "and " + (model.Lines.Count - shown) + " more",
                    UiType.Caption, UiColor.TextSecondary);
                stack.Add((RectTransform)more.transform, UiFactory.LineHeight(UiType.Caption));
                stack.Gap(UiSpace.M);
            }

            Text note = UiFactory.Label(
                "Note", _paneHost, AchievementsViewModel.AutoNote, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
        }

        private RectTransform BuildAchievementRow(UiAchievementLine line)
        {
            // StateSuccess and not AccentMoss for the earned border: moss measures
            // 2.37:1 against SurfacePanel, below the 3:1 floor for an interface
            // boundary. Jade measures 7.62:1 on the same surface.
            RectTransform row = UiFactory.Card(
                "Achievement." + line.Id,
                _paneHost,
                line.Earned ? UiColor.StateSuccess : UiColor.BorderHairline,
                line.Earned ? UiColor.SurfaceRaised : UiColor.SurfaceBase);

            RectTransform content = UiFactory.CardContent(row, UiSpace.L);

            Text title = UiFactory.Label(
                "Title", content, line.Title, UiType.H3,
                line.Earned ? UiColor.TextPrimary : UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMax = new Vector2(-ChipColumnWidth, 0f);
            titleRect.offsetMin = new Vector2(0f, AchievementTitleBand);

            // TextSecondary, not TextMuted: muted measures 4.27:1 on SurfaceRaised,
            // which is below the 4.5 floor, and an earned achievement's card IS raised.
            Text description = UiFactory.Label(
                "Description", content,
                UiFactory.Clip(line.Description, 90),
                UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform descriptionRect = UiFactory.Fill((RectTransform)description.transform);
            descriptionRect.offsetMax = new Vector2(-ChipColumnWidth, -AchievementTitleBand);

            Text chip = UiFactory.Chip(
                "State", content,
                line.Earned ? "EARNED" : "LOCKED",
                line.Earned ? UiColor.StateSuccess : UiColor.SurfaceInset,
                line.Earned ? UiColor.SurfaceInset : UiColor.TextSecondary,
                120f, 30f);

            RectTransform chipRect = (RectTransform)chip.transform.parent;
            chipRect.anchorMin = new Vector2(1f, 0.5f);
            chipRect.anchorMax = new Vector2(1f, 0.5f);
            chipRect.pivot = new Vector2(1f, 0.5f);
            chipRect.anchoredPosition = Vector2.zero;

            return row;
        }

        // =================================================================== helpers

        private void AddRow(ref UiStack stack, UiStatLine line)
        {
            Text valueText = UiFactory.StatRow(
                line.Label, line.Value, _paneHost, line.Color,
                line.Emphasis ? UiType.H3 : UiType.BodyStrong);
            stack.Add((RectTransform)valueText.transform.parent, RowHeight);
        }

        private void AddUnlockRow(ref UiStack stack, UiUnlockLine line)
        {
            RectTransform row = UiFactory.NewRect("Unlock", _paneHost);

            Text label = UiFactory.Label(
                "Label", row, line.Title, UiType.Body,
                line.Unlocked ? UiColor.TextPrimary : UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.offsetMax = new Vector2(-ValueColumnWidth, 0f);

            Text chip = UiFactory.Chip(
                "State", row,
                line.Unlocked ? "UNLOCKED" : "LEVEL " + line.RequiredLevel,
                UiColor.SurfaceInset,
                line.Unlocked ? UiColor.StateSuccess : UiColor.TextSecondary,
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
