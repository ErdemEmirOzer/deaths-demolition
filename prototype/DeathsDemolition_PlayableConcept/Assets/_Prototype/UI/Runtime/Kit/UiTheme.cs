using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>Which 9-sliced frame a surface asks for. Every entry maps to one AP-012 sprite.</summary>
    public enum UiFrame
    {
        HudPanel,
        Button,
        Card,
        Chip
    }

    /// <summary>Achievement / unlock medallion rarity (three AP-012 rings).</summary>
    public enum UiMedal
    {
        Common,
        Rare,
        Epic
    }

    /// <summary>
    /// The one UI theme asset (WP-022, ADR-022-1): every imported UI sprite the prototype
    /// draws, plus the two player-facing scale knobs. Lives at
    /// <c>Assets/_Prototype/UI/Art/Resources/UI/UiTheme.asset</c> and is built by
    /// <c>UiThemeBuilder</c> from the AP-012 atlases; the colour and type tokens stay in
    /// <see cref="UiTokens"/> because their contrast ratios are measured there.
    ///
    /// Everything is optional: a missing sprite falls back to the procedural
    /// <see cref="UiSprites"/> shape, so a screen never depends on art being present -
    /// the same rule WP-005 set for the upgrade icons.
    /// </summary>
    [CreateAssetMenu(fileName = "UiTheme", menuName = "DD/UI Theme")]
    public sealed class UiTheme : ScriptableObject
    {
        /// <summary>Resources path (without extension) the runtime loads the theme from.</summary>
        public const string ResourcePath = "UI/UiTheme";

        /// <summary>Resources folder holding the AP-012 atlases and plates.</summary>
        public const string AtlasFolder = "UI/AP012/";

        public const string IconAtlas = AtlasFolder + "AP012_icons_1024x768";
        public const string FrameAtlas = AtlasFolder + "AP012_frames_1024";
        public const string MenuPlateResource = AtlasFolder + "AP012_menu_plate_1024x576";
        public const string HudMarkerResource = AtlasFolder + "AP012_hud_marker_256";
        public const string SkinResourcePrefix = AtlasFolder + "AP012_skin";

        /// <summary>WP-022F: AP-013 weapon glyphs (silhouettes from the AP-013 side renders), keyed <c>Weapon.&lt;HudIconId&gt;</c>.</summary>
        public const string WeaponGlyphAtlas = "UI/AP013/AP013_weapon_icons_512x128";

        /// <summary>WP-022H: AP-014 Special Weapon glyphs (hand-model silhouettes), keyed <c>Sw.&lt;SpecialWeaponId&gt;</c>.</summary>
        public const string SpecialGlyphAtlas = "UI/AP014/AP014_sw_icons_256x128";

        /// <summary>WP-022L: AP-012 v003 status / threat / menu glyphs (Status.Netted, Threat.Surrounded, Menu.Accessibility ...).</summary>
        public const string StatusGlyphAtlas = AtlasFolder + "AP012_status_icons_1024x512";

        /// <summary>UI scale the player may choose (settings). Clamped to this range.</summary>
        public const float UiScaleMin = 0.9f;
        public const float UiScaleMax = 1.2f;

        [Serializable]
        public struct NamedSprite
        {
            public string Name;
            public Sprite Sprite;
        }

        [Tooltip("Icon sprites by manifest name (Hud.Gold, Upgrade.Piercing, Weapon.AutoSmg ...). White glyphs; tinted at runtime.")]
        public List<NamedSprite> Icons = new List<NamedSprite>(32);

        [Tooltip("9-sliced frames and medallions by manifest name (Frame.HudPanel, Medal.Rare, Reticle.Ticks ...).")]
        public List<NamedSprite> Frames = new List<NamedSprite>(12);

        [Tooltip("Main menu backdrop plate (DEC-076 atmosphere, left third darkened for text).")]
        public Sprite MenuPlate;

        [Tooltip("AP-010 HUD beacon marker (greyscale, tinted with the beacon reserve colour at runtime).")]
        public Sprite HudMarker;

        [Tooltip("Skin previews, index = PlayerSkin (AP-006 turnaround front views).")]
        public List<Sprite> SkinPreviews = new List<Sprite>(3);

        [Range(UiScaleMin, UiScaleMax)]
        [Tooltip("Default UI scale for a fresh install. The player's own choice is stored in UiSettings.")]
        public float DefaultUiScale = 1f;

        // =============================================================== runtime access

        private static UiTheme s_active;
        private static bool s_resolved;
        private Dictionary<string, Sprite> _icons;
        private Dictionary<string, Sprite> _frames;

        /// <summary>
        /// The theme in use. Loaded once from Resources; when the asset is absent (a
        /// project without the art import) a transient theme is created and filled
        /// straight from the atlases, and when those are absent too every lookup
        /// returns null and the procedural kit draws instead.
        /// </summary>
        public static UiTheme Active
        {
            get
            {
                if (!s_resolved)
                {
                    s_resolved = true;
                    s_active = Resources.Load<UiTheme>(ResourcePath);
                    if (s_active == null)
                    {
                        s_active = CreateInstance<UiTheme>();
                        s_active.name = "UiTheme (transient)";
                        s_active.hideFlags = HideFlags.HideAndDontSave;
                        s_active.FillFromResources();
                    }
                }

                return s_active;
            }
        }

        /// <summary>Test seam: substitute a theme (null restores the Resources lookup).</summary>
        public static void Override(UiTheme theme)
        {
            s_active = theme;
            s_resolved = theme != null;
        }

        /// <summary>
        /// Fills the sprite lists from the atlases in Resources. Used by the editor
        /// builder and by the transient fallback; safe to call again (lists are rebuilt).
        /// </summary>
        public void FillFromResources()
        {
            Icons.Clear();
            Frames.Clear();
            SkinPreviews.Clear();
            _icons = null;
            _frames = null;

            AddAll(Icons, Resources.LoadAll<Sprite>(IconAtlas));
            AddAll(Icons, Resources.LoadAll<Sprite>(WeaponGlyphAtlas)); // AP-013 roster glyphs (absent → AP-012 stand-ins via UiArt.RosterGlyph)
            AddAll(Icons, Resources.LoadAll<Sprite>(SpecialGlyphAtlas)); // AP-014 Special glyphs (absent → AP-012 Special.<id>)
            AddAll(Icons, Resources.LoadAll<Sprite>(StatusGlyphAtlas)); // AP-012 v003 status/threat/menu glyphs (absent → chips stay text-only)
            AddAll(Frames, Resources.LoadAll<Sprite>(FrameAtlas));
            MenuPlate = Resources.Load<Sprite>(MenuPlateResource);
            HudMarker = Resources.Load<Sprite>(HudMarkerResource);
            for (int i = 1; i <= 3; i++)
            {
                SkinPreviews.Add(Resources.Load<Sprite>(SkinResourcePrefix + i + "_512"));
            }
        }

        private static void AddAll(List<NamedSprite> list, Sprite[] sprites)
        {
            if (sprites == null)
            {
                return;
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null)
                {
                    list.Add(new NamedSprite { Name = sprites[i].name, Sprite = sprites[i] });
                }
            }
        }

        public Sprite Icon(string key)
        {
            if (_icons == null)
            {
                _icons = Index(Icons);
            }

            Sprite sprite;
            return key != null && _icons.TryGetValue(key, out sprite) ? sprite : null;
        }

        public Sprite Frame(string key)
        {
            if (_frames == null)
            {
                _frames = Index(Frames);
            }

            Sprite sprite;
            return key != null && _frames.TryGetValue(key, out sprite) ? sprite : null;
        }

        public Sprite Skin(int index)
        {
            return index >= 0 && index < SkinPreviews.Count ? SkinPreviews[index] : null;
        }

        private static Dictionary<string, Sprite> Index(List<NamedSprite> list)
        {
            var map = new Dictionary<string, Sprite>(list.Count, StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.IsNullOrEmpty(list[i].Name) && list[i].Sprite != null)
                {
                    map[list[i].Name] = list[i].Sprite;
                }
            }

            return map;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
            s_resolved = false;
        }
    }
}
