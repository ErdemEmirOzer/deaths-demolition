// WP-074 B5b-2 · DD/GroundTiles — the AP-023 4K ground sets (plaza slate, path granite) on the World v5 plaza mesh
// (WorldV5Ground: a 4 m cell grid, per-cell variant material and 0/90/180/270° UV rotation, tangents rotated with it).
// Surface: BaseColor + Normal (4K, 1024 px/m on the 4 m tile) + the packed Mask (R AO, G roughness, B wetness, A puddle; P-05
// ground manifest v001 channels). Wet stone darkens to the P-05 wet albedo — measured albedo × 0.82 in linear light, so a
// constant — and reads flatter and smoother (≤ 0.60, the AP-018 wet cap); standing water (puddle mask × wetness past the
// threshold) goes near-mirror with a flat normal. The lighting path, cookie and passes are DD/GroundBlend's.
Shader "DD/GroundTiles"
{
    Properties
    {
        [MainTexture] _BaseMap("BaseColor", 2D) = "gray" {}
        [Normal] _NormalMap("Normal", 2D) = "bump" {}
        _MaskMap("Mask (R AO, G roughness, B wetness, A puddle)", 2D) = "white" {}
        _NormalStrength("Normal strength", Range(0, 2)) = 1.0
        _Occlusion("AO strength", Range(0, 1)) = 1.0
        _Wetness("World wetness (0 dry … 1 the P-05 wet state)", Range(0, 1)) = 1.0
        _WetAlbedo("Wet albedo scale (linear; P-05 wetalbedo = 0.82)", Range(0.5, 1)) = 0.82
        _WetSmoothness("Wet smoothness cap (AP-018)", Range(0, 0.6)) = 0.60
        _PuddleThreshold("Puddle threshold (mask × wetness)", Range(0, 1)) = 0.45
        _PuddleRamp("Puddle ramp width", Range(0.02, 0.5)) = 0.15
        _PuddleSmoothness("Puddle smoothness", Range(0, 1)) = 0.92
        [Header(Clustered puddles in world space)]
        _PuddleScale("Puddle field scale (m)", Range(2, 20)) = 7.0
        _PuddleField("Puddle field threshold (0.62 ≈ 25 % cover)", Range(0.3, 0.95)) = 0.62
        _PuddleFieldRamp("Puddle field edge", Range(0.01, 0.2)) = 0.06
        [Header(Albedo grade)]
        _AlbedoSaturation("Albedo saturation", Range(0, 1)) = 0.28
        _AlbedoValue("Albedo value scale (linear)", Range(0.5, 4)) = 2.5
        _AlbedoTint("Albedo tint (linear; WP-075 AD S1: the wet slate reads blue-grey 205–215°, not beige)", Color) = (1, 1, 1, 1)
        _TilePuddle("Per-tile puddle mask weight (AD S1: 0 = puddles only from the world field, off the tile grid)", Range(0, 1)) = 1
        [Header(Planar reflection B8)]
        _PlanarStrength("Planar strength", Range(0, 1)) = 1.0
        _PlanarDistortion("Planar distortion (normal)", Range(0, 0.1)) = 0.03
        _PlanarWet("Planar on wet stone (× Fresnel)", Range(0, 1)) = 0.35
        _PuddleDarken("Puddle albedo scale", Range(0.5, 1)) = 0.85
        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _NormalStrength;
            half _Occlusion;
            half _Wetness;
            half _WetAlbedo;
            half _WetSmoothness;
            half _PuddleThreshold;
            half _PuddleRamp;
            half _PuddleSmoothness;
            half _PuddleDarken;
            half _PuddleScale;
            half _PuddleField;
            half _PuddleFieldRamp;
            half _AlbedoSaturation;
            half _AlbedoValue;
            half4 _AlbedoTint;
            half _TilePuddle;
            half _PlanarStrength;
            half _PlanarDistortion;
            half _PlanarWet;
            half _Cull;
        CBUFFER_END

        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);
        TEXTURE2D(_MaskMap);
        // WP-074 B8 · the planar reflection (DD.Art.PlanarReflection, globals; off where no plane publishes one).
        TEXTURE2D(_DD_PlanarReflection);   SAMPLER(sampler_DD_PlanarReflection);
        float _DD_PlanarReflectionOn;

        // Clustered puddles (AD B5b MUST-2): 2-octave value noise in world XZ (7 m / 2.5 m), blobs of 3–5 m across cells.
        float DDGroundHash(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float DDGroundValueNoise(float2 x)
        {
            float2 i = floor(x), f = frac(x), u = f * f * (3.0 - 2.0 * f);
            float a = DDGroundHash(i), b = DDGroundHash(i + float2(1, 0)), c = DDGroundHash(i + float2(0, 1)), d = DDGroundHash(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        half DDPuddleField(float2 xz)
        {
            return 0.65 * DDGroundValueNoise(xz / _PuddleScale) + 0.35 * DDGroundValueNoise(xz / (_PuddleScale * 0.36) + float2(17.3, 5.1));
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
            #pragma vertex TilesVertex
            #pragma fragment TilesFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES   // WP-075 C2: the cloud shadow (main-light cookie)
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
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings TilesVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half4 TilesFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_BaseMap, input.uv);
                half wet = saturate(mask.b * _Wetness);
                half puddle = saturate((mask.a * _Wetness - _PuddleThreshold) / max(_PuddleRamp, 0.02h) + 0.5h) * _TilePuddle;
                half field = saturate((DDPuddleField(input.positionWS.xz) - _PuddleField) / max(_PuddleFieldRamp, 0.01h)) * _Wetness;
                puddle = max(puddle, field);
                wet = max(wet, saturate(field * 2.0h));

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                // Grade (AD MUST-4): the P-05 slate is steel-blue (chroma ≈ 27 %, Y ≈ 0.09) — neutral wet grey, chroma ≤ 8 %, dry Y ≈ 0.22.
                half luma = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                albedo = lerp(luma.xxx, albedo, _AlbedoSaturation) * _AlbedoValue * _AlbedoTint.rgb;
                albedo *= lerp(1.0h, _WetAlbedo, wet);
                albedo *= lerp(1.0h, _PuddleDarken, puddle);

                half smoothness = 1.0h - mask.g;
                smoothness = lerp(smoothness, max(smoothness, _WetSmoothness), wet);
                smoothness = lerp(smoothness, _PuddleSmoothness, puddle);

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_BaseMap, input.uv), _NormalStrength);
                // Water fills the grain: wet stone reads a little flatter, standing water flat.
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), saturate(wet * 0.35h + puddle)));

                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = lerp(1.0h, mask.r, _Occlusion);
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
                if (_DD_PlanarReflectionOn > 0.5)
                {
                    // Standing water mirrors (≥ 50 % at the steep gameplay angles), wet stone by Fresnel only.
                    float2 suv = inputData.normalizedScreenSpaceUV + normalTS.xy * _PlanarDistortion;
                    half3 reflection = SAMPLE_TEXTURE2D(_DD_PlanarReflection, sampler_DD_PlanarReflection, suv).rgb;
                    half ndv = saturate(dot(normalWS, inputData.viewDirectionWS));
                    half fresnel = 0.02h + 0.98h * pow(1.0h - ndv, 5.0h);
                    half w = saturate(max(puddle, _PlanarWet * wet) * lerp(fresnel, 1.0h, 0.5h * puddle)) * _PlanarStrength;
                    color.rgb = lerp(color.rgb, reflection * lerp(1.0h, _PuddleDarken, puddle), w);
                }

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
