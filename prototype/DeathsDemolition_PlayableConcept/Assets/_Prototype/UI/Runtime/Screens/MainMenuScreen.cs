using System.Collections.Generic;
using System.IO;
using DD.Prototype.Achievements;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Inputs;
using DD.Prototype.Profile;
using TMPro;
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

        /// <summary>Achievement cards per page (WP-022D: the pane pages instead of summarising).</summary>
        private const int MaxAchievementRows = 5;
        private const float PagerHeight = 40f;
        private const float OptionHeight = 30f;

        /// <summary>Achievements pane page (0-based); survives a redraw, resets on scene change.</summary>
        private int _achievementPage;

        /// <summary>Option button to re-focus after a profile-changed redraw (title / badge pick).</summary>
        private string _refocusName;

        /// <summary>True only while ShowPane builds (SHOULD-6 pad focus rule).</summary>
        private bool _focusPaneOnOpen;

        private RectTransform _paneHost;
        private UiContext _pane = UiContext.Profile;

        private UiButton _profileTab;
        private UiButton _statisticsTab;
        private UiButton _achievementsTab;
        private UiButton _settingsTab;
        private UiButton _controlsTab;
        private UiButton _accessibilityTab;

        /// <summary>WP-022G: the remap row currently listening ("PRESS A KEY"), for the redraw after the result.</summary>
        private string _rebindingEntryId;
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

        protected override void OnDestroy()
        {
            base.OnDestroy();
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

            TMP_Text playHint = UiFactory.Label(
                "PlayHint", column,
                "Choose a skin and a classic weapon, then fight for the highest round you can reach.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)playHint.transform, UiFactory.LineHeight(UiType.Caption) * 3f);

            stack.Gap(UiSpace.XL);

            TMP_Text sectionLabel = UiFactory.Label(
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
            stack.Gap(UiSpace.S);
            _controlsTab = AddTab(ref stack, column, "CONTROLS", UiContext.Controls, "Menu.Controls");
            stack.Gap(UiSpace.S);
            _accessibilityTab = AddTab(ref stack, column, "ACCESSIBILITY", UiContext.Accessibility, "Menu.Accessibility"); // AD v006 MUST-2 → AP-012 v003 person-in-circle glyph (gamepad stays with CONTROLS)

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
            UiInput.ChainVertical(false, play, _profileTab, _statisticsTab, _achievementsTab, _settingsTab, _controlsTab, _accessibilityTab, quit);

            TMP_Text build = UiFactory.Label(
                "BuildNote", column,
                // WP-025 version stamp (WP-022B binding): "wp-022-…-g1234567 · release · 2026-09-16".
                "Prototype build · " + DD.Prototype.Core.Build.BuildInfo.Read().Label,
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
            _focusPaneOnOpen = true;
            RedrawPane();
            _focusPaneOnOpen = false;
            UiEvents.Raise(UiEvent.PaneOpened, pane.ToString());
        }

        private void RedrawPane()
        {
            if (_profileTab != null)
            {
                _profileTab.SetSelectedChoice(_pane == UiContext.Profile);
                _statisticsTab.SetSelectedChoice(_pane == UiContext.Statistics);
                _achievementsTab.SetSelectedChoice(_pane == UiContext.Achievements);
                _settingsTab.SetSelectedChoice(_pane == UiContext.Settings);
                _controlsTab.SetSelectedChoice(_pane == UiContext.Controls);
                _accessibilityTab.SetSelectedChoice(_pane == UiContext.Accessibility);
            }

            ClearChildren(_paneHost);

            // The pane's controls are about to be destroyed: no tab may keep pointing
            // Right at one of them (an explicit link to a destroyed Selectable throws
            // inside the EventSystem on the next Right press).
            ClearRightLink(_profileTab);
            ClearRightLink(_statisticsTab);
            ClearRightLink(_achievementsTab);
            ClearRightLink(_settingsTab);
            ClearRightLink(_controlsTab);
            ClearRightLink(_accessibilityTab);

            ProfileData profile = ProfileService.Current;

            switch (_pane)
            {
                case UiContext.Statistics:
                    BuildStatisticsPane(StatisticsViewModel.From(profile));
                    break;

                case UiContext.Achievements:
                    {
                        // WP-022D: the WP-035 progress source when the service has one
                        // (progress, rarity, "new"); the WP-005 definition list otherwise.
                        IAchievementProgressSource progress = ProfileService.AchievementProgressSource;
                        BuildAchievementsPane(progress != null
                            ? AchievementsViewModel.From(progress)
                            : AchievementsViewModel.From(profile, ProfileService.AllAchievements));
                        if (progress != null)
                        {
                            // After the build: this visit still shows its NEW chips; the
                            // flags clear for the next one (persists with the next save).
                            progress.MarkAchievementsSeen();
                        }

                        break;
                    }

                case UiContext.Settings:
                    BuildSettingsPane();
                    break;

                case UiContext.Controls:
                    BuildControlsPane();
                    break;

                case UiContext.Accessibility:
                    BuildAccessibilityPane();
                    break;

                default:
                    {
                        BuildProfilePane(ProfileViewModel.From(profile, ProfileServiceLookup.Instance));
                        ICosmeticsSource cosmetics = ProfileService.CosmeticsSource;
                        if (cosmetics != null)
                        {
                            cosmetics.MarkContentSeen();
                        }

                        break;
                    }
            }

            if (!string.IsNullOrEmpty(_refocusName))
            {
                Transform again = _paneHost.Find(_refocusName);
                _refocusName = null;
                UiButton button = again != null ? again.GetComponent<UiButton>() : null;
                if (button != null)
                {
                    UiInput.Focus(button);
                }
            }
        }

        private static void ClearRightLink(Selectable control)
        {
            if (control == null)
            {
                return;
            }

            UnityEngine.UI.Navigation navigation = control.navigation;
            if (navigation.selectOnRight != null)
            {
                navigation.selectOnRight = null;
                control.navigation = navigation;
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

            TMP_Text caption = UiFactory.Label(
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

            // Reduced motion moved to the ACCESSIBILITY pane (WP-022G); Settings keeps the
            // device preferences.

            // Run telemetry (WP-022E, WP-045): local files only, no identity data; the
            // player can still switch it off. Same toggle-button pattern as reduced motion.
            stack.Gap(UiSpace.S);
            UiButton telemetry = AddToggleRow(ref stack, _paneHost, "Telemetry", "RUN TELEMETRY",
                () => (UiSettings.Telemetry ? "ON" : "OFF", UiSettings.Telemetry),
                () => { UiSettings.Telemetry = !UiSettings.Telemetry; return (UiSettings.Telemetry ? "ON" : "OFF", UiSettings.Telemetry); }, "Telemetry");
            AddCaptionUnder(ref stack, _paneHost, "TelemetryNote",
                "Run telemetry writes one local file per run (rounds, kills, weapon use) for tuning. It never contains your profile or name.");

            TMP_Text note = UiFactory.Label(
                "Note", _paneHost,
                "UI scale applies to the next screen you open. Use the arrow keys or the left stick on a slider.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

            UiInput.ChainVertical(scale, volume, quality, telemetry);
            UiInput.Focus(scale);
        }

        private static string ReducedMotionLabel()
        {
            return UiSettings.ReducedMotion ? "REDUCED MOTION · ON" : "REDUCED MOTION · OFF";
        }

        private Slider AddSettingSlider(
            ref UiStack stack, string label, string icon, float min, float max, float value,
            System.Func<float, string> readout, System.Action<float> onChanged)
        {
            return AddSettingSliderIn(ref stack, _paneHost, label, icon, min, max, value, readout, onChanged);
        }

        private static Slider AddSettingSliderIn(
            ref UiStack stack, RectTransform parent, string label, string icon, float min, float max, float value,
            System.Func<float, string> readout, System.Action<float> onChanged)
        {
            RectTransform row = UiFactory.NewRect("Setting." + label, parent);
            stack.Add(row, UiTokens.TargetCompact + UiFactory.LineHeight(UiType.Label) + (int)UiSpace.XS);
            stack.Gap(UiSpace.L);

            Image glyph = null;
            if (!string.IsNullOrEmpty(icon))
            {
                glyph = UiArt.IconImage("Icon", row, icon, UiColor.TextSecondary, 22f);
                RectTransform glyphRect = (RectTransform)glyph.transform;
                glyphRect.anchorMin = new Vector2(0f, 1f);
                glyphRect.anchorMax = new Vector2(0f, 1f);
                glyphRect.pivot = new Vector2(0f, 1f);
                glyphRect.anchoredPosition = Vector2.zero;
            }

            TMP_Text title = UiFactory.Label("Label", row, label, UiType.Label, UiColor.TextMuted, TextAnchor.UpperLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Label));
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.offsetMin = new Vector2(glyph != null && glyph.enabled ? 22f + (int)UiSpace.S : 0f, titleRect.offsetMin.y);

            TMP_Text valueText = UiFactory.Label(
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
                UiEvents.Raise(UiEvent.SliderChanged, label);
            });
            return slider;
        }

        // ------------------------------------------------------------ controls pane

        private const float RemapRowHeight = 40f;   // AD v006 MUST-5: 40 px slot, 48 px pitch
        private const float RemapButtonWidth = 220f;

        /// <summary>
        /// WP-022G · key / button rebinding on the WP-024 <see cref="IInputRemapService"/>
        /// plus the gamepad options (stick dead zone, aim assist level, rumble) from
        /// <see cref="InputPreferencesStore"/>. Two columns: keyboard slots, gamepad slots.
        /// A slot is a Nav-style button showing the effective control; Submit starts the
        /// interactive rebind ("PRESS A KEY"), the service's callback redraws the pane.
        /// </summary>
        private void BuildControlsPane()
        {
            var stack = new UiStack(_paneHost);
            stack.Add(UiFactory.Heading("CONTROLS", _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XS);
            TMP_Text intro = UiFactory.Label(
                "Intro", _paneHost, "Select a slot and press the key or button you want. Escape cancels. Overrides are saved on this machine.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)intro.transform, UiFactory.LineHeight(UiType.Caption));
            stack.Gap(UiSpace.M);

            IInputRemapService remap = PrototypeInputInstaller.Instance != null ? PrototypeInputInstaller.Instance.Remap : null;
            var focusables = new List<Selectable>(16);

            RectTransform columns = UiFactory.NewRect("Columns", _paneHost);
            stack.Add(columns, ControlsColumnsHeight);
            RectTransform left = UiFactory.NewRect("Keyboard", columns);
            UiFactory.Fill(left);
            left.anchorMax = new Vector2(0.5f, 1f);
            left.offsetMax = new Vector2(-(int)UiSpace.M, 0f);
            RectTransform right = UiFactory.NewRect("Gamepad", columns);
            UiFactory.Fill(right);
            right.anchorMin = new Vector2(0.5f, 0f);
            right.offsetMin = new Vector2((int)UiSpace.M, 0f);

            var leftStack = new UiStack(left);
            var rightStack = new UiStack(right);
            var leftButtons = new List<Selectable>(8);
            var rightButtons = new List<Selectable>(8);
            AddRemapColumn(ref leftStack, "KEYBOARD", DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse, remap, leftButtons);
            AddRemapColumn(ref rightStack, "GAMEPAD", DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad, remap, rightButtons);

            // Gamepad options under the gamepad column.
            InputPreferences prefs = InputPreferencesStore.Current;
            rightStack.Gap(UiSpace.M);
            Slider deadzone = AddSettingSliderIn(ref rightStack, right, "STICK DEAD ZONE", null, 0f, 0.5f, prefs.StickDeadzone,
                v => Mathf.RoundToInt(v * 100f) + "%", v => UpdatePreferences(p => p.StickDeadzone = v));
            // AD v006 MUST-6: AIM ASSIST as a cycle toggle (Off → Light → Full), MUST-1 recipe.
            UiButton assist = AddToggleRow(ref rightStack, right, "AimAssist", "AIM ASSIST",
                () => AimAssistState(),
                () => { UpdatePreferences(p => p.AimAssist = (InputAimAssistLevel)(((int)p.AimAssist + 1) % 3)); return AimAssistState(); }, "AimAssist");   // WP-059: the level is WP-024 data
            rightStack.Gap(UiSpace.S);
            UiButton rumble = AddToggleRow(ref rightStack, right, "Rumble", "RUMBLE",
                () => (InputPreferencesStore.Current.RumbleEnabled ? "ON" : "OFF", InputPreferencesStore.Current.RumbleEnabled),
                () => { UpdatePreferences(p => p.RumbleEnabled = !p.RumbleEnabled); return (InputPreferencesStore.Current.RumbleEnabled ? "ON" : "OFF", InputPreferencesStore.Current.RumbleEnabled); }, "Rumble");
            rightButtons.Add(deadzone);
            rightButtons.Add(assist);
            rightButtons.Add(rumble);

            // AD v006 MUST-7: reset as a full-width footer row under both columns, the
            // overrides file name right-aligned on the same row.
            stack.Gap(UiSpace.XL);
            RectTransform footer = UiFactory.NewRect("Footer", _paneHost);
            stack.Add(footer, UiTokens.TargetCompact);
            UiButton reset = UiFactory.Button("ResetBindings", footer, "RESET ALL BINDINGS TO DEFAULTS", UiButtonVariant.Secondary, () =>
            {
                if (remap != null)
                {
                    remap.ResetToDefaults();
                }

                UiEvents.Raise(UiEvent.Confirm, "ResetBindings");
                _refocusName = "Footer/ResetBindings";
                RedrawPane();
            });
            RectTransform resetRect = (RectTransform)reset.transform;
            resetRect.anchorMin = new Vector2(0f, 0.5f);
            resetRect.anchorMax = new Vector2(0f, 0.5f);
            resetRect.pivot = new Vector2(0f, 0.5f);
            resetRect.sizeDelta = new Vector2(420f, UiTokens.TargetCompact);
            resetRect.anchoredPosition = Vector2.zero;
            TMP_Text file = UiFactory.Label(
                "File", footer, remap != null ? "Overrides: " + Path.GetFileName(remap.OverridesFilePath) : "Input service not running.",
                UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleRight);
            RectTransform fileRect = UiFactory.Fill((RectTransform)file.transform);
            fileRect.offsetMin = new Vector2(440f, 0f);
            leftButtons.Add(reset);

            UiInput.ChainVertical(false, leftButtons.ToArray());
            UiInput.ChainVertical(false, rightButtons.ToArray());
            for (int i = 0; i < leftButtons.Count; i++)
            {
                UiInput.LinkHorizontal(leftButtons[i], _controlsTab, rightButtons[Mathf.Min(i, rightButtons.Count - 1)]);
            }

            for (int i = 0; i < rightButtons.Count; i++)
            {
                UiInput.LinkHorizontal(rightButtons[i], leftButtons[Mathf.Min(i, leftButtons.Count - 1)], null);
            }

            if (leftButtons.Count > 0)
            {
                UiInput.LinkHorizontal(_controlsTab, null, leftButtons[0]);
                UiInput.LinkVertical(reset, leftButtons.Count > 1 ? leftButtons[leftButtons.Count - 2] : null, null);
                if (rightButtons.Count > 0)
                {
                    UiInput.LinkVertical(rightButtons[rightButtons.Count - 1], null, reset);
                }

                if (_focusPaneOnOpen)
                {
                    UiInput.Focus(leftButtons[0]); // AD v006 MUST-8
                }
            }
        }

        private const float ControlsColumnsHeight = 500f; // 9 slots × 48 + heading; gamepad column shorter

        private void AddRemapColumn(ref UiStack stack, string heading, DD.Prototype.Core.InputAbstraction.InputSchemeKind scheme, IInputRemapService remap, List<Selectable> buttons)
        {
            TMP_Text label = UiFactory.Label("Label", stack.Parent, heading, UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)label.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.XS);

            if (remap == null)
            {
                return;
            }

            IReadOnlyList<InputRemapEntry> entries = remap.Entries;
            int shown = 0;
            for (int i = 0; i < entries.Count && shown < MaxRemapRows; i++)
            {
                InputRemapEntry e = entries[i];
                if (e.Scheme != scheme || e.Map != PrototypeInputNames.MapGameplay || e.Action == PrototypeInputNames.ActionDebugPanel)
                {
                    continue;
                }

                if (e.Action == PrototypeInputNames.ActionAim || e.Action == PrototypeInputNames.ActionAimStick)
                {
                    continue; // pointer / stick axes are not rebindable slots
                }

                shown++;
                RectTransform row = UiFactory.NewRect("Slot." + e.Id, stack.Parent);
                stack.Add(row, RemapRowHeight);
                stack.Gap(UiSpace.S); // 48 px pitch

                TMP_Text name = UiFactory.Label("Action", row, RemapRowName(e), UiType.Body, UiColor.TextPrimary, TextAnchor.MiddleLeft);
                RectTransform nameRect = UiFactory.Fill((RectTransform)name.transform);
                nameRect.offsetMax = new Vector2(-RemapButtonWidth - (int)UiSpace.S, 0f);

                bool listening = _rebindingEntryId == e.Id;
                string entryId = e.Id;
                UiButton slot = UiFactory.Button("Bind." + e.Id, row, string.Empty, UiButtonVariant.Secondary, null);
                slot.SetOnClick(() => StartRebind(remap, entryId));
                RectTransform slotRect = (RectTransform)slot.transform;
                slotRect.anchorMin = new Vector2(1f, 0.5f);
                slotRect.anchorMax = new Vector2(1f, 0.5f);
                slotRect.pivot = new Vector2(1f, 0.5f);
                slotRect.sizeDelta = new Vector2(RemapButtonWidth, RemapRowHeight);
                slotRect.anchoredPosition = Vector2.zero;

                // AD v006 MUST-5: the control sits in a WP-022F key cap inside the slot
                // (inset + hairline, TextPrimary, 12 px left inset); an overridden slot adds
                // a sakura 6 px diamond after the cap (never colour alone); a listening slot
                // reads PRESS A KEY in StateWarning with the threat-chip outline.
                string capText = listening ? "PRESS A KEY" : e.DisplayName;
                TMP_Text cap = UiFactory.Chip("Cap", slot.transform, capText,
                    listening ? UiColor.SurfaceBase : UiColor.SurfaceInset,
                    listening ? UiColor.StateWarning : (e.IsOverridden ? UiColor.AccentSakura : UiColor.TextPrimary),
                    Mathf.Max(UiInputGlyphs.MinCapWidth, 20f + capText.Length * 9f), UiInputGlyphs.DefaultSize);
                cap.fontStyle = TMPro.FontStyles.Bold;
                RectTransform capRect = (RectTransform)cap.transform.parent;
                capRect.anchorMin = new Vector2(0f, 0.5f);
                capRect.anchorMax = new Vector2(0f, 0.5f);
                capRect.pivot = new Vector2(0f, 0.5f);
                capRect.anchoredPosition = new Vector2(12f, 0f);
                var capEdge = capRect.gameObject.AddComponent<UnityEngine.UI.Outline>();
                capEdge.effectDistance = new Vector2(1f, 1f);
                capEdge.useGraphicAlpha = true;
                Color edgeColor = UiTokens.Get(listening ? UiColor.StateWarning : UiColor.BorderHairline);
                if (listening) edgeColor.a = 0.6f;
                capEdge.effectColor = edgeColor;
                if (e.IsOverridden && !listening)
                {
                    Image diamond = UiFactory.NewImage("Overridden", slot.transform, UiColor.AccentSakura, UiSprites.Solid);
                    RectTransform diamondRect = (RectTransform)diamond.transform;
                    diamondRect.anchorMin = new Vector2(0f, 0.5f);
                    diamondRect.anchorMax = new Vector2(0f, 0.5f);
                    diamondRect.pivot = new Vector2(0.5f, 0.5f);
                    diamondRect.sizeDelta = new Vector2(6f, 6f);
                    diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    diamondRect.anchoredPosition = new Vector2(12f + capRect.sizeDelta.x + 8f + 3f, 0f);
                }

                buttons.Add(slot);
            }
        }

        private const int MaxRemapRows = 10; // Gameplay map: Move ×4, Fire, Reload, Pause, Special, Interact (9 on keyboard)

        private static string RemapRowName(InputRemapEntry e)
        {
            string action = e.Action == PrototypeInputNames.ActionMove ? "Move" : e.Action;
            if (!string.IsNullOrEmpty(e.CompositePart))
            {
                string part = e.CompositePart;
                return action + " " + char.ToUpperInvariant(part[0]) + part.Substring(1);
            }

            return action;
        }

        private void StartRebind(IInputRemapService remap, string entryId)
        {
            if (remap == null || remap.IsRebinding)
            {
                return;
            }

            _rebindingEntryId = entryId;
            _refocusName = null;
            RedrawPane();
            bool started = remap.StartRebind(entryId, (result, entry) =>
            {
                _rebindingEntryId = null;
                UiEvents.Raise(result == InputRebindResult.Completed ? UiEvent.Confirm : UiEvent.Back, "Rebind");
                if (this != null && _paneHost != null && _pane == UiContext.Controls)
                {
                    _refocusName = FindSlotPath(entryId);
                    RedrawPane();
                }
            });

            if (!started)
            {
                _rebindingEntryId = null;
                RedrawPane();
            }
        }

        private string FindSlotPath(string entryId)
        {
            Transform t = _paneHost.Find("Columns/Keyboard/Slot." + entryId + "/Bind." + entryId);
            if (t == null)
            {
                t = _paneHost.Find("Columns/Gamepad/Slot." + entryId + "/Bind." + entryId);
            }

            return t != null ? "Columns/" + t.parent.parent.name + "/" + t.parent.name + "/" + t.name : null;
        }

        private static void UpdatePreferences(System.Action<InputPreferences> edit)
        {
            var next = new InputPreferences();
            next.CopyFrom(InputPreferencesStore.Current);
            edit(next);
            InputPreferencesStore.Apply(next);
        }

        private static (string, bool) AimAssistState()
        {
            InputAimAssistLevel level = InputPreferencesStore.Current.AimAssist;   // WP-059: InputPreferences.AimAssistLevel is the source
            return (AimAssistLevels.Label(level), level != InputAimAssistLevel.Off);
        }

        // ------------------------------------------------------- accessibility pane

        /// <summary>
        /// WP-022G · accessibility: colour-blind safe palette, reading-text scale, flash
        /// reduction, reduced motion (moved here from Settings, which keeps device
        /// preferences). Every value lives in ui-settings.json.
        /// </summary>
        private void BuildAccessibilityPane()
        {
            var stack = new UiStack(_paneHost);
            stack.Add(UiFactory.Heading("ACCESSIBILITY", _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XS);
            TMP_Text intro = UiFactory.Label(
                "Intro", _paneHost, "These change how the game is presented, never how it plays. Text scale applies to the next screen you open; the palette to the next screen and the next run.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)intro.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
            stack.Gap(UiSpace.L);

            // AD v006 MUST-1/3/4: toggle rows (label + state chip), 40 / 8 / caption / 24
            // rhythm, and an in-place preview under PALETTE and TEXT SCALE.
            Image[] swatches = null;
            UiButton palette = AddToggleRow(ref stack, _paneHost, "Palette", "PALETTE",
                () => PaletteState(), () => { UiSettings.ColorPalette = UiSettings.ColorPalette == 1 ? 0 : 1; TintSwatches(swatches); return PaletteState(); }, "Palette");
            stack.Gap(UiSpace.S);
            swatches = AddPaletteSwatches(ref stack, _paneHost);
            AddCaptionUnder(ref stack, _paneHost, "PaletteNote",
                "Colour-blind safe moves health, earned and unlocked states from green to blue. Danger stays vermilion; every state also has a shape and a word.");

            TMP_Text sample = null;
            Slider text = AddSettingSlider(ref stack, "TEXT SCALE", null, UiSettings.TextScaleMin, UiSettings.TextScaleMax, UiSettings.TextScale,
                v => Mathf.RoundToInt(v * 100f) + "%", v =>
                {
                    UiSettings.TextScale = v;
                    if (sample != null)
                    {
                        sample.fontSize = Mathf.RoundToInt(14f * v);
                        sample.text = "Caption sample · 14 px → " + Mathf.RoundToInt(14f * v) + " px at " + Mathf.RoundToInt(v * 100f) + " %";
                    }
                });
            sample = UiFactory.Label("TextSample", _paneHost, "Caption sample · 14 px → " + Mathf.RoundToInt(14f * UiSettings.TextScale) + " px at " + Mathf.RoundToInt(UiSettings.TextScale * 100f) + " %", UiType.Caption, UiColor.TextSecondary);
            sample.fontSize = Mathf.RoundToInt(14f * UiSettings.TextScale);
            stack.Add((RectTransform)sample.transform, UiFactory.LineHeight(UiType.Caption) * 1.5f);
            stack.Gap(UiSpace.XL);

            UiButton flashes = AddToggleRow(ref stack, _paneHost, "ReduceFlashes", "REDUCE FLASHES",
                () => (UiSettings.ReduceFlashes ? "ON" : "OFF", UiSettings.ReduceFlashes),
                () => { UiSettings.ReduceFlashes = !UiSettings.ReduceFlashes; return (UiSettings.ReduceFlashes ? "ON" : "OFF", UiSettings.ReduceFlashes); }, "ReduceFlashes");
            AddCaptionUnder(ref stack, _paneHost, "FlashNote",
                "Damage and final-seconds flashes at 40%. Blood and the world are not changed. Camera shake has its own slider on the pause screen.");

            UiButton reduced = AddToggleRow(ref stack, _paneHost, "ReducedMotion", "REDUCED MOTION",
                () => (UiSettings.ReducedMotion ? "ON" : "OFF", UiSettings.ReducedMotion),
                () => { UiSettings.ReducedMotion = !UiSettings.ReducedMotion; return (UiSettings.ReducedMotion ? "ON" : "OFF", UiSettings.ReducedMotion); }, "ReducedMotion");
            AddCaptionUnder(ref stack, _paneHost, "MotionNote",
                "Pulses hold still, screen fades are instant and the menu petals stop drifting.", 1);

            UiInput.ChainVertical(false, palette, text, flashes, reduced);
            UiInput.LinkHorizontal(_accessibilityTab, null, palette);
            UiInput.LinkHorizontal(palette, _accessibilityTab, null);
            UiInput.LinkHorizontal(text, _accessibilityTab, null);
            UiInput.LinkHorizontal(flashes, _accessibilityTab, null);
            UiInput.LinkHorizontal(reduced, _accessibilityTab, null);
            if (_focusPaneOnOpen)
            {
                UiInput.Focus(palette); // AD v006 MUST-8: focus lands on the first control
            }
        }

        private static (string, bool) PaletteState()
        {
            return UiSettings.ColorPalette == 1 ? ("COLOUR-BLIND SAFE", true) : ("DEFAULT", false);
        }

        private static readonly UiColor[] SwatchTokens = { UiColor.BarHealth, UiColor.StateSuccess, UiColor.StateWarning, UiColor.StateDanger };
        private static readonly string[] SwatchNames = { "HEALTH", "EARNED", "WARNING", "DANGER" };

        /// <summary>AD v006 MUST-4: four 24 px chips filled from the CURRENT palette tokens, the word inside each; retinted the frame the palette flips.</summary>
        private static Image[] AddPaletteSwatches(ref UiStack stack, RectTransform parent)
        {
            RectTransform row = UiFactory.NewRect("Swatches", parent);
            stack.Add(row, StateChipHeight);
            var images = new Image[SwatchTokens.Length];
            for (int i = 0; i < SwatchTokens.Length; i++)
            {
                TMP_Text chip = UiFactory.Chip("Swatch." + SwatchNames[i], row, SwatchNames[i], SwatchTokens[i], UiColor.SurfaceBase, 96f, StateChipHeight);
                chip.fontStyle = TMPro.FontStyles.Bold;
                RectTransform rect = (RectTransform)chip.transform.parent;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(20f + i * (96f + (int)UiSpace.S), 0f);
                images[i] = rect.GetComponent<Image>();
            }

            return images;
        }

        private static void TintSwatches(Image[] swatches)
        {
            for (int i = 0; swatches != null && i < swatches.Length; i++)
            {
                if (swatches[i] != null)
                {
                    swatches[i].color = UiTokens.Get(SwatchTokens[i]);
                }
            }
        }

        // --------------------------------------------------------- toggle rows (AD v006)

        private const float ToggleRowHeight = 40f;
        private const float StateChipHeight = 24f;
        private const float StateChipMinWidth = 72f;

        /// <summary>
        /// AD v006 MUST-1 · a cycle toggle as a row: label left (Body, TextPrimary), the
        /// current value as a state chip on the right (OFF/DEFAULT: inset + hairline +
        /// TextSecondary; ON: StateSuccess fill + ink). The whole row is the button, so
        /// pad Submit and a click both cycle it; <paramref name="cycle"/> returns the new
        /// (chip text, isOn) after advancing.
        /// </summary>
        private static UiButton AddToggleRow(ref UiStack stack, RectTransform parent, string name, string label,
            System.Func<(string text, bool on)> current, System.Func<(string text, bool on)> cycle, string eventDetail)
        {
            UiButton button = UiFactory.Button(name, parent, label, UiButtonVariant.Secondary, null);
            stack.Add((RectTransform)button.transform, ToggleRowHeight);
            button.LabelText.alignment = TMPro.TextAlignmentOptions.Left;
            button.LabelText.fontSize = UiTokens.FontSize(UiType.Body);
            button.LabelText.color = UiTokens.Get(UiColor.TextPrimary);
            RectTransform labelRect = (RectTransform)button.LabelText.transform;
            labelRect.offsetMin = new Vector2(20f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-(ChipWidthFor("COLOUR-BLIND SAFE") + 16f + (int)UiSpace.M), labelRect.offsetMax.y);

            (string text, bool on) state = current();
            TMP_Text chip = UiFactory.Chip("State", button.transform, state.text,
                state.on ? UiColor.StateSuccess : UiColor.SurfaceInset,
                state.on ? UiColor.SurfaceBase : UiColor.TextSecondary,
                Mathf.Max(StateChipMinWidth, ChipWidthFor(state.text)), StateChipHeight);
            chip.fontStyle = TMPro.FontStyles.Bold;
            RectTransform chipRect = (RectTransform)chip.transform.parent;
            chipRect.anchorMin = new Vector2(1f, 0.5f);
            chipRect.anchorMax = new Vector2(1f, 0.5f);
            chipRect.pivot = new Vector2(1f, 0.5f);
            chipRect.anchoredPosition = new Vector2(-16f, 0f);
            var edge = chipRect.gameObject.AddComponent<UnityEngine.UI.Outline>();
            edge.effectDistance = new Vector2(1f, 1f);
            edge.useGraphicAlpha = true;
            ApplyStateChip(chipRect, chip, edge, state);

            button.SetOnClick(() =>
            {
                (string text, bool on) next = cycle();
                ApplyStateChip(chipRect, chip, edge, next);
                UiEvents.Raise(UiEvent.ToggleChanged, eventDetail);
            });
            return button;
        }

        /// <summary>Caption 14 bold at +2 tracking measures ≈ 10.5 px per capital; 28 px of padding.</summary>
        private static float ChipWidthFor(string text)
        {
            return Mathf.Max(StateChipMinWidth, 28f + text.Length * 10.5f);
        }

        private static void ApplyStateChip(RectTransform chipRect, TMP_Text chip, UnityEngine.UI.Outline edge, (string text, bool on) state)
        {
            chip.text = state.text;
            chip.color = UiTokens.Get(state.on ? UiColor.SurfaceBase : UiColor.TextSecondary);
            chipRect.GetComponent<Image>().color = UiTokens.Get(state.on ? UiColor.StateSuccess : UiColor.SurfaceInset);
            edge.effectColor = state.on ? new Color(0f, 0f, 0f, 0f) : UiTokens.Get(UiColor.BorderHairline);
            chipRect.sizeDelta = new Vector2(Mathf.Max(StateChipMinWidth, ChipWidthFor(state.text)), StateChipHeight);
        }

        private static TMP_Text AddCaptionUnder(ref UiStack stack, RectTransform parent, string name, string text, int lines = 2)
        {
            stack.Gap(UiSpace.S);
            TMP_Text caption = UiFactory.Label(name, parent, text, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)caption.transform, UiFactory.LineHeight(UiType.Caption) * lines);
            stack.Gap(UiSpace.XL);
            return caption;
        }

        // ------------------------------------------------------------- profile pane

        private void BuildProfilePane(ProfileViewModel model)
        {
            var stack = new UiStack(_paneHost);

            stack.Add(UiFactory.Heading(ProfileViewModel.Title, _paneHost), UiFactory.LineHeight(UiType.H2));

            // WP-022E name plate: the chosen title · badge on the heading's right, in the
            // sakura accent - the one place the pick is shown next to "the name".
            string plate = CosmeticsViewModel.NamePlate(ProfileService.CosmeticsSource);
            if (!string.IsNullOrEmpty(plate))
            {
                TMP_Text plateText = UiFactory.Label(
                    "NamePlate", _paneHost, plate, UiType.BodyStrong, UiColor.AccentSakura, TextAnchor.MiddleRight);
                RectTransform plateRect = (RectTransform)plateText.transform;
                plateRect.anchorMin = new Vector2(0.5f, 1f);
                plateRect.anchorMax = new Vector2(1f, 1f);
                plateRect.pivot = new Vector2(1f, 1f);
                plateRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.H2));
                plateRect.anchoredPosition = new Vector2(0f, 0f);
            }

            stack.Gap(UiSpace.L); // WP-022D: the pane gained a two-column band; gaps above it tightened by 32 px

            if (!string.IsNullOrEmpty(model.Notice))
            {
                // Error state, stated rather than hidden. The game is fully playable;
                // only the previous profile was lost, and it was kept on disk.
                stack.Add(BuildNotice(model.Notice, UiColor.StateWarning), 72f);
                stack.Gap(UiSpace.L);
            }

            // "PROFILE LEVEL", not "LEVEL": the eyebrow says at first glance that this
            // number is the profile's experience, not a character's power.
            TMP_Text levelLabel = UiFactory.Label(
                "LevelLabel", _paneHost, "PROFILE LEVEL", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)levelLabel.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.XS);

            TMP_Text level = UiFactory.Numeric(
                "Level", _paneHost, model.LevelText, UiType.NumericXL, UiColor.TextPrimary);
            stack.Add((RectTransform)level.transform, UiFactory.LineHeight(UiType.NumericXL));

            stack.Gap(UiSpace.M);

            UiBar xpBar = UiFactory.Bar("XpBar", _paneHost, UiColor.BarXp, 14f);
            xpBar.SetValue(model.Progress01, true);
            stack.Add((RectTransform)xpBar.transform, 14f);

            stack.Gap(UiSpace.S);

            TMP_Text xpCaption = UiFactory.Label(
                "XpCaption", _paneHost, model.ProgressCaption, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)xpCaption.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.L);
            stack.Add((RectTransform)UiFactory.Divider("D1", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            for (int i = 0; i < model.Headline.Count; i++)
            {
                AddRow(ref stack, model.Headline[i]);
            }

            stack.Gap(UiSpace.M);
            stack.Add((RectTransform)UiFactory.Divider("D2", _paneHost).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            // WP-022D: below the second divider the pane splits in two columns - unlocked
            // content (weapons / branches, WP-005) on the left, the WP-035 title / badge
            // picker on the right - so the picker fits the card without scrolling.
            ICosmeticsSource cosmeticsSource = ProfileService.CosmeticsSource;
            CosmeticsViewModel cosmetics = cosmeticsSource != null ? CosmeticsViewModel.From(cosmeticsSource) : null;
            bool twoColumns = cosmetics != null && !cosmetics.IsEmpty;

            RectTransform columns = UiFactory.NewRect("Columns", _paneHost);
            stack.Add(columns, ProfileColumnsHeight);
            RectTransform left = UiFactory.NewRect("Left", columns);
            UiFactory.Fill(left);
            if (twoColumns)
            {
                left.anchorMax = new Vector2(ProfileLeftColumn, 1f);
                left.offsetMax = new Vector2(-(int)UiSpace.XL, 0f); // 24 px gutter (AD v004 MUST-4)
            }

            var leftStack = new UiStack(left);
            TMP_Text unlockLabel = UiFactory.Label(
                "UnlockLabel", left, "UNLOCKED CONTENT", UiType.Label, UiColor.TextMuted);
            leftStack.Add((RectTransform)unlockLabel.transform, UiFactory.LineHeight(UiType.Label));
            leftStack.Gap(UiSpace.S);

            for (int i = 0; i < model.Unlocks.Count; i++)
            {
                AddUnlockRow(ref leftStack, model.Unlocks[i]);
            }

            leftStack.Gap(UiSpace.M);

            TMP_Text achievements = UiFactory.Label(
                "AchievementSummary", left, model.AchievementSummary, UiType.Caption, UiColor.TextSecondary);
            leftStack.Add((RectTransform)achievements.transform, UiFactory.LineHeight(UiType.Caption));

            leftStack.Gap(UiSpace.S);

            TMP_Text rule = UiFactory.Label(
                "Rule", left, ProfileViewModel.RuleNote, UiType.Caption, UiColor.TextMuted);
            leftStack.Add((RectTransform)rule.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

            if (twoColumns)
            {
                RectTransform right = UiFactory.NewRect("Right", columns);
                UiFactory.Fill(right);
                right.anchorMin = new Vector2(ProfileLeftColumn, 0f);
                var rightStack = new UiStack(right);
                BuildCosmeticsPicker(ref rightStack, cosmetics);
            }
        }

        /// <summary>Height of the profile pane's two-column band (unlocks | picker); 1080p card budget.</summary>
        private const float ProfileColumnsHeight = 340f;
        private const float ProfileLeftColumn = 0.6f; // AD v004 MUST-4: 60 / 40

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
                TMP_Text caption = UiFactory.Label(
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

            TMP_Text note = UiFactory.Label(
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
                TMP_Text title = UiFactory.Label(
                    "EmptyTitle", _paneHost, AchievementsViewModel.EmptyTitle, UiType.H3, UiColor.TextPrimary);
                stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H3));
                stack.Gap(UiSpace.S);
                stack.Add(BuildNotice(AchievementsViewModel.EmptyBody, UiColor.TextSecondary), 72f);
                return;
            }

            TMP_Text progress = UiFactory.Label(
                "Progress", _paneHost, model.Progress, UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)progress.transform, UiFactory.LineHeight(UiType.Body));

            stack.Gap(UiSpace.L);

            // WP-022D: pages of MaxAchievementRows in catalog order (27 entries, WP-023);
            // PREVIOUS / NEXT are ordinary buttons, so pad and keyboard page the same way.
            int pages = Mathf.Max(1, (model.Lines.Count + MaxAchievementRows - 1) / MaxAchievementRows);
            _achievementPage = Mathf.Clamp(_achievementPage, 0, pages - 1);
            int first = _achievementPage * MaxAchievementRows;
            int last = Mathf.Min(model.Lines.Count, first + MaxAchievementRows);
            for (int i = first; i < last; i++)
            {
                stack.Add(BuildAchievementRow(model.Lines[i]), AchievementRowHeight);
                stack.Gap(UiSpace.S);
            }

            if (pages > 1)
            {
                stack.Gap(UiSpace.XS);
                RectTransform pager = UiFactory.NewRect("Pager", _paneHost);
                stack.Add(pager, PagerHeight);

                UiButton previous = UiFactory.Button("Pager.Previous", pager, "PREVIOUS", UiButtonVariant.Secondary, () => TurnAchievementPage(-1));
                RectTransform previousRect = (RectTransform)previous.transform;
                previousRect.anchorMin = new Vector2(0f, 0.5f);
                previousRect.anchorMax = new Vector2(0f, 0.5f);
                previousRect.pivot = new Vector2(0f, 0.5f);
                previousRect.sizeDelta = new Vector2(160f, PagerHeight);
                previousRect.anchoredPosition = Vector2.zero;
                previous.SetInteractable(_achievementPage > 0);

                UiButton next = UiFactory.Button("Pager.Next", pager, "NEXT", UiButtonVariant.Secondary, () => TurnAchievementPage(1));
                RectTransform nextRect = (RectTransform)next.transform;
                nextRect.anchorMin = new Vector2(1f, 0.5f);
                nextRect.anchorMax = new Vector2(1f, 0.5f);
                nextRect.pivot = new Vector2(1f, 0.5f);
                nextRect.sizeDelta = new Vector2(160f, PagerHeight);
                nextRect.anchoredPosition = Vector2.zero;
                next.SetInteractable(_achievementPage < pages - 1);

                TMP_Text page = UiFactory.Label(
                    "Page", pager, "Page " + (_achievementPage + 1) + " of " + pages, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleCenter);
                RectTransform pageRect = UiFactory.Fill((RectTransform)page.transform);
                pageRect.offsetMin = new Vector2(168f, 0f);
                pageRect.offsetMax = new Vector2(-168f, 0f);

                UiInput.ChainHorizontal(previous, next);
                UiInput.LinkHorizontal(_achievementsTab, null, _achievementPage > 0 ? previous : next);
                UiInput.LinkHorizontal(previous, _achievementsTab, next);
                UiInput.LinkHorizontal(next, _achievementPage > 0 ? previous : _achievementsTab, null);

                // AD v004 SHOULD-6: opening the pane from the nav lands on NEXT (the rows are
                // not focusable - nothing to claim); a redraw caused by a profile change or
                // a page turn keeps its own focus rule.
                if (_focusPaneOnOpen && UiInputScheme.Current == DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad)
                {
                    UiInput.Focus(next);
                }
                stack.Gap(UiSpace.S);
            }

            TMP_Text note = UiFactory.Label(
                "Note", _paneHost, AchievementsViewModel.AutoNote, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption));
        }

        private void TurnAchievementPage(int delta)
        {
            _achievementPage += delta;
            _refocusName = delta < 0 ? "Pager/Pager.Previous" : "Pager/Pager.Next";
            UiEvents.Raise(UiEvent.PaneOpened, "AchievementsPage");
            RedrawPane();
        }

        // ---------------------------------------------------------- cosmetics picker

        /// <summary>
        /// WP-022D: "PROFILE TITLE" and "PROFILE BADGE" option rows. Each option is a Nav
        /// button (selected marker = the current choice); a locked option is not
        /// interactable and says how it opens. Selecting writes through
        /// <see cref="ICosmeticsSource.SelectTitle"/> / <see cref="ICosmeticsSource.SelectBadge"/>;
        /// the profile's Changed event redraws the pane and focus returns to the option.
        /// </summary>
        private void BuildCosmeticsPicker(ref UiStack stack, CosmeticsViewModel model)
        {
            if (model.IsEmpty)
            {
                return;
            }

            var options = new List<Selectable>(model.Titles.Count + model.Badges.Count + 2);
            AddCosmeticGroup(ref stack, CosmeticsViewModel.TitlesLabel, "Title", model.Titles, model.SelectedTitle, options);
            if (model.Badges.Count > 0)
            {
                stack.Gap(UiSpace.S);
                stack.Add((RectTransform)UiFactory.Divider("D3", stack.Parent).transform, UiTokens.StrokeHairline);
                stack.Gap(UiSpace.S);
                AddCosmeticGroup(ref stack, CosmeticsViewModel.BadgesLabel, "Badge", model.Badges, model.SelectedBadge, options);
            }

            if (options.Count > 0)
            {
                UiInput.ChainVertical(false, options.ToArray());
                UiInput.LinkHorizontal(_profileTab, null, options[0]);
                for (int i = 0; i < options.Count; i++)
                {
                    UiInput.LinkHorizontal(options[i], _profileTab, null);
                }
            }

        }

        private void AddCosmeticGroup(ref UiStack stack, string heading, string kind, List<UiCosmeticLine> lines, string selectedId, List<Selectable> options)
        {
            TMP_Text label = UiFactory.Label("Label." + kind, stack.Parent, heading, UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)label.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.XS);

            // "None" first: clearing is a choice too (empty id clears, per the contract).
            AddCosmeticOption(ref stack, kind, string.Empty, CosmeticsViewModel.NoneOption, string.Empty, true, false, string.IsNullOrEmpty(selectedId), options);
            for (int i = 0; i < lines.Count; i++)
            {
                UiCosmeticLine line = lines[i];
                AddCosmeticOption(ref stack, kind, line.Id, line.Title, line.Detail, line.Unlocked, line.IsNew, line.Selected, options);
            }
        }

        private void AddCosmeticOption(ref UiStack stack, string kind, string id, string title, string detail, bool unlocked, bool isNew, bool selected, List<Selectable> options)
        {
            string name = "Option." + kind + "." + (string.IsNullOrEmpty(id) ? "None" : id);
            bool isTitle = kind == "Title";
            // "Blooded", not "Title: Blooded": the group heading already says the kind (AD v004 MUST-2 rule).
            int colon = title.IndexOf(": ", System.StringComparison.Ordinal);
            string optionTitle = colon > 0 && colon < 12 ? title.Substring(colon + 2) : title;
            UiButton button = UiFactory.Button(name, stack.Parent, optionTitle, UiButtonVariant.Nav, () =>
            {
                ICosmeticsSource source = ProfileService.CosmeticsSource;
                bool ok = source != null && (isTitle ? source.SelectTitle(id) : source.SelectBadge(id));
                if (ok)
                {
                    _refocusName = name;
                    UiEvents.Raise(UiEvent.ChoiceSelected, kind);
                    UiSound.Play(UiSoundKind.Confirm);
                    if (_paneHost != null && _paneHost.Find(name) != null)
                    {
                        // Same choice again (no Changed event): nothing to redraw.
                        _refocusName = null;
                    }
                }
                else
                {
                    UiSound.Play(UiSoundKind.Denied);
                }
            });
            stack.Add((RectTransform)button.transform, OptionHeight);
            stack.Gap(UiSpace.XS);
            button.LabelText.fontSize = UiTokens.FontSize(UiType.Body); // AD v004 MUST-4: names at Body 18
            button.SetSelectedChoice(selected);
            button.SetInteractable(unlocked);
            options.Add(button);

            // Right-hand chip: NEW (pulse) for a fresh unlock, LOCKED when it is not open
            // yet (the contract's Detail is a description, not the condition - the
            // Achievements pane says how), nothing when it is simply available.
            if (!unlocked || isNew)
            {
                TMP_Text chip = UiFactory.Chip(
                    "State", button.transform,
                    isNew ? "NEW" : "LOCKED",
                    isNew ? UiColor.AccentSakura : UiColor.SurfaceInset,
                    isNew ? UiColor.SurfaceBase : UiColor.TextSecondary,
                    isNew ? 56f : 88f, UiFactory.RowChipHeight);
                RectTransform chipRect = (RectTransform)chip.transform.parent;
                chipRect.anchorMin = new Vector2(1f, 0.5f);
                chipRect.anchorMax = new Vector2(1f, 0.5f);
                chipRect.pivot = new Vector2(1f, 0.5f);
                chipRect.anchoredPosition = new Vector2(-(int)UiSpace.S, 0f);
                if (isNew)
                {
                    chipRect.gameObject.AddComponent<UiPulse>().Configure(
                        new Graphic[] { chipRect.GetComponent<Graphic>() }, 0.80f, 1f, UiTokens.PulsePeriod);
                }
            }
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
            // WP-022D: the medallion is the WP-023 rarity (Common ring / Rare wreath /
            // Epic sakura), jade when earned, muted while in progress.
            Image medal = UiFactory.NewImage(
                "Medal", content, line.Earned ? UiColor.StateSuccess : UiColor.TextMuted,
                UiArt.Medal(MedalForRarity(line.Rarity)));
            medal.preserveAspect = true;
            var medalRect = (RectTransform)medal.transform;
            medalRect.anchorMin = new Vector2(0f, 0.5f);
            medalRect.anchorMax = new Vector2(0f, 0.5f);
            medalRect.pivot = new Vector2(0f, 0.5f);
            medalRect.sizeDelta = new Vector2(medalSize, medalSize);
            medalRect.anchoredPosition = Vector2.zero;
            float textLeft = medalSize + (int)UiSpace.M;

            TMP_Text title = UiFactory.Label(
                "Title", content, line.Title, UiType.H3,
                line.Earned ? UiColor.TextPrimary : UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMax = new Vector2(-ChipColumnWidth, 0f);
            titleRect.offsetMin = new Vector2(textLeft, AchievementTitleBand);

            // TextSecondary, not TextMuted: muted measures 4.27:1 on SurfaceRaised,
            // which is below the 4.5 floor, and an earned achievement's card IS raised.
            TMP_Text description = UiFactory.Label(
                "Description", content,
                UiFactory.Clip(line.Description, 90),
                UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform descriptionRect = UiFactory.Fill((RectTransform)description.transform);
            descriptionRect.offsetMin = new Vector2(textLeft, line.Earned ? 0f : ProgressBand);
            descriptionRect.offsetMax = new Vector2(-ChipColumnWidth, -AchievementTitleBand);

            if (!line.Earned)
            {
                // WP-022D (WP-035): lifetime progress under the description - bar plus
                // "7 / 10" so the number and the length agree. Instant: no per-frame work.
                UiBar bar = UiFactory.Bar("Progress", content, UiColor.BarXp, 10f); // AD v004 MUST-3: a readable bar on its track
                bar.SetValue(line.Progress01, true);
                RectTransform barRect = (RectTransform)bar.transform;
                barRect.anchorMin = new Vector2(0f, 0f);
                barRect.anchorMax = new Vector2(1f, 0f);
                barRect.pivot = new Vector2(0f, 0f);
                barRect.offsetMin = new Vector2(textLeft, 2f);
                barRect.offsetMax = new Vector2(-ChipColumnWidth, 12f);

                // "2 / 5": the current value in TextPrimary, the maximum in TextSecondary (rich text, card build only).
                TMP_Text counter = UiFactory.Label(
                    "Counter", content, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.LowerRight);
                counter.richText = true;
                counter.text = "<color=#" + ColorUtility.ToHtmlStringRGB(UiTokens.Get(UiColor.TextPrimary)) + ">" + line.Current + "</color> / " + line.Threshold;
                RectTransform counterRect = (RectTransform)counter.transform;
                counterRect.anchorMin = new Vector2(1f, 0f);
                counterRect.anchorMax = new Vector2(1f, 0f);
                counterRect.pivot = new Vector2(1f, 0f);
                counterRect.sizeDelta = new Vector2(ChipColumnWidth - (int)UiSpace.S, UiFactory.LineHeight(UiType.Caption));
                counterRect.anchoredPosition = new Vector2(0f, 0f);
            }

            TMP_Text chip = UiFactory.Chip(
                "State", content,
                line.Earned ? (line.IsNew ? "NEW" : "EARNED") : "IN PROGRESS",
                line.Earned ? (line.IsNew ? UiColor.AccentSakura : UiColor.StateSuccess) : UiColor.SurfaceInset,
                line.Earned ? (line.IsNew ? UiColor.SurfaceBase : UiColor.SurfaceInset) : UiColor.TextSecondary, // NEW = ink on sakura (AD v004 MUST-1)
                140f, UiFactory.CardChipHeight); // 140: "IN PROGRESS" at Caption tracking needs it (120 clipped the last S)
            if (line.IsNew)
            {
                chip.transform.parent.gameObject.AddComponent<UiPulse>().Configure(
                    new Graphic[] { chip.transform.parent.GetComponent<Graphic>() }, 0.80f, 1f, UiTokens.PulsePeriod);
            }

            RectTransform chipRect = (RectTransform)chip.transform.parent;
            chipRect.anchorMin = new Vector2(1f, 0.5f);
            chipRect.anchorMax = new Vector2(1f, 0.5f);
            chipRect.pivot = new Vector2(1f, 0.5f);
            chipRect.anchoredPosition = Vector2.zero;

            return row;
        }

        /// <summary>Lower band of an in-progress achievement card reserved for the progress bar.</summary>
        private const float ProgressBand = 16f;

        private static UiMedal MedalForRarity(AchievementRarity rarity)
        {
            switch (rarity)
            {
                case AchievementRarity.Epic: return UiMedal.Epic;
                case AchievementRarity.Rare:
                case AchievementRarity.Uncommon: return UiMedal.Rare;
                default: return UiMedal.Common;
            }
        }

        // =================================================================== helpers

        private void AddRow(ref UiStack stack, UiStatLine line)
        {
            TMP_Text valueText = UiFactory.StatRow(
                line.Label, line.Value, _paneHost, line.Color,
                line.Emphasis ? UiType.H3 : UiType.BodyStrong);
            stack.Add((RectTransform)valueText.transform.parent, RowHeight);
        }

        private void AddUnlockRow(ref UiStack stack, UiUnlockLine line)
        {
            RectTransform row = UiFactory.NewRect("Unlock", stack.Parent);

            TMP_Text label = UiFactory.Label(
                "Label", row, line.Title, UiType.Body,
                line.Unlocked ? UiColor.TextPrimary : UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.offsetMax = new Vector2(-ValueColumnWidth, 0f);

            TMP_Text chip = UiFactory.Chip(
                "State", row,
                line.Unlocked ? "UNLOCKED" : "LEVEL " + line.RequiredLevel,
                UiColor.SurfaceInset,
                // BarHealth (moss), not StateSuccess (jade): one green in the theme (AD SHOULD-7);
                // Moss400 on SurfaceInset measures 5.1:1.
                line.Unlocked ? UiColor.BarHealth : UiColor.TextSecondary,
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

            TMP_Text text = UiFactory.Label("Text", row, message, UiType.Body, accent, TextAnchor.UpperLeft);
            RectTransform textRect = UiFactory.Fill((RectTransform)text.transform);
            textRect.offsetMin = new Vector2(UiTokens.HeadingTickWidth + (int)UiSpace.M, 0f);

            return row;
        }
    }
}
