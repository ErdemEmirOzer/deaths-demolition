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
        // AP-043: only private TD runtime material clones opt in; shared Arc/Gel/FP/TP retain the original variant.
        [HideInInspector] _Ap043Mode("AP043 option", Float) = 0
        [HideInInspector] _Ap043Layer("AP043 layer", Float) = 0
        [HideInInspector] _Ap043Frame("AP043 presentation clock/flash", Vector) = (0,1,0,1)
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
            #pragma multi_compile_local _ DD_AP043
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _MinPx;
                float _TailFalloff;
                float _TailRampShare;
                float _TailRampLevel;
                float4 _EdgeTint;
                float _Ap043Mode;
                float _Ap043Layer;
                float4 _Ap043Frame;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Head)      // xyz head (world), w width (m)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tail)      // xyz tail (world), w head bead (0..1 of the length)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HeadColor) // rgb HDR (linear), a alpha
                UNITY_DEFINE_INSTANCED_PROP(float4, _TailColor) // rgb HDR (linear), a a premultiplied halo's coverage gain outside its core (WP-104 B4-1; 0 = none, additive layers ignore it)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Span)      // x tail / y head distance from the muzzle (m), z fade distance (0 = none; < 0 = edge-tint share, WP-101), w fade-in start (m) or a bent streak's segment (−w = the streak's length, x / y its distances along it; WP-104 B4-2)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Extra)     // x min width px, y dot size px (dot mode / end-on) or a TD halo's tier step (−y px at 1080p, WP-104 B4-1), z first-person flag (> 0.5) or a TD ceiling (−z px at 1080p, WP-104 B4), w 1 = head dot or the ceiling's floor (−w m, WP-104 B4)
            // DD_AP043 private TD variant, existing channels only:
            // _Extra = (floor px, look+8*tier+64*fusion+512*pellet+seed, -cap px, signed world descriptor).
            // O1 W=+body length; O2 W=0; O3 ordinary W=-body length, tracer W=+rearward burn offset (all metres).
            // _TailColor RGB = separate element-tail tint on metal lanes; A = metal/core share (0 selects separate halo).
            // _Span W<0 keeps total bent optical length; body/focus distances use behindNose metres, not normalized trail t.
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

            #if defined(DD_AP043)
            // Local opt-in variant: no new per-instance channels. _Extra.y packs look/tier/fusion/pellet/seed.
            // _Extra.w: O1 +body length (m); O2 0; O3 ordinary -body length, tracer +burn-focus offset (m).
            // _TailColor: RGB separate element-tail colour on a metal lane; A metal/core share, 0 = separate halo.
            float Ap043PixelWorld(float3 world)
            {
                float depth = max(abs(TransformWorldToView(world).z), 1e-3);
                float perspective = 2.0 * depth / (max(abs(UNITY_MATRIX_P._m11), 1e-4) * _ScreenParams.y);
                float orthographic = 2.0 * unity_OrthoParams.y / _ScreenParams.y;
                return lerp(perspective, orthographic, unity_OrthoParams.w);
            }

            float Ap043Width(float metres, float4 extra, float pixelWorld)
            {
                float scale = _ScreenParams.y / 1080.0;
                float floorMetres = max(extra.x, 0.1) * scale * pixelWorld;
                float capMetres = max(-extra.z, 0.1) * scale * pixelWorld;
                // Neither the old .06 m dot floor, a base-width exemption, SizeScale nor a tier halo step can defeat this cap.
                return clamp(metres, min(floorMetres, capMetres), capMetres);
            }

            float Ap043Hash(float x) { return frac(x * 0.1031) * frac(x * 0.11369 + 0.37); }

            float Ap043Line(float y, float center, float halfWidth, float aa)
            {
                return 1.0 - smoothstep(halfWidth, halfWidth + aa, abs(y - center));
            }

            float Ap043Gaussian(float x) { return exp(-x * x); } // square explicitly: HLSL pow(negative, 2) is not portable

            float Ap043BodyMask(float behindNose, float bodyLength, float sensorFootprint)
            {
                // Integrate a fixed world-space body over one sensor-pixel box. No physical body can scale with the exposure.
                float nearEdge = max(behindNose - 0.5 * sensorFootprint, 0.0);
                float farEdge = min(behindNose + 0.5 * sensorFootprint, bodyLength);
                return saturate((farEdge - nearEdge) / max(sensorFootprint, 1e-5));
            }

            float2 Ap043Contour(float option, float t, bool pellet)
            {
                if (option < 1.5)
                {
                    // O1 metadata fallback only. Normal O1 takes the centimetre body mask in Ap043Frag, never this exposure-sized shape.
                    float bevel = saturate(t / 0.18) * saturate((1.0 - t) / 0.12);
                    return float2(0.68 * bevel, smoothstep(0.02, 0.14, t) * (1.0 - smoothstep(0.88, 0.98, t)));
                }
                if (option < 2.5)
                {
                    // O2: compact camera-exposure kernel with faint ghosts; this does not imply every bullet is incandescent.
                    float kernel = Ap043Gaussian((t - 0.82) / (pellet ? 0.12 : 0.16));
                    // Selected O2: concentrate the pellet into one grain; optical energy no longer spreads into ghosts.
                    if (pellet) return float2(0.45 * kernel, kernel);
                    float ghosts = 0.12 * t * (1.0 - smoothstep(0.60, 0.82, t));
                    float noseWidth = 1.0 - smoothstep(0.86, 1.0, t);
                    float noseAlpha = 1.0 - smoothstep(0.95, 1.0, t);
                    return float2((0.04 + 0.60 * kernel) * noseWidth, saturate(kernel + ghosts) * noseAlpha);
                }
                // O3: a short reflective nose followed by a separate burning-base focus, with a weak integrated exposure.
                float focus = Ap043Gaussian((t - 0.76) / 0.09);
                float nose = smoothstep(0.86, 0.92, t) * (1.0 - smoothstep(0.97, 1.0, t));
                return float2(0.16 + 0.45 * focus + 0.12 * nose, saturate(focus + 0.12 * t));
            }

            float Ap043RearWindow(float t)
            {
                // Matter has its own rear coverage. Multiplying it by the nearly invisible exposure ghost erased its form.
                // The compact white front remains at t~0.82; all rear tokens end before that focus.
                return smoothstep(0.08, 0.16, t) * (1.0 - smoothstep(0.60, 0.76, t));
            }

            float Ap043Element(float look, float t, float y, float seed, float density, float aa)
            {
                float x = t * 7.0 + seed * 13.0;
                float cell = floor(x);
                float random = frac(Ap043Hash(cell + seed * 97.0) * 19.0);
                float local = frac(x) - 0.5;
                float cy = (random - 0.5) * 1.25;
                float sparse = smoothstep(0.84 - density * 0.32, 0.92 - density * 0.32, random);
                if (look < 1.5)
                {
                    // Incendiary: tiny sparse embers, irregularly strung out; no flame tongue or solid orange band.
                    float grain = 1.0 - smoothstep(0.13, 0.24 + fwidth(x), abs(local));
                    return sparse * grain * Ap043Line(y, cy, 0.035, aa);
                }
                if (look < 2.5)
                {
                    // Cryogenic: separated diagonal crystalline ticks over a restrained condensation filament.
                    float crystal = (1.0 - smoothstep(0.18, 0.28 + fwidth(x), abs(local)))
                        * Ap043Line(y, cy + local * 1.5, 0.03, aa);
                    float vapour = Ap043Line(y, 0.3 * sin(t * 11.0 + seed * 9.0), 0.12, aa) * 0.14;
                    return max(sparse * crystal, vapour);
                }
                if (look < 3.5)
                {
                    // Ionised: thin, broken zigzag plus a brief micro-branch; FlashScale selects <= 3 Hz modulation.
                    float tipTriangle = abs(frac(t * 4.0 + seed) * 2.0 - 1.0) * 2.0 - 1.0;
                    float center = tipTriangle * 0.32;
                    float filament = Ap043Line(y, center, 0.025, aa);
                    float branch = sparse * (1.0 - smoothstep(0.2, 0.35 + fwidth(x), abs(local)))
                        * Ap043Line(y, center + local * 2.0, 0.02, aa);
                    float hz = lerp(45.0, 3.0, _Ap043Frame.z);
                    float pulse = 0.9 + 0.1 * sin(_Ap043Frame.x * hz * 6.2831853 + seed * 17.0);
                    return max(filament, branch) * pulse;
                }
                if (look < 4.5)
                {
                    // Corrosive aerosol: small asymmetric droplet ellipses, distinct from the ice ticks and electric zigzag.
                    float droplet = length(float2(local * 1.4, (y - cy) * 0.65));
                    float mist = Ap043Line(y, 0.18 * sin(t * 8.0 + seed * 7.0), 0.16, aa) * 0.12;
                    return max(sparse * (1.0 - smoothstep(0.09, 0.16 + aa, droplet)), mist);
                }
                // K3: a narrow violet rim around the sumi insert, not a tier-V halo expansion.
                return Ap043Line(abs(y), 0.55, 0.04, aa);
            }

            float4 Ap043Frag(Varyings i)
            {
                float4 headColor = UNITY_ACCESS_INSTANCED_PROP(Props, _HeadColor);
                float4 tailColor = UNITY_ACCESS_INSTANCED_PROP(Props, _TailColor);
                float4 span = UNITY_ACCESS_INSTANCED_PROP(Props, _Span);
                float4 extra = UNITY_ACCESS_INSTANCED_PROP(Props, _Extra);
                float whole = floor(extra.y);
                float look = fmod(whole, 8.0);
                float tier = fmod(floor(whole / 8.0), 8.0);
                float secondary = fmod(floor(whole / 64.0), 8.0);
                bool pellet = floor(whole / 512.0) > 0.5;
                float seed = frac(extra.y);
                float density = (0.15 + 0.85 * saturate((tier - 1.0) * 0.25)) * _Ap043Frame.w;
                float t = span.w < 0.0 ? saturate(i.muzzleDist / -span.w) : saturate(i.uv.x);
                float trailLength = span.w < 0.0 ? -span.w : max(span.y - span.x, 0.01);
                float behindNose = (1.0 - t) * trailLength;
                float y = i.uv.y;
                float aa = max(fwidth(y), 1e-3);
                float at = max(fwidth(t), 1e-3);
                float sensorFootprint = max(fwidth(behindNose), 1e-5);
                bool worldBody = (_Ap043Mode < 1.5 && extra.w > 0.0) || (_Ap043Mode > 2.5 && extra.w < 0.0);
                float bodyLength = max(abs(extra.w), 0.004);
                float metalBodyMask = worldBody ? Ap043BodyMask(behindNose, bodyLength, sensorFootprint) : 0.0;
                // Separate contours/exposure, including pellets: three matter languages, no shared capsule/taper silhouette.
                float2 contour = worldBody ? float2(0.68, metalBodyMask) : Ap043Contour(_Ap043Mode, t, pellet);
                if (_Ap043Mode > 2.5 && _Ap043Layer < 0.5 && extra.w > 0.0)
                {
                    float baseFocus = Ap043Gaussian((behindNose - extra.w) / 0.009);
                    contour = float2(0.14 + 0.48 * baseFocus, baseFocus + 0.07 * t);
                }
                float taper = contour.x;
                float across = Ap043Line(y, 0.0, taper, aa);
                float longitudinal = contour.y;
                float3 rgb = lerp(tailColor.rgb, headColor.rgb, t);
                float alpha = headColor.a * across * longitudinal;
                float3 opticalPremultiplied = float3(0.0, 0.0, 0.0);
                float3 metalReflectionPremultiplied = float3(0.0, 0.0, 0.0);
                float opticalAlpha = 0.0;
                bool metal = _Ap043Layer > 0.5 && _Ap043Layer < 1.5 && tailColor.a > 0.0;
                bool halo = _Ap043Layer > 0.5 && _Ap043Layer < 1.5 && !metal;
                if (metal)
                {
                    float coreFraction = saturate(tailColor.a);
                    across = Ap043Line(y, 0.0, taper * coreFraction, aa);
                    float ridge = Ap043Line(y, -0.14 * coreFraction, 0.035 * coreFraction, aa);
                    float sparseHighlight = smoothstep(0.42, 0.78, frac(t * 2.0 + seed));
                    rgb = lerp(headColor.rgb, float3(0.56, 0.57, 0.58), ridge * sparseHighlight * (0.42 + 0.25 * density));
                    alpha = headColor.a * across * longitudinal;
                    if (worldBody)
                    {
                        // Metal occupies 23–34 mm (pellet 5 mm), plus one pixel of sensor/AA integration; the exposure is optical.
                        float bodyT = saturate(behindNose / bodyLength);
                        float bodyHalf = (0.35 + 0.30 * smoothstep(0.0, 0.35, bodyT)) * coreFraction;
                        float bodyAcross = Ap043Line(y, 0.0, bodyHalf, aa);
                        float oneHighlight = Ap043Gaussian((behindNose - bodyLength * 0.35) / max(sensorFootprint * 0.55, 0.002));
                        float glintAcross = Ap043Line(y, -0.14 * coreFraction, 0.035 * coreFraction, aa);
                        rgb = lerp(headColor.rgb, float3(0.56, 0.57, 0.58), oneHighlight * glintAcross * (0.42 + 0.25 * density));
                        alpha = headColor.a * bodyAcross * metalBodyMask;
                        // One bounded reflected-light focus: 1 px cross FWHM, ~0.92 px longitudinal FWHM, inside the existing body.
                        // Scene-linear PM trial gain only; post +DeltaY .04-.08 awaits native capture, no alpha/length/width growth.
                        float pixelNormal = max(length(float2(ddx(y), ddy(y))), 1e-3);
                        float pixelGlint = saturate(1.0 - abs(y + 0.14 * coreFraction) / pixelNormal);
                        metalReflectionPremultiplied = float3(0.80, 0.80, 0.80) * headColor.a * bodyAcross
                            * metalBodyMask * oneHighlight * pixelGlint;
                        // A separately shaded, <=3.5% alpha sensor sliver follows the physical mass; it is not a metal rod.
                        float sliver = (1.0 - metalBodyMask) * smoothstep(0.0, 0.12, t) * t;
                        opticalAlpha = headColor.a * 0.035 * sliver * Ap043Line(y, 0.0, 0.07 * coreFraction, aa);
                        opticalPremultiplied = float3(0.28, 0.30, 0.31) * opticalAlpha;
                    }
                    if (_Ap043Mode > 1.5 && _Ap043Mode < 2.5)
                    {
                        // O2 pellet: a compact dark grain plus one sensor-pixel specular focus, within the old RGB maximum.
                        float sensorNormal = max(length(float2(ddx(y), ddy(y))), 1e-3);
                        // Keep most of the compact grain dark. A subpixel glint must not wash the entire metal mass into the lit stone value.
                        float grainGlint = saturate(1.0 - abs(y + 0.14 * coreFraction) / (sensorNormal * 0.35));
                        rgb = lerp(float3(0.05, 0.06, 0.065), float3(0.48, 0.50, 0.49), grainGlint);
                    }
                    if (_Ap043Mode > 2.5)
                    {
                        // A deterministic continuous specular glint, not per-frame random visibility or a flashing streak.
                        float reflection = pow(saturate(0.5 + 0.5 * sin(_Ap043Frame.x * 14.0 + seed * 19.0)), 6.0);
                        alpha *= 0.32 + 0.68 * reflection;
                        if (worldBody)
                        {
                            opticalAlpha *= 0.32 + 0.68 * reflection;
                            opticalPremultiplied *= 0.32 + 0.68 * reflection;
                            metalReflectionPremultiplied *= 0.75 + 0.25 * reflection;
                        }
                        if (!pellet && extra.w > 0.0)
                        {
                            // The nose is reflective; emission sits a fixed world distance behind it, not 24% of the whole trail.
                            float nose = 1.0 - smoothstep(extra.w * 0.4, extra.w * 0.9, behindNose);
                            metalBodyMask = nose;
                            alpha = headColor.a * across * nose * (0.45 + 0.55 * reflection);
                        }
                    }
                }
                else if (halo)
                {
                    across = 1.0 - smoothstep(0.15, 0.9, abs(y));
                    alpha = headColor.a * across * longitudinal * (0.35 + 0.65 * density);
                }
                else if (_Ap043Mode > 2.5 && _Ap043Layer < 0.5)
                {
                    float burning = Ap043Gaussian((behindNose - max(extra.w, 0.024)) / 0.009);
                    rgb = lerp(tailColor.rgb, headColor.rgb, burning);
                    // Emission comes from a rearward burning focus; the metal nose belongs to the premultiplied lane.
                    alpha *= smoothstep(0.002, 0.012, behindNose);
                }

                if (_Ap043Mode > 1.5 && _Ap043Mode < 2.5 && look > 0.5 && _Ap043Layer < 0.5)
                {
                    // Preserve the compact exposure head. Behind it, remove white coverage so elemental matter stays legible.
                    float matter = Ap043Element(look, t, y, seed, density, aa);
                    // Fusion's second form occupies the rear tokens; K1 keeps the dominant exposure hue.
                    if (secondary > 0.5)
                    {
                        float rearTokens = 1.0 - smoothstep(0.45, 0.62, t);
                        matter = max(matter * (1.0 - 0.7 * rearTokens), rearTokens * Ap043Element(secondary, t, -y, frac(seed + 0.37), density, aa));
                    }
                    float headGate = smoothstep(0.64, 0.84, t);
                    alpha *= lerp(0.10 + 0.65 * saturate(matter), 1.0, headGate);
                    rgb = lerp(tailColor.rgb, headColor.rgb, headGate);
                }

                if (look > 0.5 && _Ap043Layer > 0.5 && _Ap043Layer < 1.5)
                {
                    float detail = Ap043Element(look, t, y, seed, density, aa);
                    // Fusion preserves K1's dominant hue; one additional elemental form shares the existing bounded footprint.
                    if (secondary > 0.5) detail = max(detail, 0.6 * Ap043Element(secondary, t, -y, frac(seed + 0.37), density, aa));
                    float matterEnvelope = _Ap043Mode > 1.5 && _Ap043Mode < 2.5 ? Ap043RearWindow(t) : longitudinal;
                    float detailAlpha = detail * matterEnvelope * (0.12 + 0.25 * density);
                    if (metal)
                    {
                        // Element tail is coloured particulate/filament coverage. Never max() it back into the metal body.
                        float tailMask = (1.0 - (worldBody || extra.w > 0.0 ? metalBodyMask : saturate(longitudinal)))
                            * smoothstep(0.0, 0.12, t) * t;
                        float elementAlpha = detail * tailMask * (0.08 + 0.18 * density);
                        opticalPremultiplied += tailColor.rgb * elementAlpha * (1.0 - opticalAlpha);
                        opticalAlpha += elementAlpha * (1.0 - opticalAlpha);
                    }
                    else alpha = max(alpha, detailAlpha);
                }
                else if (halo && (tier >= 3.0 || (_Ap043Mode > 1.5 && _Ap043Mode < 2.5 && tier >= 2.0)))
                {
                    // Progression allocates sparse rear coverage inside the unchanged envelope; the front/core/halo caps stay fixed.
                    float arcLine = Ap043Line(y, 0.38 + 0.12 * sin(t * 9.0 + seed * 7.0), 0.025, aa);
                    if (_Ap043Mode > 1.5 && _Ap043Mode < 2.5)
                    {
                        float firstToken = Ap043Gaussian((t - 0.54) / 0.045) * Ap043Line(y, -0.1, 0.07, aa) * 0.28;
                        float gaps = 1.0 - smoothstep(0.26, 0.44, abs(frac(t * 3.0 + seed * 0.3) - 0.5));
                        float stagedFilament = tier >= 3.0 ? arcLine * gaps * (0.16 + 0.12 * density) : 0.0;
                        float secondToken = tier >= 4.0 ? Ap043Gaussian((t - 0.27) / 0.045) * Ap043Line(y, 0.2, 0.07, aa) * (0.16 + 0.08 * density) : 0.0;
                        float thirdToken = tier >= 5.0 ? Ap043Gaussian((t - 0.40) / 0.04) * Ap043Line(y, -0.25, 0.07, aa) * 0.20 : 0.0;
                        float rearCoverage = max(firstToken, max(stagedFilament, max(secondToken, thirdToken)));
                        alpha = max(alpha, Ap043RearWindow(t) * rearCoverage);
                    }
                    else alpha = max(alpha, arcLine * longitudinal * density * 0.1);
                }

                if (i.dot > 0.5)
                {
                    float r = length(float2(i.uv.x * 2.0 - 1.0, y));
                    alpha = headColor.a * (1.0 - smoothstep(0.5, 0.88, r));
                    rgb = headColor.rgb;
                    opticalAlpha = 0.0; opticalPremultiplied = float3(0.0, 0.0, 0.0);
                    metalReflectionPremultiplied = float3(0.0, 0.0, 0.0); // end-on behavior retains its original bounded dot
                }

                return float4(rgb * alpha + metalReflectionPremultiplied + opticalPremultiplied * (1.0 - alpha), saturate(alpha + opticalAlpha * (1.0 - alpha)));
            }
            #endif

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
#if defined(DD_AP043)
                float pixelWorld = Ap043PixelWorld(p);
                float width = Ap043Width(head.w, extra, pixelWorld);
                float stepPx = 0.0;
#else
                float pixelWorld = 2.0 * camDist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);   // abs: a flipped render-texture projection (Metal / URP intermediate) has _m11 < 0 - every px floor / ceiling went negative (WP-104 B4 capture)
                float minPx = extra.x > 0.0 ? extra.x : _MinPx;
                float width = max(head.w, minPx * pixelWorld);
                // WP-104 B4-1 (AD-B4 §1): a TD tracer halo's tier step, −y px at 1080p (resolution-proportional) over its metre width
                float stepPx = max(-extra.y, 0.0) * (_ScreenParams.y / 1080.0);
                width += stepPx * pixelWorld;
                if (extra.z > 0.5) width = min(width, max(6.0, minPx) * pixelWorld);   // FP: a core never wider than 6 px (AD B1 MUST 6)
                else if (extra.z < -0.5) width = min(width, max(-extra.z * pixelWorld * (_ScreenParams.y / 1080.0), -min(extra.w, 0.0)));   // WP-104 B4: a styled TD round's ceiling, -z px at 1080p (AD §2 / A5), never under its WP-101 width (-w m)

#endif

                // The segment's projected length in pixels: a bar seen end-on (velocity ≈ view axis) has no readable axis —
                // it collapses to its head bead, a round dot, instead of standing up as a stick (AD B1 MUST 1). A head dot
                // instance (extra.w) is always drawn that way.
                float4 hc = TransformWorldToHClip(head.xyz);
                float4 tc = TransformWorldToHClip(tail.xyz);
                float2 hs = hc.xy / max(hc.w, 1e-4) * 0.5 * _ScreenParams.xy;
                float2 ts = tc.xy / max(tc.w, 1e-4) * 0.5 * _ScreenParams.xy;
#if defined(DD_AP043)
                bool dotMode = length(hs - ts) < max(2.0, width / pixelWorld);
#else
                bool dotMode = extra.w > 0.5 || length(hs - ts) < 4.0;
#endif
                o.dot = dotMode ? 1.0 : 0.0;
                if (dotMode)
                {
                    float headDist = max(length(GetCameraPositionWS() - head.xyz), 1e-3);
                    float headPixel = 2.0 * headDist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
#if defined(DD_AP043)
                    headPixel = Ap043PixelWorld(head.xyz);
                    float size = Ap043Width(head.w, extra, headPixel);
#else
                    float dotW = head.w + stepPx * headPixel;   // WP-104 B4-1: an end-on halo keeps its tier step
                    if (extra.z < -0.5) dotW = min(dotW, max(-extra.z * headPixel * (_ScreenParams.y / 1080.0), -min(extra.w, 0.0)));   // WP-104 B4: the ceiling holds for an end-on dot too
                    float size = max(max(dotW * 2.0, 0.06), extra.y * headPixel);
#endif
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
#if defined(DD_AP043)
                return Ap043Frag(i);
#else
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
#endif
            }
            ENDHLSL
        }
    }
}
