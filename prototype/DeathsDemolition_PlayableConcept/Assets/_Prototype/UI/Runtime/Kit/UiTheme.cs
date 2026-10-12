using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Which 9-sliced frame a surface asks for ("Frame.&lt;name&gt;" in the A4 frames v3 atlas). Button has no v3
    /// frame (direction §7.1 A4: v3 buttons are drawn by the kit), so it resolves to the procedural control shape.
    /// </summary>
    public enum UiFrame
    {
        HudPanel,
        Button,
        Card,
        Chip,
        /// <summary>WP-068 A4: Elev2 modal, double line + corner accents.</summary>
        Modal,
        /// <summary>WP-068 A4: washi tooltip card (pointer = Frame.TooltipPointer).</summary>
        Tooltip,
        /// <summary>WP-068 A4: list row, bottom hairline only.</summary>
        ListRow,
        /// <summary>WP-068 A4: Toast → Ribbon band with swallow-tail ends.</summary>
        Ribbon,
        /// <summary>WP-068 A4: solid pill (state chips).</summary>
        ChipFill
    }

    /// <summary>Achievement / unlock medallion rarity (M1 medals v3, SDF in G1).</summary>
    public enum UiMedal
    {
        Common,
        Rare,
        Epic,
        Locked
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

        /// <summary>Resources folder holding the AP-012 plates and skin previews.</summary>
        public const string AtlasFolder = "UI/AP012/";

        public const string MenuPlateResource = AtlasFolder + "AP012_menu_plate_1024x576";
        public const string HudMarkerResource = AtlasFolder + "AP012_hud_marker_256";
        public const string SkinResourcePrefix = AtlasFolder + "AP012_skin";

        /// <summary>
        /// WP-068 §7.1 G1: every single-colour glyph in one signed-distance atlas (64² cells, spread 6, Alpha8) - the
        /// AP-012 icons / status, AP-013 weapons, AP-014 Specials, the reticle pair and divider, and the v004 A1–A3 /
        /// V1 / M1 sets and the seal. Drawn through DD/UI/Fx SdfGlyph (<see cref="UiArt.ApplyGlyph"/>).
        /// </summary>
        public const string GlyphAtlas = "UI/G1/DD_G1_glyphs_sdf_1024";

        /// <summary>WP-068 §7.1 A4: Frames v3 (2× authored, pixelsPerUnitMultiplier 2) + the V1 key caps; Alpha8.</summary>
        public const string FrameAtlas = "UI/A4/DD_A4_frames_v3_1024";

        /// <summary>WP-068 V1 hardware cursors: <c>UI/Cursor/Cursor.{Arrow,Press}_{32,64}</c> (RGBA32).</summary>
        public const string CursorFolder = "UI/Cursor/";

        /// <summary>WP-068 A6 kumiko (asanoha) tile, one channel, repeat - the curtain doors only (AD #12).</summary>
        public const string KumikoTileResource = "UI/Tiles/AP012_tile_kumiko_asanoha_256";

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
        private Texture _glyphTexture;

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

            _glyphTexture = null;
            AddAll(Icons, Resources.LoadAll<Sprite>(GlyphAtlas));   // G1: every glyph (absent → the procedural kit / text-only chips)
            AddAll(Frames, Resources.LoadAll<Sprite>(FrameAtlas));  // A4: frames v3, kit sprites, key caps
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
            if (key != null && _icons.TryGetValue(key, out sprite))
            {
                return sprite;
            }

            // WP-094: AP-032 v001's brush letters and seal ("Roman.I" … "Roman.C", "Roman.Seal") are cut from their atlas at
            // runtime (HudArt) rather than baked into this asset - the theme keys resolve all the same.
            // WP-104 B4: AP-042 v002's tree art ("Tree.<nodeId>", "Tree.Synergy.<name>", "Tree.Branch.<name>") loads the same way
            // (TreeArt, Resources/UI/Tree) - the contract's IconKey / BadgeKey resolve through the theme.
            Sprite hud = HudArt.Icon(key);
            return hud != null ? hud : TreeArt.Icon(key);
        }

        /// <summary>True when the sprite is a G1 distance-field glyph (it must be drawn through DD/UI/Fx SdfGlyph).</summary>
        public bool IsGlyph(Sprite sprite)
        {
            if (sprite == null)
            {
                return false;
            }

            if (_glyphTexture == null)
            {
                for (int i = 0; i < Icons.Count && _glyphTexture == null; i++)
                {
                    if (Icons[i].Sprite != null)
                    {
                        _glyphTexture = Icons[i].Sprite.texture;
                    }
                }
            }

            return _glyphTexture != null && sprite.texture == _glyphTexture;
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
