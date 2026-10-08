using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-081 ikon v2 B5b · the C# twin of <c>DD_IconShade</c> (DD_UiFx.hlsl, DD/UI/Fx Icon mode 8): the same constants,
    /// the same order of operations, so EditMode holds the shader's maths to AP-028-B's reference <c>render()</c>
    /// (AP-028 04-tools/build_B_icons.py:110-134 - the reference code is the source, C-00 06.10) without a GPU, as
    /// <see cref="UiEaseCurve"/> twins DD_CubicBezier. Nothing at runtime calls it.
    /// Units: AP = AP-028's 64-unit SVG viewBox = the G1 52² sprite box (B5a family frame [0, 256]); 1 G1 px = 64 / 52 AP;
    /// S / M / L (24 / 32 / 40 px) draw 2.667 / 2.0 / 1.6 AP per screen pixel (WP-081-ICON-V2-DIRECTION §1.2).
    /// Colours: the tint comes in linear (the vertex colour; Canvas.vertexColorAlwaysGammaSpace stays off), the relief is
    /// lit in sRGB-encoded space as render() lights it, and the light alone never takes a channel under the tint (IK-M5).
    /// WP-098A (C-00 08.10) · AP-034 v002.1 row d, the AD default (AP034-v002-AD §2; the reference is AP-034's
    /// build_ap034_v002_icons.py Twin.shade, variant 'd' - UiIconShadeTwinTests embeds its per-pixel output): the light cut
    /// by I1's cavity and I2's grain, the ink (cavity, grain, dry tail, bevel shadow) darkening the lit colour within the
    /// ceiling 0.06 + 0.06 s - so the floor of a lit glyph is tint × (1 − ceiling), the Twin's order: max(lit, tint) first,
    /// then the ink - the ink lob and the size-ramped glow (<see cref="Shade"/> is the whole pixel). The AD numbers are
    /// <see cref="UiMaterialSpec"/>'s; the Twin's structural constants are here and in DD_UiFx.hlsl's DD_ICON_* defines.
    /// </summary>
    public static class UiIconShade
    {
        // ---------------------------------------------------------------- frame (DD_ICON_ATLAS, _CELL_AP, _SD_AP, _SDF_RANGE)

        /// <summary>The one G1 page, px (IK-M2).</summary>
        public const float AtlasSize = 1024f;

        /// <summary>The sprite box spans the 64-unit viewBox: frame uv × 64 = AP.</summary>
        public const float CellAp = 64f;

        /// <summary>AP units per G1 pixel, 64 / 52 (G1 manifest merges[-1].ddIconSdAp).</summary>
        public const float SdAp = 64f / 52f;

        /// <summary>G1 encodes a = 0.5 + d / 12 (spread 6 G1 px).</summary>
        public const float SdfRange = 12f;

        // ---------------------------------------------------------------- render() relief

        /// <summary>h = smoothstep(0, 2, sd); also np.gradient's span - the slope filter is never narrower.</summary>
        public const float BevelAp = 2f;

        public const float NormalStrength = 0.9f;

        /// <summary>The warm upper-left key, as render() writes it (not normalised).</summary>
        public static readonly Vector3 Light = new Vector3(-0.5f, 0.5f, 0.707f);

        public const float LightGain = 0.12f;

        /// <summary>× |∂h/∂y|: render() takes the vertical derivative alone.</summary>
        public const float BevelGain = 0.4f;

        public const float SheenPeak = 0.45f;
        public const float SheenWidth = 0.14f;

        /// <summary>D1 (C-00 06.10): the band runs x/63 + y/160 (~21.5° off vertical) - the code, not README's 45°.</summary>
        public static readonly Vector2 SheenDiagonal = new Vector2(63f, 160f);

        public const float SheenTravel = 1.5f;

        /// <summary>D6 (C-00 06.10): the sheen's 0.45 peak is held to the glint token, then × FlashScale.</summary>
        public const float SheenCeiling = UiMaterialSpec.GlintMaxBrightness;

        public static readonly Vector3 Warm = new Vector3(1f, 0.83f, 0.72f);

        /// <summary>Stroke draw: a *= sat((draw × 1.2 − (x + y) / 126) × 10) - the 45° ink front (x, y = render()'s index).</summary>
        public static readonly Vector3 Draw = new Vector3(1.2f, 126f, 10f);

        /// <summary>D2 (C-00 06.10, render()'s effect): above 0.2, spread / 0.4 × 0.22 of a 0.5 AP dilation (1 px at 128).</summary>
        public static readonly Vector4 Spread = new Vector4(0.2f, 0.4f, 0.22f, 0.5f);

        /// <summary>The glow channel's AP-028 scale (README "dış glow ≤ 0,08"); the shader maps it onto the row d glow α (<see cref="GlowAlpha"/>).</summary>
        public const float GlowChannelMax = 0.08f;

        // ---------------------------------------------------------------- AP-034 v002.1 row d (the Twin's structure)

        /// <summary>I2's size, px: the grain is read 1:1 in screen pixels (uv = screen px / 256, repeat; DD_ICON_GRAIN_TILE).</summary>
        public const float GrainTile = 256f;

        /// <summary>I1 R's deepest cut (1 − 0.55): the cavity term is (1 − R) / 0.45 (DD_ICON_CAVITY_NORM).</summary>
        public const float CavityNorm = 0.45f;

        /// <summary>Row b: the grain cuts the added light by 0.6 · amp · (0.5 − grain) · 2 (DD_ICON_GRAIN_CUT).</summary>
        public const float GrainCut = 0.6f;

        /// <summary>amp = clamp(1 + 2 · (0.5 − G), 0, 3): dense ink (low G) grains more (DD_ICON_INK_GAIN).</summary>
        public const float InkGain = 2f;

        /// <summary>The bevel band 0 ≤ sd ≤ 2 AP, each edge eased over ±0.25 AP (DD_ICON_BEVEL_BAND: from, to, ease).</summary>
        public static readonly Vector3 BevelBand = new Vector3(0f, 2f, 0.25f);

        /// <summary>The key (−0.5, 0.5) normalised: θ is the angle between the outward normal (−g) and it (DD_ICON_KEY_DIR).</summary>
        public static readonly Vector2 KeyDirection = new Vector2(-0.70710678f, 0.70710678f);

        // ---------------------------------------------------------------- the shader's steps

        /// <summary>AP units per screen pixel for a glyph drawn <paramref name="glyphPixels"/> wide (the shader: 64 · fwidth(frame uv)).</summary>
        public static float ApPerPixel(float glyphPixels)
        {
            return CellAp / Mathf.Max(glyphPixels, 1e-4f);
        }

        /// <summary>The distance in AP of an 8-bit G1 SDF value (0..1).</summary>
        public static float SignedDistance(float sdf01)
        {
            return (sdf01 - 0.5f) * SdfRange * SdAp;
        }

        /// <summary>fwidth(sd) of a unit-slope field: the pixel box measured along the unit gradient <paramref name="g"/> (none: one pixel).</summary>
        public static float Footprint(float apPerPixel, Vector2 g)
        {
            return Mathf.Max(apPerPixel * Mathf.Max(Mathf.Abs(g.x) + Mathf.Abs(g.y), 1f), 1e-4f);
        }

        /// <summary>render()'s pixel index (x right, y down, 0..63) of a sprite-box uv (y up).</summary>
        public static Vector2 Index(Vector2 frame)
        {
            return new Vector2(frame.x * CellAp - 0.5f, (1f - frame.y) * CellAp - 0.5f);
        }

        public static float Height(float sdAp)
        {
            return SmoothStep(0f, BevelAp, sdAp);
        }

        /// <summary>
        /// DD_IconBoost: 0.12 · light + 0.4 · |∂h/∂y| + sheen, saturated. <paramref name="g"/> = the unit gradient (y up,
        /// toward the ink), <paramref name="w"/> = the footprint in AP, <paramref name="index"/> = render()'s pixel index,
        /// <paramref name="sheen"/> −1 = off. <paramref name="sheenCeiling"/>: the shipped D6 ceiling; render() parity passes
        /// <see cref="SheenPeak"/> (no ceiling).
        /// </summary>
        public static float Boost(float sdAp, Vector2 g, float w, Vector2 index, float sheen, float flash = 1f, float sheenCeiling = SheenCeiling)
        {
            float wb = Mathf.Max(w, BevelAp);
            float hp = (Height(sdAp + 0.5f * wb) - Height(sdAp - 0.5f * wb)) / wb;
            Vector3 n = new Vector3(-NormalStrength * hp * g.x, -NormalStrength * hp * g.y, 1f).normalized;
            float light = Mathf.Max(0f, Vector3.Dot(n, Light));
            float bevel = BevelGain * Mathf.Abs(hp * g.y);
            float band = 0f;
            if (sheen >= 0f)
            {
                float d = (index.x / SheenDiagonal.x + index.y / SheenDiagonal.y - SheenTravel * sheen) / SheenWidth;
                band = Mathf.Min(SheenPeak * Mathf.Exp(-d * d), sheenCeiling) * flash;
            }

            return Mathf.Clamp01(LightGain * light + bevel + band);
        }

        /// <summary>The lit straight colour, linear: the relief lit in sRGB-encoded space, never under the tint (per channel).</summary>
        public static Color Lit(Color tintLinear, float boost)
        {
            return new Color(
                LitChannel(tintLinear.r, boost * Warm.x),
                LitChannel(tintLinear.g, boost * Warm.y),
                LitChannel(tintLinear.b, boost * Warm.z),
                tintLinear.a);
        }

        private static float LitChannel(float tint, float lift)
        {
            float s = LinearToSrgb(tint);
            return Mathf.Max(SrgbToLinear(s + (1f - s) * lift), tint);
        }

        /// <summary>The spread's mix (0 at and under 0.2, render()'s step).</summary>
        public static float SpreadMix(float spread)
        {
            return spread > Spread.x ? spread / Spread.y * Spread.z : 0f;
        }

        /// <summary>The dilation the spread mixes in, AP (0 when off).</summary>
        public static float SpreadDilation(float spread)
        {
            return SpreadMix(spread) > 0f ? Spread.w : 0f;
        }

        /// <summary>Glyph coverage: a ±0.5 px ramp on the footprint, the spread's dilation mixed in.</summary>
        public static float Coverage(float sdAp, float w, float spread)
        {
            float mix = SpreadMix(spread);
            float dilate = SpreadDilation(spread);
            return Mathf.Lerp(Mathf.Clamp01(sdAp / w + 0.5f), Mathf.Clamp01((sdAp + dilate) / w + 0.5f), mix);
        }

        /// <summary>The outer layers' mask: 0 wherever <see cref="Coverage"/> is above 0 (it starts half a pixel past the edge).</summary>
        public static float Outside(float sdAp, float w, float spread)
        {
            return Mathf.Clamp01(-(sdAp + SpreadDilation(spread)) / w - 0.5f);
        }

        public static float Drawn(float draw, Vector2 index)
        {
            return Mathf.Clamp01((draw * Draw.x - (index.x + index.y) / Draw.y) * Draw.z);
        }

        /// <summary>
        /// The glow's α before the outside mask: the channel (0..0.08) onto row d's α 0.07 + 0.05 s (<paramref name="sizeRamp"/>
        /// = s), × FlashScale, falling off over the 6.15 AP radius.
        /// </summary>
        public static float GlowAlpha(float glow, float sdAp, float spread, float sizeRamp, float flash = 1f)
        {
            float edge = -(sdAp + SpreadDilation(spread));
            return Mathf.Clamp01(glow / GlowChannelMax) * (UiMaterialSpec.IconGlowAlpha + UiMaterialSpec.IconGlowAlphaRamp * sizeRamp) * flash
                * (1f - SmoothStep(0f, UiMaterialSpec.IconGlowRadiusAp, edge));
        }

        // ---------------------------------------------------------------- row d (DD_IconRamp, DD_IconInk, DD_IconShade)

        /// <summary>s = sat((P − 24) / 16) on the glyph's physical px (the shader: 64 AP over the footprint's AP per pixel).</summary>
        public static float SizeRamp(float glyphPixels)
        {
            return Mathf.Clamp01((glyphPixels - UiMaterialSpec.IconRampStartPx) / UiMaterialSpec.IconRampSpanPx);
        }

        /// <summary>The ink's grain gain at an I1 ink density (G): clamp(1 + 2 · (0.5 − G), 0, 3).</summary>
        public static float InkAmp(float ink)
        {
            return Mathf.Clamp(1f + InkGain * (0.5f - ink), 0f, 3f);
        }

        /// <summary>Row b: the share of the boost that survives the cavity (R) and the grain - cavity · sat(1 − 0.6 · amp · sat(0.5 − grain) · 2).</summary>
        public static float LightCut(float cavity, float ink, float grain)
        {
            return cavity * Mathf.Clamp01(1f - GrainCut * InkAmp(ink) * Mathf.Clamp01(0.5f - grain) * 2f);
        }

        /// <summary>The bevel's shadow side: sat(−cos θ) (θ = outward normal −g vs the key) in the 0..2 AP band.</summary>
        public static float BevelShadow(float sdAp, Vector2 g)
        {
            float band = SmoothStep(BevelBand.x - BevelBand.z, BevelBand.x + BevelBand.z, sdAp)
                * (1f - SmoothStep(BevelBand.y - BevelBand.z, BevelBand.y + BevelBand.z, sdAp));
            return Mathf.Clamp01(Vector2.Dot(g, KeyDirection)) * band;
        }

        /// <summary>DD_IconInk: how much the lit colour darkens (before the ceiling) - cavity, grain, dry tail, bevel shadow.</summary>
        public static float Ink(float sdAp, Vector2 g, float cavity, float ink, float grain, float sizeRamp)
        {
            float wide = 0.5f + 0.5f * sizeRamp;
            return UiMaterialSpec.IconCavity * wide * (1f - cavity) / CavityNorm
                + UiMaterialSpec.IconGrain * sizeRamp * InkAmp(ink) * Mathf.Clamp01(0.5f - grain) * 2f
                + UiMaterialSpec.IconDryTail * sizeRamp * Mathf.Clamp01(0.5f - ink) * 4f
                + UiMaterialSpec.IconBevelShadow * wide * BevelShadow(sdAp, g);
        }

        /// <summary>The darkening's ceiling 0.06 + 0.06 s: a lit glyph never falls under tint × (1 − ceiling).</summary>
        public static float Ceiling(float sizeRamp)
        {
            return UiMaterialSpec.IconCeiling + UiMaterialSpec.IconCeilingRamp * sizeRamp;
        }

        /// <summary>The factor on the lit colour: clamp(1 − ink · on, 1 − ceiling, 1) (<paramref name="on"/> = lit × tier).</summary>
        public static float InkFactor(float ink, float sizeRamp, float on = 1f)
        {
            return Mathf.Clamp(1f - ink * on, 1f - Ceiling(sizeRamp), 1f);
        }

        /// <summary>The lob tap's uv offset from the pixel's atlas uv: −offset / (64 / 52) / 1024 (the glyph moves down-right).</summary>
        public static Vector2 LobTapUv
        {
            get { return -UiMaterialSpec.IconLobOffsetAp / SdAp / AtlasSize; }
        }

        /// <summary>The lob's α before the outside mask, from the distance at its tap: smoothstep(−2.4, 2.4, sd) · 0.45.</summary>
        public static float LobAlpha(float sdLobAp)
        {
            return SmoothStep(-UiMaterialSpec.IconLobSoftAp, UiMaterialSpec.IconLobSoftAp, sdLobAp) * UiMaterialSpec.IconLobAlpha;
        }

        /// <summary>
        /// DD_IconShade at one pixel, premultiplied (rgb · α, α) - what the frag writes after its premultiply. The inputs are what
        /// the shader samples there: the G1 distance <paramref name="sdAp"/> and its unit gradient <paramref name="g"/>, the
        /// distance at the lob's tap (<see cref="LobTapUv"/>), I1's cavity (R) and ink density (G) at the glyph uv, I2's grain at
        /// the screen pixel. <paramref name="light"/> = uv1.yzw (sheen −1 | 0..1, glow 0..0.08, spread), <paramref name="parameters"/>
        /// = uv2.xyz (draw, lit, opacity), <paramref name="index"/> = render()'s pixel index, <paramref name="tier"/> = _DD_IconTier.
        /// </summary>
        public static Color Shade(Color tintLinear, float glyphPixels, Vector3 light, Vector3 parameters, Vector2 index, float sdAp, Vector2 g,
            float sdLobAp, float cavity, float ink, float grain, float tier = 1f, float flash = 1f)
        {
            float w = Footprint(ApPerPixel(glyphPixels), g);
            float s = SizeRamp(glyphPixels);
            float on = Mathf.Clamp01(parameters.y) * tier;
            float drawn = Drawn(parameters.x, index);
            float a = Coverage(sdAp, w, light.z) * drawn * parameters.z;

            float boost = Mathf.Clamp01(Boost(sdAp, g, w, index, light.x, flash) * on * LightCut(cavity, ink, grain));
            Color lit = Lit(tintLinear, boost);
            float f = InkFactor(Ink(sdAp, g, cavity, ink, grain, s), s, on);

            float outside = Outside(sdAp, w, light.z) * drawn * parameters.z * on;
            float lobA = LobAlpha(sdLobAp) * outside;
            float glowA = GlowAlpha(light.y, sdAp, light.z, s, flash) * outside;
            Color lob = UiMaterialSpec.IconLobColour;
            float outerA = glowA + (1f - glowA) * lobA;
            float k = lobA * (1f - glowA);
            float outA = a + (1f - a) * outerA;
            return new Color(
                (lit.r * f * a + (tintLinear.r * glowA + SrgbToLinear(lob.r) * k) * (1f - a)) * tintLinear.a,
                (lit.g * f * a + (tintLinear.g * glowA + SrgbToLinear(lob.g) * k) * (1f - a)) * tintLinear.a,
                (lit.b * f * a + (tintLinear.b * glowA + SrgbToLinear(lob.b) * k) * (1f - a)) * tintLinear.a,
                outA * tintLinear.a);
        }

        // ---------------------------------------------------------------- helpers (HLSL semantics)

        /// <summary>HLSL smoothstep (Mathf.SmoothStep is an interpolator, not this).</summary>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>IEC 61966-2-1 decode (DD_SRGBToLinear1).</summary>
        public static float SrgbToLinear(float c)
        {
            return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>IEC 61966-2-1 encode (DD_LinearToSRGB1).</summary>
        public static float LinearToSrgb(float c)
        {
            return c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(Mathf.Max(c, 0f), 1f / 2.4f) - 0.055f;
        }
    }
}
