// WP-093 · DD/World Vegetation — instanced grass / flora for the living ground (URP 14, forward).
// Alpha-tested, two-sided, GPU-instanced (Graphics.RenderMeshInstanced), SRP-Batcher-compatible CBUFFER for the
// non-instanced path. Wind + interaction + distance density in the vertex stage (DD_VegetationCore.hlsl);
// receives main-light shadows (cascades / soft), additional lights (forward and Forward+), SSAO, fog, light cookies.
// Casts no shadow: there is NO ShadowCaster pass (drawn with ShadowCastingMode.Off anyway). No Meta pass: the instances
// are runtime-only and never lightmapped. Depth passes include only Core.hlsl + the core (no Shadows.hlsl).
Shader "DD/World Vegetation"
{
    Properties
    {
        _BaseMap ("Atlas (RGB, A cut)", 2D) = "white" {}
        _OrmMap ("ORM (R AO, G rough, B translucency)", 2D) = "white" {}
        _BumpMap ("Normal (OpenGL)", 2D) = "bump" {}
        _BumpScale ("Normal strength (0 = none: placeholders)", Float) = 0
        _SatCap ("Saturation ceiling", Range(0, 1)) = 1
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _RootColor ("Root colour", Color) = (0.243, 0.290, 0.188, 1)
        _TipColor ("Tip colour", Color) = (0.384, 0.424, 0.282, 1)
        _DryColor ("Dry colour", Color) = (0.541, 0.525, 0.408, 1)
        _AccentColor ("Placeholder accent A", Color) = (0.392, 0.447, 0.557, 1)
        _AccentColorB ("Placeholder accent B", Color) = (0.557, 0.486, 0.549, 1)
        _TransColor ("Translucency tone", Color) = (0.784, 0.706, 0.494, 1)
        _Cutoff ("Alpha cut", Range(0, 1)) = 0.45
        _Translucency ("Translucency", Range(0, 1)) = 0.2
        _DryFraction ("Dry fraction", Range(0, 1)) = 0.1
        _HueJitter ("Hue jitter (deg)", Float) = 4
        _ValueJitter ("Value jitter", Float) = 0.06
        _SatJitter ("Saturation jitter", Float) = 0.03
        _FarKeep ("Density at fade end", Range(0, 1)) = 0.35
        _VegMeshHeight ("LOD0 mesh height (m)", Float) = 0.15
        _VegLod ("LOD range (lo, hi, half fade, level)", Vector) = (0, 100000, 1, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "DD_VegetationCore.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            AlphaToMask Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VegVertex
            #pragma fragment VegFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  color      : TEXCOORD3;
                float4 data       : TEXCOORD4;   // gust, camDist, fogFactor, -
                float3 hash       : TEXCOORD5;
                float3 rootWS     : TEXCOORD6;
                half4  tangentWS  : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VegVertex(DDVegAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                DDVegVertex v = DDVeg_Vertex(input);
                output.positionWS = v.positionWS;
                output.positionCS = TransformWorldToHClip(v.positionWS);
                output.normalWS = (half3)v.normalWS;
                output.uv = input.uv;
                output.color = (half4)input.color;
                output.data = float4(v.gust, v.camDist, ComputeFogFactor(output.positionCS.z), 0.0);
                output.hash = v.hash;
                float4x4 m = GetObjectToWorldMatrix();
                output.rootWS = float3(m._m03, m._m13, m._m23);
                float3 t = input.tangentOS.xyz;
                output.tangentWS = dot(t, t) > 1e-6 ? half4((half3)TransformObjectToWorldDir(t), (half)(input.tangentOS.w * GetOddNegativeScale())) : half4(0, 0, 0, 0);
                return output;
            }

            half3 VegLight(Light light, half3 albedo, half3 n, half3 v, half trans)
            {
                half atten = light.distanceAttenuation * light.shadowAttenuation;
                half ndl = saturate((dot(n, light.direction) + 0.35h) / 1.35h);   // wrapped: thin leaves
                half3 direct = albedo * ndl;
                // Back-light transmission (bible §3 Işık): tone × light, zero in shadow (atten), added luma capped.
                half back = pow(saturate(dot(v, -light.direction) * 0.5h + 0.5h), 3.0h);
                half3 transmit = _TransColor.rgb * (trans * back);
                half tl = dot(transmit * light.color * atten, half3(0.2126h, 0.7152h, 0.0722h));
                transmit *= min(1.0h, 0.045h / max(tl, 1e-4h));
                return (direct + transmit) * light.color * atten;
            }

            half4 VegFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                DDVeg_Clip(input.uv, input.positionCS, input.data.y);

                float gust = input.data.x;
                half3 albedo = DDVeg_Albedo(input.uv, input.color, input.hash, input.rootWS, gust);
                half4 orm = SAMPLE_TEXTURE2D(_OrmMap, sampler_OrmMap, input.uv);
                half y01 = sqrt(saturate(input.color.r));
                half ao = orm.r * input.color.a;
                half trans = orm.b * _Translucency * (1.0h + 0.25h * (half)gust) * saturate((y01 - 0.5h) * 2.0h);   // upper half only

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                half3 n = NormalizeNormalPerPixel(input.normalWS);
                if (_BumpScale > 0.0h && dot(input.tangentWS.xyz, input.tangentWS.xyz) > 1e-4h)
                {
                    half3 nts = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                    half3 tws = normalize(input.tangentWS.xyz);
                    half3 bws = cross(n, tws) * input.tangentWS.w;
                    n = NormalizeNormalPerPixel(tws * nts.x + bws * nts.y + n * nts.z);
                }
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) || defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.data.z);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
                inputData.bakedGI = SampleSH(n);

                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, ao);
                half3 v = inputData.viewDirectionWS;
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                half3 color = inputData.bakedGI * albedo * aoFactor.indirectAmbientOcclusion;
                color += VegLight(mainLight, albedo, n, v, trans) * lerp(0.6h, 1.0h, aoFactor.directAmbientOcclusion * ao);

                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    #if USE_FORWARD_PLUS
                    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                        color += VegLight(light, albedo, n, v, trans) * ao;
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                        color += VegLight(light, albedo, n, v, trans) * ao;
                    LIGHT_LOOP_END
                #endif

                color = MixFog(color, inputData.fogCoord);
                return half4(color, 1.0h);
            }
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
            #pragma target 3.5
            #pragma vertex VegDepthVertex
            #pragma fragment VegDepthFragment
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  camDist    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VegDepthVertex(DDVegAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                DDVegVertex v = DDVeg_Vertex(input);
                output.positionCS = TransformWorldToHClip(v.positionWS);
                output.uv = input.uv;
                output.camDist = v.camDist;
                return output;
            }

            half VegDepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DDVeg_Clip(input.uv, input.positionCS, input.camDist);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VegDepthNormalsVertex
            #pragma fragment VegDepthNormalsFragment
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  camDist    : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VegDepthNormalsVertex(DDVegAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                DDVegVertex v = DDVeg_Vertex(input);
                output.positionCS = TransformWorldToHClip(v.positionWS);
                output.uv = input.uv;
                output.camDist = v.camDist;
                output.normalWS = (half3)v.normalWS;
                return output;
            }

            half4 VegDepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DDVeg_Clip(input.uv, input.positionCS, input.camDist);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
