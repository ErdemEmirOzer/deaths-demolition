// WP-013 · DD_Water_v1 — the pond surface (AP-003B pond_water placeholder → v1). AP-015 owns
// the full water shader; this one is deliberately small:
//   two scrolling ripple layers (analytic sine ripples, so no normal texture enters the
//   asset gate), depth fade against the camera depth texture (URP asset: depth on),
//   PBR shading with the sky / reflection probe, transparent, no foam.
// Property names shared with URP/Lit where they mean the same thing (_BaseColor,
// _Smoothness, _Cull) so ArtMaterialBuilder's common block applies.
Shader "DD/Water v1"
{
    Properties
    {
        [MainColor] _BaseColor("Deep Color", Color) = (0.03695, 0.05114, 0.05925, 0.92)
        _ShallowColor("Shallow Color", Color) = (0.20, 0.30, 0.28, 0.35)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.95
        _DepthFade("Depth Fade (m)", Range(0.05, 3.0)) = 0.6
        _RippleScale("Ripple Scale (1/m)", Range(0.1, 10.0)) = 2.2
        _RippleStrength("Ripple Strength", Range(0.0, 1.0)) = 0.35
        _RippleSpeed("Ripple Speed", Range(0.0, 2.0)) = 0.35
        _WindRipple("Wind Ripple", Range(0.0, 1.0)) = 0.5

        _Surface("__surface", Float) = 1.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [HideInInspector] _SrcBlend("__src", Float) = 5.0
        [HideInInspector] _DstBlend("__dst", Float) = 10.0
        [HideInInspector] _ZWrite("__zw", Float) = 0.0
        _QueueOffset("Queue offset", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "DD_Wind.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShallowColor;
                half _Smoothness;
                half _DepthFade;
                half _RippleScale;
                half _RippleStrength;
                half _RippleSpeed;
                half _WindRipple;
                half _Surface;
                half _Blend;
                half _Cull;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
                half _QueueOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Sum of directional sine ripples; the derivative gives the slope, the slope
            // gives the normal. Two layers scroll against each other, one of them down-wind.
            float2 RippleSlope(float2 p, float t)
            {
                float3 wind = DD_WindDirection();
                float windStrength = saturate(_DD_WindVector.z) * _WindRipple;
                float2 d0 = normalize(float2(0.83, 0.56));
                float2 d1 = normalize(float2(-0.44, 0.90));
                float2 d2 = normalize(lerp(float2(0.31, -0.95), wind.xz, windStrength));
                float2 slope = 0;
                float k0 = _RippleScale * 6.2831853;
                float k1 = _RippleScale * 6.2831853 * 1.73;
                float k2 = _RippleScale * 6.2831853 * 0.61;
                float s = _RippleSpeed;
                slope += d0 * cos(dot(p, d0) * k0 + t * s * 2.1) * 0.5;
                slope += d1 * cos(dot(p, d1) * k1 - t * s * 3.3) * 0.28;
                slope += d2 * cos(dot(p, d2) * k2 + t * s * (1.2 + windStrength * 2.5)) * (0.4 + windStrength * 0.6);
                return slope * _RippleStrength * 0.25;
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float t = _DD_WindVector.w;
                float2 slope = RippleSlope(input.positionWS.xz, t);
                float3 up = NormalizeNormalPerPixel(input.normalWS);
                float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
                // Keep the ripple frame on the authored surface normal (the pond disc is flat, but
                // a tilted surface should still shade with its own up vector).
                normalWS = normalize(lerp(normalWS, up, 0.15));

                // Depth fade: how much water is under this pixel.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceDepth = input.positionCS.w;   // SV_Position.w = view-space depth of this pixel
                float waterDepth = max(sceneDepth - surfaceDepth, 0.0);
                float depthFade = saturate(waterDepth / max(_DepthFade, 0.01));

                half4 tint = lerp(_ShallowColor, _BaseColor, depthFade);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = tint.rgb;
                surfaceData.alpha = tint.a;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0h;
                surfaceData.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Fresnel: grazing views see more sky, straight-down views see the bed.
                half ndv = saturate(dot(normalWS, inputData.viewDirectionWS));
                half fresnel = pow(1.0h - ndv, 4.0h);
                color.a = saturate(tint.a + fresnel * 0.6h);
                color.a *= depthFade * 0.85h + 0.15h;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
