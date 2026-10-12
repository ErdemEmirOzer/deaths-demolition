// WP-019 · DD/VFX Flipbook — unlit sheet player for the AP-010 flipbooks, cards and decals.
//   _Sheet   RGBA sheet (straight alpha), _Cols/_Rows grid, per-instance _Frame (MaterialPropertyBlock)
//   _EmissiveTex  optional additive layer sampled at the same cell (armour impact, muzzle, explosion)
//   _Tint      colour multiplier (beacon / vignette runtime tint), _Alpha instance fade
//   _SrcBlend/_DstBlend  alpha (5/10) or additive (1/1); ZWrite off; fog applied for alpha, not additive
//   WP-101 (R4, AP-041): _Premultiplied 1 = a premultiplied sheet on Blend One OneMinusSrcAlpha (1/10): rgb is never
//   multiplied by a again and a is never forced to 1; fades scale rgb and a together. 0 (default) = the paths above, unchanged.
//   WP-019b: procedural (GPU) instancing for ParticleSystemRenderer mesh mode (blood droplets):
//   the per-particle transform and colour come from unity_ParticleInstanceData (URP ParticlesInstancing.hlsl).
// One material per catalog row; the frame index and fade come through a property block, so the
// pooled quads share the material (instance ≤ 1 material, ADR-019-1).
Shader "DD/VFX Flipbook"
{
    Properties
    {
        [MainTexture] _Sheet("Sheet", 2D) = "white" {}
        _EmissiveTex("Emissive sheet", 2D) = "black" {}
        [MainColor] _Tint("Tint", Color) = (1,1,1,1)
        _EmissiveStrength("Emissive strength", Range(0, 4)) = 1.0
        _Cols("Columns", Float) = 1
        _Rows("Rows", Float) = 1
        _Frame("Frame", Float) = 0
        _Alpha("Alpha", Range(0, 1)) = 1
        _AlphaCap("Alpha cap", Range(0, 1)) = 1
        [Toggle] _UseEmissive("Use emissive", Float) = 0
        _ScrollV("UV scroll v (1/s)", Float) = 0
        [HideInInspector] _SrcBlend("Src", Float) = 5
        [HideInInspector] _DstBlend("Dst", Float) = 10
        [HideInInspector] _Cull("Cull", Float) = 0
        [HideInInspector] _ZTest("ZTest", Float) = 4
        [HideInInspector] _Premultiplied("Premultiplied (WP-101)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ParticleInstancingSetup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl"

            TEXTURE2D(_Sheet);      SAMPLER(sampler_Sheet);
            TEXTURE2D(_EmissiveTex);  SAMPLER(sampler_EmissiveTex);

            // All float: a half/float mix in a constant buffer mis-packed on Metal (WP-019 diagnosis:
            // _ScrollV read garbage, v collapsed to 0 and every sheet sampled its transparent border).
            CBUFFER_START(UnityPerMaterial)
                float4 _Sheet_ST;
                float4 _Tint;
                float _EmissiveStrength;
                float _Cols;
                float _Rows;
                float _Frame;
                float _Alpha;
                float _AlphaCap;
                float _UseEmissive;
                float _ScrollV;
                float _SrcBlend;
                float _DstBlend;
                float _Cull;
                float _ZTest;
                float _Premultiplied;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                half4  color      : TEXCOORD2;
                half   fogFactor  : TEXCOORD1;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Cell from the frame index: row-major, row 0 at the TOP of the sheet (manifest).
                float cols = max(_Cols, 1.0);
                float rows = max(_Rows, 1.0);
                float frame = floor(_Frame + 0.5);
                float col = fmod(frame, cols);
                float row = floor(frame / cols);
                float2 cell = float2(col / cols, 1.0 - (row + 1.0) / rows);
                float2 uv = input.uv;
                if (_ScrollV != 0.0)
                {
                    uv.y = frac(uv.y + _ScrollV * _Time.y);   // beacon column: scroll up
                }

                output.uv = cell + uv / float2(cols, rows);
                output.color = input.color;
#if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
                // Mesh-mode particles: the particle colour travels in the instance buffer, the mesh colour is white.
                uint pc = unity_ParticleInstanceData[unity_InstanceID].color;
                output.color *= half4(pc & 255u, (pc >> 8u) & 255u, (pc >> 16u) & 255u, (pc >> 24u) & 255u) / 255.0h;
#endif
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // WP-101 (R4, AP-041): premultiplied sheets. The file's rgb already carries its coverage (sRGB(linear × a); the
            // sampler decodes it), so every fade - the block _Alpha, the tint / vertex alpha - scales rgb and a ONCE, the cap
            // rescales both by the same ratio (newA / oldA), and a is never forced to 1 (Blend One OneMinusSrcAlpha keeps the
            // background). The tint's rgb (HDR allowed) multiplies colour only; fog lerps towards fogColour × a.
            half4 FragPremultiplied(Varyings input)
            {
                half4 s = SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, input.uv);
                float4 tint = _Tint * (float4)input.color;
                float fade = tint.a * _Alpha;
                float aRaw = s.a * fade;
                float a = min(aRaw, _AlphaCap);
                float k = fade * (a / max(aRaw, 1e-5));
                float3 rgb = (float3)s.rgb * tint.rgb * k;
                if (_UseEmissive > 0.5)
                {
                    half4 e = SAMPLE_TEXTURE2D(_EmissiveTex, sampler_EmissiveTex, input.uv);
                    rgb += e.rgb * e.a * _EmissiveStrength * _Alpha;
                }

                rgb = MixFogColor(rgb, unity_FogColor.rgb * a, input.fogFactor);
                return half4(rgb, a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_Premultiplied > 0.5)
                {
                    return FragPremultiplied(input);
                }

                half4 c = SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, input.uv) * _Tint * input.color;
                half a = min(c.a * _Alpha, _AlphaCap);
                half3 rgb = c.rgb;
                if (_UseEmissive > 0.5)
                {
                    half4 e = SAMPLE_TEXTURE2D(_EmissiveTex, sampler_EmissiveTex, input.uv);
                    rgb += e.rgb * e.a * _EmissiveStrength * _Alpha;
                }

                // Additive materials (src One) carry their alpha in rgb: fade by scaling colour.
                if (_SrcBlend < 1.5)
                {
                    rgb *= a;
                    a = 1.0h;
                    return half4(rgb, a);
                }

                rgb = MixFog(rgb, input.fogFactor);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
