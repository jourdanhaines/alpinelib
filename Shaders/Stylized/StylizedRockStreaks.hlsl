#ifndef ALPINE_STYLIZED_ROCK_STREAKS_INCLUDED
#define ALPINE_STYLIZED_ROCK_STREAKS_INCLUDED

// _ROCK_STREAKS: procedural vertical streaks, desert-varnish runs and thin horizontal strata on steep faces, in object
// space so a floating-origin rebase never slides them. Near-horizontal faces (|normal.y| >= 0.5) stay plain.

// Sine-free hash (same result on every graphics API).
float AlpineRockHash(float value)
{
    value = frac(value * 0.1031);
    value *= value + 33.33;
    value *= value + value;
    return frac(value);
}

// Smooth 1D value noise in [0, 1].
float AlpineRockNoise(float x)
{
    float cell = floor(x);
    float blend = frac(x);
    blend = blend * blend * (3.0 - 2.0 * blend);
    return lerp(AlpineRockHash(cell), AlpineRockHash(cell + 1.0), blend);
}

// Darker runs from hashed tops in hashed columns, fading downward over 10-30 m.
float AlpineRockVarnish(float across, float height, float footprint)
{
    float width = 2.2 * _StreakScale;
    float column = floor(across / width);
    float inColumn = frac(across / width);
    float edges = smoothstep(0.0, 0.3, inColumn) * smoothstep(1.0, 0.7, inColumn);
    edges = lerp(edges, 0.55, saturate(footprint / width * 1.5 - 0.5));
    float isActive = step(0.55, AlpineRockHash(column + 5.7));
    float period = 36.0;
    float below = frac((AlpineRockHash(column + 1.3) * period - height) / period) * period;
    float runLength = lerp(10.0, 30.0, AlpineRockHash(column + 9.1));
    float run = saturate(1.0 - below / runLength) * smoothstep(0.0, 1.5, below);
    return run * edges * isActive;
}

// Thin level lines every ~0.6-1.4 strata periods, lighter or (more often) darker; widened and dimmed when sub-pixel.
float AlpineRockStrata(float height, float footprint)
{
    float period = _StrataPeriod;
    float layer = floor(height / period);
    float lineHeight = (layer + 0.3 + 0.4 * AlpineRockHash(layer + 3.1)) * period;
    float halfWidth = max(0.15, footprint);
    float strength = (1.0 - smoothstep(halfWidth * 0.5, halfWidth, abs(height - lineHeight))) * (0.15 / halfWidth);
    float tone = AlpineRockHash(layer + 8.2) < 0.65 ? -1.0 : 1.0;
    return tone * strength * (1.0 - smoothstep(0.6, 1.2, footprint));
}

// Albedo multiplier for a fragment at object-space position and normal.
half AlpineRockStreakShade(float3 positionOS, float3 normalOS)
{
    float3 normal = normalize(normalOS);
    float wall = 1.0 - smoothstep(0.35, 0.5, abs(normal.y));
    float2 across = normal.xz / max(length(normal.xz), 1e-4);
    float u = dot(positionOS.xz, float2(-across.y, across.x));
    float footprint = max(length(fwidth(positionOS)), 1e-4);

    float fineCell = 1.1 * _StreakScale;
    float fine = (AlpineRockNoise(u / fineCell) * 2.0 - 1.0) * (1.0 - smoothstep(0.3, 0.9, footprint / fineCell));
    float coarse = AlpineRockNoise(u / (2.6 * _StreakScale) + 17.3) * 2.0 - 1.0;
    float runBreak = 0.6 + 0.4 * AlpineRockNoise(positionOS.y / 23.0 + AlpineRockHash(floor(u / fineCell)) * 7.0);
    float streaks = (0.6 * fine + 0.4 * coarse) * runBreak - AlpineRockVarnish(u, positionOS.y, footprint);

    float shade = 1.0 + wall * (_StreakStrength * streaks + _StrataStrength * AlpineRockStrata(positionOS.y, footprint));
    return (half)max(shade, 0.0);
}

#endif
