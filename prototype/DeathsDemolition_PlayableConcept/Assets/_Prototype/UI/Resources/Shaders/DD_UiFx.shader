// WP-068 slice 4 · DD/UI/Fx — the one UI effect material (C7 / §3). UI-Default (2022.3) base: stencil, _ClipRect,
// soft RectMask2D, CanvasGroup alpha (vertex colour) and premultiplied output are unchanged; only the CLIP_RECT and
// ALPHACLIP keywords exist (4 variants). UiFx (BaseMeshEffect) writes per vertex, once per Play:
//   uv1 = (mode, start time on _DD_UiTime, duration s, loop period s | 0)
//   uv2 = mode parameters (x, y, z) + ink channel / particle seed (w)
//   uv3 = (rect-normalised x, y — the ink-map coordinate; zw = rect size px, or the rect centre for Stamp — its pivot)
// Particles: every vertex of a quad sits on its emitter point, uv3.xy is the corner (-1..1), uv3.z the burst's start radius; the vertex stage moves it.
// Icon (WP-081 ikon v2 B5b): uv1 = (8, sheen -1 off | 0..1, glow 0..0.08, spread px24) with no clock, uv2 = (stroke draw,
// lit 0|1 - 0 when disabled, opacity, -), uv3 as above (the sprite box uv); _DD_IconTier switches it to the flat token.
Shader "DD/UI/Fx"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "DD_UiFx.hlsl"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 fx       : TEXCOORD1;
                float4 prm      : TEXCOORD2;
                float4 map      : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                half4  mask          : TEXCOORD2;
                float4 fx            : TEXCOORD3;   // mode, progress (0..1), start, loop period (particles: life alpha)
                float4 prm           : TEXCOORD4;
                float4 map           : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float mode = v.fx.x;
                float4 position = v.vertex;
                float progress = DD_Progress(v.fx.y, v.fx.z);
                float lifeAlpha = 1.0;

                if (DD_IsMode(mode, DD_FX_STAMP))
                {
                    // §2.5 Stamp: 1.12 → 1.00 InCubic over 90 ms, then held (the 40 ms hold is the rest of the 130 ms envelope).
                    float k = _DD_Motion < 0.5 ? 1.0 : saturate((_DD_UiTime - v.fx.y) / DD_UI_STAMP_IN);
                    float s = lerp(DD_UI_STAMP_PEAK, 1.0, k * k * k);
                    position.xy = v.map.zw + (position.xy - v.map.zw) * s;
                }
                else if (DD_IsMode(mode, DD_FX_PARTICLES))
                {
                    // prm: x size px, y speed px/s, z gravity px/s² (+ = down), w seed 0..1. fx.z life s, fx.w loop period (0 = one-shot burst).
                    float seed = v.prm.w;
                    float life = max(v.fx.z, 0.05);
                    float age;
                    if (v.fx.w > 0.0)
                    {
                        age = frac((_DD_UiTime * _DD_Motion + seed * v.fx.w) / v.fx.w) * v.fx.w;   // RM: petals stand still
                    }
                    else
                    {
                        age = _DD_UiTime - v.fx.y - seed * 0.05;
                    }
                    float t = saturate(age / life);
                    float angle = DD_Hash(float2(seed, 0.37)) * 6.2831853;
                    float speed = v.prm.y * (0.55 + 0.45 * DD_Hash(float2(seed, 0.91)));
                    float2 dir = v.fx.w > 0.0 ? float2(sin(age * 1.7 + seed * 6.28) * 0.35, -1.0) : float2(cos(angle), sin(angle));
                    // map.z: a burst's start radius (0 = the centre) — sparks leave a ring's stroke outward (AD 8c SHOULD-2).
                    float2 offset = dir * (v.map.z + speed * max(age, 0.0)) + float2(0.0, -0.5 * v.prm.z * age * age);
                    float size = v.prm.x * (0.6 + 0.4 * DD_Hash(float2(seed, 0.13))) * (v.fx.w > 0.0 ? 1.0 : (1.0 - 0.6 * t));
                    // The CPU quad has its base size (half of prm.x) around the emitter so uGUI never drops it as
                    // degenerate; re-centre it and apply the animated size + motion here.
                    float2 centre = v.vertex.xy - v.map.xy * (v.prm.x * 0.5);
                    position.xy = centre + offset + v.map.xy * size;
                    bool alive = age >= 0.0 && age <= life;
                    lifeAlpha = alive ? (1.0 - smoothstep(0.55, 1.0, t)) * smoothstep(0.0, 0.08, t + 0.08) : 0.0;
                    // Sparks are Flash class (peak × FlashScale); ambient drift is Decor.
                    lifeAlpha *= v.fx.w > 0.0 ? 1.0 : _DD_Flash;
                    progress = t;
                }

                OUT.worldPosition = position;
                OUT.vertex = UnityObjectToClipPos(position);

                float2 pixelSize = OUT.vertex.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                OUT.mask = half4(position.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                OUT.color = v.color * _Color;
                OUT.fx = float4(mode, progress, v.fx.y, DD_IsMode(mode, DD_FX_PARTICLES) ? lifeAlpha : v.fx.w);
                OUT.prm = v.prm;
                if (DD_IsMode(mode, DD_FX_ICON))
                {
                    // WP-081 Icon: uv1 rides raw (sheen, glow, spread - the CPU writes them, there is no clock). Reduced
                    // motion rests the light channels at once - sheen off, no glow, no spread, fully drawn - whatever a
                    // running track left in the vertices (T4); the relief itself is not motion and stays.
                    bool still = _DD_Motion < 0.5;
                    OUT.fx = still ? float4(mode, -1.0, 0.0, 0.0) : v.fx;
                    OUT.prm.x = still ? 1.0 : v.prm.x;
                }

                OUT.map = v.map;
                return OUT;
            }

            // Ink reveal (§3 InkReveal): the kit's time map fills where m <= progress; a 0.04 band at the front darkens
            // and flickers (wet ink). prm.x = band (0 → 0.04), prm.y = 0 mark (the mark's shape clips — use the Ink.*
            // static mask as the sprite for its soft edge) | 1 wipe (the whole rect, empty parts ink last), prm.w = channel.
            // Mark mode runs on linear progress: the AP-012 v004 time maps carry the brush pacing themselves (head-heavy
            // stroke, lifted tail), so the Brush ease on top would double it (AD 4c MUST 1: half the timeline showed
            // 93 % of the stroke); wipe mode keeps the Brush ease.
            half4 InkReveal(half4 color, v2f IN)
            {
                bool wipe = IN.prm.y > 0.5;
                float t = wipe ? DD_EaseBrush(IN.fx.y) : IN.fx.y;
                float band = IN.prm.x > 0.0 ? IN.prm.x : 0.04;
                float raw = DD_InkSample(IN.map.xy, IN.prm.w);
                float m = DD_InkTime(raw, IN.prm.w);
                float jitter = (DD_Hash(floor(IN.map.xy * max(IN.map.zw, 1.0) / 3.0) + floor(_DD_UiTime * 8.0)) - 0.5) * 0.02 * _DD_Motion * (1.0 - t);
                float edge = t * (1.0 + band) - m + jitter;
                float visible = smoothstep(0.0, 0.012, edge);
                float front = (1.0 - smoothstep(0.0, band, edge)) * (t < 0.999 ? 1.0 : 0.0);
                color.rgb = lerp(color.rgb, color.rgb * 0.55 + _DD_Ink.rgb * 0.2, front * 0.65);
                color.a *= visible * (wipe ? 1.0 : DD_InkCoverage(raw));
                return color;
            }

            // Leaf (§3): glint band × sprite alpha × crack tile, × FlashScale. Kinpaku peak +0.25 (money / Special only),
            // ginpaku +0.10 (AD MUST 2: TextPrimary on the plate stays ≥ 4.5:1 at the peak). The crack tile is sampled in
            // pixel space (AD MUST 1: 1:1, the kinpaku tile is 128²). prm.x metal (0 ginpaku, 1 kinpaku), prm.y band width
            // (0 → 0.10), prm.z tile px (0 → 128); fx.w loop period.
            half4 Leaf(half4 color, v2f IN, float loopPeriod)
            {
                float phase = loopPeriod > 0.0 ? frac((_DD_UiTime - IN.fx.z) / loopPeriod) : IN.fx.y;
                float width = IN.prm.y > 0.0 ? IN.prm.y : 0.10;
                float tilePx = IN.prm.z > 0.0 ? IN.prm.z : 128.0;
                float d = (IN.map.x + IN.map.y * 0.35) / 1.35;
                float c = lerp(-0.3, 1.3, phase);
                float g = exp(-((d - c) * (d - c)) / (width * width));
                // AP-012 kinpaku tile: leaf ≈ 0.65, seams ≈ 0.40 → 0..1; the seams catch 40 % less light, so the 4×4 leaf
                // grid shows only where the band passes (AD 4c MUST 2: 5–8 % in the band, flat at rest).
                float crack = saturate((tex2D(_DD_LeafTile, IN.map.xy * max(IN.map.zw, 1.0) / tilePx).r - 0.40) / 0.25);
                float metal = saturate(IN.prm.x);
                float3 leaf = lerp(_DD_SilverLeaf.rgb, _DD_GoldLeaf.rgb, metal);
                float peak = lerp(0.10, DD_UI_GLINT_MAX_BRIGHTNESS, metal);   // ginpaku 0.10 (AD MUST 2), kinpaku the token ceiling
                float boost = min(peak, peak * g * lerp(0.6, 1.0, crack)) * _DD_Flash;
                color.rgb += leaf * boost * color.a;
                return color;
            }

            // Stamp: the hanko inks in with the scale — centre first, the faint rim last (the kit's B time map; the
            // Ink.Hanko static mask is the sprite).
            half4 Stamp(half4 color, v2f IN)
            {
                float k = _DD_Motion < 0.5 ? 1.0 : saturate((_DD_UiTime - IN.fx.z) / DD_UI_STAMP_IN);
                float m = DD_InkSample(IN.map.xy, 2.0);
                color.a *= saturate(k * 1.35 - m * 0.35) * (1.0 - 0.12 * m);   // AD SHOULD: the landed hanko keeps its rim
                return color;
            }

            // Sheen (menu only): a slow soft band, α ≤ 0.05 of Paper; Decor → absent under reduced motion. prm.x period.
            half4 Sheen(half4 color, v2f IN)
            {
                float period = IN.prm.x > 0.0 ? IN.prm.x : 6.0;
                float phase = frac(_DD_UiTime / period);
                float d = (IN.map.x + (1.0 - IN.map.y) * 0.5) / 1.5;
                float g = exp(-((d - lerp(-0.4, 1.4, phase)) * (d - lerp(-0.4, 1.4, phase))) / 0.012);
                color.rgb += _DD_Paper.rgb * DD_UI_SHEEN_FIELD_ALPHA_MAX * g * _DD_Motion * color.a;
                return color;
            }

            // Segment bar: prm.x fill 0..1, prm.y segments (0 = solid), prm.z ghost value (trail from a loss).
            half4 SegmentBar(half4 color, v2f IN)
            {
                float x = IN.map.x;
                float fill = saturate(IN.prm.x);
                float ghost = max(fill, saturate(IN.prm.z));
                float a = x <= fill ? 1.0 : (x <= ghost ? 0.45 : 0.0);
                float segments = IN.prm.y;
                if (segments >= 2.0)
                {
                    float f = frac(x * segments);
                    float gap = 0.06;
                    a *= step(gap * 0.5, f) * step(f, 1.0 - gap * 0.5);
                }
                color.rgb = x > fill ? lerp(color.rgb, _DD_Paper.rgb, 0.35) : color.rgb;
                color.a *= a;
                return color;
            }

            // SDF glyph: sprite alpha = distance (0.5 edge). prm.x outline width, prm.y glow, prm.z erosion.
            half4 Sdf(v2f IN, half4 tint)
            {
                float dist = tex2D(_MainTex, IN.texcoord).a;
                float aa = max(fwidth(dist), 1e-4);
                float edge = 0.5 + IN.prm.z;
                float fillA = smoothstep(edge - aa, edge + aa, dist);
                if (IN.prm.x < 0.0)
                {
                    // WP-068 12b (AD R1-C SHOULD-1): a negative width draws the glyph hollow - only a band that wide inside
                    // its edge, in the tint (the tree's NEEDS state: an outline-only glyph).
                    float innerA = smoothstep(edge - IN.prm.x - aa, edge - IN.prm.x + aa, dist);
                    return half4(tint.rgb, saturate(fillA - innerA) * tint.a);
                }

                float ringA = IN.prm.x > 0.0 ? saturate(smoothstep(edge - IN.prm.x - aa, edge - IN.prm.x + aa, dist) - fillA) : 0.0;
                float coreA = saturate(fillA + ringA);
                float glowA = IN.prm.y > 0.0 ? saturate(smoothstep(edge - IN.prm.x - IN.prm.y, edge - IN.prm.x, dist) * 0.55 * _DD_Flash - coreA) : 0.0;
                float a = saturate(coreA + glowA);
                half3 rgb = (tint.rgb * (fillA + glowA) + _DD_Ink.rgb * ringA) / max(a, 1e-4);
                return half4(rgb, a * tint.a);
            }

            half4 Particle(half4 color, v2f IN)
            {
                float r = length(IN.map.xy);
                color.a *= (1.0 - smoothstep(0.45, 1.0, r)) * IN.fx.w;
                return color;
            }

            // WP-081 ikon v2 B5b · Icon (8): AP-028-B's O1 glyph - the SDF's own light-only relief, never under its tint
            // (DD_IconShade, DD_UiFx.hlsl). UiIconButton's O1 keys only; every other glyph keeps Sdf() above (IK-M18).
            half4 Icon(v2f IN, half4 tint)
            {
                return DD_IconShade(_MainTex, IN.texcoord, IN.map.xy, tint, IN.fx, IN.prm);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Round the alpha to 8-bit precision (UI-Default: a CanvasGroup fade must not dither).
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0 / alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

                half4 color = IN.color * (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);
                float mode = IN.fx.x;

                if (DD_IsMode(mode, DD_FX_INK))            color = InkReveal(color, IN);
                else if (DD_IsMode(mode, DD_FX_LEAF))      color = Leaf(color, IN, IN.fx.w);
                else if (DD_IsMode(mode, DD_FX_STAMP))     color = Stamp(color, IN);
                else if (DD_IsMode(mode, DD_FX_SHEEN))     color = Sheen(color, IN);
                else if (DD_IsMode(mode, DD_FX_SEGMENT))   color = SegmentBar(color, IN);
                else if (DD_IsMode(mode, DD_FX_SDF))       color = Sdf(IN, IN.color);
                else if (DD_IsMode(mode, DD_FX_PARTICLES)) color = Particle(color, IN);
                else if (DD_IsMode(mode, DD_FX_ICON))      color = Icon(IN, IN.color);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;
                return color;
            }
        ENDCG
        }
    }
}
