using System;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
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
        private const float WeaponCardHeight = 112f;
        private const float SwatchWidth = 100f;
        private const float WeaponIconSize = 56f;
        private const float BadgeWidth = 160f;
        private const float FooterHeight = 88f;
        private const float TitleBand = 40f;

        private UiButton[] _skinButtons;
        private GameObject[] _skinBadges;

        private UiButton[] _weaponButtons;
        private GameObject[] _weaponBadges;

        private ClassicWeaponId[] _weaponIds;
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

            Text caption = UiFactory.Label(
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

                // Title moves right of the preview + swatch and up into its own band.
                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(PreviewSlotWidth + SwatchWidth + (int)UiSpace.XL + (int)UiSpace.L, TitleBand);
                labelRect.offsetMax = new Vector2(-BadgeWidth, -(int)UiSpace.L);
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAnchor.LowerLeft;

                Text detail = UiFactory.Label(
                    "Detail", button.transform, "Cosmetic only", UiType.Caption, UiColor.TextSecondary,
                    TextAnchor.UpperLeft);
                RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
                detailRect.offsetMin = new Vector2(PreviewSlotWidth + SwatchWidth + (int)UiSpace.XL + (int)UiSpace.L, (int)UiSpace.L);
                detailRect.offsetMax = new Vector2(-BadgeWidth, -(TitleBand + (int)UiSpace.L));

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

        private void BuildWeaponColumn(RectTransform column)
        {
            var stack = new UiStack(column);

            stack.Add(UiFactory.Heading("CLASSIC WEAPON", column), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.S);

            Text caption = UiFactory.Label(
                "WeaponCaption", column,
                "Round-end upgrades transform the weapon you take in. You never pick up another one.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)caption.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

            stack.Gap(UiSpace.L);

            _weaponIds = (ClassicWeaponId[])Enum.GetValues(typeof(ClassicWeaponId));
            _weaponButtons = new UiButton[_weaponIds.Length];
            _weaponBadges = new GameObject[_weaponIds.Length];

            for (int i = 0; i < _weaponIds.Length; i++)
            {
                ClassicWeaponId id = _weaponIds[i];
                bool unlocked = ProfileService.IsWeaponUnlocked(id);

                UiButton button = UiFactory.Button(
                    "Weapon." + id, column, ProfileService.WeaponDisplayName(id), UiButtonVariant.Card,
                    () => SelectWeapon(id));

                stack.Add((RectTransform)button.transform, WeaponCardHeight);
                stack.Gap(UiSpace.L);

                // AP-012 weapon glyph at the leading edge (WP-022); muted when locked.
                Image glyph = UiArt.IconImage(
                    "Icon", button.transform, "Weapon." + id,
                    unlocked ? UiColor.TextPrimary : UiColor.TextMuted, WeaponIconSize);
                RectTransform glyphRect = (RectTransform)glyph.transform;
                glyphRect.anchorMin = new Vector2(0f, 0.5f);
                glyphRect.anchorMax = new Vector2(0f, 0.5f);
                glyphRect.pivot = new Vector2(0f, 0.5f);
                glyphRect.anchoredPosition = new Vector2((int)UiSpace.XL, 0f);
                float textLeft = (int)UiSpace.XL + (glyph.enabled ? WeaponIconSize + (int)UiSpace.L : 0f);

                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(textLeft, TitleBand);
                labelRect.offsetMax = new Vector2(-BadgeWidth, -(int)UiSpace.L);
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAnchor.LowerLeft;

                Text detail = UiFactory.Label(
                    "Detail", button.transform,
                    unlocked ? DescribeWeapon(id) : "Reach profile level " + ProfileService.UnlockLevelFor(id) +
                                                    " to unlock this weapon.",
                    UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
                RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
                detailRect.offsetMin = new Vector2(textLeft, (int)UiSpace.L);
                detailRect.offsetMax = new Vector2(-(int)UiSpace.XL, -(TitleBand + (int)UiSpace.L));

                _weaponButtons[i] = button;
                _weaponBadges[i] = BuildBadge(button, unlocked ? "SELECTED" : "LOCKED", !unlocked);

                if (!unlocked)
                {
                    button.SetInteractable(false);

                    // A locked row keeps its badge on permanently: the state is
                    // permanent until the profile levels, so it is not a selection cue.
                    _weaponBadges[i].SetActive(true);
                }
            }
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
        }

        private void RefreshWeapons()
        {
            for (int i = 0; i < _weaponButtons.Length; i++)
            {
                ClassicWeaponId id = _weaponIds[i];
                bool unlocked = ProfileService.IsWeaponUnlocked(id);
                bool selected = unlocked && id == PrototypeUiFlow.SelectedWeapon;

                _weaponButtons[i].SetSelectedChoice(selected);
                _weaponBadges[i].SetActive(unlocked ? selected : true);
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
                ? "DMG " + settings.Damage.ToString("0.#") + " x" + settings.PelletCount
                : "DMG " + settings.Damage.ToString("0.#");

            return damage
                   + "  ·  " + settings.FireRate.ToString("0.#") + " shots/s"
                   + "  ·  MAG " + settings.MagazineSize
                   + "  ·  RELOAD " + settings.ReloadTime.ToString("0.#") + "s";
        }

        // ================================================================== chrome

        /// <summary>A pill in the top-right of a card. Hidden until the card is chosen.</summary>
        private static GameObject BuildBadge(UiButton button, string text, bool locked = false)
        {
            Text chip = UiFactory.Chip(
                "Badge",
                button.transform,
                text,
                locked ? UiColor.SurfaceInset : UiColor.AccentPrimary,
                locked ? UiColor.TextSecondary : UiColor.TextOnAccent,
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
            RectTransform backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.sizeDelta = new Vector2(240f, UiTokens.TargetPreferred);
            backRect.anchoredPosition = Vector2.zero;

            UiButton start = UiFactory.Button(
                "Start", footer, "START RUN", UiButtonVariant.Primary, PrototypeUiFlow.StartRun);
            _startButton = start;
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
