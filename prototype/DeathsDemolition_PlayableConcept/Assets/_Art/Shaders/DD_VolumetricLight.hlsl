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
//   _DD_VL_Mask      x 1 / the field's peak density (normalises DD_ShaftMask; 0 while the field is off)
//
//   DD_VL_Density(p)   the field at a world point (1/m)
//   DD_CloudSunLit01(p) (DD_CloudCookie.hlsl) the cloud cookie's sun share at p, 0 under a cloud … 1 in a gap; the shadow map is the
//                      caller's (MainLightRealtimeShadow), because only fragment programs on URP's lit path can sample it cheaply.
//   DD_ShaftMask(p)    THE RUNTIME CONTRACT for S-07's DD/VFX Atmo (WP-073 §3, C-00 25.09): 0..1, how much of a volumetric shaft a
//                      world point sits in — the field normalised by its peak × the cloud cookie's sun share; 1 while no field is live
//                      (Performance, World_AP005: the "missing → 1" of the VFX spec). Multiply by the caller's MainLightRealtimeShadow
//                      for the shaft itself (the raymarch's own terms, without the phase). Needs DD_CloudCookie.hlsl included first.

#define DD_VL_MAX_REGIONS 8

float4 _DD_VL_Density;
float4 _DD_VL_Regions[DD_VL_MAX_REGIONS];
float4 _DD_VL_RegionShape[DD_VL_MAX_REGIONS];
float4 _DD_VL_Scatter;
float4 _DD_VL_Mask;
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

#ifdef DD_CLOUD_COOKIE_INCLUDED
float DD_ShaftMask(float3 p)
{
    if (_DD_VL_Density.w < 0.5) return 1.0;
    return saturate(DD_VL_Density(p) * _DD_VL_Mask.x) * DD_CloudSunLit01(p);
}
#endif

#endif
