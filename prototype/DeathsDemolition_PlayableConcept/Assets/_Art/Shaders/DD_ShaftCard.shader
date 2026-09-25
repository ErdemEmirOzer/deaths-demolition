// WP-075 C4 · DD/ShaftCard — world-space sun shaft card (WP-075 direction Ek A §3 A). The mesh is a unit quad (uv only): the vertex
// shader lays it along the light's travel direction (_DD_ShaftDir, CloudShadowDriver) from the object origin (the card's top, in the
// canopy / opening), billboarded about that axis; length = the object's Y scale, width = its X scale. Brightness = peak alpha × the
// cloud cookie sampled at the top (the same projection as the ground shadow), so a shaft lights only while a cloud gap passes.
// Additive, HDR < 1 (never over the bloom threshold), 0 over the last 1.5 m to the ground, slow noise, fogged.
// WP-075 C7b (the HF-west frame showed the grove's cards as flat orange slabs): a beam profile across the whole width (no flat top),
// brightness falling off down the shaft, a soft intersection with the scene depth, faded inside 4–12 m of the camera and when the
// view runs along the shaft (the card collapses there).
Shader "DD/ShaftCard"
{
    Properties
    {
        _Color("Tone (#FFE7C4, linear)", Color) = (1, 0.80, 0.55, 1)
        _PeakAlpha("Peak alpha", Range(0, 0.5)) = 0.2
        _EdgeSoftness("Edge softness (m, legacy: the beam profile spans the width)", Range(0.05, 2)) = 0.4
        _AlongFalloff("Brightness at the ground end (× top)", Range(0, 1)) = 0.35
        _DepthFade("Soft intersection (m)", Range(0.05, 3)) = 0.8
        _NearFade("Camera fade (start, end m)", Vector) = (4, 12, 0, 0)
        _NoiseScale("Noise scale (m)", Range(0.5, 20)) = 5
        _NoiseSpeed("Noise speed (m/s)", Range(0, 2)) = 0.25
        _TierAlpha("Tier alpha (AtmosphereTierGroup)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ShaftCard"
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
                half4 _Color;
                half _PeakAlpha;
                half _EdgeSoftness;
                half _NoiseScale;
                half _NoiseSpeed;
                half _TierAlpha;
                half _AlongFalloff;
                half _DepthFade;
                float4 _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half lit : TEXCOORD2;
                half width : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                float4 screenPos : TEXCOORD5;
                half axial : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ShaftHash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ShaftNoise(float2 x)
            {
                float2 i = floor(x), f = frac(x), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(ShaftHash(i), ShaftHash(i + float2(1, 0)), u.x), lerp(ShaftHash(i + float2(0, 1)), ShaftHash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float width = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float len = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                float3 axis = normalize(_DD_ShaftDir.xyz + float3(0, -1e-4, 0));
                float3 toCamera = normalize(GetCameraPositionWS() - origin);
                float3 side = cross(axis, toCamera);
                side = dot(side, side) > 1e-6 ? normalize(side) : float3(1, 0, 0);
                float3 p = origin + axis * (input.uv.y * len) + side * ((input.uv.x - 0.5) * width);
                output.positionWS = p;
                output.positionCS = TransformWorldToHClip(p);
                output.uv = input.uv;
                output.lit = DD_CloudSunLit01(origin);
                output.width = width;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.axial = abs(dot(axis, toCamera));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half across = 1.0h - abs(input.uv.x * 2.0h - 1.0h);                       // 1 on the axis, 0 at the edges
                half edge = across * across * (3.0h - 2.0h * across);                   // beam profile over the whole width
                edge *= edge;
                half along = smoothstep(0.0h, 0.12h, input.uv.y) * saturate(input.positionWS.y / 1.5h);   // soft top, 0 at the ground
                along *= lerp(1.0h, _AlongFalloff, input.uv.y);
                float2 suv = input.screenPos.xy / input.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half soft = saturate((sceneEye - input.screenPos.w) / _DepthFade);
                half nearFade = saturate((input.screenPos.w - _NearFade.x) / max(_NearFade.y - _NearFade.x, 0.01));
                half view = 1.0h - pow(saturate(input.axial), 4.0h);
                half noise = lerp(0.6h, 1.0h, ShaftNoise(input.positionWS.xz / _NoiseScale + _Time.y * _NoiseSpeed));
                half a = _PeakAlpha * _TierAlpha * input.lit * edge * along * noise * soft * nearFade * view;
                half3 c = min(_Color.rgb * a, 0.99h);
                c = MixFogColor(c, half3(0, 0, 0), input.fogFactor);   // additive: fog fades it to nothing
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
