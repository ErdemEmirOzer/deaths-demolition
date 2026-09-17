// WP-038 · DD/GroundBlend — the AP-015P ground / route / paving plates (ADR-038-1). One material for
// every plate: the vertex colour AP015_Mask (R earth, G gravel, B stone, A wetness; R+G+B = 1, linear)
// blends the three ADR-015-1 Ground tiles sampled at world xz / _Tile (4 m — the stand-in / hero_earth
// tile, so plates and the golden-slice ground share one texture space). Gravel has no normal map
// (flat) and is toned by _GravelTint (0.60: T_gravel's albedo Y 0.286 would break the value order).
// Wetness (A) darkens the albedo by up to 45 % and lifts smoothness towards _WetSmoothness (hard cap
// 0.60 — no mirror-wet). Realtime lit + light probes (plates are not lightmapped: AP-015P contract);
// DepthNormals pass so the WP-029 SSAO sees the ground; ShadowCaster kept for the raised kerbs/paving.
Shader "DD/GroundBlend"
{
    Properties
    {
        _EarthMap("Earth BaseColor", 2D) = "gray" {}
        [Normal] _EarthNormal("Earth Normal", 2D) = "bump" {}
        _EarthMask("Earth MetallicSmoothness (A = smoothness)", 2D) = "black" {}
        _GravelMap("Gravel BaseColor", 2D) = "gray" {}
        _StoneMap("Stone BaseColor", 2D) = "gray" {}
        [Normal] _StoneNormal("Stone Normal", 2D) = "bump" {}
        _StoneMask("Stone MetallicSmoothness (A = smoothness)", 2D) = "black" {}

        _Tile("World tile (m)", Range(1.0, 16.0)) = 4.0
        _GravelTint("Gravel tint", Range(0.2, 1.0)) = 0.60
        _EarthSmoothness("Earth smoothness fallback (1 − roughness)", Range(0, 1)) = 0.08
        _GravelSmoothness("Gravel smoothness", Range(0, 1)) = 0.15
        _StoneSmoothness("Stone smoothness fallback", Range(0, 1)) = 0.43
        _UseMasks("Use MetallicSmoothness maps (0 = fallbacks)", Range(0, 1)) = 1.0
        _WetDarken("Wet darkening", Range(0, 1)) = 0.45
        _WetSmoothness("Wet smoothness (cap)", Range(0, 0.6)) = 0.60
        _NormalStrength("Normal strength", Range(0, 2)) = 1.0
        _Occlusion("Occlusion (vertex mask edges)", Range(0, 1)) = 1.0

        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _EarthMap_ST;
            half _Tile;
            half _GravelTint;
            half _EarthSmoothness;
            half _GravelSmoothness;
            half _StoneSmoothness;
            half _UseMasks;
            half _WetDarken;
            half _WetSmoothness;
            half _NormalStrength;
            half _Occlusion;
            half _Cull;
        CBUFFER_END

        TEXTURE2D(_EarthMap);    SAMPLER(sampler_EarthMap);
        TEXTURE2D(_EarthNormal); SAMPLER(sampler_EarthNormal);
        TEXTURE2D(_EarthMask);   SAMPLER(sampler_EarthMask);
        TEXTURE2D(_GravelMap);   SAMPLER(sampler_GravelMap);
        TEXTURE2D(_StoneMap);    SAMPLER(sampler_StoneMap);
        TEXTURE2D(_StoneNormal); SAMPLER(sampler_StoneNormal);
        TEXTURE2D(_StoneMask);   SAMPLER(sampler_StoneMask);

        // Mask weights: normalise R/G/B (the exporter keeps R+G+B = 1, a bilinear seam can drift).
        half3 GroundWeights(half4 mask)
        {
            half3 w = max(mask.rgb, 0.0h);
            half sum = w.r + w.g + w.b;
            return sum > 1e-4h ? w / sum : half3(1.0h, 0.0h, 0.0h);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GroundVertex
            #pragma fragment GroundFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                half4  color      : COLOR;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  tangentWS  : TEXCOORD2;   // xyz tangent, w sign
                half4  mask       : TEXCOORD3;   // R earth, G gravel, B stone, A wet
                half   fogFactor  : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GroundVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.mask = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half4 GroundFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.positionWS.xz / max(_Tile, 0.01);
                half3 w = GroundWeights(input.mask);
                half wet = saturate(input.mask.a);

                half3 earth = SAMPLE_TEXTURE2D(_EarthMap, sampler_EarthMap, uv).rgb;
                half3 gravel = SAMPLE_TEXTURE2D(_GravelMap, sampler_GravelMap, uv).rgb * _GravelTint;
                half3 stone = SAMPLE_TEXTURE2D(_StoneMap, sampler_StoneMap, uv).rgb;
                half3 albedo = earth * w.r + gravel * w.g + stone * w.b;
                albedo *= 1.0h - _WetDarken * wet;

                half earthS = _EarthSmoothness, stoneS = _StoneSmoothness;
                if (_UseMasks > 0.5h)
                {
                    earthS = SAMPLE_TEXTURE2D(_EarthMask, sampler_EarthMask, uv).a;
                    stoneS = SAMPLE_TEXTURE2D(_StoneMask, sampler_StoneMask, uv).a;
                }

                half smoothness = earthS * w.r + _GravelSmoothness * w.g + stoneS * w.b;
                smoothness = lerp(smoothness, _WetSmoothness, wet);
                smoothness = min(smoothness, 0.60h);

                half3 nEarth = UnpackNormalScale(SAMPLE_TEXTURE2D(_EarthNormal, sampler_EarthNormal, uv), _NormalStrength);
                half3 nStone = UnpackNormalScale(SAMPLE_TEXTURE2D(_StoneNormal, sampler_StoneNormal, uv), _NormalStrength);
                half3 normalTS = normalize(nEarth * w.r + half3(0, 0, 1) * w.g + nStone * w.b);
                // Wet surfaces read flatter (water fills the grain).
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), wet * 0.5h));

                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = lerp(1.0h, 0.85h, _Occlusion * saturate(1.0h - max(w.r, max(w.g, w.b))) * 2.0h);   // blend seams sit a touch lower
                surfaceData.normalTS = normalTS;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // Shadows.hlsl uses LerpWhiteTo from CommonMaterial.hlsl (URP's Lit pass gets it through LitInput.hlsl).
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

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
            Cull [_Cull]

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

            // The SSAO feature reads the geometric normal here (world space, like URP's Lit DepthNormals); the
            // detail normal map is not needed for occlusion on a near-flat plate.
            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
