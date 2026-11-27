#ifndef SILKYSEAL_KERR_SWIRL_HLSL
#define SILKYSEAL_KERR_SWIRL_HLSL

// Float mirror of Runtime/Core/SwirlPattern.cs; the constants and the hash must match it. How far each ring has turned
// at the camera's time comes from SwirlTables, worked out in double, so the pattern stays smooth however long the
// scene runs. GpuDiskParityTests and GpuGasParityTests compare the two.

#define KERR_SWIRL_RING_WIDTH 0.05
#define KERR_SWIRL_SLOTS 24
#define KERR_SWIRL_RINGS 128
#define KERR_SWIRL_GAS_COARSENESS 4
#define KERR_SWIRL_JITTER 0.8
#define KERR_SWIRL_SHARPNESS 8.0
#define KERR_SWIRL_WEIGHT_FLOOR 1e-3
#define KERR_SWIRL_ACROSS 2.0
#define KERR_SWIRL_ALONG 0.6
#define KERR_SWIRL_CLUMP_HEIGHT 2.0
#define KERR_SWIRL_NOISE_GAIN 1.8
#define KERR_SWIRL_CLUMP_GAIN 0.8
#define KERR_SWIRL_OCTAVES 3
#define KERR_SWIRL_DRIFT 0.7
#define KERR_SWIRL_DRIFT_ACROSS 0.8
#define KERR_SWIRL_DRIFT_ALONG 2.0
#define KERR_SWIRL_RIBBON_MASK_ACROSS 0.7
#define KERR_SWIRL_RIBBON_MASK_ALONG 2.5
#define KERR_SWIRL_RIBBON_LOW -0.05
#define KERR_SWIRL_RIBBON_SPAN 0.35
#define KERR_SWIRL_RIBBON_FREQUENCY 1.0
#define KERR_SWIRL_RIBBON_GAIN 0.6
#define KERR_SWIRL_RIBBON_STRANDS 0.2

// Turns in [0, 1), four rings to a vector: the disk's rings, then the hot flow's coarser ones (SwirlTables).
float4 _SwirlTurns[(KERR_SWIRL_RINGS + KERR_SWIRL_RINGS / KERR_SWIRL_GAS_COARSENESS) / 4];

uint KerrPcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

// Perlin's cube-edge gradient picked by hash h, dotted with the offset d from the corner.
float KerrSwirlGrad(uint h, float3 d)
{
    h &= 15u;
    float a = h < 8u ? d.x : d.y;
    float b = h < 4u ? d.y : (h == 12u || h == 14u ? d.x : d.z);
    return ((h & 1u) == 0u ? a : -a) + ((h & 2u) == 0u ? b : -b);
}

float KerrSwirlNoise(float3 p)
{
    float3 f = floor(p);
    int3 i = (int3)f;
    float3 t = p - f;
    float3 s = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
    // Corner hashes Pcg(x + Pcg(y + Pcg(z))), sharing their inner terms.
    uint x0 = (uint)i.x, x1 = x0 + 1u, z0 = KerrPcg((uint)i.z), z1 = KerrPcg((uint)i.z + 1u);
    uint y0z0 = KerrPcg((uint)i.y + z0), y1z0 = KerrPcg((uint)i.y + 1u + z0);
    uint y0z1 = KerrPcg((uint)i.y + z1), y1z1 = KerrPcg((uint)i.y + 1u + z1);
    return lerp(
        lerp(lerp(KerrSwirlGrad(KerrPcg(x0 + y0z0), t), KerrSwirlGrad(KerrPcg(x1 + y0z0), t - float3(1, 0, 0)), s.x),
             lerp(KerrSwirlGrad(KerrPcg(x0 + y1z0), t - float3(0, 1, 0)), KerrSwirlGrad(KerrPcg(x1 + y1z0), t - float3(1, 1, 0)), s.x), s.y),
        lerp(lerp(KerrSwirlGrad(KerrPcg(x0 + y0z1), t - float3(0, 0, 1)), KerrSwirlGrad(KerrPcg(x1 + y0z1), t - float3(1, 0, 1)), s.x),
             lerp(KerrSwirlGrad(KerrPcg(x0 + y1z1), t - float3(0, 1, 1)), KerrSwirlGrad(KerrPcg(x1 + y1z1), t - float3(1, 1, 1)), s.x), s.y),
        s.z);
}

float KerrSwirlFbm(float3 p, uint hash, int octaves)
{
    p += float3(hash & 0xFFFu, (hash >> 12u) & 0xFFFu, hash >> 24u) / 16.0;
    float sum = 0.0, amplitude = 1.0;
    [unroll]
    for (int i = 0; i < octaves; i++)
    {
        sum += amplitude * KerrSwirlNoise(p);
        p = 2.0 * p + float3(19.19, 7.31, 3.73);
        amplitude *= 0.5;
    }
    return sum;
}

// Fine strands that drift, bunch and merge into one broad ribbon for part of their orbit; see SwirlPattern.Strands.
float KerrSwirlStrands(float2 d, float height, uint hash)
{
    float2 o = float2(hash & 0xFFFu, (hash >> 12u) & 0xFFFu) / 16.0;
    float drift = d.x + KERR_SWIRL_DRIFT * KerrSwirlNoise(float3(d.x * KERR_SWIRL_DRIFT_ACROSS + o.x + 13.1, d.y * KERR_SWIRL_DRIFT_ALONG + o.y + 3.3, 9.5));
    float fine = KerrSwirlFbm(float3(drift * KERR_SWIRL_ACROSS, d.y * KERR_SWIRL_ALONG, height * KERR_SWIRL_ACROSS), hash, KERR_SWIRL_OCTAVES);
    float mask = smoothstep(KERR_SWIRL_RIBBON_LOW, KERR_SWIRL_RIBBON_LOW + KERR_SWIRL_RIBBON_SPAN,
        KerrSwirlNoise(float3(d.x * KERR_SWIRL_RIBBON_MASK_ACROSS + o.x + 41.3, d.y * KERR_SWIRL_RIBBON_MASK_ALONG + o.y + 17.9, 5.5)));
    float broad = KERR_SWIRL_RIBBON_GAIN * KerrSwirlNoise(float3(d.x * KERR_SWIRL_RIBBON_FREQUENCY + o.x + 7.7, d.y * KERR_SWIRL_ALONG + o.y, 0.5));
    return lerp(fine, broad + KERR_SWIRL_RIBBON_STRANDS * fine, mask);
}

// Interpolate four lattice values per octave; the clouds live in a disk, so they only need 2D noise.
float KerrCloudNoise(float2 p, uint seed, int octaves)
{
    float sum = 0.0;
    float amplitude = 1.0;
    [unroll]
    for (int octave = 0; octave < octaves; octave++)
    {
        int2 cell = (int2)floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        uint key = seed + asuint(cell.x) + 65537u * asuint(cell.y);
        float low = lerp((float)KerrPcg(key), (float)KerrPcg(key + 1u), f.x);
        float high = lerp((float)KerrPcg(key + 65537u), (float)KerrPcg(key + 65538u), f.x);
        sum += amplitude * (lerp(low, high, f.y) * 4.65661287e-10 - 1.0);
        p = p * 2.0 + float2(19.19, 7.31);
        amplitude *= 0.5;
    }
    return sum;
}

// Broad envelopes turn with the original cells. A bent coordinate folds finer filaments through each envelope.
float KerrStreamCloud(float2 d, uint hash, float envelope)
{
    d *= 1.35;
    float eddy = KerrCloudNoise(float2(1.25 * d.x, 3.2 * d.y), hash ^ 0xa561u, 3);
    float bend = 0.50 * eddy + 0.25 * envelope;
    float across = d.x + 6.0 * d.y + bend;
    float veil = KerrCloudNoise(float2(1.65 * across, 1.9 * d.y), hash ^ 0xc831u, 3);
    float wisps = KerrCloudNoise(float2(5.4 * across + 1.3 * veil, 1.7 * d.y + 0.5 * eddy), hash ^ 0x17efu, 3);
    float back = KerrCloudNoise(float2(4.8 * (d.x + 1.4 * d.y - bend), 2.0 * d.y), hash ^ 0xf41bu, 2);
    return 0.63 * envelope + 0.52 * veil + 0.68 * wisps + 0.48 * back;
}

// The point's position along ring j, in cells from the ring's own zero, in [0, slots), delay after the camera's time.
// entry: the ring's place in _SwirlTurns.
float KerrSwirlAlong(int ring, int entry, float width, int slots, float angle, float delay, float spin)
{
    float r = exp((ring + 0.5) * width);
    float turns = _SwirlTurns[entry >> 2][entry & 3] + delay / ((r * sqrt(r) + spin) * 6.28318531);
    float along = (angle - turns) * slots;
    return along - slots * floor(along / slots);
}

// The point relative to the seed k cells along from its own cell, in cells across and along the orbit.
float2 KerrSwirlOffset(int ring, int slots, float along, int k, float u, out uint hash)
{
    int slot = (int)floor(along) + k;
    hash = KerrPcg((uint)(ring * slots) + (uint)(slot + slots) % (uint)slots);
    uint next = KerrPcg(hash);
    float2 d = float2(u - (ring + 0.5 + KERR_SWIRL_JITTER * (hash * 2.32830644e-10 - 0.5)),
                      along - (slot + 0.5 + KERR_SWIRL_JITTER * (next * 2.32830644e-10 - 0.5)));
    hash = KerrPcg(next);
    return d;
}

// Multiplier on the gas's brightness or density at Kerr-Schild radius r, azimuth φ = atan2(y, x) and height z, delay
// after the camera's time; 1 when contrast is 0. coarseness: 1 for the disk, KERR_SWIRL_GAS_COARSENESS for the flow.
float KerrSwirl(float contrast, int coarseness, float r, float phi, float z, float delay, float spin, bool broadStreams, float cloudTexture)
{
    if (contrast == 0.0) return 1.0;
    float width = KERR_SWIRL_RING_WIDTH * coarseness;
    float u = log(r) / width, height = z / (r * width), angle = phi / 6.28318531;
    int first = (int)floor(u) - 1, rings = KERR_SWIRL_RINGS / coarseness, slots = KERR_SWIRL_SLOTS / coarseness;
    int table = coarseness == 1 ? 0 : KERR_SWIRL_RINGS;

    // Two passes over the 3 × 3 cells around the point: the nearest seed, then the weighted blend.
    float3 along = 0.0;
    float nearest = 1e30;
    uint hash;
    [unroll]
    for (int j = 0; j < 3; j++)
    {
        int ring = first + j;
        int clamped = clamp(ring, 0, rings - 1);
        along[j] = KerrSwirlAlong(clamped, table + clamped, width, slots, angle, delay, spin);
        if (ring < 0 || ring >= rings) continue;
        [unroll]
        for (int k = -1; k <= 1; k++)
        {
            float2 d = KerrSwirlOffset(ring, slots, along[j], k, u, hash);
            nearest = min(nearest, dot(d, d));
        }
    }
    if (nearest > 1e29) return 1.0;

    float sum = 0.0, weights = 0.0;
    [loop]
    for (int j2 = 0; j2 < 3; j2++)
    {
        int ring = first + j2;
        if (ring < 0 || ring >= rings) continue;
        [loop]
        for (int k = -1; k <= 1; k++)
        {
            float2 d = KerrSwirlOffset(ring, slots, along[j2], k, u, hash);
            float w = exp(-KERR_SWIRL_SHARPNESS * (dot(d, d) - nearest));
            if (w < KERR_SWIRL_WEIGHT_FLOOR) continue;
            // Both blue looks use the same coarse orbit grid, so the wisps move with their parent stream.
            float value;
            if (broadStreams)
            {
                value = 1.65 * KerrSwirlFbm(float3(1.25 * d.x + 0.35 * d.y, 0.55 * d.y, 0.0), hash, 2);
                if (cloudTexture > 0.0)
                    value = lerp(value, KerrStreamCloud(d, hash, value), cloudTexture);
            }
            else if (coarseness == 1)
                value = KERR_SWIRL_NOISE_GAIN * KerrSwirlStrands(d, height, hash);
            else
                value = KERR_SWIRL_CLUMP_GAIN * cos(6.28318531 * (hash * 2.32830644e-10 + height / KERR_SWIRL_CLUMP_HEIGHT));
            sum += w * value;
            weights += w;
        }
    }
    return 1.0 + contrast * clamp(sum / weights, -1.0, 1.0);
}

#endif
