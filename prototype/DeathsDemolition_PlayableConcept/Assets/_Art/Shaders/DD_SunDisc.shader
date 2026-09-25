// WP-075 · DEC-102 · DD/SunDisc — the high sun's disc and halo in the v6 sky: one quad along the sun direction (WorldV5Sky), procedural
// from its UV (a soft-edged HDR disc the bloom turns into glare, a tight glow and a wide exponential halo), additive, no fog, the depth
// pinned just inside the far plane like DD/SkyPlate. Drawn after the cloud planes: it samples both cloud sheets along each pixel's view
// ray with their own mapping and drift (DD/SkyPlate on the WorldV5Sky planes), so a passing cloud dims the sun to a bright glow through
// the cumulus instead of hiding it, and the gaps show the crisp disc.
Shader "DD/SunDisc"
{
    Properties
    {
        [HDR] _DiscColor("Disc (HDR, linear)", Color) = (30, 27, 22, 1)
        _HaloColor("Halo (linear)", Color) = (1.0, 0.82, 0.60, 1)
        _DiscRadius("Disc radius (quad half-size fraction)", Range(0.001, 0.2)) = 0.012
        _GlowIntensity("Tight glow intensity", Range(0, 8)) = 1.6
        _GlowFalloff("Tight glow falloff (per quad half-size)", Range(4, 120)) = 36
        _HaloPower("Halo falloff", Range(1, 32)) = 6
        _HaloIntensity("Halo intensity", Range(0, 2)) = 0.22
        [NoScaleOffset] _CloudNear("Near cloud sheet", 2D) = "black" {}
        _CloudNearMap("Near sheet: height, 1 / tile, drift u, drift v", Vector) = (500, 0.00057, 0, 0)
        [NoScaleOffset] _CloudFar("Far cloud sheet", 2D) = "black" {}
        _CloudFarMap("Far sheet: height, 1 / tile, drift u, drift v", Vector) = (900, 0.00086, 0, 0)
        _CloudAlphaPower("Cloud alpha power (WorldV5Sky.CloudAlphaPower)", Range(0.3, 4)) = 2
        _CloudDim("Cloud dimming: disc, halo", Vector) = (0.88, 0.5, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-104" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "SunDisc"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DiscColor;
                half4 _HaloColor;
                half _DiscRadius;
                half _GlowIntensity;
                half _GlowFalloff;
                half _HaloPower;
                half _HaloIntensity;
                float4 _CloudNearMap;
                float4 _CloudFarMap;
                half _CloudAlphaPower;
                half4 _CloudDim;
            CBUFFER_END
            TEXTURE2D(_CloudNear); SAMPLER(sampler_CloudNear);
            TEXTURE2D(_CloudFar); SAMPLER(sampler_CloudFar);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                #if UNITY_REVERSED_Z
                    output.positionCS.z = output.positionCS.w * 1.0e-5;
                #else
                    output.positionCS.z = output.positionCS.w * (1.0 - 1.0e-5);
                #endif
                output.uv = input.uv;
                return output;
            }
            // The cloud sheet's coverage along the view ray: the WorldV5Sky plane sits at (0, height, 0) with uv = world xz / tile,
            // drifting by _Time.y × the sheet's _ScrollUV (DD/SkyPlate), alpha^CloudAlphaPower.
            half Cover(TEXTURE2D_PARAM(sheet, samplerSheet), float4 map, float3 dir)
            {
                if (dir.y <= 0.01) return 0.0h;
                float2 xz = _WorldSpaceCameraPos.xz + dir.xz * ((map.x - _WorldSpaceCameraPos.y) / dir.y);
                half a = SAMPLE_TEXTURE2D(sheet, samplerSheet, xz * map.y + _Time.y * map.zw).a;
                return pow(saturate(a), _CloudAlphaPower);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.positionWS - _WorldSpaceCameraPos);
                half near = Cover(TEXTURE2D_ARGS(_CloudNear, sampler_CloudNear), _CloudNearMap, dir);
                half far = Cover(TEXTURE2D_ARGS(_CloudFar, sampler_CloudFar), _CloudFarMap, dir);
                half cover = 1.0h - (1.0h - near) * (1.0h - far);
                half r = length(input.uv * 2.0 - 1.0);
                half disc = 1.0h - smoothstep(_DiscRadius * 0.7h, _DiscRadius, r);
                half glow = exp(-r * _GlowFalloff) * _GlowIntensity;
                half halo = pow(saturate(1.0h - r), _HaloPower) * _HaloIntensity;
                half3 core = _DiscColor.rgb * disc + _HaloColor.rgb * glow;
                return half4(core * (1.0h - _CloudDim.x * cover) + _HaloColor.rgb * halo * (1.0h - _CloudDim.y * cover), 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
