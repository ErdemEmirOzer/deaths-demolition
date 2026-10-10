#ifndef DD_VEGETATION_CORE_INCLUDED
#define DD_VEGETATION_CORE_INCLUDED

// WP-093 · DD/World Vegetation core: material CBUFFER (SRP-Batcher layout, identical in every pass), the per-instance
// hash (bit-identical to VegetationHash.Instance in C#), WIND-SPEC v1 wind / interaction deformation, distance density
// shrink, the LOD dither cross-fade and (AD v1.2, 10.10) the per-variant petal shift. Include after URP Core.hlsl.
//
// Inputs: vertex colour "vc1" (AP-031): R bend weight (y/h)², G per-blade phase, B stiffness, A root AO.
// Globals:  _DD_WindVector (WorldWind: dir.xy, strength, time) — the one wind (ADR-013-1);
//           _DD_VegWave* / _DD_VegSway / _DD_VegLean / _DD_VegShape / _DD_VegTint / _DD_VegCaps / _DD_VegPhaseSpread
//           (VegetationWindSettings), _DD_VegPush* (VegetationPushMap), _DD_VegFade (distance density), _DD_VegNoiseTex.

TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
TEXTURE2D(_OrmMap);         SAMPLER(sampler_OrmMap);
TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);
TEXTURE2D(_DD_VegPushTex);
TEXTURE2D(_DD_VegNoiseTex); SAMPLER(sampler_DD_VegNoiseTex);
SAMPLER(sampler_point_repeat);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _RootColor;
    half4 _TipColor;
    half4 _DryColor;
    half4 _AccentColor;
    half4 _AccentColorB;
    half4 _TransColor;
    half _Cutoff;
    half _Translucency;
    half _DryFraction;
    half _HueJitter;
    half _ValueJitter;
    half _SatJitter;
    half _FarKeep;
    half _Cull;
    half _BumpScale;
    half _SatCap;
    float _VegMeshHeight;
    float4 _VegLod;          // lo (m), hi (m), half cross-fade (m), level
    float4 _PetalShift;      // AD v1.2: target linear rgb ÷ reference petal luma, amount (0 = atlas as authored)
    float4 _PetalKey;        // AD v1.2: petal mask channel weights (xyz) on the linear texel, gain (w)
    float4 _PetalRect;       // AD v1.2: atlas UV rect the petal mask is limited to (u0, v0, u1, v1)
    float _YellowClamp;      // w5 (10.10): linear red ≤ green × this (0 = off)
CBUFFER_END

float4 _DD_WindVector;
float4 _DD_VegWaveDir01;     // d1.xy, d2.xy
float4 _DD_VegWaveDir2;      // d3.xy, 1/noise tile (m), noise speed (m/s)
float4 _DD_VegWaveK;         // k1, k2, k3, windBase
float4 _DD_VegWaveW;         // ω1, ω2, ω3, global strength
float4 _DD_VegWaveAmp;       // a1, a2, a3, noise on
float4 _DD_VegWavePhase;     // c1, c2, c3, -
float4 _DD_VegSway;          // swayK, fMin, fMax, swayλ
float4 _DD_VegLean;          // leanBase, leanGust, swayBase, swayGust
float4 _DD_VegShape;         // swayCross, normalTilt, arcK, flatSquash
float4 _DD_VegTint;          // gustTintLo, gustTintHi, stiffScale, sway on (reduced motion 0)
float4 _DD_VegCaps;          // capGrass, capWoody, capTall, ±tallHeight (negative = Performance)
float4 _DD_VegPhaseSpread;   // short, tall, fern (B 0.5), woody (B 1)
float4 _DD_VegPushSpring;    // ω grass, ω woody, ζ grass, ζ woody
float4 _DD_VegFlat;          // hold grass, half-life grass, hold woody, half-life woody
float4 _DD_VegFlatFern;      // hold fern, half-life fern
float4 _DD_VegPushRect;      // window origin x, z, world size (m), sim time
float4 _DD_VegPushOn;        // enabled, texels per side, interaction radius (m), -
float4 _DD_VegPushFocus;     // focus x, z
float4 _DD_VegFade;          // fade start, fade end, camera-height factor, grow band

// ------------------------------------------------------------------ hash (== VegetationHash)

uint DDVeg_Mix(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

uint DDVeg_InstanceHashU(float2 xz)
{
    int ix = (int)floor(xz.x * 64.0);
    int iz = (int)floor(xz.y * 64.0);
    return DDVeg_Mix(asuint(ix) * 0x9E3779B1u ^ DDVeg_Mix(asuint(iz)));
}

float DDVeg_To01(uint h)
{
    return (float)(h >> 8) * (1.0 / 16777216.0);
}

float DDVeg_IGN(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// ------------------------------------------------------------------ per instance

struct DDVegInstance
{
    float3 rootWS;
    float  h;          // _VegHeight: LOD0 mesh height × instance Y scale (WIND-SPEC MUST)
    uint   hashU;
    float  hash;
    float  camDist;
    float  grow;       // distance-density shrink 0..1
};

DDVegInstance DDVeg_Instance()
{
    DDVegInstance o;
    float4x4 m = GetObjectToWorldMatrix();
    o.rootWS = float3(m._m03, m._m13, m._m23);
    o.h = max(_VegMeshHeight * length(float3(m._m01, m._m11, m._m21)), 0.01);
    o.hashU = DDVeg_InstanceHashU(o.rootWS.xz);
    o.hash = DDVeg_To01(o.hashU);
    o.camDist = distance(o.rootWS, _WorldSpaceCameraPos.xyz);
    float t = saturate((o.camDist - _DD_VegFade.x) / max(_DD_VegFade.y - _DD_VegFade.x, 0.01));
    float density = lerp(1.0, _FarKeep, t) * _DD_VegFade.z;
    o.grow = saturate((density - o.hash) / max(_DD_VegFade.w, 1e-3));
    return o;
}

// ------------------------------------------------------------------ wind (WIND-SPEC §1)

float DDVeg_GustNorm(float2 x, float t)
{
    float s1 = 0.5 + 0.5 * sin(_DD_VegWaveK.x * dot(x, _DD_VegWaveDir01.xy) - _DD_VegWaveW.x * t + _DD_VegWavePhase.x);
    float s2 = 0.5 + 0.5 * sin(_DD_VegWaveK.y * dot(x, _DD_VegWaveDir01.zw) - _DD_VegWaveW.y * t + _DD_VegWavePhase.y);
    float s3 = 0.5 + 0.5 * sin(_DD_VegWaveK.z * dot(x, _DD_VegWaveDir2.xy) - _DD_VegWaveW.z * t + _DD_VegWavePhase.z);
    float sum = _DD_VegWaveAmp.x * s1 * s1 + _DD_VegWaveAmp.y * s2 * s2 + _DD_VegWaveAmp.z * s3 * s3;
    float noiseMod = 1.0;
    if (_DD_VegWaveAmp.w > 0.5)
    {
        float2 uv = (x - _DD_VegWaveDir01.xy * (_DD_VegWaveDir2.w * t)) * _DD_VegWaveDir2.z;
        noiseMod = lerp(0.55, 1.15, SAMPLE_TEXTURE2D_LOD(_DD_VegNoiseTex, sampler_DD_VegNoiseTex, uv, 0).r);
    }

    float base = _DD_VegWaveK.w;
    float W = saturate(base + sum * noiseMod);
    return saturate((W - base) / max(1.0 - base, 1e-3));
}

float DDVeg_Spread(float h, float B)
{
    float grass = lerp(_DD_VegPhaseSpread.x, _DD_VegPhaseSpread.y, saturate((h - 0.15) / 0.7));
    return B < 0.5 ? lerp(grass, _DD_VegPhaseSpread.z, B * 2.0) : lerp(_DD_VegPhaseSpread.z, _DD_VegPhaseSpread.w, (B - 0.5) * 2.0);
}

float DDVeg_Cap(float h, float B)
{
    float cap = lerp(_DD_VegCaps.x, _DD_VegCaps.y, B);
    if (h > abs(_DD_VegCaps.w)) cap = min(cap, _DD_VegCaps.z);
    return cap;
}

// ------------------------------------------------------------------ interaction (WIND-SPEC §3)

float DDVeg_Release(float a, float B)
{
    float w = lerp(_DD_VegPushSpring.x, _DD_VegPushSpring.y, B);
    float z = lerp(_DD_VegPushSpring.z, _DD_VegPushSpring.w, B);
    if (z >= 0.999) return (1.0 + w * a) * exp(-w * a);
    float s = sqrt(1.0 - z * z);
    float wd = w * s;
    return exp(-z * w * a) * (cos(wd * a) + z / s * sin(wd * a));
}

float DDVeg_FlattenCurve(float a, float B)
{
    float2 grass = _DD_VegFlat.xy;
    float2 fern = _DD_VegFlatFern.xy;
    float2 woody = _DD_VegFlat.zw;
    float2 p = B < 0.5 ? lerp(grass, fern, B * 2.0) : lerp(fern, woody, (B - 0.5) * 2.0);
    float g = a < p.x ? 1.0 : exp2(-(a - p.x) / max(p.y, 0.01));
    return g * (1.0 - B);   // woody plants never lie down
}

void DDVeg_PushTexel(float2 g, float B, float w, inout float2 disp, inout float flat)
{
    float2 uv = (g + 0.5) / _DD_VegPushOn.y;
    float4 s = SAMPLE_TEXTURE2D_LOD(_DD_VegPushTex, sampler_point_repeat, uv, 0);
    float age = _DD_VegPushRect.w - s.a;
    if (age < 8.0)
    {
        age = max(age, 0.0);
        disp += s.rg * (DDVeg_Release(age, B) * w);
        flat += s.b * (DDVeg_FlattenCurve(age, B) * w);
    }
}

// Manual bilinear over four point taps, each with its own age: an expired neighbour contributes nothing.
void DDVeg_SamplePush(float2 xz, float B, out float2 disp, out float flat)
{
    disp = 0.0;
    flat = 0.0;
    float world = _DD_VegPushRect.z;
    float2 rel = xz - _DD_VegPushRect.xy;
    float edge = min(min(rel.x, rel.y), min(world - rel.x, world - rel.y));
    if (_DD_VegPushOn.x < 0.5 || edge <= 0.0) return;
    float texel = world / max(_DD_VegPushOn.y, 1.0);
    float2 g = xz / texel - 0.5;
    float2 i0 = floor(g);
    float2 f = g - i0;
    DDVeg_PushTexel(i0, B, (1.0 - f.x) * (1.0 - f.y), disp, flat);
    DDVeg_PushTexel(i0 + float2(1.0, 0.0), B, f.x * (1.0 - f.y), disp, flat);
    DDVeg_PushTexel(i0 + float2(0.0, 1.0), B, (1.0 - f.x) * f.y, disp, flat);
    DDVeg_PushTexel(i0 + float2(1.0, 1.0), B, f.x * f.y, disp, flat);
    float fade = saturate(edge);   // 1 m fade at the window edge
    disp *= fade;
    flat *= fade;
}

// ------------------------------------------------------------------ deformation (root fixed, length ≈ preserved)

void DDVeg_Deform(inout float3 pWS, inout float3 nWS, float4 vc, DDVegInstance inst, out float gust)
{
    float R = vc.r, G = vc.g, B = vc.b;
    float h = inst.h;
    float3 root = inst.rootWS;
    pWS = root + (pWS - root) * inst.grow;
    float y = pWS.y - root.y;
    float stiff = 1.0 - _DD_VegTint.z * B;

    float2 d = _DD_WindVector.xy;
    d = d / max(length(d), 1e-4);
    float2 n = float2(-d.y, d.x);
    float t = _DD_WindVector.w;
    float lag = lerp(0.20, 0.60, saturate(h / 0.9)) + 0.30 * B;
    float Wn = DDVeg_GustNorm(root.xz, t - lag);   // once per clump, at the root
    float f = clamp(_DD_VegSway.x * rsqrt(max(h, 0.02)), _DD_VegSway.y, _DD_VegSway.z) * (1.0 - 0.15 * B);
    float ph = 6.2832 * (G * DDVeg_Spread(h, B) + dot(root.xz, d) / _DD_VegSway.w);
    float swayOn = _DD_VegTint.w * (_VegLod.w >= 2.0 ? 0.0 : 1.0);   // LOD2: lean only
    if (_DD_VegCaps.w < 0.0 && h < 0.25) swayOn = 0.0;                // Performance: short grass leans and tints only
    float a = lerp(_DD_VegLean.z, _DD_VegLean.w, Wn) * stiff * swayOn;
    float2 bW = d * (lerp(_DD_VegLean.x, _DD_VegLean.y, Wn) * stiff + a * sin(6.2832 * f * t + ph))
              + n * (a * _DD_VegShape.x * sin(6.2832 * f * 1.13 * t + ph + 1.7));
    bW *= _DD_VegWaveW.w;   // global strength (WorldWind calm → no motion)

    float2 pD = 0.0;
    float fl = 0.0;
    bool perf = _DD_VegPushOn.z < 1000.0;
    bool interact = _VegLod.w < (perf ? 0.5 : 1.5) && distance(root.xz, _DD_VegPushFocus.xy) < _DD_VegPushOn.z;
    if (interact) DDVeg_SamplePush(pWS.xz, B, pD, fl);
    fl = saturate(fl);

    float pM = length(pD);
    float2 pDir = pM > 1e-3 ? pD / pM : d;
    float cap = DDVeg_Cap(h, B);
    float2 b = bW * (1.0 - 0.7 * saturate(pM)) + pDir * (pM + 0.6 * fl) * cap;
    b *= min(1.0, cap / max(length(b), 1e-4));
    float2 off = b * h * R;
    float d2 = min(dot(off, off), 0.81 * y * y);
    float yN = sqrt(max(y * y - _DD_VegShape.z * d2, 0.0)) * (1.0 - _DD_VegShape.w * (1.0 - B) * fl);
    pWS = float3(pWS.x + off.x, root.y + yN, pWS.z + off.y);
    nWS = normalize(nWS + float3(b.x, 0.0, b.y) * (_DD_VegShape.y * R));
    gust = Wn * R * step(1e-4, _DD_VegWaveW.w);
}

// ------------------------------------------------------------------ shared vertex

struct DDVegAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 color      : COLOR;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DDVegVertex
{
    float3 positionWS;
    float3 normalWS;
    float  gust;
    float  camDist;
    float3 hash;
};

// Call after UNITY_SETUP_INSTANCE_ID(input).
DDVegVertex DDVeg_Vertex(DDVegAttributes input)
{
    DDVegVertex o;
    DDVegInstance inst = DDVeg_Instance();
    float3 pWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 nWS = TransformObjectToWorldNormal(input.normalOS);
    float gust;
    DDVeg_Deform(pWS, nWS, input.color, inst, gust);
    o.positionWS = pWS;
    o.normalWS = nWS;
    o.gust = gust;
    o.camDist = inst.camDist;
    o.hash = float3(inst.hash, DDVeg_To01(DDVeg_Mix(inst.hashU ^ 0x68E31DA4u)), DDVeg_To01(DDVeg_Mix(inst.hashU ^ 0xB5297A4Du)));
    return o;
}

// Alpha cut + the 2 m LOD dither cross-fade (LOD k keeps dither ≥ t_hi, LOD k+1 keeps dither < t_lo: complementary).
void DDVeg_Clip(float2 uv, float4 positionCS, float camDist)
{
    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
    clip(alpha - _Cutoff);
    float dither = DDVeg_IGN(positionCS.xy);
    float hw = max(_VegLod.z, 1e-3);
    float tLo = saturate((camDist - _VegLod.x + hw) / (2.0 * hw));
    float tHi = saturate((camDist - _VegLod.y + hw) / (2.0 * hw));
    if (_VegLod.x > 0.0) clip(tLo - dither - 1e-4);
    clip(dither - tHi);
}

// ------------------------------------------------------------------ colour (LOOK-BIBLE §3)

half3 DDVeg_RotateHue(half3 c, half degrees)
{
    const half3x3 toYIQ = half3x3(0.299, 0.587, 0.114, 0.596, -0.274, -0.322, 0.211, -0.523, 0.312);
    const half3x3 toRGB = half3x3(1.0, 0.956, 0.621, 1.0, -0.272, -0.647, 1.0, -1.106, 1.703);
    half3 yiq = mul(toYIQ, c);
    half a = radians(degrees);
    half cs = cos(a), sn = sin(a);
    yiq.yz = half2(yiq.y * cs - yiq.z * sn, yiq.y * sn + yiq.z * cs);
    return max(mul(toRGB, yiq), 0.0);
}

// AD v1.2 (10.10) tsuyu garden: one flower atlas, several flower colours. Inside _PetalRect, a texel whose key
// (dot(linear rgb, _PetalKey.xyz) × _PetalKey.w: HY b − g, SL r − g) is positive takes the target × its own luma —
// _PetalShift.rgb is the target already divided by the variant's mean petal luma, so the atlas pattern is kept and the
// mean lands on the target. _PetalShift.w = amount; 0 (every non-flower draw, the red higanbana) returns the texel as is.
// A uniform branch per draw; no keyword.
half3 DDVeg_PetalShift(float2 uv, half3 tex)
{
    if (_PetalShift.w <= 0.0) return tex;
    float2 inLo = step(_PetalRect.xy, uv);
    float2 inHi = step(uv, _PetalRect.zw);
    half inside = (half)(inLo.x * inLo.y * inHi.x * inHi.y);
    half mask = saturate(dot(tex, (half3)_PetalKey.xyz) * (half)_PetalKey.w) * inside * (half)_PetalShift.w;
    half texLuma = dot(tex, half3(0.2126h, 0.7152h, 0.0722h));
    return lerp(tex, (half3)_PetalShift.rgb * texLuma, mask);
}

// uv.x ≥ 2: placeholder accent (2..3 A, ≥ 3 B) — never produced by the AP-031 atlas.
half3 DDVeg_Albedo(float2 uv, half4 vc, float3 hash, float3 rootWS, float gust)
{
    half t = vc.r > 1e-3 ? sqrt(saturate(vc.r)) : (half)hash.z;
    half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, t);
    if (hash.y < _DryFraction) albedo = lerp(albedo, _DryColor.rgb, saturate(t * 1.25));
    if (uv.x >= 2.0) albedo = uv.x >= 3.0 ? _AccentColorB.rgb : _AccentColor.rgb;
    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    albedo *= DDVeg_PetalShift(uv, tex.rgb) * _BaseColor.rgb;

    // Per instance: hue ±, value ±, saturation ± (bible: SS ±4°, ±6 %, ±0.03).
    half macro = SAMPLE_TEXTURE2D_LOD(_DD_VegNoiseTex, sampler_DD_VegNoiseTex, rootWS.xz * 0.1, 0).r - 0.5;   // 10 m macro tone
    albedo = DDVeg_RotateHue(albedo, (half)(hash.x * 2.0 - 1.0) * _HueJitter + macro * 6.0);
    half luma = dot(albedo, half3(0.2126, 0.7152, 0.0722));
    albedo = lerp(luma.xxx, albedo, 1.0 + (half)(hash.y * 2.0 - 1.0) * _SatJitter * 4.0);
    albedo *= 1.0 + (half)(hash.z * 2.0 - 1.0) * _ValueJitter + macro * 0.08;
    albedo *= 1.0 + lerp(_DD_VegTint.x, _DD_VegTint.y, (half)gust);   // gust sheen (WIND-SPEC §2)
    albedo = saturate(albedo);
    // w5 (S-05 10.10, AD K1 / K3 measured on the w4 TD frames): the atlas' sun-bleached and dry blade texels still read 20–65°
    // (the yellow-brown and gold bands; 6 % of the lit cover in td5) — red held to ≤ _YellowClamp × green (linear; 0.45 ≈ sRGB
    // 0.70: hue ≥ ~75° before the warm sun). Off (0) for the flowers and the leaf litter.
    if (_YellowClamp > 0.0) albedo.r = min(albedo.r, albedo.g * (half)_YellowClamp);
    // Saturation ceiling: pull toward grey at the same max. _SatCap is the bible's unit — HSV s of the sRGB albedo
    // (LOOK-BIBLE v1.1 MO 0.42; AD v1.2 SS/GM 0.48, GT 0.42, FE/LC/MO/KZ 0.50). The albedo here is linear, where the
    // same colour reads far more saturated (#3A462B: s 0.39 sRGB, 0.60 linear), so the ceiling is converted: with
    // sRGB ≈ linear^(1/2), 1 − s_srgb = sqrt(min/max) → s_linear ≤ 1 − (1 − _SatCap)² (±0.04 s over vegetation albedo
    // 0.05–0.3). AD v1.2 (10.10): the v1.1 test compared linear s against the sRGB number and greyed MO to ≈ 0.24.
    half mx = max(max(albedo.r, albedo.g), albedo.b);
    half mn = min(min(albedo.r, albedo.g), albedo.b);
    half sat = mx > 1e-4h ? (mx - mn) / mx : 0.0h;
    half keep = 1.0h - (half)_SatCap;
    half satCapLinear = 1.0h - keep * keep;
    if (sat > satCapLinear) albedo = lerp(mx.xxx, albedo, satCapLinear / sat);
    return albedo;
}

#endif
