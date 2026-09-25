// WP-075 · DEC-102 · Hidden/DD/VolumetricLight — the passes of DD.Art.VolumetricLightFeature.
//   0 Raymarch   low resolution (½ Cinematic, ¼ High): from the camera to the scene depth (≤ _VL_March.y m), _VL_March.x steps with
//                interleaved-gradient jitter; per step the DEC-102 density field (DD_VolumetricLight.hlsl), URP's main-light shadow map
//                (MainLightRealtimeShadow, one hard tap: trees, bamboo, eaves and gates cut the shafts; 1 beyond the shadow distance) × the cloud
//                cookie (DD_CloudSunLit01: a cloud puts a shaft out), a two-lobe phase (Henyey-Greenstein g + an isotropic share so
//                the top-down camera still reads the shafts) and a cold ambient scatter in the shadows. rgb = in-scatter, a = the
//                transmittance.
//   1 Composite  full resolution into the camera colour, Blend One SrcAlpha (scene × T + in-scatter), a depth-weighted 3×3 upsample
//                of the low-resolution result (no halo across silhouettes, the jitter smoothed).
Shader "Hidden/DD/VolumetricLight"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        float4 _VL_March;     // steps, max distance (m), phase g, isotropic share
        float4 _VL_SunDir;    // xyz toward the sun
        float4 _VL_Ambient;   // rgb ambient (sky) scatter colour
        float4 _VL_LowTexel;  // xy 1 / low-resolution size, z depth weight (1/m)

        float DDVLNoise(float2 pixel)   // interleaved gradient noise (Jimenez 2014): a stable per-pixel step jitter
        {
            return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
        }

        bool DDVLIsSky(float raw)
        {
            #if UNITY_REVERSED_Z
                return raw <= 1e-6;
            #else
                return raw >= 1.0 - 1e-6;
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "Raymarch"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMarch
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            // No _SHADOWS_SOFT on purpose: one hard shadow tap per step (URP's high-quality tent is 16 taps, × 32 steps per
            // pixel); the per-pixel jitter and the depth-weighted upsample soften the edges.

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DD_CloudCookie.hlsl"
            #include "DD_VolumetricLight.hlsl"

            float DDVLPhase(float cosT, float g, float iso)
            {
                float g2 = g * g;
                float hg = (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosT, 1e-4), 1.5));
                return lerp(hg, 1.0 / (4.0 * PI), iso);
            }

            half4 FragMarch(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                float3 camera = GetCameraPositionWS();
                float3 d = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP) - camera;
                float len = length(d);
                float3 dir = d / max(len, 1e-4);
                if (DDVLIsSky(raw)) len = _VL_March.y;
                len = min(len, _VL_March.y);

                int steps = max((int)_VL_March.x, 1);
                float stepLen = len / steps;
                float jitter = DDVLNoise(input.positionCS.xy);
                float phase = DDVLPhase(dot(dir, _VL_SunDir.xyz), _VL_March.z, _VL_March.w) * 4.0 * PI;   // 1 = isotropic

                float T = 1.0;
                float3 L = 0;
                [loop]
                for (int s = 0; s < 64; s++)
                {
                    if (s >= steps) break;
                    float3 p = camera + dir * ((s + jitter) * stepLen);
                    float dens = DD_VL_Density(p);
                    if (dens <= 1e-6) continue;
                    float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                    float lit = lerp(0.08, 1.0, DD_CloudSunLit01(p));
                    float3 inscatter = (_DD_VL_Scatter.rgb * phase * shadow * lit + _VL_Ambient.rgb) * dens;
                    float atten = exp(-dens * stepLen);
                    L += T * inscatter * (1.0 - atten) / max(dens, 1e-6);   // exact over the step
                    T *= atten;
                }

                return half4(L, T);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend One SrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            half4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float centre = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                half4 acc = 0;
                float wsum = 0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 tap = uv + float2(x, y) * _VL_LowTexel.xy;
                        float dz = abs(LinearEyeDepth(SampleSceneDepth(tap), _ZBufferParams) - centre);
                        float w = (x == 0 && y == 0 ? 4.0 : (x == 0 || y == 0 ? 2.0 : 1.0)) / (1.0 + dz * _VL_LowTexel.z);
                        acc += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, tap, 0) * w;
                        wsum += w;
                    }
                }

                half4 v = acc / max(wsum, 1e-4);
                return half4(v.rgb, saturate(v.a));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
