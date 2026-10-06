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
    ///
    /// MENU v4 (WP-081, the user's G5 05.10: O1 brush ink, "less text, more icons"). The left third is a calm dark spine:
    /// the name on two lines, then one vertical rail of icon controls - PLAY, the six panes, QUIT - whose focused item
    /// shows its word beside it (PLAY adds "CHOOSE SKIN & WEAPON"); every icon carries its reader name from the string
    /// table. The rest of the page is the world. A pane opens on the right when its icon is chosen and closes on Cancel;
    /// none is open on arrival (the G5 decision: Profile closed). The profile level stands top-right with its XP bar.
    /// </summary>
    public sealed class MainMenuScreen : UiScreen
    {
        // Element measures. Every one is a multiple of the 8pt base, and every GAP on
        // this screen is a UiSpace member - the two together are the whole spacing spec.
        private const float RowHeight = 40f;
        private const float AchievementRowHeight = 96f;
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
        private UiBackdrop _plates;
        public const float HeaderBlockMargin = 96f;   // AD 6ab MUST-2: title right edge + 96

        // The spine words that stand on the menu plate (12b · D1: UiTranslucentTokensTests lays them out and measures each
        // over the brightest plate texel under it). WP-068 TR: pack words in the chosen language (properties, so a language
        // change reads the new column); the title is the game's name and is never translated.
        public const string HeaderTitle = "DEATHS DEMOLITION";
        public static string PlayWord => UiStrings.Get("menu.play", "PLAY");
        public static string PlaySubline => UiStrings.Get("menu.play_sub", "CHOOSE SKIN & WEAPON");
        public static string BuildNotePrefix => UiStrings.Get("menu.build_prefix", "Prototype build · ");

        /// <summary>v4 rail: the glyph size, the pitch between icons (8 px grid), and the air between an icon and its word.
        /// ikon v2 B5a (AD MUST-2, R3): L - the O1 glyphs sit in the viewBox frame, and at M their ink was 18-25 px (median
        /// 22.2, the AP-012 rail ~29-32); at L the ink's long edge is 21.5-30.8 px, median 27.7 (4:3: 20.8 physical). The
        /// smallest pitch is L's hit (56), so the hits never overlap.</summary>
        public const UiIconSize RailIcon = UiIconSize.L;
        public const float RailPitchMax = 72f;
        public const float RailPitchMin = 56f;
        public const UiSpace RailWordGap = UiSpace.L;

        /// <summary>
        /// 12b · D1 (T10: "TextSecondary ≥ 4.5:1 with the scrim" - the dimmest text the plate carries): the spine's
        /// captions (PLAY hint, PROGRESS, the build line) stand on the plate, not on a card, so they are TextSecondary.
        /// In linear light TextMuted measured 3.2:1 on S-05's clamped third at the fixed 0.55 scrim (AD §14 MUST-5).
        /// </summary>
        public const UiColor PlateCaption = UiColor.TextSecondary;

        /// <summary>The open pane; <see cref="UiContext.MainMenu"/> = none (v4: the menu opens with every pane closed).</summary>
        private UiContext _pane = UiContext.MainMenu;

        private UiButton _profileTab;
        private UiButton _statisticsTab;
        private UiButton _achievementsTab;
        private UiButton _settingsTab;
        private UiButton _controlsTab;
        private UiButton _accessibilityTab;

        /// <summary>WP-022G: the remap row currently listening ("PRESS A KEY"), for the redraw after the result.</summary>
        private string _rebindingEntryId;
        private UiButton _playButton;

        // WP-068 TR: the menu's standing words, relabelled in place when the language changes.
        private UiButton _quitButton;
        private TMP_Text _buildNote;

        // v4: the rail, the word beside its focused item, the open pane's tick, the pane's card, the level readout.
        private readonly List<UiIconButton> _rail = new List<UiIconButton>(8);
        private UiIconButton _railFocus;
        private RectTransform _railWordRect;
        private TMP_Text _railWord;
        private TMP_Text _railSub;
        private Image _paneMark;
        private Image _railPad;
        private RectTransform _paneCard;
        private TMP_Text _level;
        private UiBar _xp;
        private UiStepper _languageRow;

        /// <summary>The rail's icon controls, top to bottom (tests).</summary>
        public IReadOnlyList<UiIconButton> Rail
        {
            get { return _rail; }
        }

        /// <summary>The word beside the focused rail item, and its sub-line (PLAY only; empty otherwise).</summary>
        public TMP_Text RailWord
        {
            get { return _railWord; }
        }

        public TMP_Text RailSub
        {
            get { return _railSub; }
        }

        /// <summary>The pane card is on screen (a pane is open).</summary>
        public bool PaneOpen
        {
            get { return _paneCard != null && _paneCard.gameObject.activeSelf; }
        }

        /// <summary>The pane on screen (<see cref="UiContext.MainMenu"/> when none). Exposed for tests; the source of truth is <see cref="UiNavigation"/>.</summary>
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

            float top = BuildTitle() + (int)UiSpace.XXL;

            RectTransform nav = Column("Nav", 0f, 0.26f, top);
            BuildRail(nav);
            CoverColumn(nav);   // 12b · D1: the text-safe scrim reaches the spine's right edge (the title stands in it too)
            BuildProgressReadout();
            BuildPaneHost(Column("Pane", 0.29f, 1f, top));

            ShowPane(_pane);

            ProfileService.Changed += OnProfileChanged;

            // WP-081 (the user's G5 addition, AP-028's five shots): the real world as the backdrop; the plates until it shows.
            MenuCinematic.Begin(gameObject, OnWorldLive, () => PaneOpen);
        }

        /// <summary>
        /// WP-081: the live world is under the menu (MenuCinematic has moved the screens to overlays) - the base fill goes,
        /// the plates fade over the blend (at once behind the curtain or with Reduced Motion); the text-safe scrim stays.
        /// </summary>
        private void OnWorldLive()
        {
            Transform backdrop = ScreenRect.Find("Backdrop");
            Transform baseFill = backdrop != null ? backdrop.Find("Base") : null;
            if (baseFill != null)
            {
                baseFill.gameObject.SetActive(false);
            }

            if (_plates != null)
            {
                bool cut = UiCurtain.IsCovering || UiMotion.ReducedMotion;
                _plates.ShowWorld(cut ? 0f : MenuCinematic.Blend, Time.unscaledTime);
            }

            // The spine over the world: the scrim and the words' ink as the menu look says (C-00 05.10 options).
            Transform title = Content.Find("Title");
            Transform nav = Content.Find("Nav");
            MenuLook.Current.ApplyUi(nav, _plates);
            MenuLook.Current.ApplyUi(title, null);
            Transform progress = Content.Find("Progress");
            // The title's and the level readout's ground: the top corners' lifts (else the readout keeps its pad).
            bool cornerLift = MenuLook.Current.LiftCorners(title as RectTransform, progress as RectTransform, _plates);
            MenuLook.ApplyReadoutInk(progress, MenuLook.Current.Pads && !cornerLift);
            if (MenuLook.Current.Pads && _railWordRect != null)
            {
                // The focused word and its sub-line stand on the world past the scrim's darkest part: their own soft pad,
                // sized to the words and moved with them (MoveRailWord).
                _railPad = MenuLook.AddPad(_railWordRect, _railWordRect.Find(MenuLook.ReadoutPadName));
                SizeRailPad();
                if (_buildNote != null && _buildNote.gameObject.activeSelf)
                {
                    MenuLook.PadBehind(_buildNote);   // the dev build's version line stands on the world too
                }
            }
            if (_plates != null)
            {
                return;
            }

            Transform plate = backdrop != null ? backdrop.Find("MenuPlate") : null;
            if (plate != null)
            {
                plate.gameObject.SetActive(false);
            }
        }

        /// <summary>v4: the name alone at the top of the spine, on as many lines as the spine's width needs (two at 1080).</summary>
        private float BuildTitle()
        {
            TMP_Text title = UiFactory.Label("Title", Content, HeaderTitle, UiType.Display, UiColor.TextPrimary, TextAnchor.LowerLeft);
            title.enableWordWrapping = true;
            RectTransform rect = title.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            // Words stay whole: the title is the spine's width, or its longest word's where the spine is narrower (UI 1.2
            // on the Mac host: "DEMOLITION" outruns a 366 px spine - TMP would break it as "DEMOLITI / ON").
            float width = Mathf.Max(Content.rect.width * 0.26f, LongestWord(title, HeaderTitle) + 2f);
            float line = UiFactory.LineHeight(UiType.Display);
            float height = width > 1f ? Mathf.Max(line, Mathf.Ceil(title.GetPreferredValues(HeaderTitle, width, 0f).y)) : line * 2f;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
            return height;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ProfileService.Changed -= OnProfileChanged;
        }

        /// <summary>
        /// Gamepad / keyboard Cancel (WP-022, v4): an open pane closes and focus returns to its icon on the rail; with no pane
        /// open the focus returns to PLAY. Nothing destructive is bound to Cancel here - QUIT stays a deliberate press.
        /// </summary>
        private void Update()
        {
            if (!UiInput.CancelPressed())
            {
                return;
            }

            if (_pane != UiContext.MainMenu)
            {
                UiButton icon = TabOf(_pane);
                ShowPane(UiContext.MainMenu);
                UiInput.Focus(icon != null ? icon : _playButton);
            }
            else
            {
                UiInput.Focus(_playButton);
            }
        }

        private UiButton TabOf(UiContext pane)
        {
            switch (pane)
            {
                case UiContext.Profile: return _profileTab;
                case UiContext.Statistics: return _statisticsTab;
                case UiContext.Achievements: return _achievementsTab;
                case UiContext.Settings: return _settingsTab;
                case UiContext.Controls: return _controlsTab;
                case UiContext.Accessibility: return _accessibilityTab;
                default: return null;
            }
        }

        /// <summary>
        /// The AP-012 menu plate (DEC-076 atmosphere) under the page, with the procedural
        /// backdrop's veils kept on top of it so text contrast is measured against the
        /// same dark ground as before. Without the atlas the procedural backdrop stands.
        /// </summary>
        private void BuildMenuPlate()
        {
            Transform backdrop = ScreenRect.Find("Backdrop");
            if (backdrop == null)
            {
                return;
            }

            // WP-068 slice 6 (E2 / §6.1): the Look B plate stack (parallax, lantern breath, rain,
            // text-safe scrim) right above the base fill; the veils and petals stay over it.
            UiBackdrop plates = UiBackdrop.Create((RectTransform)backdrop);
            if (plates != null)
            {
                plates.transform.SetSiblingIndex(1);
                _plates = plates;

                // AD 6ab MUST-1 / MUST-3: the plates are already Look B graded - no v2 wisteria /
                // ground veil over them, and no v2 petals until the A3 single-leaf glyph.
                foreach (Transform child in backdrop)
                {
                    if (child.name == "SkyVeil" || child.name == "GroundVeil" || child.name == "Petal")
                    {
                        child.gameObject.SetActive(false);
                    }
                }

                return;
            }

            // Without MenuPlates (an art-less build): the v2 single plate and its wash.
            Sprite plate = UiArt.MenuPlate();
            if (plate == null)
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


        /// <summary>12b · D1: the text-safe scrim reaches the spine's right edge (+ 12 parallax) where an ultrawide page puts it past the third.</summary>
        private void CoverColumn(RectTransform column)
        {
            if (_plates == null || column == null)
            {
                return;
            }

            var plateRect = (RectTransform)_plates.transform;
            var corners = new Vector3[4];
            column.GetWorldCorners(corners);
            float right = plateRect.InverseTransformPoint(corners[2]).x - plateRect.rect.xMin;
            _plates.CoverColumn(right + UiBackdrop.NearParallax);
        }

        private void OnProfileChanged()
        {
            RefreshProgressReadout();
            if (_paneHost != null && _pane != UiContext.MainMenu)
            {
                RedrawPane();
            }
        }

        // ============================================================== navigation

        /// <summary>
        /// v4 (O1): the spine is one rail - PLAY, the six panes, QUIT - of icon controls a grid pitch apart (up to 72, never
        /// under L's hit, 56), the build line at the foot. The focused item's word stands beside it, the open pane's icon carries the
        /// heading tick. Explicit vertical chain, no wrap: Up from PLAY never lands on QUIT.
        /// </summary>
        private void BuildRail(RectTransform column)
        {
            float captionLine = UiFactory.LineHeight(UiType.Caption);
            float width = column.rect.width;

            // The foot: the build line (WP-025 version stamp, WP-022B binding).
            TMP_Text build = UiFactory.Label(
                "BuildNote", column, BuildNotePrefix + DD.Prototype.Core.Build.BuildInfo.Read().Label, UiType.Caption, PlateCaption);
            _buildNote = build;
            // C-00 05.10: the version stamp shows in development builds only - the release menu carries no build line.
            build.gameObject.SetActive(Debug.isDebugBuild);
            float buildHeight = width > 1f
                ? Mathf.Max(captionLine, Mathf.Ceil(build.GetPreferredValues(build.text, width, 0f).y))
                : captionLine;
            RectTransform buildRect = build.rectTransform;
            buildRect.anchorMin = new Vector2(0f, 0f);
            buildRect.anchorMax = new Vector2(1f, 0f);
            buildRect.pivot = new Vector2(0.5f, 0f);
            buildRect.sizeDelta = new Vector2(0f, buildHeight);
            buildRect.anchoredPosition = Vector2.zero;

            float hit = UiIconTokens.Hit(RailIcon);
            float pitch = RailPitchFor(column.rect.height - buildHeight - (int)UiSpace.L);

            _playButton = AddRail(column, 0, pitch, "Play", "Menu.Play", "menu.play", "PLAY", PrototypeUiFlow.GoToSelection);
            _profileTab = AddRail(column, 1, pitch, "Tab." + UiContext.Profile, "Menu.Profile", "menu.tab.profile", "PROFILE", () => ShowPane(UiContext.Profile));
            _statisticsTab = AddRail(column, 2, pitch, "Tab." + UiContext.Statistics, "Menu.Statistics", "menu.tab.statistics", "STATISTICS", () => ShowPane(UiContext.Statistics));
            _achievementsTab = AddRail(column, 3, pitch, "Tab." + UiContext.Achievements, "Menu.Achievements", "menu.tab.achievements", "ACHIEVEMENTS", () => ShowPane(UiContext.Achievements));
            _settingsTab = AddRail(column, 4, pitch, "Tab." + UiContext.Settings, "Menu.Settings", "menu.tab.settings", "SETTINGS", () => ShowPane(UiContext.Settings));
            _controlsTab = AddRail(column, 5, pitch, "Tab." + UiContext.Controls, "Menu.Controls", "menu.tab.controls", "CONTROLS", () => ShowPane(UiContext.Controls));
            _accessibilityTab = AddRail(column, 6, pitch, "Tab." + UiContext.Accessibility, "Menu.Accessibility", "menu.tab.accessibility", "ACCESSIBILITY", () => ShowPane(UiContext.Accessibility));
            _quitButton = AddRail(column, 7, pitch, "Quit", "Menu.Quit", "menu.quit", "QUIT", PrototypeUiFlow.Quit);
            SetInitialFocus(_playButton);

            // The word beside the focused item: Display face (H2), the PLAY sub-line in the plate's caption colour.
            _railWordRect = UiFactory.NewRect("RailWord", column);
            _railWordRect.anchorMin = new Vector2(0f, 1f);
            _railWordRect.anchorMax = new Vector2(1f, 1f);
            _railWordRect.pivot = new Vector2(0f, 0.5f);
            _railWord = UiFactory.Label("Word", _railWordRect, string.Empty, UiType.H2, UiColor.TextPrimary, TextAnchor.LowerLeft);
            _railWord.enableWordWrapping = false;
            _railSub = UiFactory.Label("Sub", _railWordRect, string.Empty, UiType.Caption, PlateCaption, TextAnchor.UpperLeft);
            _railSub.enableWordWrapping = false;
            float wordLine = UiFactory.LineHeight(UiType.H2);
            float subLine = captionLine;
            _railWordRect.sizeDelta = new Vector2(-(hit + (int)RailWordGap), wordLine + subLine);
            Band(_railWord.rectTransform, 0f, wordLine);
            Band(_railSub.rectTransform, wordLine, subLine);

            // The open pane's mark: the heading tick (the screen's one vermilion detail now PLAY is an icon) left of its icon.
            _paneMark = UiFactory.HeadingTick("PaneMark", column);
            _paneMark.rectTransform.anchorMin = new Vector2(0f, 1f);
            _paneMark.rectTransform.anchorMax = new Vector2(0f, 1f);
            _paneMark.rectTransform.pivot = new Vector2(1f, 0.5f);
            _paneMark.gameObject.SetActive(false);

            MoveRailWord(_rail[0]);
            UiInput.ChainVertical(false, _playButton, _profileTab, _statisticsTab, _achievementsTab, _settingsTab, _controlsTab, _accessibilityTab, _quitButton);
        }

        public const int RailItems = 8;

        /// <summary>The rail's pitch for <paramref name="available"/> px of spine: the grid line that fits eight items, 56-72 (unlaid: 72).</summary>
        public static float RailPitchFor(float available)
        {
            if (available <= 1f)
            {
                return RailPitchMax;
            }

            return Mathf.Clamp(Mathf.Floor(available / RailItems / UiIconTokens.Grid) * UiIconTokens.Grid, RailPitchMin, RailPitchMax);
        }

        private UiButton AddRail(RectTransform column, int index, float pitch, string id, string icon, string nameKey, string fallback, System.Action onClick)
        {
            // O1: bare brush glyphs - no plate at rest (Ghost); focus brings the hollow ring and the travelling brackets.
            UiIconButton item = UiIconControls.Button(column, icon, nameKey, fallback, onClick, RailIcon, null, null, UiIconLabel.Beside, UiButtonVariant.Ghost);
            item.PinObjectName(id);
            var rect = (RectTransform)item.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -(index * pitch + pitch * 0.5f));
            item.Button.LiftChanged += lifted =>
            {
                if (lifted)
                {
                    MoveRailWord(item);
                }
            };
            _rail.Add(item);
            return item.Button;
        }

        /// <summary>The focused rail item's word (and PLAY's sub-line) beside its icon.</summary>
        private void MoveRailWord(UiIconButton item)
        {
            if (item == null || _railWordRect == null)
            {
                return;
            }

            _railFocus = item;
            var icon = (RectTransform)item.transform;
            float hit = icon.sizeDelta.x;
            _railWordRect.anchoredPosition = new Vector2(hit + (int)RailWordGap, icon.anchoredPosition.y);
            _railWord.text = item.ShortLabel;
            _railSub.text = item.Button == _playButton ? PlaySubline : string.Empty;
            SizeRailPad();
        }

        /// <summary>The focused word's pad hugs the words (the wider of the word and its sub-line), not the column.</summary>
        private void SizeRailPad()
        {
            if (_railPad == null)
            {
                return;
            }

            float words = Mathf.Max(_railWord.GetPreferredValues(_railWord.text).x, string.IsNullOrEmpty(_railSub.text) ? 0f : _railSub.GetPreferredValues(_railSub.text).x);
            float reach = MenuLook.PadReach;
            RectTransform rect = _railPad.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(-reach, -reach);
            rect.offsetMax = new Vector2(Mathf.Ceil(words) + reach, reach);
        }

        private static void Band(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, -top);
        }

        private static float LongestWord(TMP_Text text, string words)
        {
            float widest = 0f;
            foreach (string word in words.Split(' '))
            {
                widest = Mathf.Max(widest, text.GetPreferredValues(word).x);
            }

            return Mathf.Ceil(widest);
        }

        /// <summary>v4 (O1): the profile level top-right, its XP to the next level as a bar under it - a word and a value.</summary>
        private void BuildProgressReadout()
        {
            RectTransform root = UiFactory.NewRect("Progress", Content);
            root.anchorMin = new Vector2(1f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            float line = UiFactory.LineHeight(UiType.BodyStrong);
            const float BarHeight = 8f;
            root.sizeDelta = new Vector2(240f, line + (int)UiSpace.S + BarHeight);
            root.anchoredPosition = Vector2.zero;
            _level = UiFactory.Label("Level", root, string.Empty, UiType.BodyStrong, UiColor.TextPrimary, TextAnchor.UpperRight);
            Band(_level.rectTransform, 0f, line);
            UiHudInk.Apply(_level);   // over the world: the HUD's ink halo, no panel
            _xp = UiFactory.Bar("Xp", root, UiColor.BarXp, BarHeight);
            var bar = (RectTransform)_xp.transform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, BarHeight);
            bar.anchoredPosition = new Vector2(0f, -(line + (int)UiSpace.S));
            RefreshProgressReadout();
        }

        private void RefreshProgressReadout()
        {
            if (_level == null)
            {
                return;
            }

            ProfileViewModel model = ProfileViewModel.From(ProfileService.Current, ProfileServiceLookup.Instance);
            _level.text = model.LevelText;
            _xp.SetValue(model.Progress01, true);
        }

        /// <summary>
        /// WP-068 TR · Settings › LANGUAGE: saves the choice (UiSettings sets UiStrings.Language), relabels the menu's standing
        /// words in place and redraws the Settings pane in the new language with focus back on the LANGUAGE row. Every other
        /// screen reads the language when it is next built.
        /// </summary>
        private void ChooseLanguage(string language)
        {
            if (language == UiSettings.Language)
            {
                return;
            }

            UiSettings.Language = language;
            RelabelMenu();
            RedrawPane();
            if (_languageRow != null)
            {
                UiInput.Focus(_languageRow.Button);
            }
        }

        private void RelabelMenu()
        {
            SetLabel(_buildNote != null ? _buildNote.transform : null, BuildNotePrefix + DD.Prototype.Core.Build.BuildInfo.Read().Label);
            foreach (UiIconButton item in _rail)
            {
                item.RefreshText();
            }

            MoveRailWord(_railFocus);
            RefreshProgressReadout();
        }

        private static void SetLabel(Transform label, string text)
        {
            TMP_Text tmp = label != null ? label.GetComponent<TMP_Text>() : null;
            if (tmp != null)
            {
                tmp.text = text;
            }
        }

        private static string[] LanguageNames()
        {
            // Each language names itself: the pack keeps the same word in both columns.
            return new[] { UiStrings.Get("settings.language.en", "ENGLISH"), UiStrings.Get("settings.language.tr", "TURKISH") };
        }

        // ==================================================================== panes

        private void BuildPaneHost(RectTransform column)
        {
            RectTransform card = UiFactory.FramedCard("PaneCard", column, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            UiFactory.Fill(card);
            _paneCard = card;

            // Clipped, so a pane that grows past the card is cut at the card edge rather
            // than spilling over the navigation column.
            _paneHost = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XL));
        }

        /// <summary>
        /// Opens a pane. Idempotent through <see cref="UiNavigation"/>: a second click on
        /// the tab that is already open changes nothing and rebuilds nothing. v4: <see cref="UiContext.MainMenu"/> closes
        /// the open pane (the menu's resting state, the world behind the rail).
        /// </summary>
        public void ShowPane(UiContext pane)
        {
            if (pane == UiContext.MainMenu)
            {
                ClosePane();
                return;
            }

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

        /// <summary>v4: no pane - the card goes, the rail's tick goes, the navigation model leaves the layer.</summary>
        private void ClosePane()
        {
            if (_pane != UiContext.MainMenu)
            {
                UiNavigation.ExitLayer(_pane);
            }

            _pane = UiContext.MainMenu;
            RedrawPane();
        }

        private void RedrawPane()
        {
            if (_paneCard != null)
            {
                _paneCard.gameObject.SetActive(_pane != UiContext.MainMenu);
            }

            UiButton open = TabOf(_pane);
            if (_paneMark != null)
            {
                _paneMark.gameObject.SetActive(open != null);
                if (open != null)
                {
                    var icon = (RectTransform)open.transform;
                    _paneMark.rectTransform.anchoredPosition = new Vector2(-(int)UiSpace.XS, icon.anchoredPosition.y);
                }
            }

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

            if (_pane == UiContext.MainMenu)
            {
                return;
            }

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

        private const float SettingRowHeight = 56f;      // E6: one row per setting
        private const float SettingsColumnWidth = 720f;  // E6: the control column's measure
        private const float SettingLabelWidth = 240f;
        private const float PreviewMinWidth = 240f;
        private const float PreviewHudScale = 1f;        // AD 6ab SHOULD-3: 1:1 × UI scale, clipped by the well (legible, and 90 % / 120 % visibly change)
        private const float PreviewBlockWidth = 420f;    // the HUD health block (RunHudScreen.BlockWidth)
        private const float PreviewBlockHeight = 104f;
        private const float PreviewHudInset = 16f;      // AD 6ab MUST-5: the block's right edge − 16 from the preview's

        private static string[] DisplayModes()
        {
            return new[] { UiStrings.Get("settings.display.windowed", "WINDOWED"), UiStrings.Get("settings.display.fullscreen", "FULLSCREEN") };
        }

        private RectTransform _previewHud;
        private TMP_Text _previewCaption;

        /// <summary>
        /// Settings (WP-022; WP-068 slice 6, direction E6). The device preferences in four
        /// sections:
        /// <list type="bullet">
        /// <item>DISPLAY: mode stepper, UI scale slider with detents;</item>
        /// <item>AUDIO: master volume, UI sounds;</item>
        /// <item>GRAPHICS: quality stepper (discrete levels, not a slider);</item>
        /// <item>DATA: telemetry and hints switches, each with its sentence.</item>
        /// </list>
        /// Rows are 56 px on a ≤ 720 px control column inside a <see cref="UiScrollView"/>. At
        /// TextScale 1.25 or in a short window the pane scrolls and focus pulls the row into
        /// view, so nothing is clipped. The right well previews the HUD at 1:1 in the chosen UI
        /// scale. <see cref="UiSettings"/> owns every value, and readouts update in place (a
        /// rebuild would destroy the control under the pointer mid-drag).
        /// </summary>
        private void BuildSettingsPane()
        {
            var head = new UiStack(_paneHost);
            head.Add(UiFactory.Heading(UiStrings.Get("menu.tab.settings", "SETTINGS"), _paneHost), UiFactory.LineHeight(UiType.H2));
            AddCaptionUnder(ref head, _paneHost, "Caption",
                UiStrings.Get("settings.caption", "Device preferences. They are saved on this machine and never touch your profile."), 1, UiSpace.M);

            RectTransform body;
            float column;
            bool well;
            UiScrollView scroll = BuildControlColumn(head.Cursor, out body, out column, out well);
            RectTransform content = scroll.Content;
            var stack = new UiStack(content);

            AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.display", "DISPLAY"), false);
            UiStepper display = UiStepper.Create("Display", content, UiStrings.Get("settings.row.display_mode", "DISPLAY MODE"), DisplayModes(),
                UiSettings.Fullscreen ? 1 : 0, true, i => UiSettings.Fullscreen = i == 1);   // WP-022M: asynchronous on macOS (UiDisplayMode)
            AddSettingRow(ref stack, display.transform);
            UiSlider scale = UiSlider.Create("Setting.UI SCALE", content, UiStrings.Get("settings.row.ui_scale", "UI SCALE"),
                UiTheme.UiScaleMin, UiTheme.UiScaleMax, UiSettings.UiScale, 0.05f, Percent,
                v =>
                {
                    UiSettings.UiScale = Mathf.Round(v * 20f) / 20f;
                    ShowPreviewScale(UiSettings.UiScale);
                }, SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, scale.transform);
            if (!well)
            {
                // AD 12c SHOULD-6: where the preview well yields (6ab SHOULD-10), its "next screen" caption stays under the slider.
                AddCaptionUnder(ref stack, content, "ScaleNote", UiStrings.Get("settings.note.ui_scale", "UI scale applies to the next screen you open."), 1, UiSpace.S);
            }

            UiSwitch hudFrame = UiSwitch.Create("HudFrame", content, UiStrings.Get("settings.row.hud_frame", "HUD IN A 16:9 FRAME"), UiSettings.HudFrame169,
                on => UiSettings.HudFrame169 = on);   // 12c · B12 (AD 8-R SHOULD-A2)
            AddSettingRow(ref stack, hudFrame.transform);
            AddCaptionUnder(ref stack, content, "HudFrameNote",
                UiStrings.Get("settings.note.hud_frame", "On a screen wider than 16:9, the HUD's corners move in to a centred 16:9 frame."), 1, UiSpace.S);

            // WP-068 TR: the UI language. A change redraws this pane and relabels the menu at once (ChooseLanguage).
            UiStepper language = UiStepper.Create("Language", content, UiStrings.Get("settings.row.language", "LANGUAGE"), LanguageNames(),
                UiSettings.Language == UiStrings.Turkish ? 1 : 0, true, i => ChooseLanguage(i == 1 ? UiStrings.Turkish : UiStrings.English));
            AddSettingRow(ref stack, language.transform);
            _languageRow = language;

            AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.audio", "AUDIO"), true);
            // Volumes are continuous (no detents; ← / → step 5 %), the UI scale has its six stops.
            UiSlider master = UiSlider.Create("Setting.MASTER VOLUME", content, UiStrings.Get("settings.row.master_volume", "MASTER VOLUME"),
                0f, 1f, UiSettings.MasterVolume, 0f, Percent,
                v => UiSettings.MasterVolume = Mathf.Round(v * 20f) / 20f, SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, master.transform);
            // WP-068 U8: UI sounds on their own slider (the Interface bus).
            UiSlider uiSounds = UiSlider.Create("Setting.UI SOUNDS", content, UiStrings.Get("settings.row.ui_sounds", "UI SOUNDS"),
                0f, 1f, UiSettings.UiVolume, 0f, Percent,
                v => UiSettings.UiVolume = Mathf.Round(v * 20f) / 20f, SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, uiSounds.transform);

            AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.graphics", "GRAPHICS"), true);
            UiStepper quality = UiStepper.Create("Setting.GRAPHICS QUALITY", content, UiStrings.Get("settings.row.quality", "QUALITY"), QualityNames(),
                UiSettings.Quality, false, i => UiSettings.Quality = i);
            AddSettingRow(ref stack, quality.transform);

            // M4-R1 N2 (WP-070 §3): the third / first person rows. The mode itself is chosen in Pause (D-13); the look
            // fields are input preferences (input-settings.json, S-01's bridge feeds the camera), the rest ui-settings.
            AddSettingsSection(ref stack, content, UiStrings.Get("camera.title", "CAMERA"), true);
            InputPreferences look = InputPreferencesStore.Current;
            UiSlider lookSpeed = UiSlider.Create("Setting.LOOK SENSITIVITY", content, UiStrings.Get("settings.row.look_sensitivity", "LOOK SENSITIVITY"),
                1f, LookSteps, LookStepOf(look.LookSensitivity), 1f, v => Mathf.RoundToInt(v).ToString(),
                v => UpdatePreferences(p => p.LookSensitivity = LookSensitivityOf(v)), SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, lookSpeed.transform);
            UiSlider stickSpeed = UiSlider.Create("Setting.STICK LOOK SPEED", content, UiStrings.Get("settings.row.stick_look_speed", "STICK LOOK SPEED"),
                InputPreferences.MinStickLookDegreesPerSecond, InputPreferences.MaxStickLookDegreesPerSecond, look.StickLookDegreesPerSecond, 10f,
                v => Mathf.RoundToInt(v) + "°/s", v => UpdatePreferences(p => p.StickLookDegreesPerSecond = Mathf.Round(v / 10f) * 10f),
                SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, stickSpeed.transform);
            UiSwitch invert = UiSwitch.Create("InvertLook", content, UiStrings.Get("settings.row.invert_look", "INVERT LOOK"), look.LookInvertY,
                on => UpdatePreferences(p => p.LookInvertY = on));
            AddSettingRow(ref stack, invert.transform);
            UiSlider fpFov = UiSlider.Create("Setting.FIRST PERSON FOV", content, UiStrings.Get("camera.fov.first_person", "FIRST-PERSON FOV"),
                CameraSystem.CameraModeSettings.MinFirstPersonFov, CameraSystem.CameraModeSettings.MaxFirstPersonFov, UiSettings.FirstPersonFov, 1f,
                Degrees, v => UiSettings.FirstPersonFov = Mathf.Round(v), SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, fpFov.transform);
            UiSlider tpFov = UiSlider.Create("Setting.THIRD PERSON FOV", content, UiStrings.Get("camera.fov.third_person", "THIRD-PERSON FOV"),
                CameraSystem.CameraModeSettings.MinThirdPersonFov, CameraSystem.CameraModeSettings.MaxThirdPersonFov, UiSettings.ThirdPersonFov, 1f,
                Degrees, v => UiSettings.ThirdPersonFov = Mathf.Round(v), SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, tpFov.transform);
            UiStepper shoulder = UiStepper.Create("Shoulder", content, UiStrings.Get("camera.shoulder", "SHOULDER"), ShoulderNames(), UiSettings.Shoulder, true,
                i => UiSettings.Shoulder = i);
            AddSettingRow(ref stack, shoulder.transform);
            UiSlider bob = UiSlider.Create("Setting.HEAD BOB", content, UiStrings.Get("camera.head_bob", "HEAD BOB"), 0f, 1f, UiSettings.HeadBobScale, 0.05f, Percent,
                v => UiSettings.HeadBobScale = Mathf.Round(v * 20f) / 20f, SettingsColumnWidth, SettingLabelWidth);
            AddSettingRow(ref stack, bob.transform);
            UiSwitch dot = UiSwitch.Create("CentreDot", content, UiStrings.Get("settings.row.centre_dot", "CENTRE DOT"), UiSettings.CentreDot, on => UiSettings.CentreDot = on);
            AddSettingRow(ref stack, dot.transform);
            AddCaptionUnder(ref stack, content, "CameraNote",
                UiStrings.Get("settings.note.camera", "Third and first person only; switch the view in Pause. A low field of view causes more motion sickness; Reduced Motion (Accessibility) sets head bob to 0 and cuts the blends."), 3, UiSpace.S);   // AD R1-A SHOULD-8

            AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.data", "DATA"), true);
            UiSwitch telemetry = UiSwitch.Create("Telemetry", content, UiStrings.Get("settings.row.telemetry", "RUN TELEMETRY"), UiSettings.Telemetry,
                on => UiSettings.Telemetry = on);   // WP-022E / WP-045: local files only, no identity data
            AddSettingRow(ref stack, telemetry.transform);
            AddCaptionUnder(ref stack, content, "TelemetryNote",
                UiStrings.Get("settings.note.telemetry", "Run telemetry writes one local file per run (rounds, kills, weapon use) for tuning. It never contains your profile or name."), 1, UiSpace.S);
            UiSwitch hints = UiSwitch.Create("Hints", content, UiStrings.Get("settings.row.hints", "HINTS"), UiSettings.Hints,
                on => UiSettings.Hints = on);       // WP-068 slice 10b (U9)
            AddSettingRow(ref stack, hints.transform);
            AddCaptionUnder(ref stack, content, "HintsNote",
                UiStrings.Get("settings.note.hints", "Brief hints during the first three runs, in play and on loading screens. Off leaves the rules."), 1, UiSpace.None);

            scroll.SetContentHeight(stack.Cursor);
            scroll.TrackSelectables();

            if (well)
            {
                BuildScalePreview(body, column);   // the rail's 24 px gutter is the gap (AD 6ab MUST-5: the block fits whole at 1.0)
            }

            UiInput.ChainVertical(display.Button, scale, hudFrame.Button, language.Button, master, uiSounds, quality.Button,
                lookSpeed, stickSpeed, invert.Button, fpFov, tpFov, shoulder.Button, bob, dot.Button,
                telemetry.Button, hints.Button);
            UiInput.Focus(display.Button);
        }

        /// <summary>
        /// E6 / E8 pane body under a head <paramref name="top"/> px tall: the scrolled control
        /// column (≤ 720 + the rail's gutter) on the left, and whether the right well fits.
        /// </summary>
        private UiScrollView BuildControlColumn(float top, out RectTransform body, out float column, out bool well)
        {
            body = UiFactory.NewRect("Body", _paneHost);
            UiFactory.Fill(body);
            body.offsetMax = new Vector2(0f, -top);

            float width = _paneHost.rect.width;
            column = SettingsColumnWidth + UiScrollView.Gutter;
            if (width > 1f)
            {
                column = Mathf.Min(column, width);
            }

            well = width < 1f || width - column >= PreviewMinWidth;
            if (!well)
            {
                column = width;   // AD 6ab SHOULD-10: no well, no blank strip wider than a gutter - the column takes the pane
            }
            UiScrollView scroll = UiScrollView.Create("Scroll", body);
            var scrollRect = (RectTransform)scroll.transform;
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(0f, 1f);
            scrollRect.pivot = new Vector2(0f, 1f);
            scrollRect.sizeDelta = new Vector2(column, 0f);
            scrollRect.anchoredPosition = Vector2.zero;
            return scroll;
        }

        // M4-R1 N2: the look sensitivity as 1–20 steps (WP-070 §3.1: 1 = 0.02, 20 = 0.30 °/px).
        private const float LookSteps = 20f;
        private static string[] ShoulderNames()
        {
            return new[] { UiStrings.Get("camera.shoulder.right", "RIGHT"), UiStrings.Get("camera.shoulder.left", "LEFT") };
        }

        public static float LookSensitivityOf(float step)
        {
            float t = (Mathf.Clamp(step, 1f, LookSteps) - 1f) / (LookSteps - 1f);
            return InputPreferences.MinLookSensitivity + t * (InputPreferences.MaxLookSensitivity - InputPreferences.MinLookSensitivity);
        }

        public static float LookStepOf(float sensitivity)
        {
            float t = (sensitivity - InputPreferences.MinLookSensitivity) / (InputPreferences.MaxLookSensitivity - InputPreferences.MinLookSensitivity);
            return Mathf.Clamp(Mathf.Round(1f + t * (LookSteps - 1f)), 1f, LookSteps);
        }

        private static string Degrees(float value)
        {
            return Mathf.RoundToInt(value) + "°";
        }

        /// <summary>
        /// AD d109 SHOULD-7c: a control's name in the language. The remap service names controls in English ("Right Trigger",
        /// "Left Button"); in Turkish the descriptive gamepad and mouse controls and a few worded keys read from
        /// input.control.* (keyed by the control path after the device, "dpad/up" → "dpad.up"). Face buttons (device letters),
        /// start / select, letter keys and anything without a row keep the service's name. The path is only read as text
        /// (ADR-024-2: the UI never touches the Input System).
        /// </summary>
        public static string ControlName(string effectivePath, string displayName)
        {
            if (UiStrings.Language != UiStrings.Turkish || string.IsNullOrEmpty(effectivePath))
            {
                return displayName;
            }

            int slash = effectivePath.IndexOf('/');
            string control = slash >= 0 ? effectivePath.Substring(slash + 1) : effectivePath;
            string key = "input.control." + control.Replace('/', '.');
            return UiStrings.Has(key) ? UiStrings.Get(key, displayName) : displayName;
        }

        private static string Percent(float value)
        {
            return UiStrings.Percent(Mathf.RoundToInt(value * 100f));
        }

        private static string[] QualityNames()
        {
            string[] names = QualitySettings.names;
            if (names == null || names.Length == 0)
            {
                return new[] { UiStrings.Get("settings.quality.default", "DEFAULT") };
            }

            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                labels[i] = UiSettings.QualityLabel(i);   // DEC-100: "CINEMATIC · 30 FPS", "HIGH · 60 FPS"
            }

            return labels;
        }

        /// <summary>A 56 px row at every TextScale: one line of Body at 1.25 (20 px) sits in it with room, and a taller row only pushes the pane into scrolling sooner.</summary>
        private static void AddSettingRow(ref UiStack stack, Transform row)
        {
            stack.Add((RectTransform)row, SettingRowHeight);
            stack.Gap(UiSpace.XS);
        }

        /// <summary>
        /// E6 section head on one line: the small-caps label, then a hairline running to the
        /// column's edge. The AP-012 lozenge stays on the screen header; at section size it
        /// read as a stray dot (s6c frames).
        /// </summary>
        private static void AddSettingsSection(ref UiStack stack, RectTransform parent, string title, bool gapBefore)
        {
            if (gapBefore)
            {
                stack.Gap(UiSpace.S);   // + the last row's XS: 12 between a section's rows and the next head
            }

            RectTransform head = UiFactory.NewRect("Section." + title, parent);
            stack.Add(head, UiFactory.LineHeight(UiType.Label));
            TMP_Text label = UiFactory.Label("Label", head, title, UiType.Label, UiColor.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.Fill(label.rectTransform);
            float x = Mathf.Ceil(label.GetPreferredValues(title).x) + (int)UiSpace.M;

            Image rule = UiFactory.Divider("Rule", head);
            var ruleRect = (RectTransform)rule.transform;
            ruleRect.anchorMin = new Vector2(0f, 0.5f);
            ruleRect.anchorMax = new Vector2(1f, 0.5f);
            ruleRect.pivot = new Vector2(0f, 0.5f);
            ruleRect.offsetMin = new Vector2(x, -UiTokens.StrokeHairline * 0.5f);
            ruleRect.offsetMax = new Vector2(0f, UiTokens.StrokeHairline * 0.5f);

            stack.Gap(UiSpace.XS);
        }

        /// <summary>
        /// E6 preview well (SHOULD): the HUD health block at 1:1 × UI scale in a Look B screen corner,
        /// sized by the UI scale the slider shows, so the change is visible before the next screen.
        /// </summary>
        private void BuildScalePreview(RectTransform body, float left)
        {
            RectTransform well = UiFactory.NewRect("Preview", body);
            UiFactory.Fill(well);
            well.offsetMin = new Vector2(left, well.offsetMin.y);
            var stack = new UiStack(well);

            TMP_Text label = UiFactory.Label("Label", well, UiStrings.Get("settings.preview", "PREVIEW"), UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)label.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.S);

            float wellWidth = well.rect.width > 1f ? well.rect.width : PreviewMinWidth;
            RectTransform frame = UiFactory.FramedCard("Screen", well, UiColor.BorderHairline, UiColor.SurfaceInset, UiFrame.Card);
            stack.Add(frame, Mathf.Round(wellWidth * 9f / 16f));
            RectTransform screen = UiFactory.ClipContent(UiFactory.CardContent(frame, UiSpace.XS));

            var plate = new GameObject("Plate", typeof(RectTransform)).AddComponent<RawImage>();
            plate.transform.SetParent(screen, false);
            UiFactory.Fill(plate.rectTransform);
            plate.raycastTarget = false;
            plate.texture = Resources.Load<Texture2D>("MenuPlates/plate_far");
            if (plate.texture != null)
            {
                float aspect = (float)plate.texture.width / Mathf.Max(1, plate.texture.height);
                float w = Mathf.Min(1f, (16f / 9f) / aspect);
                plate.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                plate.gameObject.SetActive(false);
            }

            // Anchored bottom-RIGHT (AD 6ab MUST-5): at 110-120 % the clip falls on the heart /
            // label side, never on "64 / 100" or the bar's end.
            _previewHud = UiFactory.NewRect("PreviewHud", screen);
            _previewHud.anchorMin = new Vector2(1f, 0f);
            _previewHud.anchorMax = new Vector2(1f, 0f);
            _previewHud.pivot = new Vector2(1f, 0f);
            _previewHud.sizeDelta = new Vector2(PreviewBlockWidth, PreviewBlockHeight);
            BuildPreviewHealth(_previewHud);

            stack.Gap(UiSpace.S);
            _previewCaption = UiFactory.Label("Caption", well, string.Empty, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)_previewCaption.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
            ShowPreviewScale(UiSettings.UiScale);
        }

        /// <summary>A still of the HUD health block (label, value, bar at 64 %), full size; the parent scales it.</summary>
        private static void BuildPreviewHealth(RectTransform host)
        {
            RectTransform panel = UiFactory.FramedCard("Health", host, UiColor.BorderHairline, UiColor.HudInk, UiFrame.HudPanel, UiColor.TextSecondary);
            UiFactory.Fill(panel);
            RectTransform inner = UiFactory.CardContent(panel, UiSpace.L);

            TMP_Text label = UiFactory.Label("Label", inner, UiStrings.Get("hud.health", "HEALTH"), UiType.Label, UiColor.TextMuted, TextAnchor.UpperLeft);
            UiFactory.Fill(label.rectTransform);
            TMP_Text value = UiFactory.Numeric("Value", inner, "64 / 100", UiType.NumericM, UiColor.TextPrimary, TextAnchor.UpperRight, false);
            UiFactory.Fill(value.rectTransform);

            UiBar bar = UiFactory.Bar("Bar", inner, UiColor.BarHealth, 18f);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0f, 0f);
            barRect.sizeDelta = new Vector2(0f, 18f);
            barRect.anchoredPosition = Vector2.zero;
            bar.SetValue(0.64f, true);
        }

        private void ShowPreviewScale(float scale)
        {
            if (_previewHud == null)
            {
                return;
            }

            float s = PreviewHudScale * scale;
            _previewHud.localScale = new Vector3(s, s, 1f);
            _previewHud.anchoredPosition = new Vector2(-PreviewHudInset, PreviewHudInset);
            _previewCaption.text = UiStrings.Format("settings.preview.caption", "value", Percent(scale), "UI SCALE {value}. It applies to the next screen you open.");
        }

        private static string ReducedMotionLabel()
        {
            return UiSettings.ReducedMotion
                ? UiStrings.Get("access.reduced_motion.on", "REDUCED MOTION · ON")
                : UiStrings.Get("access.reduced_motion.off", "REDUCED MOTION · OFF");
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
                glyph = UiArt.IconImage("Icon", row, icon, UiColor.TextSecondary, UiIconTokens.GlyphFloor);   // ikon v2 (IK-M9): the 24 floor
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
                UiEvents.Raise(UiEvent.SliderChanged, label, Mathf.InverseLerp(min, max, v));   // WP-068 s11: the tick's pitch follows the value
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
            stack.Add(UiFactory.Heading(UiStrings.Get("menu.tab.controls", "CONTROLS"), _paneHost), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XS);
            TMP_Text intro = UiFactory.Label(
                "Intro", _paneHost, UiStrings.Get("controls.intro", "Select a slot and press the key or button you want. Escape cancels. Overrides are saved on this machine."),
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)intro.transform, UiFactory.LineHeight(UiType.Caption));
            stack.Gap(UiSpace.M);

            IInputRemapService remap = PrototypeInputInstaller.Instance != null ? PrototypeInputInstaller.Instance.Remap : null;
            var focusables = new List<Selectable>(16);

            UiScrollView scroll = UiScrollView.Create("ControlsScroll", _paneHost);
            ((RectTransform)scroll.transform).offsetMax = new Vector2(0f, -stack.Cursor);
            stack = new UiStack(scroll.Content);
            RectTransform columns = UiFactory.NewRect("Columns", scroll.Content);
            stack.Add(columns, 0f);
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
            AddRemapColumn(ref leftStack, UiStrings.Get("controls.keyboard", "KEYBOARD"), DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse, remap, leftButtons);
            AddRemapColumn(ref rightStack, UiStrings.Get("controls.gamepad", "GAMEPAD"), DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad, remap, rightButtons);

            // Gamepad options under the gamepad column.
            InputPreferences prefs = InputPreferencesStore.Current;
            rightStack.Gap(UiSpace.M);
            Slider deadzone = AddSettingSliderIn(ref rightStack, right, UiStrings.Get("controls.deadzone", "STICK DEAD ZONE"), null, 0f, 0.5f, prefs.StickDeadzone,
                v => UiStrings.Percent(Mathf.RoundToInt(v * 100f)), v => UpdatePreferences(p => p.StickDeadzone = v));
            // Slice 9 · E3 T3 (direction E7): what the dead zone does, in one sentence under its slider.
            AddCaptionUnder(ref rightStack, right, "DeadzoneNote",
                UiStrings.Get("controls.deadzone.note", "How far a stick moves before it counts. Raise it if your aim drifts on its own."), 2, UiSpace.S);
            // AD v006 MUST-6: AIM ASSIST as a cycle toggle (Off → Light → Full), MUST-1 recipe.
            UiButton assist = AddToggleRow(ref rightStack, right, "AimAssist", UiStrings.Get("controls.aim_assist", "AIM ASSIST"),
                () => AimAssistState(),
                () => { UpdatePreferences(p => p.AimAssist = (InputAimAssistLevel)(((int)p.AimAssist + 1) % 3)); return AimAssistState(); }, "AimAssist");   // WP-059: the level is WP-024 data
            rightStack.Gap(UiSpace.S);
            UiButton rumble = AddToggleRow(ref rightStack, right, "Rumble", UiStrings.Get("controls.rumble", "RUMBLE"),
                () => (InputPreferencesStore.Current.RumbleEnabled ? UiStrings.Get("common.on", "ON") : UiStrings.Get("common.off", "OFF"), InputPreferencesStore.Current.RumbleEnabled),
                () => { UpdatePreferences(p => p.RumbleEnabled = !p.RumbleEnabled); return (InputPreferencesStore.Current.RumbleEnabled ? UiStrings.Get("common.on", "ON") : UiStrings.Get("common.off", "OFF"), InputPreferencesStore.Current.RumbleEnabled); }, "Rumble");
            rightButtons.Add(deadzone);
            rightButtons.Add(assist);
            rightButtons.Add(rumble);

            // Reserve the actual scaled gamepad options as well as the keyboard rows.
            float columnsHeight = Mathf.Max(leftStack.Cursor, rightStack.Cursor);
            columns.sizeDelta = new Vector2(0f, columnsHeight);
            stack.Cursor += columnsHeight;

            // AD v006 MUST-7: reset as a full-width footer row under both columns, the
            // overrides file name right-aligned on the same row.
            stack.Gap(UiSpace.XL);
            RectTransform footer = UiFactory.NewRect("Footer", scroll.Content);
            stack.Add(footer, UiTokens.TargetCompact);
            // Slice 9 · E3 R1 (direction E7, certain): destructive, so a 500 ms hold - a click or a pad press alone does nothing.
            UiButton reset = UiFactory.Button("ResetBindings", footer, UiStrings.Get("controls.reset", "HOLD TO RESET ALL BINDINGS"), UiButtonVariant.Secondary, () =>
            {
                if (remap != null)
                {
                    remap.ResetToDefaults();
                }

                UiEvents.Raise(UiEvent.Confirm, "ResetBindings");
                _refocusName = "ControlsScroll/Viewport/Content/Footer/ResetBindings";
                RedrawPane();
            });
            reset.ConfigureHold(UiMotionTokens.HoldConfirm);
            RectTransform resetRect = (RectTransform)reset.transform;
            resetRect.anchorMin = new Vector2(0f, 0.5f);
            resetRect.anchorMax = new Vector2(0f, 0.5f);
            resetRect.pivot = new Vector2(0f, 0.5f);
            // Keep the existing minimum, but reserve the current font/scale's complete localized label plus button insets.
            float labelWidth = reset.LabelText.GetPreferredValues(reset.LabelText.text).x + (int)UiSpace.L * 2;
            float resetWidth = Mathf.Max(420f, Mathf.Ceil(labelWidth / (int)UiSpace.S) * (int)UiSpace.S);
            resetRect.sizeDelta = new Vector2(Mathf.Min(scroll.Content.rect.width, resetWidth), UiTokens.TargetCompact);
            resetRect.anchoredPosition = Vector2.zero;
            TMP_Text file = UiFactory.Label(
                "File", footer, remap != null ? UiStrings.Format("controls.file", "value", Path.GetFileName(remap.OverridesFilePath), "Overrides: {value}") : UiStrings.Get("controls.no_service", "Input service not running."),
                UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleRight);
            RectTransform fileRect = UiFactory.Fill((RectTransform)file.transform);
            fileRect.offsetMin = new Vector2(resetRect.sizeDelta.x + 20f, 0f);
            leftButtons.Add(reset);
            scroll.SetContentHeight(stack.Cursor);
            scroll.TrackSelectables();

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
            // Slice 9 · E3 R2 (direction E7): one control bound to two slots of this scheme is a conflict - both actions
            // answer it. Those caps take the warning edge and one Warning notice under the column names them (shape + words).
            List<string> conflicts = RemapConflicts(entries, scheme, out HashSet<string> conflictedPaths);
            bool anyOverridden = false;
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
                string capText = listening ? UiStrings.Get("controls.listening", "PRESS A KEY") : ControlName(e.EffectivePath, e.DisplayName);
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
                bool conflicted = !listening && !string.IsNullOrEmpty(e.EffectivePath) && conflictedPaths.Contains(e.EffectivePath);
                Color edgeColor = UiTokens.Get(listening || conflicted ? UiColor.StateWarning : UiColor.BorderHairline);
                if (listening || conflicted) edgeColor.a = 0.6f;
                capEdge.effectColor = edgeColor;
                anyOverridden |= e.IsOverridden;
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

            // The sakura diamond's word (kit rule: never a shape alone), once per column and only when a slot shows it.
            if (anyOverridden)
            {
                AddCaptionUnder(ref stack, (RectTransform)stack.Parent, "OverrideLegend",
                    UiStrings.Get("controls.override_legend", "A marked slot is changed from its default."), 1, UiSpace.XS);
            }

            if (conflicts.Count > 0)
            {
                stack.Gap(UiSpace.S);
                RectTransform column = (RectTransform)stack.Parent;
                RectTransform notice = UiNotices.Notice("Conflict", column, UiNoticeKind.Warning, string.Join("\n", conflicts),
                    column.rect.width > 1f ? column.rect.width : 480f);
                stack.Add(notice, notice.sizeDelta.y);
            }
        }

        /// <summary>
        /// Slice 9 · E3 R2: the conflict sentences of one scheme's rebindable slots - a control (effective path) that two
        /// or more slots answer, with the slots' names in list order. <paramref name="conflictedPaths"/> gets those paths.
        /// </summary>
        public static List<string> RemapConflicts(IReadOnlyList<InputRemapEntry> entries, DD.Prototype.Core.InputAbstraction.InputSchemeKind scheme, out HashSet<string> conflictedPaths)
        {
            var order = new List<string>();
            var names = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);
            var display = new Dictionary<string, string>(System.StringComparer.Ordinal);
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                InputRemapEntry e = entries[i];
                if (e.Scheme != scheme || e.Map != PrototypeInputNames.MapGameplay || e.Action == PrototypeInputNames.ActionDebugPanel
                    || e.Action == PrototypeInputNames.ActionAim || e.Action == PrototypeInputNames.ActionAimStick || string.IsNullOrEmpty(e.EffectivePath))
                {
                    continue;
                }

                if (!names.TryGetValue(e.EffectivePath, out List<string> list))
                {
                    list = new List<string>(2);
                    names.Add(e.EffectivePath, list);
                    display.Add(e.EffectivePath, ControlName(e.EffectivePath, e.DisplayName));
                    order.Add(e.EffectivePath);
                }

                list.Add(RemapRowName(e));
            }

            conflictedPaths = new HashSet<string>(System.StringComparer.Ordinal);
            var sentences = new List<string>();
            for (int i = 0; i < order.Count; i++)
            {
                List<string> list = names[order[i]];
                if (list.Count < 2)
                {
                    continue;
                }

                conflictedPaths.Add(order[i]);
                sentences.Add(UiStrings.Format("controls.conflict", "key", display[order[i]], "actions", string.Join(", ", list),
                    "{key} is bound to more than one action: {actions}. Rebind one of them."));
            }

            return sentences;
        }

        private const int MaxRemapRows = 10; // Gameplay map: Move ×4, Fire, Reload, Pause, Special, Interact (9 on keyboard)

        public static string RemapRowName(InputRemapEntry e)
        {
            string action = UiStrings.Get("controls.action." + e.Action, e.Action);
            if (string.IsNullOrEmpty(e.CompositePart))
            {
                return action;
            }

            string part = e.CompositePart;
            string fallback = char.ToUpperInvariant(part[0]) + part.Substring(1);
            return UiStrings.Format("controls.action_part", "action", action,
                "part", UiStrings.Get("controls.part." + part, fallback), "{action} {part}");
        }

        public static string AimAssistLabel(InputAimAssistLevel level)
        {
            switch (level)
            {
                case InputAimAssistLevel.Off: return UiStrings.Get("controls.assist.off", "OFF");
                case InputAimAssistLevel.Light: return UiStrings.Get("controls.assist.light", "LIGHT");
                default: return UiStrings.Get("controls.assist.full", "FULL");
            }
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
            Transform t = _paneHost.Find("ControlsScroll/Viewport/Content/Columns/Keyboard/Slot." + entryId + "/Bind." + entryId);
            if (t == null)
            {
                t = _paneHost.Find("ControlsScroll/Viewport/Content/Columns/Gamepad/Slot." + entryId + "/Bind." + entryId);
            }

            return t != null ? "ControlsScroll/Viewport/Content/Columns/" + t.parent.parent.name + "/" + t.parent.name + "/" + t.name : null;
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
            return (AimAssistLabel(level), level != InputAimAssistLevel.Off);
        }

        // ------------------------------------------------------- accessibility pane

        private static string[] PaletteModes()
        {
            return new[] { UiStrings.Get("access.palette.default", "DEFAULT"), UiStrings.Get("access.palette.colour_blind", "COLOUR-BLIND SAFE") };
        }
        private static readonly float[] TextScaleStops = { 1f, 1.1f, 1.25f };   // E8: stops 100 / 110 / 125
        private static readonly UiColor[] SwatchTokens = { UiColor.BarHealth, UiColor.StateSuccess, UiColor.StateWarning, UiColor.StateDanger };
        private static readonly string[] SwatchNames = { "HEALTH", "EARNED", "WARNING", "DANGER" };   // object names + fallbacks
        private static readonly string[] SwatchKeys = { "hud.health", "ach.earned", "access.swatch.warning", "access.swatch.danger" };
        private const float SampleBarHeight = 12f;
        private const float SampleHealth = 0.25f;           // the low-health state the sample shows
        private const float SampleHitFrom = 0.45f;
        private const float SampleEdgeWidth = 0.22f;        // damage edge ramp, fraction of the mini screen
        private const float SampleBandHeight = 0.18f;       // final-seconds bands (the HUD's 0.82 / 0.18 ramps)
        private const float SampleBandAlpha = 0.30f;        // the HUD's final-seconds pulse peak

        private Image[] _sampleChips;
        private TMP_Text _sampleCaption;
        private UiBar _sampleBar;
        private Image _sampleEdge;
        private Image[] _sampleBands;

        /// <summary>
        /// WP-022G · accessibility (WP-068 slice 6, direction E8). COLOUR (PALETTE stepper),
        /// TEXT (TEXT SCALE slider, stops 100 / 110 / 125) and MOTION (REDUCE FLASHES,
        /// REDUCED MOTION switches), each with its sentence, on the Settings column and
        /// scroller. The right well is a live sample: the four state chips in the current
        /// palette, a caption at the chosen text scale, the low health bar, and a mini screen
        /// with the damage edge and the final-seconds bands at the current flash scale. A
        /// flash or motion switch replays the hit, so the difference shows at once.
        /// </summary>
        private void BuildAccessibilityPane()
        {
            var head = new UiStack(_paneHost);
            head.Add(UiFactory.Heading(UiStrings.Get("menu.tab.accessibility", "ACCESSIBILITY"), _paneHost), UiFactory.LineHeight(UiType.H2));
            AddCaptionUnder(ref head, _paneHost, "Intro",
                UiStrings.Get("access.caption", "These change how the game is presented, never how it plays."), 1, UiSpace.M);

            RectTransform body;
            float column;
            bool well;
            UiScrollView scroll = BuildControlColumn(head.Cursor, out body, out column, out well);
            RectTransform content = scroll.Content;
            var stack = new UiStack(content);

            AddSettingsSection(ref stack, content, UiStrings.Get("access.section.colour", "COLOUR"), false);
            UiStepper palette = UiStepper.Create("Palette", content, UiStrings.Get("access.row.palette", "PALETTE"), PaletteModes(), UiSettings.ColorPalette, true,
                i =>
                {
                    UiSettings.ColorPalette = i;
                    RefreshAccessibilitySample();
                });
            AddSettingRow(ref stack, palette.transform);
            AddCaptionUnder(ref stack, content, "PaletteNote",
                UiStrings.Get("access.note.palette", "Colour-blind safe moves health, earned and unlocked states from green to blue. Danger stays red; every state also has a shape and a word."), 2, UiSpace.XS);

            AddSettingsSection(ref stack, content, UiStrings.Get("access.section.text", "TEXT"), true);
            UiSlider text = UiSlider.Create("Setting.TEXT SCALE", content, UiStrings.Get("access.row.text_scale", "TEXT SCALE"),
                UiSettings.TextScaleMin, UiSettings.TextScaleMax, UiSettings.TextScale, 0f, Percent,
                v =>
                {
                    UiSettings.TextScale = v;
                    RefreshAccessibilitySample();
                }, SettingsColumnWidth, SettingLabelWidth);
            text.SetStops(TextScaleStops);
            AddSettingRow(ref stack, text.transform);
            AddCaptionUnder(ref stack, content, "TextNote",
                UiStrings.Get("access.note.text_scale", "Body, label and caption text. It applies to the next screen you open."), 1, UiSpace.XS);

            AddSettingsSection(ref stack, content, UiStrings.Get("access.section.motion", "MOTION"), true);
            UiSwitch flashes = UiSwitch.Create("ReduceFlashes", content, UiStrings.Get("access.row.reduce_flashes", "REDUCE FLASHES"), UiSettings.ReduceFlashes,
                on =>
                {
                    UiSettings.ReduceFlashes = on;
                    RefreshAccessibilitySample();
                    PlayAccessibilitySample();
                });
            AddSettingRow(ref stack, flashes.transform);
            AddCaptionUnder(ref stack, content, "FlashNote",
                UiStrings.Get("access.note.flashes", "Damage and final-seconds flashes at 40%. Blood and the world are not changed. Camera shake has its own slider on the pause screen."), 2, UiSpace.S);
            UiSwitch reduced = UiSwitch.Create("ReducedMotion", content, UiStrings.Get("access.row.reduced_motion", "REDUCED MOTION"), UiSettings.ReducedMotion,
                on =>
                {
                    UiSettings.ReducedMotion = on;
                    PlayAccessibilitySample();
                });
            AddSettingRow(ref stack, reduced.transform);
            AddCaptionUnder(ref stack, content, "MotionNote",
                UiStrings.Get("access.note.reduced_motion", "Pulses hold still, screen fades are instant and the menu petals stop drifting."), 1, UiSpace.None);

            scroll.SetContentHeight(stack.Cursor);
            scroll.TrackSelectables();
            if (well)
            {
                BuildAccessibilitySample(body, column);
            }

            UiInput.ChainVertical(false, palette.Button, text, flashes.Button, reduced.Button);
            UiInput.LinkHorizontal(_accessibilityTab, null, palette.Button);
            UiInput.LinkHorizontal(palette.Button, _accessibilityTab, null);
            UiInput.LinkHorizontal(text, _accessibilityTab, null);
            UiInput.LinkHorizontal(flashes.Button, _accessibilityTab, null);
            UiInput.LinkHorizontal(reduced.Button, _accessibilityTab, null);
            if (_focusPaneOnOpen)
            {
                UiInput.Focus(palette.Button); // AD v006 MUST-8: focus lands on the first control
            }
        }

        /// <summary>E8 live sample well (the right five columns).</summary>
        private void BuildAccessibilitySample(RectTransform body, float left)
        {
            RectTransform well = UiFactory.NewRect("Sample", body);
            UiFactory.Fill(well);
            well.offsetMin = new Vector2(left, well.offsetMin.y);
            var stack = new UiStack(well);
            float width = well.rect.width > 1f ? well.rect.width : PreviewMinWidth;

            TMP_Text label = UiFactory.Label("Label", well, UiStrings.Get("access.sample", "SAMPLE"), UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)label.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.S);

            // The four state chips, two by two, filled from the current palette.
            float chipHeight = UiFactory.ScaledBand(StateChipHeight);
            float chipWidth = Mathf.Floor((width - (int)UiSpace.S) * 0.5f);
            RectTransform chips = UiFactory.NewRect("Swatches", well);
            stack.Add(chips, chipHeight * 2f + (int)UiSpace.S);
            _sampleChips = new Image[SwatchTokens.Length];
            for (int i = 0; i < SwatchTokens.Length; i++)
            {
                TMP_Text chip = UiFactory.Chip("Swatch." + SwatchNames[i], chips, UiStrings.Get(SwatchKeys[i], SwatchNames[i]), SwatchTokens[i], UiColor.SurfaceBase, chipWidth, chipHeight);
                chip.fontStyle = FontStyles.Bold;
                var rect = (RectTransform)chip.transform.parent;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2((i % 2) * (chipWidth + (int)UiSpace.S), -(i / 2) * (chipHeight + (int)UiSpace.S));
                _sampleChips[i] = rect.GetComponent<Image>();
            }

            stack.Gap(UiSpace.M);
            _sampleCaption = UiFactory.Label("CaptionSample", well, string.Empty, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)_sampleCaption.transform, Mathf.Ceil(UiTokens.FontSize(UiType.Caption) * 1.4f * UiSettings.TextScaleMax));
            stack.Gap(UiSpace.M);

            // The low health state exactly as the HUD draws it below LowHealthFraction (AD 6ab
            // MUST-6): cracked heart + BarHealthLow bar + the value in StateDanger, in either
            // palette. A hit on the bar is the motion sample (ghost trail).
            bool low = SampleHealth < UiTokens.LowHealthFraction;
            float labelBand = UiFactory.LineHeight(UiType.Label);
            RectTransform healthRow = UiFactory.NewRect("HealthRow", well);
            stack.Add(healthRow, labelBand);
            Image heart = UiArt.IconImage("Heart", healthRow, low ? "Hud.LowHealth" : "Hud.Health", low ? UiColor.BarHealthLow : UiColor.BarHealth, labelBand);
            var heartRect = (RectTransform)heart.transform;
            heartRect.anchorMin = new Vector2(0f, 0.5f);
            heartRect.anchorMax = new Vector2(0f, 0.5f);
            heartRect.pivot = new Vector2(0f, 0.5f);
            heartRect.anchoredPosition = Vector2.zero;
            float textLeft = heart.enabled ? labelBand + (int)UiSpace.S : 0f;
            TMP_Text healthLabel = UiFactory.Label("HealthLabel", healthRow, UiStrings.Get("hud.health", "HEALTH"), UiType.Label, UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform healthLabelRect = UiFactory.Fill(healthLabel.rectTransform);
            healthLabelRect.offsetMin = new Vector2(textLeft, 0f);
            TMP_Text healthValue = UiFactory.Numeric("HealthValue", healthRow, Mathf.RoundToInt(SampleHealth * 100f) + " / 100", UiType.NumericM, low ? UiColor.StateDanger : UiColor.TextPrimary, TextAnchor.MiddleRight, false);
            UiFactory.Fill(healthValue.rectTransform);
            stack.Gap(UiSpace.XS);
            _sampleBar = UiFactory.Bar("HealthBar", well, low ? UiColor.BarHealthLow : UiColor.BarHealth, SampleBarHeight);
            stack.Add((RectTransform)_sampleBar.transform, SampleBarHeight);
            _sampleBar.SetValue(SampleHealth, true);
            stack.Gap(UiSpace.L);

            // A mini screen: the far plate, the damage edge on the left, the final-seconds bands.
            RectTransform frame = UiFactory.FramedCard("Screen", well, UiColor.BorderHairline, UiColor.SurfaceInset, UiFrame.Card);
            stack.Add(frame, Mathf.Round(width * 9f / 16f));
            RectTransform screen = UiFactory.ClipContent(UiFactory.CardContent(frame, UiSpace.XS));
            var plate = new GameObject("Plate", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            plate.transform.SetParent(screen, false);
            UiFactory.Fill(plate.rectTransform);
            plate.raycastTarget = false;
            plate.texture = Resources.Load<Texture2D>("MenuPlates/plate_far");
            plate.gameObject.SetActive(plate.texture != null);

            _sampleEdge = UiFactory.NewImage("DamageEdge", screen, UiColor.DamageEdge, UiSprites.FadeLeft);
            RectTransform edgeRect = UiFactory.Fill((RectTransform)_sampleEdge.transform);
            edgeRect.anchorMax = new Vector2(SampleEdgeWidth, 1f);
            Image top = UiFactory.NewImage("FinalTop", screen, UiColor.StateDanger, UiSprites.FadeDown);
            RectTransform topRect = UiFactory.Fill((RectTransform)top.transform);
            topRect.anchorMin = new Vector2(0f, 1f - SampleBandHeight);
            Image bottom = UiFactory.NewImage("FinalBottom", screen, UiColor.StateDanger, UiSprites.FadeUp);
            RectTransform bottomRect = UiFactory.Fill((RectTransform)bottom.transform);
            bottomRect.anchorMax = new Vector2(1f, SampleBandHeight);
            _sampleBands = new[] { top, bottom };

            stack.Gap(UiSpace.S);
            TMP_Text note = UiFactory.Label("ScreenNote", well, UiStrings.Get("access.sample.edge", "Damage edge and final-seconds bands at their peak."), UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
            RefreshAccessibilitySample();
        }

        /// <summary>Re-reads palette, text scale and flash scale into the sample (no motion).</summary>
        private void RefreshAccessibilitySample()
        {
            if (_sampleCaption == null)
            {
                return;
            }

            for (int i = 0; i < _sampleChips.Length; i++)
            {
                _sampleChips[i].color = UiTokens.Get(SwatchTokens[i]);
            }

            _sampleCaption.fontSize = UiTokens.FontSize(UiType.Caption);
            _sampleCaption.text = UiStrings.Format("access.sample.caption", "value", Percent(UiSettings.TextScale), "Caption text at {value}.");
            SetAlpha(_sampleEdge, UiTokens.DamageEdgePeakAlpha * UiTokens.FlashScale);
            foreach (Image band in _sampleBands)
            {
                SetAlpha(band, SampleBandAlpha * UiTokens.FlashScale);
            }
        }

        /// <summary>The motion sample: a hit on the bar (ghost trail; Reduced Motion lands it) under the current settings.</summary>
        private void PlayAccessibilitySample()
        {
            if (_sampleBar == null)
            {
                return;
            }

            _sampleBar.SetValue(SampleHitFrom, true);
            _sampleBar.SetValue(SampleHealth, false);
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
            stack.Add((RectTransform)button.transform, UiFactory.ScaledBand(ToggleRowHeight));
            button.LabelText.alignment = TMPro.TextAlignmentOptions.Left;
            button.LabelText.fontSize = UiTokens.FontSize(UiType.Body);
            button.LabelText.color = UiTokens.Get(UiColor.TextPrimary);
            RectTransform labelRect = (RectTransform)button.LabelText.transform;
            labelRect.offsetMin = new Vector2(20f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-(ChipWidthFor(UiStrings.Get("access.palette.colour_blind", "COLOUR-BLIND SAFE")) + 16f + (int)UiSpace.M), labelRect.offsetMax.y);

            (string text, bool on) state = current();
            TMP_Text chip = UiFactory.Chip("State", button.transform, state.text,
                state.on ? UiColor.StateSuccess : UiColor.SurfaceInset,
                state.on ? UiColor.SurfaceBase : UiColor.TextSecondary,
                Mathf.Max(StateChipMinWidth, ChipWidthFor(state.text)), UiFactory.ScaledBand(StateChipHeight));
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
            chipRect.sizeDelta = new Vector2(Mathf.Max(StateChipMinWidth, ChipWidthFor(state.text)), UiFactory.ScaledBand(StateChipHeight));
        }

        private static TMP_Text AddCaptionUnder(ref UiStack stack, RectTransform parent, string name, string text, int lines = 2, UiSpace after = UiSpace.XL)
        {
            stack.Gap(UiSpace.S);
            TMP_Text caption = UiFactory.Label(name, parent, text, UiType.Caption, UiColor.TextMuted);
            // WP-068 (D6): the caption is as tall as its text at the pane width and the TextScale;
            // a fixed two-line band wasted a line under one-line captions (the Settings note fell
            // off the pane when HINTS arrived) and clipped at 1.25. Unlaid-out parents keep the old band.
            float width = parent.rect.width;
            float height = width > 1f
                ? Mathf.Max(UiFactory.LineHeight(UiType.Caption), Mathf.Ceil(caption.GetPreferredValues(text, width, 0f).y))
                : UiFactory.LineHeight(UiType.Caption) * lines;
            stack.Add((RectTransform)caption.transform, height);
            stack.Gap(after);
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
                "LevelLabel", _paneHost, UiStrings.Get("profile.level_label", "PROFILE LEVEL"), UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)levelLabel.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.XS);

            // WP-068 slice 3 (AD MUST-2): one face - "LEVEL 1" is an H2 Display heading, as on
            // Results (a Numeric label drew "LEVEL" in the fallback sans beside an Oswald digit).
            TMP_Text level = UiFactory.Label(
                "Level", _paneHost, model.LevelText, UiType.H2, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            stack.Add((RectTransform)level.transform, UiFactory.LineHeight(UiType.H2));

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
                "UnlockLabel", left, UiStrings.Get("profile.unlocked_content", "UNLOCKED CONTENT"), UiType.Label, UiColor.TextMuted);
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

            float needed = leftStack.Cursor;
            if (twoColumns)
            {
                RectTransform right = UiFactory.NewRect("Right", columns);
                UiFactory.Fill(right);
                right.anchorMin = new Vector2(ProfileLeftColumn, 0f);
                var rightStack = new UiStack(right);
                BuildCosmeticsPicker(ref rightStack, cosmetics);
                needed = Mathf.Max(needed, rightStack.Cursor);
            }

            // Slice 9 · E3 T1: the band grows to what its columns hold - at TextScale 1.25 the picker needs ≈ 420 px and
            // the fixed 340 clipped its last rows.
            if (needed > ProfileColumnsHeight)
            {
                columns.sizeDelta = new Vector2(columns.sizeDelta.x, Mathf.Ceil(needed));
                stack.Cursor += Mathf.Ceil(needed) - ProfileColumnsHeight;
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

            // WP-022D: pages of up to MaxAchievementRows in catalog order (27 entries, WP-023);
            // PREVIOUS / NEXT are ordinary buttons, so pad and keyboard page the same way.
            // Rows per page follow the room the pane has at this UI scale × TextScale (≤ MaxAchievementRows): the rows
            // grow with the line boxes, and the pager and the note never leave the pane (Mac 1.2 × 1.25 holds four).
            float fixedBands = UiFactory.LineHeight(UiType.H2) + (int)UiSpace.M + UiFactory.LineHeight(UiType.Body) + (int)UiSpace.L
                               + (int)UiSpace.XS + PagerHeight + (int)UiSpace.S + UiFactory.LineHeight(UiType.Caption);
            float rowPitch = AchievementRowHeightFor(false) + (int)UiSpace.S;
            int perPage = _paneHost.rect.height > 0f
                ? Mathf.Clamp(Mathf.FloorToInt((_paneHost.rect.height - fixedBands) / rowPitch), 2, MaxAchievementRows)
                : MaxAchievementRows;
            int pages = Mathf.Max(1, (model.Lines.Count + perPage - 1) / perPage);
            _achievementPage = Mathf.Clamp(_achievementPage, 0, pages - 1);
            int first = _achievementPage * perPage;
            int last = Mathf.Min(model.Lines.Count, first + perPage);
            for (int i = first; i < last; i++)
            {
                stack.Add(BuildAchievementRow(model.Lines[i]), AchievementRowHeightFor(model.Lines[i].Earned));
                stack.Gap(UiSpace.S);
            }

            if (pages > 1)
            {
                stack.Gap(UiSpace.XS);
                RectTransform pager = UiFactory.NewRect("Pager", _paneHost);
                stack.Add(pager, PagerHeight);

                UiButton previous = UiFactory.Button("Pager.Previous", pager, UiStrings.Get("ach.previous", "PREVIOUS"), UiButtonVariant.Secondary, () => TurnAchievementPage(-1));
                RectTransform previousRect = (RectTransform)previous.transform;
                previousRect.anchorMin = new Vector2(0f, 0.5f);
                previousRect.anchorMax = new Vector2(0f, 0.5f);
                previousRect.pivot = new Vector2(0f, 0.5f);
                previousRect.sizeDelta = new Vector2(160f, PagerHeight);
                previousRect.anchoredPosition = Vector2.zero;
                previous.SetInteractable(_achievementPage > 0);

                UiButton next = UiFactory.Button("Pager.Next", pager, UiStrings.Get("ach.next", "NEXT"), UiButtonVariant.Secondary, () => TurnAchievementPage(1));
                RectTransform nextRect = (RectTransform)next.transform;
                nextRect.anchorMin = new Vector2(1f, 0.5f);
                nextRect.anchorMax = new Vector2(1f, 0.5f);
                nextRect.pivot = new Vector2(1f, 0.5f);
                nextRect.sizeDelta = new Vector2(160f, PagerHeight);
                nextRect.anchoredPosition = Vector2.zero;
                next.SetInteractable(_achievementPage < pages - 1);

                TMP_Text page = UiFactory.Label(
                    "Page", pager, UiStrings.Format("ach.page", "value", (_achievementPage + 1).ToString(), "total", pages.ToString(), "Page {value} of {total}"), UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleCenter);
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
        /// interactable and says how it opens (slice 9 T1: its disabled reason, LOCKED · ROUND 10 -
        /// a press reads Denied, the pad still reaches it through the chain). Selecting writes through
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
                AddCosmeticOption(ref stack, kind, line.Id, line.Title, line.LockReason, line.Unlocked, line.IsNew, line.Selected, options);
            }
        }

        private void AddCosmeticOption(ref UiStack stack, string kind, string id, string title, string lockReason, bool unlocked, bool isNew, bool selected, List<Selectable> options)
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
            stack.Add((RectTransform)button.transform, UiFactory.ScaledBand(OptionHeight));
            stack.Gap(UiSpace.XS);
            button.LabelText.fontSize = UiTokens.FontSize(UiType.Body); // AD v004 MUST-4: names at Body 18
            button.SetSelectedChoice(selected);
            button.SetInteractable(unlocked);
            // Slice 9 · E3 T1 (direction E3: LOCKED = lock + condition): the kit's disabled reason at the row's right edge
            // says what opens it; the old LOCKED chip said only that it was shut.
            if (!unlocked)
            {
                button.SetDisabledReason(string.IsNullOrEmpty(lockReason) ? UiStrings.Get("tree.state.locked", "LOCKED") : lockReason);
            }

            options.Add(button);

            // Right-hand chip: NEW (pulse) for a fresh unlock; nothing when it is simply available.
            if (isNew)
            {
                TMP_Text chip = UiFactory.Chip(
                    "State", button.transform,
                    UiStrings.Get("tree.flag.new", "NEW"),
                    UiColor.AccentSakura,
                    UiColor.SurfaceBase,
                    56f, UiChips.Height(UiChipSize.Row)); // WP-068 slice 3 (AD MUST-1): the band from the face at the TextScale - a fixed 22 emptied LOCKED at 1.25
                RectTransform chipRect = (RectTransform)chip.transform.parent;
                chipRect.anchorMin = new Vector2(1f, 0.5f);
                chipRect.anchorMax = new Vector2(1f, 0.5f);
                chipRect.pivot = new Vector2(1f, 0.5f);
                chipRect.anchoredPosition = new Vector2(-(int)UiSpace.S, 0f);
                chipRect.gameObject.AddComponent<UiPulse>().Configure(
                    new Graphic[] { chipRect.GetComponent<Graphic>() }, 0.80f, 1f, UiTokens.PulsePeriod);
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

            // The text bands come from the faces' line boxes at the TextScale (slice 3 made them taller: a fixed
            // 24 px title band and an 8 px in-progress description band truncated both lines to nothing).
            float titleBand = UiFactory.LineHeight(UiType.H3);
            TMP_Text title = UiFactory.Label(
                "Title", content, line.Title, UiType.H3,
                line.Earned ? UiColor.TextPrimary : UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(textLeft, -titleBand);
            titleRect.offsetMax = new Vector2(-ChipColumnWidth, 0f);

            // TextSecondary, not TextMuted: muted measures 4.27:1 on SurfaceRaised,
            // which is below the 4.5 floor, and an earned achievement's card IS raised.
            TMP_Text description = UiFactory.Label(
                "Description", content,
                UiFactory.Clip(line.Description, 90),
                UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform descriptionRect = UiFactory.Fill((RectTransform)description.transform);
            descriptionRect.offsetMin = new Vector2(textLeft, line.Earned ? 0f : ProgressBand);
            descriptionRect.offsetMax = new Vector2(-ChipColumnWidth, -titleBand);

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
                line.Earned ? (line.IsNew ? UiStrings.Get("tree.flag.new", "NEW") : UiStrings.Get("ach.earned", "EARNED")) : UiStrings.Get("ach.in_progress", "IN PROGRESS"),
                line.Earned ? (line.IsNew ? UiColor.AccentSakura : UiColor.StateSuccess) : UiColor.SurfaceInset,
                line.Earned ? (line.IsNew ? UiColor.SurfaceBase : UiColor.SurfaceInset) : UiColor.TextSecondary, // NEW = ink on sakura (AD v004 MUST-1)
                140f, UiChips.Height(UiChipSize.Card)); // 140: "IN PROGRESS" at Caption tracking needs it (120 clipped the last S); height from the face (slice 3)
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

        /// <summary>
        /// A row holds its padding, the H3 title and one Caption line (+ the progress band while in progress) at the
        /// TextScale; never shorter than the 96 px medallion row. At TextScale 1.0 every row is still 96.
        /// </summary>
        private static float AchievementRowHeightFor(bool earned)
        {
            float text = UiFactory.LineHeight(UiType.H3) + UiFactory.LineHeight(UiType.Caption);
            return Mathf.Max(AchievementRowHeight, (int)UiSpace.L * 2 + text + (earned ? 0f : ProgressBand));
        }

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
                line.Unlocked ? UiStrings.Get("profile.unlocked", "UNLOCKED") : UiStrings.Format("profile.level", "value", line.RequiredLevel.ToString(), "LEVEL {value}"),
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
