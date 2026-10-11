using System.Collections.Generic;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Profile;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-106 · Prepare Run's measured layout as plain arithmetic: the screen measures its texts (TMP), these functions decide
    /// the bands, the columns and what yields when the room is short. No canvas, no allocation per call (the tip pool is built
    /// once per screen), so the EditMode suite drives every rule at the sizes the gates use (1080p, the Mac canvas at UI 1.2,
    /// TextScale 1.25) without a scene.
    /// </summary>
    public static class PrepareRunLayout
    {
        // ------------------------------------------------------------------ the page

        /// <summary>The page's three columns on the 1728 grid: run setup 456 · 24 · stage 664 · 24 · weapons 560.</summary>
        public const float SetupShare = 456f / 1728f;
        public const float GutterShare = 24f / 1728f;
        public const float StageShare = 664f / 1728f;
        public const float StageFrom = SetupShare + GutterShare;
        public const float StageTo = StageFrom + StageShare;
        public const float WeaponsFrom = StageTo + GutterShare;

        // ------------------------------------------------------------------ stat cards

        /// <summary>Five cards: damage, fire rate, magazine, range (spread on a pellet weapon), fire mode.</summary>
        public const int StatCount = 5;

        /// <summary>The fourth card on a pellet weapon (the cone matters more than the reach there).</summary>
        public const string SpreadKey = "stat.spread";

        /// <summary>The fifth card: the pack's fire mode word (weapon.&lt;id&gt;.firemode).</summary>
        public const string FireModeKey = "stat.fire_mode";

        private static readonly string[] BulletCards =
        {
            WeaponStatReadout.Damage, WeaponStatReadout.FireRate, WeaponStatReadout.Magazine, WeaponStatReadout.Range, FireModeKey
        };

        private static readonly string[] PelletCards =
        {
            WeaponStatReadout.Damage, WeaponStatReadout.FireRate, WeaponStatReadout.Magazine, SpreadKey, FireModeKey
        };

        /// <summary>The card keys for a weapon, in order (one set for every weapon, so two weapons compare card by card).</summary>
        public static string[] CardKeys(WeaponDefinition def)
        {
            return def != null && def.fire != null && def.fire.pelletCount > 1 && (def.explosive == null || !def.explosive.enabled) ? PelletCards : BulletCards;
        }

        /// <summary>
        /// Cards per row: all five while every card's widest line (<paramref name="need"/>, padding included) fits its share of
        /// <paramref name="width"/>; else three over two, two per row, one per row. A card is never narrower than its words.
        /// </summary>
        public static int StatColumns(float width, float gap, IList<float> need)
        {
            int count = need != null ? need.Count : 0;
            if (count == 0)
            {
                return 1;
            }

            int[] options = s_columnOptions;
            for (int o = 0; o < options.Length; o++)
            {
                int columns = Mathf.Min(options[o], count);
                float cell = CellWidth(width, gap, columns);
                bool fits = true;
                for (int i = 0; i < count && fits; i++)
                {
                    fits = need[i] <= cell;
                }

                if (fits)
                {
                    return columns;
                }
            }

            return 1;
        }

        private static readonly int[] s_columnOptions = { 5, 3, 2, 1 };

        /// <summary>The width of one card when <paramref name="columns"/> share <paramref name="width"/>.</summary>
        public static float CellWidth(float width, float gap, int columns)
        {
            columns = Mathf.Max(1, columns);
            return Mathf.Floor((width - gap * (columns - 1)) / columns);
        }

        public static int Rows(int count, int columns)
        {
            columns = Mathf.Max(1, columns);
            return (count + columns - 1) / columns;
        }

        // ------------------------------------------------------------------ the stage

        /// <summary>The stage card's bands: the gap between blocks, the preview's height (0 = no room for it) and the slack.</summary>
        public struct StageBands
        {
            public float Gap;
            public float Preview;

            /// <summary>Room the capped preview leaves: split above and under it, so the cards and the state line keep the card's foot.</summary>
            public float Slack;
        }

        /// <summary>
        /// The stage, top to bottom: identity · gap · preview · gap · cards · gap · state line. <paramref name="fixedHeight"/> is the
        /// measured text (identity + cards + state); the preview takes what is left, at most <paramref name="previewMax"/> (a tall
        /// 4:3 page centres it in its band - <see cref="StageBands.Slack"/> - instead of drawing a portrait-thin render). When that is under
        /// <paramref name="previewMin"/> the gaps close first (down to <paramref name="gapMin"/>), then the preview takes the rest;
        /// under <paramref name="previewHide"/> it is not drawn (the side view band stands in). The texts never yield.
        /// </summary>
        public static StageBands Stage(float height, float fixedHeight, float gap, float gapMin, float previewMin, float previewHide, float previewMax)
        {
            var bands = new StageBands { Gap = gap };
            float preview = Mathf.Min(previewMax, height - fixedHeight - gap * 3f);
            if (preview < previewMin)
            {
                bands.Gap = Mathf.Max(gapMin, gap - (previewMin - preview) / 3f);
                preview = Mathf.Min(previewMax, height - fixedHeight - bands.Gap * 3f);
            }

            bands.Preview = preview >= previewHide ? Mathf.Floor(preview) : 0f;
            bands.Slack = bands.Preview > 0f ? Mathf.Max(0f, Mathf.Floor(height - fixedHeight - bands.Gap * 3f - bands.Preview)) : 0f;
            return bands;
        }

        // ------------------------------------------------------------------ the run setup column

        /// <summary>What the setup column draws at its height.</summary>
        public struct SetupBands
        {
            public float Gap;
            public float Thumb;
            public bool Tip;
        }

        /// <summary>
        /// The setup column, top to bottom: difficulty (<paramref name="difficultyHeight"/>: heading, rows, mode line) · gap · map
        /// (its caption and name, <paramref name="mapText"/>, around the thumbnail) · gap · tip (<paramref name="tipHeight"/>).
        /// Short of room the gaps close first, then the thumbnail shrinks to <paramref name="thumbMin"/>, then the tip goes,
        /// then the thumbnail goes. The difficulty rows and the map's name always keep their room.
        /// </summary>
        public static SetupBands Setup(float height, float difficultyHeight, float mapText, float tipHeight, float thumbIdeal,
            float thumbMin, float gap, float gapMin)
        {
            var bands = new SetupBands { Gap = gap, Thumb = thumbIdeal, Tip = tipHeight > 0f };
            float Need()
            {
                return difficultyHeight + bands.Gap + mapText + bands.Thumb + (bands.Tip ? bands.Gap + tipHeight : 0f);
            }

            if (Need() > height)
            {
                bands.Gap = Mathf.Max(gapMin, gap - (Need() - height) / 2f);
            }

            if (Need() > height)
            {
                bands.Thumb = Mathf.Max(thumbMin, bands.Thumb - (Need() - height));
            }

            if (Need() > height && bands.Tip)
            {
                bands.Tip = false;
                bands.Thumb = Mathf.Min(thumbIdeal, Mathf.Max(thumbMin, bands.Thumb + (height - Need())));
            }

            if (Need() > height)
            {
                bands.Thumb = 0f;
            }

            bands.Thumb = Mathf.Floor(bands.Thumb);
            return bands;
        }

        /// <summary>
        /// The part of a texture a cover-fit box shows (a RawImage uvRect, origin bottom-left): the full width with a band of the
        /// height around <paramref name="focusY"/> (0 = bottom, 1 = top) when the box is wider than the art, else the centre strip.
        /// </summary>
        public static Rect CoverUv(float textureWidth, float textureHeight, float boxWidth, float boxHeight, float focusY)
        {
            if (textureWidth <= 0f || textureHeight <= 0f || boxWidth <= 0f || boxHeight <= 0f)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            float art = textureWidth / textureHeight;
            float box = boxWidth / boxHeight;
            if (box >= art)
            {
                float h = art / box;
                float y = Mathf.Clamp(focusY - h * 0.5f, 0f, 1f - h);
                return new Rect(0f, y, 1f, h);
            }

            float w = box / art;
            return new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }

        // ------------------------------------------------------------------ the Specials tiles

        /// <summary>
        /// U2 (R2 red: "LOB" cut on the Mac canvas at UI 1.2 × text 1.25): a Special's name and its fire mode share a line only
        /// when both fit beside the side view - the mode counted at its smallest (80 %). Otherwise the tile stacks them.
        /// </summary>
        public static bool SpecialBeside(float tileWidth, float iconWidth, float nameWidth, float modeWidthSmallest, float inset, float gap)
        {
            return inset + iconWidth + gap + nameWidth + gap + modeWidthSmallest + inset <= tileWidth;
        }

        /// <summary>A Special tile's height: one band beside, or two lines and their padding stacked.</summary>
        public static float SpecialTileHeight(bool beside, float minHeight, float nameLine, float modeLine, float pad)
        {
            return beside ? minHeight : Mathf.Max(minHeight, Mathf.Ceil(pad + nameLine + modeLine + pad));
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>The setup row a weapon row's Left lands on: the rows side by side, the lists of different lengths spread evenly.</summary>
        public static int SetupRowFor(int weaponRow, int weaponRows, int setupRows)
        {
            if (setupRows <= 0)
            {
                return -1;
            }

            return Mathf.Clamp(weaponRow * setupRows / Mathf.Max(1, weaponRows), 0, setupRows - 1);
        }

        // ------------------------------------------------------------------ locks

        /// <summary>WP-107: the weapon is bought at its wall during a run and never starts one (rl_1) - its row says WALL BUY.</summary>
        public static bool IsWallBuyOnly(MetaPathRules.WeaponLockInfo info)
        {
            return info.LockReason == MetaLockReason.WallBuyOnly;
        }

        // ------------------------------------------------------------------ the tip

        /// <summary>A tip on this page is one short line: the loading set's tips of at most this many characters (EN).</summary>
        public const int TipMaxChars = 60;

        /// <summary>Tips the R4 run tree contradicts (copy-r4 §2.3): never on this page.</summary>
        public static readonly string[] TipExcluded = { "loading.tip.econ.01", "loading.tip.econ.02", "tips.019" };

        /// <summary>
        /// The page's tips as indices into <see cref="UiLoadingTips.All"/>: the FACT tips of at most <see cref="TipMaxChars"/>,
        /// shortest first, then - while hints are on - the short HINT tips. The loading screen's own rotation is not touched (no
        /// tip is marked shown, no hint bit is spent): this page only previews.
        /// </summary>
        public static int[] TipPool(bool hintsOn)
        {
            var pool = new List<int>(UiLoadingTips.All.Length);
            for (int pass = 0; pass < 2; pass++)
            {
                UiTipClass wanted = pass == 0 ? UiTipClass.Fact : UiTipClass.Hint;
                if (wanted == UiTipClass.Hint && !hintsOn)
                {
                    break;
                }

                int from = pool.Count;
                for (int i = 0; i < UiLoadingTips.All.Length; i++)
                {
                    UiTip tip = UiLoadingTips.All[i];
                    if (tip.Class == wanted && tip.Text != null && tip.Text.Length <= TipMaxChars && System.Array.IndexOf(TipExcluded, tip.Key) < 0)
                    {
                        pool.Add(i);
                    }
                }

                // Shortest first inside the class (stable: equal lengths keep the set's order).
                for (int a = from + 1; a < pool.Count; a++)
                {
                    int item = pool[a];
                    int b = a - 1;
                    while (b >= from && UiLoadingTips.All[pool[b]].Text.Length > UiLoadingTips.All[item].Text.Length)
                    {
                        pool[b + 1] = pool[b];
                        b--;
                    }

                    pool[b + 1] = item;
                }
            }

            return pool.ToArray();
        }

        /// <summary>The tip for the page's <paramref name="visit"/>-th opening this session (0 = the first), or -1 when none qualifies.</summary>
        public static int TipFor(int[] pool, int visit)
        {
            if (pool == null || pool.Length == 0)
            {
                return -1;
            }

            return pool[((visit % pool.Length) + pool.Length) % pool.Length];
        }
    }
}
