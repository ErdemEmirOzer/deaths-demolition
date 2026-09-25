#ifndef DD_VOLUMETRIC_LIGHT_INCLUDED
#define DD_VOLUMETRIC_LIGHT_INCLUDED

// WP-075 · DEC-102 volumetric light — the shared density field (DD.Art.VolumetricLight publishes the globals every frame the v6
// world is live; include after URP Core.hlsl). S-07's particles use the same field and sun visibility as the raymarch, so dust and
// petals glow where the shafts are:
//
//   _DD_VL_Density   x base density (1/m) at y = z, y height falloff k (1/m), z base height (m), w 1 while the field is live
//   _DD_VL_Regions[i]        xyz centre (m), w horizontal radius (m)          (i < _DD_VL_RegionCount)
//   _DD_VL_RegionShape[i]    x vertical radius (m), y peak density (1/m)
//   _DD_VL_Scatter   rgb the sun's scattered colour (linear, × intensity), a the ambient (sky) share in the shadows
//
//   DD_VL_Density(p)   the field at a world point (1/m)
//   DD_CloudSunLit01(p) (DD_CloudCookie.hlsl) the cloud cookie's sun share at p, 0 under a cloud … 1 in a gap; the shadow map is the
//                      caller's (MainLightRealtimeShadow), because only fragment programs on URP's lit path can sample it cheaply.

#define DD_VL_MAX_REGIONS 8

float4 _DD_VL_Density;
float4 _DD_VL_Regions[DD_VL_MAX_REGIONS];
float4 _DD_VL_RegionShape[DD_VL_MAX_REGIONS];
float4 _DD_VL_Scatter;
float _DD_VL_RegionCount;

float DD_VL_Density(float3 p)
{
    float d = _DD_VL_Density.x * exp(-_DD_VL_Density.y * max(p.y - _DD_VL_Density.z, 0.0));
    int n = (int)_DD_VL_RegionCount;
    [loop]
    for (int i = 0; i < DD_VL_MAX_REGIONS; i++)
    {
        if (i >= n) break;
        float4 r = _DD_VL_Regions[i];
        float4 s = _DD_VL_RegionShape[i];
        float2 dxz = (p.xz - r.xz) / max(r.w, 1e-3);
        float dy = (p.y - r.y) / max(s.x, 1e-3);
        float q = dot(dxz, dxz) + dy * dy;
        d += s.y * saturate(1.0 - q) * saturate(1.0 - q);   // smooth ellipsoid
    }

    return d * _DD_VL_Density.w;
}

#endif
