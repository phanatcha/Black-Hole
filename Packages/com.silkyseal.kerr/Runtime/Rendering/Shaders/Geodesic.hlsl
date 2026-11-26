#ifndef SILKYSEAL_KERR_GEODESIC_HLSL
#define SILKYSEAL_KERR_GEODESIC_HLSL

#include "KerrSchild.hlsl"
#include "Disk.hlsl"
#include "Gas.hlsl"

// Float32 mirror of Runtime/Core/GeodesicIntegrator.cs and RayTracer.Trace. Keep the two in step:
// GpuParityTests compares them ray by ray.

#define KERR_HORIZON_MARGIN 1e-3
#define KERR_INITIAL_STEP_PER_RADIUS 0.01
#define KERR_STATUS_ESCAPED 0
#define KERR_STATUS_CAPTURED 1
#define KERR_STATUS_MAX_STEPS 2
#define KERR_STATUS_ABSORBED 3

static const float CK_B21 = 1.0 / 5.0;
static const float CK_B31 = 3.0 / 40.0, CK_B32 = 9.0 / 40.0;
static const float CK_B41 = 3.0 / 10.0, CK_B42 = -9.0 / 10.0, CK_B43 = 6.0 / 5.0;
static const float CK_B51 = -11.0 / 54.0, CK_B52 = 5.0 / 2.0, CK_B53 = -70.0 / 27.0, CK_B54 = 35.0 / 27.0;
static const float CK_B61 = 1631.0 / 55296.0, CK_B62 = 175.0 / 512.0, CK_B63 = 575.0 / 13824.0,
                   CK_B64 = 44275.0 / 110592.0, CK_B65 = 253.0 / 4096.0;
static const float CK_C1 = 37.0 / 378.0, CK_C3 = 250.0 / 621.0, CK_C4 = 125.0 / 594.0, CK_C6 = 512.0 / 1771.0;
static const float CK_E1 = 37.0 / 378.0 - 2825.0 / 27648.0, CK_E3 = 250.0 / 621.0 - 18575.0 / 48384.0,
                   CK_E4 = 125.0 / 594.0 - 13525.0 / 55296.0, CK_E5 = -277.0 / 14336.0,
                   CK_E6 = 512.0 / 1771.0 - 0.25;

void KerrDerivatives(float3 x, float3 p, float pt, float a, out float3 dx, out float dt, out float3 dp)
{
    float4 p4 = float4(p, pt);
    float4 k = KerrRaise(p4, x, a);
    dx = k.xyz;
    dt = k.w;
    dp = -KerrHamiltonianGradient(x, p4, a);
}

float KerrErrorRatio(float3 err, float3 y)
{
    float3 e = abs(err) / (1.0 + abs(y));
    return max(e.x, max(e.y, e.z));
}

// One adaptive Cash–Karp attempt; advances (x, t, p) and returns true only when the error is within tolerance.
bool GeodesicStep(inout float3 x, inout float t, inout float3 p, float pt, float a, inout float h, float tolerance)
{
    float3 kx1, kp1, kx2, kp2, kx3, kp3, kx4, kp4, kx5, kp5, kx6, kp6;
    float kt1, kt3, kt4, kt6, unused;
    KerrDerivatives(x, p, pt, a, kx1, kt1, kp1);
    KerrDerivatives(x + h * CK_B21 * kx1, p + h * CK_B21 * kp1, pt, a, kx2, unused, kp2);
    KerrDerivatives(x + h * (CK_B31 * kx1 + CK_B32 * kx2), p + h * (CK_B31 * kp1 + CK_B32 * kp2), pt, a, kx3, kt3, kp3);
    KerrDerivatives(x + h * (CK_B41 * kx1 + CK_B42 * kx2 + CK_B43 * kx3),
                    p + h * (CK_B41 * kp1 + CK_B42 * kp2 + CK_B43 * kp3), pt, a, kx4, kt4, kp4);
    KerrDerivatives(x + h * (CK_B51 * kx1 + CK_B52 * kx2 + CK_B53 * kx3 + CK_B54 * kx4),
                    p + h * (CK_B51 * kp1 + CK_B52 * kp2 + CK_B53 * kp3 + CK_B54 * kp4), pt, a, kx5, unused, kp5);
    KerrDerivatives(x + h * (CK_B61 * kx1 + CK_B62 * kx2 + CK_B63 * kx3 + CK_B64 * kx4 + CK_B65 * kx5),
                    p + h * (CK_B61 * kp1 + CK_B62 * kp2 + CK_B63 * kp3 + CK_B64 * kp4 + CK_B65 * kp5), pt, a, kx6, kt6, kp6);

    float3 ex = h * (CK_E1 * kx1 + CK_E3 * kx3 + CK_E4 * kx4 + CK_E5 * kx5 + CK_E6 * kx6);
    float3 ep = h * (CK_E1 * kp1 + CK_E3 * kp3 + CK_E4 * kp4 + CK_E5 * kp5 + CK_E6 * kp6);
    // Non-negative by construction; abs() only tells the Metal compiler so for pow().
    float err = max(KerrErrorRatio(ex, x), KerrErrorRatio(ep, p)) / tolerance;

    if (err <= 1.0)
    {
        x += h * (CK_C1 * kx1 + CK_C3 * kx3 + CK_C4 * kx4 + CK_C6 * kx6);
        t += h * (CK_C1 * kt1 + CK_C3 * kt3 + CK_C4 * kt4 + CK_C6 * kt6);
        p += h * (CK_C1 * kp1 + CK_C3 * kp3 + CK_C4 * kp4 + CK_C6 * kp6);
        h *= err > 0.0 ? min(5.0, 0.9 * pow(abs(err), -0.2)) : 5.0;
        return true;
    }
    // A NaN error also lands here; MaxSteps then ends the ray.
    h *= max(0.1, 0.9 * pow(abs(err), -0.25));
    return false;
}

// Mirrors RayTracer.EquatorCrossing: where and when the step meets z = 0, from (x, t) and h dx^μ/dλ at both ends.
// A chord would put the hit radius ~0.5 % off at this tolerance.
float4 EquatorCrossing(float4 x0, float4 v0, float4 x1, float4 v1)
{
    float s = x0.z / (x0.z - x1.z);
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float s2 = s * s;
        float z = (2.0 * s2 * s - 3.0 * s2 + 1.0) * x0.z + (s2 * s - 2.0 * s2 + s) * v0.z
                + (3.0 * s2 - 2.0 * s2 * s) * x1.z + (s2 * s - s2) * v1.z;
        float dz = (6.0 * s2 - 6.0 * s) * (x0.z - x1.z) + (3.0 * s2 - 4.0 * s + 1.0) * v0.z + (3.0 * s2 - 2.0 * s) * v1.z;
        s = saturate(s - z / dz);
    }
    float t2 = s * s, t3 = t2 * s;
    return x0 * (2.0 * t3 - 3.0 * t2 + 1.0) + v0 * (t3 - 2.0 * t2 + s) + x1 * (3.0 * t2 - 2.0 * t3) + v1 * (t3 - t2);
}

// Traces a past-directed null covector kLower = (p_x, p_y, p_z, p_t) from x0, shading the disk on the way when
// _DiskEnabled and the hot gas when _GasView is on. radiance is the disk and eye-view gas light collected,
// transmittance what still shows of the sky behind it; gas carries the 230 GHz Stokes parameters.
// Cost: at most maxSteps × 6 gradient evaluations.
void TraceRay(float3 x0, float4 kLower, float spin, float tolerance, float farRadius, int maxSteps, inout KerrGasRay gas,
              out float3 skyDir, out int status, out float3 radiance, out float transmittance)
{
    radiance = 0.0;
    transmittance = 1.0;
    float captureRadius = KerrHorizonRadius(spin) + KERR_HORIZON_MARGIN;
    float3 x = x0;
    float3 p = kLower.xyz;
    float pt = kLower.w;
    float h = KERR_INITIAL_STEP_PER_RADIUS * KerrRadius(x0, spin);
    float time = 0.0; // Kerr-Schild coordinate time relative to the camera

    status = KERR_STATUS_MAX_STEPS;
    if (_GasView != KERR_GAS_OFF) KerrGasStep(gas, x, 0.0, kLower, spin, 0.0, h, transmittance);
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        if (KerrRadius(x, spin) <= captureRadius) { status = KERR_STATUS_CAPTURED; break; }
        if (length(x) >= farRadius) { status = KERR_STATUS_ESCAPED; break; }
        float4 before = float4(x, time);
        float3 pBefore = p;
        float stepSize = h;
        if (!GeodesicStep(x, time, p, pt, spin, h, tolerance)) continue;

        if (_DiskEnabled != 0 && before.z * x.z < 0.0)
        {
            float4 v0 = KerrRaise(float4(pBefore, pt), before.xyz, spin) * stepSize;
            float4 v1 = KerrRaise(float4(p, pt), x, spin) * stepSize;
            float4 crossing = EquatorCrossing(before, v0, float4(x, time), v1);
            if (!KerrDiskCrossing(crossing.xyz, crossing.w, pt, x.x * p.y - x.y * p.x, spin, radiance, transmittance))
            {
                status = KERR_STATUS_ABSORBED;
                break;
            }
        }
        if (_GasView != KERR_GAS_OFF) KerrGasStep(gas, x, time, float4(p, pt), spin, stepSize, h, transmittance);
    }
    if (_GasView == KERR_GAS_EYE) radiance += KerrGasRgb(gas.visible);
    skyDir = normalize(KerrRaise(float4(p, pt), x, spin).xyz);
}

#endif
