// WP-036 · DD_TierLitInput — URP 14 LitInput with the version-tint lerp (ADR-036-1). Generated from
// com.unity.render-pipelines.universal@14.0.10/Shaders/LitInput.hlsl; every URP/Lit property is kept so
// ArtMaterialBuilder recipes and the SRP Batcher cbuffer layout apply unchanged.
#ifndef DD_TIER_LIT_INPUT_INCLUDED
#define DD_TIER_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

#if defined(_DETAIL_MULX2) || defined(_DETAIL_SCALED)
#define _DETAIL
#endif

// NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _DetailAlbedoMap_ST;
half4 _BaseColor;
half4 _SpecColor;
half4 _EmissionColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _BumpScale;
half _Parallax;
half _OcclusionStrength;
half _ClearCoatMask;
half _ClearCoatSmoothness;
half _DetailAlbedoMapScale;
half _DetailNormalMapScale;
half _Surface;
// WP-036 (ADR-036-1): version tint - lerp(base, tint, mask * k) on the cloth (Mask.G) and the lacquer (Mask.R); the lacquer floor lifts the black lacquer under the mask so a plum/rust tint can read (AP-014 finding).
half4 _TierClothTint;
half _TierClothK;
half4 _TierLacquerTint;
half _TierLacquerK;
half _TierLacquerFloor;
// WP-084 (AP-025 contract v002): always declared, every variant — the SRP Batcher needs one UnityPerMaterial layout.
float4 _ZombieMaskDomain;
float4 _ZombieTierClothRect;
float4 _ZombieMaskArray_TexelSize;
half4 _ZombieSkinRef;
half4 _ZombieSkinTint0;
half4 _ZombieSkinTint1;
half4 _ZombieSkinTint2;
half4 _ZombieDirtColor;
half4 _ZombieBloodColor;
half _ZombieUseOrm;
half _ZombieMaskMaxLod;
half _ZombieSkinEnable;
half _ZombieDirtRoughness;
half _ZombieBloodRoughness;
// WP-111 R4 (S-06, C-00 11.10 · AD-R3READ SHOULD-1 / N1): the hero's TD read, used by the _HERO_READ variant only — always declared.
// _HeroLift: x = gain on the dark albedo (linear), y / z = luma where the gain starts / ends fading to 1 (linear); w unused.
// _HeroRim: rgb = rim colour added after lighting (linear), a = the view-angle exponent.
half4 _HeroLift;
half4 _HeroRim;
CBUFFER_END

// NOTE: Do not ifdef the properties for dots instancing, but ifdef the actual usage.
// Otherwise you might break CPU-side as property constant-buffer offsets change per variant.
// NOTE: Dots instancing is orthogonal to the constant buffer above.
#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _SpecColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _EmissionColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _BumpScale)
    UNITY_DOTS_INSTANCED_PROP(float , _Parallax)
    UNITY_DOTS_INSTANCED_PROP(float , _OcclusionStrength)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearCoatMask)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearCoatSmoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _DetailAlbedoMapScale)
    UNITY_DOTS_INSTANCED_PROP(float , _DetailNormalMapScale)
    UNITY_DOTS_INSTANCED_PROP(float , _Surface)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

// Here, we want to avoid overriding a property like e.g. _BaseColor with something like this:
// #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor0)
//
// It would be simpler, but it can cause the compiler to regenerate the property loading code for each use of _BaseColor.
//
// To avoid this, the property loads are cached in some static values at the beginning of the shader.
// The properties such as _BaseColor are then overridden so that it expand directly to the static value like this:
// #define _BaseColor unity_DOTS_Sampled_BaseColor
//
// This simple fix happened to improve GPU performances by ~10% on Meta Quest 2 with URP on some scenes.
static float4 unity_DOTS_Sampled_BaseColor;
static float4 unity_DOTS_Sampled_SpecColor;
static float4 unity_DOTS_Sampled_EmissionColor;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_BumpScale;
static float  unity_DOTS_Sampled_Parallax;
static float  unity_DOTS_Sampled_OcclusionStrength;
static float  unity_DOTS_Sampled_ClearCoatMask;
static float  unity_DOTS_Sampled_ClearCoatSmoothness;
static float  unity_DOTS_Sampled_DetailAlbedoMapScale;
static float  unity_DOTS_Sampled_DetailNormalMapScale;
static float  unity_DOTS_Sampled_Surface;

void SetupDOTSLitMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_SpecColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecColor);
    unity_DOTS_Sampled_EmissionColor        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EmissionColor);
    unity_DOTS_Sampled_Cutoff               = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
    unity_DOTS_Sampled_Smoothness           = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_Metallic             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_BumpScale            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BumpScale);
    unity_DOTS_Sampled_Parallax             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Parallax);
    unity_DOTS_Sampled_OcclusionStrength    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _OcclusionStrength);
    unity_DOTS_Sampled_ClearCoatMask        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatMask);
    unity_DOTS_Sampled_ClearCoatSmoothness  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatSmoothness);
    unity_DOTS_Sampled_DetailAlbedoMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailAlbedoMapScale);
    unity_DOTS_Sampled_DetailNormalMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailNormalMapScale);
    unity_DOTS_Sampled_Surface              = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Surface);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSLitMaterialPropertyCaches()

#define _BaseColor              unity_DOTS_Sampled_BaseColor
#define _SpecColor              unity_DOTS_Sampled_SpecColor
#define _EmissionColor          unity_DOTS_Sampled_EmissionColor
#define _Cutoff                 unity_DOTS_Sampled_Cutoff
#define _Smoothness             unity_DOTS_Sampled_Smoothness
#define _Metallic               unity_DOTS_Sampled_Metallic
#define _BumpScale              unity_DOTS_Sampled_BumpScale
#define _Parallax               unity_DOTS_Sampled_Parallax
#define _OcclusionStrength      unity_DOTS_Sampled_OcclusionStrength
#define _ClearCoatMask          unity_DOTS_Sampled_ClearCoatMask
#define _ClearCoatSmoothness    unity_DOTS_Sampled_ClearCoatSmoothness
#define _DetailAlbedoMapScale   unity_DOTS_Sampled_DetailAlbedoMapScale
#define _DetailNormalMapScale   unity_DOTS_Sampled_DetailNormalMapScale
#define _Surface                unity_DOTS_Sampled_Surface

#endif

TEXTURE2D(_ParallaxMap);        SAMPLER(sampler_ParallaxMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask);         SAMPLER(sampler_DetailMask);
TEXTURE2D(_TierMask);           SAMPLER(sampler_TierMask);
TEXTURE2D(_ZombieORM);          SAMPLER(sampler_ZombieORM);
TEXTURE2D_ARRAY(_ZombieMaskArray); SAMPLER(sampler_ZombieMaskArray);
TEXTURE2D(_DetailAlbedoMap);    SAMPLER(sampler_DetailAlbedoMap);
TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);
TEXTURE2D(_ClearCoatMap);       SAMPLER(sampler_ClearCoatMap);

#ifdef _SPECULAR_SETUP
    #define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv)
#else
    #define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv)
#endif

half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
{
    half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
    specGloss = half4(SAMPLE_METALLICSPECULAR(uv));
    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a *= _Smoothness;
    #endif
#else // _METALLICSPECGLOSSMAP
    #if _SPECULAR_SETUP
        specGloss.rgb = _SpecColor.rgb;
    #else
        specGloss.rgb = _Metallic.rrr;
    #endif

    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a = _Smoothness;
    #endif
#endif

    return specGloss;
}

half SampleOcclusion(float2 uv)
{
    #ifdef _OCCLUSIONMAP
        half occ = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #else
        return half(1.0);
    #endif
}


// Returns clear coat parameters
// .x/.r == mask
// .y/.g == smoothness
half2 SampleClearCoat(float2 uv)
{
#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoatMaskSmoothness = half2(_ClearCoatMask, _ClearCoatSmoothness);

#if defined(_CLEARCOATMAP)
    clearCoatMaskSmoothness *= SAMPLE_TEXTURE2D(_ClearCoatMap, sampler_ClearCoatMap, uv).rg;
#endif

    return clearCoatMaskSmoothness;
#else
    return half2(0.0, 1.0);
#endif  // _CLEARCOAT
}

void ApplyPerPixelDisplacement(half3 viewDirTS, inout float2 uv)
{
#if defined(_PARALLAXMAP)
    uv += ParallaxMapping(TEXTURE2D_ARGS(_ParallaxMap, sampler_ParallaxMap), viewDirTS, _Parallax, uv);
#endif
}

// Used for scaling detail albedo. Main features:
// - Depending if detailAlbedo brightens or darkens, scale magnifies effect.
// - No effect is applied if detailAlbedo is 0.5.
half3 ScaleDetailAlbedo(half3 detailAlbedo, half scale)
{
    // detailAlbedo = detailAlbedo * 2.0h - 1.0h;
    // detailAlbedo *= _DetailAlbedoMapScale;
    // detailAlbedo = detailAlbedo * 0.5h + 0.5h;
    // return detailAlbedo * 2.0f;

    // A bit more optimized
    return half(2.0) * detailAlbedo * scale - scale + half(1.0);
}

half3 ApplyDetailAlbedo(float2 detailUv, half3 albedo, half detailMask)
{
#if defined(_DETAIL)
    half3 detailAlbedo = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv).rgb;

    // In order to have same performance as builtin, we do scaling only if scale is not 1.0 (Scaled version has 6 additional instructions)
#if defined(_DETAIL_SCALED)
    detailAlbedo = ScaleDetailAlbedo(detailAlbedo, _DetailAlbedoMapScale);
#else
    detailAlbedo = half(2.0) * detailAlbedo;
#endif

    return albedo * LerpWhiteTo(detailAlbedo, detailMask);
#else
    return albedo;
#endif
}

half3 ApplyDetailNormal(float2 detailUv, half3 normalTS, half detailMask)
{
#if defined(_DETAIL)
#if BUMP_SCALE_NOT_SUPPORTED
    half3 detailNormalTS = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv));
#else
    half3 detailNormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv), _DetailNormalMapScale);
#endif

    // With UNITY_NO_DXT5nm unpacked vector is not normalized for BlendNormalRNM
    // For visual consistancy we going to do in all cases
    detailNormalTS = normalize(detailNormalTS);

    return lerp(normalTS, BlendNormalRNM(normalTS, detailNormalTS), detailMask); // todo: detailMask should lerp the angle of the quaternion rotation, not the normals
#else
    return normalTS;
#endif
}

#if defined(_ZOMBIE_MASKS)
// WP-084 · one mask slice sampled inside its own half of the 1024² slice (the left half is the skin atlas' layout, the right
// half the cloth atlas'): the LOD comes from the domain UV, both bracketing mips are sampled with the UV clamped half a
// texel inside the domain at that mip, then blended — the halves never bleed into each other, even in the small mips;
// mip 10 (1×1) is never sampled (contract v002 mip_domain_sampling).
half ZombieMaskSample(float2 maskUv, float slice)
{
    float2 size = _ZombieMaskArray_TexelSize.z > 1.0 ? _ZombieMaskArray_TexelSize.zw : float2(1024.0, 1024.0);   // 1024² slices
    float2 dx = ddx(maskUv * size), dy = ddy(maskUv * size);
    float lod = clamp(0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8)), 0.0, (float)_ZombieMaskMaxLod);
    float l0 = floor(lod), l1 = min(l0 + 1.0, (float)_ZombieMaskMaxLod);
    float lo = _ZombieMaskDomain.z, hi = _ZombieMaskDomain.z + _ZombieMaskDomain.x;
    float h0 = 0.5 * exp2(l0) / size.x, h1 = 0.5 * exp2(l1) / size.x;
    float v0 = 0.5 * exp2(l0) / size.y, v1 = 0.5 * exp2(l1) / size.y;
    float2 uv0 = float2(clamp(maskUv.x, lo + h0, hi - h0), clamp(maskUv.y, v0, 1.0 - v0));
    float2 uv1 = float2(clamp(maskUv.x, lo + h1, hi - h1), clamp(maskUv.y, v1, 1.0 - v1));
    half a = SAMPLE_TEXTURE2D_ARRAY_LOD(_ZombieMaskArray, sampler_ZombieMaskArray, uv0, slice, l0).r;
    half b = SAMPLE_TEXTURE2D_ARRAY_LOD(_ZombieMaskArray, sampler_ZombieMaskArray, uv1, slice, l1).r;
    return saturate(lerp(a, b, (half)(lod - l0)));
}

// Indices from renderingLayerMask (bit reinterpretation, not a numeric cast): 16-20 skin, 21-25 dirt, 26-30 blood; out of
// range clamps to 0..2 (contract invalid_index_policy).
void ZombieMaskIndices(out uint skin, out uint dirt, out uint blood)
{
    uint bits = GetMeshRenderingLayer();
    skin = min((bits >> 16) & 31u, 2u);
    dirt = min((bits >> 21) & 31u, 2u);
    blood = min((bits >> 26) & 31u, 2u);
}
#endif

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;
    outSurfaceData.albedo = AlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);

#if _SPECULAR_SETUP
    outSurfaceData.metallic = half(1.0);
    outSurfaceData.specular = specGloss.rgb;
#else
    outSurfaceData.metallic = specGloss.r;
    outSurfaceData.specular = half3(0.0, 0.0, 0.0);
#endif

    outSurfaceData.smoothness = specGloss.a;
    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    outSurfaceData.occlusion = SampleOcclusion(uv);
    outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoat = SampleClearCoat(uv);
    outSurfaceData.clearCoatMask       = clearCoat.r;
    outSurfaceData.clearCoatSmoothness = clearCoat.g;
#else
    outSurfaceData.clearCoatMask       = half(0.0);
    outSurfaceData.clearCoatSmoothness = half(0.0);
#endif

#if defined(_DETAIL)
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv).a;
    float2 detailUv = uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
    outSurfaceData.albedo = ApplyDetailAlbedo(detailUv, outSurfaceData.albedo, detailMask);
    outSurfaceData.normalTS = ApplyDetailNormal(detailUv, outSurfaceData.normalTS, detailMask);
#endif

    // WP-036 (ADR-036-1): version tint. Mask.R = lacquer, Mask.G = cloth (AP-007 / AP-008 / AP-014 atlases share the channel convention).
    // lerp, not multiply: a multiplicative detail albedo cannot lift a v 0.12 black lacquer to plum / rust (AP-014 measurement).
    #if defined(_ZOMBIE_MASKS)
    // WP-084: the replacement O1 atlas has one ORM map (R AO, G roughness, B metallic) — read directly, no repacked copies
    if (_ZombieUseOrm > 0.5)
    {
        half3 orm = SAMPLE_TEXTURE2D(_ZombieORM, sampler_ZombieORM, uv).rgb;
        outSurfaceData.metallic = orm.b;
        outSurfaceData.smoothness = half(1.0) - orm.g;
        outSurfaceData.occlusion = LerpWhiteTo(orm.r, _OcclusionStrength);
    }
    #endif

    #if defined(_TIER_TINT)
    #if defined(_ZOMBIE_MASKS)
    // WP-084: the old body TierMask is not resident with the O1 atlas — the version tint's cloth coverage is the material's
    // UV rectangle (the sash / rope region on the skin atlas, the whole cloth atlas); no lacquer on these atlases.
    half inRect = (half)(step(_ZombieTierClothRect.x, uv.x) * step(_ZombieTierClothRect.y, uv.y) * step(uv.x, _ZombieTierClothRect.z) * step(uv.y, _ZombieTierClothRect.w));
    half4 tierMask = half4(0.0, inRect, 0.0, 0.0);
    #else
    half4 tierMask = SAMPLE_TEXTURE2D(_TierMask, sampler_TierMask, uv);
    #endif
    half3 tinted = outSurfaceData.albedo;
    // lacquer floor: under the lacquer mask the base is lifted to at least _TierLacquerFloor (value) so the tint has something to blend with
    half baseV = max(tinted.r, max(tinted.g, tinted.b));
    half lift = max(half(0.0), _TierLacquerFloor - baseV);
    tinted = tinted + lift * tierMask.r * step(half(0.001), _TierLacquerK);
    tinted = lerp(tinted, _TierLacquerTint.rgb, saturate(tierMask.r * _TierLacquerK));
    tinted = lerp(tinted, _TierClothTint.rgb, saturate(tierMask.g * _TierClothK));
    outSurfaceData.albedo = tinted;
    #endif

    #if defined(_HERO_READ)
    // WP-111 R4: the darks-only lift — the dark costume gains up to _HeroLift.x, the ivory / cream panels keep their albedo, nothing
    // passes 1 (R3's flat 1.3 _BaseColor put 13–16 % of the atlas above linear 1.0). By luma, so a hue keeps its hue.
    half heroLuma = dot(outSurfaceData.albedo, half3(0.2126, 0.7152, 0.0722));
    half heroGain = lerp(_HeroLift.x, half(1.0), smoothstep(_HeroLift.y, _HeroLift.z, heroLuma));
    outSurfaceData.albedo = saturate(outSurfaceData.albedo * heroGain);
    #endif

    #if defined(_ZOMBIE_MASKS)
    // WP-084 · AP-025 order (linear): tier tint (above) → skin C·T/Tref → dirt → blood (last albedo layer) → roughness.
    uint skinIndex, dirtIndex, bloodIndex;
    ZombieMaskIndices(skinIndex, dirtIndex, bloodIndex);
    float2 maskUv = uv * _ZombieMaskDomain.xy + _ZombieMaskDomain.zw;
    half s = ZombieMaskSample(maskUv, (float)skinIndex) * _ZombieSkinEnable;
    half d = ZombieMaskSample(maskUv, (float)(3u + dirtIndex));
    half b = ZombieMaskSample(maskUv, (float)(6u + bloodIndex));
    half3 skinTint = skinIndex == 0u ? _ZombieSkinTint0.rgb : (skinIndex == 1u ? _ZombieSkinTint1.rgb : _ZombieSkinTint2.rgb);
    half3 c0 = outSurfaceData.albedo;
    half3 ct = saturate(c0 * skinTint / max(_ZombieSkinRef.rgb, half3(0.001, 0.001, 0.001)));
    half3 c1 = lerp(c0, ct, s);
    half3 c2 = lerp(c1, _ZombieDirtColor.rgb, d);
    outSurfaceData.albedo = lerp(c2, _ZombieBloodColor.rgb, b);
    half r0 = half(1.0) - outSurfaceData.smoothness;
    half r1 = lerp(r0, _ZombieDirtRoughness, d);
    half r2 = lerp(r1, _ZombieBloodRoughness, b);
    outSurfaceData.smoothness = half(1.0) - saturate(r2);
    #endif
}

#endif // UNIVERSAL_INPUT_SURFACE_PBR_INCLUDED
