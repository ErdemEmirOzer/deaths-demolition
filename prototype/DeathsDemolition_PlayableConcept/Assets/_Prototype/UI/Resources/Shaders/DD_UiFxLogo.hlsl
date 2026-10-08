#ifndef DD_UIFX_LOGO_INCLUDED
#define DD_UIFX_LOGO_INCLUDED

// AP-035 R3 (S-11 O2, C-00 08.10, K10) · DD/UI/Fx mode 9 LogoInk: the AP-035 wordmark inks in through its own A8 time map
// (UiLogoAssets.TimeMapResource, 2048x768, linear, no mips, Clamp; tools/art/ap035-intake/bake_ap035_logo.py):
//   code 0 = first ink (motion-spec frame 3) ... 254 = last (frame 47), 255 = never; code = floor((T - 3) / 44 * 254).
// Its own maths, not the kit's DD_InkTime / DD_InkCoverage (S3-25: those read 254 as empty and rescale per channel).
// The time map is a global (_DD_LogoMap, set by UiLogoAssets; a missing map falls back to a 1x1 Alpha8 0 = all inked),
// sampled at the element's rect uv (uv3.xy) - the wordmark and its halo share one rect, one map, one clock.
//
// UiFx (uv1) duration = the intro's frames 1..47 (46/24 s / speed): the shader's progress runs 0 -> 1 over them, the reveal
// window is its last 44/46 (frames 3..47). Reduced motion: progress 1 (DD_Progress) - the whole wordmark at once.
// prm.x = erase start (_DD_UiTime), prm.y = erase seconds (0 = none): the outro wipes the wordmark left -> right with a
// dark wet front (v001 outro.mp4 frames 8..14). prm.z unused.

#define DD_FX_LOGO 9

sampler2D _DD_LogoMap;

#define DD_LOGO_CODE_MAX     254.0
#define DD_LOGO_LEAD         (2.0 / 46.0)     // frames 1..3 of 1..47 show nothing
#define DD_LOGO_ERASE_SOFT   0.08             // the erase front's width, in rect widths

// Reveal: a texel (floor code c: its true time lies in [c, c + 1)) turns at c + 0.5. The front's width is one screen
// pixel of the map (fwidth), clamped to [0.05, 2] codes: on a ramp the front is antialiased over a pixel; on a plateau
// (one time over an area: the ease-out caps, AP-035 A1) it is near-binary, so no area ever hangs half-inked (the flat
// grey wedges of the v003 intro); at a stroke border (codes jump) it stays ≤ 2 codes, so no half-inked seam lingers.
#define DD_LOGO_FRONT_MIN 0.05
#define DD_LOGO_FRONT_MAX 2.0

inline float DD_LogoReveal(float progress, float2 rectUv)
{
    float p = saturate((progress - DD_LOGO_LEAD) / (1.0 - DD_LOGO_LEAD));
    float code = tex2Dlod(_DD_LogoMap, float4(rectUv, 0.0, 0.0)).a * 255.0;
    float w = clamp(fwidth(code), DD_LOGO_FRONT_MIN, DD_LOGO_FRONT_MAX);
    float vis = saturate((p * (DD_LOGO_CODE_MAX + 1.0) - (code + 0.5)) / w + 0.5);
    vis = p >= 1.0 ? 1.0 : (p <= 0.0 ? 0.0 : vis);
    return code > DD_LOGO_CODE_MAX + 0.5 ? 0.0 : vis;
}

half4 DD_LogoInk(half4 color, float progress, float4 prm, float2 rectUv)
{
    float vis = DD_LogoReveal(progress, rectUv);
    if (prm.y > 0.0)
    {
        float e = saturate((_DD_UiTime - prm.x) / prm.y);
        float front = e * (1.0 + 2.0 * DD_LOGO_ERASE_SOFT) - DD_LOGO_ERASE_SOFT;   // e 0: all kept, e 1: all gone
        float keep = saturate((rectUv.x - front) / DD_LOGO_ERASE_SOFT);
        float wet = (1.0 - abs(keep * 2.0 - 1.0)) * step(0.001, e) * step(e, 0.999);
        color.rgb = lerp(color.rgb, _DD_Ink.rgb, wet * 0.6);
        vis *= keep;
    }

    color.a *= vis;
    return color;
}

#endif
