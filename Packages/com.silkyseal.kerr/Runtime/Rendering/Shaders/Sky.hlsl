#ifndef SILKYSEAL_KERR_SKY_HLSL
#define SILKYSEAL_KERR_SKY_HLSL

// Procedural debug sky: one colour per quadrant (sign of world x and z) with dark lines every
// 10° of latitude and longitude, like the checkerboard sky James et al. 2015 use to read lensing.

#define KERR_GRID_SPACING_DEG 10.0
#define KERR_GRID_LINE_HALF_WIDTH_DEG 0.4
#define KERR_GRID_LINE_DIM 0.2

float3 KerrGridSky(float3 dir)
{
    float lat = degrees(asin(clamp(dir.y, -1.0, 1.0)));
    float lon = degrees(atan2(dir.z, dir.x));
    float3 quadrant = dir.x >= 0.0
        ? (dir.z >= 0.0 ? float3(0.85, 0.25, 0.20) : float3(0.20, 0.65, 0.30))
        : (dir.z >= 0.0 ? float3(0.25, 0.40, 0.90) : float3(0.90, 0.80, 0.25));
    // Distance in degrees to the nearest grid line on each axis.
    float2 toLine = abs(frac(float2(lat, lon) / KERR_GRID_SPACING_DEG + 0.5) - 0.5) * KERR_GRID_SPACING_DEG;
    return min(toLine.x, toLine.y) < KERR_GRID_LINE_HALF_WIDTH_DEG ? quadrant * KERR_GRID_LINE_DIM : quadrant;
}

#endif
