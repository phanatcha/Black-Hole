#ifndef SILKYSEAL_KERR_EXPOSURE_HLSL
#define SILKYSEAL_KERR_EXPOSURE_HLSL

// Eye adaptation. CSRender writes the true scene luminance of every KERR_METER_STRIDE-th pixel into _MeterSamples:
// float32, so neither the half-float output's clamp nor its underflow can bias the meter and lock exposure.
// CSMeasureExposure averages log luminance over the samples within KERR_METER_RANGE stops of the brightest, so a sky
// 30 stops below the disk goes black as it would to an eye, and eases the persistent EV100 toward it.

#define KERR_METER_STRIDE 4
#define KERR_METER_THREADS 256
#define KERR_METER_RANGE 12.0
#define KERR_MIN_EV100 -10.0
#define KERR_MAX_EV100 60.0
#define KERR_NO_LIGHT -1e30

RWStructuredBuffer<float> _MeterSamples;     // scene luminance, cd/m²
RWStructuredBuffer<float2> _ExposureState;   // (EV100, 1 once initialised); persists across frames
int _MeterCount;
int _AutoExposure;
float _ManualEV100;
float _ExposureCompensation;
float _DeltaTime;
float _AdaptationSpeed;                      // 1/s

// Saturation-based exposure (Lagarde & de Rousiers 2014): luminance 1.2 · 2^EV100 maps to 1.
float KerrExposure()
{
    float ev100 = _AutoExposure != 0 ? _ExposureState[0].x : _ManualEV100;
    return exp2(_ExposureCompensation - ev100) / 1.2;
}

float KerrLuminance(float3 linearSrgb) { return dot(linearSrgb, float3(0.2126, 0.7152, 0.0722)); }

float KerrMeterLog(uint i)
{
    float l = _MeterSamples[i];
    return l > 0.0 ? log2(l) : KERR_NO_LIGHT;
}

groupshared float gs_Brightest[KERR_METER_THREADS];
groupshared float2 gs_LogSum[KERR_METER_THREADS];

// One group of KERR_METER_THREADS: two strided passes over the samples (max, then the average within range).
void KerrMeasureExposure(uint t)
{
    float brightest = KERR_NO_LIGHT;
    for (uint i = t; i < (uint)_MeterCount; i += KERR_METER_THREADS)
        brightest = max(brightest, KerrMeterLog(i));
    gs_Brightest[t] = brightest;
    GroupMemoryBarrierWithGroupSync();
    [unroll]
    for (uint s = KERR_METER_THREADS / 2; s > 0; s >>= 1)
    {
        if (t < s) gs_Brightest[t] = max(gs_Brightest[t], gs_Brightest[t + s]);
        GroupMemoryBarrierWithGroupSync();
    }

    float floorLog = gs_Brightest[0] - KERR_METER_RANGE;
    float2 sum = 0.0;
    for (uint j = t; j < (uint)_MeterCount; j += KERR_METER_THREADS)
    {
        float l = KerrMeterLog(j);
        if (l > KERR_NO_LIGHT && l >= floorLog) sum += float2(l, 1.0);
    }
    gs_LogSum[t] = sum;
    GroupMemoryBarrierWithGroupSync();
    [unroll]
    for (uint s2 = KERR_METER_THREADS / 2; s2 > 0; s2 >>= 1)
    {
        if (t < s2) gs_LogSum[t] += gs_LogSum[t + s2];
        GroupMemoryBarrierWithGroupSync();
    }

    // A black frame (blackout, or nothing lit) says nothing about the scene: keep adapting from where we were.
    if (t != 0 || gs_LogSum[0].y == 0.0) return;
    float target = gs_LogSum[0].x / gs_LogSum[0].y + log2(100.0 / 12.5);   // EV100 = log2(L S / K)
    float2 state = _ExposureState[0];
    float ev = state.y > 0.5 ? lerp(state.x, target, 1.0 - exp(-_DeltaTime * _AdaptationSpeed)) : target;
    _ExposureState[0] = float2(clamp(ev, KERR_MIN_EV100, KERR_MAX_EV100), 1.0);
}

#endif
