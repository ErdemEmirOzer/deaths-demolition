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
    /// SESSION MEMORY (acceptance criteria 23 and 24). The current choice comes from
    /// <c>PrototypeUiFlow.SelectedSkin</c> and <c>PrototypeUiFlow.SelectedWeapon</c>, statics reset by
    /// RuntimeInitializeOnLoadMethod. A second run in the same session opens on the previous choice; a restarted
    /// application opens on the defaults. Neither is written to the profile save.
    /// </summary>
    public sealed class SelectionScreen : UiScreen
    {
        private const float SkinCardHeight = 120f;          // E9: portrait 88 + 2 × 16
        private const float SwatchWidth = 100f;
        private const float BadgeWidth = 160f;
        private const float FooterHeight = 88f;

        private const float WeaponRowHeight = 48f;
        private const float WeaponRowGap = 4f;
        private const float RowGlyph = 40f;
        private const float SpecialTileHeight = 48f;
        private const float StageGlyph = 128f;
        private const float StatBarHeight = 8f;
        private const float CoreGlyphSize = 26f;            // AD v003 MUST-4: ≥ 24 px at 1080p
        private const float XpBarWidth = 120f;

        // The three columns of the 12-column grid (560 / 24 / 560 / 24 / 560 at 1728).
        private const float ColumnShare = 560f / 1728f;
        private const float GutterShare = 24f / 1728f;

        private static readonly Vector3 GlyphEdge = new Vector3(0.25f, 0f, 0f);   // the HudInk edge the HUD glyphs wear

        private UiButton[] _skinButtons;
        private GameObject[] _skinBadges;
        private Image[] _skinPreviews;

        private UiButton[] _weaponButtons;
        private GameObject[] _weaponSelected;
        private RectTransform[] _weaponLockChips;
        private Image[] _weaponGlyphs;
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
        private UiBar _xpBar;
        private int _stageIndex = -1;
        private GameObject _lastFocus;
        private RectTransform _stageContent;
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
        private TMP_Text _specialsNote;
        private float _weaponsTop;
        private float _rowHeight;
        private float _tileHeight;
        private float _specialsTitleBand;
        private float _specialsNoteBand;
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

        protected override UiContext BaseContext
        {
            get { return UiContext.Selection; }
        }

        protected override void Build()
        {
            // Defensive: a profile edited between the menu and here must not be able to
            // leave a locked weapon selected.
            if (!ProfileService.IsWeaponUnlocked(PrototypeUiFlow.SelectedWeapon))
            {
                PrototypeUiFlow.SelectedWeapon = ProfileService.FirstUnlockedWeapon();
            }

            float headerHeight = BuildHeader(
                "BEFORE THE RUN",
                "PREPARE YOUR RUN",
                "The skin is cosmetic. The classic weapon is carried for the whole run and cannot be swapped.");

            float top = headerHeight + (int)UiSpace.XXL;
            float stageFrom = ColumnShare + GutterShare;
            float weaponsFrom = stageFrom + ColumnShare + GutterShare;

            RectTransform skins = Column("Skins", 0f, ColumnShare, top, FooterHeight);
            RectTransform stage = Column("Stage", stageFrom, stageFrom + ColumnShare, top, FooterHeight);
            RectTransform weapons = Column("Weapons", weaponsFrom, 1f, top, FooterHeight);
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

            stack.Add(UiFactory.Heading("CHARACTER SKIN", column), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.S);

            TMP_Text caption = UiFactory.Label(
                "SkinCaption", column,
                "All three are available from the start and none of them changes a gameplay value.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)caption.transform, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption)) * 2f);

            stack.Gap(UiSpace.L);

            int count = PlayerSkins.Count;
            _skinButtons = new UiButton[count];
            _skinBadges = new GameObject[count];
            _skinPreviews = new Image[count];

            for (int i = 0; i < count; i++)
            {
                var skin = (PlayerSkin)i;
                PlayerSkins.Palette palette = PlayerSkins.Get(skin);

                UiButton button = UiFactory.Button(
                    "Skin." + skin, column, palette.DisplayName, UiButtonVariant.Card,
                    () => SelectSkin(skin));

                stack.Add((RectTransform)button.transform, SkinCardHeight);
                stack.Gap(UiSpace.L);

                _skinPreviews[i] = BuildPreview(button, i);
                BuildSwatch(button, palette);

                // AD G1 MUST-3: "Cosmetic only" on top in its own line box, the name in the band below it, top-aligned and
                // growing downward - the two-line name at Mac UI 1.2 × TextScale 1.25 never climbs into the caption.
                float textLeft = PreviewSlotWidth + SwatchWidthNow + (int)UiSpace.XL + (int)UiSpace.L;
                float captionBand = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption));
                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                // The name band sits under the caption row, below the SELECTED badge (30 px), so it takes the card's full
                // width: at Mac UI 1.2 × TextScale 1.25 the badge reserve clipped "Ivory / Crimson".
                labelRect.offsetMin = new Vector2(textLeft, (int)UiSpace.M);
                labelRect.offsetMax = new Vector2(-(int)UiSpace.L, -((int)UiSpace.M + Mathf.Max(captionBand, 30f) + (int)UiSpace.XS));
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAlignmentOptions.TopLeft;

                TMP_Text detail = UiFactory.Label(
                    "Detail", button.transform, "Cosmetic only", UiType.Caption, UiColor.TextSecondary,
                    TextAnchor.UpperLeft);
                RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
                detailRect.anchorMin = new Vector2(0f, 1f);
                detailRect.pivot = new Vector2(0f, 1f);
                detailRect.offsetMin = new Vector2(textLeft, -((int)UiSpace.M + captionBand));
                detailRect.offsetMax = new Vector2(-BadgeWidth, -(int)UiSpace.M);

                _skinButtons[i] = button;
                _skinBadges[i] = BuildBadge(button, "SELECTED");

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
                _skinButtons[i].SetSelectedChoice(selected);
                _skinBadges[i].SetActive(selected);
            }
        }

        // ================================================================= weapons

        /// <summary>
        /// One 48 px row per Arsenal classic weapon (WP-069 §1098: Selection reads the Arsenal, enabled only, sortOrder): the glyph in its
        /// lineage tint (N19; α 0.55 while locked, never a grey swap), the pack name, and a chip - SELECTED on the choice,
        /// LOCKED · L{n} on a weapon the profile has not earned. Then the two Specials as information tiles.
        /// </summary>
        private void BuildWeaponColumn(RectTransform column)
        {
            var stack = new UiStack(column);
            stack.Add(UiFactory.Heading("CLASSIC WEAPON", column), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.S);
            _weaponsColumn = column;
            _weaponsTop = stack.Cursor;

            List<WeaponDefinition> roster = RosterForDisplay();
            int count = roster.Count;
            _weaponIds = new ClassicWeaponId[count];
            _weaponDefs = new WeaponDefinition[count];
            _weaponButtons = new UiButton[count];
            _weaponSelected = new GameObject[count];
            _weaponLockChips = new RectTransform[count];
            _weaponGlyphs = new Image[count];
            _weaponEnabled = new bool[count];

            float rowHeight = Mathf.Max(WeaponRowHeight, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.H3)) + (int)UiSpace.M);
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

                UiButton button = UiFactory.Button(
                    "Weapon." + def.weaponId + "." + id, column, WeaponName(id), UiButtonVariant.Card,
                    () => SelectWeapon(id));
                stack.Add((RectTransform)button.transform, rowHeight);
                if (i < count - 1)
                {
                    stack.Cursor += WeaponRowGap;
                }

                // The G1 cell by runtime id (N19 Weapon.<ClassicWeaponId>; a v3 id without one borrows its class sibling's),
                // then the definition's own HUD icon key.
                Sprite glyphSprite = UiArt.WeaponIcon(id);
                if (glyphSprite == null)
                {
                    glyphSprite = UiArt.Icon(def.HudIconId);
                }

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

                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(textLeft, 0f);
                labelRect.offsetMax = new Vector2(-BadgeWidth, 0f);
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAlignmentOptions.MidlineLeft;
                button.LabelText.enableWordWrapping = false;
                button.LabelText.overflowMode = TextOverflowModes.Ellipsis;

                _weaponSelected[i] = RowChip(button, UiChipKind.Selected, "SELECTED").gameObject;
                _weaponSelected[i].SetActive(false);
                if (!unlocked)
                {
                    _weaponLockChips[i] = RowChip(button, UiChipKind.Neutral, enabled ? LockChip(id) : "UNAVAILABLE");
                }

                _weaponButtons[i] = button;
            }

            BuildSpecialTiles(column, ref stack);
            LayoutWeapons();
        }

        private static RectTransform RowChip(UiButton button, UiChipKind kind, string word)
        {
            TMP_Text label = UiChips.Create("Chip", button.transform, kind, word, UiChipSize.Row);
            var chip = (RectTransform)label.transform.parent;
            chip.anchorMin = new Vector2(1f, 0.5f);
            chip.anchorMax = new Vector2(1f, 0.5f);
            chip.pivot = new Vector2(1f, 0.5f);
            chip.anchoredPosition = new Vector2(-(int)UiSpace.M, 0f);
            return chip;
        }

        /// <summary>
        /// The two Specials (DEC-094): found on the field, never chosen here - information tiles, not controls. The glyph
        /// wears its Special's identity tint (Arc / Gel); the name and fire mode come from the pack.
        /// </summary>
        private void BuildSpecialTiles(RectTransform column, ref UiStack stack)
        {
            stack.Gap(UiSpace.M);
            float band = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Label));
            float captionBand = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption));
            RectTransform head = UiFactory.NewRect("SpecialsHead", column);
            _specialsHead = head;
            _specialsTitleBand = band;
            _specialsNoteBand = captionBand;
            stack.Add(head, band + captionBand);
            TMP_Text title = UiFactory.Label("Title", head, UiStrings.Get("selection.specials.title", "SPECIAL WEAPONS"), UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            SetTop(title, 0f, 0f, band);
            // The note on its own line: side by side they met on the Mac canvas.
            TMP_Text note = UiFactory.Label("Note", head, UiStrings.Get("selection.specials.caption", "Found on the field, never chosen here."), UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleLeft);
            SetTop(note, 0f, band, captionBand);
            note.overflowMode = TextOverflowModes.Ellipsis;
            _specialsNote = note;
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

                Sprite sprite = UiArt.Icon(glyphKeys[i]);
                float left = (int)UiSpace.M;
                if (sprite != null)
                {
                    Image glyph = UiFactory.NewImage("Icon", tile, UiColor.TextPrimary, sprite);
                    glyph.preserveAspect = true;
                    if (UiArt.IsGlyph(sprite))
                    {
                        UiArt.ApplyGlyph(glyph, GlyphEdge);
                    }

                    glyph.color = UiTokens.Get(i == 0 ? UiGlyphTints.ChainLightning : UiGlyphTints.Gel);
                    Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2(left, 0f), new Vector2(RowGlyph, RowGlyph));
                    left += RowGlyph + (int)UiSpace.S;
                }

                TMP_Text name = UiFactory.Label("Name", tile, UiStrings.Get("weapon." + ids[i] + ".name", ids[i]).ToUpperInvariant(), UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleLeft);
                RectTransform nameRect = UiFactory.Fill((RectTransform)name.transform);
                nameRect.offsetMin = new Vector2(left, 0f);
                nameRect.offsetMax = new Vector2(-(int)UiSpace.M, 0f);
                TMP_Text mode = UiFactory.Label("Mode", tile, UiStrings.Get("weapon." + ids[i] + ".firemode", string.Empty), UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleRight);
                RectTransform modeRect = UiFactory.Fill((RectTransform)mode.transform);
                modeRect.offsetMax = new Vector2(-(int)UiSpace.M, 0f);
            }
        }

        // =================================================================== stage

        private void BuildStage(RectTransform column)
        {
            UiCard card = UiCard.Create("StageCard", column, Vector2.zero, UiElevation.Elev1, UiColor.SurfacePanel, UiSpace.XL);
            UiFactory.Fill((RectTransform)card.transform);
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
            _stageRole = TopLabel("Role", _stageTop, UiType.Body, UiColor.TextPrimary, 0f, 0f, 1f);
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

            _compareCaption = UiFactory.Label("CompareCaption", content,
                UiStrings.Get("selection.stats.caption", "Bars compare with the other classic weapons. Longer is better."),
                UiType.Caption, UiColor.TextMuted);

            // CORE: the weapon's three round-end branches (WP-034 shop gate).
            _coreRow = UiFactory.NewRect("CoreBranches", content);
            TMP_Text coreLabel = UiFactory.Label("Label", _coreRow, UiStrings.Get("selection.core", "CORE"), UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform coreLabelRect = (RectTransform)coreLabel.transform;
            coreLabelRect.anchorMin = new Vector2(0f, 0f);
            coreLabelRect.anchorMax = new Vector2(0f, 1f);
            coreLabelRect.pivot = new Vector2(0f, 0.5f);
            coreLabelRect.sizeDelta = new Vector2(CoreLabelWidth, 0f);
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
            RectTransform stateRect = UiFactory.Fill((RectTransform)_stateLine.transform);
            stateRect.offsetMax = new Vector2(-(XpBarWidth + (int)UiSpace.M), 0f);
            _xpBar = UiFactory.Bar("Xp", _stateRow, UiColor.BarXp, StatBarHeight);
            Place((RectTransform)_xpBar.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(XpBarWidth, StatBarHeight));
        }

        private const float CoreLabelWidth = 52f;
        private const float StageGlyphCompact = 96f;

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
            if (area.height < 1f || area.width < 1f)
            {
                _stageLayoutPending = true;   // the canvas has not laid out yet: next Update
                return;
            }

            _stageLayoutPending = false;
            float w = area.width;
            float bodyLine = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Body));
            float captionLine = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption));
            float labelLine = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Label));
            float nameBand = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.H1));

            // Stat cells: one line when label + value fit, else the value moves under the label.
            float cellW = Mathf.Floor((w - (int)UiSpace.XL) * 0.5f);
            var stacked = new bool[4];
            float[] rowH = new float[2];
            for (int s = 0; s < 4; s++)
            {
                float lw = _statLabels[s].GetPreferredValues(_statLabels[s].text).x;
                float vw = _statValues[s].GetPreferredValues(_statValues[s].text).x;
                stacked[s] = lw + vw + (int)UiSpace.S > cellW;
                float h = (stacked[s] ? captionLine + labelLine : Mathf.Max(captionLine, labelLine)) + (int)UiSpace.XS + StatBarHeight;
                rowH[s / 2] = Mathf.Max(rowH[s / 2], h);
            }

            float statsH = rowH[0] + (int)UiSpace.M + rowH[1];
            float coreH = Mathf.Max(CoreGlyphSize + 4f, captionLine);
            float stateH = captionLine;
            float compareH = string.IsNullOrEmpty(_compareCaption.text) ? 0f : Mathf.Ceil(_compareCaption.GetPreferredValues(_compareCaption.text, w, 0f).y);
            float flavourH = string.IsNullOrEmpty(_stageFlavour.text) ? 0f : captionLine;
            float descMax = bodyLine * 5f;
            float descH = string.IsNullOrEmpty(_stageDescription.text) ? 0f
                : Mathf.Min(descMax, Mathf.Ceil(_stageDescription.GetPreferredValues(_stageDescription.text, w, 0f).y));

            float glyph = StageGlyph;
            float gap = (int)UiSpace.L;
            bool showFlavour = flavourH > 0f;
            bool showCompare = compareH > 0f;
            float topH = TopHeight(glyph, w, nameBand, labelLine, bodyLine);
            float Total()
            {
                return topH + gap + descH + (showFlavour ? (int)UiSpace.XS + flavourH : 0f) + gap + statsH
                       + (showCompare ? (int)UiSpace.S + compareH : 0f) + gap + coreH + gap + stateH;
            }

            if (Total() > area.height) { gap = (int)UiSpace.S; }
            if (Total() > area.height) { showFlavour = false; }
            if (Total() > area.height) { showCompare = false; }
            if (Total() > area.height) { glyph = StageGlyphCompact; topH = TopHeight(glyph, w, nameBand, labelLine, bodyLine); }
            if (Total() > area.height) { descH = Mathf.Max(bodyLine * 2f, descH - (Total() - area.height)); }

            // Place, top to bottom.
            float y = 0f;
            Dock(_stageTop, y, topH);
            RectTransform glyphRect = (RectTransform)_stageGlyph.transform;
            glyphRect.anchorMin = new Vector2(0f, 1f);
            glyphRect.anchorMax = new Vector2(0f, 1f);
            glyphRect.pivot = new Vector2(0f, 1f);
            glyphRect.sizeDelta = new Vector2(glyph, glyph);
            glyphRect.anchoredPosition = Vector2.zero;
            float textLeft = glyph + (int)UiSpace.L;
            float roleH = RoleHeight(w - textLeft, bodyLine);
            SetTop(_stageName, textLeft, 0f, nameBand);
            SetTop(_stageClass, textLeft, nameBand, labelLine);
            SetTop(_stageRole, textLeft, nameBand + labelLine + (int)UiSpace.S, roleH);
            y += topH + gap;

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
                int row = s / 2;
                int col = s % 2;
                float cellTop = y + (row == 1 ? rowH[0] + (int)UiSpace.M : 0f);
                RectTransform cell = _statCells[s];
                cell.anchorMin = new Vector2(0f, 1f);
                cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                cell.sizeDelta = new Vector2(cellW, rowH[row]);
                cell.anchoredPosition = new Vector2(col * (cellW + (int)UiSpace.XL), -cellTop);
                float line = stacked[s] ? captionLine : Mathf.Max(captionLine, labelLine);
                SetTop(_statLabels[s], 0f, 0f, line);
                _statLabels[s].alignment = TextAlignmentOptions.MidlineLeft;
                if (stacked[s])
                {
                    SetTop(_statValues[s], 0f, captionLine, labelLine);
                    _statValues[s].alignment = TextAlignmentOptions.MidlineLeft;
                }
                else
                {
                    SetTop(_statValues[s], 0f, 0f, line);
                    _statValues[s].alignment = TextAlignmentOptions.MidlineRight;
                }
            }

            y += statsH;
            _compareCaption.gameObject.SetActive(showCompare);
            if (showCompare)
            {
                y += (int)UiSpace.S;
                Dock((RectTransform)_compareCaption.transform, y, compareH);
                y += compareH;
            }

            y += gap;
            Dock(_coreRow, y, coreH);

            // The CORE word gives its width up before any branch name would be cut (T3: nothing truncates).
            float namesW = 0f;
            for (int i = 0; i < 3; i++)
            {
                if (_coreGlyphs[i].transform.parent.gameObject.activeSelf)
                {
                    namesW += CoreGlyphSize + (int)UiSpace.XS + Mathf.Ceil(_coreNames[i].GetPreferredValues(_coreNames[i].text).x);
                }
            }

            bool coreWord = namesW + (int)UiSpace.S * 2f + CoreLabelWidth <= w;
            _coreRow.Find("Label").gameObject.SetActive(coreWord);
            float left = coreWord ? CoreLabelWidth : 0f;
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

            y += coreH + gap;
            Dock(_stateRow, y, stateH);
        }

        private float TopHeight(float glyph, float width, float nameBand, float labelLine, float bodyLine)
        {
            return Mathf.Max(glyph, nameBand + labelLine + (int)UiSpace.S + RoleHeight(width - glyph - (int)UiSpace.L, bodyLine));
        }

        private float RoleHeight(float width, float bodyLine)
        {
            if (string.IsNullOrEmpty(_stageRole.text) || width < 1f)
            {
                return bodyLine;
            }

            return Mathf.Min(bodyLine * 2f, Mathf.Ceil(_stageRole.GetPreferredValues(_stageRole.text, width, 0f).y));
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
            bool note = true;
            float Need()
            {
                return _weaponsTop + n * rowH + Mathf.Max(0, n - 1) * WeaponRowGap + (int)UiSpace.M
                       + _specialsTitleBand + (note ? _specialsNoteBand : 0f) + (int)UiSpace.XS + _tileHeight;
            }

            if (Need() > avail)
            {
                note = false;
            }

            if (Need() > avail && n > 0)
            {
                float floor = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.H3)) + (int)UiSpace.S;
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
            float headH = _specialsTitleBand + (note ? _specialsNoteBand : 0f);
            Dock(_specialsHead, y, headH);
            _specialsNote.gameObject.SetActive(note);
            y += headH + (int)UiSpace.XS;
            Dock(_specialsRow, y, _tileHeight);
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
            Color tint = UiTokens.Get(UiGlyphTints.Weapon(id));
            tint.a = unlocked ? 1f : UiGlyphTints.LockedAlpha;
            _stageGlyph.color = tint;

            _stageName.text = WeaponName(id).ToUpperInvariant();
            _stageClass.text = ClassLine(key, def);
            _stageRole.text = UiStrings.Get("weapon." + key + ".role", string.Empty);
            _stageDescription.text = UiStrings.Get("weapon." + key + ".desc", string.Empty);
            _stageFlavour.text = UiStrings.Get("weapon." + key + ".flavor", string.Empty);

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

            if (!enabled)
            {
                _stateLine.text = UiStrings.Get("selection.not_in_build", "Not in this build.");
                _xpBar.gameObject.SetActive(false);
            }
            else if (unlocked)
            {
                _stateLine.text = UiStrings.Get("selection.carried", "Carried for the whole run. It cannot be swapped.");
                _xpBar.gameObject.SetActive(false);
            }
            else
            {
                MetaPathRules.WeaponLockInfo lockInfo = ProfileService.WeaponLock(id);
                _stateLine.text = LockLine(lockInfo);
                _xpBar.gameObject.SetActive(lockInfo.Ladder || !lockInfo.LevelMet);   // the level is what is missing
                _xpBar.SetValue(ProfileService.LevelProgress01, true);
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
        /// WP-077 (S-04 ProfileService.WeaponLock): a ladder weapon reads "LOCKED · L3"; a PATH weapon reads "PATH · L11",
        /// or "PATH › ready" once the meta PATH can sell it. Built once per row.
        /// </summary>
        private static string LockChip(ClassicWeaponId id)
        {
            MetaPathRules.WeaponLockInfo info = ProfileService.WeaponLock(id);
            string level = info.LevelGate.ToString(CultureInfo.InvariantCulture);
            if (info.OnPath)
            {
                return info.Ready ? UiStrings.Get("path.lock.ready", "PATH › ready").ToUpperInvariant() : UiStrings.Get("path.title", "PATH") + " · L" + level;
            }

            return UiStrings.Get("tree.state.locked", "LOCKED") + " · L" + level;
        }

        /// <summary>
        /// The stage's lock line: "Reach profile level 3 · you are level 1" on the ladder. On the PATH it is the pack's
        /// path.lock.line (cost and level gate, each number in cream once it is met and secondary while it is not), or
        /// "PATH › ready".
        /// </summary>
        private static string LockLine(MetaPathRules.WeaponLockInfo info)
        {
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
                // E9: a locked row refuses - its chip nudges (the words stay readable), Denied plays, nothing changes.
                for (int i = 0; i < _weaponIds.Length; i++)
                {
                    if (_weaponIds[i] == id && _weaponLockChips[i] != null)
                    {
                        UiPresets.DeniedNudge(_weaponLockChips[i]);
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

                _weaponButtons[i].SetSelectedChoice(selected);
                _weaponSelected[i].SetActive(selected);
                if (_weaponGlyphs[i] != null)
                {
                    // N19: the lineage tint is identity - a locked weapon keeps it at α 0.55, never a grey swap.
                    Color tint = UiTokens.Get(UiGlyphTints.Weapon(id));
                    tint.a = unlocked ? 1f : UiGlyphTints.LockedAlpha;
                    _weaponGlyphs[i].color = tint;
                }
            }
        }

        // ================================================================== chrome

        /// <summary>A pill in the top-right of a skin card. Hidden until the card is chosen.</summary>
        private static GameObject BuildBadge(UiButton button, string text)
        {
            TMP_Text chip = UiFactory.Chip(
                "Badge",
                button.transform,
                text,
                UiColor.TextPrimary, // AD v003 MUST-3: cream, vermilion stays with START RUN
                UiColor.SurfaceBase,
                140f,
                30f);

            var chipRect = (RectTransform)chip.transform.parent;
            chipRect.anchorMin = new Vector2(1f, 1f);
            chipRect.anchorMax = new Vector2(1f, 1f);
            chipRect.pivot = new Vector2(1f, 1f);
            chipRect.anchoredPosition = new Vector2(-(int)UiSpace.L, -(int)UiSpace.L);

            GameObject badge = chipRect.gameObject;
            badge.SetActive(false);
            return badge;
        }

        private void BuildFooter()
        {
            RectTransform footer = UiFactory.NewRect("Footer", Content);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, UiTokens.TargetPreferred);
            footer.anchoredPosition = Vector2.zero;

            UiButton back = UiFactory.Button("Back", footer, "BACK", UiButtonVariant.Secondary, GoBack);
            _backButton = back;
            UiInput.LeadingIcon(back, "Menu.Back", UiColor.TextSecondary, 22f);
            UiInput.Prompt(back, UiPromptAction.Cancel);
            RectTransform backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.sizeDelta = new Vector2(240f, UiTokens.TargetPreferred);
            backRect.anchoredPosition = Vector2.zero;

            UiButton start = UiFactory.Button(
                "Start", footer, "START RUN", UiButtonVariant.Primary, PrototypeUiFlow.StartRun);
            _startButton = start;
            UiInput.Prompt(start, UiPromptAction.Submit, true);
            RectTransform startRect = (RectTransform)start.transform;
            startRect.anchorMin = new Vector2(1f, 0.5f);
            startRect.anchorMax = new Vector2(1f, 0.5f);
            startRect.pivot = new Vector2(1f, 0.5f);
            startRect.sizeDelta = new Vector2(320f, UiTokens.TargetPreferred);
            startRect.anchoredPosition = Vector2.zero;

            // E9 / GD P14: the caption beside START RUN echoes the loadout ("Ivory / Crimson · Hinawa").
            _startCaption = UiFactory.Label("Loadout", footer, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleRight);
            RectTransform captionRect = UiFactory.Fill((RectTransform)_startCaption.transform);
            captionRect.offsetMin = new Vector2(240f + (int)UiSpace.XL, 0f);
            captionRect.offsetMax = new Vector2(-(320f + (int)UiSpace.XL), 0f);
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
    }
}
