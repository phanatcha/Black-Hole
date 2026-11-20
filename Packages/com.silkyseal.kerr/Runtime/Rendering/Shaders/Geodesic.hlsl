#ifndef SILKYSEAL_KERR_GEODESIC_HLSL
#define SILKYSEAL_KERR_GEODESIC_HLSL

#include "KerrSchild.hlsl"
#include "Disk.hlsl"

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

void KerrDerivatives(float3 x, float3 p, float pt, float a, out float3 dx, out float3 dp)
{
    float4 p4 = float4(p, pt);
    dx = KerrRaise(p4, x, a).xyz;
    dp = -KerrHamiltonianGradient(x, p4, a);
}

float KerrErrorRatio(float3 err, float3 y)
{
    float3 e = abs(err) / (1.0 + abs(y));
    return max(e.x, max(e.y, e.z));
}

// One adaptive Cash–Karp attempt; advances (x, p) only when the error is within tolerance.
void GeodesicStep(inout float3 x, inout float3 p, float pt, float a, inout float h, float tolerance)
{
    float3 kx1, kp1, kx2, kp2, kx3, kp3, kx4, kp4, kx5, kp5, kx6, kp6;
    KerrDerivatives(x, p, pt, a, kx1, kp1);
    KerrDerivatives(x + h * CK_B21 * kx1, p + h * CK_B21 * kp1, pt, a, kx2, kp2);
    KerrDerivatives(x + h * (CK_B31 * kx1 + CK_B32 * kx2), p + h * (CK_B31 * kp1 + CK_B32 * kp2), pt, a, kx3, kp3);
    KerrDerivatives(x + h * (CK_B41 * kx1 + CK_B42 * kx2 + CK_B43 * kx3),
                    p + h * (CK_B41 * kp1 + CK_B42 * kp2 + CK_B43 * kp3), pt, a, kx4, kp4);
    KerrDerivatives(x + h * (CK_B51 * kx1 + CK_B52 * kx2 + CK_B53 * kx3 + CK_B54 * kx4),
                    p + h * (CK_B51 * kp1 + CK_B52 * kp2 + CK_B53 * kp3 + CK_B54 * kp4), pt, a, kx5, kp5);
    KerrDerivatives(x + h * (CK_B61 * kx1 + CK_B62 * kx2 + CK_B63 * kx3 + CK_B64 * kx4 + CK_B65 * kx5),
                    p + h * (CK_B61 * kp1 + CK_B62 * kp2 + CK_B63 * kp3 + CK_B64 * kp4 + CK_B65 * kp5), pt, a, kx6, kp6);

    float3 ex = h * (CK_E1 * kx1 + CK_E3 * kx3 + CK_E4 * kx4 + CK_E5 * kx5 + CK_E6 * kx6);
    float3 ep = h * (CK_E1 * kp1 + CK_E3 * kp3 + CK_E4 * kp4 + CK_E5 * kp5 + CK_E6 * kp6);
    // Non-negative by construction; abs() only tells the Metal compiler so for pow().
    float err = max(KerrErrorRatio(ex, x), KerrErrorRatio(ep, p)) / tolerance;

    if (err <= 1.0)
    {
        x += h * (CK_C1 * kx1 + CK_C3 * kx3 + CK_C4 * kx4 + CK_C6 * kx6);
        p += h * (CK_C1 * kp1 + CK_C3 * kp3 + CK_C4 * kp4 + CK_C6 * kp6);
        h *= err > 0.0 ? min(5.0, 0.9 * pow(abs(err), -0.2)) : 5.0;
    }
    else
    {
        // A NaN error also lands here; MaxSteps then ends the ray.
        h *= max(0.1, 0.9 * pow(abs(err), -0.25));
    }
}

// Where one accepted step of length h meets z = 0: root of the cubic Hermite through both ends and their velocities.
// Mirrors RayTracer.EquatorCrossing; a chord would put the hit radius ~0.5 % off at this tolerance.
float3 EquatorCrossing(float3 x0, float3 p0, float3 x1, float3 p1, float pt, float a, float h)
{
    float3 v0 = KerrRaise(float4(p0, pt), x0, a).xyz * h;
    float3 v1 = KerrRaise(float4(p1, pt), x1, a).xyz * h;
    float t = x0.z / (x0.z - x1.z);
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float t2 = t * t;
        float z = (2.0 * t2 * t - 3.0 * t2 + 1.0) * x0.z + (t2 * t - 2.0 * t2 + t) * v0.z
                + (3.0 * t2 - 2.0 * t2 * t) * x1.z + (t2 * t - t2) * v1.z;
        float dz = (6.0 * t2 - 6.0 * t) * (x0.z - x1.z) + (3.0 * t2 - 4.0 * t + 1.0) * v0.z + (3.0 * t2 - 2.0 * t) * v1.z;
        t = saturate(t - z / dz);
    }
    float s2 = t * t, s3 = s2 * t;
    return x0 * (2.0 * s3 - 3.0 * s2 + 1.0) + v0 * (s3 - 2.0 * s2 + t) + x1 * (3.0 * s2 - 2.0 * s3) + v1 * (s3 - s2);
}

// Traces a past-directed null covector kLower = (p_x, p_y, p_z, p_t) from x0, shading the disk on the way when
// _DiskEnabled. radiance is the disk light collected, transmittance what still shows of the sky behind it.
// Cost: at most maxSteps × 6 gradient evaluations.
void TraceRay(float3 x0, float4 kLower, float spin, float tolerance, float farRadius, int maxSteps,
              out float3 skyDir, out int status, out float3 radiance, out float transmittance)
{
    radiance = 0.0;
    transmittance = 1.0;
    float captureRadius = KerrHorizonRadius(spin) + KERR_HORIZON_MARGIN;
    float3 x = x0;
    float3 p = kLower.xyz;
    float pt = kLower.w;
    float h = KERR_INITIAL_STEP_PER_RADIUS * KerrRadius(x0, spin);

    status = KERR_STATUS_MAX_STEPS;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        if (KerrRadius(x, spin) <= captureRadius) { status = KERR_STATUS_CAPTURED; break; }
        if (length(x) >= farRadius) { status = KERR_STATUS_ESCAPED; break; }
        float3 before = x, pBefore = p;
        float stepSize = h;
        GeodesicStep(x, p, pt, spin, h, tolerance);
        // A rejected step leaves x unchanged, so z² ≥ 0 and no crossing is seen twice.
        if (_DiskEnabled == 0 || before.z * x.z >= 0.0) continue;

        float3 crossing = EquatorCrossing(before, pBefore, x, p, pt, spin, stepSize);
        if (!KerrDiskCrossing(crossing, pt, x.x * p.y - x.y * p.x, spin, radiance, transmittance))
        {
            status = KERR_STATUS_ABSORBED;
            break;
        }
    }
    skyDir = normalize(KerrRaise(float4(p, pt), x, spin).xyz);
}

#endif
