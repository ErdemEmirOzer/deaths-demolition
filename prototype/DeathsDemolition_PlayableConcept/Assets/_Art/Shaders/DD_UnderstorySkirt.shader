// DEC-108 AD MUST 2 (01.10) · DD/UnderstorySkirt — the dark understory behind the vista forest (WorldV5Atmosphere.Skirt):
// unlit (a sunlit skirt would glow on its south face), opaque, the trunk-shadow tone; fogged like the world (MixFog) and hazed
// like the vista trees (_DD_VegHaze, M-46), so the gaps between trunks read as forest depth, not as sky and not as a wall.
// Depth passes so the screen-space atmosphere and SSAO see it as geometry.
Shader "DD/UnderstorySkirt"
{
    Properties
    {
        [MainColor] _BaseColor("Understory tone", Color) = (0.10, 0.11, 0.095, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "UnderstorySkirt"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            float4 _DD_VegHaze;   // WorldAtmosphere.ApplyEnvironment: x start, y end (m), z max — the vista trees' distance haze

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 color = MixFog(_BaseColor.rgb, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                // The same ramp as DD/Foliage_Wind (M-46), so the skirt fades with the trunks in front of it.
                half vegHaze = saturate((distance(input.positionWS, _WorldSpaceCameraPos) - _DD_VegHaze.x) / max(1e-3, _DD_VegHaze.y - _DD_VegHaze.x)) * _DD_VegHaze.z;
                return half4(lerp(color, unity_FogColor.rgb, vegHaze), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFragment(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h); }
            ENDHLSL
        }
    }
    FallBack Off
}
