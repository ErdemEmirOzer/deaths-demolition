// WP-075 C7b · DD/GroundMoss — the World v5 ground outside the plaza (the 170 m greybox ground box). C-00 note: the kit moss at a
// flat 3 m tile read as a saturated, single-tone "golf green"; the HF frames show dark, wet, patterned moss broken by gravel, soil
// and leaf patches and never bare ground. Everything is in world XZ (no box UVs): the AP-023 moss graded (saturation / value; DEC-101: dark olive wet moss — no lawn — under the overcast v6 light and LUT v2b)
// and toned by a 2-octave field between a dark blue-green and an olive tint (~14 m and ~5 m: a TP view must not read one lawn), soil (ADR-015-1 earth)
// and gravel (AP-023) patches from a medium field (~6 m), leaf-litter flecks, and the path bands along the layout's lanes and
// garden paths (_PathMask: R gravel core, G soil shoulder; WorldV5Ground.BuildPathMask) with a noise wobble so the edges are not
// ruled. Wet throughout (the Islak Arduvaz look): albedo × _WetAlbedo, smoothness to the wet cap (0.22: moss is damp, not a mirror). Lighting path
// and passes are DD/GroundTiles' (realtime + probes; the ground box is not lightmapped).
Shader "DD/GroundMoss"
{
    Properties
    {
        _MossMap("Moss BaseColor", 2D) = "gray" {}
        [Normal] _MossNormal("Moss Normal", 2D) = "bump" {}
        _GravelMap("Gravel BaseColor", 2D) = "gray" {}
        [Normal] _GravelNormal("Gravel Normal", 2D) = "bump" {}
        _EarthMap("Earth BaseColor", 2D) = "gray" {}
        [Normal] _EarthNormal("Earth Normal", 2D) = "bump" {}
        _PathMask("Path mask (R gravel core, G soil shoulder; world)", 2D) = "black" {}
        _PathBounds("Path mask bounds (xMin, zMin, size x, size z)", Vector) = (-85, -85, 170, 170)
        _MossTile("Moss tile (m)", Range(0.5, 8)) = 3.0
        _GravelTile("Gravel tile (m)", Range(0.5, 8)) = 2.0
        _EarthTile("Earth tile (m)", Range(0.5, 8)) = 4.0
        [Header(Moss grade)]
        _MossSaturation("Moss saturation", Range(0, 1.5)) = 0.66
        _MossValue("Moss value scale (linear)", Range(0.2, 4)) = 0.95
        _TintDark("Tone field: dark end (linear)", Color) = (0.62, 0.72, 0.50, 1)
        _TintLight("Tone field: light end (linear)", Color) = (0.95, 1.02, 0.62, 1)
        _ToneScale("Tone field scale (m)", Range(5, 80)) = 14.0
        _ToneContrast("Tone field contrast", Range(0.5, 4)) = 3.0
        [Header(Patches)]
        _PatchScale("Patch field scale (m)", Range(1, 20)) = 6.0
        _SoilCover("Soil patch cover", Range(0, 0.5)) = 0.18
        _GravelCover("Gravel patch cover", Range(0, 0.5)) = 0.05
        _PatchEdge("Patch edge width", Range(0.01, 0.3)) = 0.08
        _EarthTint("Earth tint (linear)", Color) = (0.62, 0.55, 0.47, 1)
        _GravelTint("Gravel tint (linear)", Color) = (1.05, 1.05, 1.05, 1)
        _LeafScale("Leaf fleck scale (m)", Range(0.2, 4)) = 0.9
        _LeafCover("Leaf fleck cover", Range(0, 0.4)) = 0.16
        _LeafTint("Leaf litter albedo (linear)", Color) = (0.20, 0.10, 0.045, 1)
        _PathWobble("Path edge wobble (m)", Range(0, 2)) = 0.6
        [Header(Surface)]
        _NormalStrength("Normal strength", Range(0, 2)) = 1.0
        _MossSmoothness("Moss smoothness", Range(0, 1)) = 0.18
        _GravelSmoothness("Gravel smoothness", Range(0, 1)) = 0.22
        _EarthSmoothness("Earth smoothness", Range(0, 1)) = 0.14
        _Wetness("Wetness", Range(0, 1)) = 1.0
        _WetAlbedo("Wet albedo scale (linear)", Range(0.5, 1)) = 0.82
        _WetSmoothness("Wet smoothness cap (AP-018; AD K4: 0.52 / 0.38 gave the TP view's sunlit moss a grazing sheen over the slate's value)", Range(0, 0.6)) = 0.22
        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _PathBounds;
            half4 _TintDark;
            half4 _TintLight;
            half4 _EarthTint;
            half4 _GravelTint;
            half4 _LeafTint;
            half _MossTile;
            half _GravelTile;
            half _EarthTile;
            half _MossSaturation;
            half _MossValue;
            half _ToneScale;
            half _ToneContrast;
            half _PatchScale;
            half _SoilCover;
            half _GravelCover;
            half _PatchEdge;
            half _LeafScale;
            half _LeafCover;
            half _PathWobble;
            half _NormalStrength;
            half _MossSmoothness;
            half _GravelSmoothness;
            half _EarthSmoothness;
            half _Wetness;
            half _WetAlbedo;
            half _WetSmoothness;
            half _Cull;
        CBUFFER_END

        TEXTURE2D(_MossMap);      SAMPLER(sampler_MossMap);
        TEXTURE2D(_MossNormal);
        TEXTURE2D(_GravelMap);
        TEXTURE2D(_GravelNormal);
        TEXTURE2D(_EarthMap);
        TEXTURE2D(_EarthNormal);
        TEXTURE2D(_PathMask);     SAMPLER(sampler_PathMask);

        // WP-093 · grass cover from the vegetation system (VegetationDrawSet): 0..1 per 0.5 m, world-aligned. Principle 1 of the
        // LOOK-BIBLE (soil under grass ≥ 8 L* darker than the grass) and P-05 08.10 §2 (specular occlusion ≈ ×0.45 under the grass):
        // x albedo ×, y occlusion (indirect diffuse + specular), z smoothness × (direct back-lit specular), w on. Off (w 0) = unchanged.
        TEXTURE2D(_DD_VegCoverTex); SAMPLER(sampler_DD_VegCoverTex);
        float4 _DD_VegCoverRect;
        float4 _DD_VegGround;

        half DDVegGroundCover(float2 xz)
        {
            if (_DD_VegGround.w < 0.5) return 0.0h;
            float2 uv = (xz - _DD_VegCoverRect.xy) * _DD_VegCoverRect.zw;
            if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return 0.0h;
            return (half)SAMPLE_TEXTURE2D_LOD(_DD_VegCoverTex, sampler_DD_VegCoverTex, uv, 0).r;
        }

        float DDMossHash(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float DDMossNoise(float2 x)
        {
            float2 i = floor(x), f = frac(x), u = f * f * (3.0 - 2.0 * f);
            float a = DDMossHash(i), b = DDMossHash(i + float2(1, 0)), c = DDMossHash(i + float2(0, 1)), d = DDMossHash(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        // Two octaves, 0…1 (mean 0.5).
        float DDMossField(float2 xz, float scale, float2 offset)
        {
            return 0.65 * DDMossNoise(xz / scale + offset) + 0.35 * DDMossNoise(xz / (scale * 0.37) + offset * 1.7 + float2(11.2, 3.9));
        }

        half DDCover(float field, half cover, half edge)
        {
            // A field value above (1 − cover) is inside the patch; the 2-octave field's spread is ~0.35, so cover maps loosely to area.
            half t = 1.0h - cover * 0.5h;
            return smoothstep(t - edge, t + edge, field);
        }

        struct DDMossSurface
        {
            half3 albedo;
            half smoothness;
            half3 normalTS;
        };

        DDMossSurface DDMossSample(float2 xz)
        {
            DDMossSurface s;
            float2 uvM = xz / _MossTile, uvG = xz / _GravelTile, uvE = xz / _EarthTile;

            // Moss: graded down, then the large-scale tone field (dark blue-green ↔ olive) and a value swing.
            half3 moss = SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, uvM).rgb;
            half luma = dot(moss, half3(0.2126h, 0.7152h, 0.0722h));
            moss = lerp(luma.xxx, moss, _MossSaturation) * _MossValue;
            half tone = saturate((DDMossField(xz, _ToneScale, float2(3.1, 7.7)) - 0.5h) * _ToneContrast + 0.5h);
            moss *= lerp(_TintDark.rgb, _TintLight.rgb, tone);

            // Patches: soil and gravel from their own medium fields; the path mask (wobbled) adds the bands.
            float2 wobble = (float2(DDMossNoise(xz * 0.35 + 5.3), DDMossNoise(xz * 0.35 + 9.1)) - 0.5) * 2.0 * _PathWobble;
            float2 maskUV = (xz + wobble - _PathBounds.xy) / _PathBounds.zw;
            half2 path = SAMPLE_TEXTURE2D(_PathMask, sampler_PathMask, maskUV).rg;
            half grain = DDMossNoise(xz * 1.3 + 2.7);
            half gravel = max(DDCover(DDMossField(xz, _PatchScale * 0.8, float2(19.4, 1.3)), _GravelCover, _PatchEdge), saturate(path.r * (0.8h + 0.4h * grain)));
            half soil = max(DDCover(DDMossField(xz, _PatchScale, float2(7.9, 23.6)), _SoilCover, _PatchEdge), saturate(path.g * (0.6h + 0.6h * grain)));
            soil *= 1.0h - gravel;

            half3 earth = SAMPLE_TEXTURE2D(_EarthMap, sampler_MossMap, uvE).rgb * _EarthTint.rgb;
            half3 grav = SAMPLE_TEXTURE2D(_GravelMap, sampler_MossMap, uvG).rgb * _GravelTint.rgb;
            half3 albedo = lerp(lerp(moss, earth, soil), grav, gravel);

            // Leaf litter: small flecks on moss and soil (not on the gravel core), denser in the dark tone.
            half leaf = DDCover(DDMossField(xz, _LeafScale, float2(41.3, 17.2)), _LeafCover * (1.4h - tone * 0.8h), 0.05h) * (1.0h - gravel);
            albedo = lerp(albedo, _LeafTint.rgb * (0.8h + 0.4h * grain), leaf * 0.85h);

            half wet = _Wetness;
            s.albedo = albedo * lerp(1.0h, _WetAlbedo, wet);
            half smooth = lerp(lerp(_MossSmoothness, _EarthSmoothness, soil), _GravelSmoothness, gravel);
            s.smoothness = lerp(smooth, max(smooth, _WetSmoothness * lerp(0.7h, 1.0h, gravel)), wet);

            half3 nM = UnpackNormalScale(SAMPLE_TEXTURE2D(_MossNormal, sampler_MossMap, uvM), _NormalStrength);
            half3 nG = UnpackNormalScale(SAMPLE_TEXTURE2D(_GravelNormal, sampler_MossMap, uvG), _NormalStrength);
            half3 nE = UnpackNormalScale(SAMPLE_TEXTURE2D(_EarthNormal, sampler_MossMap, uvE), _NormalStrength);
            s.normalTS = normalize(lerp(lerp(nM, nE, soil), nG, gravel));
            return s;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MossVertex
            #pragma fragment MossFragment

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
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 3);
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings MossVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half4 MossFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                DDMossSurface s = DDMossSample(input.positionWS.xz);
                half vegCover = DDVegGroundCover(input.positionWS.xz);   // WP-093
                s.albedo *= lerp(1.0h, (half)_DD_VegGround.x, vegCover);
                s.smoothness *= lerp(1.0h, (half)_DD_VegGround.z, vegCover);
                // World-XZ texture space: tangent +X, bitangent +Z about the (flat, axis-aligned) ground normal.
                half3 n = NormalizeNormalPerPixel(input.normalWS);
                half3 normalWS = NormalizeNormalPerPixel(half3(1, 0, 0) * s.normalTS.x + half3(0, 0, 1) * s.normalTS.y + n * s.normalTS.z);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = s.albedo;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = s.smoothness;
                surfaceData.occlusion = lerp(1.0h, (half)_DD_VegGround.y, vegCover);
                surfaceData.normalTS = s.normalTS;

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
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target { return 0; }
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
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
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
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
