#ifndef ALPINE_STYLIZED_LIGHTING_INCLUDED
#define ALPINE_STYLIZED_LIGHTING_INCLUDED

// Lighting model of AlpineLib/Stylized/Lit. With every style global at zero it is plain Lambert:
// albedo * (SH ambient + light colour * N.L * shadow), plus the material-driven metal terms.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Share of the ambient reflection a fully metallic surface always gets, before the global adds to it.
#define ALPINE_METAL_REFLECTION_BASE 0.5h
// Ramp softness at or above which the ramp fully replaces the linear response (continuous down to 0).
#define ALPINE_RAMP_BLEND_SCALE 64.0h

struct StylizedSurface
{
    half3 albedo;
    half alpha;
    half3 emission;
    half metallic;
    half smoothness;
    half rimStrength;
};

half StylizedWrap(half ndotl, half wrap)
{
    return saturate((ndotl + wrap) / (1.0h + wrap));
}

// Two-tone step with an edge half-width of softness; softness 0 returns x unchanged.
half StylizedRamp(half x, half softness)
{
    half stepped = smoothstep(0.5h - softness, 0.5h + softness, x);
    return lerp(x, stepped, saturate(softness * ALPINE_RAMP_BLEND_SCALE));
}

// 1 when a main light shines, 0 when captures switch lights off, so tint and rim go flat with it.
half StylizedLightPresence(half3 lightColor)
{
    return saturate(Luminance(lightColor) * 4.0h);
}

half StylizedLightResponse(half3 normalWS, Light light)
{
    half ndotl = dot(normalWS, light.direction);
    half shaped = StylizedRamp(StylizedWrap(ndotl, _AlpineLightParams.x), _AlpineLightParams.y);
    return shaped * light.shadowAttenuation * light.distanceAttenuation;
}

// Blinn-Phong highlight shaped by the same ramp, so it is smooth with neutral globals and stepped when styled.
half StylizedHighlight(half3 normalWS, half3 viewDirWS, half3 lightDirWS, half smoothness)
{
    half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
    half ndoth = max(dot(normalWS, halfDir), 1e-4h);
    half exponent = exp2(10.0h * smoothness + 1.0h);
    return StylizedRamp(pow(ndoth, exponent), _AlpineLightParams.y);
}

half3 StylizedAdditionalLights(InputData inputData, StylizedSurface surface, half3 diffuseAlbedo, half4 shadowMask)
{
    half3 color = 0;
#if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
    uint meshRenderingLayers = GetMeshRenderingLayer();
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            half wrapped = StylizedWrap(dot(inputData.normalWS, light.direction), _AlpineLightParams.x);
            color += diffuseAlbedo * light.color * (wrapped * light.shadowAttenuation * light.distanceAttenuation);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            half wrapped = StylizedWrap(dot(inputData.normalWS, light.direction), _AlpineLightParams.x);
            color += diffuseAlbedo * light.color * (wrapped * light.shadowAttenuation * light.distanceAttenuation);
        }
    LIGHT_LOOP_END
#endif
    return color;
}

half3 StylizedFragmentColor(InputData inputData, StylizedSurface surface, half4 shadowMask)
{
    half3 normalWS = inputData.normalWS;
    half3 viewDirWS = inputData.viewDirectionWS;

    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask);
#ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer()))
    {
        mainLight.color = 0;
    }
#endif

    half presence = StylizedLightPresence(mainLight.color);
    half lit = StylizedLightResponse(normalWS, mainLight);

    // Metals trade diffuse for an always-on ambient reflection tinted by their albedo.
    half reflectionWeight = saturate(surface.metallic * (ALPINE_METAL_REFLECTION_BASE + _AlpineLightParams.w));
    half3 diffuseAlbedo = surface.albedo * (1.0h - reflectionWeight);

    half tintAmount = _AlpineShadowTint.a * (1.0h - lit) * presence;
    half3 ambient = SampleSH(normalWS) * lerp(half3(1, 1, 1), _AlpineShadowTint.rgb, tintAmount);

    half3 color = diffuseAlbedo * (ambient + mainLight.color * lit);

    half3 reflected = reflect(-viewDirWS, normalWS);
    color += SampleSH(reflected) * surface.albedo * reflectionWeight;

    half highlight = StylizedHighlight(normalWS, viewDirWS, mainLight.direction, surface.smoothness);
    half3 specularColor = lerp(half3(1, 1, 1), surface.albedo, surface.metallic);
    half directMask = mainLight.shadowAttenuation * mainLight.distanceAttenuation * saturate(dot(normalWS, mainLight.direction) * 4.0h);
    color += mainLight.color * specularColor * (highlight * surface.smoothness * surface.metallic * directMask);

    half facing = max(1.0h - saturate(dot(normalWS, viewDirWS)), 1e-4h);
    half rim = pow(facing, max(_AlpineLightParams.z, 1e-3h)) * _AlpineRimColor.a * surface.rimStrength * presence;
    color += _AlpineRimColor.rgb * rim;

    color += StylizedAdditionalLights(inputData, surface, diffuseAlbedo, shadowMask);
    color += surface.emission;
    return color;
}

#endif
