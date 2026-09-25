// WP-075 C5 · DD/FogSheet — "very light" regional ground fog (WP-075 direction Ek A §5.1): a horizontal sheet (unit quad scaled in
// X / Z) with 2-octave world-space noise drifting at its speed, 3 m soft edges, a soft intersection with the scene depth, and a fade
// inside 3 m of the camera (FP: no flat-sheet artefact). The colour is #D3D9DF in shadow and #E9E2D5 under the cloud cookie's light.
// Peak alpha ≤ 0.12 (the ≤ 12 % rule) × the tier alpha. No collision, fogged like the world.
Shader "DD/FogSheet"
{
    Properties
    {
        _ShadowColor("Colour in shadow (#D3D9DF, linear)", Color) = (0.651, 0.694, 0.745, 1)
        _LitColor("Colour lit (#E9E2D5, linear)", Color) = (0.815, 0.761, 0.665, 1)
        _PeakAlpha("Peak alpha (≤ 0.12)", Range(0, 0.12)) = 0.08
        _EdgeSoftness("Edge softness (m)", Range(0.1, 8)) = 3
        _NoiseScale("Noise scale (m)", Range(2, 40)) = 20
        _NoiseSpeed("Drift (m/s)", Range(0, 2)) = 0.4
        _DepthFade("Soft intersection (m)", Range(0.05, 3)) = 0.6
        _TierAlpha("Tier alpha (AtmosphereTierGroup)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "FogSheet"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "DD_CloudCookie.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half4 _LitColor;
                half _PeakAlpha;
                half _EdgeSoftness;
                half _NoiseScale;
                half _NoiseSpeed;
                half _DepthFade;
                half _TierAlpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 size : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float FogHash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float FogNoise(float2 x)
            {
                float2 i = floor(x), f = frac(x), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(FogHash(i), FogHash(i + float2(1, 0)), u.x), lerp(FogHash(i + float2(0, 1)), FogHash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.uv = input.uv;
                output.size = float2(length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                                     length(float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22)));
                output.screenPos = ComputeScreenPos(p.positionCS);
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 edgeUv = min(input.uv, 1.0 - input.uv) * input.size;             // metres to the nearest edge per axis
                half edge = smoothstep(0.0h, _EdgeSoftness, min(edgeUv.x, edgeUv.y));
                float2 drift = _Time.y * _NoiseSpeed * float2(0.707, 0.707);             // toward NE (§5.1)
                half n = 0.65h * FogNoise(input.positionWS.xz / _NoiseScale + drift / _NoiseScale)
                       + 0.35h * FogNoise(input.positionWS.xz / (_NoiseScale * 0.3) - drift / _NoiseScale + 11.7);
                half body = smoothstep(0.25h, 0.85h, n);

                float2 suv = input.screenPos.xy / input.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float sheetEye = input.screenPos.w;
                half soft = saturate((sceneEye - sheetEye) / _DepthFade);
                half nearFade = smoothstep(1.0h, 3.0h, distance(GetCameraPositionWS(), input.positionWS));

                half a = _PeakAlpha * _TierAlpha * edge * body * soft * nearFade;
                half3 c = lerp(_ShadowColor.rgb, _LitColor.rgb, DD_CloudSunLit01(input.positionWS));
                c = MixFog(c, input.fogFactor);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
