// WP-081 B · N2 (C-00 06.10, (a)) · DD/SkyPanorama — the v6 sky (DD_Sky_v3: AP-023 sky_v3's 360° lat-long, DEC-102 r2) without the
// 2 px line at compass 270°. Skybox/Panoramic reads the panorama with tex2D: its u = 0.5 − atan2(z, x) / 2π wraps 1 → 0 on world −X,
// and in a 2×2 pixel quad that straddles the wrap the hardware u derivative is ≈ 1, so the GPU takes the coarsest mip there — a 2 px
// column of the panorama's average that flickers with the quad parity as the camera turns (K/wp-038m/kadro-0610/menu-n2-n4 §N2).
// This shader is Skybox/Panoramic's 360°, no-3D-layout path — the same mapping, rotation and colour (tint × unity_ColorSpaceDouble ×
// [Gamma] exposure) — with one change: the texture is read with SAMPLE_TEXTURE2D_GRAD, and u's derivative is taken, per screen axis, from
// whichever of two cuts is continuous at the pixel (Tarini 2012): u (cut on −X) or u' = frac(u + 0.5) (cut on +X; the same texel under
// the importer's U Repeat). Away from both cuts the two derivatives are equal, so the mip and the anisotropy are the built-in's; in the
// seam quads they become the true footprint. v = 1 − acos(y) / π has no wrap: its finite difference is the pixel's angular size / π, at
// the poles too. At the poles u's true derivative is large (the columns converge); both cuts' derivatives are then large and the mip is
// at most the built-in's (the panorama's top and bottom rows are flat, so the u prefilter there is exact).
// The mesh, the queue and the depth are the engine's skybox draw (DrawSkybox), as for the built-in; no fog, no lighting, no keywords.
Shader "DD/SkyPanorama"
{
    Properties
    {
        // Skybox/Panoramic's names, types, attributes and defaults: DD_Sky_v3 keeps its values (ArtSkyBuilder.BuildV3).
        _Tint("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure("Exposure", Range(0, 8)) = 1.0
        _Rotation("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _MainTex("Spherical (360° lat-long, LDR sRGB)", 2D) = "grey" {}
    }

    SubShader
    {
        // No RenderPipeline tag, like the built-in: the engine's own skybox paths (the default reflection / ambient cube rendered from
        // RenderSettings.skybox, the material preview) take this SubShader as well as URP's DrawSkybox.
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "SkyPanorama"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // UnityCG.cginc's unity_ColorSpaceDouble (URP does not define it): 2 in gamma space, GammaToLinear(2) in linear (the project).
            #if defined(UNITY_COLORSPACE_GAMMA)
                #define DD_SKY_COLORSPACE_DOUBLE half3(2.0, 2.0, 2.0)
            #else
                #define DD_SKY_COLORSPACE_DOUBLE half3(4.59479380, 4.59479380, 4.59479380)
            #endif

            // Every material property in UnityPerMaterial: SRP Batcher compatible (the skybox draw itself is not batched). No _MainTex_HDR:
            // the panorama is LDR sRGB (BC7), whose decode multiplier is 1 — Skybox/Panoramic's DecodeHDR returns its rgb unchanged.
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure;
                float _Rotation;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);   // the texture's own sampler: U/V Repeat, trilinear, aniso 4 (ArtImportContract)

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 directionOS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            // Skybox/Panoramic's RotateAroundYInDegrees: the mesh turns by +_Rotation, the lookup keeps the unrotated direction.
            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(RotateAroundYInDegrees(input.positionOS.xyz, _Rotation));
                output.directionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.directionOS);
                // Skybox/Panoramic's ToRadialCoords (360°, no 3D layout): +Z at u 0.25, −X at u 0 / 1; v 0 = nadir, 1 = zenith.
                // y is clamped only against a rounding |y| > 1 (acos → NaN); it changes no valid direction.
                float u = 0.5 - atan2(d.z, d.x) * (0.5 / PI);
                float v = 1.0 - acos(clamp(d.y, -1.0, 1.0)) * (1.0 / PI);

                // Seamless derivatives (Tarini 2012, "Cylindrical and toroidal parameterizations without vertex seams"): u2 is u shifted
                // half a turn, so it wraps on +X where u is continuous and is continuous on −X where u wraps. Per screen axis the smaller
                // |derivative| is the continuous one (one step can cross at most one of the two cuts, except in the quad on a pole,
                // where both are large anyway). Taken in uniform control flow, before any branch; the sample position stays u.
                float u2 = frac(u + 0.5);
                float2 du = float2(ddx(u), ddy(u));
                float2 du2 = float2(ddx(u2), ddy(u2));
                float dudx = abs(du.x) <= abs(du2.x) ? du.x : du2.x;
                float dudy = abs(du.y) <= abs(du2.y) ? du.y : du2.y;
                half4 tex = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, float2(u, v), float2(dudx, ddx(v)), float2(dudy, ddy(v)));

                // Skybox/Panoramic's colour, in its order: DecodeHDR (× 1 here), × tint × ColorSpaceDouble, × exposure.
                half3 c = tex.rgb;
                c = c * _Tint.rgb * DD_SKY_COLORSPACE_DOUBLE;
                c *= _Exposure;
                return half4(c, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
