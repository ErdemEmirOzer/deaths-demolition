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
    ///
    /// WP-068 §7.1: glyphs are G1 distance fields - an image showing one draws through DD/UI/Fx SdfGlyph
    /// (<see cref="ApplyGlyph"/>; <see cref="UiFactory.NewImage"/> does it for a glyph sprite, <see cref="SetGlyph"/>
    /// on a swap). Frames are the A4 v3 set, authored 2× (<see cref="FramePixelsPerUnitMultiplier"/>).
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
            Sprite own = Icon("Weapon." + id);
            if (own != null || (int)id <= (int)ClassicWeaponId.CombatShotgun)
            {
                return own;
            }

            // WP-069 B2: a v3 weapon without its own glyph shows its class sibling's until the AP-021 icons.
            return Icon("Weapon." + (ClassicWeaponId)DD.Prototype.Core.Weapons.Arsenal.LegacyPoseIndex(id));
        }

        public static Sprite SpecialIcon(SpecialWeaponId id)
        {
            // WP-022H: the AP-014 silhouette (UI/AP014, "Sw.<id>") when the atlas ships it,
            // the AP-012 v002 glyph otherwise (Tide Jewel has no AP-014 model yet).
            Sprite sw = Icon("Sw." + id);
            return sw != null ? sw : Icon("Special." + id);
        }

        /// <summary>
        /// WP-077 B1 added three weapon-core branches (ArmorBreak, Impact, Cluster) before their G1 glyphs exist: each
        /// borrows the nearest existing glyph until M4-R1 N19 (R1-B, the living icon set) draws its own "Upgrade.*" cell.
        /// A branch with neither is a test failure (UiArtThemeTests).
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<UpgradeBranch, string> UpgradeGlyphStandIns =
            new System.Collections.Generic.Dictionary<UpgradeBranch, string>
            {
                { UpgradeBranch.ArmorBreak, "Ach.ArmorBreaker" },
                { UpgradeBranch.Impact, "Status.Dazed" },
                { UpgradeBranch.Cluster, "Ach.PlateWrecker" },
            };

        public static Sprite UpgradeIcon(UpgradeBranch branch)
        {
            Sprite own = Icon("Upgrade." + branch);
            string standIn;
            return own != null ? own : UpgradeGlyphStandIns.TryGetValue(branch, out standIn) ? Icon(standIn) : null;
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
                case UiFrame.Chip:
                case UiFrame.ChipFill: return UiSprites.Chip;
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

        /// <summary>M1 medal v3 (G1 glyph); the procedural ring when absent.</summary>
        public static Sprite Medal(UiMedal medal)
        {
            Sprite sprite = Icon("Medal." + medal);
            return sprite != null ? sprite : UiSprites.Ring;
        }

        public static Sprite Reticle(bool ring)
        {
            Sprite sprite = Icon(ring ? "Reticle.Ring" : "Reticle.Ticks");
            return sprite != null ? sprite : UiSprites.RingThin;
        }

        public static Sprite FocusBrackets()
        {
            return Kit("Focus.Brackets");
        }

        public static Sprite Divider()
        {
            return Icon("Ornament.Divider");
        }

        /// <summary>
        /// An A4 kit sprite by name: Ring.Progress132 / Ring.Progress64 (Radial360), Slider.Rail / Handle /
        /// HandleFocus, Shadow.Soft, Bar.Segments, Frame.TooltipPointer, Focus.Brackets, Key.Cap / Key.Wide. Null when
        /// absent - the caller keeps its procedural shape. All are 2× authored (<see cref="FramePixelsPerUnitMultiplier"/>).
        /// </summary>
        public static Sprite Kit(string key)
        {
            UiTheme theme = UiTheme.Active;
            return theme != null ? theme.Frame(key) : null;
        }

        /// <summary>V1 key cap, 9-sliced around live TMP text (Key.Wide for a word, Key.Cap for a letter); null when absent.</summary>
        public static Sprite KeyCap(bool wide)
        {
            return Kit(wide ? "Key.Wide" : "Key.Cap");
        }

        // ------------------------------------------------------------- SDF glyphs

        /// <summary>True when the sprite is a G1 distance-field glyph.</summary>
        public static bool IsGlyph(Sprite sprite)
        {
            UiTheme theme = UiTheme.Active;
            return theme != null && theme.IsGlyph(sprite);
        }

        /// <summary>
        /// Draws the image through DD/UI/Fx SdfGlyph (fill only; <paramref name="outlineGlowErosion"/> = the shader's
        /// outline width, glow, erosion). An image already carrying another Fx mode keeps it.
        /// </summary>
        public static void ApplyGlyph(Image image, Vector3 outlineGlowErosion = default)
        {
            if (image == null)
            {
                return;
            }

            UiFx fx = image.GetComponent<UiFx>();
            if (fx == null)
            {
                fx = image.gameObject.AddComponent<UiFx>();
                fx.Configure(UiFxMode.SdfGlyph, 1f, outlineGlowErosion);
                return;
            }

            if (fx.Mode == UiFxMode.SdfGlyph || fx.Mode == UiFxMode.None)
            {
                fx.Configure(UiFxMode.SdfGlyph, 1f, outlineGlowErosion);
                fx.enabled = true;
            }
        }

        /// <summary>A sprite swap that keeps the draw path right: SdfGlyph on for a G1 glyph, off for anything else.</summary>
        public static void SetGlyph(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            if (IsGlyph(sprite))
            {
                ApplyGlyph(image);
                return;
            }

            UiFx fx = image.GetComponent<UiFx>();
            if (fx != null && fx.Mode == UiFxMode.SdfGlyph)
            {
                fx.enabled = false;
            }
        }

        // ------------------------------------------------------- O1 icon material

        /// <summary>
        /// WP-081 ikon v2 B5b · the G1 cells AP-028-B's O1 family drew - the G1 manifest's last merge (B5a r2): 20 replaced in
        /// place + 30 added. Only these sit in the SVG viewBox frame the icon material's constants assume (DD_ICON_SD_AP =
        /// 64 / 52); every other G1 cell has its own frame and stays on SdfGlyph. Ordinal order; UiIconMaterialKeysTests
        /// holds the list to the manifest (a new merge that changes the set fails there first).
        /// </summary>
        public static readonly string[] O1IconKeys =
        {
            "FireMode.Auto", "FireMode.Charge", "FireMode.Lob", "FireMode.Semi", "Menu.Accessibility",
            "Menu.Achievements", "Menu.Audio", "Menu.Back", "Menu.Confirm", "Menu.Controls", "Menu.EndRun",
            "Menu.Gamepad", "Menu.Graphics", "Menu.Home", "Menu.Keyboard", "Menu.Next", "Menu.Palette", "Menu.Path",
            "Menu.Pause", "Menu.Play", "Menu.Previous", "Menu.Profile", "Menu.Quit", "Menu.ReduceFlashes",
            "Menu.Resume", "Menu.Retry", "Menu.Save", "Menu.Settings", "Menu.Statistics", "Menu.TextScale",
            "Menu.Upgrade", "Notice.Info", "Notice.Notification", "Profile.Level", "Profile.Progress", "Profile.Xp",
            "Selection.Selected", "Selection.Skin", "Selection.Weapon", "Settings.Camera", "Settings.Data",
            "Settings.Display", "Settings.Hints", "Settings.Language", "Stat.Damage", "Stat.FireRate", "Stat.Magazine",
            "Stat.Range", "State.Locked", "State.Unlocked"
        };

        private static readonly System.Collections.Generic.HashSet<string> s_o1IconKeys =
            new System.Collections.Generic.HashSet<string>(O1IconKeys, System.StringComparer.Ordinal);

        /// <summary>True for an AP-028-B O1 key (<see cref="O1IconKeys"/>).</summary>
        public static bool IsO1Glyph(string key)
        {
            return key != null && s_o1IconKeys.Contains(key);
        }

        /// <summary>
        /// WP-081 ikon v2 B5b · an icon control's glyph (UiIconButton only, C-00 06.10): an O1 key draws through DD/UI/Fx
        /// Icon (8) at rest - the light-only relief, sheen off, no glow, drawn - lit, or flat when <paramref name="lit"/> is
        /// false (disabled). Any other key, a missing sprite or a non-glyph sprite keeps what <see cref="ApplyGlyph"/> gave
        /// it; the HUD and every other glyph never come here (IK-M18). Returns the Icon driver, or null.
        /// </summary>
        public static UiFx ApplyIconMaterial(Image image, string key, bool lit)
        {
            if (image == null || !IsO1Glyph(key) || !IsGlyph(image.sprite))
            {
                return null;
            }

            UiFx fx = image.GetComponent<UiFx>();
            if (fx == null)
            {
                fx = image.gameObject.AddComponent<UiFx>();
            }

            fx.ConfigureIcon(lit);
            fx.enabled = true;
            return fx;
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

        /// <summary>A4 frames v3 are authored at 2× (a Card corner = 34 px in the atlas, 17 on screen).</summary>
        public const float FramePixelsPerUnitMultiplier = 2f;
    }
}
