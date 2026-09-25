// DD/VFX Atmo — WP-073 B5 · atmosphere grains (direction §3 A3, DEC-100 / DEC-102): dust and pollen motes, sakura petals,
// bamboo / broad leaves, wet-ground steam wisps and eave / bamboo drips, all drawn by AtmosphereSystem's five world-space
// ParticleSystems (billboards; drips as stretched billboards). One shader, one material per layer (per-layer look below).
//
// LIGHT. Nothing here is lit by URP's light loop; the grain is a tinted card whose brightness follows the sun it actually sits in:
//   sunVis   = DD_CloudSunLit01(centre) (S-05 cloud cookie, 0 in the cloud shadow, 1 in a sun patch — for a point in the air
//              the cookie of the ground at the end of its sun ray, i.e. the same value along a whole shaft)
//            × the main light's realtime shadow at the centre (vertex stage, one hard tap: in the canopy shade a mote is dim)
//   base     = lerp(_AtmoLight.x, _AtmoLight.y, sunVis)                       0.7 in shade → 1.6 in the sun (direction §3)
//   scatter  = [4 · pow(cosθ, 8) · (0.5 + 0.5 · _DD_ShaftDir.w) + 0.6 · pow(0.5 + 0.5 cosθ, 4)] · sunVis · (1 + 1.5 · shaft)
//              cosθ = dot(view ray camera → grain, −light travel direction): the narrow forward lobe is the "backlit dust"
//              glint when the camera looks into the sun; the broad lobe keeps a sun-side sheen under DEC-102's HIGH sun, where
//              a horizontal TP/FP view never gets within 30° of the sun and the narrow lobe alone would never fire.
//   shaft    = DEC-102, the volumetric field live (_DD_VL_Density.w): DD_ShaftMask(centre) — S-05's field density over its
//              peak × the cloud cookie (grove > 0.8, plaza < 0.2), so the grains glint where the light shafts are;
//              the field off (Performance): inside one of S-05's shaft cards (_DD_AtmoShaftA/B, published by AtmosphereSystem
//              from "Shafts (C4)"), radial falloff × along-shaft falloff × the card's peak weight.
//   sparkle  = a 3–7 Hz per-grain twinkle (pow 6 of a sine; phase from StableRandom.x), only where the sun reaches.
//   colour   = vertex colour × (base + scatter + sparkle) + the sun's tone × (scatter + sparkle) × 0.35, ≤ _AtmoLight.z (HDR);
//              in shade the colour leans 35 % toward the cool mist #D0D7DE (DEC-101: cool shadows, warmth only in the sun).
// READABILITY (direction §4 #6). α ≤ _AtmoAlpha (grains 0.35, steam 0.12); 0 within _AtmoFade.x (0.35 m) of the camera, full by
// _AtmoFade.y; screen size clamped to [_AtmoPx.x, _AtmoPx.y] = [1.5, 24] px (an enlarged far grain loses α in proportion, so a
// 0.03 m mote seen from the top-down camera is a faint 1.5 px dot, not a bright square); the player guard (_DD_AtmoGuard: chest
// point + radius) fades grains on the camera → player line of sight and around the body to × 0.2, so nothing sits on the silhouette.
// SHAPE. Petals and leaves tumble without a mesh: the billboard is squashed along its own in-plane axis by |cos φ| (φ spins at
// _AtmoMotion.x rev/s, per-grain rate and phase) and its back face is shaded darker — the edge-on flicker of a falling petal; petals
// also sway sideways (_AtmoMotion.y Hz, _AtmoMotion.z m). The atlas frame (AtmosphereAtlas 4 × 2) rides in size.z (the billboard
// ignores it; sizeOverLifetime keeps z at 1). Drips swap the uv (stretched billboards run along u).
// Soft particles (_DD_ATMO_SOFT, High / Cinematic: scene depth, _AtmoFade.z m), linear fog. Alpha blend, ZWrite off, no culling.
// Vertex streams (AtmosphereSystem.Streams, in this order): Position, Color, UV, Center, StableRandomX, AgePercent, Rotation, SizeXYZ
//   → TEXCOORD0 = (uv.xy, centre.xy) · TEXCOORD1 = (centre.z, random, age01, rotation rad) · TEXCOORD2 = size.xyz.
// All-float CBUFFER (Metal, as DD/VFX Ribbon). Include order: URP Core → Lighting (shadows, main light) → depth → DD_CloudCookie
// → DD_VolumetricLight.
Shader "DD/VFX Atmo"
{
    Properties
    {
        _MainTex("Atlas (AtmosphereAtlas, 4 x 2, procedural)", 2D) = "white" {}
        _AtmoAlpha("Alpha cap (grain 0.35, steam 0.12)", Float) = 0.35
        _AtmoGlint("Glint gain (motes 1, petals 0.5, steam 0.25)", Float) = 1
        _AtmoTwinkle("Twinkle: Hz min, Hz max, amount, shade alpha floor", Vector) = (3, 7, 0.45, 0.45)
        _AtmoMotion("Motion: tumble rev/s, sway Hz, sway m, uv swap", Vector) = (0, 0, 0, 0)
        _AtmoFade("Fade: near start m, near full m, soft m, far m", Vector) = (0.35, 1.0, 0.3, 60)
        _AtmoLight("Light: shade, sun, HDR cap, steam sun floor", Vector) = (0.7, 1.6, 2.0, 1)
        _AtmoPx("Screen size px: min, max", Vector) = (1.5, 24, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Atmo"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            // Vertex-stage shadow: the cascade / single-map keywords only. _MAIN_LIGHT_SHADOWS_SCREEN samples a screen-space
            // texture in the fragment stage and is not usable here — the variant falls back to "no shadow" (cookie only).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_local _ _DD_ATMO_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/_Art/Shaders/DD_CloudCookie.hlsl"
            #include "Assets/_Art/Shaders/DD_VolumetricLight.hlsl"   // after the cookie: DD_ShaftMask (S-05 205ef275)

            #define DD_ATMO_MAX_SHAFTS 20

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _AtmoAlpha;
                float _AtmoGlint;
                float4 _AtmoTwinkle;
                float4 _AtmoMotion;
                float4 _AtmoFade;
                float4 _AtmoLight;
                float4 _AtmoPx;
            CBUFFER_END

            // Globals written by AtmosphereSystem (WP-073 B5).
            float4 _DD_AtmoGuard;                               // xyz player chest (world), w radius (0 = off: first person)
            float _DD_AtmoShaftCount;                           // active shaft cards this tier (≤ 20)
            float4 _DD_AtmoShaftA[DD_ATMO_MAX_SHAFTS];          // xyz card top (world), w radius (m)
            float4 _DD_AtmoShaftB[DD_ATMO_MAX_SHAFTS];          // x length (m), y peak weight, z lit floor (_MinLit), w unused

            static const float3 kMist = float3(0.6308, 0.6795, 0.7305);   // #D0D7DE, linear

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 texcoord0 : TEXCOORD0;   // uv.xy, centre.xy
                float4 texcoord1 : TEXCOORD1;   // centre.z, stable random, age 0..1, rotation (rad)
                float3 texcoord2 : TEXCOORD2;   // size xyz (z = atlas frame)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;       // rgb lit (HDR), a pre-cap alpha
                float4 screenPos : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float face : TEXCOORD4;         // 1 front, lower on the back of a tumbling petal
            };

            float SunShadow(float3 positionWS)
            {
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                float4 coord = TransformWorldToShadowCoord(positionWS);
                float s = MainLightRealtimeShadow(coord);
                return lerp(s, 1.0, GetMainLightShadowFade(positionWS));
            #else
                return 1.0;
            #endif
            }

            // 0..1: how deep the point sits inside one of S-05's shaft cards (card top, light travel axis, radius, length).
            float ShaftMask(float3 p, float3 axis, out float litFloor)
            {
                float m = 0.0;
                litFloor = 0.0;
                int n = min((int)_DD_AtmoShaftCount, DD_ATMO_MAX_SHAFTS);
                [loop]
                for (int i = 0; i < n; i++)
                {
                    float4 a = _DD_AtmoShaftA[i];
                    float4 b = _DD_AtmoShaftB[i];
                    float3 d = p - a.xyz;
                    float t = dot(d, axis);
                    float radial = length(d - axis * t);
                    float inside = step(0.0, t) * step(t, b.x);
                    float r = saturate(1.0 - radial / max(a.w, 0.05));
                    float along = saturate(t / max(b.x, 0.01));
                    float mi = inside * r * r * (1.0 - 0.6 * along) * b.y;
                    if (mi > m) { m = mi; litFloor = b.z; }
                }
                return m;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 centreWS = TransformObjectToWorld(float3(input.texcoord0.zw, input.texcoord1.x));
                float rnd = input.texcoord1.y;
                float rot = input.texcoord1.w;
                float3 offset = posWS - centreWS;

                float3 camPos = GetCameraPositionWS();
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float t = _Time.y;

                // Sway (petals): the whole card drifts sideways on a slow sine, per-grain phase.
                if (_AtmoMotion.z > 0.0)
                    centreWS += right * (sin(6.2831853 * (_AtmoMotion.y * t + rnd)) * _AtmoMotion.z);

                // Tumble: squash along the card's own in-plane axis by |cos φ|; the back face is darker.
                o.face = 1.0;
                if (_AtmoMotion.x > 0.0)
                {
                    float phi = 6.2831853 * (_AtmoMotion.x * (0.6 + 0.8 * rnd) * t + rnd * 3.7);
                    float c = cos(phi);
                    float3 axis = cos(rot) * right + sin(rot) * up;
                    offset -= axis * dot(offset, axis) * (1.0 - max(abs(c), 0.08));
                    o.face = c;
                }

                // Screen-size clamp [min px, max px]; an enlarged grain keeps its energy by losing alpha.
                float viewZ = max(-TransformWorldToView(centreWS).z, 0.02);
                // |P11|: the projection's y scale is negative when the target is flipped (Metal / render textures) — a signed
                // value made every grain's pixel size negative, the clamp's floor then enlarged it 15 000× (a full-screen quad).
                float pxPerMetre = _ScreenParams.y * 0.5 * abs(UNITY_MATRIX_P._m11) / viewZ;
                float diameterPx = max(length(offset) * 1.4142 * pxPerMetre, 1e-4);
                float clamped = clamp(diameterPx, _AtmoPx.x, _AtmoPx.y);
                float k = clamped / max(diameterPx, 1e-4);
                offset *= k;
                float sizeAlpha = k > 1.0 ? max(1.0 / k, 0.35) : 1.0;

                posWS = centreWS + offset;
                o.positionCS = TransformWorldToHClip(posWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);

                // Atlas frame (4 × 2) from size.z; drips run along u on a stretched billboard.
                float2 uv = input.texcoord0.xy;
                if (_AtmoMotion.w > 0.5) uv = uv.yx;
                float frame = floor(input.texcoord2.z + 0.5);
                float2 cell = float2(fmod(frame, 4.0), floor(frame / 4.0));
                o.uv = (cell + uv) * float2(0.25, 0.5);

                // Sun where the grain actually is: cloud cookie × realtime shadow.
                float3 L = normalize(_DD_ShaftDir.xyz + float3(0.0, -1e-4, 0.0));   // direction the sunlight travels
                float cookie = DD_CloudSunLit01(centreWS);
                float litFloor;
                float shaft = ShaftMask(centreWS, L, litFloor);
                // DEC-102: while the volumetric field is live (Cinematic / High) the shafts are the field itself — its density
                // over its peak × the cloud cookie — and the cards stay a Performance-only read (no cards there anyway).
                if (_DD_VL_Density.w > 0.5)
                {
                    shaft = DD_ShaftMask(centreWS);
                    litFloor = 0.0;
                }
                float sunVis = max(cookie, shaft * litFloor) * SunShadow(centreWS);

                float3 V = normalize(centreWS - camPos);
                float cosT = dot(V, -L);
                float forward = 4.0 * pow(saturate(cosT), 8.0) * (0.5 + 0.5 * saturate(_DD_ShaftDir.w));
                float broad = 0.6 * pow(saturate(0.5 + 0.5 * cosT), 4.0);
                float scatter = (forward + broad) * sunVis * (1.0 + 1.5 * shaft) * _AtmoGlint;

                float hz = lerp(_AtmoTwinkle.x, _AtmoTwinkle.y, rnd);
                float tw = 0.5 + 0.5 * sin(6.2831853 * (hz * t + rnd * 7.13));
                float sparkle = pow(tw, 6.0) * _AtmoTwinkle.z * sunVis * (0.4 + 0.6 * saturate(shaft * 2.0 + cookie * 0.5)) * _AtmoGlint;

                float3 tone = _MainLightColor.rgb / max(max(_MainLightColor.r, max(_MainLightColor.g, _MainLightColor.b)), 1e-3);
                float base = lerp(_AtmoLight.x, _AtmoLight.y, sunVis);
                float3 albedo = lerp(lerp(input.color.rgb, kMist, 0.35), input.color.rgb, sunVis);
                float3 rgb = albedo * (base + scatter + sparkle) + tone * (scatter + sparkle) * 0.35;
                o.color.rgb = min(rgb, _AtmoLight.z);

                // Alpha: authored × shade floor (grains fade in the shade, steam shows where the sun warms the wet stone)
                // × near / far fade × player guard × size energy.
                float dist = length(centreWS - camPos);
                float nearFade = saturate((dist - _AtmoFade.x) / max(_AtmoFade.y - _AtmoFade.x, 0.01));
                float farFade = saturate((_AtmoFade.w - dist) / 10.0);
                float shadeAlpha = lerp(_AtmoTwinkle.w, 1.0, sunVis) * lerp(_AtmoLight.w, 1.0, cookie);

                float guard = 1.0;
                if (_DD_AtmoGuard.w > 0.0)
                {
                    float3 seg = _DD_AtmoGuard.xyz - camPos;
                    float s = saturate(dot(centreWS - camPos, seg) / max(dot(seg, seg), 1e-4));
                    float d = length(centreWS - (camPos + seg * s));
                    float r = _DD_AtmoGuard.w * (0.35 + 0.65 * s);
                    guard = lerp(0.2, 1.0, smoothstep(r * 0.6, r, d));
                }

                o.color.a = input.color.a * nearFade * farFade * shadeAlpha * guard * sizeAlpha;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float face = lerp(0.72, 1.0, saturate(i.face * 0.5 + 0.5));
                float3 rgb = tex.rgb * i.color.rgb * face;
                float a = tex.a * i.color.a;

            #if defined(_DD_ATMO_SOFT)
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                a *= saturate((sceneEye - i.screenPos.w) / max(_AtmoFade.z, 0.01));
            #endif

                rgb = MixFog(rgb, i.fogFactor);
                return float4(rgb, min(a, _AtmoAlpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
