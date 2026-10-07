#ifndef DD_UIFX_INCLUDED
#define DD_UIFX_INCLUDED

// WP-068 slice 4 · DD/UI/Fx shared include. Token constants (eases, stamp envelope, glint / sheen ceilings) come from
// S-09's generated UiTokens.hlsl (slice 1, `DD/UI/Export Tokens`); this file adds the runtime globals and the Fx helpers.
#include "../../Art/Shaders/UiTokens.hlsl"

// Globals, written every frame by UiFxGlobals (unscaled clock, reduced motion, flash scale, palette).
float  _DD_UiTime;      // Time.unscaledTime: menus, HUD, pause (timeScale 0 freeze)
float  _DD_WorldTime;   // Time.time: scaled effects
float  _DD_Motion;      // 1, or 0 under reduced motion (Decor jumps to its end state)
float  _DD_Flash;       // UiTokens.FlashScale (reduce flashes: 0.4)
float4 _DD_Ink;         // Sumi900
float4 _DD_Paper;       // Cream100
float4 _DD_GoldLeaf;    // Gold400 (kinpaku, reserve: money / Special only)
float4 _DD_SilverLeaf;  // Cream100 (ginpaku)
// Ink kit (AP-012 v004 A8, one RGBA BC7 linear texture; time maps: 0 = first ink, 1.0 = empty): each channel holds one
// mark in its own rect of the texture — R brush underline, G brush divider, B hanko, A blot. _DD_InkRect[c] is that
// rect (xy origin, zw size, bottom-left UV), written by UiFxGlobals from the kit manifest; the element's 0..1 rect maps
// into it. Outside a mark the channel is 1.0 (never inks).
sampler2D _DD_InkMap;
float4 _DD_InkMapSize;     // (1/w, 1/h, w, h)
float4 _DD_InkRect[4];
float4 _DD_InkTimeMax;     // per channel: the mark's 99th-percentile time — the time map is read as m / max, so its last ink lands at t = 1
// Kinpaku leaf tile (128², 4×4 squares with darker seams; single channel) for the Leaf glint, and the washi paper
// tile (256², single channel) for the frozen frame's mottle. Both repeat.
sampler2D _DD_LeafTile;
sampler2D _DD_PaperTile;
// WP-081 ikon v2 B5b · the icon material's tier switch, written by UiFxGlobals once at install and on QualityTiers.Changed
// (not per frame): 1 = the full material (Cinematic, High), 0 = the flat token, today's look (Performance). Never set = 0.
float  _DD_IconTier;

// Modes (UiFxMode). One material, the mode rides in uv1.x.
#define DD_FX_NONE      0
#define DD_FX_INK       1
#define DD_FX_LEAF      2
#define DD_FX_STAMP     3
#define DD_FX_SHEEN     4
#define DD_FX_SEGMENT   5
#define DD_FX_SDF       6
#define DD_FX_PARTICLES 7
#define DD_FX_ICON      8

inline bool DD_IsMode(float mode, float id) { return abs(mode - id) < 0.5; }

// §2.5 UiEase: cubic-bezier (x1, y1, x2, y2) solved for x with four Newton steps (UiEaseCurve.Evaluate is the C# twin).
inline float DD_Bezier1(float a, float b, float t) { float u = 1.0 - t; return 3.0 * u * u * t * a + 3.0 * u * t * t * b + t * t * t; }
inline float DD_Bezier1D(float a, float b, float t) { float u = 1.0 - t; return 3.0 * u * u * a + 6.0 * u * t * (b - a) + 3.0 * t * t * (1.0 - b); }
inline float DD_CubicBezier(float x, float x1, float y1, float x2, float y2)
{
    float t = x;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float d = DD_Bezier1D(x1, x2, t);
        t -= (DD_Bezier1(x1, x2, t) - x) / max(d, 1e-4);
        t = saturate(t);
    }
    return DD_Bezier1(y1, y2, t);
}
inline float DD_Ease(float4 bezier, float t) { return DD_CubicBezier(saturate(t), bezier.x, bezier.y, bezier.z, bezier.w); }
inline float DD_EaseSettle(float t) { return DD_Ease(DD_UI_EASE_SETTLE, t); }
inline float DD_EaseBrush(float t)  { return DD_Ease(DD_UI_EASE_BRUSH, t); }
inline float DD_EaseExit(float t)   { return DD_Ease(DD_UI_EASE_EXIT, t); }

// Progress of a one-shot (start, duration) on the unscaled clock. Reduced motion: the end state at once.
inline float DD_Progress(float start, float duration)
{
    if (_DD_Motion < 0.5) return 1.0;
    return saturate((_DD_UiTime - start) / max(duration, 1e-4));
}

inline float DD_Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// The deepest mip the time maps are read from (mip 3 = 128² of the 1024² kit): lower mips average first ink with
// the 1.0 "empty" around it and smear the timeline (AP-012 v004: "no mips beyond 4 levels").
#define DD_INK_MAX_LOD 3.0

// Element uv (0..1 over the rect) → kit uv inside the channel's mark rect, inset half a texel so a bilinear tap never
// reads the neighbouring mark.
inline float2 DD_InkUv(float2 uv, int channel)
{
    float4 r = _DD_InkRect[channel];
    float2 inset = 0.5 * _DD_InkMapSize.xy / max(r.zw, 1e-4);
    return r.xy + clamp(uv, inset, 1.0 - inset) * r.zw;
}

// One channel of the kit at the element uv, with the mip clamped to DD_INK_MAX_LOD.
inline float DD_InkSample(float2 uv, float channel)
{
    int c = (int)clamp(round(channel), 0.0, 3.0);
    float2 kuv = DD_InkUv(uv, c);
    float2 texels = kuv * _DD_InkMapSize.zw;
    float2 dx = ddx(texels), dy = ddy(texels);
    float lod = clamp(0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8)), 0.0, DD_INK_MAX_LOD);
    float4 s = tex2Dlod(_DD_InkMap, float4(kuv, 0.0, lod));
    return c == 0 ? s.r : (c == 1 ? s.g : (c == 2 ? s.b : s.a));
}

// The kit time in 0..1 of the reveal (empty stays 1.0).
inline float DD_InkTime(float m, float channel)
{
    int c = (int)clamp(round(channel), 0.0, 3.0);
    return m >= 0.995 ? 1.0 : saturate(m / max(_DD_InkTimeMax[c], 0.05));
}

// Coverage of the mark (1 inside, 0 where the time map is empty): the finished mark's shape, for an element drawn
// without its static-mask sprite.
inline float DD_InkCoverage(float m) { return 1.0 - smoothstep(0.975, 0.995, m); }

// ---------------------------------------------------------------------------------------------------------------------
// WP-081 ikon v2 B5b · Icon (8): AP-028-B "O1 Fırça Mürekkebi" — the light-only relief of the reference render()
// (AP-028 04-tools/build_B_icons.py:110-134; the code is the source, C-00 06.10), derived from the G1 SDF itself: no
// height / normal page, no extra sampler, no keyword. UiIconShade (UI/Runtime/Fx) is the C# twin — UiIconShadeTwinTests
// holds the two to each other and to render()'s table.
// Units: AP = AP-028's 64-unit SVG viewBox, which is the G1 52² sprite box (the B5a family frame [0, 256]): 1 G1 px =
// 64 / 52 AP; on screen S / M / L (24 / 32 / 40 px) = 2.667 / 2.0 / 1.6 AP per pixel (direction §1.2).
#define DD_ICON_ATLAS        1024.0                       // the one G1 page (IK-M2); _MainTex_TexelSize is not relied on
#define DD_ICON_CELL_AP      64.0                         // the sprite box spans the viewBox: frame uv × 64 = AP
#define DD_ICON_SD_AP        1.2307692                    // 64 / 52 AP per G1 px (manifest merges[-1].ddIconSdAp)
#define DD_ICON_SDF_RANGE    12.0                         // G1 encoding a = 0.5 + d / 12 (spread 6 G1 px)
#define DD_ICON_BEVEL_AP     2.0                          // h = smoothstep(0, 2, sd); also np.gradient's span (2 AP)
#define DD_ICON_NORMAL       0.9                          // normal strength (OpenGL Y+)
#define DD_ICON_LIGHT        float3(-0.5, 0.5, 0.707)     // the warm upper-left key, as render() writes it
#define DD_ICON_LIGHT_GAIN   0.12
#define DD_ICON_BEVEL_GAIN   0.4                          // × |∂h/∂y|: render() takes the vertical derivative alone
#define DD_ICON_SHEEN_PEAK   0.45
#define DD_ICON_SHEEN_WIDTH  0.14
#define DD_ICON_SHEEN_DIAG   float2(63.0, 160.0)          // D1: the band runs x/63 + y/160 (~21.5°), not README's 45°
#define DD_ICON_SHEEN_TRAVEL 1.5                          // band centre = sheen × 1.5
#define DD_ICON_WARM         float3(1.0, 0.83, 0.72)      // the light's warm tint
#define DD_ICON_DRAW         float3(1.2, 126.0, 10.0)     // stroke draw: a *= sat((draw × 1.2 − (x + y) / 126) × 10)
#define DD_ICON_SPREAD       float4(0.2, 0.4, 0.22, 0.5)  // D2: above 0.2, spread / 0.4 × 0.22 of a 0.5 AP dilation
#define DD_ICON_GLOW_MAX     0.08                         // the glow channel's AP-028 scale (README "dış glow ≤ 0,08")
#define DD_ICON_LOB_AP       0.6666667                    // the cold lob's offset: 0.25 px24 = 0.667 AP = 0.542 G1 px

// sRGB transfer (IEC 61966-2-1, with the linear toe): the relief is lit in sRGB-encoded space as render() lights it;
// the base and the floor stay linear (unity-material §2.2).
inline float DD_SRGBToLinear1(float c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
inline float DD_LinearToSRGB1(float c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(max(c, 0.0), 1.0 / 2.4) - 0.055; }
inline float3 DD_SRGBToLinear(float3 c) { return float3(DD_SRGBToLinear1(c.r), DD_SRGBToLinear1(c.g), DD_SRGBToLinear1(c.b)); }
inline float3 DD_LinearToSRGB(float3 c) { return float3(DD_LinearToSRGB1(c.r), DD_LinearToSRGB1(c.g), DD_LinearToSRGB1(c.b)); }

// The SDF at an atlas uv as a signed distance in AP (+ inside the ink). tex2Dlod: G1 has no mips and the taps need no
// derivatives, so they are safe in the frag's mode chain.
inline float DD_IconSd(sampler2D atlas, float2 uv)
{
    return (tex2Dlod(atlas, float4(uv, 0.0, 0.0)).a - 0.5) * DD_ICON_SDF_RANGE * DD_ICON_SD_AP;
}

inline float DD_IconHeight(float sd) { return smoothstep(0.0, DD_ICON_BEVEL_AP, sd); }

// render()'s boost at one pixel: 0.12 · light + 0.4 · |∂h/∂y| + sheen, saturated. sd and w in AP; g = the distance's unit
// gradient (y up, toward the ink); ix = render()'s pixel index (x right, y down, 0..63); sheen −1 = off. The height
// slope is box-filtered over the pixel's footprint, never narrower than the reference's 2 AP central difference
// (wb = max(fwidth, 2 AP)), so 24 px does not shimmer and 40 px is not sharper than render().
inline float DD_IconBoost(float sd, float2 g, float w, float2 ix, float sheen)
{
    float wb = max(w, DD_ICON_BEVEL_AP);
    float hp = (DD_IconHeight(sd + 0.5 * wb) - DD_IconHeight(sd - 0.5 * wb)) / wb;
    float3 n = normalize(float3(-DD_ICON_NORMAL * hp * g, 1.0));
    float light = max(0.0, dot(n, DD_ICON_LIGHT));
    float bevel = DD_ICON_BEVEL_GAIN * abs(hp * g.y);
    float band = 0.0;
    if (sheen >= 0.0)
    {
        float d = (ix.x / DD_ICON_SHEEN_DIAG.x + ix.y / DD_ICON_SHEEN_DIAG.y - DD_ICON_SHEEN_TRAVEL * sheen) / DD_ICON_SHEEN_WIDTH;
        // D6: the band's 0.45 peak is held to the glint token (0.25), then × FlashScale like every Flash-class light.
        band = min(DD_ICON_SHEEN_PEAK * exp(-d * d), DD_UI_GLINT_MAX_BRIGHTNESS) * _DD_Flash;
    }

    return saturate(DD_ICON_LIGHT_GAIN * light + bevel + band);
}

// Icon (8), straight colour (the frag premultiplies). fx = (8, sheen −1 | 0..1, glow 0..0.08, spread px24), prm = (draw
// 0..1, lit 0..1 — 0 when disabled, opacity, −), frame = the sprite box uv (IN.map.xy, y up), tint = the linear token.
//  · coverage: a ±0.5 px ramp on the footprint (the existing Sdf() keeps its ±1 px);
//  · colour: the relief lit in sRGB-encoded space, then max(lit, tint) per channel BEFORE the premultiply — light only
//    adds; the flat token is the floor, exactly (IK-M5);
//  · outer layers (cold lob, glow) only where the glyph has no coverage at all: their mask starts half a pixel beyond the
//    (spread) edge, where the ramp has reached 0, so no glyph pixel changes (IK-M5, T2);
//  · Performance tier (_DD_IconTier 0) and disabled (prm.y 0): no light, no lob, no glow — the flat token.
half4 DD_IconShade(sampler2D atlas, float2 uv, float2 frame, half4 tint, float4 fx, float4 prm)
{
    // Footprint: AP per screen pixel from the frame uv (the box spans 64 AP) — the derivative of an interpolant, so it
    // holds at a stroke's ridge where an 8-bit distance's own 2×2 derivative would collapse.
    float apx = DD_ICON_CELL_AP * max(fwidth(frame.x), fwidth(frame.y));
    float t = 1.0 / DD_ICON_ATLAS;
    float sd = DD_IconSd(atlas, uv);
    float2 delta = float2(DD_IconSd(atlas, uv + float2(t, 0.0)) - DD_IconSd(atlas, uv - float2(t, 0.0)),
                          DD_IconSd(atlas, uv + float2(0.0, t)) - DD_IconSd(atlas, uv - float2(0.0, t)));
    float2 g = delta / max(length(delta), 1e-5);
    // fwidth(sd) of a unit-slope field (|∇sd| ≈ 1 AP per AP): the pixel box measured along g; the saturated plateau
    // (no gradient) keeps one pixel.
    float w = max(apx * max(abs(g.x) + abs(g.y), 1.0), 1e-4);

    float on = saturate(prm.y) * _DD_IconTier;
    float spread = fx.w > DD_ICON_SPREAD.x ? fx.w / DD_ICON_SPREAD.y * DD_ICON_SPREAD.z : 0.0;
    float dilate = spread > 0.0 ? DD_ICON_SPREAD.w : 0.0;
    float cov = lerp(saturate(sd / w + 0.5), saturate((sd + dilate) / w + 0.5), spread);
    float2 ix = float2(frame.x, 1.0 - frame.y) * DD_ICON_CELL_AP - 0.5;
    float drawn = saturate((prm.x * DD_ICON_DRAW.x - (ix.x + ix.y) / DD_ICON_DRAW.y) * DD_ICON_DRAW.z);
    float a = cov * drawn * prm.z;

    float boost = DD_IconBoost(sd, g, w, ix, fx.y) * on;
    float3 tintS = DD_LinearToSRGB(tint.rgb);
    float3 litRgb = DD_SRGBToLinear(tintS + (1.0 - tintS) * boost * DD_ICON_WARM);   // ('lit' is an HLSL intrinsic)
    float3 rgb = max(litRgb, (float3)tint.rgb);

    // Outer layers follow the ink front (drawn) and the opacity; the lob is the glyph shifted down-right (one tap at the
    // upper-left), the glow falls off over DD_UI_ICON_GLOW_RADIUS AP from the edge. Both × the tier and the light switch.
    float outside = saturate(-(sd + dilate) / w - 0.5) * drawn * prm.z * on;
    float2 lobUv = float2(-1.0, 1.0) * (DD_ICON_LOB_AP / DD_ICON_SD_AP * t);
    float lobA = saturate(DD_IconSd(atlas, uv + lobUv) / w + 0.5) * DD_UI_ICON_LOB.a * outside;
    float glowA = saturate(fx.z / DD_ICON_GLOW_MAX) * DD_UI_ICON_GLOW_ALPHA * _DD_Flash
                * (1.0 - smoothstep(0.0, DD_UI_ICON_GLOW_RADIUS, -(sd + dilate))) * outside;
    float outerA = glowA + (1.0 - glowA) * lobA;
    float3 outerRgb = tint.rgb * glowA + DD_SRGBToLinear(DD_UI_ICON_LOB.rgb) * (lobA * (1.0 - glowA));

    // The Sdf() pattern: composite the glyph over its outer layers (premultiplied), back to straight colour.
    float outA = a + (1.0 - a) * outerA;
    float3 outRgb = (rgb * a + outerRgb * (1.0 - a)) / max(outA, 1e-4);
    return half4(outRgb, outA * tint.a);
}

#endif
