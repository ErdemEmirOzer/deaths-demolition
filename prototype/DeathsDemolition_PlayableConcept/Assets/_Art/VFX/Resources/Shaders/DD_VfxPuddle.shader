// DD/VFX Puddle — WP-073 B6 · Namekuji gel puddles + splats (direction §2.2) and Raiju Lichtenberg burn marks (§2.1), one
// flat XZ quad per instance drawn by GelSystem / ArcSystem with Graphics.DrawMeshInstanced (the instance matrix carries
// centre, surface rotation, yaw and diameter). _Atlas holds three variants side by side (u = (uv.x + variant) / 3).
//
// _Mode 0 (gel, atlas = SpecialVfxAssets.SplatAtlas: R coverage/height, G cracks, B thickness):
//   · live: dark glossy magenta body (#FF5FD2 albedo × 0.35) lit by the main light + SH, smoothness 0.92 specular (wetter
//     than the 0.60 wet slate), a view-reflected SH sheen with Schlick fresnel, emission × 0.6 with a 0.3 Hz pulse, two
//     normal layers scrolling at 0.15 Hz, 3 bubbles / s (UV-animated rings on a 7 × 7 cell grid, no particles), and the
//     #B040FF × 1.5 rim band on the coverage edge — the edge is drawn in every phase (§4.5 danger read);
//   · drying (_PuddleParams.z 0 → 1): emission → 0, colour → #6E3560 matte (smoothness → 0.25), the crack mask darkens,
//     α 0.92 → 0.80; _PuddleParams.w is the C# fade (last 2 s / early end); _Fancy 0 (Performance) drops bubbles + pulse;
//   · a splat (_PuddleExtra.z = 1): the same look without bubbles, used for impact wet flashes.
// _Mode 1 (burn, atlas = SpecialVfxAssets.BurnAtlas, R8): #2A2420 × coverage, α = R × _PuddleParams.w (C# sends 0.6 × fade).
// Straight alpha, ZWrite off, a small depth offset (the quads also sit 0.015–0.03 m off the surface), fog applied.
// All-float CBUFFER (Metal); per-instance data through UNITY_INSTANCING_BUFFER (MaterialPropertyBlock arrays).
Shader "DD/VFX Puddle"
{
    Properties
    {
        _Atlas("Atlas (3 variants)", 2D) = "black" {}
        _Mode("Mode (0 gel, 1 burn)", Float) = 0
        _GelColor("Gel (#FF5FD2)", Color) = (1, 0.373, 0.824, 1)
        _RimColor("Rim (#B040FF)", Color) = (0.690, 0.251, 1, 1)
        _DryColor("Dry (#6E3560)", Color) = (0.431, 0.208, 0.376, 1)
        _BurnColor("Burn (#2A2420)", Color) = (0.165, 0.141, 0.125, 1)
        _Emission("Emission", Float) = 0.6
        _RimIntensity("Rim intensity", Float) = 1.5
        _PulseHz("Pulse Hz", Float) = 0.3
        _Smoothness("Smoothness", Float) = 0.92
        _BubbleRate("Bubbles per second", Float) = 3
        _Fancy("Bubbles + pulse (tier)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Puddle"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Atlas);
            SAMPLER(sampler_Atlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _Atlas_TexelSize;
                float4 _GelColor;
                float4 _RimColor;
                float4 _DryColor;
                float4 _BurnColor;
                float _Mode;
                float _Emission;
                float _RimIntensity;
                float _PulseHz;
                float _Smoothness;
                float _BubbleRate;
                float _Fancy;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PuddleParams) // x life 0..1, y variant 0..2, z drying 0..1, w alpha (fade)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PuddleExtra)  // x time seed, y radius (m), z 1 = splat (no bubbles), w unused
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.world);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionHCS.z);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 prm = UNITY_ACCESS_INSTANCED_PROP(Props, _PuddleParams);
                float4 ext = UNITY_ACCESS_INSTANCED_PROP(Props, _PuddleExtra);
                float variant = floor(prm.y + 0.5);
                float2 cellUV = float2((saturate(i.uv.x) + variant) / 3.0, saturate(i.uv.y));
                float4 s = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, cellUV);

                if (_Mode > 0.5)
                {
                    // Burn: the charred Lichtenberg figure, lit only by the ambient (a scorch never shines).
                    float3 burn = _BurnColor.rgb * (0.6 + 0.4 * SampleSH(float3(0, 1, 0)));
                    float a = s.r * prm.w;
                    return float4(MixFog(burn, i.fog), a);
                }

                float cover = s.r;
                float inside = smoothstep(0.02, 0.10, cover);
                if (inside <= 0.001) discard;
                float dry = saturate(prm.z);
                float fancy = _Fancy * (1.0 - step(0.5, ext.z));
                float t = _Time.y + ext.x;

                // Height normal from the dome (R) + two 0.15 Hz ripple layers.
                float2 du = float2(_Atlas_TexelSize.x * 2.0, 0.0), dv = float2(0.0, _Atlas_TexelSize.y * 2.0);
                float hx = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, cellUV + du).r - SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, cellUV - du).r;
                float hy = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, cellUV + dv).r - SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, cellUV - dv).r;
                float2 p = i.uv * 6.0;
                float w = 6.2831853 * 0.15;
                float2 ripple = 0.12 * float2(sin(p.x * 3.1 + t * w), cos(p.y * 2.7 - t * w))
                              + 0.08 * float2(sin((p.x + p.y) * 5.3 - t * w * 0.7), cos((p.x - p.y) * 4.1 + t * w * 0.7));
                ripple *= (1.0 - dry);
                float3 nOS = normalize(float3(-hx * 4.0 + ripple.x, 1.0, -hy * 4.0 + ripple.y));
                float3 N = TransformObjectToWorldNormal(nOS);

                Light light = GetMainLight();
                float3 V = normalize(GetCameraPositionWS() - i.world);
                float3 H = normalize(light.direction + V);
                float ndl = saturate(dot(N, light.direction) * 0.8 + 0.2);
                float smooth = lerp(_Smoothness, 0.25, dry);
                float specPow = exp2(10.0 * smooth + 1.0);
                float spec = pow(saturate(dot(N, H)), specPow) * smooth * smooth * 4.0;
                float fres = pow(1.0 - saturate(dot(N, V)), 5.0);
                float3 env = SampleSH(reflect(-V, N)) * lerp(0.04, 1.0, fres) * smooth;

                float3 albedo = lerp(_GelColor.rgb * 0.35, _DryColor.rgb, dry) * lerp(0.85, 1.1, s.b);
                float3 col = albedo * (ndl * light.color + SampleSH(N)) + spec * light.color * (1.0 - dry) + env;

                // AD B6 MUST 4: the overcast sky's sheen — a fresnel-weighted sky reflection (brighter toward the far side) and a
                // soft "sky light" highlight that the two ripple layers move across the surface: glossy gel, not paint.
                float3 sky = SampleSH(float3(0.0, 1.0, 0.0)) * 1.6;
                float sheen = pow(1.0 - saturate(dot(N, V)), 3.0) * smooth;
                float3 skyDir = normalize(float3(0.0, 1.0, 0.0) + V * 0.6);
                float glint = pow(saturate(dot(reflect(-V, N), skyDir)), 48.0) * smooth;
                col += (sky * (0.35 + 1.4 * sheen) + sky * glint * 2.5) * (1.0 - dry);

                // Emission: × 0.6 with the 0.3 Hz pulse, gone by the end of drying.
                float pulse = 1.0 + 0.25 * fancy * sin(t * 6.2831853 * _PulseHz);
                col += _GelColor.rgb * _Emission * pulse * (1.0 - dry);

                // Bubbles: 3 / s over a 7 × 7 grid (each cell pops every 49 / rate s, a ring growing for 5 % of its period).
                if (fancy > 0.5 && dry < 0.6 && cover > 0.3)
                {
                    float2 g = i.uv * 7.0;
                    float2 cell = floor(g);
                    float h = Hash21(cell + variant * 13.0 + ext.x);
                    float period = 49.0 / max(0.1, _BubbleRate);
                    float ph = frac(t / period + h);
                    if (ph < 0.05)
                    {
                        // AD B6 MUST 4: a bubble is a bright speck (#FFFFFF × 2, ≈ 0.03–0.05 m), swelling then popping.
                        float2 c = cell + 0.3 + 0.4 * float2(Hash21(cell + 1.7), Hash21(cell + 3.1));
                        float r = length(g - c);
                        float size = 0.09 * (0.6 + 0.4 * sin(ph / 0.05 * 3.14159));
                        float speck = saturate(1.0 - r / max(size, 1e-3));
                        col += float3(2.0, 2.0, 2.0) * speck * speck * (1.0 - dry);
                    }
                }

                // Cracks while drying (strongest at the rim), and the rim band that never disappears.
                col *= 1.0 - s.g * dry * 0.6;
                // AD B6 MUST 5: the edge is its own colour, not the fill's — a thin #B040FF × 1.5 band (≈ 0.06–0.10 m on a 1.6 m
                // puddle) that REPLACES the fill there; dried, it becomes a dark crust line. Danger stays readable in every phase.
                float edge = smoothstep(0.02, 0.05, cover) * (1.0 - smoothstep(0.08, 0.15, cover));
                float3 band = lerp(_RimColor.rgb * _RimIntensity, _DryColor.rgb * 0.35, dry);
                col = lerp(col, band, edge);

                float alpha = max(inside * lerp(0.92, 0.80, dry), edge) * prm.w;
                return float4(MixFog(col, i.fog), alpha);
            }
            ENDHLSL
        }
    }
}
