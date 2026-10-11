using System;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Skin and Weapon Selection. Shown before every run.
    ///
    /// WP-068 slice 7 · E9 v3, reworked for the M4-R1 ten-weapon roster. Three columns on the page grid:
    ///   * CHARACTER SKIN: three 120 px cards (the AP-006 portrait, the palette strip, the name, "Cosmetic only").
    ///     A skin is a taste choice with no consequence, so its cards carry no numbers.
    ///   * The stage, an Elev1 card: the weapon under focus (or the chosen one when focus is elsewhere). It shows the
    ///     identity-tinted glyph (N19), the pack's name / class / fire mode / role / description / flavour, and the
    ///     weapon's own four-stat StatStrip, each stat with its number and a bar compared with the classic roster.
    ///     Below that come the CORE branches and the state line (carried for the run, or what unlocks it). The v2 screen
    ///     printed all of this on every card; ten cards cannot hold it (GD P14: show the numbers the rule uses, invent none).
    ///   * CLASSIC WEAPON: one 48 px row per roster weapon (glyph, name, state chip), then the two Specials as
    ///     information tiles. They are found on the field and never chosen here.
    /// A locked row can be focused so the stage can say what unlocks it. Pressing it nudges its chip, plays Denied and
    /// changes nothing.
    ///
    /// WP-098B (AP-028 §3.4; brush kit KILAVUZ §3 "Seçim"): the window is the kit's - the world plate under the Window
    /// scrim (<see cref="UiBrushPane"/>, linear 0.95; the v2 backdrop's veils, petals and lozenge are gone), the kit's C5
    /// title without a glyph, the skins and the weapons as C18 list rows (unframed; the choice is the check glyph, a lock is
    /// the lock glyph and its level, never a chip), Back (C10, Menu.Back + the pad's Cancel) and START RUN (C11, Menu.Play).
    /// The column headings are words without the vermilion tick. Trace budget: title brush 1; vermilion 0. The stage card,
    /// the Specials' tiles and the AP-036 side views are unchanged.
    ///
    /// SESSION MEMORY (acceptance criteria 23 and 24). The current choice comes from
    /// <c>PrototypeUiFlow.SelectedSkin</c> and <c>PrototypeUiFlow.SelectedWeapon</c>, statics reset by
    /// RuntimeInitializeOnLoadMethod. A second run in the same session opens on the previous choice; a restarted
    /// application opens on the defaults. Neither is written to the profile save.
    /// </summary>
    public sealed class SelectionScreen : UiScreen
    {
        private const float SkinCardHeight = 120f;          // E9: portrait 88 + 2 × 16
        private const float SwatchWidth = 100f;
        private const float BadgeWidth = 160f;              // a row's trailing reserve: the status glyph, its word and the gap
        private const float FooterHeight = 88f;             // the kit's 64 high C10 / C11 + XL

        private const float WeaponRowHeight = 48f;
        private const float WeaponRowGap = 4f;
        private const float RowGlyph = 40f;
        private const float SpecialTileHeight = 48f;
        private const float StageGlyph = 128f;
        private const float StageBandMin = 48f;             // R3 m12: the side view's last yield before the description is clipped
        private const float RowSideMax = 128f;              // R3 m12: the rows' side-view slot (the longest weapon's width), at most
        private const float StatBarHeight = 8f;
        private const float CoreGlyphSize = 26f;            // AD v003 MUST-4: ≥ 24 px at 1080p

        // The three columns of the 12-column grid (560 / 24 / 560 / 24 / 560 at 1728).
        private const float ColumnShare = 560f / 1728f;
        private const float GutterShare = 24f / 1728f;

        private static readonly Vector3 GlyphEdge = new Vector3(0.25f, 0f, 0f);   // the HudInk edge the HUD glyphs wear

        /// <summary>The window's kit pane (its root under the page grid; its C1 on the screen root).</summary>
        public const string PaneName = "SelectionPane";

        /// <summary>C18 status glyphs (G1 atlas): the choice is the check, a weapon not yet earned the lock.</summary>
        public const string CheckGlyph = "Selection.Selected";

        public const string LockGlyph = "State.Locked";

        private UiBrushPane _pane;
        private RectTransform _page;                        // the pane's clipped content: the columns and the footer
        private UiButton[] _skinButtons;
        private Image[] _skinPreviews;

        private UiButton[] _weaponButtons;
        private string[] _weaponLockWords;                  // a locked row's trailing word (null: unlocked), built once
        private Image[] _weaponGlyphs;
        private int[] _weaponSides;                         // R3 m12: WeaponSideView.Table index per row, -1 = the G1 glyph stand-in
        private ClassicWeaponId[] _weaponIds;
        private WeaponDefinition[] _weaponDefs;
        private bool[] _weaponEnabled;

        private UiButton _backButton;
        private UiButton _startButton;
        private TMP_Text _startCaption;

        // The stage
        private CanvasGroup _stageText;
        private Image _stageGlyph;
        private TMP_Text _stageName;
        private TMP_Text _stageClass;
        private TMP_Text _stageRole;
        private TMP_Text _stageDescription;
        private TMP_Text _stageFlavour;
        private readonly TMP_Text[] _statLabels = new TMP_Text[4];
        private readonly TMP_Text[] _statValues = new TMP_Text[4];
        private readonly UiBar[] _statBars = new UiBar[4];
        private readonly WeaponStat[] _stats = new WeaponStat[4];
        private readonly Image[] _coreGlyphs = new Image[3];
        private readonly TMP_Text[] _coreNames = new TMP_Text[3];
        private TMP_Text _stateLine;
        private int _stageIndex = -1;
        private GameObject _lastFocus;
        private RectTransform _stageContent;
        private RectTransform _stageCard;
        private static readonly Vector3[] s_stageCorners = new Vector3[4];
        private RectTransform _stageTop;
        private RectTransform _coreRow;
        private RectTransform _stateRow;
        private TMP_Text _compareCaption;
        private readonly RectTransform[] _statCells = new RectTransform[4];
        private bool _stageLayoutPending;
        private Vector2 _stageSize;
        private RectTransform _weaponsColumn;
        private RectTransform _specialsHead;
        private RectTransform _specialsRow;
        private readonly TMP_Text[] _specialNames = new TMP_Text[2];
        private readonly TMP_Text[] _specialModes = new TMP_Text[2];
        private readonly float[] _specialTextLeft = new float[2];
        private readonly Image[] _specialIcons = new Image[2];
        private readonly int[] _specialSides = { -1, -1 };
        private float _specialModeSize;
        private float _weaponsTop;
        private float _rowHeight;
        private float _tileHeight;
        private float _specialsTitleBand;
        private bool _weaponsLayoutPending;
        private Vector2 _weaponsSize;

        /// <summary>The preview images the skin cards show, index = PlayerSkin (tests read the sprite).</summary>
        public Image[] SkinPreviews
        {
            get { return _skinPreviews; }
        }

        /// <summary>The roster row the stage shows (tests, captures).</summary>
        public int StageIndex
        {
            get { return _stageIndex; }
        }

        /// <summary>The stage's texts, top to bottom (tests: name, class line, role, description, state line).</summary>
        public TMP_Text[] StageTexts
        {
            get { return new[] { _stageName, _stageClass, _stageRole, _stageDescription, _stateLine }; }
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

            // WP-098B (KILAVUZ §3 "Seçim"): the world plate, then the kit's Window pane over the page grid - its C1 is the
            // scrim (no hand-set alpha), its clipped content holds every column.
            ResultsScreen.BuildWorldPlate(ScreenRect);
            _pane = UiBrushKit.Pane(ScreenRect, Content, PaneName, UiBrushScrim.Window, UiBrushWash.None);
            _page = _pane.Content;

            // WP-081 U1b (R2 U1, fewer letters): the title alone - no eyebrow, no subtitle. What the subtitle said lives on
            // the screen itself: the skins' swatches only recolour, the weapon row is the run's one choice (its lock line).
            // WP-098B: the kit's C5 title without a glyph (AP-028 §3.4) - its rule and its title brush, inked in on Open.
            UiBrushTitle title = UiBrushKit.Title(_page, "selection.title", "PREPARE YOUR RUN");

            // The kit's title band is taller than the v2 H1 + rule (Display 56 + the rule gap + the stroke): M under it keeps the
            // columns within ~15 px of their v2 room (the Mac weapon column fits its ten rows and the Specials).
            float top = title.Height + (int)UiSpace.M;
            float stageFrom = ColumnShare + GutterShare;
            float weaponsFrom = stageFrom + ColumnShare + GutterShare;

            RectTransform skins = PageColumn("Skins", 0f, ColumnShare, top, FooterHeight);
            RectTransform stage = PageColumn("Stage", stageFrom, stageFrom + ColumnShare, top, FooterHeight);
            RectTransform weapons = PageColumn("Weapons", weaponsFrom, 1f, top, FooterHeight);
            BuildSkinColumn(skins);
            BuildWeaponColumn(weapons);
            BuildStage(stage);
            BuildFooter();

            RefreshSkins();
            RefreshWeapons();
            ShowOnStage(SelectedIndex());
            LinkNavigation();

            // E9 choreography: the three columns land left to right, 60 ms apart, once the curtain has revealed the
            // screen (Decor: Reduced Motion shows them at rest).
            gameObject.AddComponent<UiStaggerIn>().Configure(new[]
            {
                UiFactory.Group(skins.gameObject),
                UiFactory.Group(stage.gameObject),
                UiFactory.Group(weapons.gameObject)
            });

            // The window opens with the screen (input live at once); the chosen skin takes the focus in Start.
            _pane.Open(null);
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
        private static RectTransform ColumnHeading(string words, RectTransform column)
        {
            TMP_Text heading = UiFactory.Label("Heading", column, words, UiType.H2, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            heading.enableWordWrapping = false;
            return heading.rectTransform;
        }

        private void Update()
        {
            if (UiInput.CancelPressed())
            {
                GoBack();
                return;
            }

            UiInput.ReclaimFocus(_skinButtons != null && _skinButtons.Length > 0
                ? _skinButtons[(int)PrototypeUiFlow.SelectedSkin]
                : _startButton);

            // The stage follows focus (a pointer hover moves focus too). A change of the selected object is the only
            // trigger: the stage is rebuilt on a change, never per frame.
            if (_stageContent != null && (_stageLayoutPending || _stageContent.rect.size != _stageSize))
            {
                LayoutStage();
            }

            if (_weaponsColumn != null && (_weaponsLayoutPending || _weaponsColumn.rect.size != _weaponsSize))
            {
                LayoutWeapons();
            }

            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focus != _lastFocus)
            {
                _lastFocus = focus;
                int index = IndexOf(focus);
                ShowOnStage(index >= 0 ? index : SelectedIndex());
            }
        }

        /// <summary>
        /// Gamepad / keyboard map (E9): skins ↕ and weapons ↕ are two vertical chains; Left / Right hops between the
        /// columns; Down from the last card lands on the footer. Locked weapon rows are in the chain (the stage explains
        /// them); pressing one is refused.
        /// </summary>
        private void LinkNavigation()
        {
            var skins = new List<Selectable>(_skinButtons);
            var weapons = new List<Selectable>(_weaponButtons);
            skins.Add(_backButton);
            weapons.Add(_startButton);
            UiInput.ChainVertical(skins.ToArray());
            UiInput.ChainVertical(weapons.ToArray());

            int skinCards = skins.Count - 1;
            int weaponCards = weapons.Count - 1;
            int selected = Mathf.Max(0, SelectedIndex());
            for (int i = 0; i < skinCards; i++)
            {
                // → from any skin lands on the chosen weapon: the decision the player came back to adjust.
                Selectable right = weaponCards > 0 ? weapons[Mathf.Min(selected, weaponCards - 1)] : _startButton;
                UiInput.LinkHorizontal(skins[i], right, right);
            }

            for (int i = 0; i < weaponCards; i++)
            {
                // ← from a weapon row lands on the skin card beside it (ten rows over three cards).
                Selectable left = skinCards > 0 ? skins[Mathf.Min(i * skinCards / Mathf.Max(1, weaponCards), skinCards - 1)] : _backButton;
                UiInput.LinkHorizontal(weapons[i], left, left);
            }

            UiInput.LinkHorizontal(_backButton, _startButton, _startButton);
            UiInput.LinkHorizontal(_startButton, _backButton, _backButton);
        }

        // =================================================================== skins

        private void BuildSkinColumn(RectTransform column)
        {
            var stack = new UiStack(column);

            // WP-081 U1b: the heading, then the cards - the caption ("all three from the start, none changes a value") and each
            // card's "Cosmetic only" left with R2 U1; the swatch and the preview show what a skin is.
            stack.Add(ColumnHeading(UiStrings.Get("selection.skin.title", "CHARACTER SKIN"), column), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.L);

            int count = PlayerSkins.Count;
            _skinButtons = new UiButton[count];
            _skinPreviews = new Image[count];

            for (int i = 0; i < count; i++)
            {
                var skin = (PlayerSkin)i;
                PlayerSkins.Palette palette = PlayerSkins.Get(skin);

                // WP-098B (KILAVUZ §4.1 C18, AP-028 §3.4): an unframed kit list row - the portrait, the palette strip, the name
                // (MenuBody) and the check glyph (32) on the choice; no card, no SELECTED badge.
                UiButton button = UiBrushKit.ListRow(column, "Skin." + skin, palette.DisplayName, null, () => SelectSkin(skin), false);

                stack.Add((RectTransform)button.transform, SkinCardHeight);
                stack.Gap(UiSpace.L);

                _skinPreviews[i] = BuildPreview(button, i);
                BuildSwatch(button, palette);

                // AD G1 MUST-3: the name top-aligned and growing downward, from the strip to the row's status glyph (the kit's
                // SetRowStatus keeps that right edge measured; only the left and the top are set here).
                float textLeft = PreviewSlotWidth + SwatchWidthNow + (int)UiSpace.XL + (int)UiSpace.L;
                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(textLeft, (int)UiSpace.M);
                labelRect.offsetMax = new Vector2(labelRect.offsetMax.x, -(int)UiSpace.L);
                button.LabelText.enableWordWrapping = true;
                button.LabelText.alignment = TextAlignmentOptions.TopLeft;

                _skinButtons[i] = button;

                // Focus opens on the skin that is already selected - on a second run of
                // the session that is the remembered one, not the first card.
                if (skin == PrototypeUiFlow.SelectedSkin)
                {
                    SetInitialFocus(button);
                }
            }
        }

        /// <summary>Width the character preview takes at the card's leading edge (0 without art).</summary>
        private static float PreviewSlotWidth
        {
            get { return UiArt.SkinPreview(0) != null ? SkinCardHeight - (int)UiSpace.L * 2 + (int)UiSpace.L : 0f; }
        }

        private static float SwatchWidthNow
        {
            get { return PreviewSlotWidth > 0f ? 24f : SwatchWidth; }
        }

        /// <summary>
        /// The AP-006 front-view render of the skin (WP-022): the real character, not a
        /// colour strip, is what a skin choice is about. Sits inside a recessed well so a
        /// pale skin still has an edge on the card. Absent art leaves the slot at zero width.
        /// </summary>
        private static Image BuildPreview(UiButton button, int skinIndex)
        {
            Sprite sprite = UiArt.SkinPreview(skinIndex);
            if (sprite == null)
            {
                return null;
            }

            float size = SkinCardHeight - (int)UiSpace.L * 2;
            Image well = UiFactory.NewImage("PreviewWell", button.transform, UiColor.SurfaceInset, UiSprites.Chip);
            var wellRect = (RectTransform)well.transform;
            wellRect.anchorMin = new Vector2(0f, 0.5f);
            wellRect.anchorMax = new Vector2(0f, 0.5f);
            wellRect.pivot = new Vector2(0f, 0.5f);
            wellRect.sizeDelta = new Vector2(size, size);
            wellRect.anchoredPosition = new Vector2((int)UiSpace.L, 0f);

            Image preview = UiFactory.NewImage("Preview", wellRect, UiColor.TextPrimary, sprite);
            UiFactory.Fill((RectTransform)preview.transform, (int)UiSpace.XS);
            preview.preserveAspect = true;
            return preview;
        }

        /// <summary>Three stacked bands of the skin's own palette. No text needed.</summary>
        private static void BuildSwatch(UiButton button, PlayerSkins.Palette palette)
        {
            RectTransform swatch = UiFactory.NewRect("Swatch", button.transform);
            swatch.anchorMin = new Vector2(0f, 0f);
            swatch.anchorMax = new Vector2(0f, 1f);
            swatch.pivot = new Vector2(0f, 0.5f);
            swatch.sizeDelta = new Vector2(SwatchWidthNow, -((int)UiSpace.L * 2));
            swatch.anchoredPosition = new Vector2((int)UiSpace.L + PreviewSlotWidth, 0f);

            Image frame = UiFactory.NewImage("Frame", swatch, UiColor.BorderStrong, UiSprites.Chip);
            UiFactory.Fill((RectTransform)frame.transform);

            AddBand(swatch, palette.Primary, 0f, 0.5f);
            AddBand(swatch, palette.Secondary, 0.5f, 0.82f);
            AddBand(swatch, palette.Accent, 0.82f, 1f);
        }

        /// <summary>
        /// The swatch is the ONE place in this module that draws a colour that is not a
        /// UI token: it is showing the player character's palette, which is owned by
        /// <c>PlayerSkins</c>. Painting it from a UI token would show the wrong colour.
        /// It carries no text, so no contrast pair depends on it.
        /// </summary>
        private static void AddBand(RectTransform parent, Color color, float from, float to)
        {
            var go = new GameObject("Band", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, from);
            rect.anchorMax = new Vector2(1f, to);
            rect.offsetMin = new Vector2(UiTokens.StrokeControl, UiTokens.StrokeControl);
            rect.offsetMax = new Vector2(-UiTokens.StrokeControl, -UiTokens.StrokeControl);

            var image = go.GetComponent<Image>();
            image.sprite = UiSprites.Solid;
            image.color = color;
            image.raycastTarget = false;
        }

        private void SelectSkin(PlayerSkin skin)
        {
            PrototypeUiFlow.SelectedSkin = skin;
            RefreshSkins();
            RefreshStartCaption();
            UiEvents.Raise(UiEvent.ChoiceSelected, "Skin");
        }

        private void RefreshSkins()
        {
            for (int i = 0; i < _skinButtons.Length; i++)
            {
                bool selected = (PlayerSkin)i == PrototypeUiFlow.SelectedSkin;
                _skinButtons[i].SetSelectedChoice(selected);   // the state the pad / tests read; the check glyph draws it
                UiBrushKit.SetRowStatus(_skinButtons[i], selected ? CheckGlyph : null, null);
            }
        }

        // ================================================================= weapons

        /// <summary>
        /// One 48 px row per Arsenal classic weapon (WP-069 §1098: Selection reads the Arsenal, enabled only, sortOrder): the glyph in its
        /// lineage tint (N19; α 0.55 while locked, never a grey swap), the pack name, and a chip - SELECTED on the choice,
        /// LOCKED · L{n} on a weapon the profile has not earned. Then the two Specials as information tiles.
        /// R3 m12: the glyph is the weapon's AP-036 side view (untinted, α 0.55 while locked), every row at one scale.
        /// WP-098B (KILAVUZ §4.1 C18): each row is the kit's unframed compact list row - the name at the dense list's 20 px,
        /// the check glyph on the choice, the lock glyph and its level ("L3", "PATH · L11") on a weapon not yet earned; no
        /// SELECTED / LOCKED chip.
        /// </summary>
        private void BuildWeaponColumn(RectTransform column)
        {
            var stack = new UiStack(column);
            stack.Add(ColumnHeading(UiStrings.Get("selection.weapon.title", "CLASSIC WEAPON"), column), UiFactory.LineHeight(UiType.H2));
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
                // G1 cell by runtime id (N19 Weapon.<ClassicWeaponId>; a v3 id without one borrows its class sibling's),
                // then the definition's own HUD icon key.
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
        /// The two Specials (DEC-094): found on the field, never chosen here - information tiles, not controls. The glyph
        /// wears its Special's identity tint (Arc / Gel); the name and fire mode come from the pack.
        /// </summary>
        private void BuildSpecialTiles(RectTransform column, ref UiStack stack)
        {
            stack.Gap(UiSpace.M);
            float band = UiFactory.LineHeight(UiType.Label);
            RectTransform head = UiFactory.NewRect("SpecialsHead", column);
            _specialsHead = head;
            _specialsTitleBand = band;
            stack.Add(head, band);
            // WP-081 U1b: the title alone - the note ("found on the field, never chosen here") left with R2 U1; the tiles
            // carry no button, so nothing on them reads as a choice.
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

                // R3 m12: the AP-036 side view at the rows' scale (LayoutWeapons places it); the tinted G1 glyph without it.
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
                RectTransform nameRect = UiFactory.Fill((RectTransform)name.transform);
                nameRect.offsetMin = new Vector2(left, 0f);
                nameRect.offsetMax = new Vector2(-(int)UiSpace.M, 0f);
                name.enableWordWrapping = false;
                TMP_Text mode = UiFactory.Label("Mode", tile, UiStrings.Get("weapon." + ids[i] + ".firemode", string.Empty), UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleRight);
                RectTransform modeRect = UiFactory.Fill((RectTransform)mode.transform);
                modeRect.offsetMax = new Vector2(-(int)UiSpace.M, 0f);
                mode.enableWordWrapping = false;
                mode.overflowMode = TextOverflowModes.Ellipsis;
                // U1b MUST-2 (AD + ui-ux, 06.10): LayoutSpecialTiles keeps the name whole and the mode >= S beside it.
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
            _stageCard = (RectTransform)card.transform;   // LayoutStage fits it to its content (U1b MUST-1)
            RectTransform content = card.Content;
            _stageContent = content;
            _stageText = UiFactory.Group(content.gameObject);

            // Every block is built here and placed by LayoutStage from the texts it holds (the stage is measured, not
            // stacked on fixed bands: TextScale 1.25 on the Mac canvas at UI scale 1.2 leaves ~560 px for it).
            _stageTop = UiFactory.NewRect("Identity", content);
            _stageGlyph = UiFactory.NewImage("StageGlyph", _stageTop, UiColor.TextPrimary, UiArt.WeaponIcon(ClassicWeaponId.AssaultRifle));
            _stageGlyph.preserveAspect = true;
            if (UiArt.IsGlyph(_stageGlyph.sprite))
            {
                UiArt.ApplyGlyph(_stageGlyph, GlyphEdge);
            }

            _stageName = TopLabel("Name", _stageTop, UiType.H1, UiColor.TextPrimary, 0f, 0f, 1f);
            _stageClass = TopLabel("Class", _stageTop, UiType.Label, UiColor.TextSecondary, 0f, 0f, 1f);
            _stageRole = TopLabel("Role", content, UiType.Body, UiColor.TextPrimary, 0f, 0f, 1f);
            _stageName.enableWordWrapping = false;
            _stageClass.enableWordWrapping = false;
            _stageName.overflowMode = TextOverflowModes.Ellipsis;
            _stageClass.overflowMode = TextOverflowModes.Ellipsis;   // "LIGHT MACHINE GUN · AUTO" on the 1408 canvas
            _stageRole.overflowMode = TextOverflowModes.Ellipsis;

            _stageDescription = UiFactory.Label("Description", content, string.Empty, UiType.Body, UiColor.TextSecondary);
            _stageDescription.overflowMode = TextOverflowModes.Ellipsis;
            _stageFlavour = UiFactory.Label("Flavour", content, string.Empty, UiType.Caption, UiColor.TextMuted);
            _stageFlavour.fontStyle = FontStyles.Italic;
            _stageFlavour.overflowMode = TextOverflowModes.Ellipsis;

            // The StatStrip: two rows of two, each a label, a number and a roster bar.
            for (int s = 0; s < 4; s++)
            {
                RectTransform cell = UiFactory.NewRect("Stat" + s, content);
                _statCells[s] = cell;
                _statLabels[s] = TopLabel("Label", cell, UiType.Caption, UiColor.TextSecondary, 0f, 0f, 1f);
                _statValues[s] = TopLabel("Value", cell, UiType.Label, UiColor.TextPrimary, 0f, 0f, 1f);
                _statLabels[s].enableWordWrapping = false;
                _statValues[s].enableWordWrapping = false;
                _statLabels[s].overflowMode = TextOverflowModes.Ellipsis;

                UiBar bar = UiFactory.Bar("Bar", cell, UiColor.TextPrimary, StatBarHeight);
                RectTransform barRect = (RectTransform)bar.transform;
                barRect.anchorMin = new Vector2(0f, 0f);
                barRect.anchorMax = new Vector2(1f, 0f);
                barRect.pivot = new Vector2(0.5f, 0f);
                barRect.sizeDelta = new Vector2(0f, StatBarHeight);
                barRect.anchoredPosition = Vector2.zero;
                _statBars[s] = bar;
            }

            // WP-081 U1b: no caption under the bars ("compare with the other classic weapons, longer is better") - the
            // roster bars read on their own; the label stays for LayoutStage, empty, so its band closes.
            _compareCaption = UiFactory.Label("CompareCaption", content, string.Empty, UiType.Caption, UiColor.TextMuted);

            // CORE: the weapon's three round-end branches (WP-034 shop gate).
            _coreRow = UiFactory.NewRect("CoreBranches", content);
            TMP_Text coreLabel = UiFactory.Label("Label", _coreRow, UiStrings.Get("selection.core", "CORE"), UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform coreLabelRect = (RectTransform)coreLabel.transform;
            coreLabelRect.anchorMin = new Vector2(0f, 0f);
            coreLabelRect.anchorMax = new Vector2(0f, 1f);
            coreLabelRect.pivot = new Vector2(0f, 0.5f);
            coreLabelRect.sizeDelta = new Vector2(CoreLabelMinWidth, 0f);
            coreLabelRect.anchoredPosition = Vector2.zero;
            for (int i = 0; i < 3; i++)
            {
                RectTransform slot = UiFactory.NewRect("Core" + i, _coreRow);
                slot.anchorMin = new Vector2(0f, 0f);
                slot.anchorMax = new Vector2(0f, 1f);
                slot.pivot = new Vector2(0f, 0.5f);
                Image glyph = UiFactory.NewImage("Glyph", slot, UiColor.TextSecondary, UiSprites.Disc);
                glyph.preserveAspect = true;
                Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(CoreGlyphSize, CoreGlyphSize));
                _coreGlyphs[i] = glyph;
                TMP_Text name = UiFactory.Label("Name", slot, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleLeft);
                RectTransform nameRect = UiFactory.Fill((RectTransform)name.transform);
                nameRect.offsetMin = new Vector2(CoreGlyphSize + (int)UiSpace.XS, 0f);
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                _coreNames[i] = name;
            }

            // The state line: what carrying it means, or what unlocks it (+ the XP mini-bar).
            _stateRow = UiFactory.NewRect("State", content);
            _stateLine = UiFactory.Label("Line", _stateRow, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            _stateLine.richText = true;   // the PATH line marks each met part in cream (built on a focus change)
            _stateLine.overflowMode = TextOverflowModes.Ellipsis;
            UiFactory.Fill((RectTransform)_stateLine.transform);   // the full width: AD R1-B SHOULD-3 retired the unlabeled XP track
        }

        // "CORE" and its gap; the word itself is measured (AD d109 MUST-2: the Turkish word was cut at a fixed 52).
        private const float CoreLabelMinWidth = 52f;
        private const float StageGlyphCompact = 96f;

        /// <summary>U1b MUST-1: the stage card's height - its content, at least down to the skins' last card, at most the column.</summary>
        private void FitStageCard(RectTransform stageColumn, float contentHeight)
        {
            float floor = 0f;
            if (_skinButtons != null && _skinButtons.Length > 0 && _skinButtons[_skinButtons.Length - 1] != null)
            {
                ((RectTransform)_skinButtons[_skinButtons.Length - 1].transform).GetWorldCorners(s_stageCorners);
                floor = stageColumn.rect.yMax - stageColumn.InverseTransformPoint(s_stageCorners[0]).y;
            }

            float height = Mathf.Min(stageColumn.rect.height, Mathf.Max(contentHeight, floor));
            _stageCard.anchorMin = new Vector2(0f, 1f);
            _stageCard.anchorMax = new Vector2(1f, 1f);
            _stageCard.pivot = new Vector2(0.5f, 1f);
            _stageCard.sizeDelta = new Vector2(0f, height);
            _stageCard.anchoredPosition = Vector2.zero;
            _stageSize = _stageContent.rect.size;   // the card's own resize is not a new layout trigger
        }

        /// <summary>
        /// Places the stage's blocks from what they hold at the card's real size. Heights come from the texts (the
        /// description's measured lines, up to five). A stat whose label and value do not fit on one line puts the value
        /// under its label. When the card is short (Mac canvas at UI scale 1.2 × TextScale 1.25), the gaps close first,
        /// then the flavour line and the bar caption drop, then the glyph steps down to 96 and the description is clipped
        /// last. Run on a focus change and when the card's size changes, never per frame otherwise.
        /// </summary>
        private void LayoutStage()
        {
            if (_stageContent == null)
            {
                return;
            }

            Rect area = _stageContent.rect;
            _stageSize = area.size;
            RectTransform stageColumn = (RectTransform)_stageCard.parent;
            if (area.height < 1f || area.width < 1f || stageColumn.rect.height < 1f)
            {
                _stageLayoutPending = true;   // the canvas has not laid out yet: next Update
                return;
            }

            _stageLayoutPending = false;
            float w = area.width;
            // U1b MUST-1 (AD + ui-ux, 06.10): the card ends under its content, so the room is the column's, less the card's
            // own top and bottom padding (the card - content difference holds at any card height).
            float inset = _stageCard.rect.height - area.height;
            float h = stageColumn.rect.height - inset;
            // Line heights measured from the texts themselves (their font size already carries the TextScale; the
            // ScaledBand metric would count it twice and plan a Mac stage ~25 % taller than it renders - AD R1-B re-check).
            float bodyLine = LineOf(_stageDescription);
            float captionLine = LineOf(_compareCaption);
            float labelLine = LineOf(_stageClass);
            float nameBand = LineOf(_stageName);
            float statLine = Mathf.Max(LineOf(_statLabels[0]), LineOf(_statValues[0]));

            // The StatStrip: two by two while every label and value share a line; one column when any cannot (AD R1-B
            // MUST-2a: a value never wraps under its label).
            float cellW = Mathf.Floor((w - (int)UiSpace.XL) * 0.5f);
            bool oneColumn = false;
            for (int s = 0; s < 4; s++)
            {
                float lw = _statLabels[s].GetPreferredValues(_statLabels[s].text).x;
                float vw = _statValues[s].GetPreferredValues(_statValues[s].text).x;
                oneColumn |= lw + vw + (int)UiSpace.S > cellW;
            }

            float cellH = statLine + (int)UiSpace.XS + StatBarHeight;
            float rowGap = oneColumn ? (int)UiSpace.S : (int)UiSpace.M;
            float statsH = oneColumn ? cellH * 4f + rowGap * 3f : cellH * 2f + rowGap;
            float coreH = Mathf.Max(CoreGlyphSize + 4f, captionLine);
            float stateH = captionLine;
            float compareH = string.IsNullOrEmpty(_compareCaption.text) ? 0f : Mathf.Ceil(_compareCaption.GetPreferredValues(_compareCaption.text, w, 0f).y);
            float flavourH = string.IsNullOrEmpty(_stageFlavour.text) ? 0f : captionLine;
            float descH = string.IsNullOrEmpty(_stageDescription.text) ? 0f
                : Mathf.Min(bodyLine * 5f, Mathf.Ceil(_stageDescription.GetPreferredValues(_stageDescription.text, w, 0f).y));

            // The class line ("LIGHT MACHINE GUN · AUTO") must hold one line beside the glyph: a narrow card takes the
            // 96 glyph from the start (T3: nothing on the stage is cut).
            // R3 m12 (WP-098B): with its AP-036 side view the weapon is a band over the card's width instead - one scale for
            // every weapon (the longest fills the width, the tallest the band), the name, class and role under it.
            int stageSide = _weaponSides != null && _stageIndex >= 0 && _stageIndex < _weaponSides.Length ? _weaponSides[_stageIndex] : -1;
            bool band = stageSide >= 0;
            float bandH = 0f;
            float stageScale = 0f;
            float classW = _stageClass.GetPreferredValues(_stageClass.text).x;
            float glyph = band || classW <= w - StageGlyph - (int)UiSpace.L ? StageGlyph : StageGlyphCompact;
            float gap = (int)UiSpace.L;
            bool showFlavour = flavourH > 0f;
            bool showCompare = compareH > 0f;
            bool roleBeside = true;
            bool classBeside = true;
            float roleBelowH = 0f;
            float topH = glyph;
            float BelowH()
            {
                return (classBeside ? 0f : (int)UiSpace.S + labelLine) + (roleBelowH > 0f ? (int)UiSpace.S + roleBelowH : 0f);
            }

            void Identity()
            {
                // The class and the role sit beside the glyph when each holds one line there; otherwise they take the
                // card's width under the identity block (AD R1-B MUST-2a: no wrap, no cut beside the glyph). Under a side-view
                // band the "beside" column is the card's full width.
                if (band)
                {
                    stageScale = WeaponSideView.PixelsPerMetre(w, glyph);
                    bandH = Mathf.Ceil(WeaponSideView.MaxHeightM * stageScale);
                }

                float textW = band ? w : w - glyph - (int)UiSpace.L;
                classBeside = classW <= textW;
                float roleW = string.IsNullOrEmpty(_stageRole.text) ? 0f : _stageRole.GetPreferredValues(_stageRole.text).x;
                roleBeside = roleW <= textW;
                roleBelowH = roleBeside || roleW <= 0f ? 0f : Mathf.Min(bodyLine * 2f, Mathf.Ceil(_stageRole.GetPreferredValues(_stageRole.text, w, 0f).y));
                float textH = nameBand + (classBeside ? labelLine : 0f) + (roleBeside && roleW > 0f ? (int)UiSpace.S + bodyLine : 0f);
                topH = band ? bandH + (int)UiSpace.S + textH : Mathf.Max(glyph, textH);
            }

            float Total()
            {
                return topH + BelowH() + gap + descH + (showFlavour ? (int)UiSpace.XS + flavourH : 0f)
                       + gap + statsH + (showCompare ? (int)UiSpace.S + compareH : 0f) + gap + coreH + gap + stateH;   // U1b MUST-1: the state line keeps its place when empty
            }

            // Yield order when short (AD R1-B re-check): the flavour line, the bar caption, the gaps, the glyph (96; a side-view
            // band then down to 48), and only then the description - never under three lines, so CORE's top stays >= the last bar + 12.
            Identity();
            if (Total() > h) { showFlavour = false; }
            if (Total() > h) { showCompare = false; }
            if (Total() > h) { gap = Mathf.Max((int)UiSpace.M, gap - (Total() - h) / 5f); }
            if (Total() > h) { glyph = StageGlyphCompact; Identity(); }
            if (Total() > h && band) { glyph = Mathf.Max(StageBandMin, glyph - (Total() - h)); Identity(); }
            if (Total() > h) { descH = Mathf.Max(Mathf.Min(descH, bodyLine * 3f), descH - (Total() - h)); }

            // Top group, from the top.
            float y = 0f;
            Dock(_stageTop, y, topH);
            RectTransform glyphRect = (RectTransform)_stageGlyph.transform;
            float textLeft = glyph + (int)UiSpace.L;
            float textTop = 0f;
            if (band)
            {
                // The stock on the card's right edge, the weapon's foot on the band's floor (WeaponSideView.Place).
                WeaponSideView.Place(glyphRect, stageSide, stageScale, new Vector2(0f, 1f), new Vector2(w, -bandH));
                textLeft = 0f;
                textTop = bandH + (int)UiSpace.S;
            }
            else
            {
                glyphRect.anchorMin = new Vector2(0f, 1f);
                glyphRect.anchorMax = new Vector2(0f, 1f);
                glyphRect.pivot = new Vector2(0f, 1f);
                glyphRect.sizeDelta = new Vector2(glyph, glyph);
                glyphRect.anchoredPosition = Vector2.zero;
            }

            SetTop(_stageName, textLeft, textTop, nameBand);
            y += topH;
            if (classBeside)
            {
                SetTop(_stageClass, textLeft, textTop + nameBand, labelLine);
            }
            else
            {
                y += (int)UiSpace.S;
                SetTop(_stageClass, 0f, y, labelLine);
                y += labelLine;
            }

            if (roleBeside)
            {
                SetTop(_stageRole, textLeft, textTop + nameBand + (classBeside ? labelLine : 0f) + (int)UiSpace.S, bodyLine);
                _stageRole.enableWordWrapping = false;
            }
            else
            {
                y += (int)UiSpace.S;
                SetTop(_stageRole, 0f, y, roleBelowH);
                _stageRole.enableWordWrapping = true;
                y += roleBelowH;
            }

            y += gap;
            Dock((RectTransform)_stageDescription.transform, y, descH);
            y += descH;
            _stageFlavour.gameObject.SetActive(showFlavour);
            if (showFlavour)
            {
                y += (int)UiSpace.XS;
                Dock((RectTransform)_stageFlavour.transform, y, flavourH);
                y += flavourH;
            }

            y += gap;
            for (int s = 0; s < 4; s++)
            {
                RectTransform cell = _statCells[s];
                cell.anchorMin = new Vector2(0f, 1f);
                cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                if (oneColumn)
                {
                    cell.sizeDelta = new Vector2(w, cellH);
                    cell.anchoredPosition = new Vector2(0f, -(y + s * (cellH + rowGap)));
                }
                else
                {
                    cell.sizeDelta = new Vector2(cellW, cellH);
                    cell.anchoredPosition = new Vector2((s % 2) * (cellW + (int)UiSpace.XL), -(y + (s / 2) * (cellH + rowGap)));
                }

                SetTop(_statLabels[s], 0f, 0f, statLine);
                SetTop(_statValues[s], 0f, 0f, statLine);
                _statLabels[s].alignment = TextAlignmentOptions.MidlineLeft;
                _statValues[s].alignment = TextAlignmentOptions.MidlineRight;
            }

            y += statsH;
            _compareCaption.gameObject.SetActive(showCompare);
            if (showCompare)
            {
                y += (int)UiSpace.S;
                Dock((RectTransform)_compareCaption.transform, y, compareH);
                y += compareH;
            }

            // U1b MUST-1 (AD + ui-ux, 06.10): CORE and the state line follow the bars with the section gap - no longer anchored
            // to the card's foot, where the slack had become an empty band once the paragraph left. The card then ends under
            // them, never above the skins' last card (AD: the middle column closes on the left column's line).
            y += gap;
            Dock(_coreRow, y, coreH);
            y += coreH + gap;
            Dock(_stateRow, y, stateH);
            y += stateH;
            FitStageCard(stageColumn, y + inset);

            // The CORE word gives its width up before any branch name would be cut (T3: nothing truncates).
            float namesW = 0f;
            for (int i = 0; i < 3; i++)
            {
                if (_coreGlyphs[i].transform.parent.gameObject.activeSelf)
                {
                    namesW += CoreGlyphSize + (int)UiSpace.XS + Mathf.Ceil(_coreNames[i].GetPreferredValues(_coreNames[i].text).x);
                }
            }

            // The CORE word takes its own measured width (a one-word label never takes an ellipsis), else it gives way.
            Transform labelTransform = _coreRow.Find("Label");
            TMP_Text coreLabel = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            float labelW = coreLabel != null
                ? Mathf.Max(CoreLabelMinWidth, Mathf.Ceil(coreLabel.GetPreferredValues(coreLabel.text).x) + (int)UiSpace.S)
                : CoreLabelMinWidth;
            bool coreWord = namesW + (int)UiSpace.S * 2f + labelW <= w;
            if (labelTransform != null)
            {
                labelTransform.gameObject.SetActive(coreWord);
                ((RectTransform)labelTransform).sizeDelta = new Vector2(labelW, 0f);
            }

            float left = coreWord ? labelW : 0f;
            float slotW = Mathf.Floor((w - left - (int)UiSpace.S * 2f) / 3f);
            float x = left;
            for (int i = 0; i < 3; i++)
            {
                RectTransform slot = (RectTransform)_coreGlyphs[i].transform.parent;
                float want = CoreGlyphSize + (int)UiSpace.XS + Mathf.Ceil(_coreNames[i].GetPreferredValues(_coreNames[i].text).x);
                bool natural = namesW + (int)UiSpace.S * 2f + left <= w;
                float width = natural ? want : slotW;   // each takes what it needs while all three fit, else equal thirds
                slot.sizeDelta = new Vector2(width, 0f);
                slot.anchoredPosition = new Vector2(x, 0f);
                x += width + (int)UiSpace.S;
            }
        }

        /// <summary>
        /// Fits the weapon rows and the Specials strip to the column's real height. On the short Mac canvas (UI scale 1.2 ×
        /// TextScale 1.25), first the Specials note line goes, then the rows give up height down to their text line + 8 px.
        /// </summary>
        private void LayoutWeapons()
        {
            if (_weaponsColumn == null || _weaponButtons == null || _specialsRow == null)
            {
                return;
            }

            _weaponsSize = _weaponsColumn.rect.size;
            float avail = _weaponsSize.y;
            if (avail < 1f)
            {
                _weaponsLayoutPending = true;
                return;
            }

            _weaponsLayoutPending = false;
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
            LayoutSideViews(rowH);
            LayoutSpecialTiles();
        }

        /// <summary>
        /// R3 m12 (WP-098B): the weapons column's side views at one scale - ten rows and the two Specials. The scale is the
        /// largest that lets the longest weapon fill the row slot (at most 128, never past the longest row name's room), the
        /// tallest stand in the row less 8, and each Special keep its name and its mode (at 80 %) beside it in the tile; it
        /// never drops below the old 40 px glyph slot. Each weapon's stock stands on the slot's right edge (rows) or after the
        /// tile's inset (Specials), its foot on one baseline - the tallest weapon centred in the row.
        /// </summary>
        private void LayoutSideViews(float rowH)
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
                float tileW = ((RectTransform)_specialNames[i].transform.parent).rect.width;
                float room = tileW - (int)UiSpace.M * 2f - (int)UiSpace.S * 2f
                             - Mathf.Ceil(_specialNames[i].GetPreferredValues(_specialNames[i].text).x)
                             - Mathf.Ceil(mode.GetPreferredValues(mode.text).x * 0.8f);
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

                // The name follows the slot (the old glyph's 40 + its M while no row has a side view).
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
                RectTransform nameRect = (RectTransform)_specialNames[i].transform;
                nameRect.offsetMin = new Vector2(_specialTextLeft[i], nameRect.offsetMin.y);
            }
        }

        /// <summary>
        /// U1b MUST-2 (AD + ui-ux, 06.10): on the Mac canvas in TR the special tile read "NAMEKUJİYAY ATIŞ" - the name and the
        /// fire mode met. The name takes its own width and never shrinks; the mode stands right-aligned at least S after it,
        /// stepping down to 80 % of its size when the room is short, then an ellipsis.
        /// </summary>
        private void LayoutSpecialTiles()
        {
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
                float nameW = Mathf.Ceil(name.GetPreferredValues(name.text).x);
                float nameRight = Mathf.Min(tileW - (int)UiSpace.M, left + nameW);
                ((RectTransform)name.transform).offsetMax = new Vector2(nameRight - tileW, 0f);

                float modeLeft = nameRight + (int)UiSpace.S;
                float room = Mathf.Max(0f, tileW - (int)UiSpace.M - modeLeft);
                ((RectTransform)mode.transform).offsetMin = new Vector2(modeLeft, 0f);
                mode.fontSize = _specialModeSize;
                float modeW = Mathf.Ceil(mode.GetPreferredValues(mode.text).x);
                if (modeW > room && modeW > 0f)
                {
                    mode.fontSize = Mathf.Max(_specialModeSize * 0.8f, _specialModeSize * room / modeW);
                }
            }
        }

        private static float LineOf(TMP_Text text)
        {
            return Mathf.Ceil(text.GetPreferredValues("Ag").y);
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

        private static TMP_Text TopLabel(string name, RectTransform parent, UiType type, UiColor colour, float left, float top, float height)
        {
            TMP_Text label = UiFactory.Label(name, parent, string.Empty, type, colour, TextAnchor.UpperLeft);
            RectTransform rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
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

        /// <summary>Put roster row <paramref name="index"/> on the stage. Rebuilt on a change only.</summary>
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

            Sprite sprite = _weaponGlyphs[index] != null ? _weaponGlyphs[index].sprite : null;
            UiArt.SetGlyph(_stageGlyph, sprite);
            _stageGlyph.enabled = sprite != null;
            _stageGlyph.color = RowTint(index, unlocked);

            _stageName.text = UiStrings.Upper(WeaponName(id));
            _stageClass.text = ClassLine(key, def);
            _stageRole.text = UiStrings.Get("weapon." + key + ".role", string.Empty);
            // WP-081 U1b (R2 U1, fewer letters): the class line, the role and the bars say what the weapon is; the paragraph
            // and the flavour line leave the card (their strings stay in the table, LayoutStage closes their bands when empty).
            _stageDescription.text = string.Empty;
            _stageFlavour.text = string.Empty;

            WeaponStatReadout.Fill(def, _stats);
            for (int s = 0; s < 4; s++)
            {
                _statLabels[s].text = _stats[s].Label;
                _statValues[s].text = _stats[s].Value;
                bool hasBar = _stats[s].Bar01 >= 0f;
                _statBars[s].gameObject.SetActive(hasBar);
                if (hasBar)
                {
                    // E9: the bars move with the focus (eased by UiBar); the first show lands at once.
                    _statBars[s].SetValue(_stats[s].Bar01, first);
                }
            }

            UpgradeBranch[] branches = def != null && def.upgrade != null && def.upgrade.coreBranches != null ? def.upgrade.coreBranches : new UpgradeBranch[0];
            for (int i = 0; i < 3; i++)
            {
                bool has = i < branches.Length;
                _coreGlyphs[i].transform.parent.gameObject.SetActive(has);
                if (!has)
                {
                    continue;
                }

                Sprite branchSprite = UiArt.UpgradeIcon(branches[i]);
                UiArt.SetGlyph(_coreGlyphs[i], branchSprite != null ? branchSprite : UiSprites.Disc);
                _coreGlyphs[i].color = UiTokens.Get(unlocked ? UiColor.TextPrimary : UiColor.TextSecondary);
                _coreNames[i].text = BranchName(branches[i], key);
            }

            // The state line names only what stands between the player and the weapon (AD R1-B MUST-2a: "carried for the
            // whole run" is the header's sentence, so a weapon the player owns shows no line).
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
                _stateLine.text = LockLine(ProfileService.WeaponLock(id));
            }

            LayoutStage();

            if (!first)
            {
                // A focus change re-inks the stage: the text block dips and returns (Fast, Decor - at rest under RM).
                _stageText.alpha = 0.55f;
                UiMotion.Alpha(_stageText, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            }
        }

        /// <summary>
        /// WP-077 (S-04 ProfileService.WeaponLock): a ladder weapon reads "L3" beside the kit's lock glyph (WP-098B: the glyph
        /// says LOCKED - U1, fewer letters); a PATH weapon reads "PATH · L11", or "PATH › ready" once the meta PATH can sell
        /// it. Built once per row.
        /// </summary>
        private static string LockChip(ClassicWeaponId id)
        {
            MetaPathRules.WeaponLockInfo info = ProfileService.WeaponLock(id);
            if (info.LockReason == MetaLockReason.WallBuyOnly)
            {
                return UiStrings.Get("selection.lock.wallbuy_chip", "WALL BUY");   // WP-107 (R4): rl_1 is bought in the run
            }

            string level = info.LevelGate.ToString(CultureInfo.InvariantCulture);
            if (info.OnPath)
            {
                return info.Ready ? UiStrings.Upper(UiStrings.Get("path.lock.ready", "PATH › ready")) : UiStrings.Get("path.title", "PATH") + " · L" + level;
            }

            return "L" + level;
        }

        /// <summary>
        /// The stage's lock line: "Reach profile level 3 · you are level 1" on the ladder. On the PATH it is the pack's
        /// path.lock.line (cost and level gate, each number in cream once it is met and secondary while it is not), or
        /// "PATH › ready".
        /// </summary>
        private static string LockLine(MetaPathRules.WeaponLockInfo info)
        {
            if (info.LockReason == MetaLockReason.WallBuyOnly)
            {
                return UiStrings.Get("selection.lock.wallbuy", "Bought at its wall during a run. It is never a starting weapon.");
            }

            string level = info.LevelGate.ToString(CultureInfo.InvariantCulture);
            if (!info.OnPath)
            {
                return UiStrings.Get("selection.lock.level", "Reach profile level {level} · you are level {current}")
                    .Replace("{level}", level)
                    .Replace("{current}", ProfileService.Current.ProfileLevel.ToString(CultureInfo.InvariantCulture));
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

        private static string ClassLine(string key, WeaponDefinition def)
        {
            string cls = UiStrings.Get("weapon." + key + ".class", string.Empty);
            string mode = UiStrings.Get("weapon." + key + ".firemode", string.Empty);
            if (def != null && mode.Contains("{count}"))
            {
                mode = mode.Replace("{count}", Mathf.Max(1, def.fire.burstCount).ToString(CultureInfo.InvariantCulture));
            }

            return string.IsNullOrEmpty(mode) ? cls : cls + "  ·  " + mode;
        }

        /// <summary>The branch's pack name (tree.run.branch.&lt;snake&gt;.name); rl_1's Explosion reads "Blast" (pack A6).</summary>
        private static string BranchName(UpgradeBranch branch, string weaponKey)
        {
            string snake = branch == UpgradeBranch.Explosion && weaponKey == "rl_1" ? "blast" : Snake(branch.ToString());
            return UiStrings.Get("tree.run.branch." + snake + ".name", branch.ToString());
        }

        private static string Snake(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0 && char.IsUpper(value[i]))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(value[i]));
            }

            return sb.ToString();
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
        /// The Arsenal's classic weapons in sortOrder (WP-069; the ten of DEC-097). A definition that is not shipped
        /// (enabled = false: rl_1 until B3) is not shown at all - no locked card, no placeholder (user decision, C-00
        /// 2026-09-17). Specials have their own tiles.
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
            RefreshStartCaption();
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
        /// glyph beside it; Cancel does the same), START RUN its C11 (Menu.Play + the word, unfilled, as wide as its word; the Submit glyph beside it);
        /// focus on either is the kit's cream brackets. The loadout echo sits between them.
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
            float startWidth = startRect.sizeDelta.x;   // the kit's measured width
            startRect.anchorMin = new Vector2(1f, 0.5f);
            startRect.anchorMax = new Vector2(1f, 0.5f);
            startRect.pivot = new Vector2(1f, 0.5f);
            startRect.anchoredPosition = Vector2.zero;

            // WP-022F: the footer's two prompts follow the device (A / ENTER on START RUN, B / ESC on Back). The kit's Back
            // carries its Cancel glyph outside its right edge; START RUN's Submit glyph mirrors it outside its left edge, so
            // the C11 keeps its measured width and stays unfilled (no key chip inside the brush control).
            UiPrompt startPrompt = UiInputGlyphs.Build("Prompt", start.transform, UiPromptAction.Submit, UiColor.TextPrimary, UiColor.SurfaceInset);
            float startGlyph = 0f;
            if (startPrompt != null)
            {
                var promptRect = (RectTransform)startPrompt.transform;
                promptRect.anchorMin = new Vector2(0f, 0.5f);
                promptRect.anchorMax = new Vector2(0f, 0.5f);
                promptRect.pivot = new Vector2(1f, 0.5f);
                promptRect.anchoredPosition = new Vector2(-(int)UiSpace.S, 0f);
                startGlyph = UiInputGlyphs.ReservedWidth(UiPromptAction.Submit) + (int)UiSpace.S;
            }

            // E9 / GD P14: the caption beside START RUN echoes the loadout ("Ivory / Crimson · Hinawa"). It starts after Back
            // and its pad glyph (the C10's width + a glyph's room) and ends before START RUN's glyph.
            _startCaption = UiFactory.Label("Loadout", footer, string.Empty, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleRight);
            RectTransform captionRect = UiFactory.Fill((RectTransform)_startCaption.transform);
            captionRect.offsetMin = new Vector2(backRect.sizeDelta.x + UiBrushTokens.ControlHeight + (int)UiSpace.XL, 0f);
            captionRect.offsetMax = new Vector2(-(startWidth + startGlyph + (int)UiSpace.XL), 0f);
            _startCaption.enableWordWrapping = false;
            RefreshStartCaption();
        }

        private void RefreshStartCaption()
        {
            if (_startCaption == null || _weaponIds == null)
            {
                return;
            }

            int index = SelectedIndex();
            string weapon = index >= 0 ? WeaponName(_weaponIds[index]) : string.Empty;
            _startCaption.text = PlayerSkins.Get(PrototypeUiFlow.SelectedSkin).DisplayName + "  ·  " + weapon;
        }

        private static void GoBack()
        {
            UiSound.Play(UiSoundKind.Back);
            PrototypeUiFlow.GoToMainMenu();
        }

        // ============================================================= side views

        /// <summary>
        /// R3 m12 (WP-098B, user 07.10: "use the images in the folder for the Prepare Run weapons"): the AP-036 v001 side
        /// views, every weapon picture on this page (ten rows, the stage, the two Specials). The renders were framed one by
        /// one (331-1292 px per metre), so the intake (UI/AP036/AP036-intake-manifest.json) trimmed each to its alpha box,
        /// resampled it to at most 600 px/m and padded it to a multiple of 4. This table is that manifest's numbers - the
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
