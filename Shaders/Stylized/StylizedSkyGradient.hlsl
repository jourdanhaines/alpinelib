#ifndef ALPINE_STYLIZED_SKY_GRADIENT_INCLUDED
#define ALPINE_STYLIZED_SKY_GRADIENT_INCLUDED

// The stylized sky's colour at a unit direction's height: horizon to zenith above, horizon to ground below, the
// haze band on the horizon. Shared by the skybox and by materials that fog toward the sky (_FOG_SKY).
half3 AlpineSkyGradient(float height, half3 zenith, half3 horizon, half3 ground, half3 haze, half exponent, half hazeHeight, half hazeSharpness)
{
    half shape = max(exponent, 0.05h);
    half3 sky = lerp(horizon, zenith, pow(saturate(height), shape));
    half3 below = lerp(horizon, ground, pow(saturate(-height), shape));
    half3 color = height >= 0.0 ? sky : below;

    half hazeShare = pow(saturate(1.0h - abs(height) / max(hazeHeight, 0.001h)), max(hazeSharpness, 0.1h));
    return lerp(color, haze, hazeShare);
}

// Strength of the sun's glow (not its disc) along a unit direction, toward a unit sun direction.
half AlpineSunGlow(float3 direction, float3 sun, half glowSize)
{
    half falloff = saturate(1.0h - length(direction - sun) / max(glowSize, 0.001h));
    return falloff * falloff;
}

#endif
