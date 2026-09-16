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
        private UiButton _settingsTab;
        private UiButton _playButton;

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
            BuildMenuPlate();

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

        /// <summary>
        /// Gamepad / keyboard Cancel (WP-022): a pane other than Profile goes back to
        /// Profile; on Profile the focus returns to PLAY. Nothing destructive is bound to
        /// Cancel here - QUIT stays a deliberate press.
        /// </summary>
        private void Update()
        {
            if (!UiInput.CancelPressed())
            {
                return;
            }

            if (_pane != UiContext.Profile)
            {
                ShowPane(UiContext.Profile);
                UiInput.Focus(_profileTab);
            }
            else
            {
                UiInput.Focus(_playButton);
            }
        }

        /// <summary>
        /// The AP-012 menu plate (DEC-076 atmosphere) under the page, with the procedural
        /// backdrop's veils kept on top of it so text contrast is measured against the
        /// same dark ground as before. Without the atlas the procedural backdrop stands.
        /// </summary>
        private void BuildMenuPlate()
        {
            Sprite plate = UiArt.MenuPlate();
            if (plate == null)
            {
                return;
            }

            Transform backdrop = ScreenRect.Find("Backdrop");
            if (backdrop == null)
            {
                return;
            }

            Image image = UiFactory.NewImage("MenuPlate", backdrop, UiColor.TextPrimary, plate);
            RectTransform rect = UiFactory.Fill((RectTransform)image.transform);
            image.preserveAspect = false;
            // Below the veils (index 1: right after the base fill), above the base.
            rect.SetSiblingIndex(1);

            // A darkening wash over the plate: the plate's own left third is already
            // x0.55; this brings the panel side down too, so the pane card and nav
            // buttons keep their measured ratios (SurfacePanel on SurfaceBase-like ground).
            Image wash = UiFactory.NewImage("PlateWash", backdrop, UiColor.SurfaceBase);
            UiFactory.Fill((RectTransform)wash.transform);
            SetAlpha(wash, 0.55f);
            wash.transform.SetSiblingIndex(2);
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
            _playButton = play;

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

            _profileTab = AddTab(ref stack, column, "PROFILE", UiContext.Profile, "Menu.Profile");
            stack.Gap(UiSpace.S);
            _statisticsTab = AddTab(ref stack, column, "STATISTICS", UiContext.Statistics, "Menu.Statistics");
            stack.Gap(UiSpace.S);
            _achievementsTab = AddTab(ref stack, column, "ACHIEVEMENTS", UiContext.Achievements, "Menu.Achievements");
            stack.Gap(UiSpace.S);
            _settingsTab = AddTab(ref stack, column, "SETTINGS", UiContext.Settings, "Menu.Settings");

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

            // Explicit vertical chain (WP-022 gamepad): PLAY -> tabs -> QUIT, no wrap - Up
            // from PLAY must never land on QUIT. Automatic navigation would also reach the
            // pane's sliders from a tab, which is what the horizontal axis is for.
            UiInput.ChainVertical(false, play, _profileTab, _statisticsTab, _achievementsTab, _settingsTab, quit);

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

        private UiButton AddTab(ref UiStack stack, RectTransform column, string label, UiContext pane, string icon)
        {
            UiButton button = UiFactory.Button("Tab." + pane, column, label, UiButtonVariant.Nav,
                () => ShowPane(pane));
            stack.Add((RectTransform)button.transform, NavButtonHeight);
            UiInput.LeadingIcon(button, icon, UiColor.TextSecondary, 24f);
            return button;
        }

        // ==================================================================== panes

        private void BuildPaneHost(RectTransform column)
        {
            RectTransform card = UiFactory.FramedCard("PaneCard", column, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
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
                _settingsTab.SetSelectedChoice(_pane == UiContext.Settings);
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

                case UiContext.Settings:
                    BuildSettingsPane();
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

        // ------------------------------------------------------------ settings pane

        /// <summary>
        /// Settings (WP-022): three knobs the UI owns the presentation of and
        /// <see cref="UiSettings"/> owns the values of. Sliders are the same control the
        /// pause screen uses for camera shake, so keyboard and gamepad already drive them.
        /// </summary>
        private void BuildSettingsPane()
        {
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading("SETTINGS", _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.S);

            Text caption = UiFactory.Label(
                "Caption", _paneHost,
                "Device preferences. They are saved on this machine and never touch your profile.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)caption.transform, UiFactory.LineHeight(UiType.Caption));
            stack.Gap(UiSpace.XL);

            // Readouts are updated in place by the change handlers: the pane is NOT
            // rebuilt on a settings change, or the slider under the cursor would be
            // destroyed mid-drag.
            Slider scale = AddSettingSlider(
                ref stack, "UI SCALE", "Menu.Graphics",
                UiTheme.UiScaleMin, UiTheme.UiScaleMax, UiSettings.UiScale,
                v => Mathf.RoundToInt(v * 100f) + "%",
                v => UiSettings.UiScale = Mathf.Round(v * 20f) / 20f);

            Slider volume = AddSettingSlider(
                ref stack, "MASTER VOLUME", "Menu.Audio",
                0f, 1f, UiSettings.MasterVolume,
                v => Mathf.RoundToInt(v * 100f) + "%",
                v => UiSettings.MasterVolume = Mathf.Round(v * 20f) / 20f);

            int qualityCount = Mathf.Max(1, QualitySettings.names.Length);
            Slider quality = AddSettingSlider(
                ref stack, "GRAPHICS QUALITY", "Menu.Settings",
                0f, qualityCount - 1, UiSettings.Quality,
                v => UiSettings.QualityName(Mathf.RoundToInt(v)),
                v => UiSettings.Quality = Mathf.RoundToInt(v));
            quality.wholeNumbers = true;

            stack.Gap(UiSpace.L);
            Text note = UiFactory.Label(
                "Note", _paneHost,
                "UI scale applies to the next screen you open. Use the arrow keys or the left stick on a slider.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

            UiInput.ChainVertical(scale, volume, quality);
            UiInput.Focus(scale);
        }

        private Slider AddSettingSlider(
            ref UiStack stack, string label, string icon, float min, float max, float value,
            System.Func<float, string> readout, System.Action<float> onChanged)
        {
            RectTransform row = UiFactory.NewRect("Setting." + label, _paneHost);
            stack.Add(row, UiTokens.TargetCompact + UiFactory.LineHeight(UiType.Label) + (int)UiSpace.XS);
            stack.Gap(UiSpace.L);

            Image glyph = UiArt.IconImage("Icon", row, icon, UiColor.TextSecondary, 22f);
            RectTransform glyphRect = (RectTransform)glyph.transform;
            glyphRect.anchorMin = new Vector2(0f, 1f);
            glyphRect.anchorMax = new Vector2(0f, 1f);
            glyphRect.pivot = new Vector2(0f, 1f);
            glyphRect.anchoredPosition = Vector2.zero;

            Text title = UiFactory.Label("Label", row, label, UiType.Label, UiColor.TextMuted, TextAnchor.UpperLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Label));
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.offsetMin = new Vector2(glyph.enabled ? 22f + (int)UiSpace.S : 0f, titleRect.offsetMin.y);

            Text valueText = UiFactory.Label(
                "Value", row, readout(value), UiType.Label, UiColor.TextPrimary, TextAnchor.UpperRight);
            RectTransform valueRect = UiFactory.Fill((RectTransform)valueText.transform);
            valueRect.anchorMin = new Vector2(0f, 1f);
            valueRect.pivot = new Vector2(0f, 1f);
            valueRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Label));
            valueRect.anchoredPosition = Vector2.zero;

            Slider slider = UiFactory.NewSlider("Slider", row, min, max, value);
            RectTransform sliderRect = (RectTransform)slider.transform;
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 0f);
            sliderRect.pivot = new Vector2(0.5f, 0f);
            sliderRect.sizeDelta = new Vector2(0f, UiTokens.TargetCompact);
            sliderRect.anchoredPosition = Vector2.zero;
            slider.onValueChanged.AddListener(v =>
            {
                onChanged(v);
                valueText.text = readout(v);
            });
            return slider;
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

            // WP-022: AP-012 medallion - epic sakura ring when earned, plain common ring
            // when not - at the leading edge; text starts after it.
            float medalSize = AchievementRowHeight - (int)UiSpace.L * 2;
            Image medal = UiFactory.NewImage(
                "Medal", content, line.Earned ? UiColor.StateSuccess : UiColor.TextMuted,
                UiArt.Medal(line.Earned ? UiMedal.Epic : UiMedal.Common));
            medal.preserveAspect = true;
            var medalRect = (RectTransform)medal.transform;
            medalRect.anchorMin = new Vector2(0f, 0.5f);
            medalRect.anchorMax = new Vector2(0f, 0.5f);
            medalRect.pivot = new Vector2(0f, 0.5f);
            medalRect.sizeDelta = new Vector2(medalSize, medalSize);
            medalRect.anchoredPosition = Vector2.zero;
            float textLeft = medalSize + (int)UiSpace.M;

            Text title = UiFactory.Label(
                "Title", content, line.Title, UiType.H3,
                line.Earned ? UiColor.TextPrimary : UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMax = new Vector2(-ChipColumnWidth, 0f);
            titleRect.offsetMin = new Vector2(textLeft, AchievementTitleBand);

            // TextSecondary, not TextMuted: muted measures 4.27:1 on SurfaceRaised,
            // which is below the 4.5 floor, and an earned achievement's card IS raised.
            Text description = UiFactory.Label(
                "Description", content,
                UiFactory.Clip(line.Description, 90),
                UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform descriptionRect = UiFactory.Fill((RectTransform)description.transform);
            descriptionRect.offsetMin = new Vector2(textLeft, 0f);
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
