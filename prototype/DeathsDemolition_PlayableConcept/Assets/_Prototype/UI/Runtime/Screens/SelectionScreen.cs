using System;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Profile;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Prepare Run. Shown before every run.
    ///
    /// WP-106 (R4, user note 7: "the old character's skin choices are still there - remove them; the page fuller, with 3D
    /// interactive weapon previews"): the skin column is gone (hero v2 is the one hero; PrototypeUiFlow.SelectedSkin keeps its
    /// default and the game applies it as before). Three columns on the page grid (PrepareRunLayout: 456 / 24 / 664 / 24 / 560
    /// at 1728):
    ///   * RUN SETUP: DIFFICULTY - one kit list row per WP-102 mode (its name, a 1-3 mark indicator, the check glyph on the
    ///     choice) and one line under the rows: the chosen mode's short line, or the focused mode's detail while a row has
    ///     focus. Then the MAP (the AP-028 L01 courtyard art, cover-fit, and its name) and one short TIP from the loading set.
    ///   * The stage, an Elev1 card: the weapon under focus (or the chosen one when focus is elsewhere) - its name and class,
    ///     its role, the 3D preview (PrepareRunStage, which owns its input: drag turns and tilts, the wheel zooms, a double
    ///     click resets; the pad's right stick and triggers from anywhere on the page; the AP-036 side view stands in until the
    ///     first render), five stat cards (damage, fire rate, magazine, range - spread on a pellet weapon -, fire mode; each a
    ///     short label, its number and a roster bar) and the state line (what keeps a weapon out of a starting loadout).
    ///   * CLASSIC WEAPON: one row per roster weapon (side view, name, check / lock glyph and its chip), then the two Specials
    ///     as information tiles. They are found on the field and never chosen here.
    /// A locked row can be focused so the stage can say why. Pressing it nudges the row, plays Denied and changes nothing.
    ///
    /// WP-098B (AP-028 §3.4; brush kit KILAVUZ §3 "Seçim"): the window is the kit's - the world plate under the Window scrim
    /// (<see cref="UiBrushPane"/>), the kit's C5 title without a glyph, every choice a C18 list row (unframed; the choice is the
    /// check glyph, a lock the lock glyph and its chip), Back (C10, Menu.Back + the pad's Cancel) and START RUN (C11, Menu.Play).
    /// Trace budget: title brush 1; vermilion 0.
    ///
    /// R2 U1 (fewer letters, binding): what the page adds is drawn, not written - the mode marks, the stat icons and bars, the
    /// map art, the preview glyph. A mode's detail shows only on its focus, the preview's words only on the pointer's hover,
    /// the tip is one short line (PrepareRunLayout.TipPool).
    ///
    /// SESSION MEMORY (acceptance criteria 23 and 24): the weapon is <c>PrototypeUiFlow.SelectedWeapon</c>, the difficulty
    /// WP-102's <c>DifficultyService.Selected</c> (through <see cref="PrepareDifficulty"/>); both statics reset when the
    /// application restarts. A second run in the same session opens on the previous choices. Neither is written to the profile.
    /// </summary>
    public sealed class SelectionScreen : UiScreen
    {
        private const float FooterHeight = 88f;             // the kit's 64 high C10 / C11 + XL
        private const float BadgeWidth = 160f;              // a row's trailing reserve: the status glyph, its word and the gap

        private const float WeaponRowHeight = 48f;
        private const float WeaponRowGap = 4f;
        private const float RowGlyph = 40f;
        private const float SpecialTileHeight = 48f;
        private const float RowSideMax = 128f;              // R3 m12: the rows' side-view slot (the longest weapon's width), at most

        private const float StatBarHeight = 8f;

        /// <summary>AD S3 (WP-106): a stat that has a value never draws an empty-looking bar - its fill is at least this share.</summary>
        public const float StatBarMinFill = 0.08f;
        private const float StatIcon = 20f;
        private const float PreviewMin = 120f;              // under this the gaps close first
        private const float PreviewHide = 56f;              // under this the preview gives its room up (never a text)
        private const float PreviewMaxAspect = 0.8f;        // the preview's height at most 0.8 × its width (the 4:3 page)
        private const float HintGlyph = 32f;

        private const float DifficultyRowGap = 4f;
        private const float MarkSize = 14f;
        private const float MarkGap = 4f;
        private const float ThumbMin = 72f;
        private const float ThumbFocusY = 0.62f;            // L01: the palace and its roofs stand in the upper half of the art
        private const float TipGlyph = 24f;

        private static readonly Vector3 GlyphEdge = new Vector3(0.25f, 0f, 0f);   // the HudInk edge the HUD glyphs wear

        /// <summary>The window's kit pane (its root under the page grid; its C1 on the screen root).</summary>
        public const string PaneName = "SelectionPane";

        /// <summary>C18 status glyphs (G1 atlas): the choice is the check, a weapon or mode not open the lock.</summary>
        public const string CheckGlyph = "Selection.Selected";

        public const string LockGlyph = "State.Locked";

        /// <summary>
        /// The difficulty indicator's marks (G1). The R4 copy asks for skulls 1 / 2 / 3; the atlas has none yet, so the pips
        /// stand in - an AP-042 skull cell swaps in here, nothing else changes.
        /// </summary>
        public const string MarkFull = "Pip.Full";

        public const string MarkEmpty = "Pip.Empty";

        /// <summary>The map's art (AP-028 L01, the palace courtyard - the loading rotation's first painting).</summary>
        public const string MapArt = UiCurtain.LoadingKeyArt;

        // Page
        private UiBrushPane _pane;
        private RectTransform _page;                        // the pane's clipped content: the columns and the footer
        private UiButton _backButton;
        private UiButton _startButton;
        private GameObject _lastFocus;

        // Weapons column
        private UiButton[] _weaponButtons;
        private string[] _weaponLockWords;                  // a locked row's trailing word (null: unlocked), built once
        private Image[] _weaponGlyphs;
        private int[] _weaponSides;                         // R3 m12: WeaponSideView.Table index per row, -1 = the G1 glyph stand-in
        private ClassicWeaponId[] _weaponIds;
        private WeaponDefinition[] _weaponDefs;
        private bool[] _weaponEnabled;
        private RectTransform _weaponsColumn;
        private RectTransform _specialsHead;
        private RectTransform _specialsRow;
        private readonly TMP_Text[] _specialNames = new TMP_Text[2];
        private readonly TMP_Text[] _specialModes = new TMP_Text[2];
        private readonly float[] _specialTextLeft = new float[2];
        private readonly Image[] _specialIcons = new Image[2];
        private readonly int[] _specialSides = { -1, -1 };
        private float _specialModeSize;
        private bool _specialsBeside = true;
        private bool _specialsStacked;                      // laid side by side, a mode still did not fit: stacked for this size
        private Vector2 _specialsStackedSize;
        private float _weaponsTop;
        private float _rowHeight;
        private float _tileHeight;
        private float _specialsTitleBand;
        private bool _weaponsLayoutPending;
        private Vector2 _weaponsSize;

        // The stage
        private RectTransform _stageCard;
        private RectTransform _stageContent;
        private CanvasGroup _stageText;
        private RectTransform _stageTop;
        private TMP_Text _stageName;
        private TMP_Text _stageClass;
        private TMP_Text _stageRole;
        private RectTransform _previewWell;
        private Image _stageGlyph;                          // the side-view stand-in under the 3D render
        private PrepareRunStage _stage;
        private PrepareRunPreviewHover _previewHover;
        private Image _previewHint;
        private TMP_Text _previewHintText;
        private bool _awaitFirstRender;
        private RectTransform _statsRow;
        private readonly RectTransform[] _statCells = new RectTransform[PrepareRunLayout.StatCount];
        private readonly Image[] _statIcons = new Image[PrepareRunLayout.StatCount];
        private readonly TMP_Text[] _statLabels = new TMP_Text[PrepareRunLayout.StatCount];
        private readonly TMP_Text[] _statValues = new TMP_Text[PrepareRunLayout.StatCount];
        private readonly UiBar[] _statBars = new UiBar[PrepareRunLayout.StatCount];
        private readonly WeaponStat[] _stats = new WeaponStat[PrepareRunLayout.StatCount];
        private readonly float[] _statNeed = new float[PrepareRunLayout.StatCount];
        private int _statColumns = PrepareRunLayout.StatCount;
        private RectTransform _stateRow;
        private TMP_Text _stateLine;
        private int _stageIndex = -1;
        private bool _stageLayoutPending;
        private Vector2 _stageSize;

        // Run setup column
        private RectTransform _setupColumn;
        private TMP_Text _difficultyHeading;
        private UiButton[] _difficultyRows;
        private Image[][] _difficultyMarks;
        private string[] _modeDesc;
        private string[] _modeDetail;
        private TMP_Text _modeLine;
        private int _modeLineIndex = -1;
        private bool _modeLineDetail;
        private RectTransform _mapBlock;
        private TMP_Text _mapTitle;
        private RectTransform _mapFrame;
        private RawImage _mapThumb;
        private TMP_Text _mapName;
        private RectTransform _tipBlock;
        private TMP_Text _tipText;
        private bool _setupLayoutPending;
        private Vector2 _setupSize;

        /// <summary>The page's openings this session: the tip rotates through its pool, one step per opening.</summary>
        private static int s_visits;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_visits = 0;
        }

        // ================================================================= public surface (tests, captures)

        /// <summary>The roster row the stage shows.</summary>
        public int StageIndex
        {
            get { return _stageIndex; }
        }

        /// <summary>The stage's texts, top to bottom: name, class line, role, state line.</summary>
        public TMP_Text[] StageTexts
        {
            get { return new[] { _stageName, _stageClass, _stageRole, _stateLine }; }
        }

        /// <summary>The state line: what keeps the weapon on the stage out of a starting loadout (empty when nothing does).</summary>
        public TMP_Text StateLine
        {
            get { return _stateLine; }
        }

        /// <summary>The 3D preview (P1); null when it could not be built - the side view then stands in.</summary>
        public PrepareRunStage Stage
        {
            get { return _stage; }
        }

        /// <summary>The preview's well on the stage card (the stage's host).</summary>
        public RectTransform PreviewWell
        {
            get { return _previewWell; }
        }

        /// <summary>The hint's hover relay on the preview's view (null without a stage).</summary>
        public PrepareRunPreviewHover PreviewHover
        {
            get { return _previewHover; }
        }

        /// <summary>The preview's hint words (shown only while the pointer is over the view).</summary>
        public TMP_Text PreviewHintText
        {
            get { return _previewHintText; }
        }

        /// <summary>The weapon rows, in roster order.</summary>
        public UiButton[] WeaponRows
        {
            get { return _weaponButtons; }
        }

        /// <summary>The difficulty rows, in the source's order.</summary>
        public UiButton[] DifficultyRows
        {
            get { return _difficultyRows; }
        }

        /// <summary>The line under the difficulty rows (the chosen mode's line, or the focused mode's detail).</summary>
        public TMP_Text ModeLine
        {
            get { return _modeLine; }
        }

        public TMP_Text MapName
        {
            get { return _mapName; }
        }

        /// <summary>The tip's words (inactive when the column has no room for it or no tip qualifies).</summary>
        public TMP_Text TipText
        {
            get { return _tipText; }
        }

        /// <summary>Stat cards per row as last laid out (5, else 3 over 2, …).</summary>
        public int StatColumns
        {
            get { return _statColumns; }
        }

        /// <summary>The Specials tiles carry name and mode on one line (false: stacked - the Mac canvas at text 1.25).</summary>
        public bool SpecialsBeside
        {
            get { return _specialsBeside; }
        }

        /// <summary>The kit's carrier: C1 Window scrim, the clipped content (tests read its level and its text ground).</summary>
        public UiBrushPane Pane
        {
            get { return _pane; }
        }

        protected override UiContext BaseContext
        {
            get { return UiContext.Selection; }
        }

        /// <summary>WP-098B: the v2 backdrop (veils, petals, foot rule) gives way to the world plate (as Results).</summary>
        protected override bool WantsBackdrop
        {
            get { return false; }
        }

        protected override void Build()
        {
            // Defensive: a profile edited between the menu and here must not be able to
            // leave a locked weapon selected.
            if (!ProfileService.IsWeaponUnlocked(PrototypeUiFlow.SelectedWeapon))
            {
                PrototypeUiFlow.SelectedWeapon = ProfileService.FirstUnlockedWeapon();
            }

            ResultsScreen.BuildWorldPlate(ScreenRect);
            _pane = UiBrushKit.Pane(ScreenRect, Content, PaneName, UiBrushScrim.Window, UiBrushWash.None);
            _page = _pane.Content;

            UiBrushTitle title = UiBrushKit.Title(_page, "selection.title", "PREPARE YOUR RUN");
            float top = title.Height + (int)UiSpace.M;
            RectTransform setup = PageColumn("Setup", 0f, PrepareRunLayout.SetupShare, top, FooterHeight);
            RectTransform stage = PageColumn("Stage", PrepareRunLayout.StageFrom, PrepareRunLayout.StageTo, top, FooterHeight);
            RectTransform weapons = PageColumn("Weapons", PrepareRunLayout.WeaponsFrom, 1f, top, FooterHeight);
            BuildWeaponColumn(weapons);
            BuildStage(stage);
            BuildSetupColumn(setup);
            BuildFooter();

            RefreshWeapons();
            RefreshDifficulty();
            ShowOnStage(SelectedIndex());
            LinkNavigation();
            UiInputScheme.Changed += OnSchemeChanged;

            // Focus opens on the chosen weapon - on a second run of the session the remembered one.
            int chosen = SelectedIndex();
            SetInitialFocus(chosen >= 0 ? _weaponButtons[chosen] : _startButton);

            // E9 choreography: the three columns land left to right, 60 ms apart, once the curtain has revealed the
            // screen (Decor: Reduced Motion shows them at rest).
            gameObject.AddComponent<UiStaggerIn>().Configure(new[]
            {
                UiFactory.Group(setup.gameObject),
                UiFactory.Group(stage.gameObject),
                UiFactory.Group(weapons.gameObject)
            });

            _pane.Open(null);
        }

        protected override void OnDestroy()
        {
            UiInputScheme.Changed -= OnSchemeChanged;
            base.OnDestroy();
        }

        /// <summary>A column of the pane's page, on fractional anchors (UiScreen.Column's rule, inside the kit's content).</summary>
        private RectTransform PageColumn(string name, float from, float to, float top, float bottom)
        {
            RectTransform column = UiFactory.NewRect(name, _page);
            column.anchorMin = new Vector2(from, 0f);
            column.anchorMax = new Vector2(to, 1f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.offsetMin = new Vector2(0f, bottom);
            column.offsetMax = new Vector2(0f, -top);
            return column;
        }

        /// <summary>A column's heading: its words in TextPrimary - no vermilion heading tick in a brush window (KILAVUZ §1).</summary>
        private static TMP_Text ColumnHeading(string words, RectTransform column)
        {
            TMP_Text heading = UiFactory.Label("Heading", column, words, UiType.H2, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            heading.enableWordWrapping = false;
            return heading;
        }

        private void Update()
        {
            if (UiInput.CancelPressed())
            {
                GoBack();
                return;
            }

            UiInput.ReclaimFocus(FocusFallback());

            // Layout runs when a column's size changes or its first pass found no canvas yet - never per frame otherwise.
            if (_stageContent != null && (_stageLayoutPending || _stageContent.rect.size != _stageSize))
            {
                LayoutStage();
            }

            if (_weaponsColumn != null && (_weaponsLayoutPending || _weaponsColumn.rect.size != _weaponsSize))
            {
                LayoutWeapons();
            }

            if (_setupColumn != null && (_setupLayoutPending || _setupColumn.rect.size != _setupSize))
            {
                LayoutSetup();
            }

            // The stage and the mode line follow focus (a pointer hover moves focus too). A change of the selected object
            // is the only trigger.
            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focus != _lastFocus)
            {
                _lastFocus = focus;
                OnFocusChanged(focus);
            }

            // The stage turns itself (its pointer events, the pad's view controls in its own tick); the screen only hands
            // over from the side view once the first render stands.
            if (_awaitFirstRender && _stage != null && _stage.Renders > 0)
            {
                _awaitFirstRender = false;
                _stageGlyph.gameObject.SetActive(false);
            }
        }

        private Selectable FocusFallback()
        {
            int chosen = SelectedIndex();
            return chosen >= 0 && _weaponButtons != null ? _weaponButtons[chosen] : _startButton;
        }

        private void OnFocusChanged(GameObject focus)
        {
            int weapon = IndexOf(focus);
            if (weapon >= 0)
            {
                ShowOnStage(weapon);
            }
            else if (focus != null)
            {
                ShowOnStage(SelectedIndex());   // focus on the setup, Back or START RUN: the run's weapon
            }

            // focus == null (a pointer on the preview, a click on empty page): the stage keeps what it shows.
            int mode = DifficultyIndexOf(focus);
            ShowModeLine(mode >= 0 ? mode : PrepareDifficulty.Source.Selected, mode >= 0);
        }

        /// <summary>
        /// Gamepad / keyboard map: the difficulty rows ↕ and the weapons ↕ are two vertical chains (Back closes the first,
        /// START RUN the second); Left / Right hops between them - Right from a mode lands on the chosen weapon, Left from a
        /// weapon on the mode row beside it; Back ↔ START RUN. Locked rows are in the chains (the stage explains them); pressing
        /// one is refused. The stage is not a stop: the pad's right stick and triggers turn and zoom it from anywhere on the page.
        /// </summary>
        private void LinkNavigation()
        {
            var setup = new List<Selectable>(_difficultyRows);
            var weapons = new List<Selectable>(_weaponButtons);
            setup.Add(_backButton);
            weapons.Add(_startButton);
            UiInput.ChainVertical(setup.ToArray());
            UiInput.ChainVertical(weapons.ToArray());

            int modes = _difficultyRows.Length;
            int weaponRows = _weaponButtons.Length;
            int chosen = Mathf.Max(0, SelectedIndex());
            for (int i = 0; i < modes; i++)
            {
                Selectable right = weaponRows > 0 ? (Selectable)_weaponButtons[Mathf.Min(chosen, weaponRows - 1)] : _startButton;
                UiInput.LinkHorizontal(_difficultyRows[i], right, right);
            }

            for (int i = 0; i < weaponRows; i++)
            {
                int row = PrepareRunLayout.SetupRowFor(i, weaponRows, modes);
                Selectable left = row >= 0 ? (Selectable)_difficultyRows[row] : _backButton;
                UiInput.LinkHorizontal(_weaponButtons[i], left, left);
            }

            UiInput.LinkHorizontal(_backButton, _startButton, _startButton);
            UiInput.LinkHorizontal(_startButton, _backButton, _backButton);
        }

        // =================================================================== run setup

        private void BuildSetupColumn(RectTransform column)
        {
            _setupColumn = column;
            IPrepareDifficulty source = PrepareDifficulty.Source;
            _difficultyHeading = ColumnHeading(UiStrings.Get("difficulty.title", "DIFFICULTY"), column);

            int count = source.Count;
            _difficultyRows = new UiButton[count];
            _difficultyMarks = new Image[count][];
            _modeDesc = new string[count];
            _modeDetail = new string[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                string id = source.IdAt(i) ?? i.ToString(CultureInfo.InvariantCulture);
                // The pack writes the mode names in capitals (DEMOLITION is a proper name in TR too): never upper-cased here,
                // a Turkish upper-casing would dot its I.
                string name = UiStrings.Get("difficulty." + id + ".name", id.ToUpperInvariant());
                UiButton row = UiBrushKit.ListRow(column, "Difficulty." + id, name, null, () => SelectDifficulty(index));
                _difficultyRows[i] = row;
                _modeDesc[i] = UiStrings.Get("difficulty." + id + ".desc", string.Empty);
                _modeDetail[i] = UiStrings.Get("difficulty." + id + ".detail", string.Empty);

                // The 1-3 indicator: three marks on the row's right, left of the check glyph's slot (fixed - the marks never
                // move when the choice does).
                var marks = new Image[3];
                for (int m = 0; m < marks.Length; m++)
                {
                    Image mark = UiFactory.NewImage("Mark" + m, row.transform, UiColor.TextPrimary, UiArt.Icon(MarkEmpty));
                    mark.preserveAspect = true;
                    var rect = (RectTransform)mark.transform;
                    rect.anchorMin = new Vector2(1f, 0.5f);
                    rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    rect.sizeDelta = new Vector2(MarkSize, MarkSize);
                    rect.anchoredPosition = new Vector2(-(MarksRight + (2 - m) * (MarkSize + MarkGap)), 0f);
                    marks[m] = mark;
                }

                _difficultyMarks[i] = marks;
            }

            _modeLine = UiFactory.Label("ModeLine", column, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            _modeLine.enableWordWrapping = true;

            // The map: its caption, the L01 art cover-fit in a framed well, its name.
            _mapBlock = UiFactory.NewRect("Map", column);
            // R4 WP-106 (R2 U1): no MAP word over the map - the picture and its name say it; the label stays, empty, for the layout.
            _mapTitle = UiFactory.Label("Title", _mapBlock, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            _mapTitle.enableWordWrapping = false;
            _mapFrame = UiFactory.FramedCard("Frame", _mapBlock, UiColor.BorderHairline, UiColor.SurfaceInset, UiFrame.Card);
            var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var thumbRect = (RectTransform)thumb.transform;
            thumbRect.SetParent(_mapFrame, false);
            UiFactory.Fill(thumbRect, UiTokens.StrokeHairline);
            thumbRect.SetSiblingIndex(1);   // over the well's fill, under the ink frame
            _mapThumb = thumb.GetComponent<RawImage>();
            _mapThumb.raycastTarget = false;
            _mapThumb.texture = Resources.Load<Texture2D>(MapArt);
            _mapName = UiFactory.Label("Name", _mapBlock, UiStrings.Get("map.palace.name", "Palace Courtyard"), UiType.H3, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            _mapName.enableWordWrapping = false;

            // The tip: the Tip.Hint glyph and one short line (no TIP word: the glyph says it).
            _tipBlock = UiFactory.NewRect("Tip", column);
            Image glyph = UiArt.IconImage("Glyph", _tipBlock, "Tip.Hint", UiColor.TextSecondary, TipGlyph);
            var glyphRect = (RectTransform)glyph.transform;
            glyphRect.anchorMin = new Vector2(0f, 1f);
            glyphRect.anchorMax = new Vector2(0f, 1f);
            glyphRect.pivot = new Vector2(0f, 1f);
            glyphRect.anchoredPosition = Vector2.zero;
            _tipText = UiFactory.Label("Text", _tipBlock, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            _tipText.enableWordWrapping = true;
            RectTransform tipRect = UiFactory.Fill(_tipText.rectTransform);
            tipRect.offsetMin = new Vector2(TipGlyph + (int)UiSpace.S, 0f);

            bool hintsOn = UiSettings.Hints && (ProfileService.Current == null || ProfileService.Current.RunsPlayed < UiLoadingTips.HintRuns);
            int tip = PrepareRunLayout.TipFor(PrepareRunLayout.TipPool(hintsOn), s_visits);
            s_visits++;
            _tipText.text = tip >= 0 ? UiStrings.Get(UiLoadingTips.All[tip].Key, UiLoadingTips.All[tip].Text) : string.Empty;
            _tipBlock.gameObject.SetActive(tip >= 0);

            LayoutSetup();
        }

        /// <summary>The marks' right edge from the row's: the check glyph's 24 px slot and its gap stay reserved.</summary>
        private static float MarksRight
        {
            get { return (int)UiIconSize.S + (int)UiSpace.M; }
        }

        private static float MarksWidth
        {
            get { return 3f * MarkSize + 2f * MarkGap; }
        }

        /// <summary>
        /// Places the setup column from what it holds at its real size (PrepareRunLayout.Setup): the heading, the rows, the mode
        /// line (its room is the longest line or detail of any mode at this width, so nothing moves when focus walks), the map
        /// and the tip. Short of room the gaps close, then the map art shrinks, then the tip goes, then the art.
        /// </summary>
        private void LayoutSetup()
        {
            if (_setupColumn == null || _difficultyRows == null)
            {
                return;
            }

            Rect area = _setupColumn.rect;
            _setupSize = area.size;
            if (area.width < 1f || area.height < 1f)
            {
                _setupLayoutPending = true;
                return;
            }

            _setupLayoutPending = false;
            float w = area.width;
            float headH = LineOf(_difficultyHeading);
            float rowH = _difficultyRows.Length > 0 ? ((RectTransform)_difficultyRows[0].transform).sizeDelta.y : 0f;
            float rowsH = _difficultyRows.Length * rowH + Mathf.Max(0, _difficultyRows.Length - 1) * DifficultyRowGap;
            float lineH = LineOf(_modeLine);
            for (int i = 0; i < _difficultyRows.Length; i++)
            {
                lineH = Mathf.Max(lineH, Measure(_modeLine, _modeDesc[i], w), Measure(_modeLine, _modeDetail[i], w));
            }

            float difficultyH = headH + (int)UiSpace.S + rowsH + (int)UiSpace.S + lineH;
            float mapTitleH = string.IsNullOrEmpty(_mapTitle.text) ? 0f : LineOf(_mapTitle) + (int)UiSpace.XS;   // the MAP word is off (R2 U1)
            float mapNameH = LineOf(_mapName);
            float mapText = mapTitleH + (int)UiSpace.S + mapNameH;
            bool tip = _tipBlock.gameObject.activeSelf || !string.IsNullOrEmpty(_tipText.text);
            float tipH = tip ? Mathf.Max(TipGlyph, Measure(_tipText, _tipText.text, w - TipGlyph - (int)UiSpace.S)) : 0f;
            float thumbIdeal = Mathf.Floor(w * 9f / 16f);
            PrepareRunLayout.SetupBands bands = PrepareRunLayout.Setup(area.height, difficultyH, mapText, tipH, thumbIdeal, ThumbMin,
                (int)UiSpace.XL, (int)UiSpace.M);

            float y = 0f;
            Dock(_difficultyHeading.rectTransform, y, headH);
            y += headH + (int)UiSpace.S;
            for (int i = 0; i < _difficultyRows.Length; i++)
            {
                Dock((RectTransform)_difficultyRows[i].transform, y, rowH);
                y += rowH + (i < _difficultyRows.Length - 1 ? DifficultyRowGap : 0f);
            }

            y += (int)UiSpace.S;
            Dock(_modeLine.rectTransform, y, lineH);
            y += lineH + bands.Gap;

            float mapH = mapTitleH + bands.Thumb + (int)UiSpace.S + mapNameH;
            Dock(_mapBlock, y, mapH);
            SetTop(_mapTitle, 0f, 0f, mapTitleH);
            _mapFrame.gameObject.SetActive(bands.Thumb > 0f);
            if (bands.Thumb > 0f)
            {
                Dock(_mapFrame, mapTitleH, bands.Thumb);
                Texture art = _mapThumb.texture;
                _mapThumb.uvRect = art != null
                    ? PrepareRunLayout.CoverUv(art.width, art.height, w, bands.Thumb, ThumbFocusY)
                    : new Rect(0f, 0f, 1f, 1f);
            }

            SetTop(_mapName, 0f, mapTitleH + bands.Thumb + (int)UiSpace.S, mapNameH);
            y += mapH;

            _tipBlock.gameObject.SetActive(bands.Tip && tip);
            if (bands.Tip && tip)
            {
                y += bands.Gap;
                Dock(_tipBlock, y, tipH);
            }
        }

        private void SelectDifficulty(int index)
        {
            IPrepareDifficulty source = PrepareDifficulty.Source;
            if (!source.Select(index))
            {
                // A locked mode refuses as a locked weapon does: the row nudges, Denied plays, nothing changes.
                UiPresets.DeniedNudge((RectTransform)_difficultyRows[index].transform);
                UiSound.Play(UiSoundKind.Denied);
                return;
            }

            RefreshDifficulty();
            UiEvents.Raise(UiEvent.ChoiceSelected, "Difficulty");
        }

        private void RefreshDifficulty()
        {
            if (_difficultyRows == null)
            {
                return;
            }

            IPrepareDifficulty source = PrepareDifficulty.Source;
            int chosen = source.Selected;
            Sprite full = UiArt.Icon(MarkFull);
            Sprite empty = UiArt.Icon(MarkEmpty);
            for (int i = 0; i < _difficultyRows.Length; i++)
            {
                UiButton row = _difficultyRows[i];
                bool locked = source.IsLockedAt(i);
                bool selected = i == chosen && !locked;
                row.SetSelectedChoice(selected);   // the state the pad / tests read; the check glyph draws it
                row.ConfirmCue = !locked;          // WP-105 SHOULD-8: a refused press has one voice (Denied)
                UiBrushKit.SetRowStatus(row, selected ? CheckGlyph : locked ? LockGlyph : null, null);

                // The label stops before the marks (the kit measured it against the status glyph only).
                RectTransform label = row.LabelText.rectTransform;
                label.offsetMax = new Vector2(-(MarksRight + MarksWidth + (int)UiSpace.L), label.offsetMax.y);

                int level = source.LevelAt(i);
                Image[] marks = _difficultyMarks[i];
                for (int m = 0; m < marks.Length; m++)
                {
                    bool on = m < level;
                    UiArt.SetGlyph(marks[m], on ? full : empty);
                    marks[m].enabled = marks[m].sprite != null;
                    marks[m].color = UiTokens.Get(on ? UiColor.TextPrimary : UiColor.TextSecondary);
                }
            }

            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            int focused = DifficultyIndexOf(focus);
            _modeLineIndex = -1;
            ShowModeLine(focused >= 0 ? focused : chosen, focused >= 0);
        }

        /// <summary>
        /// The mode line: the focused mode's detail while a row has focus, else nothing - R4 WP-106 (R2 U1: u1_same_content
        /// measured 0.397 &lt; 0.40 with the chosen mode's short line at rest; the rows and their marks say the mode). Its band
        /// stays reserved (measured for every mode's line), so nothing moves when a row takes the focus.
        /// </summary>
        private void ShowModeLine(int index, bool focused)
        {
            if (_modeLine == null || _modeDesc == null || index < 0 || index >= _modeDesc.Length)
            {
                return;
            }

            if (index == _modeLineIndex && focused == _modeLineDetail)
            {
                return;
            }

            _modeLineIndex = index;
            _modeLineDetail = focused;
            string detail = _modeDetail[index];
            _modeLine.text = !focused ? string.Empty : !string.IsNullOrEmpty(detail) ? detail : _modeDesc[index];
        }

        private int DifficultyIndexOf(GameObject focus)
        {
            if (focus == null || _difficultyRows == null)
            {
                return -1;
            }

            for (int i = 0; i < _difficultyRows.Length; i++)
            {
                if (_difficultyRows[i] != null && _difficultyRows[i].gameObject == focus)
                {
                    return i;
                }
            }

            return -1;
        }

        // ================================================================= weapons

        /// <summary>
        /// One row per Arsenal classic weapon (WP-069 §1098: enabled only, sortOrder): the AP-036 side view (untinted, α 0.55
        /// while locked), the pack name at the dense list's 20 px, the check glyph on the choice, the lock glyph and its chip
        /// on a weapon that cannot start a run (WP-107: "WALL BUY" on the rocket launcher; "LEVEL n" / "PATH · Ln" where a
        /// profile has not earned one). Then the two Specials as information tiles.
        /// </summary>
        private void BuildWeaponColumn(RectTransform column)
        {
            var stack = new UiStack(column);
            stack.Add(ColumnHeading(UiStrings.Get("selection.weapon.title", "CLASSIC WEAPON"), column).rectTransform, UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.S);
            _weaponsColumn = column;
            _weaponsTop = stack.Cursor;

            List<WeaponDefinition> roster = RosterForDisplay();
            int count = roster.Count;
            _weaponIds = new ClassicWeaponId[count];
            _weaponDefs = new WeaponDefinition[count];
            _weaponButtons = new UiButton[count];
            _weaponLockWords = new string[count];
            _weaponGlyphs = new Image[count];
            _weaponSides = new int[count];
            _weaponEnabled = new bool[count];

            float rowHeight = Mathf.Max(WeaponRowHeight, UiFactory.LineHeight(UiType.H3) + (int)UiSpace.M);
            _rowHeight = rowHeight;

            for (int i = 0; i < count; i++)
            {
                WeaponDefinition def = roster[i];
                ClassicWeaponId id = def.id;
                bool enabled = def.enabled;
                bool unlocked = enabled && ProfileService.IsWeaponUnlocked(id);
                _weaponIds[i] = id;
                _weaponDefs[i] = def;
                _weaponEnabled[i] = enabled;

                UiButton button = UiBrushKit.ListRow(column, "Weapon." + def.weaponId + "." + id, WeaponName(id), null, () => SelectWeapon(id));
                stack.Add((RectTransform)button.transform, rowHeight);
                if (i < count - 1)
                {
                    stack.Cursor += WeaponRowGap;
                }

                // R3 m12 (WP-098B): the AP-036 side view, placed at the column's one scale by LayoutWeapons. Without it the
                // G1 cell by runtime id, then the definition's own HUD icon key.
                int side = WeaponSideView.IndexOf(def.weaponId);
                Sprite glyphSprite = side >= 0 ? WeaponSideView.Load(side) : null;
                if (glyphSprite == null)
                {
                    side = -1;
                    glyphSprite = UiArt.WeaponIcon(id);
                }

                if (glyphSprite == null)
                {
                    glyphSprite = UiArt.Icon(def.HudIconId);
                }

                _weaponSides[i] = side;

                float textLeft = (int)UiSpace.M + RowGlyph + (int)UiSpace.M;
                if (glyphSprite != null)
                {
                    Image glyph = UiFactory.NewImage("Icon", button.transform, UiColor.TextPrimary, glyphSprite);
                    glyph.preserveAspect = true;
                    if (UiArt.IsGlyph(glyph.sprite))
                    {
                        UiArt.ApplyGlyph(glyph, GlyphEdge);
                    }

                    Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2((int)UiSpace.M, 0f), new Vector2(RowGlyph, RowGlyph));
                    _weaponGlyphs[i] = glyph;
                }
                else
                {
                    Image well = UiFactory.NewImage("IconWell", button.transform, UiColor.SurfaceInset, UiSprites.Disc);
                    Place((RectTransform)well.transform, new Vector2(0f, 0.5f), new Vector2((int)UiSpace.M, 0f), new Vector2(RowGlyph, RowGlyph));
                    TMP_Text mono = UiFactory.Label("Monogram", well.transform, def.weaponId, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleCenter);
                    UiFactory.Fill((RectTransform)mono.transform);
                }

                // The name starts after the side-view slot (LayoutSideViews moves it); its right edge is the kit's, measured
                // from the row's status (SetRowStatus).
                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(textLeft, labelRect.offsetMin.y);
                // The dense list keeps its 20 px name (H3's size, not text-scaled): ten rows and the Specials must fit the Mac
                // column, where MenuSmall × 1.25 would need ~60 px rows (the kit's compact hit stays 48: WeaponRowHeight).
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAlignmentOptions.MidlineLeft;
                button.LabelText.overflowMode = TextOverflowModes.Ellipsis;

                if (!unlocked)
                {
                    _weaponLockWords[i] = enabled ? LockChip(id) : UiStrings.Get("selection.unavailable", "UNAVAILABLE");
                }

                _weaponButtons[i] = button;
            }

            BuildSpecialTiles(column, ref stack);
            LayoutWeapons();
        }

        /// <summary>
        /// The two Specials (DEC-094): found on the field, never chosen here - information tiles, not controls. The side view
        /// at the rows' scale; the name and fire mode from the pack, side by side - or stacked when they do not fit beside it
        /// (U2: the Mac canvas at UI 1.2 × text 1.25 cut "LOB"; PrepareRunLayout.SpecialBeside).
        /// </summary>
        private void BuildSpecialTiles(RectTransform column, ref UiStack stack)
        {
            stack.Gap(UiSpace.M);
            float band = UiFactory.LineHeight(UiType.Label);
            RectTransform head = UiFactory.NewRect("SpecialsHead", column);
            _specialsHead = head;
            _specialsTitleBand = band;
            stack.Add(head, band);
            TMP_Text title = UiFactory.Label("Title", head, UiStrings.Get("selection.specials.title", "SPECIAL WEAPONS"), UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            SetTop(title, 0f, 0f, band);
            stack.Gap(UiSpace.XS);

            RectTransform row = UiFactory.NewRect("Specials", column);
            _tileHeight = Mathf.Max(SpecialTileHeight, band + (int)UiSpace.M);
            stack.Add(row, _tileHeight);
            _specialsRow = row;
            string[] ids = { "sw_arc", "sw_gel" };
            string[] glyphKeys = { "Special.Raiju", "Special.Namekuji" };   // N19 G1 cells (the SpecialWeaponId names, DEC-094)
            for (int i = 0; i < ids.Length; i++)
            {
                RectTransform tile = UiFactory.FramedCard("Special." + ids[i], row, UiColor.BorderHairline, UiColor.SurfaceInset, UiFrame.Card);
                tile.anchorMin = new Vector2(i * 0.5f, 0f);
                tile.anchorMax = new Vector2(i * 0.5f + 0.5f, 1f);
                tile.offsetMin = new Vector2(i == 0 ? 0f : (int)UiSpace.XS, 0f);
                tile.offsetMax = new Vector2(i == 0 ? -(int)UiSpace.XS : 0f, 0f);

                int side = WeaponSideView.IndexOf(ids[i]);
                Sprite sprite = side >= 0 ? WeaponSideView.Load(side) : null;
                if (sprite == null)
                {
                    side = -1;
                    sprite = UiArt.Icon(glyphKeys[i]);
                }

                _specialSides[i] = side;
                float left = (int)UiSpace.M;
                if (sprite != null)
                {
                    Image glyph = UiFactory.NewImage("Icon", tile, UiColor.TextPrimary, sprite);
                    glyph.preserveAspect = true;
                    if (UiArt.IsGlyph(sprite))
                    {
                        UiArt.ApplyGlyph(glyph, GlyphEdge);
                    }

                    glyph.color = side >= 0 ? WeaponSideView.Tint(true) : UiTokens.Get(i == 0 ? UiGlyphTints.ChainLightning : UiGlyphTints.Gel);
                    Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2(left, 0f), new Vector2(RowGlyph, RowGlyph));
                    _specialIcons[i] = glyph;
                    left += RowGlyph + (int)UiSpace.S;
                }

                TMP_Text name = UiFactory.Label("Name", tile, UiStrings.Upper(UiStrings.Get("weapon." + ids[i] + ".name", ids[i])), UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleLeft);
                UiFactory.Fill((RectTransform)name.transform);
                name.enableWordWrapping = false;
                TMP_Text mode = UiFactory.Label("Mode", tile, UiStrings.Get("weapon." + ids[i] + ".firemode", string.Empty), UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleRight);
                UiFactory.Fill((RectTransform)mode.transform);
                mode.enableWordWrapping = false;
                mode.overflowMode = TextOverflowModes.Ellipsis;
                _specialNames[i] = name;
                _specialModes[i] = mode;
                _specialTextLeft[i] = left;
                _specialModeSize = mode.fontSize;
            }
        }

        // =================================================================== stage

        private void BuildStage(RectTransform column)
        {
            UiCard card = UiCard.Create("StageCard", column, Vector2.zero, UiElevation.Elev1, UiColor.SurfacePanel, UiSpace.XL);
            UiFactory.Fill((RectTransform)card.transform);
            _stageCard = (RectTransform)card.transform;
            RectTransform content = card.Content;
            _stageContent = content;
            _stageText = UiFactory.Group(content.gameObject);

            // Every block is built here and placed by LayoutStage from the texts it holds (measured, not stacked on fixed
            // bands: the Mac canvas at UI scale 1.2 × TextScale 1.25 is the short case; the preview takes the slack).
            _stageTop = UiFactory.NewRect("Identity", content);
            _stageName = TopLabel("Name", _stageTop, UiType.H1, UiColor.TextPrimary);
            _stageClass = TopLabel("Class", _stageTop, UiType.Label, UiColor.TextSecondary);
            _stageRole = TopLabel("Role", content, UiType.Body, UiColor.TextPrimary);
            _stageName.enableWordWrapping = false;
            _stageClass.enableWordWrapping = false;
            _stageName.overflowMode = TextOverflowModes.Ellipsis;
            _stageClass.overflowMode = TextOverflowModes.Ellipsis;
            _stageRole.enableWordWrapping = true;

            // The 3D preview: a recessed well, the side view standing in until the first render, the stage's view over it, and
            // the device's hint glyph in its corner (its words on the pointer's hover only - R2 U1). Nothing over the view takes
            // the pointer: the stage's drag, wheel and double click land on its own image.
            Image well = UiFactory.NewImage("Preview", content, UiColor.SurfaceInset, UiSprites.Card);
            _previewWell = (RectTransform)well.transform;
            _stageGlyph = UiFactory.NewImage("StageGlyph", _previewWell, UiColor.TextPrimary, UiArt.WeaponIcon(ClassicWeaponId.AssaultRifle));
            _stageGlyph.preserveAspect = true;
            if (UiArt.IsGlyph(_stageGlyph.sprite))
            {
                UiArt.ApplyGlyph(_stageGlyph, GlyphEdge);
            }

            _stage = CreateStage(_previewWell);
            _awaitFirstRender = _stage != null;
            if (_stage != null && _stage.View != null)
            {
                _previewHover = _stage.View.gameObject.AddComponent<PrepareRunPreviewHover>();
                _previewHover.Bind(OnPreviewHover);
            }

            _previewHint = UiFactory.NewImage("Hint", _previewWell, UiColor.TextSecondary, UiArt.Icon(HintGlyphKey(UiInputScheme.Current)));
            _previewHint.preserveAspect = true;
            _previewHint.enabled = _previewHint.sprite != null && _stage != null;
            Place((RectTransform)_previewHint.transform, new Vector2(1f, 0f), new Vector2(-(int)UiSpace.M, (int)UiSpace.M), new Vector2(HintGlyph, HintGlyph));
            ((RectTransform)_previewHint.transform).pivot = new Vector2(1f, 0f);
            _previewHintText = UiFactory.Label("HintText", _previewWell, UiStrings.Get("selection.preview.hint", "Drag: rotate · Scroll: zoom"),
                UiType.Caption, UiColor.TextSecondary, TextAnchor.LowerRight);
            _previewHintText.enableWordWrapping = false;
            RectTransform hintText = _previewHintText.rectTransform;
            hintText.anchorMin = new Vector2(0f, 0f);
            hintText.anchorMax = new Vector2(1f, 0f);
            hintText.pivot = new Vector2(1f, 0f);
            hintText.offsetMin = new Vector2((int)UiSpace.M, (int)UiSpace.M);
            hintText.offsetMax = new Vector2(-((int)UiSpace.M + HintGlyph + (int)UiSpace.S), (int)UiSpace.M + HintGlyph);
            _previewHintText.gameObject.SetActive(false);

            // Five stat cards: icon + short label, the number, the roster bar (a card without a bar keeps its row).
            _statsRow = UiFactory.NewRect("Stats", content);
            for (int s = 0; s < PrepareRunLayout.StatCount; s++)
            {
                RectTransform cell = UiFactory.FramedCard("Stat" + s, _statsRow, UiColor.BorderHairline, UiColor.SurfaceInset, UiFrame.Card);
                _statCells[s] = cell;
                Image icon = UiFactory.NewImage("Icon", cell, UiColor.TextSecondary, null);
                icon.preserveAspect = true;
                icon.enabled = false;
                Place((RectTransform)icon.transform, new Vector2(0f, 1f), new Vector2((int)UiSpace.S, -(int)UiSpace.S), new Vector2(StatIcon, StatIcon));
                _statIcons[s] = icon;
                _statLabels[s] = TopLabel("Label", cell, UiType.Caption, UiColor.TextSecondary);
                _statValues[s] = TopLabel("Value", cell, UiType.Label, UiColor.TextPrimary);
                _statLabels[s].enableWordWrapping = false;
                _statValues[s].enableWordWrapping = false;

                UiBar bar = UiFactory.Bar("Bar", cell, UiColor.TextPrimary, StatBarHeight);
                RectTransform barRect = (RectTransform)bar.transform;
                barRect.anchorMin = new Vector2(0f, 0f);
                barRect.anchorMax = new Vector2(1f, 0f);
                barRect.pivot = new Vector2(0.5f, 0f);
                barRect.offsetMin = new Vector2((int)UiSpace.S, (int)UiSpace.S);
                barRect.offsetMax = new Vector2(-(int)UiSpace.S, (int)UiSpace.S + StatBarHeight);
                _statBars[s] = bar;
            }

            // The state line: what keeps the weapon out of a starting loadout (WP-107: the wall-buy weapon), else empty.
            _stateRow = UiFactory.NewRect("State", content);
            _stateLine = UiFactory.Label("Line", _stateRow, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            _stateLine.richText = true;   // the PATH line marks each met part in cream (built on a focus change)
            _stateLine.enableWordWrapping = true;
            UiFactory.Fill((RectTransform)_stateLine.transform);
        }

        /// <summary>P1's 3D stage on the well; a stage that cannot be built leaves the side view standing in (never a dead page).</summary>
        private static PrepareRunStage CreateStage(RectTransform host)
        {
            try
            {
                return PrepareRunStage.Create(host);
            }
            catch (Exception e)
            {
                Debug.LogError("[PrepareRun] the 3D preview could not be built - the side view stands in: " + e.Message);
                return null;
            }
        }

        private static string HintGlyphKey(InputSchemeKind scheme)
        {
            return scheme == InputSchemeKind.Gamepad ? "Pad.RSMove" : "Mouse.Move";
        }

        private void OnSchemeChanged(InputSchemeKind scheme)
        {
            if (_previewHint == null)
            {
                return;
            }

            UiArt.SetGlyph(_previewHint, UiArt.Icon(HintGlyphKey(scheme)));
            _previewHint.enabled = _previewHint.sprite != null && _stage != null;
            if (scheme == InputSchemeKind.Gamepad)
            {
                _previewHintText.gameObject.SetActive(false);
            }
        }

        private void OnPreviewHover(bool over)
        {
            if (_previewHintText != null)
            {
                _previewHintText.gameObject.SetActive(over && _stage != null && UiInputScheme.Current != InputSchemeKind.Gamepad);
            }
        }

        /// <summary>
        /// Places the stage's blocks from what they hold at the card's real size (PrepareRunLayout.Stage): the name with its
        /// class beside it when both hold one line (else the class under it), the role (measured, it wraps, never cut), the
        /// preview (the slack), the stat cards (five a row while each card's widest line fits, else three over two …) and the
        /// state line (its place kept when empty). Short of room the gaps close, then the preview shrinks; the texts never
        /// yield. Run on a focus change and when the card's size changes, never per frame otherwise.
        /// </summary>
        private void LayoutStage()
        {
            if (_stageContent == null)
            {
                return;
            }

            Rect area = _stageContent.rect;
            _stageSize = area.size;
            if (area.height < 1f || area.width < 1f)
            {
                _stageLayoutPending = true;   // the canvas has not laid out yet: next Update
                return;
            }

            _stageLayoutPending = false;
            float w = area.width;
            float h = area.height;

            // Identity
            float nameLine = LineOf(_stageName);
            bool hasClass = !string.IsNullOrEmpty(_stageClass.text);
            float classLine = hasClass ? LineOf(_stageClass) : 0f;
            float nameW = Mathf.Ceil(_stageName.GetPreferredValues(_stageName.text).x);
            float classW = hasClass ? Mathf.Ceil(_stageClass.GetPreferredValues(_stageClass.text).x) : 0f;
            bool classBeside = hasClass && nameW + (int)UiSpace.L + classW <= w;
            float roleH = Measure(_stageRole, _stageRole.text, w);
            float topH = nameLine + (hasClass && !classBeside ? (int)UiSpace.XS + classLine : 0f);
            float identityH = topH + (roleH > 0f ? (int)UiSpace.S + roleH : 0f);

            // Stat cards
            float labelLine = LineOf(_statLabels[0]);
            float valueLine = LineOf(_statValues[0]);
            for (int s = 0; s < PrepareRunLayout.StatCount; s++)
            {
                float lw = Mathf.Ceil(_statLabels[s].GetPreferredValues(_statLabels[s].text).x);
                float vw = Mathf.Ceil(_statValues[s].GetPreferredValues(_statValues[s].text).x);
                float iconW = _statIcons[s].enabled ? StatIcon + (int)UiSpace.XS : 0f;
                _statNeed[s] = (int)UiSpace.S * 2f + Mathf.Max(iconW + lw, vw);
            }

            _statColumns = PrepareRunLayout.StatColumns(w, (int)UiSpace.M, _statNeed);
            int rows = PrepareRunLayout.Rows(PrepareRunLayout.StatCount, _statColumns);
            float cellW = PrepareRunLayout.CellWidth(w, (int)UiSpace.M, _statColumns);
            float headLine = Mathf.Max(StatIcon, labelLine);
            float cellH = (int)UiSpace.S + headLine + (int)UiSpace.XS + valueLine + (int)UiSpace.XS + StatBarHeight + (int)UiSpace.S;
            float statsH = rows * cellH + (rows - 1) * (int)UiSpace.M;

            // State line: its measured lines, at least one (its place is kept when empty: the stats do not jump on focus).
            float stateH = Mathf.Max(LineOf(_stateLine), Measure(_stateLine, _stateLine.text, w));

            PrepareRunLayout.StageBands bands = PrepareRunLayout.Stage(h, identityH + statsH + stateH, (int)UiSpace.L, (int)UiSpace.S, PreviewMin, PreviewHide,
                Mathf.Floor(w * PreviewMaxAspect));

            float y = 0f;
            Dock(_stageTop, y, topH);
            SetTop(_stageName, 0f, 0f, nameLine);
            _stageName.alignment = TextAlignmentOptions.TopLeft;
            if (classBeside)
            {
                // Beside the name, on the name band's foot (its own baseline, right-aligned).
                SetTop(_stageClass, nameW + (int)UiSpace.L, 0f, nameLine);
                _stageClass.alignment = TextAlignmentOptions.BottomRight;
            }
            else if (hasClass)
            {
                SetTop(_stageClass, 0f, nameLine + (int)UiSpace.XS, classLine);
                _stageClass.alignment = TextAlignmentOptions.TopLeft;
            }

            y += topH;
            if (roleH > 0f)
            {
                y += (int)UiSpace.S;
                Dock(_stageRole.rectTransform, y, roleH);
                y += roleH;
            }

            y += bands.Gap;
            _previewWell.gameObject.SetActive(bands.Preview > 0f);
            if (bands.Preview > 0f)
            {
                // A capped preview stands in the middle of its band; the cards and the state line keep the card's foot.
                y += Mathf.Floor(bands.Slack * 0.5f);
                Dock(_previewWell, y, bands.Preview);
                PlaceStandIn(w, bands.Preview);
                y += bands.Preview + bands.Gap + (bands.Slack - Mathf.Floor(bands.Slack * 0.5f));
            }

            Dock(_statsRow, y, statsH);
            for (int s = 0; s < PrepareRunLayout.StatCount; s++)
            {
                int col = s % _statColumns;
                int row = s / _statColumns;
                RectTransform cell = _statCells[s];
                cell.anchorMin = new Vector2(0f, 1f);
                cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                cell.sizeDelta = new Vector2(cellW, cellH);
                cell.anchoredPosition = new Vector2(col * (cellW + (int)UiSpace.M), -(row * (cellH + (int)UiSpace.M)));

                float labelLeft = (int)UiSpace.S + (_statIcons[s].enabled ? StatIcon + (int)UiSpace.XS : 0f);
                SetTop(_statLabels[s], labelLeft, (int)UiSpace.S, headLine);
                RectTransform labelRect = _statLabels[s].rectTransform;
                labelRect.offsetMax = new Vector2(-(int)UiSpace.S, labelRect.offsetMax.y);
                _statLabels[s].alignment = TextAlignmentOptions.MidlineLeft;
                SetTop(_statValues[s], (int)UiSpace.S, (int)UiSpace.S + headLine + (int)UiSpace.XS, valueLine);
                RectTransform valueRect = _statValues[s].rectTransform;
                valueRect.offsetMax = new Vector2(-(int)UiSpace.S, valueRect.offsetMax.y);
                _statValues[s].alignment = TextAlignmentOptions.MidlineLeft;
                RectTransform iconRect = (RectTransform)_statIcons[s].transform;
                iconRect.anchoredPosition = new Vector2((int)UiSpace.S, -((int)UiSpace.S + (headLine - StatIcon) * 0.5f));
            }

            y += statsH + bands.Gap;
            Dock(_stateRow, y, stateH);
        }

        /// <summary>The side view stand-in, centred in the well at the one scale the well allows (the longest weapon fills it).</summary>
        private void PlaceStandIn(float w, float h)
        {
            int side = _weaponSides != null && _stageIndex >= 0 && _stageIndex < _weaponSides.Length ? _weaponSides[_stageIndex] : -1;
            RectTransform rect = (RectTransform)_stageGlyph.transform;
            float inset = (int)UiSpace.XL;
            if (side >= 0)
            {
                float scale = WeaponSideView.PixelsPerMetre(Mathf.Max(1f, w - inset * 2f), Mathf.Max(1f, h - inset * 2f));
                WeaponSideView.Entry e = WeaponSideView.Table[side];
                float length = e.LengthM * scale;
                float height = e.HeightM * scale;
                WeaponSideView.Place(rect, side, scale, new Vector2(0f, 1f), new Vector2((w + length) * 0.5f, -(h + height) * 0.5f));
            }
            else
            {
                float size = Mathf.Max(1f, Mathf.Min(h - inset * 2f, 128f));
                Place(rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            }
        }

        /// <summary>
        /// Fits the weapon rows and the Specials strip to the column's real height. Short of room (the Mac canvas at UI 1.2 ×
        /// TextScale 1.25) the rows give up height down to their text line + 8 px. The Specials tiles stack their name and
        /// mode when the two do not fit side by side (U2).
        /// </summary>
        private void LayoutWeapons()
        {
            if (_weaponsColumn == null || _weaponButtons == null || _specialsRow == null)
            {
                return;
            }

            _weaponsSize = _weaponsColumn.rect.size;
            float avail = _weaponsSize.y;
            if (avail < 1f || _weaponsSize.x < 1f)
            {
                _weaponsLayoutPending = true;
                return;
            }

            _weaponsLayoutPending = false;
            float tileW = Mathf.Max(1f, _weaponsSize.x * 0.5f - (int)UiSpace.XS);
            if (_specialsStacked && _specialsStackedSize != _weaponsSize)
            {
                _specialsStacked = false;   // a new size (window, UI or text scale): measure beside again
            }

            _specialsBeside = !_specialsStacked && SpecialsFitBeside(tileW);
            float minTile = Mathf.Max(SpecialTileHeight, _specialsTitleBand + (int)UiSpace.M);
            _tileHeight = PrepareRunLayout.SpecialTileHeight(_specialsBeside, minTile, LineOf(_specialNames[0]), ModeLineHeight(), (int)UiSpace.S);

            int n = _weaponButtons.Length;
            float rowH = _rowHeight;
            float Need()
            {
                return _weaponsTop + n * rowH + Mathf.Max(0, n - 1) * WeaponRowGap + (int)UiSpace.M
                       + _specialsTitleBand + (int)UiSpace.XS + _tileHeight;
            }

            if (Need() > avail && n > 0)
            {
                float floor = UiFactory.LineHeight(UiType.H3) + (int)UiSpace.S;
                rowH = Mathf.Max(floor, Mathf.Floor(rowH - (Need() - avail) / n));
            }

            float y = _weaponsTop;
            for (int i = 0; i < n; i++)
            {
                var rect = (RectTransform)_weaponButtons[i].transform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, rowH);
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -y);
                y += rowH + (i < n - 1 ? WeaponRowGap : 0f);
            }

            y += (int)UiSpace.M;
            float headH = _specialsTitleBand;
            Dock(_specialsHead, y, headH);
            y += headH + (int)UiSpace.XS;
            Dock(_specialsRow, y, _tileHeight);
            LayoutSideViews(rowH, tileW);
            if (!LayoutSpecialTiles() && _specialsBeside)
            {
                // R4 WP-106 (U2 "LOB" at Mac 1.2 x text 1.25): the side views' floor scale (LayoutSideViews) can leave a
                // mode less room than SpecialsFitBeside reckoned - laid beside, it would cut. Stack the tiles for this size
                // and lay the column once more (the next size measures beside again).
                _specialsStacked = true;
                _specialsStackedSize = _weaponsSize;
                LayoutWeapons();
            }
        }

        /// <summary>The Specials' name and mode fit one line beside the smallest side view (the mode at 80 %), in both tiles.</summary>
        private bool SpecialsFitBeside(float tileW)
        {
            bool beside = true;
            for (int i = 0; i < _specialNames.Length; i++)
            {
                if (_specialNames[i] == null || _specialModes[i] == null)
                {
                    continue;
                }

                _specialModes[i].fontSize = _specialModeSize;
                float iconW = _specialSides[i] >= 0
                    ? Mathf.Ceil(WeaponSideView.Table[_specialSides[i]].LengthM * RowGlyph / WeaponSideView.MaxLengthM)
                    : (_specialIcons[i] != null ? RowGlyph : 0f);
                float nameW = Mathf.Ceil(_specialNames[i].GetPreferredValues(_specialNames[i].text).x);
                float modeW = Mathf.Ceil(_specialModes[i].GetPreferredValues(_specialModes[i].text).x * 0.8f);
                beside &= PrepareRunLayout.SpecialBeside(tileW, iconW, nameW, modeW, (int)UiSpace.M, (int)UiSpace.S);
            }

            return beside;
        }

        private float ModeLineHeight()
        {
            return _specialModes[0] != null ? LineOf(_specialModes[0]) : 0f;
        }

        /// <summary>
        /// R3 m12 (WP-098B): the weapons column's side views at one scale - the rows and the two Specials. The scale is the
        /// largest that lets the longest weapon fill the row slot (at most 128, never past the longest row name's room), the
        /// tallest stand in the row less 8, and each Special keep its words beside it (or, stacked, its longer word); it never
        /// drops below the old 40 px glyph slot. Each weapon's stock stands on the slot's right edge (rows) or after the tile's
        /// inset (Specials), its foot on one baseline - the tallest weapon centred in the row.
        /// </summary>
        private void LayoutSideViews(float rowH, float tileW)
        {
            int n = _weaponButtons.Length;
            float colW = _weaponsColumn.rect.width;
            float nameMax = 0f;
            for (int i = 0; i < n; i++)
            {
                TMP_Text label = _weaponButtons[i].LabelText;
                nameMax = Mathf.Max(nameMax, Mathf.Ceil(label.GetPreferredValues(label.text).x));
            }

            float slotH = Mathf.Max(1f, Mathf.Min(rowH, _tileHeight) - (int)UiSpace.S);
            float slotCap = Mathf.Clamp(colW - BadgeWidth - (int)UiSpace.M * 2f - nameMax, RowGlyph, RowSideMax);
            float scale = WeaponSideView.PixelsPerMetre(slotCap, slotH);
            for (int i = 0; i < _specialNames.Length; i++)
            {
                if (_specialSides[i] < 0 || _specialNames[i] == null || _specialModes[i] == null)
                {
                    continue;
                }

                TMP_Text mode = _specialModes[i];
                mode.fontSize = _specialModeSize;
                float nameW = Mathf.Ceil(_specialNames[i].GetPreferredValues(_specialNames[i].text).x);
                float modeW = Mathf.Ceil(mode.GetPreferredValues(mode.text).x);
                float room = _specialsBeside
                    ? tileW - (int)UiSpace.M * 2f - (int)UiSpace.S * 2f - nameW - Mathf.Ceil(modeW * 0.8f)
                    : tileW - (int)UiSpace.M * 2f - (int)UiSpace.S - Mathf.Max(nameW, modeW);
                scale = Mathf.Min(scale, room / WeaponSideView.Table[_specialSides[i]].LengthM);
            }

            scale = Mathf.Max(scale, WeaponSideView.PixelsPerMetre(RowGlyph, slotH));
            float slotW = Mathf.Ceil(WeaponSideView.MaxLengthM * scale);
            float baseline = -Mathf.Ceil(WeaponSideView.MaxHeightM * scale) * 0.5f;
            float textLeft = (int)UiSpace.M + slotW + (int)UiSpace.M;
            for (int i = 0; i < n; i++)
            {
                if (_weaponGlyphs[i] != null && _weaponSides[i] >= 0)
                {
                    WeaponSideView.Place((RectTransform)_weaponGlyphs[i].transform, _weaponSides[i], scale,
                        new Vector2(0f, 0.5f), new Vector2((int)UiSpace.M + slotW, baseline));
                }

                RectTransform labelRect = (RectTransform)_weaponButtons[i].LabelText.transform;
                labelRect.offsetMin = new Vector2(_weaponSides[i] >= 0 ? textLeft : (int)UiSpace.M + RowGlyph + (int)UiSpace.M, labelRect.offsetMin.y);
            }

            for (int i = 0; i < _specialIcons.Length; i++)
            {
                if (_specialIcons[i] == null || _specialSides[i] < 0 || _specialNames[i] == null)
                {
                    continue;
                }

                float width = Mathf.Ceil(WeaponSideView.Table[_specialSides[i]].LengthM * scale);
                WeaponSideView.Place((RectTransform)_specialIcons[i].transform, _specialSides[i], scale,
                    new Vector2(0f, 0.5f), new Vector2((int)UiSpace.M + width, baseline));
                _specialTextLeft[i] = (int)UiSpace.M + width + (int)UiSpace.S;
            }
        }

        /// <summary>
        /// U1b MUST-2 (AD + ui-ux, 06.10) + WP-106 (U2): beside, the name takes its own width and never shrinks, the mode stands
        /// right-aligned at least S after it, stepping down to 80 % of its size when the room is short (it fits there:
        /// SpecialsFitBeside checked). Stacked, the name is the tile's first line and the mode its second, both at full size.
        /// </summary>
        /// <returns>false when a tile laid beside could not keep its mode whole (even at 80 %): the caller stacks the tiles.</returns>
        private bool LayoutSpecialTiles()
        {
            bool fits = true;
            for (int i = 0; i < _specialNames.Length; i++)
            {
                TMP_Text name = _specialNames[i];
                TMP_Text mode = _specialModes[i];
                if (name == null || mode == null)
                {
                    continue;
                }

                float tileW = ((RectTransform)name.transform.parent).rect.width;
                float left = _specialTextLeft[i];
                RectTransform nameRect = (RectTransform)name.transform;
                RectTransform modeRect = (RectTransform)mode.transform;
                mode.fontSize = _specialModeSize;
                if (!_specialsBeside)
                {
                    float nameLine = LineOf(name);
                    float modeLine = LineOf(mode);
                    float top = Mathf.Max(0f, (_tileHeight - nameLine - modeLine) * 0.5f);
                    SetTop(name, left, top, nameLine);
                    nameRect.offsetMax = new Vector2(-(int)UiSpace.M, nameRect.offsetMax.y);
                    name.alignment = TextAlignmentOptions.MidlineLeft;
                    SetTop(mode, left, top + nameLine, modeLine);
                    modeRect.offsetMax = new Vector2(-(int)UiSpace.M, modeRect.offsetMax.y);
                    mode.alignment = TextAlignmentOptions.MidlineLeft;
                    continue;
                }

                UiFactory.Fill(nameRect);
                UiFactory.Fill(modeRect);
                name.alignment = TextAlignmentOptions.MidlineLeft;
                mode.alignment = TextAlignmentOptions.MidlineRight;
                float nameW = Mathf.Ceil(name.GetPreferredValues(name.text).x);
                float nameRight = Mathf.Min(tileW - (int)UiSpace.M, left + nameW);
                nameRect.offsetMin = new Vector2(left, 0f);
                nameRect.offsetMax = new Vector2(nameRight - tileW, 0f);

                float modeLeft = nameRight + (int)UiSpace.S;
                float room = Mathf.Max(0f, tileW - (int)UiSpace.M - modeLeft);
                modeRect.offsetMin = new Vector2(modeLeft, 0f);
                modeRect.offsetMax = new Vector2(-(int)UiSpace.M, 0f);
                float modeW = Mathf.Ceil(mode.GetPreferredValues(mode.text).x);
                if (modeW > room && modeW > 0f)
                {
                    mode.fontSize = Mathf.Max(_specialModeSize * 0.8f, _specialModeSize * room / modeW);
                    fits &= modeW * 0.8f + 1f <= room;   // 1 px for TMP's sub-pixel advance at the smaller size
                }
            }

            return fits;
        }

        private static float LineOf(TMP_Text text)
        {
            return Mathf.Ceil(text.GetPreferredValues("Ag").y);
        }

        /// <summary>The height <paramref name="words"/> take wrapped at <paramref name="width"/> (0 for none).</summary>
        private static float Measure(TMP_Text text, string words, float width)
        {
            return string.IsNullOrEmpty(words) ? 0f : Mathf.Ceil(text.GetPreferredValues(words, Mathf.Max(1f, width), 0f).y);
        }

        private static void Dock(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
        }

        private static void SetTop(TMP_Text label, float left, float top, float height)
        {
            RectTransform rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
        }

        private static TMP_Text TopLabel(string name, RectTransform parent, UiType type, UiColor colour)
        {
            TMP_Text label = UiFactory.Label(name, parent, string.Empty, type, colour, TextAnchor.UpperLeft);
            SetTop(label, 0f, 0f, 1f);
            return label;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 at, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = at;
        }

        /// <summary>Put roster row <paramref name="index"/> on the stage (and in the 3D preview). Rebuilt on a change only.</summary>
        private void ShowOnStage(int index)
        {
            if (index < 0 || _weaponIds == null || index >= _weaponIds.Length || index == _stageIndex || _stageName == null)
            {
                return;
            }

            bool first = _stageIndex < 0;
            _stageIndex = index;
            ClassicWeaponId id = _weaponIds[index];
            WeaponDefinition def = _weaponDefs[index];
            string key = def != null ? def.weaponId : null;
            bool enabled = _weaponEnabled[index];
            bool unlocked = enabled && ProfileService.IsWeaponUnlocked(id);

            _stageName.text = UiStrings.Upper(WeaponName(id));
            _stageClass.text = UiStrings.Get("weapon." + key + ".class", string.Empty);
            _stageRole.text = UiStrings.Get("weapon." + key + ".role", string.Empty);

            // The 3D preview shows the weapon; the side view stands in until the stage's first render.
            if (_stage != null && key != null)
            {
                _stage.ShowWeapon(key);
            }

            Sprite sprite = _weaponGlyphs[index] != null ? _weaponGlyphs[index].sprite : null;
            UiArt.SetGlyph(_stageGlyph, sprite);
            _stageGlyph.enabled = sprite != null;
            _stageGlyph.color = RowTint(index, unlocked);
            _stageGlyph.gameObject.SetActive(_stage == null || _stage.Renders == 0);

            string[] keys = PrepareRunLayout.CardKeys(def);
            for (int s = 0; s < PrepareRunLayout.StatCount; s++)
            {
                _stats[s] = CardStat(def, key, keys[s]);
                _statLabels[s].text = _stats[s].Label;
                _statValues[s].text = _stats[s].Value;
                string iconKey = CardIcon(def, keys[s]);
                Sprite icon = iconKey != null ? UiArt.Icon(iconKey) : null;
                UiArt.SetGlyph(_statIcons[s], icon);
                _statIcons[s].enabled = icon != null;
                bool hasBar = _stats[s].Bar01 >= 0f;
                _statBars[s].gameObject.SetActive(hasBar);
                if (hasBar)
                {
                    // E9: the bars move with the focus (eased by UiBar); the first show lands at once.
                    _statBars[s].SetValue(_stats[s].Bar01 > 0f ? Mathf.Max(StatBarMinFill, _stats[s].Bar01) : 0f, first);
                }
            }

            // The state line names only what stands between the player and the weapon (AD R1-B MUST-2a: a weapon the player
            // can start with shows no line).
            if (!enabled)
            {
                _stateLine.text = UiStrings.Get("selection.not_in_build", "Not in this build.");
            }
            else if (unlocked)
            {
                _stateLine.text = string.Empty;
            }
            else
            {
                _stateLine.text = LockLineFor(ProfileService.WeaponLock(id));
            }

            LayoutStage();

            if (!first)
            {
                // A focus change re-inks the stage: the block dips and returns (Fast, Decor - at rest under RM).
                _stageText.alpha = 0.55f;
                UiMotion.Alpha(_stageText, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            }
        }

        /// <summary>One stat card: the readout's pack label, number and roster bar; the spread and the fire mode carry no bar.</summary>
        private static WeaponStat CardStat(WeaponDefinition def, string weaponKey, string cardKey)
        {
            if (cardKey == PrepareRunLayout.SpreadKey)
            {
                WeaponStat spread = WeaponStatReadout.Read(def, WeaponStatReadout.Accuracy);
                spread.Key = cardKey;
                spread.Label = UiStrings.Get("stat.spread", "SPREAD");
                spread.Bar01 = -1f;   // copy-r4 §2: a "less is better" bar misreads - the number alone
                return spread;
            }

            if (cardKey == PrepareRunLayout.FireModeKey)
            {
                return new WeaponStat
                {
                    Key = cardKey,
                    Label = UiStrings.Get("stat.fire_mode", "FIRE MODE"),
                    Value = FireModeWord(weaponKey, def),
                    Bar01 = -1f
                };
            }

            return WeaponStatReadout.Read(def, cardKey);
        }

        /// <summary>The card's G1 icon: the stat's own (O1 family), the spread arc, the fire mode's glyph (none for a burst).</summary>
        private static string CardIcon(WeaponDefinition def, string cardKey)
        {
            switch (cardKey)
            {
                case WeaponStatReadout.Damage: return "Stat.Damage";
                case WeaponStatReadout.FireRate: return "Stat.FireRate";
                case WeaponStatReadout.Magazine: return "Stat.Magazine";
                case WeaponStatReadout.Range: return "Stat.Range";
                case PrepareRunLayout.SpreadKey: return "Reticle.SpreadArc";
                case PrepareRunLayout.FireModeKey:
                    if (def == null || def.fire == null) return null;
                    return def.fire.fireMode == WeaponFireMode.Auto ? "FireMode.Auto" : def.fire.fireMode == WeaponFireMode.Single ? "FireMode.Semi" : null;
                default: return null;
            }
        }

        /// <summary>The pack's fire mode word ("AUTO", "BURST ×3", "PUMP" …), its {count} filled from the definition.</summary>
        private static string FireModeWord(string weaponKey, WeaponDefinition def)
        {
            string mode = UiStrings.Get("weapon." + weaponKey + ".firemode", string.Empty);
            if (def != null && mode.Contains("{count}"))
            {
                mode = mode.Replace("{count}", Mathf.Max(1, def.fire.burstCount).ToString(CultureInfo.InvariantCulture));
            }

            return string.IsNullOrEmpty(mode) ? "—" : mode;
        }

        /// <summary>
        /// A locked row's chip, from the profile's lock (built once per row): WP-107 "WALL BUY" on a weapon bought at its wall
        /// in the run; on the level ladder "LEVEL 3" (selection.lock.chip - the kit's lock glyph says LOCKED); on the PATH
        /// "PATH · L11" (the pack's path.lock.line form) or "PATH › ready".
        /// </summary>
        private static string LockChip(ClassicWeaponId id)
        {
            return LockChipFor(ProfileService.WeaponLock(id));
        }

        /// <summary>The chip words for a lock (public for the EditMode tests: the words, no profile needed).</summary>
        public static string LockChipFor(MetaPathRules.WeaponLockInfo info)
        {
            if (PrepareRunLayout.IsWallBuyOnly(info))
            {
                return UiStrings.Get("selection.lock.wallbuy_chip", "WALL BUY");   // WP-107 (R4): rl_1 is bought in the run
            }

            string level = info.LevelGate.ToString(CultureInfo.InvariantCulture);
            if (info.OnPath)
            {
                return info.Ready ? UiStrings.Upper(UiStrings.Get("path.lock.ready", "PATH › ready")) : UiStrings.Get("path.title", "PATH") + " · L" + level;
            }

            return UiStrings.Format("selection.lock.chip", "level", level, "LEVEL {level}");
        }

        /// <summary>
        /// The stage's lock line: WP-107's wall-buy sentence; "Reach profile level 3 · you are level 1" on the ladder; on the
        /// PATH the pack's path.lock.line (cost and level gate, each number in cream once met), or "PATH › ready".
        /// </summary>
        public static string LockLineFor(MetaPathRules.WeaponLockInfo info)
        {
            if (PrepareRunLayout.IsWallBuyOnly(info))
            {
                return UiStrings.Get("selection.lock.wallbuy", "Bought at its wall during a run. It is never a starting weapon.");
            }

            string level = info.LevelGate.ToString(CultureInfo.InvariantCulture);
            if (!info.OnPath)
            {
                int current = ProfileService.Current != null ? ProfileService.Current.ProfileLevel : 1;
                return UiStrings.Get("selection.lock.level", "Reach profile level {level} · you are level {current}")
                    .Replace("{level}", level)
                    .Replace("{current}", current.ToString(CultureInfo.InvariantCulture));
            }

            if (info.Ready)
            {
                return UiStrings.Get("path.lock.ready", "PATH › ready");
            }

            // The currency word comes from the pack (A9: "Kül" in both languages); only the numbers are marked.
            return UiStrings.Get("path.lock.line", "PATH · {kul} · L{level}")
                .Replace("{kul}", Met(info.AshCost.ToString(CultureInfo.InvariantCulture), info.AshMet))
                .Replace("L{level}", Met("L" + level, info.LevelMet))
                .Replace("{level}", Met(level, info.LevelMet));
        }

        private static string Met(string part, bool met)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(UiTokens.Get(met ? UiColor.TextPrimary : UiColor.TextSecondary)) + ">" + part + "</color>";
        }

        /// <summary>The pack's weapon name (Hinawa …), else the config display name.</summary>
        private static string WeaponName(ClassicWeaponId id)
        {
            WeaponDefinition def = Arsenal.Get(id);
            string name = def != null ? UiStrings.Get("weapon." + def.weaponId + ".name", null) : null;
            return !string.IsNullOrEmpty(name) ? name : ProfileService.WeaponDisplayName(id);
        }

        private int IndexOf(GameObject focus)
        {
            if (focus == null || _weaponButtons == null)
            {
                return -1;
            }

            for (int i = 0; i < _weaponButtons.Length; i++)
            {
                if (_weaponButtons[i] != null && _weaponButtons[i].gameObject == focus)
                {
                    return i;
                }
            }

            return -1;
        }

        private int SelectedIndex()
        {
            if (_weaponIds == null)
            {
                return -1;
            }

            for (int i = 0; i < _weaponIds.Length; i++)
            {
                if (_weaponIds[i] == PrototypeUiFlow.SelectedWeapon)
                {
                    return i;
                }
            }

            return _weaponIds.Length > 0 ? 0 : -1;
        }

        /// <summary>
        /// The Arsenal's classic weapons in sortOrder (WP-069). A definition that is not shipped (enabled = false) is not shown
        /// at all - no locked card, no placeholder (user decision, C-00 2026-09-17). Specials have their own tiles.
        /// </summary>
        private static List<WeaponDefinition> RosterForDisplay()
        {
            var list = new List<WeaponDefinition>();
            IReadOnlyList<WeaponDefinition> all = Arsenal.All;
            for (int i = 0; i < all.Count; i++)
            {
                WeaponDefinition d = all[i];
                if (d != null && d.enabled && d.slot == WeaponSlotKind.Classic)
                {
                    list.Add(d);
                }
            }

            list.Sort((x, y) => x.sortOrder != y.sortOrder ? x.sortOrder.CompareTo(y.sortOrder) : string.CompareOrdinal(x.weaponId, y.weaponId));
            return list;
        }

        private void SelectWeapon(ClassicWeaponId id)
        {
            if (!ProfileService.IsWeaponUnlocked(id))
            {
                // E9: a locked row refuses - the row nudges (the words stay readable), Denied plays, nothing changes.
                for (int i = 0; i < _weaponIds.Length; i++)
                {
                    if (_weaponIds[i] == id && _weaponLockWords[i] != null)
                    {
                        UiPresets.DeniedNudge((RectTransform)_weaponButtons[i].transform);
                    }
                }

                UiSound.Play(UiSoundKind.Denied);
                return;
            }

            PrototypeUiFlow.SelectedWeapon = id;
            RefreshWeapons();
            UiEvents.Raise(UiEvent.ChoiceSelected, "Weapon");
        }

        private void RefreshWeapons()
        {
            for (int i = 0; i < _weaponButtons.Length; i++)
            {
                ClassicWeaponId id = _weaponIds[i];
                bool unlocked = _weaponEnabled[i] && ProfileService.IsWeaponUnlocked(id);
                bool selected = unlocked && id == PrototypeUiFlow.SelectedWeapon;

                _weaponButtons[i].SetSelectedChoice(selected);   // the state the pad / tests read; the check glyph draws it
                // WP-105 (SHOULD-8): SelectWeapon refuses this row with Denied - then that is the press's one voice (no Confirm).
                _weaponButtons[i].ConfirmCue = ProfileService.IsWeaponUnlocked(id);
                bool locked = _weaponLockWords[i] != null;
                UiBrushKit.SetRowStatus(_weaponButtons[i], selected ? CheckGlyph : locked ? LockGlyph : null, locked ? _weaponLockWords[i] : null);
                if (_weaponGlyphs[i] != null)
                {
                    _weaponGlyphs[i].color = RowTint(i, unlocked);
                }
            }

            // Right from a mode lands on the chosen weapon: keep that link on the new choice.
            if (_difficultyRows != null && _weaponButtons.Length > 0)
            {
                Selectable right = _weaponButtons[Mathf.Clamp(SelectedIndex(), 0, _weaponButtons.Length - 1)];
                for (int i = 0; i < _difficultyRows.Length; i++)
                {
                    UiInput.LinkHorizontal(_difficultyRows[i], right, right);
                }
            }
        }

        /// <summary>
        /// N19: the lineage tint is identity - a locked weapon keeps it at α 0.55, never a grey swap. R3 m12: a side view
        /// is the render's own colour (white), at the same α 0.55 while locked.
        /// </summary>
        private Color RowTint(int index, bool unlocked)
        {
            if (_weaponSides != null && index < _weaponSides.Length && _weaponSides[index] >= 0)
            {
                return WeaponSideView.Tint(unlocked);
            }

            Color tint = UiTokens.Get(UiGlyphTints.Weapon(_weaponIds[index]));
            tint.a = unlocked ? 1f : UiGlyphTints.LockedAlpha;
            return tint;
        }

        // ================================================================== chrome

        /// <summary>
        /// WP-098B (AP-028 §3.4 footer; KILAVUZ §4.1 C10 / C11): Back is the kit's footer C10 (Menu.Back, the pad's Cancel
        /// glyph beside it; Cancel does the same), START RUN its C11 (Menu.Play + the word, unfilled, as wide as its word; the
        /// Submit glyph beside it); focus on either is the kit's cream brackets. WP-106: no loadout echo between them - the
        /// choices are on the page (the check glyphs), and the words would spend R2 U1's letters twice.
        /// </summary>
        private void BuildFooter()
        {
            RectTransform footer = UiFactory.NewRect("Footer", _page);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, UiBrushTokens.ControlHeight);
            footer.anchoredPosition = Vector2.zero;

            UiIconButton back = UiBrushKit.Back(footer, GoBack);
            _backButton = back.Button;
            RectTransform backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.anchoredPosition = Vector2.zero;

            UiButton start = UiBrushKit.Button(footer, "Start", "selection.start", "START RUN", "Menu.Play", PrototypeUiFlow.StartRun);
            _startButton = start;
            RectTransform startRect = (RectTransform)start.transform;
            startRect.anchorMin = new Vector2(1f, 0.5f);
            startRect.anchorMax = new Vector2(1f, 0.5f);
            startRect.pivot = new Vector2(1f, 0.5f);
            startRect.anchoredPosition = Vector2.zero;

            // WP-022F: the footer's two prompts follow the device (A / ENTER on START RUN, B / ESC on Back). The kit's Back
            // carries its Cancel glyph outside its right edge; START RUN's Submit glyph mirrors it outside its left edge, so
            // the C11 keeps its measured width and stays unfilled (no key chip inside the brush control).
            UiPrompt startPrompt = UiInputGlyphs.Build("Prompt", start.transform, UiPromptAction.Submit, UiColor.TextPrimary, UiColor.SurfaceInset);
            if (startPrompt != null)
            {
                var promptRect = (RectTransform)startPrompt.transform;
                promptRect.anchorMin = new Vector2(0f, 0.5f);
                promptRect.anchorMax = new Vector2(0f, 0.5f);
                promptRect.pivot = new Vector2(1f, 0.5f);
                promptRect.anchoredPosition = new Vector2(-(int)UiSpace.S, 0f);
            }
        }

        private static void GoBack()
        {
            UiSound.Play(UiSoundKind.Back);
            PrototypeUiFlow.GoToMainMenu();
        }

        // ============================================================= side views

        /// <summary>
        /// R3 m12 (WP-098B, user 07.10: "use the images in the folder for the Prepare Run weapons"): the AP-036 v001 side
        /// views, every weapon picture on this page (ten rows, the stage's stand-in, the two Specials). The renders were framed
        /// one by one (331-1292 px per metre), so the intake (UI/AP036/AP036-intake-manifest.json) trimmed each to its alpha
        /// box, resampled it to at most 600 px/m and padded it to a multiple of 4. This table is that manifest's numbers - the
        /// weapon's real length (AP-021 length_m) and its alpha box in the sprite - and the one place a placement reads:
        /// one px-per-metre gives every sprite its size, so the 1.45 m Tomi draws twice the 0.72 m Tetsui, and each weapon's
        /// foot (the box's bottom) stands on one baseline with its stock (the box's right edge, muzzles face left) on one line.
        /// </summary>
        public static class WeaponSideView
        {
            public const string Folder = "UI/AP036/";
            public const string FilePrefix = "AP036_side_";

            public readonly struct Entry
            {
                public readonly string WeaponId;
                public readonly float LengthM;
                public readonly int Width;
                public readonly int Height;
                public readonly RectInt Content;   // the weapon's alpha box in the sprite, px from its bottom-left

                public Entry(string weaponId, float lengthM, int width, int height, int x, int y, int w, int h)
                {
                    WeaponId = weaponId;
                    LengthM = lengthM;
                    Width = width;
                    Height = height;
                    Content = new RectInt(x, y, w, h);
                }

                /// <summary>The weapon's height in metres at the render's aspect.</summary>
                public float HeightM
                {
                    get { return LengthM * Content.height / Content.width; }
                }
            }

            /// <summary>Weapon id, length (m), sprite W × H, alpha box x, y, w, h (AP036-intake-manifest.json).</summary>
            public static readonly Entry[] Table =
            {
                new Entry("ar_1", 1.2000f, 728, 140, 4, 4, 720, 131),     // Hinawa, 0.218 m tall
                new Entry("ar_2", 1.0999f, 668, 216, 4, 4, 660, 205),     // Sanren, 0.342 m
                new Entry("lmg_1", 1.3002f, 788, 236, 4, 4, 780, 227),    // Mukade, 0.378 m
                new Entry("lmg_2", 1.1999f, 728, 212, 4, 4, 720, 204),    // Kurogane, 0.340 m
                new Entry("rl_1", 1.1000f, 668, 196, 4, 4, 660, 186),     // Bohiya, 0.310 m
                new Entry("sg_1", 0.7800f, 476, 132, 4, 4, 468, 123),     // Rappa, 0.205 m
                new Entry("sg_2", 0.9200f, 560, 160, 4, 4, 552, 149),     // Nito, 0.248 m
                new Entry("smg_1", 0.8500f, 520, 212, 4, 4, 510, 203),    // Shippu, 0.338 m
                new Entry("smg_2", 0.7200f, 440, 140, 4, 4, 432, 131),    // Tetsui, 0.218 m
                new Entry("sr_1", 1.4495f, 488, 92, 4, 4, 480, 84),       // Tomi, 0.254 m (source 331 px/m, kept)
                new Entry("sw_arc", 1.0502f, 640, 232, 4, 4, 630, 222),   // Raiju, 0.370 m
                new Entry("sw_gel", 0.9500f, 580, 240, 4, 4, 570, 230),   // Namekuji, 0.383 m
            };

            /// <summary>The longest weapon (m): it fills a slot's width.</summary>
            public static readonly float MaxLengthM = MaxOf(true);

            /// <summary>The tallest weapon (m): it fills a slot's height.</summary>
            public static readonly float MaxHeightM = MaxOf(false);

            public static int IndexOf(string weaponId)
            {
                for (int i = 0; i < Table.Length; i++)
                {
                    if (Table[i].WeaponId == weaponId)
                    {
                        return i;
                    }
                }

                return -1;
            }

            public static Sprite Load(int index)
            {
                return index >= 0 && index < Table.Length ? Resources.Load<Sprite>(Folder + FilePrefix + Table[index].WeaponId) : null;
            }

            /// <summary>The one scale for a slot: the longest weapon fills its width or the tallest its height.</summary>
            public static float PixelsPerMetre(float slotWidth, float slotHeight)
            {
                return Mathf.Max(0f, Mathf.Min(slotWidth / MaxLengthM, slotHeight / MaxHeightM));
            }

            /// <summary>A render's own colour; a locked weapon keeps it at α 0.55 (N19, never a grey swap).</summary>
            public static Color Tint(bool unlocked)
            {
                Color tint = Color.white;
                tint.a = unlocked ? 1f : UiGlyphTints.LockedAlpha;
                return tint;
            }

            /// <summary>
            /// Sizes <paramref name="rect"/> to sprite <paramref name="index"/> at <paramref name="pixelsPerMetre"/> and puts the
            /// weapon's stock (alpha box right) and foot (alpha box bottom) on <paramref name="stockBaseline"/>, given from
            /// <paramref name="anchor"/> in the parent.
            /// </summary>
            public static void Place(RectTransform rect, int index, float pixelsPerMetre, Vector2 anchor, Vector2 stockBaseline)
            {
                Entry e = Table[index];
                float k = e.LengthM * pixelsPerMetre / e.Content.width;   // UI px per sprite px
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = new Vector2(1f, 0f);
                rect.sizeDelta = new Vector2(e.Width * k, e.Height * k);
                rect.anchoredPosition = new Vector2(stockBaseline.x + (e.Width - e.Content.xMax) * k, stockBaseline.y - e.Content.y * k);
            }

            private static float MaxOf(bool length)
            {
                float max = 0f;
                for (int i = 0; i < Table.Length; i++)
                {
                    max = Mathf.Max(max, length ? Table[i].LengthM : Table[i].HeightM);
                }

                return max;
            }
        }
    }
}
