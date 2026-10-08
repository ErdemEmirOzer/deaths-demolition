#ifndef DD_GROUND_LIGHTING_INCLUDED
#define DD_GROUND_LIGHTING_INCLUDED

// WP-092 B5 · The ground's PBR loop: URP 14.0.10 UniversalFragmentPBR (Lighting.hlsl) with two changes for the analytic lights (the
// sun, the lantern pool); the probes / the environment keep the true smoothness.
// 1. Sun-disc roughness floor: the lights see the surface no smoother than directSmoothnessCap (the sun is a 0.5° disc, not a
//    point: a minimum roughness for analytic lights is the standard area-light correction, Karis 2013 "sphere light").
// 2. Highlight knees (DDKnee: linear up to half the cap, then a smooth roll-off that never passes the cap): the direct specular
//    (F0 × GGX, before the light's radiance) under directSpecMax, the environment (probe) specular under envSpecMax. The V6 sun
//    stands 50° up in the NNW and the TD camera looks north 49° down, so every flat wet surface mirrors the sun — and the probe's
//    sky round it — right in front of the player: an uncapped GGX peak (smoothness 0.86–0.92) was in the thousands and the bloom
//    spread it over a quarter of the frame (WP-092 B5 #4, the plaza blot); a low smoothness cap alone only widened it into a haze
//    over half the plaza (r1), and the probe's sun-side sky still lit puddles and ruts like neon (t1). With the knees the glint
//    keeps its tight puddle-sized shape and stops below the bloom threshold.
// Include after Lighting.hlsl. No new keywords; the pass's URP multi_compiles drive it exactly like the stock loop.

// Linear below cap / 2, then rolls off smoothly toward cap (on the brightest channel; hue kept).
half3 DDKnee(half3 c, half cap)
{
    half knee = 0.5h * cap;
    half peak = max(c.r, max(c.g, c.b));
    half excess = max(peak - knee, 0.0h);
    half target = peak - excess + excess / (1.0h + excess / knee);
    return c * (target / max(peak, 1e-4h));
}

half3 DDGroundDirect(BRDFData brdf, Light light, half3 normalWS, half3 viewDirectionWS, half directSpecMax)
{
    half NdotL = saturate(dot(normalWS, light.direction));
    half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation * NdotL);
    half3 spec = DDKnee(brdf.specular * DirectBRDFSpecular(brdf, normalWS, light.direction, viewDirectionWS), directSpecMax);
    return (brdf.diffuse + spec) * radiance;
}

// URP 14.0.10 GlobalIllumination (no clear coat) with the probe's specular under the knee.
half3 DDGroundGI(BRDFData brdfData, half3 bakedGI, half occlusion, float3 positionWS, half3 normalWS, half3 viewDirectionWS,
                 float2 normalizedScreenSpaceUV, half envSpecMax)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);
    half3 indirectSpecular = DDKnee(GlossyEnvironmentReflection(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h, normalizedScreenSpaceUV), envSpecMax);
    half3 color = EnvironmentBRDF(brdfData, bakedGI, indirectSpecular, fresnelTerm);
    if (IsOnlyAOLightingFeatureEnabled()) color = half3(1, 1, 1);
    return color * occlusion;
}

half4 DDGroundFragmentPBR(InputData inputData, SurfaceData surfaceData, half directSmoothnessCap, half directSpecMax, half envSpecMax)
{
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);
    SurfaceData directSurface = surfaceData;
    directSurface.smoothness = min(surfaceData.smoothness, directSmoothnessCap);
    BRDFData brdfDirect;
    InitializeBRDFData(directSurface, brdfDirect);

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = DDGroundGI(brdfData, inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                      inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV, envSpecMax);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = DDGroundDirect(brdfDirect, mainLight, inputData.normalWS, inputData.viewDirectionWS, directSpecMax);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += DDGroundDirect(brdfDirect, light, inputData.normalWS, inputData.viewDirectionWS, directSpecMax);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += DDGroundDirect(brdfDirect, light, inputData.normalWS, inputData.viewDirectionWS, directSpecMax);
        }
    LIGHT_LOOP_END
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
    #endif

#if REAL_IS_HALF
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

#endif // DD_GROUND_LIGHTING_INCLUDED
