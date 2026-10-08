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
        private const float PagerHeight = 48f;   // WP-098B: the C29 icon pager's hit (UiIconTokens.Hit(PaneLook.PagerIcon))

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
        public static string BuildNotePrefix => UiStrings.Get("menu.build_prefix", "Build · ");

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

        // v4: the rail, the word beside its focused item, the open pane's tick, the level readout.
        private readonly List<UiIconButton> _rail = new List<UiIconButton>(8);
        private UiIconButton _railFocus;
        private RectTransform _railWordRect;
        private TMP_Text _railWord;
        private TMP_Text _railSub;
        private UiBrushMark _paneMark;   // WP-098A (S-09 S4): the open pane's C8 under its rail icon (was the vermilion heading tick)
        private UiButton _paneMarkOn;     // the rail item the C8 sits under (a redraw of the same pane lands it, no new stroke)
        private Image _railPad;
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

        /// <summary>A pane is open (WP-098B: false from the moment it is closed - the carrier's fade-out is not "open").</summary>
        public bool PaneOpen
        {
            get { return _pane != UiContext.MainMenu && _brushPane != null && _brushPane.IsOpen; }
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

            // WP-098B: the QUIT confirm owns this Cancel (it closes itself in LateUpdate); an open pane must not close with it.
            if (PrototypeUiFlow.QuitConfirmOpen)
            {
                return;
            }

            if (_pane != UiContext.MainMenu)
            {
                // WP-098B: the kit's HandleCancel closes the carrier (and plays Back); the screen returns focus to the icon.
                UiButton icon = TabOf(_pane);
                CancelPane();
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
            _quitButton = AddRail(column, 7, pitch, "Quit", "Menu.Quit", "menu.quit", "QUIT", PrototypeUiFlow.RequestQuit);   // WP-098B (C-00 07.10): QUIT asks first
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

            // The open pane's mark: WP-098A (S-09 07.10 S4) the kit's C8 under its icon - the rail's one selection trace and
            // the screen's vermilion (the heading tick it replaces counted the same). Hidden until a pane opens.
            _paneMark = UiBrushKit.SelectionMark(column);
            _paneMark.gameObject.name = "PaneMark";

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

        /// <summary>
        /// WP-098B · the pane's look (AP-028 v002 main-profile / settings / achievements; KIT-KILAVUZU §3 "Menü pane'leri"):
        /// the brush kit's frameless carrier - C1 ink scrim at the Pane level, the C2 sumi wash under the content column,
        /// the C37 edge trace on its left - a C5 title (48 px glyph + Display word + the title brush), at most two C6
        /// brush dividers per pane (every other rule is the plain hairline), and one C8 under the chosen settings tab.
        /// Every colour, alpha, size and stroke is the kit's (UiBrushTokens); what is left here is the menu's own choice
        /// of kit pieces and the rows' stagger. The pane object keeps its name ("PaneCard": the tests find the pane by it).
        /// </summary>
        public static class PaneLook
        {
            public const string CardName = "PaneCard";
            public const string LeavingName = "PaneLeaving";
            public const string ScrollName = "PaneScroll";
            public const string TitleName = "Heading";
            public const string SettingsTabPrefix = "SettingsTab.";
            public const string SettingsStripName = "SettingsStrip";
            public const string SettingsTabsName = "SettingsTabs";

            /// <summary>C1 level of the carrier itself (AP-028 main-profile, 118); the reading pane's separate Window ground guarantees text contrast (K1).</summary>
            public const UiBrushScrim Scrim = UiBrushScrim.Pane;
            public const UiBrushWash Wash = UiBrushWash.Column;

            /// <summary>
            /// S-04 08.10 (KIT-KILAVUZU §2.2: ScrimWindow = the full-screen windows - settings, achievements; text stands on it
            /// directly): Settings, Controls, Accessibility, Achievements and Statistics put the Window level over the menu, on a
            /// second, scrim-only kit pane (<see cref="GroundName"/>), while the carrier's own Pane scrim fades out under it. Profile
            /// also uses this ground for K1 (the actual AP-034 wash plateau is not opaque).
            /// </summary>
            public const UiBrushScrim WindowScrim = UiBrushScrim.Window;
            public const string GroundName = "PaneGround";

            /// <summary>
            /// Every reading pane uses Window; none with no pane. WP-098B GT G-S1 / K1: AP-034's C2 plateau is below 1,
            /// so Pane plus the real mask misses TextGroundMin on Profile. Window alone carries its text even where the
            /// wash is transparent, without changing the shared kit's masks, colours or linear alpha tokens.
            /// </summary>
            public static UiBrushScrim ScrimOf(UiContext pane)
            {
                switch (pane)
                {
                    case UiContext.MainMenu: return UiBrushScrim.None;
                    default: return WindowScrim;
                }
            }

            /// <summary>The least ink under a pane's text: its C1 level and the carrier's C2 core combined (≥ TextGroundMin).</summary>
            public static float TextGroundOf(UiContext pane)
            {
                return UiBrushTokens.Combined(UiBrushTokens.Scrim(ScrimOf(pane)), UiBrushTokens.WashCore);
            }

            /// <summary>The band a C6 brush divider stands in (8 px grid; the stroke's own height is the kit's).</summary>
            public const float DividerBand = 8f;

            /// <summary>C12 settings tabs: one per section of the settings list (icon, reader name key, object id / fallback).</summary>
            public static readonly string[] SettingsTabIcons = { "Settings.Display", "Menu.Audio", "Menu.Graphics", "Settings.Camera", "Settings.Data" };
            public static readonly string[] SettingsTabKeys = { "settings.section.display", "settings.section.audio", "settings.section.graphics", "camera.title", "settings.section.data" };
            public static readonly string[] SettingsTabIds = { "DISPLAY", "AUDIO", "GRAPHICS", "CAMERA", "DATA" };

            /// <summary>S-09 S1 (G2): the word stays under every settings tab until AP-034 brings the Display glyph.</summary>
            public static readonly bool SettingsTabWords = true;

            /// <summary>C29 pager: two icon buttons (← / →) around the page; their hit is the pager's height.</summary>
            public const UiIconSize PagerIcon = UiIconSize.M;

            /// <summary>A read-only pane on the pad: one Up / Down scrolls this many wheel steps.</summary>
            public const float ReaderSteps = 3f;

            // Motion: the carrier's open / close is the kit's (UiBrushPane: C1 ScrimIn, C2 + marks Open, PaneIn after
            // ContentDelay; Close). The menu adds the rows' stagger (UiMotion; Decor - Reduced Motion lands it at once).
            public const UiDuration RowIn = UiDuration.Fast;            // each list row (140), StaggerRow apart, ≤ 6 steps
            public const UiDuration ContentIn = UiDuration.Base;        // UiPresets.PaneIn (220)

            /// <summary>The ceiling every open / close stays under (WP-098B: brush wipe ≤ 0.35 s).</summary>
            public const float MaxSeconds = 0.35f;

            /// <summary>Seconds until the opened pane is at rest (scrim, wash and marks, PaneIn, row stagger); 0 under Reduced Motion.</summary>
            public static float OpenSeconds
            {
                get
                {
                    if (UiMotion.ReducedMotion)
                    {
                        return 0f;
                    }

                    float carrier = Mathf.Max(UiMotionTokens.Seconds(UiBrushTokens.ScrimIn), UiMotionTokens.Seconds(UiBrushTokens.Open));
                    float content = UiBrushTokens.ContentDelay + UiMotionTokens.Seconds(ContentIn);
                    float rows = RowDelay(UiMotionTokens.StaggerRowMaxItems - 1) + UiMotionTokens.Seconds(RowIn);
                    float ground = 2f * UiMotionTokens.Seconds(UiBrushTokens.ScrimIn);   // Pane → Window: the Window C1 in, then the Pane C1 out
                    return Mathf.Max(Mathf.Max(carrier, content), Mathf.Max(rows, ground));
                }
            }

            /// <summary>Seconds until a closed pane is gone (content, C2 and C1 fade together); 0 under Reduced Motion.</summary>
            public static float CloseSeconds
            {
                get { return UiMotion.ReducedMotion ? 0f : UiMotionTokens.Seconds(UiBrushTokens.Close); }
            }

            /// <summary>The least ink under the pane's text: the scrim and the wash core combined (≥ UiBrushTokens.TextGroundMin).</summary>
            public static float TextGround
            {
                get { return UiBrushTokens.Combined(UiBrushTokens.Scrim(Scrim), UiBrushTokens.WashCore); }
            }

            /// <summary>Row <paramref name="index"/>'s delay: StaggerRow apart, the 7th+ share the 6th's.</summary>
            public static float RowDelay(int index)
            {
                return Mathf.Clamp(index, 0, UiMotionTokens.StaggerRowMaxItems - 1) * UiMotionTokens.StaggerRow;
            }

            /// <summary>
            /// WP-098B AD M2 · how far in from each edge an authored AP-034 C2 mask (ap034_kit_c02wash_tall / wide / square) keeps
            /// its soft edge, as a fraction of the mask on its softest axis (measured 08.10 from the PNGs: the plateau, α 0.91–0.96,
            /// starts 0.30 in on the wide mask's height, the tall mask's width and both axes of the square one). The masks are
            /// sliceless, so the kit stretches them: their soft edge grows with the rect instead of staying WashReach wide, and a
            /// wash sized "pane + WashReach" left the pane's outer ~30 % - the title, the help line, the first and last rows, the
            /// preview column - on the falloff (cap-5: 0.16–0.28 of the world showed through there, the scrim alone is 0.28).
            /// </summary>
            public const float WashFalloff = 0.31f;

            /// <summary>
            /// The C2's spill past the pane on each side, as a fraction of the pane's size: the least that puts the whole content
            /// column inside the mask's plateau (falloff / (1 − 2 × falloff); the kit's WashReach comes on top). Only for a
            /// sliceless mask; a 9-sliced one (the kit's SoftShadow fallback) keeps the kit's own WashReach geometry.
            /// </summary>
            public static float WashSpill
            {
                get { return WashFalloff / (1f - 2f * WashFalloff); }
            }

            /// <summary>
            /// The pane carrier on its column: the kit's frameless pane ("PaneCard", C1 on <paramref name="screen"/>, C2 under the
            /// content) and its one edge trace. Closed until Open. The column draws first in its page, under the spine (title,
            /// rail, level readout): the wash's spill lies beneath them, never over them (AP-028 main-profile: the spine stands on
            /// the same ink as the pane). The columns do not overlap, so no hit changes hands.
            /// </summary>
            public static UiBrushPane BuildCarrier(RectTransform screen, RectTransform column)
            {
                if (column.parent != screen)
                {
                    column.SetAsFirstSibling();   // first in the page; never on the screen root, where it would go under the backdrop
                }

                UiBrushPane pane = UiBrushKit.Pane(screen, column, CardName, Scrim, Wash);
                FitWash(pane);
                UiBrushKit.EdgeTrace(pane);
                return pane;
            }

            /// <summary>The Window-level C1 for the panes that stand on it: a kit pane with no wash and no content, closed until a pane needs it.</summary>
            public static UiBrushPane BuildGround(RectTransform screen, RectTransform column)
            {
                return UiBrushKit.Pane(screen, column, GroundName, WindowScrim, UiBrushWash.None);
            }

            /// <summary>
            /// Puts the menu on <paramref name="level"/> (after the carrier's Open in the same frame), through the kit only - the
            /// levels' α are the kit's tokens, the fades its C1 fades on the renderer:
            ///   Window  the ground opens (its C1 0 → 0.95, ScrimIn); the carrier's Pane C1 fades out under it (ScrimIn), after
            ///           the ground is in when it was showing (from Profile: 0.72 → 0.986 → 0.95, never lighter than 0.72), at
            ///           once when the carrier is just opening (0 → 0.95).
            ///   Pane    the carrier's C1 comes back (Instant, Settle) while the ground closes (Close, Exit: slow start), so
            ///           the ink goes 0.95 → 0.72 without a dip.
            ///   None    the ground closes with the carrier (the carrier's own Close fades its C1).
            /// Reduced Motion: every fade lands in the same frame. No allocation.
            /// </summary>
            public static void ApplyGround(UiBrushPane carrier, UiBrushPane ground, UiBrushScrim level)
            {
                if (ground == null)
                {
                    return;
                }

                Image own = carrier != null ? carrier.Scrim : null;
                int owner = carrier != null ? carrier.GetInstanceID() : 0;
                if (level == WindowScrim)
                {
                    float delay = own != null && own.canvasRenderer.GetAlpha() > 1e-3f ? UiMotionTokens.Seconds(UiBrushTokens.ScrimIn) : 0f;
                    if (!ground.IsOpen)
                    {
                        ground.Open(null);
                    }

                    UiMotion.TintAlpha(own, 0f, UiBrushTokens.ScrimIn, UiEase.Settle, UiMotionClass.Decor, delay, UiClock.Unscaled, owner);
                    return;
                }

                if (level != UiBrushScrim.None && own != null)
                {
                    UiMotion.TintAlpha(own, 1f, UiDuration.Instant, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
                }

                ground.Close();
            }

            /// <summary>The ink a pane's text stands on as drawn now: the two C1s (their level × their fade) and the carrier's C2.</summary>
            public static float DrawnGround(UiBrushPane carrier, UiBrushPane ground)
            {
                float ink = 0f;
                if (carrier != null && carrier.Scrim != null && carrier.Scrim.gameObject.activeInHierarchy)
                {
                    ink = UiBrushTokens.Combined(ink, carrier.Scrim.color.a * carrier.Scrim.canvasRenderer.GetAlpha());
                }

                if (ground != null && ground.Scrim != null && ground.Scrim.gameObject.activeInHierarchy)
                {
                    ink = UiBrushTokens.Combined(ink, ground.Scrim.color.a * ground.Scrim.canvasRenderer.GetAlpha());
                }

                if (carrier != null && carrier.Wash != null && carrier.Wash.gameObject.activeInHierarchy)
                {
                    ink = UiBrushTokens.Combined(ink, carrier.Wash.color.a * carrier.Wash.canvasRenderer.GetAlpha());
                }

                return ink;
            }

            /// <summary>
            /// WP-098B AD M2 · the kit's C2, sized so the content column (the title band to the last row, the help line and the
            /// preview column) lies in the mask's plateau, not on its soft edge: anchored to the pane (it follows a resize),
            /// spilling <see cref="WashSpill"/> of the pane's size plus WashReach past every side. The kit's wash, sprite, α (the
            /// linear WashCore), sibling place and ink-in stay as the kit made them; a 9-sliced mask (soft edge = WashReach
            /// already) is left alone. No-op without a wash. Build time only; no per-frame work.
            /// </summary>
            public static void FitWash(UiBrushPane pane)
            {
                Image wash = pane != null ? pane.Wash : null;
                if (wash == null || wash.type == Image.Type.Sliced)
                {
                    return;
                }

                float spill = WashSpill;
                RectTransform rect = wash.rectTransform;
                rect.anchorMin = new Vector2(-spill, -spill);
                rect.anchorMax = new Vector2(1f + spill, 1f + spill);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(-UiBrushTokens.WashReach, -UiBrushTokens.WashReach);
                rect.offsetMax = new Vector2(UiBrushTokens.WashReach, UiBrushTokens.WashReach);
            }

            /// <summary>
            /// The part of <paramref name="wash"/> at its full core, in world space: a sliceless mask inset by
            /// <see cref="WashFalloff"/> of its size, a 9-sliced one by WashReach (tests: the content column lies inside it).
            /// </summary>
            public static Rect WashPlateau(Image wash)
            {
                var corners = new Vector3[4];
                wash.rectTransform.GetWorldCorners(corners);
                Rect world = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                Vector2 inset;
                if (wash.type == Image.Type.Sliced)
                {
                    float reach = UiBrushTokens.WashReach * wash.rectTransform.lossyScale.x;
                    inset = new Vector2(reach, reach);
                }
                else
                {
                    inset = world.size * WashFalloff;
                }

                return Rect.MinMaxRect(world.xMin + inset.x, world.yMin + inset.y, world.xMax - inset.x, world.yMax - inset.y);
            }

            /// <summary>The title copy and glyph of a pane (the rail's icon: one glyph names the pane in both places).</summary>
            public static void TitleOf(UiContext pane, out string key, out string fallback, out string icon)
            {
                switch (pane)
                {
                    case UiContext.Statistics: key = "menu.tab.statistics"; fallback = "STATISTICS"; icon = "Menu.Statistics"; return;
                    case UiContext.Achievements: key = "menu.tab.achievements"; fallback = "ACHIEVEMENTS"; icon = "Menu.Achievements"; return;
                    case UiContext.Settings: key = "menu.tab.settings"; fallback = "SETTINGS"; icon = "Menu.Settings"; return;
                    case UiContext.Controls: key = "menu.tab.controls"; fallback = "CONTROLS"; icon = "Menu.Controls"; return;
                    case UiContext.Accessibility: key = "menu.tab.accessibility"; fallback = "ACCESSIBILITY"; icon = "Menu.Accessibility"; return;
                    default: key = "menu.tab.profile"; fallback = "PROFILE"; icon = "Menu.Profile"; return;
                }
            }

            /// <summary>
            /// C5 · the pane's heading: the kit title (48 px glyph, Display word, the header rule and the title brush on it) in
            /// its own band at the stack's cursor. The title brush registers with the pane; <paramref name="marks"/> collects it
            /// for a pane change (the carrier is already open, so the screen inks it in).
            /// </summary>
            public static UiBrushTitle Title(ref UiStack stack, RectTransform parent, UiContext pane, List<UiBrushMark> marks)
            {
                TitleOf(pane, out string key, out string fallback, out string icon);
                RectTransform band = UiFactory.NewRect(TitleName, parent);
                UiBrushTitle title = UiBrushKit.Title(band, key, fallback, icon);
                stack.Add(band, title.Height);
                if (marks != null && title.Stroke != null)
                {
                    marks.Add(title.Stroke);
                }

                return title;
            }

            /// <summary>
            /// C6 · a brush divider between a pane's bands (≤ UiBrushTokens.MaxDivider per pane: Profile and Statistics use
            /// two, the rest none) in a <see cref="DividerBand"/> band named <paramref name="name"/>.
            /// </summary>
            public static UiBrushMark Divider(ref UiStack stack, RectTransform parent, string name, List<UiBrushMark> marks)
            {
                RectTransform band = UiFactory.NewRect(name, parent);
                stack.Add(band, DividerBand);
                float width = parent.rect.width > 1f ? parent.rect.width : UiBrushTokens.ListWidth;
                UiBrushMark mark = UiBrushKit.Divider(band, width);
                if (marks != null && mark != null)
                {
                    marks.Add(mark);
                }

                return mark;
            }

            /// <summary>C6 · the plain 1 px rule in the line colour (no brush, no budget): section heads, list rows, the picker's halves.</summary>
            public static Image Rule(RectTransform parent, string name)
            {
                float width = parent.rect.width > 1f ? parent.rect.width : UiBrushTokens.ListWidth;
                Image rule = UiBrushKit.Hairline(parent, width);
                rule.name = name;
                rule.raycastTarget = false;
                return rule;
            }

            /// <summary>The settings strip's height: the tab (ControlHeight), its word (G2) and the C8 under it.</summary>
            public static float SettingsStripHeight
            {
                get
                {
                    float words = SettingsTabWords ? UiFactory.LineHeight(UiType.MenuSmall) : 0f;
                    return UiBrushTokens.ControlHeight + words + UiBrushTokens.SelectionGap + UiBrushTokens.SelectionCore + 2 * UiBrushTokens.SelectionBed;
                }
            }

            /// <summary>The five settings tabs (C12): object name, glyph, reader name and the word under the tab.</summary>
            public static UiBrushTab[] SettingsTabs()
            {
                var tabs = new UiBrushTab[SettingsTabIds.Length];
                for (int i = 0; i < tabs.Length; i++)
                {
                    tabs[i] = new UiBrushTab
                    {
                        ObjectName = SettingsTabPrefix + SettingsTabIds[i],
                        IconKey = SettingsTabIcons[i],
                        NameKey = SettingsTabKeys[i],
                        NameFallback = SettingsTabIds[i],
                        ShortKey = SettingsTabKeys[i],
                        ShortFallback = SettingsTabIds[i],
                    };
                }

                return tabs;
            }

            /// <summary>A list row's staggered entry (alpha only - rows in a scroller keep their measured places).</summary>
            public static UiTweenHandle FadeRowIn(CanvasGroup row, int index, int owner)
            {
                if (row == null)
                {
                    return default(UiTweenHandle);
                }

                row.alpha = 0f;
                return UiMotion.Alpha(row, 1f, RowIn, UiEase.Settle, UiMotionClass.Decor, RowDelay(index), UiClock.Unscaled, owner);
            }

            /// <summary>
            /// A pane change on an open carrier: the new content rises in (PaneIn after the kit's ContentDelay) and its brush
            /// marks ink in over the kit's Open, as the carrier's own Open does from closed. Decor: Reduced Motion lands both.
            /// </summary>
            public static void Reenter(RectTransform content, List<UiBrushMark> marks, int owner)
            {
                if (content != null)
                {
                    UiMotion.KillTarget(content, true);   // a rise still running lands first: PaneIn reads the rest position
                    UiPresets.PaneIn(UiFactory.Group(content.gameObject), content, UiBrushTokens.ContentDelay, owner);
                }

                for (int i = 0; marks != null && i < marks.Count; i++)
                {
                    if (marks[i] != null)
                    {
                        marks[i].Draw(UiBrushTokens.Open);
                    }
                }
            }

            // ---------------------------------------------------------------- content rows (WP-098B 2a)
            // The panes' rows are the kit's: C14 Slider, C15 Toggle, C16 Cycle (C13 setting rows, label | value), C18 ListRow
            // with its status glyph and trailing word (profile picker, achievements, binding slots), C35 Scroll. What is left
            // here is the menu's choice of glyph and word per state (pure, EditMode-tested) and the list measures.

            /// <summary>C18 status glyphs (G1 atlas). The choice is the check; a lock, a warning or a changed slot is its own shape.</summary>
            public const string CheckGlyph = "Selection.Selected";
            public const string LockGlyph = "State.Locked";
            public const string EarnedGlyph = "State.Unlocked";
            public const string ProgressGlyph = "Profile.Progress";
            public const string ConflictGlyph = "Notice.Warning";
            public const string ChangedGlyph = "Pip.Full";

            /// <summary>The C13 list never narrower than the E6 control column while it shares the pane with the preview well.</summary>
            public const float SettingsListMin = 720f;

            /// <summary>One C13 setting row: the kit's hit (56), taller when MenuBody's line box at the TextScale needs it.</summary>
            public static float SettingRowHeight
            {
                get { return UiBrushTokens.RowHeight(UiType.MenuBody, UiBrushTokens.RowHit); }
            }

            /// <summary>The air between two setting rows: the kit's step (64) less its hit (56).</summary>
            public static float SettingRowGap
            {
                get { return UiBrushTokens.RowStep - UiBrushTokens.RowHit; }
            }

            /// <summary>One compact C18 list row (picker, binding slots): the kit's compact hit (48), taller when MenuSmall needs it.</summary>
            public static float ListRowHeight
            {
                get { return UiBrushTokens.RowHeight(UiType.MenuSmall, UiBrushTokens.RowHitCompact); }
            }

            /// <summary>The air between two compact list rows: the kit's compact step (56) less its hit (48).</summary>
            public static float ListRowGap
            {
                get { return UiBrushTokens.RowStepCompact - UiBrushTokens.RowHitCompact; }
            }

            /// <summary>
            /// The C13 list's width on a pane <paramref name="paneWidth"/> wide (the scroller adds its gutter): the kit's
            /// ListWidth when the preview well (≥ PreviewMinWidth) still fits beside it; else as wide as leaves the well its
            /// minimum, down to <see cref="SettingsListMin"/>; below that the well yields and the list takes the pane (AD 6ab
            /// SHOULD-10: no blank strip wider than a gutter). An unlaid-out pane (width 0) gets ListWidth and the well.
            /// </summary>
            public static float SettingsList(float paneWidth, out bool well)
            {
                float gutter = UiScrollView.Gutter;
                if (paneWidth < 1f)
                {
                    well = true;
                    return UiBrushTokens.ListWidth;
                }

                if (paneWidth - (UiBrushTokens.ListWidth + gutter) >= PreviewMinWidth)
                {
                    well = true;
                    return UiBrushTokens.ListWidth;
                }

                float shared = paneWidth - PreviewMinWidth - gutter;
                if (shared >= SettingsListMin)
                {
                    well = true;
                    return Mathf.Floor(shared);
                }

                well = false;
                return Mathf.Max(0f, paneWidth - gutter);
            }

            /// <summary>
            /// A title / badge option's C18 status (G1): locked → the lock glyph and its condition (LOCKED · ROUND 10; the word
            /// the row always showed); chosen → the check glyph; a fresh unlock adds the word NEW. The tint never carries it.
            /// </summary>
            public static void OptionStatus(bool unlocked, bool selected, bool isNew, string lockReason, out string icon, out string trailing)
            {
                if (!unlocked)
                {
                    icon = LockGlyph;
                    trailing = string.IsNullOrEmpty(lockReason) ? UiStrings.Get("tree.state.locked", "LOCKED") : lockReason;
                    return;
                }

                icon = selected ? CheckGlyph : null;
                trailing = isNew ? UiStrings.Get("tree.flag.new", "NEW") : null;
            }

            /// <summary>An achievement row's C18 status: earned → the unlocked glyph + EARNED (NEW on its first visit); else the progress glyph + IN PROGRESS.</summary>
            public static void AchievementStatus(bool earned, bool isNew, out string icon, out string trailing)
            {
                if (earned)
                {
                    icon = EarnedGlyph;
                    trailing = isNew ? UiStrings.Get("tree.flag.new", "NEW") : UiStrings.Get("ach.earned", "EARNED");
                    return;
                }

                icon = ProgressGlyph;
                trailing = UiStrings.Get("ach.in_progress", "IN PROGRESS");
            }

            /// <summary>
            /// A binding slot's C18 status: the control's name as the trailing word (PRESS A KEY while it listens); a control two
            /// slots share takes the warning glyph (the notice under the column words it), a changed slot the marked glyph (its
            /// legend words it). Shape + words, never colour alone (slice 9 E3 R2).
            /// </summary>
            public static void BindStatus(bool listening, bool conflicted, bool overridden, string control, out string icon, out string trailing)
            {
                if (listening)
                {
                    icon = null;
                    trailing = UiStrings.Get("controls.listening", "PRESS A KEY");
                    return;
                }

                icon = conflicted ? ConflictGlyph : overridden ? ChangedGlyph : null;
                trailing = control;
            }
        }

        // The pane's live state (WP-098B).
        private UiBrushPane _brushPane;          // the kit's carrier: C1 scrim, C2 wash, edge trace, clipped Content (= _paneHost)
        private UiBrushPane _paneGround;         // S-04 08.10: the Window-level C1 for every reading pane, including Profile (K1; scrim only)
        private RectTransform _paneLeaving;      // the previous pane's content while it fades out under a pane change
        private UiTweenHandle _paneLeavingFade;
        private Coroutine _paneWatch;
        private Selectable _paneFocus;           // the open pane's first control: Right from its rail icon, focus reclaim
        private Selectable _paneClaim;           // a pane that always takes focus on open (Settings; Controls / Accessibility from the rail)
        private bool _paneCancel;                // the close in progress came from Cancel (the kit's HandleCancel: Back sound)
        private bool _paneRowsEnter;             // the pane being built is entering: its rows stagger in
        private bool _paneTurn;                  // an achievements page turn: the new page's rows stagger in
        private readonly List<RectTransform> _paneRows = new List<RectTransform>(32);
        private readonly List<UiBrushMark> _paneMarks = new List<UiBrushMark>(4);

        // Settings (C12): the tab strip and where each section starts.
        private UiBrushChoiceGroup _settingsTabs;
        private RectTransform[] _settingsHeads;
        private Selectable[] _settingsFirst;
        private UiScrollView _settingsScroll;
        private int _settingsSection = -1;

        /// <summary>The pane carrier (tests: its scrim, wash, ground alpha and open state).</summary>
        public UiBrushPane PaneCarrier
        {
            get { return _brushPane; }
        }

        /// <summary>The Window-level C1 all reading panes stand on (tests: open with them, closed at rest).</summary>
        public UiBrushPane PaneGround
        {
            get { return _paneGround; }
        }

        private void BuildPaneHost(RectTransform column)
        {
            // WP-098B: the kit's frameless carrier (scrim + wash + edge trace); clipped, so nothing spills over the rail. A
            // pane taller than the column scrolls (Profile, Statistics, the settings lists) or pages (Achievements).
            _brushPane = PaneLook.BuildCarrier(ScreenRect, column);
            _paneGround = PaneLook.BuildGround(ScreenRect, column);
            _paneHost = _brushPane.Content;
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
            UiContext from = _pane;
            _pane = pane;
            _focusPaneOnOpen = true;
            RedrawPane(from);
            _focusPaneOnOpen = false;
            UiEvents.Raise(UiEvent.PaneOpened, pane.ToString());
        }

        /// <summary>v4: no pane - the carrier closes (WP-098B: the kit's Close), the rail's tick goes, the navigation model leaves the layer.</summary>
        private void ClosePane()
        {
            UiContext from = _pane;
            if (_pane != UiContext.MainMenu)
            {
                UiNavigation.ExitLayer(_pane);
            }

            _pane = UiContext.MainMenu;
            RedrawPane(from);
        }

        /// <summary>Cancel on an open pane: the kit's HandleCancel (Close + the Back sound) through the same close path.</summary>
        private void CancelPane()
        {
            _paneCancel = true;
            try
            {
                ClosePane();
            }
            finally
            {
                _paneCancel = false;
            }
        }

        /// <summary>The open pane again, in place (a profile change, a language, a rebind, a page): no entry motion, the scroll and focus kept.</summary>
        private void RedrawPane()
        {
            RedrawPane(_pane);
        }

        /// <summary>
        /// Draws <see cref="_pane"/> coming from <paramref name="from"/>. WP-098B: a pane that opens from none opens the kit's
        /// carrier (input at once, the scrim, wash, edge, title brush and content come in over ≤ 0.35 s); a pane that replaces
        /// another cross-fades over it (the old content fades out on top while the new one rises in and its marks ink in);
        /// a pane redrawn in place (from == _pane) keeps its scroll offset and plays nothing.
        /// </summary>
        private void RedrawPane(UiContext from)
        {
            bool entering = from != _pane;
            UiButton open = TabOf(_pane);
            if (_paneMark != null)
            {
                if (open != null)
                {
                    // Inks in under a newly opened pane's icon (Reselect; a cut under RM); a redraw of the same pane lands it.
                    _paneMark.Under((RectTransform)open.transform, open != _paneMarkOn);
                }
                else
                {
                    _paneMark.Hide(true);
                }

                _paneMarkOn = open;
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

            // The pane's controls are about to be destroyed (or to fade out, inert): no tab may keep pointing
            // Right at one of them (an explicit link to a destroyed Selectable throws
            // inside the EventSystem on the next Right press).
            ClearRightLink(_profileTab);
            ClearRightLink(_statisticsTab);
            ClearRightLink(_achievementsTab);
            ClearRightLink(_settingsTab);
            ClearRightLink(_controlsTab);
            ClearRightLink(_accessibilityTab);
            _paneFocus = null;
            _paneClaim = null;

            if (_pane == UiContext.MainMenu)
            {
                PaneLook.ApplyGround(_brushPane, _paneGround, UiBrushScrim.None);

                // WP-098B: the kit closes the carrier (input off at once, a Close fade, then it deactivates itself); the
                // content stays while it fades, inert, and the watch clears it once the carrier is gone.
                if (entering && _brushPane != null && _brushPane.IsOpen)
                {
                    if (_paneCancel)
                    {
                        _brushPane.HandleCancel();
                    }
                    else
                    {
                        _brushPane.Close();
                    }

                    if (_brushPane.Root.gameObject.activeSelf)
                    {
                        WatchPane();
                        return;
                    }
                }

                FinishPaneClose();
                return;
            }

            bool fromClosed = _brushPane != null && !_brushPane.IsOpen;
            float keepOffset = -1f;
            if (!entering)
            {
                UiScrollView kept = _paneHost.GetComponentInChildren<UiScrollView>(true);
                keepOffset = kept != null ? kept.Offset : -1f;
                ClearChildren(_paneHost);
            }
            else if (fromClosed)
            {
                // From none (or from a close still fading): whatever the carrier held goes. The carrier is live while the
                // pane builds (text measures, controls wake); Open comes in the same frame, before anything is drawn.
                DropLeaving();
                ClearChildren(_paneHost);
                _brushPane.Root.gameObject.SetActive(true);
            }
            else
            {
                HandOverToLeaving();
            }

            _paneRows.Clear();
            _paneMarks.Clear();
            _paneRowsEnter = entering || _paneTurn;
            _paneTurn = false;
            _settingsTabs = null;

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

            _paneRowsEnter = false;
            int owner = GetInstanceID();

            // Gamepad (WP-098B): a pane opened from the rail takes focus on its first control - Settings always, Controls and
            // Accessibility from the rail; the reading panes do so on the pad only (a mouse click keeps its icon).
            Selectable focus = _paneClaim;
            if (focus == null && _focusPaneOnOpen && _paneFocus != null && UiInputScheme.Current == DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad)
            {
                focus = _paneFocus;
            }

            if (entering)
            {
                if (fromClosed)
                {
                    _brushPane.Open(focus != null ? focus : open);
                }
                else
                {
                    PaneLook.Reenter(_paneHost, _paneMarks, owner);
                }

                PaneLook.ApplyGround(_brushPane, _paneGround, PaneLook.ScrimOf(_pane));   // after the carrier's Open: its C1 level per pane
            }
            else if (keepOffset > 0f)
            {
                UiScrollView scroll = _paneHost.GetComponentInChildren<UiScrollView>(true);
                if (scroll != null)
                {
                    scroll.ScrollTo(keepOffset, false);
                }
            }

            _paneMarks.Clear();

            // Rows built while entering (or on a page turn) stagger in, StaggerRow apart.
            for (int i = 0; i < _paneRows.Count; i++)
            {
                if (_paneRows[i] != null)
                {
                    PaneLook.FadeRowIn(UiFactory.Group(_paneRows[i].gameObject), i, owner);
                }
            }

            _paneRows.Clear();

            if (focus != null)
            {
                UiInput.Focus(focus);
            }

            if (!string.IsNullOrEmpty(_refocusName))
            {
                Transform again = _paneHost.Find(_refocusName);
                if (again == null)
                {
                    again = FindDeep(_paneHost, _refocusName);   // an option inside the profile's scroller
                }

                _refocusName = null;
                UiButton button = again != null ? again.GetComponent<UiButton>() : null;
                if (button != null)
                {
                    UiInput.Focus(button);
                }
            }

            if (entering)
            {
                WatchPane();
            }
        }

        /// <summary>A pane that always takes focus on open (the kit's Open focuses it; an in-place redraw focuses it at once).</summary>
        private void ClaimPaneFocus(Selectable control)
        {
            _paneClaim = control;
            UiInput.Focus(control);
        }

        /// <summary>
        /// WP-098B · the old pane's content moves to a sibling over the new one (same rect as the kit's Content), inert, and
        /// fades out (PaneOut); the watch drops it.
        /// </summary>
        private void HandOverToLeaving()
        {
            DropLeaving();
            if (_paneHost == null || _paneHost.childCount == 0)
            {
                return;
            }

            RectTransform leaving = UiFactory.NewRect(PaneLook.LeavingName, _paneHost.parent);
            leaving.anchorMin = _paneHost.anchorMin;
            leaving.anchorMax = _paneHost.anchorMax;
            leaving.pivot = _paneHost.pivot;
            leaving.offsetMin = _paneHost.offsetMin;
            leaving.offsetMax = _paneHost.offsetMax;
            leaving.SetSiblingIndex(_paneHost.GetSiblingIndex() + 1);
            UiFactory.ClipContent(leaving);
            CanvasGroup group = UiFactory.Group(leaving.gameObject);
            CanvasGroup hostGroup = _paneHost.GetComponent<CanvasGroup>();
            group.alpha = hostGroup != null ? hostGroup.alpha : 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            while (_paneHost.childCount > 0)
            {
                _paneHost.GetChild(0).SetParent(leaving, false);
            }

            _paneLeaving = leaving;
            _paneLeavingFade = UiPresets.PaneOut(group, GetInstanceID());
            if (!UiMotion.IsAlive(_paneLeavingFade))
            {
                DropLeaving();   // Reduced Motion: the old pane goes at once
            }
        }

        private void DropLeaving()
        {
            UiMotion.Kill(_paneLeavingFade, false);
            _paneLeavingFade = default(UiTweenHandle);
            if (_paneLeaving == null)
            {
                return;
            }

            _paneLeaving.SetParent(null, false);
            Destroy(_paneLeaving.gameObject);
            _paneLeaving = null;
        }

        /// <summary>The closed carrier's end state: the kit has put the carrier away; its content is cleared (nothing kept inert).</summary>
        private void FinishPaneClose()
        {
            DropLeaving();
            ClearChildren(_paneHost);
        }

        /// <summary>
        /// WP-098B · one coroutine while a pane is open or fading: drops the cross-faded content when its fade ends, clears
        /// a closed pane once the kit's carrier has put itself away (its own Close cue - no timer here), and reclaims focus
        /// for the pad while a pane is open (UiInput.ReclaimFocus: a mouse click on empty space, then a pad press, lands on
        /// the pane's first control). No per-frame allocation (yield null).
        /// </summary>
        private void WatchPane()
        {
            if (_paneWatch == null && isActiveAndEnabled)
            {
                _paneWatch = StartCoroutine(PaneWatch());
            }
        }

        private System.Collections.IEnumerator PaneWatch()
        {
            while (true)
            {
                if (_paneLeaving != null && !UiMotion.IsAlive(_paneLeavingFade))
                {
                    DropLeaving();
                }

                if (_pane == UiContext.MainMenu)
                {
                    if (_brushPane == null || (!_brushPane.IsOpen && !_brushPane.Root.gameObject.activeSelf))
                    {
                        FinishPaneClose();
                        break;
                    }
                }
                else
                {
                    UiInput.ReclaimFocus(PaneFocusFallback());
                }

                yield return null;
            }

            _paneWatch = null;
        }

        /// <summary>Where a lost focus returns while a pane is open: its first control, else its rail icon.</summary>
        private Selectable PaneFocusFallback()
        {
            if (_paneFocus != null && _paneFocus.isActiveAndEnabled && _paneFocus.IsInteractable())
            {
                return _paneFocus;
            }

            return TabOf(_pane);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                Transform found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>A built row of the entering pane (its CanvasGroup staggers in after the build).</summary>
        private void EnterRow(Transform row)
        {
            if (_paneRowsEnter && row != null)
            {
                _paneRows.Add((RectTransform)row);
            }
        }

        /// <summary>
        /// WP-098B · a read-only pane on the pad (Statistics, the profile's lists): one focus stop on the scroller's content
        /// - Up / Down scroll it, at an end the move goes on through the explicit links (Left: the rail icon). The kit's
        /// focus brackets frame the visible list. Interactable only while the content is taller than its viewport.
        /// </summary>
        private sealed class PaneReader : Selectable
        {
            public UiScrollView Scroll;

            public override void OnMove(UnityEngine.EventSystems.AxisEventData eventData)
            {
                bool down = eventData.moveDir == UnityEngine.EventSystems.MoveDirection.Down;
                if (Scroll != null && (down || eventData.moveDir == UnityEngine.EventSystems.MoveDirection.Up))
                {
                    float max = Mathf.Max(0f, Scroll.Content.rect.height - Scroll.Viewport.rect.height);
                    float offset = Scroll.Offset;
                    if (down ? offset < max - 0.5f : offset > 0.5f)
                    {
                        float step = UiScrollView.WheelStep * PaneLook.ReaderSteps;
                        Scroll.ScrollTo(offset + (down ? step : -step), true);
                        eventData.Use();
                        return;
                    }
                }

                base.OnMove(eventData);
            }

            public override void OnSelect(UnityEngine.EventSystems.BaseEventData eventData)
            {
                base.OnSelect(eventData);
                if (Scroll != null)
                {
                    UiFocusBrackets.Follow(Scroll.Viewport);
                }
            }

            public override void OnDeselect(UnityEngine.EventSystems.BaseEventData eventData)
            {
                base.OnDeselect(eventData);
                if (Scroll != null)
                {
                    UiFocusBrackets.Release(Scroll.Viewport);
                }
            }
        }

        /// <summary>The reader on a scroller's content (after TrackSelectables - it must not reveal itself); null when it fits.</summary>
        private static PaneReader AttachReader(UiScrollView scroll, Selectable back)
        {
            if (scroll == null || !scroll.Scrolls)
            {
                return null;
            }

            PaneReader reader = scroll.Content.gameObject.AddComponent<PaneReader>();
            reader.Scroll = scroll;
            reader.transition = Selectable.Transition.None;
            UnityEngine.UI.Navigation navigation = reader.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            navigation.selectOnLeft = back;
            reader.navigation = navigation;
            return reader;
        }

        /// <summary>WP-098B (C12): a settings row tells the tab strip which section has focus (no polling).</summary>
        private sealed class SectionMark : MonoBehaviour, UnityEngine.EventSystems.ISelectHandler
        {
            public MainMenuScreen Owner;
            public int Section;

            public void OnSelect(UnityEngine.EventSystems.BaseEventData eventData)
            {
                if (Owner != null)
                {
                    Owner.MarkSettingsSection(Section, false);
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
            PaneLook.Title(ref head, _paneHost, UiContext.Settings, _paneMarks);
            AddCaptionUnder(ref head, _paneHost, "Caption",
                UiStrings.Get("settings.caption", "Device preferences. They are saved on this machine and never touch your profile."), 1, UiSpace.M);

            // WP-098B (AP-028 C12): the kit's icon tab strip over the list - one glyph per section, its word under it (G2)
            // and in the reader name, one C8 under the chosen tab. A tab jumps the list to its section; the chosen tab
            // follows the focused row.
            RectTransform strip = UiFactory.NewRect(PaneLook.SettingsStripName, _paneHost);
            head.Add(strip, PaneLook.SettingsStripHeight);
            head.Gap(UiSpace.M);
            BuildSettingsTabs(strip);

            RectTransform body;
            float column;
            float list;
            bool well;
            UiScrollView scroll = BuildControlColumn(head.Cursor, out body, out column, out list, out well);
            RectTransform content = scroll.Content;
            var stack = new UiStack(content);
            _settingsScroll = scroll;
            _settingsHeads = new RectTransform[PaneLook.SettingsTabIds.Length];
            _settingsFirst = new Selectable[PaneLook.SettingsTabIds.Length];

            // WP-098B 2a (C13–C16): every value row is the kit's - C16 Cycle for a choice of 2–6 ("◂ value ▸"), C14 Slider for
            // a range, C15 Toggle for ON / OFF - name left (MenuBody), value right, in the brush style; the row names stay.
            _settingsHeads[0] = AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.display", "DISPLAY"), false);
            UiStepper display = UiBrushKit.Cycle(content, "Display", "settings.row.display_mode", "DISPLAY MODE", DisplayModes(),
                UiSettings.Fullscreen ? 1 : 0, true, i => UiSettings.Fullscreen = i == 1, list);   // WP-022M: asynchronous on macOS (UiDisplayMode)
            AddSettingRow(ref stack, display.transform);
            UiSlider scale = UiBrushKit.Slider(content, "Setting.UI SCALE", "settings.row.ui_scale", "UI SCALE",
                UiTheme.UiScaleMin, UiTheme.UiScaleMax, UiSettings.UiScale, 0.05f, Percent,
                v =>
                {
                    UiSettings.UiScale = Mathf.Round(v * 20f) / 20f;
                    ShowPreviewScale(UiSettings.UiScale);
                }, list);
            AddSettingRow(ref stack, scale.transform);
            if (!well)
            {
                // AD 12c SHOULD-6: where the preview well yields (6ab SHOULD-10), its "next screen" caption stays under the slider.
                AddCaptionUnder(ref stack, content, "ScaleNote", UiStrings.Get("settings.note.ui_scale", "UI scale applies to the next screen you open."), 1, UiSpace.S);
            }

            UiSwitch hudFrame = UiBrushKit.Toggle(content, "HudFrame", "settings.row.hud_frame", "HUD IN A 16:9 FRAME", UiSettings.HudFrame169,
                on => UiSettings.HudFrame169 = on, list);   // 12c · B12 (AD 8-R SHOULD-A2)
            AddSettingRow(ref stack, hudFrame.transform);
            AddCaptionUnder(ref stack, content, "HudFrameNote",
                UiStrings.Get("settings.note.hud_frame", "On a screen wider than 16:9, the HUD's corners move in to a centred 16:9 frame."), 1, UiSpace.S);

            // WP-068 TR: the UI language. A change redraws this pane and relabels the menu at once (ChooseLanguage).
            UiStepper language = UiBrushKit.Cycle(content, "Language", "settings.row.language", "LANGUAGE", LanguageNames(),
                UiSettings.Language == UiStrings.Turkish ? 1 : 0, true, i => ChooseLanguage(i == 1 ? UiStrings.Turkish : UiStrings.English), list);
            AddSettingRow(ref stack, language.transform);
            _languageRow = language;

            _settingsHeads[1] = AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.audio", "AUDIO"), true);
            // Volumes are continuous (no detents; ← / → step 5 %), the UI scale has its six stops.
            UiSlider master = UiBrushKit.Slider(content, "Setting.MASTER VOLUME", "settings.row.master_volume", "MASTER VOLUME",
                0f, 1f, UiSettings.MasterVolume, 0f, Percent,
                v => UiSettings.MasterVolume = Mathf.Round(v * 20f) / 20f, list);
            AddSettingRow(ref stack, master.transform);
            // WP-068 U8: UI sounds on their own slider (the Interface bus).
            UiSlider uiSounds = UiBrushKit.Slider(content, "Setting.UI SOUNDS", "settings.row.ui_sounds", "UI SOUNDS",
                0f, 1f, UiSettings.UiVolume, 0f, Percent,
                v => UiSettings.UiVolume = Mathf.Round(v * 20f) / 20f, list);
            AddSettingRow(ref stack, uiSounds.transform);

            _settingsHeads[2] = AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.graphics", "GRAPHICS"), true);
            UiStepper quality = UiBrushKit.Cycle(content, "Setting.GRAPHICS QUALITY", "settings.row.quality", "QUALITY", QualityNames(),
                UiSettings.Quality, false, i => UiSettings.Quality = i, list);
            AddSettingRow(ref stack, quality.transform);

            // M4-R1 N2 (WP-070 §3): the third / first person rows. The mode itself is chosen in Pause (D-13); the look
            // fields are input preferences (input-settings.json, S-01's bridge feeds the camera), the rest ui-settings.
            _settingsHeads[3] = AddSettingsSection(ref stack, content, UiStrings.Get("camera.title", "CAMERA"), true);
            InputPreferences look = InputPreferencesStore.Current;
            UiSlider lookSpeed = UiBrushKit.Slider(content, "Setting.LOOK SENSITIVITY", "settings.row.look_sensitivity", "LOOK SENSITIVITY",
                1f, LookSteps, LookStepOf(look.LookSensitivity), 1f, v => Mathf.RoundToInt(v).ToString(),
                v => UpdatePreferences(p => p.LookSensitivity = LookSensitivityOf(v)), list);
            AddSettingRow(ref stack, lookSpeed.transform);
            UiSlider stickSpeed = UiBrushKit.Slider(content, "Setting.STICK LOOK SPEED", "settings.row.stick_look_speed", "STICK LOOK SPEED",
                InputPreferences.MinStickLookDegreesPerSecond, InputPreferences.MaxStickLookDegreesPerSecond, look.StickLookDegreesPerSecond, 10f,
                v => Mathf.RoundToInt(v) + "°/s", v => UpdatePreferences(p => p.StickLookDegreesPerSecond = Mathf.Round(v / 10f) * 10f), list);
            AddSettingRow(ref stack, stickSpeed.transform);
            UiSwitch invert = UiBrushKit.Toggle(content, "InvertLook", "settings.row.invert_look", "INVERT LOOK", look.LookInvertY,
                on => UpdatePreferences(p => p.LookInvertY = on), list);
            AddSettingRow(ref stack, invert.transform);
            UiSlider fpFov = UiBrushKit.Slider(content, "Setting.FIRST PERSON FOV", "camera.fov.first_person", "FIRST-PERSON FOV",
                CameraSystem.CameraModeSettings.MinFirstPersonFov, CameraSystem.CameraModeSettings.MaxFirstPersonFov, UiSettings.FirstPersonFov, 1f,
                Degrees, v => UiSettings.FirstPersonFov = Mathf.Round(v), list);
            AddSettingRow(ref stack, fpFov.transform);
            UiSlider tpFov = UiBrushKit.Slider(content, "Setting.THIRD PERSON FOV", "camera.fov.third_person", "THIRD-PERSON FOV",
                CameraSystem.CameraModeSettings.MinThirdPersonFov, CameraSystem.CameraModeSettings.MaxThirdPersonFov, UiSettings.ThirdPersonFov, 1f,
                Degrees, v => UiSettings.ThirdPersonFov = Mathf.Round(v), list);
            AddSettingRow(ref stack, tpFov.transform);
            UiStepper shoulder = UiBrushKit.Cycle(content, "Shoulder", "camera.shoulder", "SHOULDER", ShoulderNames(), UiSettings.Shoulder, true,
                i => UiSettings.Shoulder = i, list);
            AddSettingRow(ref stack, shoulder.transform);
            UiSlider bob = UiBrushKit.Slider(content, "Setting.HEAD BOB", "camera.head_bob", "HEAD BOB", 0f, 1f, UiSettings.HeadBobScale, 0.05f, Percent,
                v => UiSettings.HeadBobScale = Mathf.Round(v * 20f) / 20f, list);
            AddSettingRow(ref stack, bob.transform);
            UiSwitch dot = UiBrushKit.Toggle(content, "CentreDot", "settings.row.centre_dot", "CENTRE DOT", UiSettings.CentreDot, on => UiSettings.CentreDot = on, list);
            AddSettingRow(ref stack, dot.transform);
            AddCaptionUnder(ref stack, content, "CameraNote",
                UiStrings.Get("settings.note.camera", "Third and first person only; switch the view in Pause. A low field of view causes more motion sickness; Reduced Motion (Accessibility) sets head bob to 0 and cuts the blends."), 3, UiSpace.S);   // AD R1-A SHOULD-8

            _settingsHeads[4] = AddSettingsSection(ref stack, content, UiStrings.Get("settings.section.data", "DATA"), true);
            UiSwitch telemetry = UiBrushKit.Toggle(content, "Telemetry", "settings.row.telemetry", "RUN TELEMETRY", UiSettings.Telemetry,
                on => UiSettings.Telemetry = on, list);   // WP-022E / WP-045: local files only, no identity data
            AddSettingRow(ref stack, telemetry.transform);
            AddCaptionUnder(ref stack, content, "TelemetryNote",
                UiStrings.Get("settings.note.telemetry", "Run telemetry writes one local file per run (rounds, kills, weapon use) for tuning. It never contains your profile or name."), 1, UiSpace.S);
            UiSwitch hints = UiBrushKit.Toggle(content, "Hints", "settings.row.hints", "HINTS", UiSettings.Hints,
                on => UiSettings.Hints = on, list);       // WP-068 slice 10b (U9)
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

            // WP-098B (C12): each section's rows tell the strip they have focus; a tab's Down is its section's first row.
            _settingsFirst[0] = display.Button;
            _settingsFirst[1] = master;
            _settingsFirst[2] = quality.Button;
            _settingsFirst[3] = lookSpeed;
            _settingsFirst[4] = telemetry.Button;
            MarkSection(0, display.Button, scale, hudFrame.Button, language.Button);
            MarkSection(1, master, uiSounds);
            MarkSection(2, quality.Button);
            MarkSection(3, lookSpeed, stickSpeed, invert.Button, fpFov, tpFov, shoulder.Button, bob, dot.Button);
            MarkSection(4, telemetry.Button, hints.Button);
            for (int i = 0; _settingsTabs != null && i < _settingsTabs.Count; i++)
            {
                UiInput.LinkVertical(_settingsTabs[i], null, _settingsFirst[i]);
            }

            if (_settingsTabs != null && _settingsTabs.Count > 0)
            {
                UiInput.LinkHorizontal(_settingsTabs[0], _settingsTab, null);
                UiInput.LinkVertical(display.Button, _settingsTabs[0], null);   // Up from the first row: the strip (Hints still wraps Down to it)
            }

            UiInput.LinkHorizontal(_settingsTab, null, display.Button);
            _paneFocus = display.Button;
            _settingsSection = 0;   // the strip was built on DISPLAY: its C8 inks in with the pane
            ClaimPaneFocus(display.Button);
        }

        /// <summary>
        /// WP-098B (C12): the kit's tab strip - five icon tabs (72 × 64, step 88), the word under each (G2), one C8 under the
        /// chosen tab; ←/→ walk the strip, Submit picks a tab (focus alone does not: no rebuild per arrow press).
        /// </summary>
        private void BuildSettingsTabs(RectTransform strip)
        {
            _settingsSection = -1;
            _settingsTabs = UiBrushKit.Tabs(strip, PaneLook.SettingsTabsName, PaneLook.SettingsTabs(), 0, PickSettingsTab, PaneLook.SettingsTabWords);
            if (_settingsTabs != null && _brushPane != null)
            {
                _brushPane.Track(_settingsTabs.Mark);   // the C8 inks in with the carrier's Open (KILAVUZ §4.2)
                _paneMarks.Add(_settingsTabs.Mark);
            }
        }

        /// <summary>A settings tab picked (Submit / click): the group has already moved its C8; the list jumps to the section.</summary>
        private void PickSettingsTab(int section)
        {
            _settingsSection = section;
            JumpToSettingsSection(section);
        }

        private void MarkSection(int section, params Selectable[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] != null)
                {
                    SectionMark mark = rows[i].gameObject.AddComponent<SectionMark>();
                    mark.Owner = this;
                    mark.Section = section;
                }
            }
        }

        /// <summary>
        /// The chosen settings tab follows the focused row (SectionMark): the kit group tints the glyphs and redraws its C8
        /// under the tab; Up from the list's first row goes to the chosen tab. <paramref name="jump"/> (a tab pressed): the
        /// list scrolls its section's head to the top and focus goes to the section's first row.
        /// </summary>
        private void MarkSettingsSection(int section, bool jump)
        {
            if (_settingsTabs == null || section < 0 || section >= _settingsTabs.Count)
            {
                return;
            }

            if (section != _settingsSection)
            {
                _settingsSection = section;
                _settingsTabs.Select(section, false);
                if (_settingsFirst != null && _settingsFirst[0] != null)
                {
                    UiInput.LinkVertical(_settingsFirst[0], _settingsTabs[section], null);
                }
            }

            if (jump)
            {
                JumpToSettingsSection(section);
            }
        }

        private void JumpToSettingsSection(int section)
        {
            if (_settingsFirst != null && section >= 0 && section < _settingsFirst.Length && _settingsFirst[section] != null)
            {
                UiInput.Focus(_settingsFirst[section]);
            }

            if (_settingsScroll != null && _settingsHeads != null && section >= 0 && section < _settingsHeads.Length && _settingsHeads[section] != null)
            {
                _settingsScroll.ScrollTo(-_settingsHeads[section].anchoredPosition.y, true);
            }
        }

        /// <summary>
        /// E6 / E8 pane body under a head <paramref name="top"/> px tall: the scrolled control
        /// column (≤ 720 + the rail's gutter) on the left, and whether the right well fits.
        /// </summary>
        private UiScrollView BuildControlColumn(float top, out RectTransform body, out float column, out float list, out bool well)
        {
            body = UiFactory.NewRect("Body", _paneHost);
            UiFactory.Fill(body);
            body.offsetMax = new Vector2(0f, -top);

            // WP-098B (C13): the kit's list width (1008) where the well still fits beside it, else narrower down to the E6
            // column; without the well the list takes the pane (AD 6ab SHOULD-10).
            list = PaneLook.SettingsList(_paneHost.rect.width, out well);
            column = list + UiScrollView.Gutter;
            UiScrollView scroll = UiBrushKit.Scroll(body, "Scroll");   // C35
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

        /// <summary>
        /// WP-098B (C13): one kit setting row - the kit's hit (56) or MenuBody's line box + 8 at the TextScale, whichever is
        /// taller (measured, never a fixed band), then the rest of the kit's 64 step.
        /// </summary>
        private void AddSettingRow(ref UiStack stack, Transform row)
        {
            stack.Add((RectTransform)row, PaneLook.SettingRowHeight);
            stack.Cursor += PaneLook.SettingRowGap;
            EnterRow(row);
        }

        /// <summary>
        /// E6 section head on one line: the label (MenuSmall, TextSecondary - on the wash core), then the plain 1 px rule
        /// running to the column's edge (WP-098B, C6: a section head is a row rule, not a brush divider - the pane's brush
        /// budget stays with its title and C8). Returns the head (the settings tabs scroll to it).
        /// </summary>
        private static RectTransform AddSettingsSection(ref UiStack stack, RectTransform parent, string title, bool gapBefore)
        {
            if (gapBefore)
            {
                stack.Gap(UiSpace.S);   // + the last row's XS: 12 between a section's rows and the next head
            }

            RectTransform head = UiFactory.NewRect("Section." + title, parent);
            stack.Add(head, UiFactory.LineHeight(UiType.MenuSmall));
            TMP_Text label = UiFactory.Label("Label", head, title, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Fill(label.rectTransform);
            float x = Mathf.Ceil(label.GetPreferredValues(title).x) + (int)UiSpace.M;

            Image rule = PaneLook.Rule(head, "Rule");
            RectTransform ruleRect = rule.rectTransform;
            ruleRect.anchorMin = new Vector2(0f, 0.5f);
            ruleRect.anchorMax = new Vector2(1f, 0.5f);
            ruleRect.pivot = new Vector2(0f, 0.5f);
            ruleRect.offsetMin = new Vector2(x, -0.5f);
            ruleRect.offsetMax = new Vector2(0f, 0.5f);

            stack.Gap(UiSpace.XS);
            return head;
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

            TMP_Text label = UiFactory.Label("Label", well, UiStrings.Get("settings.preview", "PREVIEW"), UiType.Label, UiColor.TextSecondary);
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
            _previewCaption = UiFactory.Label("Caption", well, string.Empty, UiType.Caption, UiColor.TextSecondary);
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

        // ------------------------------------------------------------ controls pane

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
            PaneLook.Title(ref stack, _paneHost, UiContext.Controls, _paneMarks);
            AddCaptionUnder(ref stack, _paneHost, "Intro",
                UiStrings.Get("controls.intro", "Select a slot and press the key or button you want. Escape cancels. Overrides are saved on this machine."), 1, UiSpace.M);

            IInputRemapService remap = PrototypeInputInstaller.Instance != null ? PrototypeInputInstaller.Instance.Remap : null;
            var focusables = new List<Selectable>(16);

            UiScrollView scroll = UiBrushKit.Scroll(_paneHost, "ControlsScroll");   // C35
            ((RectTransform)scroll.transform).offsetMax = new Vector2(0f, -stack.Cursor);
            stack = new UiStack(scroll.Content);
            RectTransform columns = UiFactory.NewRect("Columns", scroll.Content);
            stack.Add(columns, 0f);
            RectTransform left = UiFactory.NewRect("Keyboard", columns);
            RectTransform right = UiFactory.NewRect("Gamepad", columns);

            // Gamepad options under the gamepad column (WP-098B 2a: the kit's rows - the legacy uGUI slider and the
            // outlined state chips are gone). The kit's rows keep a fixed value column (ValueColumn + gap = 408) and give the
            // label what is left, so the toggle and the cycle are made first and their measured words choose the layout:
            // side by side where the half column holds every option's word at this TextScale, else Keyboard over Gamepad,
            // each the pane's width (the Mac at UI 1.2 × text 1.25: a half column leaves the label ~68 px - RUMBLE was cut).
            InputPreferences prefs = InputPreferencesStore.Current;
            UiStepper assist = UiBrushKit.Cycle(right, "AimAssist", "controls.aim_assist", "AIM ASSIST", AimAssistNames(),
                (int)prefs.AimAssist, true, i => UpdatePreferences(p => p.AimAssist = (InputAimAssistLevel)i));   // C16
            UiSwitch rumble = UiBrushKit.Toggle(right, "Rumble", "controls.rumble", "RUMBLE", prefs.RumbleEnabled,
                on => UpdatePreferences(p => p.RumbleEnabled = on));   // C15
            string deadzoneLabel = UiStrings.Get("controls.deadzone", "STICK DEAD ZONE");
            // The slider's label column is fixed at build (width − 408): its word is measured in the same MenuBody face.
            float optionsWidth = Mathf.Max(Mathf.Max(KitRowWidthFor(assist.Label, assist.Label.text), KitRowWidthFor(rumble.Label, rumble.Label.text)),
                Mathf.Ceil(rumble.Label.GetPreferredValues(deadzoneLabel).x) + (UiBrushTokens.ListWidth - UiBrushTokens.LabelColumn));
            float columnsWidth = columns.rect.width;
            bool stacked = columnsWidth > 1f && optionsWidth > columnsWidth * 0.5f - (int)UiSpace.M;
            if (stacked)
            {
                TopBand(left, 0f);
                TopBand(right, 0f);   // placed under the keyboard column once its height is known
            }
            else
            {
                UiFactory.Fill(left);
                left.anchorMax = new Vector2(0.5f, 1f);
                left.offsetMax = new Vector2(-(int)UiSpace.M, 0f);
                UiFactory.Fill(right);
                right.anchorMin = new Vector2(0.5f, 0f);
                right.offsetMin = new Vector2((int)UiSpace.M, 0f);
            }

            var leftStack = new UiStack(left);
            var rightStack = new UiStack(right);
            var leftButtons = new List<Selectable>(8);
            var rightButtons = new List<Selectable>(8);
            AddRemapColumn(ref leftStack, UiStrings.Get("controls.keyboard", "KEYBOARD"), DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse, remap, leftButtons);
            AddRemapColumn(ref rightStack, UiStrings.Get("controls.gamepad", "GAMEPAD"), DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad, remap, rightButtons);

            float column = right.rect.width > 1f ? right.rect.width : UiBrushTokens.ListWidth * 0.5f;
            rightStack.Gap(UiSpace.M);
            UiSlider deadzone = UiBrushKit.Slider(right, "Setting.STICK DEAD ZONE", "controls.deadzone", "STICK DEAD ZONE", 0f, 0.5f, prefs.StickDeadzone, 0f,
                v => UiStrings.Percent(Mathf.RoundToInt(v * 100f)), v => UpdatePreferences(p => p.StickDeadzone = v), column);   // C14
            AddSettingRow(ref rightStack, deadzone.transform);
            // Slice 9 · E3 T3 (direction E7): what the dead zone does, in one sentence under its slider.
            AddCaptionUnder(ref rightStack, right, "DeadzoneNote",
                UiStrings.Get("controls.deadzone.note", "How far a stick moves before it counts. Raise it if your aim drifts on its own."), 2, UiSpace.S);
            // AD v006 MUST-6: AIM ASSIST's three levels (Off / Light / Full) - the kit's C16 cycle (WP-059: the level is WP-024 data).
            assist.transform.SetAsLastSibling();   // made first for the measure; the hierarchy keeps the visual order
            AddSettingRow(ref rightStack, assist.transform);
            rumble.transform.SetAsLastSibling();
            AddSettingRow(ref rightStack, rumble.transform);
            rightButtons.Add(deadzone);
            rightButtons.Add(assist.Button);
            rightButtons.Add(rumble.Button);

            // Reserve the actual scaled gamepad options as well as the keyboard rows.
            float columnsHeight;
            if (stacked)
            {
                left.sizeDelta = new Vector2(0f, leftStack.Cursor);
                float rightTop = leftStack.Cursor + (int)UiSpace.XL;
                TopBand(right, rightTop);
                right.sizeDelta = new Vector2(0f, rightStack.Cursor);
                columnsHeight = rightTop + rightStack.Cursor;
            }
            else
            {
                columnsHeight = Mathf.Max(leftStack.Cursor, rightStack.Cursor);
            }

            columns.sizeDelta = new Vector2(0f, columnsHeight);
            stack.Cursor += columnsHeight;

            // AD v006 MUST-7: reset as a full-width footer row under both columns, the
            // overrides file name right-aligned on the same row.
            stack.Gap(UiSpace.XL);
            RectTransform footer = UiFactory.NewRect("Footer", scroll.Content);
            stack.Add(footer, UiBrushTokens.ControlHeight);
            // Slice 9 · E3 R1 (direction E7, certain): destructive, so a 500 ms hold - a click or a pad press alone does nothing.
            // WP-098B 2a: the kit's C11 (unfilled glyph + MenuBody word), worded as the destructive act - never coloured.
            UiButton reset = UiBrushKit.Button(footer, "ResetBindings", "controls.reset", "HOLD TO RESET ALL BINDINGS", "Menu.Retry", () =>
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
            // As wide as the kit made it, never narrower than its localized word at this TextScale (measured) beside the glyph cell.
            float labelWidth = Mathf.Ceil(reset.LabelText.GetPreferredValues(reset.LabelText.text).x) + UiBrushTokens.IconButtonWidth + (int)UiSpace.L;
            float resetWidth = Mathf.Max(Mathf.Max(resetRect.sizeDelta.x, UiBrushTokens.LabelButtonMin), labelWidth);
            if (scroll.Content.rect.width > 1f)
            {
                resetWidth = Mathf.Min(scroll.Content.rect.width, resetWidth);
            }

            resetRect.sizeDelta = new Vector2(resetWidth, UiBrushTokens.ControlHeight);
            resetRect.anchoredPosition = Vector2.zero;
            TMP_Text file = UiFactory.Label(
                "File", footer, remap != null ? UiStrings.Format("controls.file", "value", Path.GetFileName(remap.OverridesFilePath), "Overrides: {value}") : UiStrings.Get("controls.no_service", "Input service not running."),
                UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleRight);
            RectTransform fileRect = UiFactory.Fill((RectTransform)file.transform);
            fileRect.offsetMin = new Vector2(resetWidth + (int)UiSpace.XL, 0f);
            // MenuSmall beside a MenuBody word: where the line does not fit (TR at 1.25 on the Mac) it wraps and the footer
            // grows to its measured height - never cut.
            file.enableWordWrapping = true;
            float fileWidth = scroll.Content.rect.width - resetWidth - (int)UiSpace.XL;
            if (fileWidth > 1f)
            {
                float band = Mathf.Max(UiBrushTokens.ControlHeight, Mathf.Ceil(file.GetPreferredValues(file.text, fileWidth, 0f).y));
                if (band > UiBrushTokens.ControlHeight)
                {
                    footer.sizeDelta = new Vector2(footer.sizeDelta.x, band);
                    stack.Cursor += band - UiBrushTokens.ControlHeight;
                }
            }
            leftButtons.Add(reset);
            scroll.SetContentHeight(stack.Cursor);
            scroll.TrackSelectables();

            if (stacked)
            {
                // One column: keyboard slots, gamepad slots and options, then the reset - Down walks them in reading order,
                // Left goes back to the rail's Controls icon.
                var order = new List<Selectable>(leftButtons.Count + rightButtons.Count);
                order.AddRange(leftButtons.GetRange(0, leftButtons.Count - 1));
                order.AddRange(rightButtons);
                order.Add(reset);
                UiInput.ChainVertical(false, order.ToArray());
                for (int i = 0; i < order.Count; i++)
                {
                    UiInput.LinkHorizontal(order[i], _controlsTab, null);
                }
            }
            else
            {
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
            }

            if (leftButtons.Count > 0)
            {
                UiInput.LinkHorizontal(_controlsTab, null, leftButtons[0]);
                _paneFocus = leftButtons[0];
                if (!stacked)
                {
                    UiInput.LinkVertical(reset, leftButtons.Count > 1 ? leftButtons[leftButtons.Count - 2] : null, null);
                    if (rightButtons.Count > 0)
                    {
                        UiInput.LinkVertical(rightButtons[rightButtons.Count - 1], null, reset);
                    }
                }

                if (_focusPaneOnOpen)
                {
                    ClaimPaneFocus(leftButtons[0]); // AD v006 MUST-8
                }
            }
        }

        /// <summary>
        /// WP-098B (F3): the narrowest width a kit setting row (C15 toggle, C16 cycle) shows <paramref name="word"/> whole at -
        /// the word measured in the row label's face at this TextScale, plus what the kit keeps beside it (its value column,
        /// the gap, the label's insets: the label stretches across the row).
        /// </summary>
        private static float KitRowWidthFor(TMP_Text label, string word)
        {
            RectTransform rect = label.rectTransform;
            return Mathf.Ceil(label.GetPreferredValues(word).x) + rect.offsetMin.x - rect.offsetMax.x;
        }

        /// <summary>A full-width band hung from the parent's top, <paramref name="top"/> below it (height: its sizeDelta.y).</summary>
        private static void TopBand(RectTransform rect, float top)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(0f, -top);
        }

        private void AddRemapColumn(ref UiStack stack, string heading, DD.Prototype.Core.InputAbstraction.InputSchemeKind scheme, IInputRemapService remap, List<Selectable> buttons)
        {
            TMP_Text label = UiFactory.Label("Label", stack.Parent, heading, UiType.MenuSmall, UiColor.TextSecondary);   // WP-098B: the brush pane's small text
            stack.Add((RectTransform)label.transform, UiFactory.LineHeight(UiType.MenuSmall));
            stack.Gap(UiSpace.XS);

            if (remap == null)
            {
                return;
            }

            IReadOnlyList<InputRemapEntry> entries = remap.Entries;
            // Slice 9 · E3 R2 (direction E7): one control bound to two slots of this scheme is a conflict - both actions
            // answer it. Those slots take the warning glyph and one Warning notice under the column names them (shape + words).
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
                stack.Add(row, PaneLook.ListRowHeight);
                stack.Cursor += PaneLook.ListRowGap;
                EnterRow(row);

                // WP-098B 2a (C18 / C13 in the controls' dense rows): the slot is one kit list row - the action's name left,
                // the control's name as its trailing word (measured by the kit - no outlined key-cap chip), PRESS A KEY
                // while it listens; a conflicted slot takes the warning glyph, a changed one the marked glyph (shape + the
                // column's words, never colour alone). "Slot.<id>/Bind.<id>" keeps the rebind's refocus path.
                bool listening = _rebindingEntryId == e.Id;
                bool conflicted = !listening && !string.IsNullOrEmpty(e.EffectivePath) && conflictedPaths.Contains(e.EffectivePath);
                PaneLook.BindStatus(listening, conflicted, e.IsOverridden, ControlName(e.EffectivePath, e.DisplayName), out string icon, out string trailing);
                string entryId = e.Id;
                UiButton slot = UiBrushKit.ListRow(row, "Bind." + e.Id, RemapRowName(e), icon, () => StartRebind(remap, entryId));
                UiFactory.Fill((RectTransform)slot.transform);
                UiBrushKit.SetRowStatus(slot, icon, trailing);
                anyOverridden |= e.IsOverridden && !listening;
                buttons.Add(slot);
            }

            // The marked glyph's word (kit rule: never a shape alone), once per column and only when a slot shows it.
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

        /// <summary>The C16 cycle's values in the level's order (Off 0, Light 1, Full 2; WP-059: InputPreferences.AimAssistLevel is the source).</summary>
        private static string[] AimAssistNames()
        {
            return new[] { AimAssistLabel(InputAimAssistLevel.Off), AimAssistLabel(InputAimAssistLevel.Light), AimAssistLabel(InputAimAssistLevel.Full) };
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
            PaneLook.Title(ref head, _paneHost, UiContext.Accessibility, _paneMarks);
            AddCaptionUnder(ref head, _paneHost, "Intro",
                UiStrings.Get("access.caption", "These change how the game is presented, never how it plays."), 1, UiSpace.M);

            RectTransform body;
            float column;
            float list;
            bool well;
            UiScrollView scroll = BuildControlColumn(head.Cursor, out body, out column, out list, out well);
            RectTransform content = scroll.Content;
            var stack = new UiStack(content);

            // WP-098B 2a: the kit's C16 / C14 / C15 rows (as Settings), the names kept.
            AddSettingsSection(ref stack, content, UiStrings.Get("access.section.colour", "COLOUR"), false);
            UiStepper palette = UiBrushKit.Cycle(content, "Palette", "access.row.palette", "PALETTE", PaletteModes(), UiSettings.ColorPalette, true,
                i =>
                {
                    UiSettings.ColorPalette = i;
                    RefreshAccessibilitySample();
                }, list);
            AddSettingRow(ref stack, palette.transform);
            AddCaptionUnder(ref stack, content, "PaletteNote",
                UiStrings.Get("access.note.palette", "Colour-blind safe moves health, earned and unlocked states from green to blue. Danger stays red; every state also has a shape and a word."), 2, UiSpace.XS);

            AddSettingsSection(ref stack, content, UiStrings.Get("access.section.text", "TEXT"), true);
            UiSlider text = UiBrushKit.Slider(content, "Setting.TEXT SCALE", "access.row.text_scale", "TEXT SCALE",
                UiSettings.TextScaleMin, UiSettings.TextScaleMax, UiSettings.TextScale, 0f, Percent,
                v =>
                {
                    UiSettings.TextScale = v;
                    RefreshAccessibilitySample();
                }, list);
            text.SetStops(TextScaleStops);
            AddSettingRow(ref stack, text.transform);
            AddCaptionUnder(ref stack, content, "TextNote",
                UiStrings.Get("access.note.text_scale", "Body, label and caption text. It applies to the next screen you open."), 1, UiSpace.XS);

            AddSettingsSection(ref stack, content, UiStrings.Get("access.section.motion", "MOTION"), true);
            UiSwitch flashes = UiBrushKit.Toggle(content, "ReduceFlashes", "access.row.reduce_flashes", "REDUCE FLASHES", UiSettings.ReduceFlashes,
                on =>
                {
                    UiSettings.ReduceFlashes = on;
                    RefreshAccessibilitySample();
                    PlayAccessibilitySample();
                }, list);
            AddSettingRow(ref stack, flashes.transform);
            AddCaptionUnder(ref stack, content, "FlashNote",
                UiStrings.Get("access.note.flashes", "Damage and final-seconds flashes at 40%. Blood and the world are not changed. Camera shake has its own slider on the pause screen."), 2, UiSpace.S);
            UiSwitch reduced = UiBrushKit.Toggle(content, "ReducedMotion", "access.row.reduced_motion", "REDUCED MOTION", UiSettings.ReducedMotion,
                on =>
                {
                    UiSettings.ReducedMotion = on;
                    PlayAccessibilitySample();
                }, list);
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
            _paneFocus = palette.Button;
            if (_focusPaneOnOpen)
            {
                ClaimPaneFocus(palette.Button); // AD v006 MUST-8: focus lands on the first control
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

            TMP_Text label = UiFactory.Label("Label", well, UiStrings.Get("access.sample", "SAMPLE"), UiType.Label, UiColor.TextSecondary);
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
            TMP_Text note = UiFactory.Label("ScreenNote", well, UiStrings.Get("access.sample.edge", "Damage edge and final-seconds bands at their peak."), UiType.Caption, UiColor.TextSecondary);
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

        /// <summary>The sample well's state chips (a HUD sample, not a pane row).</summary>
        private const float StateChipHeight = 24f;

        private static TMP_Text AddCaptionUnder(ref UiStack stack, RectTransform parent, string name, string text, int lines = 2, UiSpace after = UiSpace.XL)
        {
            stack.Gap(UiSpace.S);
            // WP-098B (KILAVUZ §0 T2/T3): the brush pane's small text - MenuSmall in TextSecondary (TextMuted is not text on the wash).
            TMP_Text caption = UiFactory.Label(name, parent, text, UiType.MenuSmall, UiColor.TextSecondary);
            // WP-068 (D6): the caption is as tall as its text at the pane width and the TextScale;
            // a fixed two-line band wasted a line under one-line captions (the Settings note fell
            // off the pane when HINTS arrived) and clipped at 1.25. Unlaid-out parents keep the old band.
            float width = parent.rect.width;
            float height = width > 1f
                ? Mathf.Max(UiFactory.LineHeight(UiType.MenuSmall), Mathf.Ceil(caption.GetPreferredValues(text, width, 0f).y))
                : UiFactory.LineHeight(UiType.MenuSmall) * lines;
            stack.Add((RectTransform)caption.transform, height);
            stack.Gap(after);
            return caption;
        }

        // ------------------------------------------------------------- profile pane

        private void BuildProfilePane(ProfileViewModel model)
        {
            // WP-098B: the pane scrolls where it outgrows the column (TextScale 1.25, a Mac window) instead of being cut at
            // the card edge; the picker's options pull themselves into view, the reader stop covers the lists past them.
            UiScrollView scroll = UiBrushKit.Scroll(_paneHost, PaneLook.ScrollName);   // C35
            RectTransform body = scroll.Content;
            var stack = new UiStack(body);

            UiBrushTitle heading = PaneLook.Title(ref stack, body, UiContext.Profile, _paneMarks);

            // WP-022E name plate: the chosen title · badge on the heading's right, in the
            // sakura accent - the one place the pick is shown next to "the name" (WP-098B: in the title's band, above its rule).
            string plate = CosmeticsViewModel.NamePlate(ProfileService.CosmeticsSource);
            if (!string.IsNullOrEmpty(plate))
            {
                TMP_Text plateText = UiFactory.Label(
                    "NamePlate", body, plate, UiType.BodyStrong, UiColor.AccentSakura, TextAnchor.MiddleRight);
                RectTransform plateRect = (RectTransform)plateText.transform;
                plateRect.anchorMin = new Vector2(0.5f, 1f);
                plateRect.anchorMax = new Vector2(1f, 1f);
                plateRect.pivot = new Vector2(1f, 1f);
                plateRect.sizeDelta = new Vector2(0f, Mathf.Max(UiFactory.LineHeight(UiType.BodyStrong),
                    heading.Height - UiBrushTokens.TitleRuleGap - UiBrushTokens.TitleStrokeHeight));
                plateRect.anchoredPosition = new Vector2(0f, 0f);
            }

            stack.Gap(UiSpace.L); // WP-022D: the pane gained a two-column band; gaps above it tightened by 32 px

            if (!string.IsNullOrEmpty(model.Notice))
            {
                // Error state, stated rather than hidden. The game is fully playable;
                // only the previous profile was lost, and it was kept on disk.
                RectTransform notice = BuildNotice(body, model.Notice, UiColor.StateWarning);
                stack.Add(notice, NoticeHeight(body, notice, model.Notice, 72f));
                stack.Gap(UiSpace.L);
            }

            // "PROFILE LEVEL", not "LEVEL": the eyebrow says at first glance that this
            // number is the profile's experience, not a character's power.
            TMP_Text levelLabel = UiFactory.Label(
                "LevelLabel", body, UiStrings.Get("profile.level_label", "PROFILE LEVEL"), UiType.Label, UiColor.TextSecondary);
            stack.Add((RectTransform)levelLabel.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.XS);

            // WP-068 slice 3 (AD MUST-2): one face - "LEVEL 1" is an H2 Display heading, as on
            // Results (a Numeric label drew "LEVEL" in the fallback sans beside an Oswald digit).
            TMP_Text level = UiFactory.Label(
                "Level", body, model.LevelText, UiType.H2, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            stack.Add((RectTransform)level.transform, UiFactory.LineHeight(UiType.H2));

            stack.Gap(UiSpace.M);

            UiBar xpBar = UiFactory.Bar("XpBar", body, UiColor.BarXp, 14f);
            xpBar.SetValue(model.Progress01, true);
            stack.Add((RectTransform)xpBar.transform, 14f);

            stack.Gap(UiSpace.S);

            TMP_Text xpCaption = UiFactory.Label(
                "XpCaption", body, model.ProgressCaption, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)xpCaption.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.L);
            PaneLook.Divider(ref stack, body, "D1", _paneMarks);
            stack.Gap(UiSpace.M);

            List<RectTransform> rows = _paneRowsEnter ? _paneRows : null;
            for (int i = 0; i < model.Headline.Count; i++)
            {
                AddRow(ref stack, model.Headline[i], rows);
            }

            stack.Gap(UiSpace.M);
            PaneLook.Divider(ref stack, body, "D2", _paneMarks);
            stack.Gap(UiSpace.M);

            // WP-022D: below the second divider the pane splits in two columns - unlocked
            // content (weapons / branches, WP-005) on the left, the WP-035 title / badge
            // picker on the right.
            ICosmeticsSource cosmeticsSource = ProfileService.CosmeticsSource;
            CosmeticsViewModel cosmetics = cosmeticsSource != null ? CosmeticsViewModel.From(cosmeticsSource) : null;
            bool twoColumns = cosmetics != null && !cosmetics.IsEmpty;

            RectTransform columns = UiFactory.NewRect("Columns", body);
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
                "UnlockLabel", left, UiStrings.Get("profile.unlocked_content", "UNLOCKED CONTENT"), UiType.Label, UiColor.TextSecondary);
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
                "Rule", left, ProfileViewModel.RuleNote, UiType.Caption, UiColor.TextSecondary);
            leftStack.Add((RectTransform)rule.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

            float needed = leftStack.Cursor;
            List<Selectable> options = null;
            if (twoColumns)
            {
                RectTransform right = UiFactory.NewRect("Right", columns);
                UiFactory.Fill(right);
                right.anchorMin = new Vector2(ProfileLeftColumn, 0f);
                var rightStack = new UiStack(right);
                options = BuildCosmeticsPicker(ref rightStack, cosmetics);
                needed = Mathf.Max(needed, rightStack.Cursor);
            }

            // Slice 9 · E3 T1: the band grows to what its columns hold - at TextScale 1.25 the picker needs ≈ 420 px and
            // the fixed 340 clipped its last rows.
            if (needed > ProfileColumnsHeight)
            {
                columns.sizeDelta = new Vector2(columns.sizeDelta.x, Mathf.Ceil(needed));
                stack.Cursor += Mathf.Ceil(needed) - ProfileColumnsHeight;
            }

            scroll.SetContentHeight(stack.Cursor);
            scroll.TrackSelectables();

            // Pad: the options top to bottom, then (only when the pane runs past its column) the reader stop.
            PaneReader reader = AttachReader(scroll, _profileTab);
            if (options != null && options.Count > 0)
            {
                _paneFocus = options[0];
                if (reader != null)
                {
                    Selectable last = options[options.Count - 1];
                    UiInput.LinkVertical(last, null, reader);
                    UiInput.LinkVertical(reader, last, null);
                }
            }
            else if (reader != null)
            {
                _paneFocus = reader;
                UiInput.LinkHorizontal(_profileTab, null, reader);
            }
        }

        /// <summary>Height of the profile pane's two-column band (unlocks | picker); 1080p card budget.</summary>
        private const float ProfileColumnsHeight = 340f;
        private const float ProfileLeftColumn = 0.6f; // AD v004 MUST-4: 60 / 40

        // ---------------------------------------------------------- statistics pane

        /// <summary>WP-098B: the statistics read in a scroller (the list outgrew the card at 1080: it was cut); on the pad the reader stop scrolls it.</summary>
        private void BuildStatisticsPane(StatisticsViewModel model)
        {
            UiScrollView scroll = UiBrushKit.Scroll(_paneHost, PaneLook.ScrollName);   // C35
            float height = BuildStatisticsBody(scroll.Content, model, _paneMarks, _paneRowsEnter ? _paneRows : null);
            scroll.SetContentHeight(height);
            scroll.TrackSelectables();
            PaneReader reader = AttachReader(scroll, _statisticsTab);
            if (reader != null)
            {
                _paneFocus = reader;
                UiInput.LinkHorizontal(_statisticsTab, null, reader);
            }
        }

        /// <summary>
        /// The statistics, top-down on <paramref name="parent"/> (the scroller's content): the kit title, the empty notice,
        /// runs · kills · progress between the pane's two brush dividers, the note. Returns the height laid out (the
        /// scroller's content height). Static so EditMode measures it (WP-098B overflow test); <paramref name="marks"/>
        /// collects the brush marks (title, dividers), <paramref name="rows"/> the rows that stagger in.
        /// </summary>
        public static float BuildStatisticsBody(RectTransform parent, StatisticsViewModel model, List<UiBrushMark> marks, List<RectTransform> rows)
        {
            var stack = new UiStack(parent);

            PaneLook.Title(ref stack, parent, UiContext.Statistics, marks);
            stack.Gap(UiSpace.L);

            if (!string.IsNullOrEmpty(model.EmptyNotice))
            {
                // Empty state. Zeros with no explanation read as a broken screen.
                RectTransform notice = BuildNotice(parent, model.EmptyNotice, UiColor.TextSecondary);
                stack.Add(notice, NoticeHeight(parent, notice, model.EmptyNotice, 56f));
                stack.Gap(UiSpace.L);
            }

            // Run facts first, kills second, XP last: the numbers a player made with
            // their hands come before the numbers the profile derived from them.
            for (int i = 0; i < model.Runs.Count; i++)
            {
                AddRow(ref stack, model.Runs[i], rows);
            }

            stack.Gap(UiSpace.S);
            PaneLook.Divider(ref stack, parent, "D1", marks);
            stack.Gap(UiSpace.S);

            for (int i = 0; i < model.Kills.Count; i++)
            {
                AddRow(ref stack, model.Kills[i], rows);
            }

            if (!string.IsNullOrEmpty(model.TierCaption))
            {
                // WP-098B: as tall as its words at the pane width and TextScale (a fixed line cut it in a narrow pane).
                AddCaptionUnder(ref stack, parent, "TierCaption", model.TierCaption, 1, UiSpace.None);
            }

            stack.Gap(UiSpace.S);
            PaneLook.Divider(ref stack, parent, "D2", marks);
            stack.Gap(UiSpace.S);

            for (int i = 0; i < model.Progress.Count; i++)
            {
                AddRow(ref stack, model.Progress[i], rows);
            }

            stack.Gap(UiSpace.S);   // + the caption's own S: the L the note always had
            AddCaptionUnder(ref stack, parent, "Note", StatisticsViewModel.Note, 2, UiSpace.None);
            return stack.Cursor;
        }

        // -------------------------------------------------------- achievements pane

        private void BuildAchievementsPane(AchievementsViewModel model)
        {
            var stack = new UiStack(_paneHost);

            UiBrushTitle heading = PaneLook.Title(ref stack, _paneHost, UiContext.Achievements, _paneMarks);
            stack.Gap(UiSpace.M);

            if (model.IsEmpty)
            {
                // A build with no definitions is a legitimate state, not an error. It is
                // written as a finished sentence, and the pane still works.
                TMP_Text title = UiFactory.Label(
                    "EmptyTitle", _paneHost, AchievementsViewModel.EmptyTitle, UiType.H3, UiColor.TextPrimary);
                stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H3));
                stack.Gap(UiSpace.S);
                stack.Add(BuildNotice(_paneHost, AchievementsViewModel.EmptyBody, UiColor.TextSecondary), 72f);
                return;
            }

            TMP_Text progress = UiFactory.Label(
                "Progress", _paneHost, model.Progress, UiType.MenuBody, UiColor.TextSecondary);
            stack.Add((RectTransform)progress.transform, UiFactory.LineHeight(UiType.MenuBody));

            stack.Gap(UiSpace.L);

            // WP-022D: pages of up to MaxAchievementRows in catalog order (27 entries, WP-023);
            // PREVIOUS / NEXT are ordinary buttons, so pad and keyboard page the same way (WP-098B: the C29 pager -
            // two bare brush glyphs, their words in the tooltip and the reader name, the page between them).
            // Rows per page follow the room the pane has at this UI scale × TextScale (≤ MaxAchievementRows): the rows
            // grow with the line boxes, and the pager and the note never leave the pane (Mac 1.2 × 1.25 holds four).
            float fixedBands = heading.Height + (int)UiSpace.M + UiFactory.LineHeight(UiType.MenuBody) + (int)UiSpace.L
                               + (int)UiSpace.XS + PagerHeight + (int)UiSpace.S + UiFactory.LineHeight(UiType.MenuSmall);
            float rowPitch = AchievementRowHeightFor(false) + (int)UiSpace.S;
            int perPage = _paneHost.rect.height > 0f
                ? Mathf.Clamp(Mathf.FloorToInt((_paneHost.rect.height - fixedBands) / rowPitch), 2, MaxAchievementRows)
                : MaxAchievementRows;
            int pages = Mathf.Max(1, (model.Lines.Count + perPage - 1) / perPage);
            _achievementPage = Mathf.Clamp(_achievementPage, 0, pages - 1);
            int first = _achievementPage * perPage;
            int last = Mathf.Min(model.Lines.Count, first + perPage);
            var rows = new List<Selectable>(last - first + 2);
            for (int i = first; i < last; i++)
            {
                UiButton row = BuildAchievementRow(model.Lines[i]);
                stack.Add((RectTransform)row.transform, AchievementRowHeightFor(model.Lines[i].Earned));
                stack.Gap(UiSpace.S);
                EnterRow(row.transform);
                rows.Add(row);
            }

            // WP-098B 2a (pad): the page's rows are one vertical chain (Left: the rail icon); without a pager the icon's
            // Right lands on the first row. The pager below links to them when it is built.
            UiInput.ChainVertical(false, rows.ToArray());
            for (int i = 0; i < rows.Count; i++)
            {
                UiInput.LinkHorizontal(rows[i], _achievementsTab, null);
            }

            if (rows.Count > 0)
            {
                _paneFocus = rows[0];
                UiInput.LinkHorizontal(_achievementsTab, null, rows[0]);
            }

            if (pages > 1)
            {
                stack.Gap(UiSpace.XS);
                RectTransform pager = UiFactory.NewRect("Pager", _paneHost);
                stack.Add(pager, PagerHeight);

                UiIconButton previousIcon = UiIconControls.Button(pager, "Menu.Previous", "ach.previous", "PREVIOUS", () => TurnAchievementPage(-1),
                    PaneLook.PagerIcon, null, null, UiIconLabel.Tooltip, UiButtonVariant.Ghost);
                previousIcon.PinObjectName("Pager.Previous");
                UiButton previous = previousIcon.Button;
                RectTransform previousRect = (RectTransform)previous.transform;
                previousRect.anchorMin = new Vector2(0f, 0.5f);
                previousRect.anchorMax = new Vector2(0f, 0.5f);
                previousRect.pivot = new Vector2(0f, 0.5f);
                previousRect.anchoredPosition = Vector2.zero;
                previousIcon.SetInteractable(_achievementPage > 0);

                UiIconButton nextIcon = UiIconControls.Button(pager, "Menu.Next", "ach.next", "NEXT", () => TurnAchievementPage(1),
                    PaneLook.PagerIcon, null, null, UiIconLabel.Tooltip, UiButtonVariant.Ghost);
                nextIcon.PinObjectName("Pager.Next");
                UiButton next = nextIcon.Button;
                RectTransform nextRect = (RectTransform)next.transform;
                nextRect.anchorMin = new Vector2(1f, 0.5f);
                nextRect.anchorMax = new Vector2(1f, 0.5f);
                nextRect.pivot = new Vector2(1f, 0.5f);
                nextRect.anchoredPosition = Vector2.zero;
                nextIcon.SetInteractable(_achievementPage < pages - 1);

                TMP_Text page = UiFactory.Label(
                    "Page", pager, UiStrings.Format("ach.page", "value", (_achievementPage + 1).ToString(), "total", pages.ToString(), "Page {value} of {total}"), UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleCenter);
                RectTransform pageRect = UiFactory.Fill((RectTransform)page.transform);
                float pageInset = PagerHeight + (int)UiSpace.S;
                pageRect.offsetMin = new Vector2(pageInset, 0f);
                pageRect.offsetMax = new Vector2(-pageInset, 0f);

                UiInput.ChainHorizontal(previous, next);
                UiInput.LinkHorizontal(_achievementsTab, null, _achievementPage > 0 ? previous : next);
                UiInput.LinkHorizontal(previous, _achievementsTab, next);
                UiInput.LinkHorizontal(next, _achievementPage > 0 ? previous : _achievementsTab, null);

                // AD v004 SHOULD-6: opening the pane from the nav lands on NEXT; a redraw caused by a profile change or a
                // page turn keeps its own focus rule.
                // (WP-098B: RedrawPane focuses it on a pad open - NEXT, or PREVIOUS on the last page where NEXT is off.)
                _paneFocus = _achievementPage < pages - 1 ? next : previous;

                // WP-098B 2a: Up from the pager reads the page's rows bottom-up; Down from the last row returns to it.
                if (rows.Count > 0)
                {
                    Selectable lastRow = rows[rows.Count - 1];
                    UiInput.LinkVertical(previous, lastRow, null);
                    UiInput.LinkVertical(next, lastRow, null);
                    UiInput.LinkVertical(lastRow, null, _paneFocus);
                }
                stack.Gap(UiSpace.S);
            }

            TMP_Text note = UiFactory.Label(
                "Note", _paneHost, AchievementsViewModel.AutoNote, UiType.MenuSmall, UiColor.TextSecondary);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.MenuSmall));
        }

        private void TurnAchievementPage(int delta)
        {
            _achievementPage += delta;
            _refocusName = delta < 0 ? "Pager/Pager.Previous" : "Pager/Pager.Next";
            _paneTurn = true;   // WP-098B: the new page's rows stagger in (the pane itself stays)
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
        private List<Selectable> BuildCosmeticsPicker(ref UiStack stack, CosmeticsViewModel model)
        {
            if (model.IsEmpty)
            {
                return null;
            }

            var options = new List<Selectable>(model.Titles.Count + model.Badges.Count + 2);
            AddCosmeticGroup(ref stack, CosmeticsViewModel.TitlesLabel, "Title", model.Titles, model.SelectedTitle, options);
            if (model.Badges.Count > 0)
            {
                stack.Gap(UiSpace.S);
                RectTransform split = UiFactory.NewRect("D3", stack.Parent);   // the picker's halves: a row rule, not a third brush divider
                stack.Add(split, PaneLook.DividerBand);
                RectTransform splitRule = PaneLook.Rule(split, "Rule").rectTransform;
                splitRule.anchorMin = new Vector2(0f, 0.5f);
                splitRule.anchorMax = new Vector2(1f, 0.5f);
                splitRule.pivot = new Vector2(0.5f, 0.5f);
                splitRule.offsetMin = new Vector2(0f, -0.5f);
                splitRule.offsetMax = new Vector2(0f, 0.5f);
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

            return options;
        }

        private void AddCosmeticGroup(ref UiStack stack, string heading, string kind, List<UiCosmeticLine> lines, string selectedId, List<Selectable> options)
        {
            TMP_Text label = UiFactory.Label("Label." + kind, stack.Parent, heading, UiType.MenuSmall, UiColor.TextSecondary);   // WP-098B: brush small text
            stack.Add((RectTransform)label.transform, UiFactory.LineHeight(UiType.MenuSmall));
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

            // WP-098B 2a (C18): an unframed kit list row - the name, the check glyph on the current choice (G1: the choice
            // is the check, not a bar or a tint), a locked option's lock glyph + its condition (LOCKED · ROUND 10, the words
            // the row always showed), NEW on a fresh unlock (a word, no pulsing chip). A locked row stays reachable on the pad.
            PaneLook.OptionStatus(unlocked, selected, isNew, lockReason, out string icon, out string trailing);
            UiButton button = UiBrushKit.ListRow(stack.Parent, name, optionTitle, icon, () =>
            {
                ICosmeticsSource source = ProfileService.CosmeticsSource;
                // WP-098B (pad): the name is set BEFORE the choice - the profile's Changed event redraws the pane inside
                // Select*, and that redraw puts focus back on this option (set after, the redraw found no name and the
                // pad lost focus). A repeated choice raises no Changed: nothing redraws, the name is dropped below.
                _refocusName = name;
                bool ok = source != null && (isTitle ? source.SelectTitle(id) : source.SelectBadge(id));
                _refocusName = null;
                if (ok)
                {
                    UiEvents.Raise(UiEvent.ChoiceSelected, kind);
                    UiSound.Play(UiSoundKind.Confirm);
                }
                else
                {
                    UiSound.Play(UiSoundKind.Denied);
                }
            });
            stack.Add((RectTransform)button.transform, PaneLook.ListRowHeight);
            stack.Cursor += PaneLook.ListRowGap;
            EnterRow(button.transform);
            if (!string.IsNullOrEmpty(trailing))
            {
                UiBrushKit.SetRowStatus(button, icon, trailing);
            }

            button.SetSelectedChoice(selected);   // the state the pad / tests read; the check glyph draws it
            button.SetInteractable(unlocked);
            options.Add(button);
        }

        /// <summary>
        /// WP-098B 2a (AP-028 C32 on the kit's C18): one achievement as a kit list row - its name is the row's label, the
        /// status glyph and word on the right (earned: the open glyph + EARNED / NEW; else the progress glyph + IN PROGRESS;
        /// shape + word, no filled chip, no pulse); under the name its description, and while in progress the bar with
        /// "7 / 10". The rarity medallion leads; a 1 px row rule (C6) under it. A focus stop, so the pad reads the page.
        /// </summary>
        private UiButton BuildAchievementRow(UiAchievementLine line)
        {
            PaneLook.AchievementStatus(line.Earned, line.IsNew, out string icon, out string trailing);
            UiButton button = UiBrushKit.ListRow(_paneHost, "Achievement." + line.Id, line.Title, icon, null, false);
            UiBrushKit.SetRowStatus(button, icon, trailing);
            var row = (RectTransform)button.transform;

            Image rule = PaneLook.Rule(row, "Rule");
            RectTransform ruleRect = rule.rectTransform;
            ruleRect.anchorMin = new Vector2(0f, 0f);
            ruleRect.anchorMax = new Vector2(1f, 0f);
            ruleRect.pivot = new Vector2(0.5f, 0f);
            ruleRect.sizeDelta = new Vector2(0f, 1f);
            ruleRect.anchoredPosition = Vector2.zero;

            float pad = (int)UiSpace.L;
            RectTransform content = UiFactory.CardContent(row, UiSpace.L);

            // WP-022: AP-012 medallion at the leading edge - the WP-023 rarity (Common ring / Rare wreath / Epic sakura),
            // jade when earned, the secondary tone while in progress (the glyph and the word carry the state too).
            float medalSize = AchievementRowHeight - pad * 2;
            Image medal = UiFactory.NewImage(
                "Medal", content, line.Earned ? UiColor.StateSuccess : UiColor.TextSecondary,
                UiArt.Medal(MedalForRarity(line.Rarity)));
            medal.preserveAspect = true;
            medal.raycastTarget = false;
            var medalRect = (RectTransform)medal.transform;
            medalRect.anchorMin = new Vector2(0f, 0.5f);
            medalRect.anchorMax = new Vector2(0f, 0.5f);
            medalRect.pivot = new Vector2(0f, 0.5f);
            medalRect.sizeDelta = new Vector2(medalSize, medalSize);
            medalRect.anchoredPosition = Vector2.zero;
            float textLeft = medalSize + (int)UiSpace.M;

            float titleBand = UiFactory.LineHeight(UiType.MenuBody);
            TMP_Text description = UiFactory.Label(
                "Description", content,
                UiFactory.Clip(line.Description, 90),
                UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.UpperLeft);

            // The status column on the right: the kit's glyph + its word, as wide as this word measures at the TextScale
            // (TR KAZANILDI is wider than EN EARNED), never under the old chip column.
            float status = Mathf.Max(ChipColumnWidth, Mathf.Ceil(description.GetPreferredValues(trailing).x) + StatusGlyph + (int)UiSpace.S * 2);

            RectTransform descriptionRect = UiFactory.Fill((RectTransform)description.transform);
            descriptionRect.offsetMin = new Vector2(textLeft, line.Earned ? 0f : ProgressBand);
            descriptionRect.offsetMax = new Vector2(-status, -titleBand);

            // The name: the kit row's own label, moved to the top band after the medallion (the status keeps the right
            // column). The bands come from the faces' line boxes at the TextScale (never a fixed band - slice 3).
            TMP_Text title = button.LabelText;
            if (title != null)
            {
                RectTransform titleRect = title.rectTransform;
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.pivot = new Vector2(0f, 1f);
                titleRect.offsetMin = new Vector2(pad + textLeft, -(pad + titleBand));
                titleRect.offsetMax = new Vector2(-(pad + status), -pad);
            }

            if (!line.Earned)
            {
                // "2 / 5": the current value in TextPrimary, the maximum in TextSecondary (rich text, row build only), in the
                // progress band at the bar's end (under the description, clear of the status column) - its width measured,
                // so the bar runs up to it.
                TMP_Text counter = UiFactory.Label(
                    "Counter", content, string.Empty, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.LowerRight);
                counter.richText = true;
                counter.text = "<color=#" + ColorUtility.ToHtmlStringRGB(UiTokens.Get(UiColor.TextPrimary)) + ">" + line.Current + "</color> / " + line.Threshold;
                float counterWidth = Mathf.Ceil(counter.GetPreferredValues(counter.text).x);
                RectTransform counterRect = counter.rectTransform;
                counterRect.anchorMin = new Vector2(1f, 0f);
                counterRect.anchorMax = new Vector2(1f, 0f);
                counterRect.pivot = new Vector2(1f, 0f);
                counterRect.sizeDelta = new Vector2(counterWidth, ProgressBand);
                counterRect.anchoredPosition = new Vector2(-status, 0f);

                // WP-022D (WP-035): lifetime progress under the description - bar plus "7 / 10" so the number and the
                // length agree. Instant: no per-frame work.
                UiBar bar = UiFactory.Bar("Progress", content, UiColor.BarXp, ProgressBarHeight); // AD v004 MUST-3: a readable bar on its track
                bar.SetValue(line.Progress01, true);
                RectTransform barRect = (RectTransform)bar.transform;
                barRect.anchorMin = new Vector2(0f, 0f);
                barRect.anchorMax = new Vector2(1f, 0f);
                barRect.pivot = new Vector2(0f, 0f);
                barRect.offsetMin = new Vector2(textLeft, (ProgressBand - ProgressBarHeight) * 0.5f);
                barRect.offsetMax = new Vector2(-(status + counterWidth + (int)UiSpace.S), (ProgressBand + ProgressBarHeight) * 0.5f);
            }

            return button;
        }

        /// <summary>Lower band of an in-progress achievement row: the bar and its "7 / 10" (one MenuSmall line at the TextScale).</summary>
        private static float ProgressBand
        {
            get { return UiFactory.LineHeight(UiType.MenuSmall); }
        }

        private const float ProgressBarHeight = 10f;

        /// <summary>C18's larger status glyph (24 / 32): the achievement row's.</summary>
        private const float StatusGlyph = 32f;

        /// <summary>
        /// A row holds its padding, the MenuBody name and one MenuSmall line (+ the progress band while in progress) at the
        /// TextScale; never shorter than the 96 px medallion row.
        /// </summary>
        private static float AchievementRowHeightFor(bool earned)
        {
            float text = UiFactory.LineHeight(UiType.MenuBody) + UiFactory.LineHeight(UiType.MenuSmall);
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

        /// <summary>A key / value row on the stack's parent; <paramref name="rows"/> (when entering) collects it for the stagger.</summary>
        private static void AddRow(ref UiStack stack, UiStatLine line, List<RectTransform> rows)
        {
            TMP_Text valueText = UiFactory.StatRow(
                line.Label, line.Value, stack.Parent, line.Color,
                line.Emphasis ? UiType.H3 : UiType.BodyStrong);
            var row = (RectTransform)valueText.transform.parent;
            stack.Add(row, RowHeight);
            if (rows != null)
            {
                rows.Add(row);
            }
        }

        /// <summary>
        /// WP-098B 2a · an unlocked-content line in C18's look, read-only (the profile's lists are reading, not choices): the
        /// name (MenuBody; TextSecondary while locked - TextMuted is not text on the wash), the open / lock glyph and the word
        /// (UNLOCKED / LEVEL n, MenuSmall) on the right. Shape + word carry the state; no filled chip.
        /// </summary>
        private void AddUnlockRow(ref UiStack stack, UiUnlockLine line)
        {
            RectTransform row = UiFactory.NewRect("Unlock", stack.Parent);
            float glyph = UiIconTokens.GlyphFloor;

            TMP_Text label = UiFactory.Label(
                "Label", row, line.Title, UiType.MenuBody,
                line.Unlocked ? UiColor.TextPrimary : UiColor.TextSecondary, TextAnchor.MiddleLeft);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.offsetMax = new Vector2(-(ValueColumnWidth + glyph + (int)UiSpace.S * 2), 0f);

            Image icon = UiArt.IconImage("Glyph", row, line.Unlocked ? PaneLook.EarnedGlyph : PaneLook.LockGlyph,
                line.Unlocked ? UiColor.TextPrimary : UiColor.TextSecondary, glyph);
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = new Vector2(1f, 0.5f);
            iconRect.anchorMax = new Vector2(1f, 0.5f);
            iconRect.pivot = new Vector2(1f, 0.5f);
            iconRect.anchoredPosition = new Vector2(-(ValueColumnWidth + (int)UiSpace.S), 0f);

            TMP_Text state = UiFactory.Label(
                "State", row,
                line.Unlocked ? UiStrings.Get("profile.unlocked", "UNLOCKED") : UiStrings.Format("profile.level", "value", line.RequiredLevel.ToString(), "LEVEL {value}"),
                UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleRight);
            RectTransform stateRect = state.rectTransform;
            stateRect.anchorMin = new Vector2(1f, 0f);
            stateRect.anchorMax = new Vector2(1f, 1f);
            stateRect.pivot = new Vector2(1f, 0.5f);
            stateRect.sizeDelta = new Vector2(ValueColumnWidth, 0f);
            stateRect.anchoredPosition = Vector2.zero;

            stack.Add(row, UiBrushTokens.RowHeight(UiType.MenuBody, UiBrushTokens.RowHitCompact));
            EnterRow(row);
        }

        /// <summary>WP-098B: a notice's band - <paramref name="minimum"/>, or its wrapped words at the parent's width and TextScale when taller.</summary>
        private static float NoticeHeight(RectTransform parent, RectTransform notice, string message, float minimum)
        {
            float width = parent.rect.width - UiTokens.HeadingTickWidth - (int)UiSpace.M;
            Transform text = notice != null ? notice.Find("Text") : null;
            TMP_Text measure = text != null ? text.GetComponent<TMP_Text>() : null;
            if (width <= 1f || measure == null || string.IsNullOrEmpty(message))
            {
                return minimum;
            }

            return Mathf.Max(minimum, Mathf.Ceil(measure.GetPreferredValues(message, width, 0f).y));
        }

        /// <summary>An inline notice: accent rule on the left, wrapped text beside it.</summary>
        private static RectTransform BuildNotice(RectTransform parent, string message, UiColor accent)
        {
            RectTransform row = UiFactory.NewRect("Notice", parent);

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
