using DD.Prototype.Core.Config;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// The screens' one door to imported art (WP-022). Every method answers with the
    /// AP-012 sprite when the theme has it and with the procedural <see cref="UiSprites"/>
    /// shape otherwise, so a screen is written once and looks right in both states.
    ///
    /// Icons are white glyphs: colour is always a <see cref="UiColor"/> token applied at
    /// the call site, which keeps the contrast budget in <see cref="UiTokens"/> the only
    /// place a colour is decided.
    /// </summary>
    public static class UiArt
    {
        // ------------------------------------------------------------------ icons

        public static Sprite Icon(string key)
        {
            UiTheme theme = UiTheme.Active;
            return theme != null ? theme.Icon(key) : null;
        }

        public static bool HasIcon(string key)
        {
            return Icon(key) != null;
        }

        public static Sprite WeaponIcon(ClassicWeaponId id)
        {
            return Icon("Weapon." + id);
        }

        public static Sprite SpecialIcon(SpecialWeaponId id)
        {
            // WP-022H: the AP-014 silhouette (UI/AP014, "Sw.<id>") when the atlas ships it,
            // the AP-012 v002 glyph otherwise (Tide Jewel has no AP-014 model yet).
            Sprite sw = Icon("Sw." + id);
            return sw != null ? sw : Icon("Special." + id);
        }

        public static Sprite UpgradeIcon(UpgradeBranch branch)
        {
            return Icon("Upgrade." + branch);
        }

        /// <summary>
        /// WP-022C: the WP-034 roster names its glyph by <c>HudIconId</c> ("weapon_rifle",
        /// "weapon_carbine", "weapon_shotgun", "weapon_heavypistol", ...). The AP-012 v002
        /// set has three weapon glyphs; this table is the PLACEHOLDER CONTRACT until
        /// AP-013 / AP-012 v003 deliver one per roster key:
        ///   weapon_rifle -> Weapon.AssaultRifle · weapon_carbine -> Weapon.AutoSmg (stand-in)
        ///   weapon_shotgun -> Weapon.CombatShotgun · anything else -> null (monogram tile).
        /// A future atlas that ships "Weapon.<hudIconId>" wins automatically - and since
        /// WP-022F one does: UI/AP013/AP013_weapon_icons_512x128 (silhouettes cut from the
        /// AP-013 side renders, wp-022d/tools/build_ap013_icons.py), so the table below is
        /// only the fallback for a build without that atlas.
        /// </summary>
        public static Sprite RosterGlyph(string hudIconId)
        {
            if (string.IsNullOrEmpty(hudIconId))
            {
                return null;
            }

            Sprite direct = Icon("Weapon." + hudIconId);
            if (direct != null)
            {
                return direct;
            }

            switch (hudIconId)
            {
                case "weapon_rifle": return Icon("Weapon.AssaultRifle");
                case "weapon_carbine":
                case "weapon_smg": return Icon("Weapon.AutoSmg");
                case "weapon_shotgun": return Icon("Weapon.CombatShotgun");
                default: return null;
            }
        }

        // ----------------------------------------------------------------- frames

        /// <summary>A 9-sliced frame sprite; the procedural rounded rect when art is absent.</summary>
        public static Sprite Frame(UiFrame frame)
        {
            UiTheme theme = UiTheme.Active;
            Sprite sprite = theme != null ? theme.Frame("Frame." + frame) : null;
            if (sprite != null)
            {
                return sprite;
            }

            switch (frame)
            {
                case UiFrame.Chip: return UiSprites.Chip;
                case UiFrame.Button: return UiSprites.Control;
                default: return UiSprites.Card;
            }
        }

        /// <summary>True when the frame comes from the atlas (drawn as an ink line over the fill).</summary>
        public static bool HasFrame(UiFrame frame)
        {
            UiTheme theme = UiTheme.Active;
            return theme != null && theme.Frame("Frame." + frame) != null;
        }

        public static Sprite Medal(UiMedal medal)
        {
            UiTheme theme = UiTheme.Active;
            Sprite sprite = theme != null ? theme.Frame("Medal." + medal) : null;
            return sprite != null ? sprite : UiSprites.Ring;
        }

        public static Sprite Reticle(bool ring)
        {
            UiTheme theme = UiTheme.Active;
            Sprite sprite = theme != null ? theme.Frame(ring ? "Reticle.Ring" : "Reticle.Ticks") : null;
            return sprite != null ? sprite : UiSprites.RingThin;
        }

        public static Sprite FocusBrackets()
        {
            UiTheme theme = UiTheme.Active;
            return theme != null ? theme.Frame("Focus.Brackets") : null;
        }

        public static Sprite Divider()
        {
            UiTheme theme = UiTheme.Active;
            return theme != null ? theme.Frame("Ornament.Divider") : null;
        }

        public static Sprite Cursor()
        {
            UiTheme theme = UiTheme.Active;
            return theme != null ? theme.Frame("Cursor.Arrow") : null;
        }

        // ----------------------------------------------------------------- plates

        public static Sprite MenuPlate()
        {
            UiTheme theme = UiTheme.Active;
            return theme != null ? theme.MenuPlate : null;
        }

        /// <summary>AP-010 beacon marker (greyscale); the procedural disc when absent.</summary>
        public static Sprite HudMarker()
        {
            UiTheme theme = UiTheme.Active;
            Sprite sprite = theme != null ? theme.HudMarker : null;
            return sprite != null ? sprite : UiSprites.Disc;
        }

        public static Sprite SkinPreview(int skinIndex)
        {
            UiTheme theme = UiTheme.Active;
            return theme != null ? theme.Skin(skinIndex) : null;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// An icon image sized to <paramref name="size"/>, tinted by a token. When the
        /// atlas has no such icon the image is created disabled, so layout code can keep
        /// the slot without drawing a blank square.
        /// </summary>
        public static Image IconImage(string name, Transform parent, string key, UiColor color, float size)
        {
            Sprite sprite = Icon(key);
            Image image = UiFactory.NewImage(name, parent, color, sprite);
            var rect = (RectTransform)image.transform;
            rect.sizeDelta = new Vector2(size, size);
            image.preserveAspect = true;
            if (sprite == null)
            {
                image.enabled = false;
            }

            return image;
        }

        /// <summary>
        /// Lays the AP-012 ink frame over a card. The fill underneath stays the token
        /// surface (that is what the contrast budget measures); the frame is decoration,
        /// drawn last so it is never covered by content padding.
        /// </summary>
        public static Image FrameOverlay(RectTransform card, UiFrame frame, UiColor color = UiColor.TextSecondary)
        {
            if (!HasFrame(frame))
            {
                return null;
            }

            Image overlay = UiFactory.NewImage("Frame", card, color, Frame(frame));
            UiFactory.Fill((RectTransform)overlay.transform);
            overlay.type = Image.Type.Sliced;
            overlay.fillCenter = false;
            overlay.pixelsPerUnitMultiplier = FramePixelsPerUnitMultiplier;
            overlay.transform.SetAsLastSibling();
            return overlay;
        }

        /// <summary>
        /// Frames are exported at 256 px for a ~180 px on-screen panel edge; the border
        /// is scaled down so a corner ornament reads at roughly 24-28 px.
        /// </summary>
        public const float FramePixelsPerUnitMultiplier = 1.8f;
    }
}
