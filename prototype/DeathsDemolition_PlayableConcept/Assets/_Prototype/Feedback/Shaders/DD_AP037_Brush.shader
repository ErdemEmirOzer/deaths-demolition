Shader "DeathsDemolition/VFX/AP037Brush"
{
    Properties
    {
        _Mask ("Linear RGBA mask", 2D) = "white" {}
        _TimeMask ("Linear reveal time", 2D) = "black" {}
        _UseTime ("Use reveal time", Float) = 0
        _InkCap ("Cap blood/ink luminance (dust off)", Float) = 1
        _Soft ("Reveal softness", Float) = .015
        _AtlasUV ("Cell scale/offset", Vector) = (1,1,0,0)
        _Tint ("Runtime tint (straight alpha)", Color) = (1,1,1,1)
        _Alpha ("Envelope alpha", Range(0,1)) = 1
        _Reveal ("Reveal 0..1", Range(0,1)) = 1
        _WetScale ("Wetness envelope", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Name "AP037Brush"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            TEXTURE2D(_TimeMask); SAMPLER(sampler_TimeMask);
            CBUFFER_START(UnityPerMaterial)
            float4 _AtlasUV, _Tint;
            float _Alpha, _Reveal, _WetScale, _UseTime, _Soft, _InkCap;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _AtlasUV.xy + _AtlasUV.zw;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, input.uv);
                float density = m.r / max(m.a, .0001);
                float edge = saturate((density - .82) / .18);
                float inner = clamp((density - .72) / .09, -1.0, 1.0);
                float3 ink = _Tint.rgb * (1.0 - .70 * edge) * (1.0 - .12 * inner * (1.0 - edge));
                // Narrow wet highlight fades after 1.5 s for stains. Peak luminance is clamped to AP037 AD R2.
                float wet = smoothstep(.8, 1.0, m.g) * (1.0 - edge) * _WetScale;
                ink += float3(.025, .032, .04) * wet;
                // AD R2 caps luminance of blood/ink; explosion dust keeps its authored tint.
                float luminance = dot(ink, float3(.2126, .7152, .0722));
                ink *= lerp(1.0, min(1.0, .25 / max(.0001, luminance)), saturate(_InkCap));
                float reveal = 1.0;
                if (_UseTime > .5)
                {
                    float tm = SAMPLE_TEXTURE2D(_TimeMask, sampler_TimeMask, input.uv).r;
                    float soft = max(.0001, _Soft);
                    reveal = saturate((_Reveal * (1.0 + soft) - tm) / soft);
                }
                return half4(ink, saturate(m.a * _Alpha * _Tint.a * reveal));
            }
            ENDHLSL
        }
    }
}
