// WP-111 R4 (S-06, C-00 11.10 · AD-R3READ N1) · DD/Character/Tier Lit forward pass: URP 14's LitForwardPass unchanged for every
// variant; the _HERO_READ variant wraps its fragment and adds a thin view-angle rim after lighting, so the dark hero costume
// keeps an edge on dark ground from the TD camera. Zombie variants compile the URP pass as before.
#ifndef DD_TIER_LIT_FORWARD_PASS_INCLUDED
#define DD_TIER_LIT_FORWARD_PASS_INCLUDED

#if defined(_HERO_READ)
#define LitPassFragment DDTierLitBaseFragment
#endif
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
#if defined(_HERO_READ)
#undef LitPassFragment

void LitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out float4 outRenderingLayers : SV_Target1
#endif
)
{
    DDTierLitBaseFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
    // the geometric normal: the silhouette is the mesh's, the normal map's detail would only speckle the rim
    half3 n = normalize(input.normalWS);
    half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half rim = PositivePow(half(1.0) - saturate(dot(n, v)), max(_HeroRim.a, half(0.5)));
    outColor.rgb += _HeroRim.rgb * rim;
}
#endif

#endif
