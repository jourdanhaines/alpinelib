#ifndef ALPINE_STYLIZED_LIT_FORWARD_PASS_INCLUDED
#define ALPINE_STYLIZED_LIT_FORWARD_PASS_INCLUDED

// A material that opts out of the main-light cookie (sky layers above a cloud-shadow cookie).
#if defined(_NO_LIGHT_COOKIE)
    #undef _LIGHT_COOKIES
#endif

#include "StylizedLighting.hlsl"
#include "StylizedSkyGradient.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 texcoord : TEXCOORD0;
#if defined(_VERTEX_COLOR) || defined(_FOG_SCALE_BLEND)
    half4 color : COLOR;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
#if defined(_VERTEX_COLOR)
    half3 vertexColor : TEXCOORD3;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD4;
#endif
#if defined(_FOG_SCALE_BLEND)
    half fogBlend : TEXCOORD5;
#endif
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// _FogScale eased to _FogScaleFar between view depths _FogScaleRamp.x and .y (shared haze for far layers);
// a ramp whose end is not past its start (the 0/0 default) is off and returns _FogScale exactly.
half StylizedRampedFogScale(float viewZ)
{
    if (!(_FogScaleRamp.y > _FogScaleRamp.x)) return _FogScale;

    return lerp(_FogScale, _FogScaleFar, (half)smoothstep(_FogScaleRamp.x, _FogScaleRamp.y, viewZ));
}

// Distance fog with the view depth scaled by the (ramped) _FogScale, so a material can sit further into (or out of)
// the fog; with _FOG_SCALE_BLEND the scale runs to _FogScaleAlpha by the vertex alpha. _FOG_RADIAL fogs by the
// distance to the camera instead, so sky objects fog alike across the screen and while the camera turns.
half StylizedFogFactor(float3 positionWS, half fogBlend)
{
    bool anyFogEnabled = false;
#if defined(FOG_LINEAR_KEYWORD_DECLARED)
    if (FOG_LINEAR) anyFogEnabled = true;
#endif
#if defined(FOG_EXP_KEYWORD_DECLARED)
    if (FOG_EXP) anyFogEnabled = true;
#endif
#if defined(FOG_EXP2_KEYWORD_DECLARED)
    if (FOG_EXP2) anyFogEnabled = true;
#endif
    if (!anyFogEnabled) return 0.0h;

#if defined(_FOG_RADIAL)
    float viewZ = distance(positionWS, GetCameraPositionWS());
#else
    float viewZ = -(mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).z);
#endif
    float nearToFarZ = max(viewZ - _ProjectionParams.y, 0.0);
    half fogScale = lerp(StylizedRampedFogScale(viewZ), _FogScaleAlpha, fogBlend);
    return ComputeFogFactorZ0ToFar(nearToFarZ * fogScale);
}

// The colour _FOG_SKY fogs toward: the sky (gradient, haze and sun glow) behind the fragment, or the scene fog colour
// while the sky globals are unset.
half3 StylizedSkyFogColor(float3 positionWS)
{
    if (_AlpineSkyZenith.w < 0.5h) return unity_FogColor.rgb;

    float3 direction = SafeNormalize(positionWS - GetCameraPositionWS());
    half3 sky = AlpineSkyGradient(direction.y, _AlpineSkyZenith.rgb, _AlpineSkyHorizon.rgb, _AlpineSkyGround.rgb, _AlpineSkyHaze.rgb,
        _AlpineSkyShape.x, _AlpineSkyShape.y, _AlpineSkyShape.z);
    if (_AlpineSunDirection.w > 0.5) sky += _AlpineSkyGlow.rgb * AlpineSunGlow(direction, SafeNormalize(_AlpineSunDirection.xyz), _AlpineSkyShape.w);
    return sky;
}

// Faceted normal from screen derivatives, turned to the side the mesh normal faces.
half3 StylizedFlatNormal(float3 positionWS, half3 meshNormalWS)
{
    half3 flatNormal = normalize(cross(ddy(positionWS), ddx(positionWS)));
    return dot(flatNormal, meshNormalWS) < 0.0h ? -flatNormal : flatNormal;
}

StylizedSurface StylizedInitializeSurface(Varyings input)
{
    half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    StylizedSurface surface;
    surface.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);
    surface.albedo = albedoAlpha.rgb * _BaseColor.rgb;
#if defined(_VERTEX_COLOR)
    surface.albedo *= lerp(half3(1, 1, 1), input.vertexColor, _VertexColorStrength);
#endif
#if defined(_EMISSION)
    surface.emission = SampleEmission(input.uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));
#else
    surface.emission = 0;
#endif
    surface.metallic = _Metallic;
    surface.smoothness = _Smoothness;
    surface.rimStrength = _RimStrength;
    return surface;
}

InputData StylizedInitializeInputData(Varyings input)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
#if defined(_FLAT_SHADING)
    normalWS = StylizedFlatNormal(input.positionWS, normalWS);
#endif
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
#if defined(_FOG_SCALE_BLEND)
    inputData.fogCoord = StylizedFogFactor(input.positionWS, input.fogBlend);
#else
    inputData.fogCoord = StylizedFogFactor(input.positionWS, 0.0h);
#endif
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    return inputData;
}

Varyings StylizedLitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;
    output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
#if defined(_VERTEX_COLOR)
    // Mesh colours arrive as raw sRGB bytes (Color32).
    output.vertexColor = SRGBToLinear(input.color.rgb);
#endif
#if defined(_FOG_SCALE_BLEND)
    output.fogBlend = input.color.a;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif
    return output;
}

void StylizedLitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    StylizedSurface surface = StylizedInitializeSurface(input);
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData = StylizedInitializeInputData(input);
#if defined(SHADOWS_SHADOWMASK)
    half4 shadowMask = unity_ProbesOcclusion;
#else
    half4 shadowMask = half4(1, 1, 1, 1);
#endif

    half3 color = StylizedFragmentColor(inputData, surface, shadowMask);
#if defined(_FOG_SKY)
    color = MixFogColor(color, StylizedSkyFogColor(input.positionWS), inputData.fogCoord);
#else
    color = MixFog(color, inputData.fogCoord);
#endif
    // Last, so the flat normal's and the texture's screen derivatives are taken with the whole pixel quad alive.
    ALPINE_DITHER_FADE_CLIP(input.positionCS);
    outColor = half4(color, 1.0h);

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
