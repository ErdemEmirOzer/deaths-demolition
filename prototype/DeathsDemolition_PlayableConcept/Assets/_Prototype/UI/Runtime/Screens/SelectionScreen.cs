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
        private const float SkinCardHeight = 120f;
        private const float WeaponCardHeight = 112f;
        private const float SwatchWidth = 88f;
        private const float BadgeWidth = 160f;
        private const float FooterHeight = 88f;
        private const float TitleBand = 40f;

        private UiButton[] _skinButtons;
        private GameObject[] _skinBadges;

        private UiButton[] _weaponButtons;
        private GameObject[] _weaponBadges;

        private ClassicWeaponId[] _weaponIds;

        protected override void Build()
        {
            // Defensive: a profile edited between the menu and here must not be able to
            // leave a locked weapon selected.
            if (!ProfileService.IsWeaponUnlocked(PrototypeUiFlow.SelectedWeapon))
            {
                PrototypeUiFlow.SelectedWeapon = ProfileService.FirstUnlockedWeapon();
            }

            float headerHeight = BuildHeader(
                "STEP ONE OF ONE",
                "PREPARE YOUR RUN",
                "The skin is cosmetic. The classic weapon is carried for the whole run and cannot be swapped.");

            float top = headerHeight + (int)UiSpace.XXL;

            BuildSkinColumn(Column("Skins", 0f, 0.40f, top, FooterHeight));
            BuildWeaponColumn(Column("Weapons", 0.44f, 1f, top, FooterHeight));
            BuildFooter();

            RefreshSkins();
            RefreshWeapons();
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

            for (int i = 0; i < count; i++)
            {
                var skin = (PlayerSkin)i;
                PlayerSkins.Palette palette = PlayerSkins.Get(skin);

                UiButton button = UiFactory.Button(
                    "Skin." + skin, column, palette.DisplayName, UiButtonVariant.Card,
                    () => SelectSkin(skin));

                stack.Add((RectTransform)button.transform, SkinCardHeight);
                stack.Gap(UiSpace.L);

                BuildSwatch(button, palette);

                // Title moves right of the swatch and up into its own band.
                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2(SwatchWidth + (int)UiSpace.XL + (int)UiSpace.L, TitleBand);
                labelRect.offsetMax = new Vector2(-BadgeWidth, -(int)UiSpace.L);
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAnchor.LowerLeft;

                Text detail = UiFactory.Label(
                    "Detail", button.transform, "Cosmetic only", UiType.Caption, UiColor.TextSecondary,
                    TextAnchor.UpperLeft);
                RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
                detailRect.offsetMin = new Vector2(SwatchWidth + (int)UiSpace.XL + (int)UiSpace.L, (int)UiSpace.L);
                detailRect.offsetMax = new Vector2(-BadgeWidth, -(TitleBand + (int)UiSpace.L));

                _skinButtons[i] = button;
                _skinBadges[i] = BuildBadge(button, "SELECTED");

                if (i == 0)
                {
                    SetInitialFocus(button);
                }
            }
        }

        /// <summary>Three stacked bands of the skin's own palette. No text needed.</summary>
        private static void BuildSwatch(UiButton button, PlayerSkins.Palette palette)
        {
            RectTransform swatch = UiFactory.NewRect("Swatch", button.transform);
            swatch.anchorMin = new Vector2(0f, 0f);
            swatch.anchorMax = new Vector2(0f, 1f);
            swatch.pivot = new Vector2(0f, 0.5f);
            swatch.sizeDelta = new Vector2(SwatchWidth, -((int)UiSpace.L * 2));
            swatch.anchoredPosition = new Vector2((int)UiSpace.L, 0f);

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

                RectTransform labelRect = (RectTransform)button.LabelText.transform;
                labelRect.offsetMin = new Vector2((int)UiSpace.XL, TitleBand);
                labelRect.offsetMax = new Vector2(-BadgeWidth, -(int)UiSpace.L);
                button.LabelText.fontSize = UiTokens.FontSize(UiType.H3);
                button.LabelText.alignment = TextAnchor.LowerLeft;

                Text detail = UiFactory.Label(
                    "Detail", button.transform,
                    unlocked ? DescribeWeapon(id) : "Reach profile level " + ProfileService.UnlockLevelFor(id) +
                                                    " to unlock this weapon.",
                    UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
                RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
                detailRect.offsetMin = new Vector2((int)UiSpace.XL, (int)UiSpace.L);
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
                   + "     " + settings.FireRate.ToString("0.#") + " shots/s"
                   + "     MAG " + settings.MagazineSize
                   + "     RELOAD " + settings.ReloadTime.ToString("0.#") + "s";
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
            RectTransform backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.sizeDelta = new Vector2(240f, UiTokens.TargetPreferred);
            backRect.anchoredPosition = Vector2.zero;

            UiButton start = UiFactory.Button(
                "Start", footer, "START RUN", UiButtonVariant.Primary, PrototypeUiFlow.StartRun);
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
