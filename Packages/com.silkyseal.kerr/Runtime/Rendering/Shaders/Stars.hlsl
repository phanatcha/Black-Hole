#ifndef SILKYSEAL_KERR_STARS_HLSL
#define SILKYSEAL_KERR_STARS_HLSL

#include "Disk.hlsl"

// Float mirror of StarMap.Radiance (Runtime/Core/StarMap.cs), same cube layout; GpuSkyParityTests compares the two.

Texture2DArray<float4> _Stars;   // StarMap: faces as slices, levels as mips; texels (lux, kelvin, u, v)
int _StarSize;                   // texels per face edge on level 0
int _StarLevels;

void KerrToFace(float3 d, out int face, out float2 uv)
{
    float3 a = abs(d);
    if (a.x >= a.y && a.x >= a.z) { face = d.x >= 0.0 ? 0 : 1; uv = d.yz / a.x; }
    else if (a.y >= a.z) { face = d.y >= 0.0 ? 2 : 3; uv = d.zx / a.y; }
    else { face = d.z >= 0.0 ? 4 : 5; uv = d.xy / a.z; }
}

float3 KerrFromFace(int face, float2 uv)
{
    float s = (face & 1) == 0 ? 1.0 : -1.0;
    int axis = face >> 1;
    return normalize(axis == 0 ? float3(s, uv) : (axis == 1 ? float3(uv.y, s, uv.x) : float3(uv, s)));
}

int KerrStarTexel(float coordinate, int n) { return clamp((int)floor((coordinate + 1.0) * 0.5 * n), 0, n - 1); }

// Stars of the 3 × 3 texels around the pixel on one level, each tent-weighted in footprint units.
float3 KerrStarGather(int level, float3 d, int face, float2 uv, float3 jx, float3 jy, float g)
{
    float xx = dot(jx, jx), xy = dot(jx, jy), yy = dot(jy, jy), det = xx * yy - xy * xy;
    float solidAngle = length(cross(jx, jy));
    if (!(det > 0.0) || !(solidAngle > 0.0)) return 0.0;

    int n = _StarSize >> level;
    int2 centre = int2(KerrStarTexel(uv.x, n), KerrStarTexel(uv.y, n));
    float3 sum = 0.0;
    [loop] for (int dj = -1; dj <= 1; dj++)
    [loop] for (int di = -1; di <= 1; di++)
    {
        int f = face;
        int2 t = centre + int2(di, dj);
        bool offX = t.x < 0 || t.x >= n, offY = t.y < 0 || t.y >= n;
        if (offX && offY) continue; // no texel diagonally past a cube corner
        if (offX || offY)
        {
            float2 e;
            KerrToFace(KerrFromFace(face, (float2(t) + 0.5) * 2.0 / n - 1.0), f, e);
            t = int2(KerrStarTexel(e.x, n), KerrStarTexel(e.y, n));
        }

        float4 s = _Stars.Load(int4(t, f, level));
        if (s.x == 0.0) continue;
        float3 star = KerrFromFace(f, s.zw);
        float cosine = dot(star, d);
        if (cosine <= 0.0) continue;
        float3 offset = star / cosine - d;
        float ox = dot(offset, jx), oy = dot(offset, jy);
        float2 tent = max(0.0, 1.0 - abs(float2(yy * ox - xy * oy, xx * oy - xy * ox) / det));
        float weight = tent.x * tent.y;
        if (weight <= 0.0) continue;

        // The blackbody at g·T, at the ratio of its luminance to the emitted one; per lux of the star.
        float4 seen = KerrBlackbodyTexel(g * s.y);
        sum += weight * s.x * seen.rgb * exp2(seen.a - KerrBlackbodyTexel(s.y).a);
    }
    return sum / solidAngle;
}

// Starlight through a pixel in cd/m², from the level whose texels span the footprint blended with the next.
float3 KerrStarRadiance(float3 d, float3 jx, float3 jy, float g)
{
    int face;
    float2 uv;
    KerrToFace(d, face, uv);
    float texel = 2.0 / _StarSize / (1.0 + dot(uv, uv));
    float level = log2((length(jx) + length(jy)) / texel) + 1.0;
    if (!(level > 0.0)) return KerrStarGather(0, d, face, uv, jx, jy, g);
    int below = (int)level;
    if (below >= _StarLevels - 1) return KerrStarGather(_StarLevels - 1, d, face, uv, jx, jy, g);
    return lerp(KerrStarGather(below, d, face, uv, jx, jy, g), KerrStarGather(below + 1, d, face, uv, jx, jy, g), level - below);
}

#endif
