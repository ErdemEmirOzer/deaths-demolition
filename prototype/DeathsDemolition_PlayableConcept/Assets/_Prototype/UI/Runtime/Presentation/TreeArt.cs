using System.Collections.Generic;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>The AP-042 v002 frames and seals the tree screen draws (<see cref="TreeArt.Frame"/>).</summary>
    public enum TreeFrameKind
    {
        Available = 0,
        Locked = 1,
        Purchased = 2,
        Maximum = 3,
        Synergy = 4,
        Carrier = 5,
        StampPurchased = 6,
        SealLocked = 7,
        SealPurchased = 8,
        SealSynergy = 9,
        TierFiveEdge = 10
    }

    /// <summary>
    /// WP-104 B4 (S-09) · the AP-042 v002 tree art as the tree screen and the Awakening page read it, from
    /// <c>Assets/_Prototype/UI/Art/Resources/UI/Tree/</c> (the HudArt pattern: hand-written metas - Sprite, sRGB, no mip, Bilinear,
    /// Clamp; the three node frames 9-sliced from <c>nine-slice.json</c>; import table in the WP-104 S-09 NOTES). Files are
    /// renamed to the catalog ids on import, so a key resolves without a table here:
    /// <list type="bullet">
    /// <item><c>Nodes/&lt;nodeId&gt;_64|_256</c> - the 63 nodes, the 7 alternative capstones and the 5 other Awakening nodes;</item>
    /// <item><c>Synergy/&lt;TreeSynergy&gt;_64|_256</c> - the 10 "double seal" badges;</item>
    /// <item><c>Branch/&lt;TreeBranch&gt;_64|_128</c> - the 7 branch emblems;</item>
    /// <item><c>Frames/&lt;file&gt;</c>, <c>tier_fog_overlay_1024x512</c>; the parchment is <c>UI/Wallpapers/tree_parchment</c> (Texture2D).</item>
    /// </list>
    /// THE LAYER RULE (HudArt §1): the smallest layer whose content is not smaller than the physical size it is drawn at
    /// (canvas scale × ref px); past the largest, the largest. Every lookup is optional - a missing file returns null and the
    /// screen keeps its procedural shape (no blank square, no id on screen). Theme keys: <c>Tree.&lt;nodeId&gt;</c>,
    /// <c>Tree.Synergy.&lt;name&gt;</c>, <c>Tree.Branch.&lt;name&gt;</c> (UiTheme falls back here, as it does for HudArt).
    /// PIGMENTS (AD-VISUAL §1.2, C-00 decision 4, AP-042 ek1-palette.json): the paper pigments, for tints on the parchment; Speed
    /// is hagane silver and electricity Raiju blue - gold is Economy's alone (AD §1.6, <see cref="IsGoldReserve"/>).
    /// INK ON PAPER: the tree screen is the one light surface of the UI (AP-042 runtime_text_colors: #12100F on the
    /// parchment). Its words use <see cref="InkText"/> / <see cref="InkSecondary"/> on paper plates, its numbers in gold and its
    /// actions on ink plates (<see cref="InkPlate"/>) where the kit's cream words stand. Main thread; loads on first use.
    /// </summary>
    public static class TreeArt
    {
        public const string Folder = "UI/Tree/";
        public const string NodeFolder = Folder + "Nodes/";
        public const string SynergyFolder = Folder + "Synergy/";
        public const string BranchFolder = Folder + "Branch/";
        public const string FrameFolder = Folder + "Frames/";
        /// <summary>
        /// The parchment lives in the wallpaper class (UiArtThemeTests' 10 MB Wallpapers budget; the 12 MB UI class has no room
        /// for a 2560 × 1440 source) - the round-end wallpaper's neighbour.
        /// </summary>
        public const string Parchment = "UI/Wallpapers/tree_parchment";
        public const string FogOverlay = Folder + "tier_fog_overlay_1024x512";

        public const string KeyPrefix = "Tree.";
        public const string SynergyKeyPrefix = "Tree.Synergy.";
        public const string BranchKeyPrefix = "Tree.Branch.";

        /// <summary>The words on the parchment: AP-042's ink (#12100F = the token Sumi900).</summary>
        public const UiColor InkText = UiColor.SurfaceInset;
        /// <summary>Quieter words on the parchment (#34322F, Stone600): ≥ 8:1 on its paper plates.</summary>
        public const UiColor InkSecondary = UiColor.SurfaceRaised;
        /// <summary>The paper plates under words and the fallback paper when the parchment art is absent (#F3EEE3, AP-042 Cream).</summary>
        public const UiColor Paper = UiColor.TextPrimary;
        /// <summary>The ink plates the kit's cream controls stand on (the AP-042 mock's black price field).</summary>
        public const UiColor InkPlate = UiColor.SurfaceInset;

        /// <summary>The paper plate's alpha over the parchment (its darkest ink-wash corner included: TreeArtTests' contrast pair).</summary>
        public const float PaperPlateAlpha = 0.9f;
        /// <summary>The parchment's darkest linear luminance under a word (AP-042 v002 bg, measured at 1920 × 1080: the header's branch corner).</summary>
        public const float ParchmentDarkestLinear = 0.037f;
        /// <summary>The fog over closed tiers (AD §5.3: sumi α 0.35 at its densest).</summary>
        public const float FogAlpha = 0.35f;
        /// <summary>A locked node's icon: grey silhouette at α 0.45 (AD §5.2).</summary>
        public const float LockedIconAlpha = 0.45f;
        /// <summary>The branch wash under a row (AD §5.5 washi rule: α ≤ 0.07).</summary>
        public const float RowWashAlpha = 0.07f;

        private static readonly Color s_shot = Hex(0xC4, 0x36, 0x2B);
        private static readonly Color s_magazine = Hex(0x46, 0x56, 0x7C);
        private static readonly Color s_speed = Hex(0x7F, 0x8C, 0x99);
        private static readonly Color s_fire = Hex(0xE0, 0x70, 0x2A);
        private static readonly Color s_ice = Hex(0x3E, 0x9A, 0xAE);
        private static readonly Color s_shock = Hex(0x5B, 0x7F, 0xD1);
        private static readonly Color s_venom = Hex(0x6E, 0x9A, 0x2E);
        private static readonly Color s_element = Hex(0x4A, 0x47, 0x44);
        private static readonly Color s_chaos = Hex(0x5E, 0x3A, 0x8C);
        private static readonly Color s_survival = Hex(0x4F, 0x8A, 0x63);
        private static readonly Color s_economy = Hex(0xD9, 0xA9, 0x3C);

        private static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>(256, System.StringComparer.Ordinal);
        private static readonly HashSet<string> s_tried = new HashSet<string>(System.StringComparer.Ordinal);
        private static Texture2D s_parchment;
        private static bool s_parchmentTried;

        /// <summary>A branch's paper pigment; the Element branch takes its sub-line's colour (fusion and the capstone: neutral sumi).</summary>
        public static Color Pigment(TreeBranch branch, TreeElement element = TreeElement.None)
        {
            switch (branch)
            {
                case TreeBranch.Shot: return s_shot;
                case TreeBranch.Magazine: return s_magazine;
                case TreeBranch.Speed: return s_speed;
                case TreeBranch.Element:
                    if ((element & TreeElement.Fire) != 0) return s_fire;
                    if ((element & TreeElement.Ice) != 0) return s_ice;
                    if ((element & TreeElement.Shock) != 0) return s_shock;
                    if ((element & TreeElement.Venom) != 0) return s_venom;
                    return s_element;
                case TreeBranch.Chaos: return s_chaos;
                case TreeBranch.Survival: return s_survival;
                default: return s_economy;
            }
        }

        /// <summary>The world/UI gold reserve test of AD §1.6 / A17: hue 35–65°, saturation over 0.30.</summary>
        public static bool IsGoldReserve(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out _);
            float degrees = h * 360f;
            return degrees >= 35f && degrees <= 65f && s > 0.30f;
        }

        // ------------------------------------------------------------------ sprites

        /// <summary>A node's icon drawn at <paramref name="physicalPx"/> screen px (the 64 or the 256 layer); null without AP-042.</summary>
        public static Sprite NodeIcon(string nodeId, float physicalPx)
        {
            return Layered(NodeFolder, nodeId, physicalPx, 64, 256);
        }

        /// <summary>A synergy's "double seal" badge; null without AP-042.</summary>
        public static Sprite SynergyBadge(TreeSynergy synergy, float physicalPx)
        {
            return synergy == TreeSynergy.None ? null : Layered(SynergyFolder, synergy.ToString(), physicalPx, 64, 256);
        }

        /// <summary>A branch emblem (the row label's 48 px glyph); null without AP-042.</summary>
        public static Sprite BranchEmblem(TreeBranch branch, float physicalPx)
        {
            return Layered(BranchFolder, branch.ToString(), physicalPx, 64, 128);
        }

        public static Sprite Frame(TreeFrameKind kind)
        {
            switch (kind)
            {
                case TreeFrameKind.Available: return Load(FrameFolder + "frame_available_256");
                case TreeFrameKind.Locked: return Load(FrameFolder + "frame_locked_256");
                case TreeFrameKind.Purchased: return Load(FrameFolder + "frame_purchased_256");
                case TreeFrameKind.Maximum: return Load(FrameFolder + "frame_maximum_256");
                case TreeFrameKind.Synergy: return Load(FrameFolder + "frame_synergy_256");
                case TreeFrameKind.Carrier: return Load(FrameFolder + "node_carrier_256");
                case TreeFrameKind.StampPurchased: return Load(FrameFolder + "stamp_purchased_256");
                case TreeFrameKind.SealLocked: return Load(FrameFolder + "seal_locked_128");
                case TreeFrameKind.SealPurchased: return Load(FrameFolder + "seal_purchased_128");
                case TreeFrameKind.SealSynergy: return Load(FrameFolder + "seal_synergy_128");
                default: return Load(FrameFolder + "edge_tier_v_ginpaku_256");
            }
        }

        /// <summary>AP-042's 88 px node frame is drawn from a 256 px source with 52 px borders: 256 / 88 (nine-slice.json).</summary>
        public const float FramePixelsPerUnitMultiplier = 256f / 88f;

        public static Sprite Fog()
        {
            return Load(FogOverlay);
        }

        /// <summary>The 2560 × 1440 parchment (a RawImage, cover-fit); null without AP-042.</summary>
        public static Texture2D ParchmentTexture()
        {
            if (!s_parchmentTried)
            {
                s_parchmentTried = true;
                s_parchment = Resources.Load<Texture2D>(Parchment);
            }

            return s_parchment;
        }

        /// <summary>
        /// The theme keys (UiTheme.Icon falls back here): <c>Tree.&lt;nodeId&gt;</c>, <c>Tree.Synergy.&lt;name&gt;</c>,
        /// <c>Tree.Branch.&lt;name&gt;</c> at the 64 px layer's physical size on this screen. Null for any other key.
        /// </summary>
        public static Sprite Icon(string key)
        {
            if (key == null || key.Length <= KeyPrefix.Length || !key.StartsWith(KeyPrefix, System.StringComparison.Ordinal))
            {
                return null;
            }

            float physical = TreeV2Layout.Icon * HudArt.CanvasScale;
            if (key.StartsWith(SynergyKeyPrefix, System.StringComparison.Ordinal))
            {
                return Layered(SynergyFolder, key.Substring(SynergyKeyPrefix.Length), physical, 64, 256);
            }

            if (key.StartsWith(BranchKeyPrefix, System.StringComparison.Ordinal))
            {
                return Layered(BranchFolder, key.Substring(BranchKeyPrefix.Length), physical, 64, 128);
            }

            return Layered(NodeFolder, key.Substring(KeyPrefix.Length), physical, 64, 256);
        }

        /// <summary>
        /// The node id a snapshot icon key names: "Tree.&lt;id&gt;" → id. B1 currently writes the AP-042 file name ("atis_I_…png")
        /// into the key - the contract says "Tree.&lt;NodeId&gt;" (S-04 ek istek) - so anything else falls back to the node's own id.
        /// </summary>
        public static string NodeIdOf(string iconKey, string nodeId)
        {
            if (!string.IsNullOrEmpty(iconKey) && iconKey.StartsWith(KeyPrefix, System.StringComparison.Ordinal) &&
                !iconKey.StartsWith(SynergyKeyPrefix, System.StringComparison.Ordinal) && iconKey.Length > KeyPrefix.Length)
            {
                return iconKey.Substring(KeyPrefix.Length);
            }

            return nodeId;
        }

        private static Sprite Layered(string folder, string name, float physicalPx, int small, int large)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            bool useSmall = physicalPx <= small + 0.01f;
            Sprite first = Load(folder + name + "_" + (useSmall ? small : large));
            return first != null ? first : Load(folder + name + "_" + (useSmall ? large : small));
        }

        private static Sprite Load(string path)
        {
            if (s_sprites.TryGetValue(path, out Sprite sprite))
            {
                return sprite;
            }

            if (s_tried.Contains(path))
            {
                return null;
            }

            s_tried.Add(path);
            sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
            {
                s_sprites[path] = sprite;
            }

            return sprite;
        }

        private static Color Hex(byte r, byte g, byte b)
        {
            return new Color32(r, g, b, 255);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_sprites.Clear();
            s_tried.Clear();
            s_parchment = null;
            s_parchmentTried = false;
        }
    }
}
