// WP-038 · DD/Water v2 — the three koi basins on the AP-015P water meshes (ADR-038-1). Replaces DD_Water_v1
// (kept on disk, unreferenced). P-05's contract (`AP015-surfaces-manifest-v001.json` shader_contract.water):
//   colour   deep sRGB (0.180, 0.294, 0.310) / shallow (0.240, 0.330, 0.330), depth fade 0.6 m from the scene
//            depth (mesh vertex colour R = depth / 0.6 m is the fallback when the depth texture is off),
//            smoothness 0.88, Fresnel power 4;
//   normals  large tile 4 m, 0.03 m/s, 0.35 + small tile 1 m, 0.06 m/s, 0.25 (OpenGL +Y, BC5);
//   flow     world map x −16..16 / z −46..−14 (PNG top row = north = +z), RG = flow (clockwise), B = water mask,
//            0.04 m/s at full magnitude — the normal layers are advected by it (two-phase flow map);
//   foam     tile 2 m, intensity 0.20 in the shore band (mesh vertex colour G) 0.25 m — a wet scum line, no crests;
//   sparkle  tile 1.5 m, density 0.15, cap 0.30 (specular stays point-like at the −49° gameplay camera);
//   caustic  4×4 sheet, 8 fps, tile 2 m, intensity 0.18, only where depth ≥ 0.15 m.
// Rain rings / koi rings (rings sheet) are a VFX-side emitter (S-07 drizzle state), not this shader.
Shader "DD/Water v2"
{
    Properties
    {
        [MainColor] _BaseColor("Deep Color", Color) = (0.180, 0.294, 0.310, 0.92)
        _ShallowColor("Shallow Color", Color) = (0.240, 0.330, 0.330, 0.45)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.88
        _FresnelPower("Fresnel Power", Range(1.0, 8.0)) = 4.0
        _DepthFade("Depth Fade (m)", Range(0.05, 3.0)) = 0.6

        [Normal] _NormalLarge("Normal Large (4 m)", 2D) = "bump" {}
        [Normal] _NormalSmall("Normal Small (1 m)", 2D) = "bump" {}
        _NormalLargeTile("Large tile (m)", Float) = 4.0
        _NormalSmallTile("Small tile (m)", Float) = 1.0
        _NormalLargeSpeed("Large speed (m/s)", Float) = 0.03
        _NormalSmallSpeed("Small speed (m/s)", Float) = 0.06
        _NormalLargeStrength("Large strength", Range(0, 1)) = 0.35
        _NormalSmallStrength("Small strength", Range(0, 1)) = 0.25

        _FlowMap("Flow Map (RG flow, B mask)", 2D) = "gray" {}
        _FlowBounds("Flow bounds (xMin, zMin, xMax, zMax)", Vector) = (-16, -46, 16, -14)
        _FlowSpeed("Flow speed (m/s)", Float) = 0.04
        _FlowPeriod("Flow phase period (s)", Float) = 3.0

        _FoamMap("Foam (tile 2 m)", 2D) = "black" {}
        _FoamTile("Foam tile (m)", Float) = 2.0
        _FoamIntensity("Foam intensity", Range(0, 1)) = 0.20
        _SparkleMap("Sparkle (tile 1.5 m)", 2D) = "black" {}
        _SparkleTile("Sparkle tile (m)", Float) = 1.5
        _SparkleDensity("Sparkle density", Range(0, 1)) = 0.15
        _SparkleCap("Sparkle cap", Range(0, 1)) = 0.30
        _CausticMap("Caustic sheet 4×4", 2D) = "black" {}
        _CausticTile("Caustic tile (m)", Float) = 2.0
        _CausticFps("Caustic fps", Float) = 8.0
        _CausticIntensity("Caustic intensity", Range(0, 1)) = 0.18
        _CausticMinDepth("Caustic min depth (m)", Float) = 0.15

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
        LOD 300

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
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShallowColor;
                half _Smoothness;
                half _FresnelPower;
                half _DepthFade;
                float _NormalLargeTile, _NormalSmallTile, _NormalLargeSpeed, _NormalSmallSpeed;
                half _NormalLargeStrength, _NormalSmallStrength;
                float4 _FlowBounds;
                float _FlowSpeed, _FlowPeriod;
                float _FoamTile; half _FoamIntensity;
                float _SparkleTile; half _SparkleDensity, _SparkleCap;
                float _CausticTile, _CausticFps; half _CausticIntensity; float _CausticMinDepth;
                half _Surface, _Blend, _Cull, _SrcBlend, _DstBlend, _ZWrite, _QueueOffset;
            CBUFFER_END

            TEXTURE2D(_NormalLarge); SAMPLER(sampler_NormalLarge);
            TEXTURE2D(_NormalSmall); SAMPLER(sampler_NormalSmall);
            TEXTURE2D(_FlowMap);     SAMPLER(sampler_FlowMap);
            TEXTURE2D(_FoamMap);     SAMPLER(sampler_FoamMap);
            TEXTURE2D(_SparkleMap);  SAMPLER(sampler_SparkleMap);
            TEXTURE2D(_CausticMap);  SAMPLER(sampler_CausticMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;      // R depth / 0.6 m (fallback), G shore mask
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
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
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Flow map lookup in world space (PNG top row = north = +z → v flips).
            float3 Flow(float2 xz)
            {
                float2 uv = (xz - _FlowBounds.xy) / max(_FlowBounds.zw - _FlowBounds.xy, 1e-3);
                float inside = (uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1) ? 1.0 : 0.0;
                half4 f = SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, saturate(uv));
                float2 dir = (f.rg * 2.0 - 1.0) * inside * f.b;
                return float3(dir, f.b * inside);
            }

            // Two-phase flow advection: the same layer sampled at two half-period offsets, cross-faded.
            half3 FlowNormal(TEXTURE2D_PARAM(tex, smp), float2 xz, float tile, float speed, float2 flow, float t, half strength)
            {
                float2 base = xz / tile;
                float2 drift = flow * (_FlowSpeed * t / tile);
                float2 scroll = float2(0.71, 0.71) * (speed * t / tile);
                float phase = frac(t / max(_FlowPeriod, 0.1));
                float2 offA = base + scroll + drift * 0.0 - flow * (phase * _FlowPeriod * _FlowSpeed / tile);
                float2 offB = base + scroll + float2(0.37, 0.61) - flow * (frac(phase + 0.5) * _FlowPeriod * _FlowSpeed / tile);
                half wA = abs(phase * 2.0 - 1.0);
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, offA), strength);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, offB), strength);
                return normalize(lerp(nA, nB, 1.0h - wA));
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float t = _Time.y;
                float2 xz = input.positionWS.xz;
                float3 flowInfo = Flow(xz);
                float2 flow = flowInfo.xy;

                // Depth: scene depth under the pixel; the mesh's R channel (depth / 0.6 m) when the depth texture is unavailable.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceDepth = input.positionCS.w;
                float waterDepth = max(sceneDepth - surfaceDepth, 0.0);
                float depthFade = saturate(waterDepth / max(_DepthFade, 0.01));
                float meshFade = saturate(input.color.r);
                depthFade = max(depthFade, meshFade * 0.5);   // never fully clear over a dry depth read

                // Normals: two advected layers in the surface's tangent frame (flat pond: tangent = +x, bitangent = +z).
                half3 nLarge = FlowNormal(TEXTURE2D_ARGS(_NormalLarge, sampler_NormalLarge), xz, _NormalLargeTile, _NormalLargeSpeed, flow, t, _NormalLargeStrength);
                half3 nSmall = FlowNormal(TEXTURE2D_ARGS(_NormalSmall, sampler_NormalSmall), xz, _NormalSmallTile, _NormalSmallSpeed, flow, t, _NormalSmallStrength);
                half3 nTS = normalize(half3(nLarge.xy + nSmall.xy, nLarge.z * nSmall.z));
                half3 up = NormalizeNormalPerPixel(input.normalWS);
                half3 normalWS = normalize(half3(nTS.x, nTS.z, nTS.y));   // tangent x → world x, tangent y → world z
                normalWS = normalize(lerp(normalWS, up, 0.15h));

                half4 tint = lerp(_ShallowColor, _BaseColor, depthFade);

                // Caustics on the bed, seen through the water where it is deep enough.
                float frame = floor(t * _CausticFps) % 16.0;
                float2 cuv = frac(xz / _CausticTile);
                float2 cell = float2(fmod(frame, 4.0), 3.0 - floor(frame / 4.0));
                half caustic = SAMPLE_TEXTURE2D(_CausticMap, sampler_CausticMap, (cuv + cell) * 0.25).r;
                half causticMask = saturate((waterDepth - _CausticMinDepth) / 0.15) * (1.0h - depthFade * 0.5h);
                tint.rgb += caustic * _CausticIntensity * causticMask;

                // Shore scum line (mesh G) — a low-contrast tone, never white crests.
                half foam = SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, xz / _FoamTile + flow * (t * _FlowSpeed / _FoamTile)).r;
                half shore = saturate(input.color.g);
                half3 foamTint = half3(0.62h, 0.66h, 0.62h);
                tint.rgb = lerp(tint.rgb, foamTint, foam * shore * _FoamIntensity);
                tint.a = saturate(tint.a + foam * shore * _FoamIntensity * 0.5h);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = tint.rgb;
                surfaceData.alpha = tint.a;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0h;
                surfaceData.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = screenUV;
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Sparkle: a sparse specular glint sheet, capped so the highlight stays point-like.
                half sparkle = SAMPLE_TEXTURE2D(_SparkleMap, sampler_SparkleMap, xz / _SparkleTile + float2(0.13, -0.09) * t).r;
                Light mainLight = GetMainLight();
                half3 h = normalize(mainLight.direction + inputData.viewDirectionWS);
                half glint = pow(saturate(dot(normalWS, h)), 64.0h);
                color.rgb += mainLight.color * min(sparkle * _SparkleDensity * glint * 4.0h, _SparkleCap);

                half ndv = saturate(dot(normalWS, inputData.viewDirectionWS));
                half fresnel = pow(1.0h - ndv, _FresnelPower);
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
