// DD/VFX HeatHaze — WP-047E · the AP-010B `barrel_heat_distort` data card (R = haze falloff, G = tileable noise).
// With the URP opaque texture available (DD_OPAQUE_TEXTURE, set by VfxDressing from the pipeline asset) the card
// refracts the scene behind it; without it (WP-029 pipeline asset: opaque texture off) it draws a faint scrolled
// shimmer instead — never a solid card. All-float CBUFFER (Metal), no instancing needed (one card per barrel).
Shader "DD/VFX HeatHaze"
{
    Properties
    {
        [MainTexture] _Sheet("Distort data (R falloff, G noise)", 2D) = "gray" {}
        _AlphaCap("Heat 0..1 (FlipbookEffect.SetAlphaCap)", Range(0, 1)) = 0
        _Strength("Refraction strength (uv)", Range(0, 0.05)) = 0.012
        _ShimmerAlpha("Shimmer alpha (no opaque texture)", Range(0, 1)) = 0.18
        _Scroll("Noise scroll (1/s)", Float) = 1.6
        _TileMode("0 = AP-010B data card (R falloff, G noise) · 1 = AP-014C normal tile (RGB normal, A mask)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HeatHaze"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ DD_OPAQUE_TEXTURE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if DD_OPAQUE_TEXTURE
            // Declared only in the refracting variant: the shimmer variant must not carry an unbound opaque-texture slot (Metal).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #endif

            TEXTURE2D(_Sheet); SAMPLER(sampler_Sheet);

            CBUFFER_START(UnityPerMaterial)
                float4 _Sheet_ST;
                float _AlphaCap;
                float _Strength;
                float _ShimmerAlpha;
                float _Scroll;
                float _TileMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionHCS);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float falloff, noise;
                if (_TileMode > 0.5)
                {
                    // AP-014C: A = intensity (1 at the muzzle end v 0 → 0 at v 1), RGB = tangent normal scrolled along V.
                    float4 t0 = SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, i.uv);
                    float2 nuvT = float2(i.uv.x, frac(i.uv.y + _Time.y * _Scroll * 0.375));
                    float3 n = SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, nuvT).rgb * 2.0 - 1.0;
                    falloff = t0.a;
                    noise = n.y * 0.5;
                }
                else
                {
                    falloff = SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, i.uv).r;
                    float2 nuv = float2(i.uv.x, frac(i.uv.y + _Time.y * _Scroll));
                    noise = SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, nuv).g - 0.5;
                }
                float amount = falloff * saturate(_AlphaCap);
                #if DD_OPAQUE_TEXTURE
                    float2 suv = i.screenPos.xy / i.screenPos.w;
                    suv.y += noise * _Strength * amount;
                    float3 scene = SampleSceneColor(suv);
                    return float4(scene, saturate(amount * 2.0));
                #else
                    return float4(1.0, 0.98, 0.94, (noise + 0.5) * _ShimmerAlpha * amount);
                #endif
            }
            ENDHLSL
        }
    }
}
