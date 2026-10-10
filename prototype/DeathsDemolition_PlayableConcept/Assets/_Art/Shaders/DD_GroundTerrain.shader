// WP-092 B5 · DD/GroundTerrain — the V6 world's ground (WorldV5Terrain): one opaque pass, SRP Batcher compatible, no keywords of
// its own. Layers in world XZ (no UVs): moss (two sets at two scales and angles, no visible tile at the TD height), dark soil, dirt,
// gravel, river pebbles and mud, composed by two world masks (_MaskA R gravel, G dirt, B mud / rut, A paved — ≥ 0.75 under a paving
// mesh, below that its contact halo; _MaskB R moss, G litter, B wet, A pebble) and value-noise fields (moss / soil patches, tone,
// puddles, leaf flecks). Layer edges are height-blended (texture luminance as height: gravel peeks into moss, moss creeps onto the
// path). The path beds and ruts read sunk through a bank normal taken from _MaskA's gradient; the mesh carries the micro-relief
// (normals), colour R its hollow depth (puddles gather there) and G the contact occlusion of walls, trunks and buildings.
// Look B (AP-018 / DEC-101/102): damp everywhere, wet by the ponds, drip lines, ruts and hollows; standing water near-mirror for the
// probes, the sun's glint held under a soft knee (DD_GroundLighting.hlsl). Realtime lit + ambient (not lightmapped, off the baked
// probes: AD 07.10 M1); DepthOnly / DepthNormals for the SSAO; casts no shadow.
// The 3D read at the TD scale (0.7 px/cm) comes from what the lighting can show: the mesh relief, a tussock / clod normal field
// (analytic value-noise derivatives: moss cushions 0.45 m, soil clods 0.22 m), the layers' own normal maps, the bank of every path
// bed, and a cavity term from the layer heights (the lows of moss, soil and gravel darken the sun too, not only the ambient).
// LOOK-BIBLE principle 1: the open ground stays ≥ 8 L* under the S-07 grass (dark humus, dark olive moss); grass and plants
// themselves are WP-093's instanced system (the masks this ground exports).
Shader "DD/GroundTerrain"
{
    Properties
    {
        _MaskA("Mask A (R gravel, G dirt, B mud / rut, A paved; world)", 2D) = "black" {}
        _MaskB("Mask B (R moss, G litter, B wet, A pebble; world)", 2D) = "black" {}
        _MaskBounds("Mask bounds (xMin, zMin, size x, size z)", Vector) = (-85, -85, 170, 170)
        _MaskTexel("Mask A texel (m)", Float) = 0.083
        _MossMap("Moss BaseColor (cushion)", 2D) = "gray" {}
        [Normal] _MossNormal("Moss Normal", 2D) = "bump" {}
        _Moss2Map("Moss 2 BaseColor (patchy, on soil)", 2D) = "gray" {}
        _SoilMap("Soil BaseColor", 2D) = "gray" {}
        [Normal] _SoilNormal("Soil Normal", 2D) = "bump" {}
        _DirtMap("Dirt path BaseColor", 2D) = "gray" {}
        _GravelMap("Gravel BaseColor", 2D) = "gray" {}
        [Normal] _GravelNormal("Gravel Normal", 2D) = "bump" {}
        _PebbleMap("Pebble BaseColor", 2D) = "gray" {}
        [Normal] _PebbleNormal("Pebble Normal", 2D) = "bump" {}

        [Header(Tiles (m))]
        _MossTile("Moss", Range(0.5, 8)) = 3.4
        _Moss2Tile("Moss 2", Range(0.5, 8)) = 4.3
        _SoilTile("Soil", Range(0.5, 8)) = 3.1
        _DirtTile("Dirt", Range(0.5, 8)) = 3.7
        _GravelTile("Gravel (the pebble set, small)", Range(0.2, 4)) = 0.42
        _PebbleTile("Pebble", Range(0.3, 4)) = 1.1

        // R3 10.10 (user: "too much brown and yellow on the ground"; AD v1.2): clean greens, neutral / cool soil and dirt, less of
        // them (moss cover 0.12 → 0.30), litter cooled and halved; moss saturation 0.82 → 0.90 (AD MUST-1 ceiling). The builder
        // copies these defaults into V6_GroundTerrain.mat (WorldV5Terrain.BuildMaterial): the shader's defaults are the tuning.
        // w4 (measured on the w3 TD frames, AD K2 "median hue 92–110°, P95 s ≤ 0.50"): the lit moss still read 63–67° (olive) — moss
        // tints red × 0.60, overall × 1.05 (luminance kept), blue × 1.04 (the look's warm filter is back), saturation 0.90 → 0.72;
        // modelled on the w3 pixels: ground median ≈ 96°, lit ≈ 94°, P95 s ≈ 0.47.
        // w5 (w4 frames: ground median 94–109°, yet 16 % of the bare moss pixels in the yellow-brown band): red ≤ 0.45 × green (linear,
        // ≈ sRGB 0.70), saturation 0.72 → 0.65; value × 1.2 (C-00 10.10, S-06 AD N2: the dark hero costume — linear Y ≈ 0.009 —
        // melts into a shaded moss ring ≈ 0.03; solved on the ground side): cushion 4.0 → 4.8, patch 1.9 → 2.28.
        [Header(Grade (linear))]
        _MossValue("Moss value (cushion set)", Range(0.5, 8)) = 4.8
        _Moss2Value("Moss 2 value (patch set)", Range(0.5, 8)) = 2.28
        _MossSaturation("Moss saturation", Range(0, 1.5)) = 0.65
        _MossYellowClamp("Moss yellow clamp: linear red <= green x this (0 = off)", Range(0, 1)) = 0.45
        // Tints and albedos are linear multipliers (Vector: a Color property would be sRGB-decoded in this Linear project).
        _MossTintShade("Moss tint, shade end of the tone field (linear)", Vector) = (0.52, 1.05, 1.07, 1)
        _MossTintSun("Moss tint, sun end of the tone field (linear)", Vector) = (0.58, 1.11, 0.90, 1)
        _SoilValue("Soil value", Range(0.1, 2)) = 0.42
        _SoilTint("Soil tint (linear)", Vector) = (0.88, 0.90, 0.95, 1)
        _DirtValue("Dirt value", Range(0.1, 2)) = 0.58
        _DirtTint("Dirt tint (linear)", Vector) = (0.90, 0.92, 0.95, 1)
        _GravelValue("Gravel value", Range(0.1, 2)) = 0.5
        _GravelSaturation("Gravel / pebble saturation (garden grey)", Range(0, 1)) = 0.35
        _GravelTint("Gravel tint (linear; cool slate)", Vector) = (0.95, 1.0, 1.06, 1)
        _PebbleValue("Pebble value", Range(0.1, 2)) = 0.42
        _MudTint("Mud albedo (linear)", Vector) = (0.022, 0.021, 0.020, 1)
        _PavedUnder("Under-paving bed (linear × the gravel; seen only at a piece's edge, or before the kit is laid)", Vector) = (0.42, 0.42, 0.42, 1)
        _LitterFresh("Litter momiji (LOOK-BIBLE #6E4632, linear)", Vector) = (0.155, 0.061, 0.031, 1)
        _LitterDry("Litter momiji dry (#86603E, linear)", Vector) = (0.238, 0.117, 0.048, 1)
        _LitterOld("Litter old (cooled, AD v1.2: was #6A5A44; linear)", Vector) = (0.102, 0.090, 0.072, 1)
        _LitterBamboo("Litter bamboo (dry culm leaf, linear)", Vector) = (0.20, 0.17, 0.10, 1)

        [Header(Fields)]
        _ToneScale("Tone field (m)", Range(4, 40)) = 11.0
        _ToneValue("Tone value swing", Range(0, 0.4)) = 0.12
        _PatchScale("Moss / soil patch field (m)", Range(1, 20)) = 4.6
        _MossCover("Moss cover of the open ground", Range(-1, 1)) = 0.30
        _HeightBlend("Height-blend strength", Range(0, 2)) = 0.9
        _LeafScale("Leaf cell (m; a leaf ≈ 0.85 × 0.5 of it)", Range(0.03, 0.4)) = 0.09
        _LeafCover("Leaf cells taken at litter 1", Range(0, 1)) = 0.30
        _LeafMute("Leaf colour mute (toward its own luma)", Range(0, 1)) = 0.5

        [Header(Relief)]
        _NormalStrength("Texture normal strength", Range(0, 2)) = 1.25
        _BankDepth("Path bank depth (m, normal only)", Range(0, 0.12)) = 0.07
        _HollowAO("Hollow darkening", Range(0, 0.5)) = 0.18
        _TussockScale("Moss cushion size (m)", Range(0.1, 2)) = 0.45
        _TussockSlope("Moss cushion slope (normal)", Range(0, 1.5)) = 0.7
        _ClodScale("Soil clod size (m)", Range(0.05, 1)) = 0.22
        _ClodSlope("Soil clod slope (normal)", Range(0, 1.5)) = 0.45
        _Cavity("Cavity darkening from the layer heights (direct + ambient)", Range(0, 1)) = 0.55

        [Header(Wet Look B)]
        _Wetness("Base dampness", Range(0, 1)) = 0.3
        _WetMoss("Wet albedo: moss", Range(0.3, 1)) = 0.86
        _WetSoil("Wet albedo: soil / dirt", Range(0.3, 1)) = 0.62
        _WetGravel("Wet albedo: gravel / pebble", Range(0.3, 1)) = 0.72
        _SmoothMoss("Smoothness moss dry / wet", Vector) = (0.10, 0.30, 0, 0)
        _SmoothSoil("Smoothness soil dry / wet", Vector) = (0.14, 0.52, 0, 0)
        _SmoothGravel("Smoothness gravel dry / wet", Vector) = (0.20, 0.58, 0, 0)
        _PuddleThreshold("Puddle threshold", Range(0, 1.5)) = 0.58
        _PuddleRamp("Puddle ramp", Range(0.02, 0.5)) = 0.1
        _PuddleScale("Puddle field (m)", Range(0.5, 10)) = 2.4
        _PuddleDip("Puddle weight of the mesh hollows", Range(0, 1.5)) = 0.55
        _PuddleSmoothness("Puddle smoothness (probes)", Range(0, 1)) = 0.92
        _PuddleAlbedo("Puddle albedo scale", Range(0.05, 1)) = 0.3
        _PuddleSky("Puddle sky (probe) reflection boost", Range(0, 1)) = 0.22
        _DirectSmoothnessCap("Smoothness the sun / lights see (the sun disc's roughness floor)", Range(0, 1)) = 0.85
        _DirectSpecMax("Direct specular cap (the glint stops at this × the light)", Range(0.05, 4)) = 0.25
        _EnvSpecMax("Probe specular cap (the sun-side sky the puddles and ruts mirror)", Range(0.1, 16)) = 1.2
        [Enum(Off, 0, Layers, 1, Albedo, 2, Normal, 3)] _DebugView("Debug view (look work only; 0 in the game)", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MaskBounds;
            float _MaskTexel;
            half _MossTile, _Moss2Tile, _SoilTile, _DirtTile, _GravelTile, _PebbleTile;
            half _MossValue, _Moss2Value, _MossSaturation, _MossYellowClamp;
            half4 _MossTintShade, _MossTintSun;
            half _SoilValue; half4 _SoilTint;
            half _DirtValue; half4 _DirtTint;
            half _GravelValue, _GravelSaturation; half4 _GravelTint;
            half _PebbleValue;
            half4 _MudTint, _PavedUnder, _LitterFresh, _LitterDry, _LitterOld, _LitterBamboo;
            half _ToneScale, _ToneValue, _PatchScale, _MossCover, _HeightBlend, _LeafScale, _LeafCover, _LeafMute;
            half _NormalStrength, _BankDepth, _HollowAO, _TussockScale, _TussockSlope, _ClodScale, _ClodSlope, _Cavity;
            half _Wetness, _WetMoss, _WetSoil, _WetGravel;
            half4 _SmoothMoss, _SmoothSoil, _SmoothGravel;
            half _PuddleThreshold, _PuddleRamp, _PuddleScale, _PuddleDip, _PuddleSmoothness, _PuddleAlbedo, _PuddleSky;
            half _DirectSmoothnessCap, _DirectSpecMax, _EnvSpecMax, _DebugView;
            half _Cull;
        CBUFFER_END

        // Every layer set shares one repeat sampler (the moss map's: repeat, trilinear, aniso); the masks share the clamped one.
        TEXTURE2D(_MaskA);      SAMPLER(sampler_MaskA);
        TEXTURE2D(_MaskB);
        TEXTURE2D(_MossMap);    SAMPLER(sampler_MossMap);
        TEXTURE2D(_MossNormal);
        TEXTURE2D(_Moss2Map);
        TEXTURE2D(_SoilMap);
        TEXTURE2D(_SoilNormal);
        TEXTURE2D(_DirtMap);
        TEXTURE2D(_GravelMap);
        TEXTURE2D(_GravelNormal);
        TEXTURE2D(_PebbleMap);
        TEXTURE2D(_PebbleNormal);

        float DDTerrainHash(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float DDTerrainNoise(float2 x)
        {
            float2 i = floor(x), f = frac(x), u = f * f * (3.0 - 2.0 * f);
            float a = DDTerrainHash(i), b = DDTerrainHash(i + float2(1, 0)), c = DDTerrainHash(i + float2(0, 1)), d = DDTerrainHash(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        // Value noise with its analytic gradient (xy: d/dx, d/dz of the noise at unit frequency; z: value).
        float3 DDTerrainNoiseD(float2 x)
        {
            float2 i = floor(x), f = frac(x), u = f * f * (3.0 - 2.0 * f), du = 6.0 * f * (1.0 - f);
            float a = DDTerrainHash(i), b = DDTerrainHash(i + float2(1, 0)), c = DDTerrainHash(i + float2(0, 1)), d = DDTerrainHash(i + float2(1, 1));
            float k1 = b - a, k2 = c - a, k3 = a - b - c + d;
            return float3(du * float2(k1 + k3 * u.y, k2 + k3 * u.x), a + k1 * u.x + k2 * u.y + k3 * u.x * u.y);
        }

        // A bump field's tilt (unitless, the gradient of a field whose amplitude is proportional to its feature size, so the
        // tilt reads the same at every scale): two octaves at scale and 0.43 × it, mean |tilt| ≈ 0.4.
        float2 DDBumpTilt(float2 xz, float scale, float2 offset)
        {
            float3 a = DDTerrainNoiseD(xz / scale + offset);
            float3 b = DDTerrainNoiseD(xz / (scale * 0.43) + offset * 1.3 + float2(5.1, 2.7));
            return a.xy * 0.7 + b.xy * 0.3;
        }

        // Two octaves, 0…1 (mean 0.5).
        float DDTerrainField(float2 xz, float scale, float2 offset)
        {
            return 0.65 * DDTerrainNoise(xz / scale + offset) + 0.35 * DDTerrainNoise(xz / (scale * 0.37) + offset * 1.7 + float2(11.2, 3.9));
        }

        half DDLuma(half3 c) { return dot(c, half3(0.2126h, 0.7152h, 0.0722h)); }

        // Texture luminance as a blend height, 0…1 about the layer's mean.
        half DDHeight(half3 c, half mean) { return saturate(0.5h + (DDLuma(c) / mean - 1.0h) * 0.8h); }

        // The top layer's weight with its edge broken by the two heights (only inside the mask's transition).
        half DDHeightBlend(half a, half hTop, half hBase, half strength)
        {
            half t = a + (hTop - hBase) * strength * saturate(1.0h - abs(2.0h * a - 1.0h));
            return smoothstep(0.32h, 0.68h, t);
        }

        float2 DDRotate(float2 p, float s, float c) { return float2(c * p.x - s * p.y, s * p.x + c * p.y); }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TerrainVertex
            #pragma fragment TerrainFragment

            // URP's own lighting set; the ground is never lightmapped (no LIGHTMAP_ON / shadowmask / dirlightmap variants).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES   // WP-075 C2: the cloud shadow (main-light cookie)
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
            #include "DD_GroundLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;   // R hollow depth, G contact occlusion
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half2  vertexData : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                half3  vertexSH   : TEXCOORD4;
                float4 positionCS : SV_POSITION;
            };

            Varyings TerrainVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.vertexData = input.color.rg;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                output.vertexSH = SampleSHVertex(output.normalWS);
                return output;
            }

            half4 TerrainFragment(Varyings input) : SV_Target
            {
                float2 xz = input.positionWS.xz;
                float2 muv = (xz - _MaskBounds.xy) / _MaskBounds.zw;
                half4 A = SAMPLE_TEXTURE2D(_MaskA, sampler_MaskA, muv);
                half4 B = SAMPLE_TEXTURE2D(_MaskB, sampler_MaskA, muv);
                half hollow = input.vertexData.x, contact = input.vertexData.y;

                // ---- the bank normal: path beds, ruts and mud sit lower (mask gradient, 1.5 texels)
                float du = 1.5 * _MaskTexel / _MaskBounds.z;
                half4 Ax = SAMPLE_TEXTURE2D(_MaskA, sampler_MaskA, muv + float2(du, 0));
                half4 Az = SAMPLE_TEXTURE2D(_MaskA, sampler_MaskA, muv + float2(0, du));
                half3 bed = half3(1.0h, 0.8h, 0.7h);
                half hC = -dot(A.rgb, bed), hX = -dot(Ax.rgb, bed), hZ = -dot(Az.rgb, bed);
                half slope = _BankDepth / (1.5h * _MaskTexel);
                half3 bankTS = half3(-(hX - hC) * slope, -(hZ - hC) * slope, 1.0h);

                // ---- fields
                half tone = DDTerrainField(xz, _ToneScale, float2(3.1, 7.7));
                half patch = DDTerrainField(xz, _PatchScale, float2(7.9, 23.6));
                half mix2 = DDTerrainField(xz, 6.3, float2(19.4, 1.3));
                half grain = DDTerrainNoise(xz * 1.7 + 2.7);

                // ---- layer samples (two moss sets at two scales / angles; dirt turned 31°)
                float2 uvMoss = xz / _MossTile;
                float2 uvMoss2 = DDRotate(xz, 0.5446, 0.8387) / _Moss2Tile;
                float2 uvSoil = xz / _SoilTile;
                float2 uvDirt = DDRotate(xz, -0.5150, 0.8572) / _DirtTile;
                float2 uvGravel = xz / _GravelTile;
                float2 uvPebble = DDRotate(xz, 0.2588, 0.9659) / _PebbleTile;

                // The cushion set repeats on a visible grid: a second read turned 37° at 1.37× breaks it in noise-shaped patches.
                float2 uvMossB = DDRotate(xz, 0.6018, 0.7986) / (_MossTile * 1.37) + float2(0.31, 0.77);
                half bomb = smoothstep(0.40h, 0.60h, DDTerrainField(xz, 2.9, float2(9.7, 2.1)));
                half3 m1 = lerp(SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, uvMoss).rgb, SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, uvMossB).rgb, bomb) * _MossValue;
                half3 m2 = SAMPLE_TEXTURE2D(_Moss2Map, sampler_MossMap, uvMoss2).rgb * _Moss2Value;
                half mossMix = smoothstep(0.35h, 0.65h, mix2);
                half3 moss = lerp(m1, m2, mossMix);
                moss = lerp(DDLuma(moss).xxx, moss, _MossSaturation) * lerp(_MossTintShade.rgb, _MossTintSun.rgb, tone);
                if (_MossYellowClamp > 0.0h) moss.r = min(moss.r, moss.g * _MossYellowClamp);
                half3 soil = SAMPLE_TEXTURE2D(_SoilMap, sampler_MossMap, uvSoil).rgb * _SoilValue * _SoilTint.rgb;
                half3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_MossMap, uvDirt).rgb * _DirtValue * _DirtTint.rgb;
                half3 gravel = SAMPLE_TEXTURE2D(_GravelMap, sampler_MossMap, uvGravel).rgb * _GravelValue;
                half3 pebble = SAMPLE_TEXTURE2D(_PebbleMap, sampler_MossMap, uvPebble).rgb * _PebbleValue;
                gravel = lerp(DDLuma(gravel).xxx, gravel, _GravelSaturation) * _GravelTint.rgb;
                pebble = lerp(DDLuma(pebble).xxx, pebble, _GravelSaturation) * _GravelTint.rgb;
                half toneValue = 1.0h + (tone - 0.5h) * 2.0h * _ToneValue;
                soil *= toneValue;
                moss *= toneValue;

                half hMoss = DDHeight(moss, 0.055h), hSoil = DDHeight(soil, 0.037h), hDirt = DDHeight(dirt, 0.085h);
                half hGravel = DDHeight(gravel, 0.18h), hPebble = DDHeight(pebble, 0.18h);

                // ---- composition: the open ground (moss over dark soil), dirt, gravel / pebble, mud, the paved underlay
                half mossW = saturate(_MossCover + 0.5h + (patch - 0.5h) * 2.4h + B.r * 1.1h - B.g * 0.35h - A.g * 0.9h);
                mossW = DDHeightBlend(mossW, hMoss, hSoil, _HeightBlend);
                half3 albedo = lerp(soil, moss, mossW);
                half baseH = lerp(hSoil, hMoss, mossW);

                half dirtW = DDHeightBlend(A.g, hDirt, baseH, _HeightBlend);
                albedo = lerp(albedo, dirt, dirtW);
                baseH = lerp(baseH, hDirt, dirtW);

                half3 stones = lerp(gravel, pebble, B.a);
                half stonesH = lerp(hGravel, hPebble, B.a);
                half gravelW = DDHeightBlend(A.r, stonesH, baseH, _HeightBlend * 1.2h);
                albedo = lerp(albedo, stones, gravelW);

                half layerH = lerp(baseH, stonesH, gravelW);
                half mudW = saturate(A.b * 1.4h - 0.15h) * (1.0h - gravelW * 0.6h);
                albedo = lerp(albedo, _MudTint.rgb * (0.8h + 0.4h * grain), mudW * 0.85h);

                half pavedCore = smoothstep(0.7h, 0.8h, A.a);
                half halo = saturate(A.a * 2.0h) * (1.0h - pavedCore);
                albedo = lerp(albedo, gravel * _PavedUnder.rgb, pavedCore);

                // ---- leaf litter where B.g says (old, flat — the fresh momiji are S-07's cards): one elliptical leaf per taken cell of a
                // jittered grid (≈ 7.5 × 4.5 cm, any heading), the 2 × 2 cells that can reach this point; bamboo leaf west of the grove line.
                half leafCover = B.g * _LeafCover * (1.0h - gravelW * 0.8h) * (1.0h - pavedCore);
                half leaf = 0.0h;
                half3 leafColour = _LitterOld.rgb;
                float2 lc = xz / _LeafScale;
                float aa = max(fwidth(lc.x), fwidth(lc.y)) * 2.5;   // in the leaf's own (minor-axis) units: crisp near, soft specks at TD (outside the branch: no gradient in flow control)
                [branch] if (leafCover > 0.01h)
                {
                    float2 corner = floor(lc) + step(0.5, frac(lc)) - 1.0;
                    [unroll] for (int j = 0; j < 2; j++)
                    [unroll] for (int i = 0; i < 2; i++)
                    {
                        float2 c = corner + float2(i, j);
                        float h1 = DDTerrainHash(c), h2 = DDTerrainHash(c + 17.3), h3 = DDTerrainHash(c + 41.9);
                        float2 d = lc - (c + 0.25 + 0.5 * float2(h2, h3));
                        float sn, cs;
                        sincos(h1 * 47.0, sn, cs);
                        d = float2(cs * d.x - sn * d.y, sn * d.x + cs * d.y) / float2(0.42, 0.25);
                        half m = (half)(1.0 - smoothstep(1.0 - aa, 1.0 + aa, length(d))) * step(h1, leafCover);
                        [flatten] if (m > leaf)
                        {
                            leaf = m;
                            leafColour = h2 < 0.4 ? _LitterFresh.rgb : (h2 < 0.75 ? _LitterDry.rgb : _LitterOld.rgb);
                        }
                    }
                }

                leafColour = lerp(leafColour, _LitterBamboo.rgb, saturate((-37.0h - (half)xz.x) * 0.5h));
                leafColour = lerp(DDLuma(leafColour).xxx, leafColour, 1.0h - _LeafMute);
                albedo = lerp(albedo, leafColour * (0.85h + 0.3h * grain), leaf);

                // ---- wetness and puddles
                half wet = saturate(_Wetness + B.b * 0.75h + A.b * 0.9h + hollow * 0.45h + dirtW * 0.2h);
                half puddleNoise = DDTerrainField(xz, _PuddleScale, float2(5.3, 9.1));
                half score = (A.b * 0.8h + hollow * _PuddleDip + B.b * 0.15h) * lerp(0.55h, 1.35h, puddleNoise);
                half puddle = smoothstep(_PuddleThreshold, _PuddleThreshold + _PuddleRamp, score) * (1.0h - pavedCore) * (1.0h - gravelW * 0.5h);
                wet = max(wet, smoothstep(_PuddleThreshold - 0.25h, _PuddleThreshold, score));

                half wetAlbedo = lerp(lerp(_WetSoil, _WetMoss, mossW * (1.0h - dirtW)), _WetGravel, gravelW);
                albedo *= lerp(1.0h, wetAlbedo, wet);
                albedo *= lerp(1.0h, _PuddleAlbedo, puddle);
                albedo *= 1.0h - _HollowAO * hollow;
                // Cavity: the low parts of each layer (moss gaps, soil cracks, gravel interstices) — dry ground only (water fills them).
                half cavity = lerp(1.0h, saturate(0.25h + layerH * 1.2h), _Cavity * (1.0h - puddle) * (1.0h - mudW * 0.7h));
                albedo *= lerp(1.0h, cavity, 0.65h);

                half2 sm = lerp(lerp(_SmoothSoil.xy, _SmoothMoss.xy, mossW * (1.0h - dirtW)), _SmoothGravel.xy, gravelW);
                half smoothness = lerp(sm.x, sm.y, wet);
                smoothness = lerp(smoothness, max(smoothness, 0.6h), mudW);
                smoothness = lerp(smoothness, _PuddleSmoothness, puddle);
                smoothness *= 1.0h - leaf * 0.5h;

                // ---- normals: the layers' tangent-space maps (world X / Z frame), the bank, flattened by water
                half3 nMoss = UnpackNormalScale(lerp(SAMPLE_TEXTURE2D(_MossNormal, sampler_MossMap, uvMoss), SAMPLE_TEXTURE2D(_MossNormal, sampler_MossMap, uvMossB), bomb), _NormalStrength);
                half3 nSoil = UnpackNormalScale(SAMPLE_TEXTURE2D(_SoilNormal, sampler_MossMap, uvSoil), _NormalStrength * 0.8h);
                half3 nGravel = UnpackNormalScale(SAMPLE_TEXTURE2D(_GravelNormal, sampler_MossMap, uvGravel), _NormalStrength);
                half3 nPebble = UnpackNormalScale(SAMPLE_TEXTURE2D(_PebbleNormal, sampler_MossMap, uvPebble), _NormalStrength);
                half3 nTS = lerp(nSoil, nMoss, mossW);
                nTS = lerp(nTS, lerp(nGravel, nPebble, B.a), gravelW);
                // Tussocks on the moss, clods on the bare soil (not on paths, water or paving): the relief the 0.5 m mesh cannot carry.
                half open = (1.0h - gravelW) * (1.0h - dirtW * 0.75h) * (1.0h - pavedCore) * (1.0h - puddle);
                float2 tussock = DDBumpTilt(xz, _TussockScale, float2(13.7, 4.2)) * (_TussockSlope * mossW);
                float2 clod = DDBumpTilt(xz, _ClodScale, float2(2.3, 17.9)) * (_ClodSlope * (1.0h - mossW));
                half2 bump = (half2)(-(tussock + clod)) * open;
                nTS = normalize(half3(nTS.xy + bankTS.xy + bump, nTS.z));
                nTS = normalize(lerp(nTS, half3(0, 0, 1), saturate(wet * 0.3h + puddle + mudW * 0.4h)));

                half3 n = NormalizeNormalPerPixel(input.normalWS);
                half3 t = normalize(half3(1, 0, 0) - n * n.x);
                half3 b = cross(t, n);
                half3 normalWS = NormalizeNormalPerPixel(t * nTS.x + b * nTS.y + n * nTS.z);

                if (_DebugView > 0.5h)
                {
                    half3 dbg = _DebugView < 1.5h ? half3(mossW, dirtW + mudW, gravelW + pavedCore) : _DebugView < 2.5h ? albedo * 4.0h : normalWS * 0.5h + 0.5h;
                    return half4(dbg, 1.0h);
                }

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = contact * (1.0h - 0.45h * halo) * cavity;
                surfaceData.normalTS = nTS;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) || defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSHPixel(input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = DDGroundFragmentPBR(inputData, surfaceData, _DirectSmoothnessCap, _DirectSpecMax, _EnvSpecMax);

                // Standing water from the steep TD angle: the probes' sky a little stronger than Fresnel alone gives (no black blots).
                half3 r = reflect(-inputData.viewDirectionWS, normalWS);
                half3 sky = DDKnee(GlossyEnvironmentReflection(r, input.positionWS, 0.08h, 1.0h, inputData.normalizedScreenSpaceUV), _EnvSpecMax);
                color.rgb += sky * puddle * _PuddleSky;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFragment(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // The SSAO reads the relief's geometric normal (world space, like URP Lit's DepthNormals).
            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
