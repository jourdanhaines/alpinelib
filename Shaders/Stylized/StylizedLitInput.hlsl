#ifndef ALPINE_STYLIZED_LIT_INPUT_INCLUDED
#define ALPINE_STYLIZED_LIT_INPUT_INCLUDED

// Shared by every pass of AlpineLib/Stylized/Lit, so UnityPerMaterial is identical everywhere (SRP Batcher).
// The URP pass files (ShadowCaster, DepthOnly, DepthNormals) read _BaseMap, _BaseColor and _Cutoff from here.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseMap_TexelSize;
    half4 _BaseColor;
    half4 _EmissionColor;
    half _Cutoff;
    half _Smoothness;
    half _Metallic;
    half _RimStrength;
    half _VertexColorStrength;
    half _FogScale;
    half _FogScaleAlpha;
    // _FogScale ramps to _FogScaleFar from view depth x to y (x 0, y 0: no ramp).
    half _FogScaleFar;
    float4 _FogScaleRamp;
    // The screen-door amount of a non-instanced fading draw (kept here in every variant: SRP Batcher layout).
    float _FadeAmount;
    // _ROCK_STREAKS: column width multiplier, streak luminance share, strata period (m), strata luminance share.
    half _StreakScale;
    half _StreakStrength;
    half _StrataPeriod;
    half _StrataStrength;
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// Style globals (StylizedLightingGlobals); all zero is plain Lambert.
// rgb colour, a strength.
half4 _AlpineShadowTint;
half4 _AlpineRimColor;
// x wrap, y ramp softness, z rim power, w metal reflection.
half4 _AlpineLightParams;
// xyz toward the sun, w 1 when set.
float4 _AlpineSunDirection;

// The skybox's gradient for _FOG_SKY (StylizedLightingGlobals.SetSky): linear rgb, zenith w 1 when set.
half4 _AlpineSkyZenith;
half4 _AlpineSkyHorizon;
half4 _AlpineSkyGround;
half4 _AlpineSkyHaze;
// x gradient exponent, y haze height, z haze sharpness, w sun glow size.
half4 _AlpineSkyShape;
// Sun glow colour times its strength.
half4 _AlpineSkyGlow;

#include "StylizedDitherFade.hlsl"

#endif
