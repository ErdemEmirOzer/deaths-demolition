using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Skin and Weapon Selection. Shown before every run.
    ///
    /// TWO COLUMNS, TWO DIFFERENT KINDS OF CHOICE
    /// ------------------------------------------
    /// The skin column and the weapon column look deliberately different because the
    /// decisions are not equivalent, and a screen that made them look the same would be
    /// lying about the stakes:
    ///
    ///   * A skin is a taste choice with no consequence. Its cards are swatch first,
    ///     name second, and carry no numbers at all - there is nothing to compare.
    ///   * A weapon is carried for the whole run and cannot be swapped. Its rows lead
    ///     with the four numbers that differ between them, and a locked row shows the
    ///     level that would unlock it rather than vanishing, so the ladder is legible
    ///     from the first run.
    ///
    /// SESSION MEMORY (acceptance criteria 23 and 24). The current choice comes from
    /// <c>PrototypeUiFlow.SelectedSkin</c> and <c>WeaponRegistry.SelectedWeapon</c>, both
    /// of which are statics reset by RuntimeInitializeOnLoadMethod. A second run in the
    /// same session therefore opens on the previous choice; a restarted application
    /// opens on the prototype defaults. Neither is written to the profile save.
    ///
    /// KNOWN AND INTENTIONAL: on a brand-new profile this screen shows one unlocked
    /// weapon and two locked ones. That is correct, not a defect, and it is the reason
    /// the locked rows are designed rather than hidden - the column has to look
    /// finished in exactly that state.
    /// </summary>
    public sealed class SelectionScreen : UiScreen
    {
        // WP-022: the skin card grew to carry the AP-006 character preview (a 3:4 portrait
        // slot at the leading edge); the palette bands survive as a thin strip beside it.
        private const float SkinCardHeight = 168f;
        private const float WeaponCardHeight = 136f;
        private const float SwatchWidth = 100f;
        private const float WeaponIconSize = 56f;
        private const float BadgeWidth = 160f;
        private const float FooterHeight = 88f;

        private UiButton[] _skinButtons;
        private GameObject[] _skinBadges;

        private UiButton[] _weaponButtons;
        private GameObject[] _weaponBadges;
        private Graphic[][] _coreGlyphs;

        private ClassicWeaponId[] _weaponIds;
        private bool[] _weaponEnabled;
        private UiButton _backButton;
        private UiButton _startButton;
        private Image[] _skinPreviews;

        /// <summary>The preview images the skin cards show, index = PlayerSkin (tests read the sprite).</summary>
        public Image[] SkinPreviews
        {
            get { return _skinPreviews; }
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

            BuildSkinColumn(Column("Skins", 0f, 0.40f, top, FooterHeight));
            BuildWeaponColumn(Column("Weapons", 0.44f, 1f, top, FooterHeight));
            BuildFooter();

            RefreshSkins();
            RefreshWeapons();
            LinkNavigation();
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
        }

        /// <summary>
        /// Gamepad / keyboard map (WP-022): skins and weapons are two vertical chains,
        /// Left/Right hops between the columns at the same row, Down from the last card
        /// lands on the footer, Up from the footer returns to the last card. Locked
        /// weapons are not interactable, so the chain skips them by construction.
        /// </summary>
        private void LinkNavigation()
        {
            var skins = new System.Collections.Generic.List<Selectable>(_skinButtons);
            var weapons = new System.Collections.Generic.List<Selectable>();
            for (int i = 0; i < _weaponButtons.Length; i++)
            {
                if (_weaponButtons[i].IsInteractable())
                {
                    weapons.Add(_weaponButtons[i]);
                }
            }

            skins.Add(_backButton);
            weapons.Add(_startButton);
            UiInput.ChainVertical(skins.ToArray());
            UiInput.ChainVertical(weapons.ToArray());

            // Cards pair by row (clamped to the shorter column); the footer pairs with the
            // footer, so Left from START RUN is always BACK and never a skin card.
            int skinCards = skins.Count - 1;
            int weaponCards = weapons.Count - 1;
            for (int i = 0; i < skinCards; i++)
            {
                Selectable right = weaponCards > 0 ? weapons[Mathf.Min(i, weaponCards - 1)] : _startButton;
                UiInput.LinkHorizontal(skins[i], right, right);
            }

            for (int i = 0; i < weaponCards; i++)
            {
                Selectable left = skinCards > 0 ? skins[Mathf.Min(i, skinCards - 1)] : _backButton;
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
            stack.Add((RectTransform)caption.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

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
                // growing downward - the two-line name at Mac UI 1.2 × TextScale 1.25 ("Ivory / Crimson") never climbs
                // into the caption (it used to sit bottom-aligned and wrap upward over it).
                float textLeft = PreviewSlotWidth + SwatchWidth + (int)UiSpace.XL + (int)UiSpace.L;
                float captionBand = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption));
                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(textLeft, (int)UiSpace.L);
                labelRect.offsetMax = new Vector2(-BadgeWidth, -((int)UiSpace.L + captionBand + (int)UiSpace.XS));
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAlignmentOptions.TopLeft;

                TMP_Text detail = UiFactory.Label(
                    "Detail", button.transform, "Cosmetic only", UiType.Caption, UiColor.TextSecondary,
                    TextAnchor.UpperLeft);
                RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
                detailRect.anchorMin = new Vector2(0f, 1f);
                detailRect.pivot = new Vector2(0f, 1f);
                detailRect.offsetMin = new Vector2(textLeft, -((int)UiSpace.L + captionBand));
                detailRect.offsetMax = new Vector2(-BadgeWidth, -(int)UiSpace.L);

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
            float width = PreviewSlotWidth > 0f ? 24f : SwatchWidth;
            swatch.sizeDelta = new Vector2(width, -((int)UiSpace.L * 2));
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
        /// One card per roster definition (WP-034 <c>WeaponRoster.All</c>, sorted): enabled
        /// entries come from <see cref="IWeaponRosterSource"/> with their earned unlock state;
        /// a disabled definition (roster D in this build) is shown as a dead card so the
        /// player sees the whole roster and the data value is never touched here. Each card
        /// carries the glyph (placeholder contract in <see cref="UiArt.RosterGlyph"/>), the
        /// type / shot model line and the three core upgrade branches the weapon gates.
        /// </summary>
        private void BuildWeaponColumn(RectTransform column)
        {
            var stack = new UiStack(column);

            stack.Add(UiFactory.Heading("CLASSIC WEAPON", column), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.S);

            TMP_Text caption = UiFactory.Label(
                "WeaponCaption", column,
                "Round-end upgrades transform the weapon you take in. You never pick up another one.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)caption.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

            stack.Gap(UiSpace.L);

            List<ClassicWeaponDefinition> roster = RosterForDisplay();
            _weaponIds = new ClassicWeaponId[roster.Count];
            _weaponButtons = new UiButton[roster.Count];
            _weaponBadges = new GameObject[roster.Count];
            _coreGlyphs = new Graphic[roster.Count][];
            _weaponEnabled = new bool[roster.Count];

            IWeaponRosterSource source = WeaponRosterSourceRegistry.Current;

            for (int i = 0; i < roster.Count; i++)
            {
                ClassicWeaponDefinition def = roster[i];
                ClassicWeaponId id = def.Id;
                bool enabled = def.Enabled && def.HasRuntimeId && (source == null || IsInSource(source, id));
                bool unlocked = enabled && ProfileService.IsWeaponUnlocked(id);
                _weaponIds[i] = id;
                _weaponEnabled[i] = enabled;

                string title = enabled ? ProfileService.WeaponDisplayName(id) : RosterTitle(def);
                UiButton button = UiFactory.Button(
                    "Weapon." + def.RosterKey + "." + id, column, title, UiButtonVariant.Card,
                    () => SelectWeapon(id));

                // WP-068 slice 3 (AD MUST-1, D6): bands from the faces at the TextScale, stacked from the
                // bottom - core row, name, stat line - so the stat line never meets the name at 1.25.
                float coreBand = UiFactory.ScaledBand(CoreBranchRowHeight);
                float nameBand = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.H3));
                float statBand = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption));
                float nameBottom = (int)UiSpace.M + coreBand + (int)UiSpace.XS;
                float cardHeight = Mathf.Max(WeaponCardHeight, nameBottom + nameBand + (int)UiSpace.XS + statBand + (int)UiSpace.L);
                stack.Add((RectTransform)button.transform, cardHeight);
                stack.Gap(UiSpace.L);

                // AP-012 glyph at the leading edge (roster HudIconId → placeholder contract);
                // a roster key monogram stands in where the set has no glyph yet (AP-013).
                Sprite glyphSprite = UiArt.RosterGlyph(def.HudIconId);
                UiColor glyphColor = unlocked ? UiColor.TextPrimary : UiColor.TextMuted;
                float textLeft = (int)UiSpace.XL + WeaponIconSize + (int)UiSpace.L;
                if (glyphSprite != null)
                {
                    Image glyph = UiFactory.NewImage("Icon", button.transform, glyphColor, glyphSprite);
                    glyph.preserveAspect = true;
                    RectTransform glyphRect = (RectTransform)glyph.transform;
                    glyphRect.anchorMin = new Vector2(0f, 0.5f);
                    glyphRect.anchorMax = new Vector2(0f, 0.5f);
                    glyphRect.pivot = new Vector2(0f, 0.5f);
                    glyphRect.sizeDelta = new Vector2(WeaponIconSize, WeaponIconSize);
                    glyphRect.anchoredPosition = new Vector2((int)UiSpace.XL, 0f);
                }
                else
                {
                    Image well = UiFactory.NewImage("IconWell", button.transform, UiColor.SurfaceInset, UiSprites.Disc);
                    RectTransform wellRect = (RectTransform)well.transform;
                    wellRect.anchorMin = new Vector2(0f, 0.5f);
                    wellRect.anchorMax = new Vector2(0f, 0.5f);
                    wellRect.pivot = new Vector2(0f, 0.5f);
                    wellRect.sizeDelta = new Vector2(WeaponIconSize, WeaponIconSize);
                    wellRect.anchoredPosition = new Vector2((int)UiSpace.XL, 0f);
                    TMP_Text mono = UiFactory.Label("Monogram", wellRect, def.RosterKey, UiType.H3, glyphColor, TextAnchor.MiddleCenter);
                    UiFactory.Fill((RectTransform)mono.transform);
                }

                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(textLeft, nameBottom);
                labelRect.offsetMax = new Vector2(-BadgeWidth, -(cardHeight - nameBottom - nameBand));
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAlignmentOptions.BottomLeft;

                string detailText = !enabled
                    ? "Not in this build."
                    : unlocked
                        ? DescribeWeapon(id) + "  ·  " + DescribeModel(def)
                        : "Reach profile level " + ProfileService.UnlockLevelFor(id) + " to unlock this weapon.  ·  " + DescribeModel(def);
                TMP_Text detail = UiFactory.Label(
                    "Detail", button.transform, detailText, UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
                detail.richText = true; // AD v003 MUST-5: values in TextPrimary, labels in TextSecondary (built once per card)
                RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
                detailRect.offsetMin = new Vector2(textLeft, nameBottom + nameBand + (int)UiSpace.XS);
                detailRect.offsetMax = new Vector2(-(int)UiSpace.XL, -(int)UiSpace.L);

                _coreGlyphs[i] = BuildCoreBranchRow(button, def, textLeft, unlocked);

                _weaponButtons[i] = button;
                _weaponBadges[i] = BuildBadge(button, !enabled ? "UNAVAILABLE" : unlocked ? "SELECTED" : "LOCKED", !unlocked);

                if (!unlocked)
                {
                    button.SetInteractable(false);

                    // A locked or unavailable row keeps its badge on permanently: the state
                    // is not a selection cue.
                    _weaponBadges[i].SetActive(true);
                }
            }
        }

        /// <summary>Row height reserved under the detail line for the three core-branch glyphs.</summary>
        private const float CoreBranchRowHeight = 30f;
        private const float CoreGlyphSize = 26f; // AD v003 MUST-4: >= 24 px at 1080p

        /// <summary>
        /// The weapon's three core upgrade branches (WP-034 shop gate) as small glyph tiles
        /// with a "CORE" label: what the round-end shop will offer this weapon beyond the
        /// shared branches. Muted on a locked card.
        /// </summary>
        private static Graphic[] BuildCoreBranchRow(UiButton button, ClassicWeaponDefinition def, float left, bool unlocked)
        {
            var glyphs = new List<Graphic>(3);
            RectTransform row = UiFactory.NewRect("CoreBranches", button.transform);
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(1f, 0f);
            row.pivot = new Vector2(0f, 0f);
            row.sizeDelta = new Vector2(0f, UiFactory.ScaledBand(CoreBranchRowHeight));
            row.offsetMin = new Vector2(left, (int)UiSpace.M);
            row.offsetMax = new Vector2(-BadgeWidth, (int)UiSpace.M + UiFactory.ScaledBand(CoreBranchRowHeight));

            TMP_Text label = UiFactory.Label("Label", row, "CORE", UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.sizeDelta = new Vector2(44f, 0f);

            UpgradeBranch[] branches = def.CoreBranches ?? new UpgradeBranch[0];
            UiColor tint = unlocked ? UiColor.TextSecondary : UiColor.TextMuted;
            for (int i = 0; i < branches.Length && i < 3; i++)
            {
                float x = 44f + i * (CoreGlyphSize + (int)UiSpace.S);
                Sprite sprite = UiArt.UpgradeIcon(branches[i]);
                if (sprite != null)
                {
                    Image glyph = UiFactory.NewImage("Core." + branches[i], row, tint, sprite);
                    glyph.preserveAspect = true;
                    RectTransform glyphRect = (RectTransform)glyph.transform;
                    glyphRect.anchorMin = new Vector2(0f, 0.5f);
                    glyphRect.anchorMax = new Vector2(0f, 0.5f);
                    glyphRect.pivot = new Vector2(0f, 0.5f);
                    glyphRect.sizeDelta = new Vector2(CoreGlyphSize, CoreGlyphSize);
                    glyphRect.anchoredPosition = new Vector2(x, 0f);
                    glyphs.Add(glyph);
                }
                else
                {
                    TMP_Text mono = UiFactory.Label("Core." + branches[i], row, UpgradeIcons.Monogram(branches[i]), UiType.Caption, tint, TextAnchor.MiddleLeft);
                    RectTransform monoRect = (RectTransform)mono.transform;
                    monoRect.anchorMin = new Vector2(0f, 0f);
                    monoRect.anchorMax = new Vector2(0f, 1f);
                    monoRect.pivot = new Vector2(0f, 0.5f);
                    monoRect.sizeDelta = new Vector2(30f, 0f);
                    monoRect.anchoredPosition = new Vector2(x, 0f);
                    glyphs.Add(mono);
                }
            }

            return glyphs.ToArray();
        }

        /// <summary>"Rifle · Hitscan" / "Shotgun · Pellet ×8" - the roster's type and shot model, no numbers changed.</summary>
        private static string DescribeModel(ClassicWeaponDefinition def)
        {
            string model = def.ShotModel == WeaponShotModel.Pellet ? "Pellet" : "Hitscan";
            PrototypeConfig config = PrototypeConfig.Active;
            ClassicWeaponSettings settings = config != null ? def.Settings(config) : null;
            if (def.ShotModel == WeaponShotModel.Pellet && settings != null && settings.PelletCount > 1)
            {
                model += " x" + settings.PelletCount;
            }

            return def.TypeClass + " · " + model;
        }

        private static string RosterTitle(ClassicWeaponDefinition def)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            ClassicWeaponSettings settings = config != null ? def.Settings(config) : null;
            string name = settings != null && !string.IsNullOrEmpty(settings.DisplayName) ? settings.DisplayName : SpaceCamelCase(def.TypeClass.ToString());
            return def.RosterKey + " · " + name;
        }

        /// <summary>"HeavyPistol" → "Heavy Pistol": the enum name is a stand-in until the
        /// weapon has a config entry with a display name (build once per card, not per frame).</summary>
        private static string SpaceCamelCase(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                {
                    sb.Append(' ');
                }

                sb.Append(value[i]);
            }

            return sb.ToString();
        }

        private static bool IsInSource(IWeaponRosterSource source, ClassicWeaponId id)
        {
            IReadOnlyList<WeaponRosterEntry> entries = source.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every roster definition in roster order (A, B, C, D...). Falls back to the
        /// three enum ids when the roster is not loaded (a test scene without the assets).
        /// </summary>
        private static List<ClassicWeaponDefinition> RosterForDisplay()
        {
            var list = new List<ClassicWeaponDefinition>();
            IReadOnlyList<ClassicWeaponDefinition> all = WeaponRoster.All;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    // User decision (C-00, 2026-09-17): a roster entry that is not shipped
                    // (D · Enabled=false / no runtime id) is not shown at all - no locked
                    // card, no placeholder. Only what the player can eventually carry.
                    if (all[i] != null && all[i].Enabled && all[i].HasRuntimeId)
                    {
                        list.Add(all[i]);
                    }
                }
            }

            list.Sort((x, y) => x.SortOrder != y.SortOrder ? x.SortOrder.CompareTo(y.SortOrder) : string.CompareOrdinal(x.RosterKey, y.RosterKey));
            if (list.Count > 0)
            {
                return list;
            }

            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                var stub = ScriptableObject.CreateInstance<ClassicWeaponDefinition>();
                stub.Id = id;
                stub.RosterKey = id.ToString();
                stub.Enabled = true;
                list.Add(stub);
            }

            return list;
        }

        private void SelectWeapon(ClassicWeaponId id)
        {
            if (!ProfileService.IsWeaponUnlocked(id))
            {
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

                _weaponButtons[i].SetSelectedChoice(selected);
                _weaponBadges[i].SetActive(unlocked ? selected : true);

                // AD v003 MUST-4: the selected card's core glyphs read in TextPrimary.
                Graphic[] glyphs = _coreGlyphs != null && i < _coreGlyphs.Length ? _coreGlyphs[i] : null;
                if (glyphs != null && unlocked)
                {
                    Color tint = UiTokens.Get(selected ? UiColor.TextPrimary : UiColor.TextSecondary);
                    for (int g = 0; g < glyphs.Length; g++)
                    {
                        if (glyphs[g] != null && glyphs[g].color != tint)
                        {
                            glyphs[g].color = tint;
                        }
                    }
                }
            }
        }

        /// <summary>PROTOTYPE-ONLY stat line. Every number is read live from PrototypeConfig.</summary>
        private static string DescribeWeapon(ClassicWeaponId id)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            ClassicWeaponSettings settings = config != null ? config.Weapons.Find(id) : null;

            if (settings == null)
            {
                // Loading/absent-data state. Named, not blank.
                return "Weapon statistics are unavailable because PrototypeConfig is not loaded.";
            }

            string damage = settings.PelletCount > 1
                ? "DMG " + Value(settings.Damage.ToString("0.#") + " x" + settings.PelletCount)
                : "DMG " + Value(settings.Damage.ToString("0.#"));

            return damage
                   + "  ·  " + Value(settings.FireRate.ToString("0.#")) + " shots/s"
                   + "  ·  MAG " + Value(settings.MagazineSize.ToString())
                   + "  ·  RELOAD " + Value(settings.ReloadTime.ToString("0.#") + "s");
        }

        /// <summary>Stat value in TextPrimary inside a TextSecondary line (TMP rich text; card build only).</summary>
        private static string Value(string text)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(UiTokens.Get(UiColor.TextPrimary)) + ">" + text + "</color>";
        }

        // ================================================================== chrome

        /// <summary>A pill in the top-right of a card. Hidden until the card is chosen.</summary>
        private static GameObject BuildBadge(UiButton button, string text, bool locked = false)
        {
            TMP_Text chip = UiFactory.Chip(
                "Badge",
                button.transform,
                text,
                locked ? UiColor.SurfaceInset : UiColor.TextPrimary, // AD v003 MUST-3: cream, vermilion stays with START RUN
                locked ? UiColor.TextSecondary : UiColor.SurfaceBase,
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
        }

        private static void GoBack()
        {
            UiSound.Play(UiSoundKind.Back);
            PrototypeUiFlow.GoToMainMenu();
        }
    }
}
