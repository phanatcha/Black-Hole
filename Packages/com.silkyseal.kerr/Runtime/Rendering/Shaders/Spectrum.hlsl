#ifndef SILKYSEAL_KERR_SPECTRUM_HLSL
#define SILKYSEAL_KERR_SPECTRUM_HLSL

#include "Exposure.hlsl"

// Float mirror of RgbSpectrum.Shift (Runtime/Core/RgbSpectrum.cs); the constants are its own. GpuSkyParityTests
// compares the two.

#define KERR_SPECTRUM_TABLE 128
#define KERR_SPECTRUM_SAMPLES 48
#define KERR_SPECTRUM_FIRST_NM 360.0
#define KERR_SPECTRUM_STEP_NM 10.0
#define KERR_SPECTRUM_NORMAL_NM 555.0
#define KERR_SPECTRUM_BUMP_NM 520.0
#define KERR_SPECTRUM_BUMP_WIDTH_NM 30.0
#define KERR_SECOND_RADIATION 1.4387768775e7   // hc / k in nm·K

Texture2DArray<float4> _Spectrum;     // (ln T, a, b, 0); slice = largest channel, axes = the other two over it
Texture2D<float4> _SpectrumWeights;   // linear sRGB per unit spectrum at each quadrature wavelength

float KerrBump(float nm)
{
    float t = (nm - KERR_SPECTRUM_BUMP_NM) / KERR_SPECTRUM_BUMP_WIDTH_NM;
    return exp(-0.5 * t * t);
}

// (ln T, a, b) of a colour that is not black, by bilinear lookup.
float3 KerrSpectrumParameters(float3 rgb)
{
    float m = max(rgb.r, max(rgb.g, rgb.b));
    int slice = rgb.r >= rgb.g && rgb.r >= rgb.b ? 0 : (rgb.g >= rgb.b ? 1 : 2);
    float2 p = (slice == 0 ? rgb.gb : (slice == 1 ? rgb.br : rgb.rg)) / m * (KERR_SPECTRUM_TABLE - 1);
    int2 i = min((int2)p, KERR_SPECTRUM_TABLE - 2);
    float2 f = p - i;
    float4 bottom = lerp(_Spectrum.Load(int4(i, slice, 0)), _Spectrum.Load(int4(i + int2(1, 0), slice, 0)), f.x);
    float4 top = lerp(_Spectrum.Load(int4(i + int2(0, 1), slice, 0)), _Spectrum.Load(int4(i + int2(1, 1), slice, 0)), f.x);
    float4 c = lerp(bottom, top, f.y);
    return float3(c.x, c.yz * 2.0 * m); // the table is fitted for a largest channel of ½
}

// Linear sRGB of a source with this colour seen with frequency shift g (observed / emitted).
float3 KerrShiftRgb(float3 rgb, float g)
{
    rgb = max(rgb, 0.0);
    if (max(rgb.r, max(rgb.g, rgb.b)) <= 0.0) return 0.0;

    float3 p = KerrSpectrumParameters(rgb);
    float kelvin = exp(p.x);
    float norm = exp(KERR_SECOND_RADIATION / (KERR_SPECTRUM_NORMAL_NM * kelvin)) - 1.0;
    float3 model = 0.0, shifted = 0.0;
    [loop] for (int k = 0; k < KERR_SPECTRUM_SAMPLES; k++)
    {
        float nm = KERR_SPECTRUM_FIRST_NM + k * KERR_SPECTRUM_STEP_NM;
        float3 w = _SpectrumWeights.Load(int3(k, 0, 0)).rgb * norm;
        // exp overflows to infinity far into the red: that sample is then 0, as it should be.
        model += w * ((p.y + p.z * KerrBump(nm)) / (exp(KERR_SECOND_RADIATION / (nm * kelvin)) - 1.0));
        shifted += w * ((p.y + p.z * KerrBump(g * nm)) / (exp(KERR_SECOND_RADIATION / (nm * g * kelvin)) - 1.0));
    }
    // What the spectrum cannot hold keeps its hue and takes the spectrum's change in brightness.
    float y = KerrLuminance(model);
    return max(shifted + (rgb - model) * (y > 0.0 ? KerrLuminance(shifted) / y : 1.0), 0.0);
}

#endif
