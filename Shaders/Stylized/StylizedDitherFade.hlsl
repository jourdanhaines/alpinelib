#ifndef ALPINE_STYLIZED_DITHER_FADE_INCLUDED
#define ALPINE_STYLIZED_DITHER_FADE_INCLUDED

// Screen-door fade-in (_DITHER_FADE): _FadeAmount 0 clips every pixel, 1 none. Per instance when drawn instanced
// (a MaterialPropertyBlock array), a plain uniform otherwise. Every pass clips the same pixels, so colour, depth
// and the shadow map fade together.
#if defined(_DITHER_FADE)
UNITY_INSTANCING_BUFFER_START(AlpineDitherFade)
    UNITY_DEFINE_INSTANCED_PROP(float, _FadeAmount)
UNITY_INSTANCING_BUFFER_END(AlpineDitherFade)

// 4x4 ordered (Bayer) threshold in (0, 1) for a pixel, built from two 2x2 levels without a table.
float AlpineDitherThreshold(float2 pixel)
{
    float2 cell = fmod(floor(pixel), 4.0);
    float2 fine = fmod(cell, 2.0);
    float2 coarse = floor(cell * 0.5);
    float level = 4.0 * fmod(2.0 * fine.x + 3.0 * fine.y, 4.0) + fmod(2.0 * coarse.x + 3.0 * coarse.y, 4.0);
    return (level + 0.5) / 16.0;
}

void AlpineDitherFadeClip(float4 positionCS)
{
    float amount = UNITY_ACCESS_INSTANCED_PROP(AlpineDitherFade, _FadeAmount);
    clip(amount - AlpineDitherThreshold(positionCS.xy));
}

    #define ALPINE_DITHER_FADE_CLIP(positionCS) AlpineDitherFadeClip(positionCS)
#else
    #define ALPINE_DITHER_FADE_CLIP(positionCS)
#endif

#endif
