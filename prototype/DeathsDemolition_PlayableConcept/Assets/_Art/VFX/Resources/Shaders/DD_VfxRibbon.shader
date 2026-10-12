// DD/VFX Ribbon — WP-073 · tracer / glint / pellet-bar ribbons (direction §1.2). One quad mesh (u 0 tail → 1 head, v −1..1
// across) drawn instanced by TracerRibbonRenderer; the vertex stage builds a camera-facing strip between the per-instance
// tail and head, never thinner than _MinPx on screen (K2: ≥ 2 px, so a far tracer stays a line, not a flicker). The
// fragment stage: a full-intensity head bead, an exponential tail whose last 40 % stays ≤ 0.15 (_TailRampShare / _TailRampLevel), a soft cross-section, an
// optional distance fade (pellet bars die out after 6 m) and the first-person near-camera ramp (α 0→1 over the first
// 0.4 m from the muzzle, HDR × 0.15 inside 1.5 m of the eye → × 1 at 3 m, core ≤ 6 px), a segment shorter than 4 px on screen
// (seen end-on) or a head-dot instance drawn as a soft round dot. Additive, HDR (bloom does the glow's core).
// WP-104 B4-1 (AD-B4): a TD tracer halo widens by its tier's px step (_Extra.y < 0, px at 1080p over its metre width) and an
// element halo covers the ground outside its core (_TailColor.a: the premultiplied α grows, the added colour does not).
// WP-104 B4-2 (B9): a bending seeker streak is several consecutive instances; _Span.w < 0 marks one of them — t runs along the WHOLE
// streak (its distance along it, _Span.x → .y, over the streak's length −w), so the head bead, the tail falloff and the colour run
// once, and the muzzle fade-in is skipped. Every other instance writes w ≥ 0: unchanged.
// All-float CBUFFER (Metal); per-instance data through UNITY_INSTANCING_BUFFER (MaterialPropertyBlock arrays).
Shader "DD/VFX Ribbon"
{
    Properties
    {
        _MinPx("Minimum width (px)", Float) = 2
        _TailFalloff("Tail falloff (exp k)", Float) = 3.2
        // WP-095 R3 (C-00 11.10): the tail-end share held to a linear ramp and the ramp's top; 0.4 / 0.15 = every existing ribbon
        _TailRampShare("Tail ramp share", Float) = 0.4
        _TailRampLevel("Tail ramp level", Float) = 0.15
        // WP-095 R3 (AD 11.10 M1): additive by default (One / One, every existing ribbon); the top-down tracer glow uses a
        // premultiplied instance (One / OneMinusSrcAlpha) - added orange over the blue sky reflection read salmon-pink.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst blend", Float) = 1
        // WP-101 (AD M3): the top-down halo's outer half takes this tint (a negative _Span.z gives the share); white = none
        _EdgeTint("Edge tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Ribbon"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend], Zero One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _MinPx;
                float _TailFalloff;
                float _TailRampShare;
                float _TailRampLevel;
                float4 _EdgeTint;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Head)      // xyz head (world), w width (m)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tail)      // xyz tail (world), w head bead (0..1 of the length)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HeadColor) // rgb HDR (linear), a alpha
                UNITY_DEFINE_INSTANCED_PROP(float4, _TailColor) // rgb HDR (linear), a a premultiplied halo's coverage gain outside its core (WP-104 B4-1; 0 = none, additive layers ignore it)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Span)      // x tail / y head distance from the muzzle (m), z fade distance (0 = none; < 0 = edge-tint share, WP-101), w fade-in start (m) or a bent streak's segment (−w = the streak's length, x / y its distances along it; WP-104 B4-2)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Extra)     // x min width px, y dot size px (dot mode / end-on) or a TD halo's tier step (−y px at 1080p, WP-104 B4-1), z first-person flag (> 0.5) or a TD ceiling (−z px at 1080p, WP-104 B4), w 1 = head dot or the ceiling's floor (−w m, WP-104 B4)
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
                float muzzleDist : TEXCOORD2;
                float dot : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 head = UNITY_ACCESS_INSTANCED_PROP(Props, _Head);
                float4 tail = UNITY_ACCESS_INSTANCED_PROP(Props, _Tail);
                float4 span = UNITY_ACCESS_INSTANCED_PROP(Props, _Span);
                float4 extra = UNITY_ACCESS_INSTANCED_PROP(Props, _Extra);

                float3 axis = head.xyz - tail.xyz;
                float len = length(axis);
                float3 dir = len > 1e-4 ? axis / len : float3(0, 0, 1);
                float3 p = lerp(tail.xyz, head.xyz, v.uv.x);
                float3 toCam = GetCameraPositionWS() - p;
                float camDist = max(length(toCam), 1e-3);
                float3 side = cross(dir, toCam / camDist);
                float sideLen = length(side);
                side = sideLen > 1e-4 ? side / sideLen : float3(0, 1, 0);

                // K2: one screen pixel at this depth ≈ 2·d / (P11 · screen height) in world units (perspective).
                float pixelWorld = 2.0 * camDist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);   // abs: a flipped render-texture projection (Metal / URP intermediate) has _m11 < 0 - every px floor / ceiling went negative (WP-104 B4 capture)
                float minPx = extra.x > 0.0 ? extra.x : _MinPx;
                float width = max(head.w, minPx * pixelWorld);
                // WP-104 B4-1 (AD-B4 §1): a TD tracer halo's tier step, −y px at 1080p (resolution-proportional) over its metre width
                float stepPx = max(-extra.y, 0.0) * (_ScreenParams.y / 1080.0);
                width += stepPx * pixelWorld;
                if (extra.z > 0.5) width = min(width, max(6.0, minPx) * pixelWorld);   // FP: a core never wider than 6 px (AD B1 MUST 6)
                else if (extra.z < -0.5) width = min(width, max(-extra.z * pixelWorld * (_ScreenParams.y / 1080.0), -min(extra.w, 0.0)));   // WP-104 B4: a styled TD round's ceiling, -z px at 1080p (AD §2 / A5), never under its WP-101 width (-w m)

                // The segment's projected length in pixels: a bar seen end-on (velocity ≈ view axis) has no readable axis —
                // it collapses to its head bead, a round dot, instead of standing up as a stick (AD B1 MUST 1). A head dot
                // instance (extra.w) is always drawn that way.
                float4 hc = TransformWorldToHClip(head.xyz);
                float4 tc = TransformWorldToHClip(tail.xyz);
                float2 hs = hc.xy / max(hc.w, 1e-4) * 0.5 * _ScreenParams.xy;
                float2 ts = tc.xy / max(tc.w, 1e-4) * 0.5 * _ScreenParams.xy;
                bool dotMode = extra.w > 0.5 || length(hs - ts) < 4.0;
                o.dot = dotMode ? 1.0 : 0.0;
                if (dotMode)
                {
                    float headDist = max(length(GetCameraPositionWS() - head.xyz), 1e-3);
                    float headPixel = 2.0 * headDist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                    float dotW = head.w + stepPx * headPixel;   // WP-104 B4-1: an end-on halo keeps its tier step
                    if (extra.z < -0.5) dotW = min(dotW, max(-extra.z * headPixel * (_ScreenParams.y / 1080.0), -min(extra.w, 0.0)));   // WP-104 B4: the ceiling holds for an end-on dot too
                    float size = max(max(dotW * 2.0, 0.06), extra.y * headPixel);
                    float3 camRight = UNITY_MATRIX_V[0].xyz;
                    float3 camUp = UNITY_MATRIX_V[1].xyz;
                    p = head.xyz + (camRight * (v.uv.x * 2.0 - 1.0) + camUp * v.uv.y) * (0.5 * size);
                }
                else
                {
                    p += side * (v.uv.y * 0.5 * width);
                }

                o.world = p;
                o.uv = v.uv;
                o.muzzleDist = dotMode ? span.y : lerp(span.x, span.y, v.uv.x);
                o.positionHCS = TransformWorldToHClip(p);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 headColor = UNITY_ACCESS_INSTANCED_PROP(Props, _HeadColor);
                float4 tailColor = UNITY_ACCESS_INSTANCED_PROP(Props, _TailColor);
                float4 tail = UNITY_ACCESS_INSTANCED_PROP(Props, _Tail);
                float4 span = UNITY_ACCESS_INSTANCED_PROP(Props, _Span);
                float4 extra = UNITY_ACCESS_INSTANCED_PROP(Props, _Extra);

                float t = span.w < 0.0 ? saturate(i.muzzleDist / -span.w) : saturate(i.uv.x);   // WP-104 B4-2: a bent streak's segment reads t along the whole streak
                float bead = tail.w;                                    // the head bead's share of the length
                float body = exp(-(1.0 - t) * _TailFalloff);            // exponential tail
                body = t < _TailRampShare ? min(body, _TailRampLevel * (t / max(_TailRampShare, 1e-4))) : body;   // the last 40 % stays ≤ 0.15 (defaults)
                float a = t >= 1.0 - bead ? 1.0 : body;
                float across = 1.0 - smoothstep(0.35, 1.0, abs(i.uv.y));
                float3 rgb = lerp(tailColor.rgb, headColor.rgb, smoothstep(0.0, 1.0, t));
                if (i.dot > 0.5)
                {
                    // The head bead as a soft round dot, head colour, full intensity.
                    float r = length(float2(i.uv.x * 2.0 - 1.0, i.uv.y));
                    a = 1.0;
                    across = 1.0 - smoothstep(0.3, 1.0, r);
                    rgb = headColor.rgb;
                }

                float alpha = headColor.a * a * across;
                // WP-104 B4-1 (AD-B4 M5): an element halo hides more of the ground it veils outside its core (any halo's core lies in
                // |v| ≤ 0.25) - the colour it adds stays rgb × α, only the premultiplied α grows (_TailColor.a; 0 = WP-101)
                float cover = i.dot < 0.5 ? 1.0 + max(tailColor.a, 0.0) * smoothstep(0.15, 0.35, abs(i.uv.y)) : 1.0;
                if (span.z < 0.0 && i.dot < 0.5)
                {
                    // WP-101 (AD M3): the halo's outer half warms to _EdgeTint (an amber rim reads on the light stone)
                    rgb = lerp(rgb, _EdgeTint.rgb, smoothstep(0.45, 0.85, abs(i.uv.y)) * saturate(-span.z));
                }
                if (span.z > 0.0)
                {
                    alpha *= 1.0 - smoothstep(span.z * 0.6, span.z, i.muzzleDist);   // pellet bars die out
                }

                if (span.w >= 0.0) alpha *= smoothstep(span.w, span.w + 0.4, i.muzzleDist);   // fade in off the muzzle (a bent streak's segment: none, WP-104 B4-2)
                if (extra.z > 0.5)
                {
                    float eye = length(GetCameraPositionWS() - i.world);
                    rgb *= lerp(0.15, 1.0, saturate((eye - 1.5) / 1.5));                // FP: no blinding bar at the eye
                }

                return float4(rgb * alpha, min(alpha * cover, 1.0));   // premultiplied; the target's alpha is kept (Zero One)
            }
            ENDHLSL
        }
    }
}
