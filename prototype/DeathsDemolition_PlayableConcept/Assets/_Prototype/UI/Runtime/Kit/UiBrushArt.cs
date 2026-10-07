using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>WP-098A · AP-034 brush-kit masks (AP-032-034-SPEK §4.2: ap034_kit_*_mask, white, tinted by token).</summary>
    public enum UiBrushSprite
    {
        WashTall,
        WashWide,
        WashSquare,
        TitleStroke,
        SelectShort,
        SelectLong,
        Divider,
        Corner,
        SliderHead,
        TogglePill,
        SealRing,
        TooltipWash,
        TooltipPointer,
        ScrollThumb
    }

    /// <summary>
    /// WP-098A (KIT-KILAVUZU §4.5, S-09 S6) · resolves AP-034's sprite once, else its code fallback - never null. AP-034 lands
    /// its masks in <c>Assets/_Prototype/UI/Art/Resources/UI/Brush/</c> (slices from <c>kit/ap034_kit.json</c>, S-09's importer);
    /// until then every id falls back to an AP-012 ink mask or a procedural <see cref="UiSprites"/> shape, so a screen built
    /// on the kit changes nothing when the art arrives. The colour is always the code's (white masks, token tint).
    /// Lookups are cached in fixed arrays (no allocation after the first call per id).
    /// </summary>
    public static class UiBrushArt
    {
        public const string Folder = "UI/Brush/";   // Assets/_Prototype/UI/Art/Resources/UI/Brush/ (S-09 07.10, S6)

        private const int Count = (int)UiBrushSprite.ScrollThumb + 1;

        private static readonly string[] s_files =
        {
            "ap034_kit_c02wash_tall_256x512_mask",
            "ap034_kit_c02wash_wide_512x256_mask",
            "ap034_kit_c02wash_square_512x512_mask",
            "ap034_kit_title_1024x32_mask",
            "ap034_kit_c08short_64x32_mask",
            "ap034_kit_c08long_512x32_mask",
            "ap034_kit_c06div_512x32_mask",
            "ap034_kit_c07corner_32x32_mask",
            "ap034_kit_c14head_32x32_mask",
            "ap034_kit_c15toggle_64x32_sdf",
            "ap034_kit_c23ring_128_mask",
            "ap034_kit_c27tip_128x64_mask",
            "ap034_kit_c27ptr_32x16_mask",
            "ap034_kit_c35thumb_16x64_mask"
        };

        private static readonly Sprite[] s_sprites = new Sprite[Count];
        private static readonly bool[] s_authored = new bool[Count];
        private static readonly bool[] s_resolved = new bool[Count];

        /// <summary>The sprite: AP-034's file when present, else the fallback; cached in a fixed array.</summary>
        public static Sprite Get(UiBrushSprite id)
        {
            int i = (int)id;
            if (i < 0 || i >= Count)
            {
                return UiSprites.Solid;
            }

            // A procedural fallback is HideAndDontSave and survives scenes; the check still covers an asset unloaded under us.
            if (!s_resolved[i] || s_sprites[i] == null)
            {
                Resolve(i);
            }

            return s_sprites[i];
        }

        /// <summary>
        /// The C2 wash whose aspect is nearest <paramref name="size"/> (tall 1:2 / square / wide 2:1, nearest in log aspect;
        /// the masks are sliceless and stretch freely).
        /// </summary>
        public static Sprite Wash(Vector2 size)
        {
            float w = Mathf.Max(1f, size.x), h = Mathf.Max(1f, size.y);
            float aspect = w / h;
            const float Split = 1.41421356f;   // √2: halfway between 1 and 2 in log aspect
            if (aspect >= Split)
            {
                return Get(UiBrushSprite.WashWide);
            }

            return aspect <= 1f / Split ? Get(UiBrushSprite.WashTall) : Get(UiBrushSprite.WashSquare);
        }

        /// <summary>True when AP-034's file is in the build (captures, tests).</summary>
        public static bool IsAuthored(UiBrushSprite id)
        {
            int i = (int)id;
            if (i < 0 || i >= Count)
            {
                return false;
            }

            Get(id);
            return s_authored[i];
        }

        /// <summary>The Resources name AP-034 delivers ("ap034_kit_c08short_64x32_mask" …).</summary>
        public static string FileName(UiBrushSprite id)
        {
            int i = (int)id;
            return i >= 0 && i < Count ? s_files[i] : string.Empty;
        }

        /// <summary>Which ink time map orders this mark's InkReveal (Underline: strokes; Divider: rules and the edge).</summary>
        public static UiInkChannel RevealChannel(UiBrushSprite id)
        {
            return id == UiBrushSprite.Divider ? UiInkChannel.Divider : UiInkChannel.Underline;
        }

        private static void Resolve(int i)
        {
            s_resolved[i] = true;
            Sprite authored = Resources.Load<Sprite>(Folder + s_files[i]);
            s_authored[i] = authored != null;
            s_sprites[i] = authored != null ? authored : Fallback((UiBrushSprite)i);
        }

        /// <summary>KIT-KILAVUZU §4.5 "Kod yedeği": the AP-012 ink mask of the same stroke, else a procedural shape; never null.</summary>
        private static Sprite Fallback(UiBrushSprite id)
        {
            Sprite sprite = null;
            switch (id)
            {
                case UiBrushSprite.WashTall:
                case UiBrushSprite.WashWide:
                case UiBrushSprite.WashSquare:
                case UiBrushSprite.TooltipWash:
                    sprite = UiSprites.SoftShadow;
                    break;
                case UiBrushSprite.TitleStroke:
                case UiBrushSprite.SelectShort:
                case UiBrushSprite.SelectLong:
                    sprite = UiFxGlobals.InkMask(UiInkChannel.Underline);
                    break;
                case UiBrushSprite.Divider:
                    sprite = UiFxGlobals.InkMask(UiInkChannel.Divider);
                    break;
                case UiBrushSprite.Corner:
                    sprite = UiSprites.FocusBracketsInk;
                    break;
                case UiBrushSprite.SliderHead:
                    sprite = UiSprites.Disc;
                    break;
                case UiBrushSprite.TogglePill:
                case UiBrushSprite.ScrollThumb:
                    sprite = UiSprites.Chip;
                    break;
                case UiBrushSprite.SealRing:
                    sprite = UiSprites.EnsoBrush;
                    break;
                case UiBrushSprite.TooltipPointer:
                    sprite = UiSprites.Chevron;
                    break;
            }

            return sprite != null ? sprite : UiSprites.Solid;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < Count; i++)
            {
                s_sprites[i] = null;
                s_authored[i] = false;
                s_resolved[i] = false;
            }
        }
    }
}
