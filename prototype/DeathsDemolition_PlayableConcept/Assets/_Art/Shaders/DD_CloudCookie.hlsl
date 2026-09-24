#ifndef DD_CLOUD_COOKIE_INCLUDED
#define DD_CLOUD_COOKIE_INCLUDED

// WP-075 C2 · Cloud-shadow contract (CloudShadowDriver, direction Ek A §4). Include after URP Core.hlsl.
//
//   _DD_CloudCookie   AP-023 T_cloud_cookie (R8, repeat): 1 = sun patch, 0.40 = cloud shadow, mean 0.62
//   _DD_CloudCookieU  uv.x = dot(float4(positionWS, 1), _DD_CloudCookieU)
//   _DD_CloudCookieV  uv.y = dot(float4(positionWS, 1), _DD_CloudCookieV)
//   _DD_ShaftDir      xyz = direction the sunlight travels (unit), w = screen-space shaft strength 0..1 (TP/FP, C4)
//
// The rows are URP's own main-light cookie projection (light space, 240 m tile on the ground, drifting 1.3 m/s
// toward 52°), so a sample here equals the cookie the lit ground receives — for a point in the air, the ground
// at the end of its sun ray. No driver (menus, tests without a world): a white cookie, everything reads lit.
//
// Shaft cards and ground mist (C4 / C5) and particles (S-07: ×1.6 in a shaft, ×0.7 in shadow) use
// DD_CloudSunLit01 — 0 in the cloud shadow, 1 in the sun patch.

TEXTURE2D(_DD_CloudCookie);
SAMPLER(sampler_DD_CloudCookie);
float4 _DD_CloudCookieU;
float4 _DD_CloudCookieV;
float4 _DD_ShaftDir;

float2 DD_CloudCookieUV(float3 positionWS)
{
    float4 p = float4(positionWS, 1.0);
    return float2(dot(p, _DD_CloudCookieU), dot(p, _DD_CloudCookieV));
}

// Raw cookie 0.40..1. Mip 0 so it works in vertex programs too (the pattern has no detail under 5 m).
half DD_CloudCookieSample(float3 positionWS)
{
    return SAMPLE_TEXTURE2D_LOD(_DD_CloudCookie, sampler_DD_CloudCookie, DD_CloudCookieUV(positionWS), 0).r;
}

// 0 in the cloud shadow (0.40), 1 in the sun patch (≥ 0.80 counts as sun, Ek A §4).
half DD_CloudSunLit01(float3 positionWS)
{
    return saturate((DD_CloudCookieSample(positionWS) - 0.40h) / 0.40h);
}

#endif
