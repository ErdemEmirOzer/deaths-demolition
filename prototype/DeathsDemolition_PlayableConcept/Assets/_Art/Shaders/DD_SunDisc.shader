// WP-075 · DEC-102 · DD/SunDisc — the high sun's disc and halo in the v6 sky: one quad along the sun direction (WorldV5Sky), procedural
// from its UV (a soft-edged HDR disc the bloom turns into glare, a tight glow and a wide exponential halo), additive, no fog, the depth
// pinned just inside the far plane like DD/SkyPlate. DEC-102 r2: drawn after the skybox, it reads the sky_v3 panorama along each pixel's
// view ray (Skybox/Panoramic's own lat-long mapping and rotation) for its cloud cover — the cumulus dims the sun to a bright glow instead
// of hiding it, the gaps and the forward-scatter glow show the crisp disc (the cookie's rule: wp-074/tools/stage-sky-v3.py).
Shader "DD/SunDisc"
{
    Properties
    {
        [HDR] _DiscColor("Disc (HDR, linear)", Color) = (30, 27, 22, 1)
        _HaloColor("Halo (linear)", Color) = (1.0, 0.82, 0.60, 1)
        _DiscRadius("Disc radius (quad half-size fraction)", Range(0.001, 0.2)) = 0.012
        _GlowIntensity("Tight glow intensity", Range(0, 8)) = 1.6
        _GlowFalloff("Tight glow falloff (per quad half-size)", Range(4, 120)) = 36
        _HaloPower("Halo falloff", Range(1, 32)) = 6
        _HaloIntensity("Halo intensity", Range(0, 2)) = 0.22
        [NoScaleOffset] _SkyPano("Sky panorama (the skybox's lat-long)", 2D) = "white" {}
        _SkyRotation("Skybox rotation (degrees)", Float) = 0
        _CloudDim("Cloud dimming: disc, halo", Vector) = (0.88, 0.5, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-104" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "SunDisc"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"   // LinearToSRGB (the panorama's cloud rule is in sRGB)
            CBUFFER_START(UnityPerMaterial)
                half4 _DiscColor;
                half4 _HaloColor;
                half _DiscRadius;
                half _GlowIntensity;
                half _GlowFalloff;
                half _HaloPower;
                half _HaloIntensity;
                float _SkyRotation;
                half4 _CloudDim;
            CBUFFER_END
            TEXTURE2D(_SkyPano); SAMPLER(sampler_SkyPano);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                #if UNITY_REVERSED_Z
                    output.positionCS.z = output.positionCS.w * 1.0e-5;
                #else
                    output.positionCS.z = output.positionCS.w * (1.0 - 1.0e-5);
                #endif
                output.uv = input.uv;
                return output;
            }
            // The panorama's cloud cover along the view ray: Skybox/Panoramic's lat-long (u = 0.5 − atan2(z, x) / 2π after the rotation,
            // v = 1 − acos(y) / π); cloud = neither blue (b − r) nor the near-white glow round the sun (the cookie's rule).
            half Cover(float3 dir)
            {
                float a = radians(_SkyRotation), c = cos(a), s = sin(a);
                float3 d = float3(c * dir.x - s * dir.z, dir.y, s * dir.x + c * dir.z);
                float2 uv = float2(0.5 - atan2(d.z, d.x) / (2.0 * PI), 1.0 - acos(clamp(d.y, -1.0, 1.0)) / PI);
                half3 sky = SAMPLE_TEXTURE2D_LOD(_SkyPano, sampler_SkyPano, uv, 0).rgb;
                #if !UNITY_COLORSPACE_GAMMA
                    sky = LinearToSRGB(sky);   // the rule is in sRGB (the panorama is an sRGB texture)
                #endif
                half blue = saturate((sky.b - sky.r - 0.06h) / 0.28h);
                half glow = saturate((max(sky.r, max(sky.g, sky.b)) - 0.78h) / 0.12h);
                half t = saturate((1.0h - max(blue, glow) - 0.55h) / 0.30h);
                return t * t * (3.0h - 2.0h * t);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.positionWS - _WorldSpaceCameraPos);
                half cover = Cover(dir);
                half r = length(input.uv * 2.0 - 1.0);
                half disc = 1.0h - smoothstep(_DiscRadius * 0.7h, _DiscRadius, r);
                half glow = exp(-r * _GlowFalloff) * _GlowIntensity;
                half halo = pow(saturate(1.0h - r), _HaloPower) * _HaloIntensity;
                half3 core = _DiscColor.rgb * disc + _HaloColor.rgb * glow;
                return half4(core * (1.0h - _CloudDim.x * cover) + _HaloColor.rgb * halo * (1.0h - _CloudDim.y * cover), 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
