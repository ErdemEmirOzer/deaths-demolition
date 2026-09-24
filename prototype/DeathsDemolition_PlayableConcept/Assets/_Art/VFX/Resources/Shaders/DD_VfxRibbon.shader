// DD/VFX Ribbon — WP-073 · tracer / glint / pellet-bar ribbons (direction §1.2). One quad mesh (u 0 tail → 1 head, v −1..1
// across) drawn instanced by TracerRibbonRenderer; the vertex stage builds a camera-facing strip between the per-instance
// tail and head, never thinner than _MinPx on screen (K2: ≥ 2 px, so a far tracer stays a line, not a flicker). The
// fragment stage: a full-intensity head bead, an exponential tail whose last 40 % stays ≤ 0.15, a soft cross-section, an
// optional distance fade (pellet bars die out after 6 m) and the first-person near-camera ramp (α 0→1 over the first
// 0.4 m from the muzzle, HDR × 0.3 inside 1.5 m of the eye → × 1 at 3 m). Additive, HDR (bloom does the glow's core).
// All-float CBUFFER (Metal); per-instance data through UNITY_INSTANCING_BUFFER (MaterialPropertyBlock arrays).
Shader "DD/VFX Ribbon"
{
    Properties
    {
        _MinPx("Minimum width (px)", Float) = 2
        _TailFalloff("Tail falloff (exp k)", Float) = 3.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Ribbon"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
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
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Head)      // xyz head (world), w width (m)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tail)      // xyz tail (world), w head bead (0..1 of the length)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HeadColor) // rgb HDR (linear), a alpha
                UNITY_DEFINE_INSTANCED_PROP(float4, _TailColor) // rgb HDR (linear), a first-person flag (1 = ramp)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Span)      // x tail / y head distance from the muzzle (m), z fade distance (0 = none), w fade-in start (m)
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
                float pixelWorld = 2.0 * camDist / (UNITY_MATRIX_P._m11 * _ScreenParams.y);
                float width = max(head.w, _MinPx * pixelWorld);
                p += side * (v.uv.y * 0.5 * width);

                o.world = p;
                o.uv = v.uv;
                o.muzzleDist = lerp(span.x, span.y, v.uv.x);
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

                float t = saturate(i.uv.x);
                float bead = tail.w;                                    // the head bead's share of the length
                float body = exp(-(1.0 - t) * _TailFalloff);            // exponential tail
                body = t < 0.4 ? min(body, 0.15 * (t / 0.4)) : body;   // the last 40 % stays ≤ 0.15
                float a = t >= 1.0 - bead ? 1.0 : body;
                float across = 1.0 - smoothstep(0.35, 1.0, abs(i.uv.y));
                float3 rgb = lerp(tailColor.rgb, headColor.rgb, smoothstep(0.0, 1.0, t));

                float alpha = headColor.a * a * across;
                if (span.z > 0.0)
                {
                    alpha *= 1.0 - smoothstep(span.z * 0.6, span.z, i.muzzleDist);   // pellet bars die out
                }

                alpha *= smoothstep(span.w, span.w + 0.4, i.muzzleDist);               // fade in off the muzzle
                if (tailColor.a > 0.5)
                {
                    float eye = length(GetCameraPositionWS() - i.world);
                    rgb *= lerp(0.3, 1.0, saturate((eye - 1.5) / 1.5));                 // FP: no blinding bar at the eye
                }

                return float4(rgb * alpha, 0.0);
            }
            ENDHLSL
        }
    }
}
