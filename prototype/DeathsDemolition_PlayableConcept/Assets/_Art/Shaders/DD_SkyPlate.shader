// WP-038E (2) · DD/SkyPlate — the AP-019 horizon plates: unlit, straight-alpha blended, no fog (the plates sit beyond
// the 210 m fog end and carry their own baked haze; fogging them would dissolve the silhouettes), no depth write,
// drawn right after the skybox (Transparent−100) so the sky pass cannot paint over them. Sampled by the cylinder's own UV (u = repeats around 360°, v = height).
Shader "DD/SkyPlate"
{
    Properties
    {
        [MainTexture] _BaseMap("Plate (RGBA sheet)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _ScrollUV("UV scroll per second (xy; WP-075 C3 cloud planes)", Vector) = (0, 0, 0, 0)
        _AlphaPower("Alpha power (WP-075 AD S3: > 1 thins the cloud cover, < 1 thickens it; 1 = the sheet)", Range(0.3, 4)) = 1
        [ToggleUI] _BottomEdgeAA("Bottom edge pixel coverage", Float) = 0
        [ToggleUI] _BottomPanoramaReveal("Reveal the panorama through the authored fog footer (DEC-108)", Float) = 0
        _RevealHeight("Reveal height (V; the sheet's fog footer)", Range(0.02, 0.5)) = 0.16
        _HazeColor("Haze colour (DEC-101 aerial perspective; DEC-076 fog colour)", Color) = (0.815, 0.843, 0.870, 1)
        _Haze("Haze amount (0 = the sheet: the AP-019 plates unchanged)", Range(0, 1)) = 0
    }

    SubShader
    {
        // Transparent-100: after the skybox (a Background-queue plate is painted over by the skybox pass, which fills every
        // texel the plate did not write depth to) and before the world's own transparents; back-to-front so the far ring draws first.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "SkyPlate"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _ScrollUV;
                half _AlphaPower;
                float _BottomEdgeAA;
                float _BottomPanoramaReveal;
                float _RevealHeight;
                half4 _HazeColor;
                half _Haze;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float originalV : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // The rings sit at 260–420 m, beyond the gameplay camera's 260 m far clip (PrototypeCameraRig, S-02): like the
                // skybox, the depth is pinned just inside the far plane so nothing is clipped while x/y keep their true
                // perspective (the plates are behind every opaque anyway, ZTest LEqual).
                #if UNITY_REVERSED_Z
                    output.positionCS.z = output.positionCS.w * 1.0e-5;
                #else
                    output.positionCS.z = output.positionCS.w * (1.0 - 1.0e-5);
                #endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap) + _Time.y * _ScrollUV.xy;   // C3: the cloud planes drift with the cookie (0 on the plates)
                output.originalV = input.uv.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float footprint = fwidth(input.originalV);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                c.a = pow(saturate(c.a), _AlphaPower);   // AD S3 thinned the cover; DEC-101 thickens it (overcast)
                if (_BottomEdgeAA > 0.5)
                    c.a *= footprint > 0.0 ? saturate(input.originalV / footprint) : 1.0;
                // DEC-108: the panorama shows under the near plate's fog footer (AP023-sky-v002 authors it into the bottom 0.16 of V);
                // the builder sets the height (WorldV5Sky.PanoramaRevealHeight, M-44).
                if (_BottomPanoramaReveal > 0.5)
                    c.a *= smoothstep(0.0, _RevealHeight, input.originalV);
                c.rgb = lerp(c.rgb, _HazeColor.rgb, _Haze);   // DEC-101: distant layers fade into the haze
                return c;   // no MixFog on purpose (AP-019 contract: fog-excluded)
            }
            ENDHLSL
        }
    }
    FallBack Off
}
