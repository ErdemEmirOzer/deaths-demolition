// WP-075 C4 B + C5 §5.2 · Hidden/DD/ScreenAtmosphere — the passes of DD.Art.ScreenAtmosphereFeature (TP / FP, High / Cinematic).
//   0 Mask      half resolution: 1 where the scene depth is sky (roofs, trees, bamboo, lattices occlude), × a 0.3-screen disc
//               around the sun's screen position (_DD_SunScreen.xy; z = aspect) — a wider mask reads as a milky glow, not rays.
//   1 Blur      radial blur of the mask toward the sun (GPU Gems 3 ch. 13): _DD_ShaftParams = (samples, density, weight, decay),
//               normalised so an open-sky ray reads 1.
//   2 Composite into the camera colour, Blend One OneMinusSrcAlpha: rgb = fog colour × fog + shafts, a = fog. Height fog: base
//               y, density / m, falloff k (ln 2 / 5 m), cap — integrated along the view ray; 20 % toward the sun colour facing
//               the sun. Shafts: × tone × strength (_DD_ShaftTone, CPU) × the cloud cookie at the camera; FP: ≤ 0.25 inside the
//               6° reticle cone (_DD_SunScreen.w = 1).
Shader "Hidden/DD/ScreenAtmosphere"
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
        #include "DD_CloudCookie.hlsl"

        float4 _DD_SunScreen;          // xy sun viewport, z aspect, w FP
        float4 _DD_ShaftParams;        // samples, density, weight, decay
        float4 _DD_ShaftTone;          // linear tone × strength
        float4 _DD_HeightFogParams;    // base y, density / m, k, cap
        float4 _DD_HeightFogColor;
        float4 _DD_HeightFogSunColor;
        float4 _DD_SunDirWS;           // xyz toward the sun, w in-scatter mix

        bool DDIsSky(float raw)
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
            Name "ShaftMask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMask

            half4 FragMask(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float raw = SampleSceneDepth(input.texcoord);
                float2 d = (input.texcoord - _DD_SunScreen.xy) * float2(_DD_SunScreen.z, 1.0);
                half fall = saturate(1.0 - length(d) / 0.3);   // a sun disc: the blur turns its occluded edges into rays
                return (DDIsSky(raw) ? 1.0h : 0.0h) * fall * fall;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShaftBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur

            half4 FragBlur(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                int samples = (int)_DD_ShaftParams.x;
                float2 delta = (uv - _DD_SunScreen.xy) * (_DD_ShaftParams.y / max(samples, 1));
                half illum = 1.0h, acc = 0.0h, norm = 0.0h;
                [loop]
                for (int s = 0; s < 64; s++)
                {
                    if (s >= samples) break;
                    uv -= delta;
                    half m = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
                    acc += m * illum * _DD_ShaftParams.z;
                    norm += illum * _DD_ShaftParams.z;
                    illum *= _DD_ShaftParams.w;
                }

                return acc / max(norm, 1e-4h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            half4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                float3 camera = GetCameraPositionWS();
                float3 d = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP) - camera;
                float len = length(d);
                float3 v = d / max(len, 1e-4);
                if (DDIsSky(raw)) len = 400.0;

                // Height fog: ρ(y) = density · e^(−k (y − base)), integrated from the camera along the ray.
                float4 f = _DD_HeightFogParams;
                float a0 = f.y * exp(-f.z * (camera.y - f.x));
                float kv = f.z * v.y;
                float optical = abs(kv * len) > 1e-3 ? a0 * (1.0 - exp(-kv * len)) / kv : a0 * len;
                half fog = min(1.0 - exp(-optical), f.w);
                half facing = smoothstep(0.3h, 1.0h, saturate(dot(v, _DD_SunDirWS.xyz)));
                half3 fogColor = lerp(_DD_HeightFogColor.rgb, _DD_HeightFogSunColor.rgb, _DD_SunDirWS.w * facing);

                half shaft = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
                half3 add = _DD_ShaftTone.rgb * shaft * DD_CloudSunLit01(camera);
                if (_DD_SunScreen.w > 0.5)
                {
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    half peak = max(add.r, max(add.g, add.b));
                    if (dot(v, forward) > 0.99452 && peak > 0.25h) add *= 0.25h / peak;   // cos 6°
                }

                return half4(fogColor * fog + add, fog);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
