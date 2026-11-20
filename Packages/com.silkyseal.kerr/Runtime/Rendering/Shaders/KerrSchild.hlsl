#ifndef SILKYSEAL_KERR_SCHILD_HLSL
#define SILKYSEAL_KERR_SCHILD_HLSL

// Float32 mirror of Runtime/Core/KerrSchild.cs (G = c = M = 1, spin along +z).
// 4-vectors are float4(x, y, z, t) so .xyz is the spatial part.

float KerrRadius(float3 x, float a)
{
    float w = dot(x, x) - a * a;
    return sqrt(0.5 * w + sqrt(0.25 * w * w + a * a * x.z * x.z));
}

float KerrHorizonRadius(float a)
{
    return 1.0 + sqrt(1.0 - a * a);
}

// r, f = 2r³/(r⁴ + a²z²) and the spatial part of l_μ (l_t = 1).
void KerrGeometry(float3 x, float a, out float r, out float f, out float3 l)
{
    r = KerrRadius(x, a);
    float r2 = r * r;
    float a2 = a * a;
    f = 2.0 * r2 * r / (r2 * r2 + a2 * x.z * x.z);
    float s = r2 + a2;
    l = float3((r * x.x + a * x.y) / s, (r * x.y - a * x.x) / s, x.z / r);
}

float4 KerrLower(float4 v, float3 x, float a)
{
    float r, f;
    float3 l;
    KerrGeometry(x, a, r, f, l);
    float flv = f * (v.w + dot(l, v.xyz));
    return float4(v.xyz + l * flv, -v.w + flv);
}

float4 KerrRaise(float4 p, float3 x, float a)
{
    float r, f;
    float3 l;
    KerrGeometry(x, a, r, f, l);
    float flp = f * (-p.w + dot(l, p.xyz));
    return float4(p.xyz - l * flp, -p.w + flp);
}

float3 KerrHamiltonianGradient(float3 x, float4 p, float a)
{
    float r, f;
    float3 l;
    KerrGeometry(x, a, r, f, l);
    float r2 = r * r;
    float a2 = a * a;
    float s = r2 + a2;
    float q = r2 * r2 + a2 * x.z * x.z;
    float3 dr = float3(x.x * r2 * r, x.y * r2 * r, x.z * r * s) / q;
    float lp = -p.w + dot(l, p.xyz);

    float A = p.x * (r * x.x + a * x.y) + p.y * (r * x.y - a * x.x);
    float3 dA = dr * (p.x * x.x + p.y * x.y) + float3(r * p.x - a * p.y, a * p.x + r * p.y, 0.0);
    float3 dlp = dA / s - dr * (2.0 * r * A / (s * s)) + float3(0.0, 0.0, p.z / r) - dr * (p.z * x.z / r2);

    // Core's 6r²/q − 8r⁶/q² regrouped as (f/r)(3 − 4r⁴/q): q² would overflow float past r ≈ 1e4.
    float3 df = dr * (f / r * (3.0 - 4.0 * r2 * r2 / q)) - float3(0.0, 0.0, f * 2.0 * a2 * x.z / q);

    return df * (-0.5 * lp * lp) - dlp * (f * lp);
}

#endif
