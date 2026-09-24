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

// Modes (UiFxMode). One material, the mode rides in uv1.x.
#define DD_FX_NONE      0
#define DD_FX_INK       1
#define DD_FX_LEAF      2
#define DD_FX_STAMP     3
#define DD_FX_SHEEN     4
#define DD_FX_SEGMENT   5
#define DD_FX_SDF       6
#define DD_FX_PARTICLES 7

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

#endif
