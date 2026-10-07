using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-094 HUD v4 · the AP-032 v001 HUD art (P-07 → S-09, AD ACCEPT 08.10; K/wp-094/AP-032-034-SPEK.md §3, §7) as the HUD
    /// reads it, from <c>Assets/_Prototype/UI/Art/Resources/UI/Hud/</c> (hand-written metas: Sprite, sRGB, no mip, Clamp; the
    /// smear's 3-slice borders from <c>smear/ap032_smear.json</c>). Every lookup is optional - a missing file returns null and
    /// the HUD keeps its pre-AP-032 path (G1 glyph, TMP numeral, AP-012 brush, the kit's hanko mask).
    /// - THE LAYER RULE (§1): of the shipped layers, the smallest whose content is not smaller than the physical size it is
    ///   drawn at (canvas scale × ref px); past the largest, the largest. Gold body 48 / 96 (64 / 128 canvases), Roman cap
    ///   80 / 160 (the 512 / 1024 atlases), smear height 128 / 256.
    /// - GOLD (§3.1): the full-colour koban base replaces the G1 <c>Hud.Gold</c> glyph on the HUD (the glyph stays the
    ///   single-colour symbol everywhere else). Its glint is the existing UiFx Leaf band; data / normal need no new mode.
    /// - ROMAN (§3.2): the 13 atlas cells (I×3 V×2 X×3 L×2 C×2 + the seal) as Sprites cut once per layer from the base atlas
    ///   (Sprite.Create on the cell rects of <c>roman/ap032_roman_metrics.json</c>: 4 × 4 cells, rows I_a I_b I_c V_a / V_b X_a
    ///   X_b X_c / L_a L_b C_a C_b / seal) - bound to the theme keys <c>Roman.I … Roman.C</c> and <c>Roman.Seal</c> through
    ///   <see cref="Icon"/> (UiTheme falls back here for them). The alternates are the numeral's to pick (HudRoundNumeral).
    /// - SMEAR (§3.3): the 3-slice plate, centre Tiled (period = the plate's height), laid out by <see cref="LaySmear"/>.
    /// Loads on first use and caches in fixed arrays; nothing allocates after that. Main thread only.
    /// </summary>
    public static class HudArt
    {
        public const string Folder = "UI/Hud/";
        public const string GoldSmall = "ap032_gold_64_base";
        public const string GoldLarge = "ap032_gold_128_base";
        public const string RomanSmall = "ap032_roman_atlas_512_base";
        public const string RomanLarge = "ap032_roman_atlas_1024_base";
        public const string SmearSmall = "ap032_smear_512x128_mask";
        public const string SmearLarge = "ap032_smear_1024x256_mask";

        /// <summary>The layers' content sizes (§1 table): gold body, Roman cap, smear height - small then large.</summary>
        public const float GoldBodySmall = 48f;
        public const float GoldBodyLarge = 96f;
        public const float RomanCapSmall = 80f;
        public const float RomanCapLarge = 160f;
        public const float SmearHeightSmall = 128f;
        public const float SmearHeightLarge = 256f;

        /// <summary>The gold canvas holds its body at 75 % (192 / 96 / 48 of 256 / 128 / 64): the Image is the body / 0.75.</summary>
        public const float GoldBodyShare = 0.75f;

        /// <summary>The Roman cell (1.6 em square: cell 256 / cap 160) and its baseline over the cell's bottom (48 / 160 em).</summary>
        public const float RomanCellEm = 1.6f;
        public const float RomanBaselineEm = 0.3f;

        /// <summary>Cells in atlas order (row-major from the top-left): the first cell of each letter I V X L C, its alternates, the seal.</summary>
        public static readonly int[] RomanFirstCell = { 0, 3, 5, 8, 10 };
        public static readonly int[] RomanAlternateCount = { 3, 2, 3, 2, 2 };
        public const int RomanSealCell = 12;
        public const int RomanCells = 13;

        /// <summary>The seal ring's outer diameter in the cell: 72 of 256 (0.45 em); the Image is the whole cell.</summary>
        public const float SealRingShareOfCell = 72f / 256f;

        // ---- the smear plate (x1 texels; smear/ap032_smear.json "128" layer - the x2 one is the same × 2)

        public const float SmearNativeWidth = 512f;
        public const float SmearNativeHeight = 128f;
        public const float SmearHead = 96f;
        public const float SmearTail = 160f;
        public const float SmearPeriod = 128f;
        public const float SmearDenseLeft = 35f;
        public const float SmearDenseRight = 382f;

        /// <summary>The glyphs' baseline from the plate's top (0.62 of its height).</summary>
        public const float SmearBaselineFromTop = 0.62f;

        /// <summary>
        /// Cap height / plate height. P-07 proposes 0.375 (the plate ≈ 2.7 × cap); the AD allows up to 0.42 "if the top right
        /// reads heavy" (AP032-v001-AD.md S-09 note 2). 08.10 (S-09: "plaka yüksekliği" lever, the 1.5 % coverage cap with
        /// XIV / XXXVIII - HudCoverageTests): 0.46 - the largest ratio at which the plate's dense band (rows 25–84 of 128,
        /// 0.461 of its height) still holds the whole cap, so the letters never leave the band (legibility unchanged); the plate
        /// is 9 % shorter than at 0.42. AD to confirm at delivery.
        /// </summary>
        public const float SmearCapRatio = 0.46f;

        /// <summary>
        /// 08.10 (S-09): the plate is at most this wide (ref px) - inside S-09's 320 ceiling, at 288 so XXXVIII (two centre
        /// periods) keeps the coverage cap (estimate 1.39 % against 1.54 % at 320); a longer numeral's cap shrinks to fit it,
        /// down to <see cref="CapFloor"/> (past the floor the plate may run longer - LXXXVIII and up).
        /// </summary>
        public const float PlateMaxWidth = 288f;
        public const float PlateCeiling = 320f;
        public const float CapFloor = 32f;

        /// <summary>The seal: 0.45 em, its left edge 0.22 em after the last glyph's advance, its centre 0.55 diameters left of the dense zone's right end.</summary>
        public const float SealEm = 0.45f;
        public const float SealGapEm = 0.22f;
        public const float SealFromDenseRight = 0.55f;

        private static readonly Sprite[] s_gold = new Sprite[2];
        private static readonly Sprite[] s_smear = new Sprite[2];
        private static readonly Sprite[][] s_roman = new Sprite[2][];
        private static readonly bool[] s_goldTried = new bool[2];
        private static readonly bool[] s_smearTried = new bool[2];
        private static readonly bool[] s_romanTried = new bool[2];

        /// <summary>
        /// The HUD canvas's scale for this screen: the CanvasScaler's Expand at the 1920 × 1080 reference divided by the UI
        /// scale (min of the two ratios) - 1 at 1920 × 1080, 1.89 on the Mac at UI 1.2.
        /// </summary>
        public static float CanvasScale
        {
            get
            {
                float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
                Vector2 reference = UiTokens.ReferenceResolution;
                return Mathf.Min(w / reference.x, h / reference.y) * Mathf.Max(0.1f, UiSettings.UiScale);
            }
        }

        /// <summary>§1: 0 for the small layer when its content covers <paramref name="physical"/> px, else 1 (the large one, past it too).</summary>
        public static int Layer(float physical, float smallContent)
        {
            return physical <= smallContent + 0.01f ? 0 : 1;
        }

        // ================================================================== gold

        /// <summary>The koban for a body drawn at <paramref name="bodyPhysicalPx"/> screen px; null without AP-032.</summary>
        public static Sprite GoldCoin(float bodyPhysicalPx)
        {
            int layer = Layer(bodyPhysicalPx, GoldBodySmall);
            Sprite sprite = Gold(layer);
            return sprite != null ? sprite : Gold(1 - layer);
        }

        private static Sprite Gold(int layer)
        {
            if (!s_goldTried[layer] || s_gold[layer] == null)
            {
                s_goldTried[layer] = true;
                s_gold[layer] = Resources.Load<Sprite>(Folder + (layer == 0 ? GoldSmall : GoldLarge));
            }

            return s_gold[layer];
        }

        // ================================================================== Roman

        /// <summary>True when both atlases are in the build (tests).</summary>
        public static bool RomanAuthored
        {
            get { return Cells(0) != null && Cells(1) != null; }
        }

        /// <summary>The alternates of letter <paramref name="kind"/> (I V X L C = 0 … 4) for a cap drawn at <paramref name="capPhysicalPx"/>; null without AP-032.</summary>
        public static Sprite[] RomanAlternates(int kind, float capPhysicalPx, Sprite[] into)
        {
            Sprite[] cells = CellsFor(capPhysicalPx);
            if (cells == null || kind < 0 || kind >= RomanFirstCell.Length)
            {
                return null;
            }

            int n = RomanAlternateCount[kind];
            Sprite[] result = into != null && into.Length == n ? into : new Sprite[n];
            for (int i = 0; i < n; i++)
            {
                result[i] = cells[RomanFirstCell[kind] + i];
            }

            return result;
        }

        /// <summary>The seal cell (a white ring mask, tinted vermilion by the HUD); null without AP-032.</summary>
        public static Sprite RomanSeal(float capPhysicalPx)
        {
            Sprite[] cells = CellsFor(capPhysicalPx);
            return cells != null ? cells[RomanSealCell] : null;
        }

        /// <summary>
        /// The theme keys AP-032 binds (UiTheme.Icon falls back here): <c>Roman.I … Roman.C</c> (alternate a) and
        /// <c>Roman.Seal</c>, at the layer the HUD's 44 px cap takes on this screen. Null for any other key.
        /// </summary>
        public static Sprite Icon(string key)
        {
            if (key == null || key.Length < 7 || !key.StartsWith("Roman.", System.StringComparison.Ordinal))
            {
                return null;
            }

            float cap = RunHudScreen.RoundGlyphHeight * CanvasScale;
            if (key.Length == 10 && key.EndsWith("Seal", System.StringComparison.Ordinal))
            {
                return RomanSeal(cap);
            }

            int kind = key.Length == 7 ? HudRoundNumeral.KindOf(key[6]) : -1;
            Sprite[] cells = kind >= 0 ? CellsFor(cap) : null;
            return cells != null ? cells[RomanFirstCell[kind]] : null;
        }

        private static Sprite[] CellsFor(float capPhysicalPx)
        {
            int layer = Layer(capPhysicalPx, RomanCapSmall);
            Sprite[] cells = Cells(layer);
            return cells != null ? cells : Cells(1 - layer);
        }

        /// <summary>The 13 cells of one atlas, cut once (Sprite.Create on the metrics' cell rects, bottom-left origin).</summary>
        private static Sprite[] Cells(int layer)
        {
            if (s_romanTried[layer])
            {
                return s_roman[layer];
            }

            s_romanTried[layer] = true;
            var atlas = Resources.Load<Texture2D>(Folder + (layer == 0 ? RomanSmall : RomanLarge));
            if (atlas == null)
            {
                return null;
            }

            float cell = atlas.width / 4f;
            var cells = new Sprite[RomanCells];
            for (int i = 0; i < RomanCells; i++)
            {
                int col = i % 4, row = i / 4;
                var rect = new Rect(col * cell, atlas.height - (row + 1) * cell, cell, cell);
                Sprite sprite = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = CellName(i);
                sprite.hideFlags = HideFlags.DontSave;
                cells[i] = sprite;
            }

            s_roman[layer] = cells;
            return cells;
        }

        /// <summary>A cell's name ("Roman.I_a" … "Roman.Seal") - the metrics' stems, the theme key's form.</summary>
        public static string CellName(int cell)
        {
            if (cell == RomanSealCell)
            {
                return "Roman.Seal";
            }

            for (int kind = RomanFirstCell.Length - 1; kind >= 0; kind--)
            {
                if (cell >= RomanFirstCell[kind])
                {
                    return "Roman." + HudRoundNumeral.Letters[kind] + "_" + (char)('a' + cell - RomanFirstCell[kind]);
                }
            }

            return "Roman.?";
        }

        // ================================================================== smear

        /// <summary>The 3-slice plate for a height drawn at <paramref name="heightPhysicalPx"/>; its native height in <paramref name="nativeHeight"/>. Null without AP-032.</summary>
        public static Sprite Smear(float heightPhysicalPx, out float nativeHeight)
        {
            int layer = Layer(heightPhysicalPx, SmearHeightSmall);
            Sprite sprite = SmearLayer(layer);
            if (sprite == null)
            {
                layer = 1 - layer;
                sprite = SmearLayer(layer);
            }

            nativeHeight = layer == 0 ? SmearHeightSmall : SmearHeightLarge;
            return sprite;
        }

        private static Sprite SmearLayer(int layer)
        {
            if (!s_smearTried[layer])
            {
                s_smearTried[layer] = true;
                s_smear[layer] = Resources.Load<Sprite>(Folder + (layer == 0 ? SmearSmall : SmearLarge));
            }

            return s_smear[layer];
        }

        /// <summary>
        /// §3.3 / smear.json "layout_recommendation" for a numeral <paramref name="runWidth"/> wide (advances + kerning) at cap
        /// <paramref name="cap"/>, its first letter's left side bearing <paramref name="firstLsbEm"/>: the plate is cap /
        /// <see cref="SmearCapRatio"/> tall and L + R + n·period wide - n the fewest periods whose dense zone holds the run, the
        /// 0.22 em gap and the seal (+ 0.02 em); a width on a period multiple cuts the tiled centre on a period boundary, so no
        /// seam. The seal's centre sits 0.55 diameters left of the dense zone's right end; the run ends 0.22 em before the
        /// seal; any spare dense width stays at the head (left). Distances from the plate's right edge, in the cap's units.
        /// </summary>
        public static void LaySmear(float cap, float runWidth, float firstLsbEm, out float width, out float height, out float runRight,
            out float sealCentre, out float sealDiameter)
        {
            height = cap / SmearCapRatio;
            float texel = height / SmearNativeHeight;   // ref px per x1 texel
            float capTexels = cap / texel;
            sealDiameter = SealEm * cap;
            float required = (runWidth / Mathf.Max(1f, cap) + SealGapEm + 1.05f * SealEm - firstLsbEm + 0.02f) * capTexels;
            float denseAtZero = SmearHead + SmearTail - SmearDenseLeft - (SmearNativeWidth - SmearDenseRight);
            int n = Mathf.Max(0, Mathf.CeilToInt((required - denseAtZero) / SmearPeriod - 1e-4f));
            width = (SmearHead + SmearTail + n * SmearPeriod) * texel;
            float tail = (SmearNativeWidth - SmearDenseRight) * texel;
            sealCentre = tail + SealFromDenseRight * sealDiameter;
            runRight = sealCentre + sealDiameter * 0.5f + SealGapEm * cap;
        }

        /// <summary>
        /// The cap a numeral of <paramref name="runEm"/> (advances + kerning, em) takes on the plate: <paramref name="baseCap"/>
        /// unless the plate would pass <see cref="PlateMaxWidth"/>, then smaller in proportion (the plate's width is linear in the
        /// cap - its period count depends on em only), never under <see cref="CapFloor"/>.
        /// </summary>
        public static float FitCap(float baseCap, float runEm, float firstLsbEm)
        {
            float width, height, runRight, sealCentre, seal;
            LaySmear(baseCap, runEm * baseCap, firstLsbEm, out width, out height, out runRight, out sealCentre, out seal);
            if (width <= PlateMaxWidth + 0.01f)
            {
                return baseCap;
            }

            return Mathf.Max(CapFloor, baseCap * PlateMaxWidth / width);
        }

        /// <summary>The glyph's cap centre below the plate's centre (the baseline at 0.62 of the height, the cap above it), px.</summary>
        public static float SmearCapOffset(float height)
        {
            return (0.5f - (SmearBaselineFromTop - SmearCapRatio * 0.5f)) * height;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < 2; i++)
            {
                s_gold[i] = null;
                s_smear[i] = null;
                s_roman[i] = null;
                s_goldTried[i] = false;
                s_smearTried[i] = false;
                s_romanTried[i] = false;
            }
        }
    }
}
