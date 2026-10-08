// WP-092 A (S-05) · TdVisibilityAudit's object-id pass: one flat colour per renderer (_IdColor through a MaterialPropertyBlock;
// 24-bit id in RGB, alpha 1), no lighting, fog or post. One material per ORIGINAL material carries what changes coverage: the
// alpha test (_ClipTex / _ClipTex_ST / _ClipAlphaScale / _Cutoff; _Cutoff < 0 = no test), the cull mode, and per pass the
// colour mask (opaque depth-only in the transparent pass) and depth write. Editor-only (never referenced by a scene or build).
Shader "Hidden/DD/TdVisibilityId"
{
    Properties
    {
        _ClipTex ("Clip texture", 2D) = "white" {}
        _ClipAlphaScale ("Alpha scale", Float) = 1
        _Cutoff ("Cutoff (<0 off)", Float) = -1
        _Cull ("Cull", Float) = 2
        _ColorMask ("Colour mask", Float) = 15
        _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "TdVisibilityId"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest LEqual
            ColorMask [_ColorMask]
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ClipTex);
            SAMPLER(sampler_ClipTex);
            float4 _ClipTex_ST;
            float _ClipAlphaScale;
            float _Cutoff;
            float4 _IdColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _ClipTex_ST.xy + _ClipTex_ST.zw;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                if (_Cutoff >= 0.0)
                {
                    float alpha = SAMPLE_TEXTURE2D(_ClipTex, sampler_ClipTex, input.uv).a * _ClipAlphaScale;
                    clip(alpha - _Cutoff);
                }

                return _IdColor;
            }
            ENDHLSL
        }
    }
}
