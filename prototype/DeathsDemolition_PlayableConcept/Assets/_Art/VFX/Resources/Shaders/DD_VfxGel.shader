// DD/VFX Gel — WP-073 B6 · the Specials' living liquids (direction §2.1 orb core / plasma shell, §2.2 blob, droplets,
// canister). One shader, three uses:
//   · Namekuji blob + droplets (GelSystem, instanced icosphere): vertex wobble along the normal (_GelWobble: amplitude
//     m, Hz, phase; 6 Hz / 0.015 m on the blob), glossy lit look (smoothness 0.95 main-light specular + SH sheen), a
//     #B040FF Schlick-power fresnel rim and the HDR emission (#FF5FD2 × 3) — straight alpha, _GelColor.a;
//   · Raiju orb core (_Additive 0, #EAF6FF × 10, wobble × 0.3) and plasma shell (_Additive 1, Blend One One: emission ×
//     (0.35 + fresnel), no lighting) — ArcSystem, instanced;
//   · the Namekuji canister liquid (SpecialWeaponGlow, a regular renderer with a property block, _PlaneOn 1): fragments
//     above the world-space surface plane _GelPlane (xyz normal, w = d; the level in 8 steps + 0.5 Hz slosh) are
//     clipped, with a bright meniscus line under the surface.
// Transparent, ZWrite off, blend from material floats (_SrcBlend / _DstBlend), fog applied. All-float CBUFFER (Metal);
// per-instance data through UNITY_INSTANCING_BUFFER (MaterialPropertyBlock arrays or single values).
Shader "DD/VFX Gel"
{
    Properties
    {
        _Tint("Albedo tint (#FF5FD2)", Color) = (1, 0.373, 0.824, 1)
        _Smoothness("Smoothness", Float) = 0.95
        _Additive("Additive (plasma shell)", Float) = 0
        _PlaneOn("Cut plane (canister)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Gel"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Smoothness;
                float _Additive;
                float _PlaneOn;
                float _SrcBlend;
                float _DstBlend;
                float _Cull;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GelColor)  // rgb emission (linear HDR), a alpha
                UNITY_DEFINE_INSTANCED_PROP(float4, _GelRim)    // rgb rim (linear HDR), a fresnel power
                UNITY_DEFINE_INSTANCED_PROP(float4, _GelWobble) // x amplitude (m), y Hz, z phase, w stretch along object Z (1 = none)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GelPlane)  // xyz world normal, w d (keep dot(n, p) <= d) — canister only
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 wob = UNITY_ACCESS_INSTANCED_PROP(Props, _GelWobble);
                float3 p = v.positionOS.xyz;
                // The wobble's amplitude is in metres: divide by the object scale so a 0.02 m droplet and a 0.45 m shell
                // move by what the direction says, not by a share of their size.
                float scale = max(length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x)), 1e-4);
                float phase = wob.y * 6.2831853 * _Time.y + wob.z + dot(p, float3(7.1, 11.3, 5.7));
                p += v.normalOS * (wob.x / scale) * sin(phase);
                p.z *= wob.w > 0.0 ? wob.w : 1.0;
                o.world = TransformObjectToWorld(p);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionHCS = TransformWorldToHClip(o.world);
                o.fog = ComputeFogFactor(o.positionHCS.z);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 colour = UNITY_ACCESS_INSTANCED_PROP(Props, _GelColor);
                float4 rim = UNITY_ACCESS_INSTANCED_PROP(Props, _GelRim);
                float meniscus = 0.0;
                if (_PlaneOn > 0.5)
                {
                    float4 plane = UNITY_ACCESS_INSTANCED_PROP(Props, _GelPlane);
                    float below = plane.w - dot(plane.xyz, i.world);
                    clip(below);
                    meniscus = 1.0 - saturate(below / 0.004);
                }

                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetCameraPositionWS() - i.world);
                float fres = pow(1.0 - saturate(abs(dot(N, V))), max(0.5, rim.a));

                if (_Additive > 0.5)
                {
                    float3 glow = colour.rgb * (0.35 + fres) + rim.rgb * fres;
                    return float4(MixFogColor(glow, float3(0, 0, 0), i.fog), 1.0);
                }

                Light light = GetMainLight();
                float3 H = normalize(light.direction + V);
                float ndl = saturate(dot(N, light.direction) * 0.7 + 0.3);
                float spec = pow(saturate(dot(N, H)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness * _Smoothness * 6.0;
                float3 env = SampleSH(reflect(-V, N)) * lerp(0.04, 1.0, fres) * _Smoothness;
                float3 col = _Tint.rgb * 0.3 * (ndl * light.color + SampleSH(N)) + spec * light.color + env + rim.rgb * fres + colour.rgb;
                col += rim.rgb * meniscus * 2.0;
                float alpha = saturate(colour.a + fres * 0.2);
                return float4(MixFog(col, i.fog), alpha);
            }
            ENDHLSL
        }
    }
}
