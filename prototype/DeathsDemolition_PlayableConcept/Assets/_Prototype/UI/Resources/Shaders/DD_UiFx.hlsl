#ifndef DD_UIFX_INCLUDED
#define DD_UIFX_INCLUDED

// WP-068 slice 4 · DD/UI/Fx shared include. Until S-09's slice-1 exporter ships UiTokens.hlsl this file carries the
// globals and the §2.5 easing curves itself; the swap is one #include (the names below are the exporter's names).

// Globals, written every frame by UiFxGlobals (unscaled clock, reduced motion, flash scale, palette).
float  _DD_UiTime;      // Time.unscaledTime: menus, HUD, pause (timeScale 0 freeze)
float  _DD_WorldTime;   // Time.time: scaled effects
float  _DD_Motion;      // 1, or 0 under reduced motion (Decor jumps to its end state)
float  _DD_Flash;       // UiTokens.FlashScale (reduce flashes: 0.4)
float4 _DD_Ink;         // Sumi900
float4 _DD_Paper;       // Cream100
float4 _DD_GoldLeaf;    // Gold400 (kinpaku, reserve: money / Special only)
float4 _DD_SilverLeaf;  // Cream100 (ginpaku)
// Ink kit (RGBA, one mark per channel; time maps: 0 = first ink, 1.0 = empty): R brush stroke, G bleed/leak,
// B hanko, A leaf crack tile. A procedural placeholder today; the AP A8 BC4 kit replaces the texture only.
sampler2D _DD_InkMap;

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

// §2.5 UiEase: cubic-bezier (x1, y1, x2, y2) solved for x with four Newton steps (UiEase.cs uses the same closed form).
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
inline float DD_EaseSettle(float t) { return DD_CubicBezier(saturate(t), 0.33, 1.00, 0.68, 1.00); }
inline float DD_EaseBrush(float t)  { return DD_CubicBezier(saturate(t), 0.25, 0.10, 0.25, 1.00); }
inline float DD_EaseExit(float t)   { return DD_CubicBezier(saturate(t), 0.32, 0.00, 0.67, 0.00); }

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

// Time maps are one-shot marks: sample inside the texture (the kit texture repeats for the leaf tile, and a wrapped
// bilinear tap at u = 1 would pull the left edge's "first ink" into the right edge).
inline float2 DD_MapUv(float2 uv) { return clamp(uv, 0.004, 0.996); }

inline float DD_InkChannel(float4 s, float channel)
{
    return channel < 0.5 ? s.r : (channel < 1.5 ? s.g : (channel < 2.5 ? s.b : s.a));
}

#endif
