#ifndef SILKYSEAL_KERR_DISK_HLSL
#define SILKYSEAL_KERR_DISK_HLSL

#include "KerrSchild.hlsl"

// Float mirror of Runtime/Core/ThinDiskRay.cs and CircularOrbit.Shift; temperature and colour come from the tables
// DiskTables.cs bakes from the exact Core functions. GpuDiskParityTests compares the two pixel by pixel.

#define KERR_TABLE_SIZE 1024
#define KERR_OPAQUE_TRANSMITTANCE 1e-3

int _DiskEnabled;
int _DiskShifts;
float _DiskOpacity;
float4 _DiskRange;        // (r_in, 1 / ln(r_out / r_in), r_out, T_peak in K)
float4 _BlackbodyRange;   // (ln T_min, 1 / (ln T_max − ln T_min))
Texture2D<float> _DiskFlux;    // (T / T_peak)⁴ over ln r
Texture2D<float4> _Blackbody;  // rgb = linear sRGB / Y, a = log2 Y in cd/m², over ln T

// Linear interpolation by hand: point loads work on every float format, filtering 32-bit floats is optional on Metal.
void KerrTableTexels(float u, out int i0, out float f)
{
    float i = saturate(u) * (KERR_TABLE_SIZE - 1);
    i0 = min((int)i, KERR_TABLE_SIZE - 2);
    f = i - i0;
}

float KerrDiskFlux(float r)
{
    int i0; float f;
    KerrTableTexels(log(r / _DiskRange.x) * _DiskRange.y, i0, f);
    return lerp(_DiskFlux.Load(int3(i0, 0, 0)), _DiskFlux.Load(int3(i0 + 1, 0, 0)), f);
}

// Linear sRGB in cd/m² of a blackbody surface.
float3 KerrBlackbody(float kelvin)
{
    int i0; float f;
    KerrTableTexels((log(kelvin) - _BlackbodyRange.x) * _BlackbodyRange.y, i0, f);
    float4 c = lerp(_Blackbody.Load(int3(i0, 0, 0)), _Blackbody.Load(int3(i0 + 1, 0, 0)), f);
    return c.rgb * exp2(c.a);
}

// One pass of the ray through z = 0 at x, with the ray's conserved k_t and k_φ = x k_y − y k_x.
// Composites front to back; returns false once the disk has blocked everything behind it.
bool KerrDiskCrossing(float3 x, float kt, float kPhi, float a, inout float3 radiance, inout float transmittance)
{
    float r = KerrRadius(x, a);
    if (r < _DiskRange.x || r > _DiskRange.z) return true;
    float flux = KerrDiskFlux(r);
    if (flux <= 0.0) return true;

    float kelvin = _DiskRange.w * sqrt(sqrt(flux));
    if (_DiskShifts != 0)
    {
        // g = 1 / (u^t (k_t + Ω k_φ)) for prograde circular-orbit gas; a blackbody at T seen at g is one at gT.
        float sr = sqrt(r);
        float ut = (r * sr + a) / (sqrt(sr) * sr * sqrt(r * sr - 3.0 * sr + 2.0 * a));
        kelvin /= ut * (kt + kPhi / (r * sr + a));
    }
    radiance += transmittance * _DiskOpacity * KerrBlackbody(kelvin);
    transmittance *= 1.0 - _DiskOpacity;
    return transmittance >= KERR_OPAQUE_TRANSMITTANCE;
}

#endif
