#ifndef SILKYSEAL_KERR_SCENE_HLSL
#define SILKYSEAL_KERR_SCENE_HLSL

#include "KerrSchild.hlsl"

// Float mirror of Runtime/Core/SceneBvh.cs and SceneRay.cs; the layout comes from SceneBuffers. Meshes near the hole,
// in Kerr-Schild coordinates, tested one integrator step (a straight chord) at a time. GpuSceneParityTests compares
// the two.

// Deepest stack a query needs: the median-split tree is about log₂(triangles / 4) deep.
#define KERR_SCENE_STACK 32
// SceneRay.MaxChordPerRadius: longest chord near the meshes, per unit distance from the hole's centre.
#define KERR_SCENE_CHORD 0.02

int _SceneEnabled;
float3 _SceneSunDirection; // unit, towards the sun
float3 _SceneSun;          // sun and sky light, the scene's luminance scale folded in
float3 _SceneAmbient;
StructuredBuffer<float4> _SceneNodes;     // per node: (min, first), (max, count); count 0 means children first, first + 1
StructuredBuffer<float4> _SceneTriangles; // per slot: (v0, triangle), (v1 − v0, 0), (v2 − v0, 0)
StructuredBuffer<float4> _SceneShading;   // per slot: three normals, three albedos

// Where the segment enters node i's box, as a fraction of its length; 2 when it misses or enters after tMax.
float KerrSceneEntry(int i, float3 from, float3 inverse, float tMax)
{
    float3 t1 = (_SceneNodes[2 * i].xyz - from) * inverse, t2 = (_SceneNodes[2 * i + 1].xyz - from) * inverse;
    float3 lo = min(t1, t2), hi = max(t1, t2);
    float enter = max(lo.x, max(lo.y, lo.z)), leave = min(hi.x, min(hi.y, hi.z));
    return leave >= max(enter, 0.0) && enter <= tMax ? max(enter, 0.0) : 2.0;
}

// Möller & Trumbore (1997), both faces, t in [0, tMax).
bool KerrSceneTriangle(int slot, float3 from, float3 dir, float tMax, out float t, out float u, out float v)
{
    t = 0.0; u = 0.0; v = 0.0;
    float3 v0 = _SceneTriangles[3 * slot].xyz, e1 = _SceneTriangles[3 * slot + 1].xyz, e2 = _SceneTriangles[3 * slot + 2].xyz;
    float3 p = cross(dir, e2);
    float det = dot(e1, p);
    if (det == 0.0) return false;
    float inverse = 1.0 / det;
    float3 s = from - v0;
    u = dot(s, p) * inverse;
    if (u < 0.0 || u > 1.0) return false;
    float3 q = cross(s, e1);
    v = dot(dir, q) * inverse;
    if (v < 0.0 || u + v > 1.0) return false;
    t = dot(e2, q) * inverse;
    return t >= 0.0 && t < tMax;
}

// Nearest triangle the segment from → to passes through: its leaf slot, the fraction t along the segment, and the
// weights u, v of its second and third vertex.
bool KerrSceneHit(float3 from, float3 to, out int slot, out float t, out float u, out float v)
{
    slot = -1; t = 1.0; u = 0.0; v = 0.0;
    float3 dir = to - from, inverse = 1.0 / dir;
    if (KerrSceneEntry(0, from, inverse, t) > 1.0) return false;
    int stack[KERR_SCENE_STACK];
    int top = 0;
    stack[top++] = 0;
    [loop]
    while (top > 0)
    {
        int node = stack[--top];
        int first = (int)_SceneNodes[2 * node].w, count = (int)_SceneNodes[2 * node + 1].w;
        if (count > 0)
        {
            [loop]
            for (int i = first; i < first + count; i++)
            {
                float ti, ui, vi;
                if (KerrSceneTriangle(i, from, dir, t, ti, ui, vi)) { slot = i; t = ti; u = ui; v = vi; }
            }
            continue;
        }
        float nearT = KerrSceneEntry(first, from, inverse, t), farT = KerrSceneEntry(first + 1, from, inverse, t);
        int nearChild = first, farChild = first + 1;
        if (farT < nearT)
        {
            float swapT = nearT; nearT = farT; farT = swapT;
            nearChild = first + 1; farChild = first;
        }
        // The nearer child goes on top so it is searched first and shortens the segment for the other.
        if (farT <= 1.0 && top < KERR_SCENE_STACK) stack[top++] = farChild;
        if (nearT <= 1.0 && top < KERR_SCENE_STACK) stack[top++] = nearChild;
    }
    return slot >= 0;
}

// Farthest any mesh can lie from the hole's centre: the root box's farthest corner. Mirror of SceneRay.Reach.
float KerrSceneReach()
{
    return length(max(abs(_SceneNodes[0].xyz), abs(_SceneNodes[1].xyz)));
}

// Unshifted linear RGB the surface at slot sends back up a ray that reached it travelling along dir: diffuse, both
// faces, no shadows, sunlight unbent. Mirror of SceneSurfaces.Shade.
float3 KerrSceneShade(int slot, float u, float v, float3 dir)
{
    float w = 1.0 - u - v;
    float3 n = normalize(w * _SceneShading[6 * slot].xyz + u * _SceneShading[6 * slot + 1].xyz + v * _SceneShading[6 * slot + 2].xyz);
    if (dot(n, dir) > 0.0) n = -n;
    float3 albedo = w * _SceneShading[6 * slot + 3].xyz + u * _SceneShading[6 * slot + 4].xyz + v * _SceneShading[6 * slot + 5].xyz;
    return albedo * (_SceneSun * max(0.0, dot(n, _SceneSunDirection)) + _SceneAmbient);
}

// g = ν_observed / ν_emitted for light from a surface at rest at x: √(−g_tt) / k_t; 0 inside the ergosphere.
float KerrSceneShift(float3 x, float kt, float a)
{
    float r, f;
    float3 l;
    KerrGeometry(x, a, r, f, l);
    return f < 1.0 && kt > 0.0 ? sqrt(1.0 - f) / kt : 0.0;
}

#endif
