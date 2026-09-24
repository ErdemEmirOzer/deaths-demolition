// WP-068 slice 4 · C8 / §6.4 frozen-frame blur: dual Kawase (pass 0 down, pass 1 up) and the ink wash (pass 2) that
// writes the persistent ¼-resolution frame the pause / shop layer shows. Run once per request by UiFrozenFramePass.
Shader "Hidden/DD/UI/FrozenFrame"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "DD_UiFx.hlsl"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    float _DD_FrozenOffset;     // Kawase offset in texels (1.0)
    float _DD_FrozenWash;       // 0..1 ink wash strength (0.55)
    float _DD_FrozenFlip;       // 1 when the camera colour target is stored top-down (graphicsUVStartsAtTop, e.g. Metal)

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv  : TEXCOORD0;
    };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    half4 fragDown(v2f i) : SV_Target
    {
        float2 h = _MainTex_TexelSize.xy * 0.5 * _DD_FrozenOffset;
        half4 sum = tex2D(_MainTex, i.uv) * 4.0;
        sum += tex2D(_MainTex, i.uv - h);
        sum += tex2D(_MainTex, i.uv + h);
        sum += tex2D(_MainTex, i.uv + float2(h.x, -h.y));
        sum += tex2D(_MainTex, i.uv - float2(h.x, -h.y));
        return sum * 0.125;
    }

    half4 fragUp(v2f i) : SV_Target
    {
        float2 h = _MainTex_TexelSize.xy * 0.5 * _DD_FrozenOffset;
        half4 sum = tex2D(_MainTex, i.uv + float2(-h.x * 2.0, 0.0));
        sum += tex2D(_MainTex, i.uv + float2(-h.x, h.y)) * 2.0;
        sum += tex2D(_MainTex, i.uv + float2(0.0, h.y * 2.0));
        sum += tex2D(_MainTex, i.uv + float2(h.x, h.y)) * 2.0;
        sum += tex2D(_MainTex, i.uv + float2(h.x * 2.0, 0.0));
        sum += tex2D(_MainTex, i.uv + float2(h.x, -h.y)) * 2.0;
        sum += tex2D(_MainTex, i.uv + float2(0.0, -h.y * 2.0));
        sum += tex2D(_MainTex, i.uv + float2(-h.x, -h.y)) * 2.0;
        return sum / 12.0;
    }

    // Ink wash (AD MUST 4): a luminance-keeping sumi / washi duotone — the frame keeps its light, the hue moves toward
    // the palette (dark → Sumi ink, light → Cream paper), so it is never a flat cold grey (§1). Desaturation 0.75,
    // washi mottle ±4.5 %, soft vignette 0.25; all × wash strength. Alpha 1.
    half4 fragWash(v2f i) : SV_Target
    {
        float2 uv = float2(i.uv.x, _DD_FrozenFlip > 0.5 ? 1.0 - i.uv.y : i.uv.y);
        half3 c = tex2D(_MainTex, uv).rgb;
        half luma = dot(c, half3(0.2126, 0.7152, 0.0722));
        half3 grey = lerp(c, luma.xxx, 0.75 * _DD_FrozenWash);
        half3 duo = lerp(_DD_Ink.rgb, _DD_Paper.rgb * 0.6, saturate(luma));
        half3 inked = lerp(grey, duo, 0.45 * _DD_FrozenWash);
        // Washi (AP-012 256² tile, ~1:1 at 1080p) + a second tap at 1.37× turned 90° to break the repeat; normalised
        // (mean 0.52, σ 0.032) to ±σ = 4.5 % × wash (AD 4c SHOULD 3: 2–3 % at the default wash 0.55).
        float2 tuv = uv * float2(7.5, 4.22);
        half paper = 0.5 * (tex2D(_DD_PaperTile, tuv).r + tex2D(_DD_PaperTile, float2(-tuv.y, tuv.x) * 1.37).r);
        half mottle = clamp((paper - 0.52) / 0.032, -3.0, 3.0);
        inked *= 1.0 + 0.045 * mottle * _DD_FrozenWash;
        float2 d = uv - 0.5;
        half vignette = 1.0 - smoothstep(0.35, 0.85, length(d * float2(1.25, 1.0))) * 0.25 * _DD_FrozenWash;
        return half4(inked * vignette, 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass { Name "Down" CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
        ENDCG }

        Pass { Name "Up" CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
        ENDCG }

        Pass { Name "Wash" CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragWash
        ENDCG }
    }
}
