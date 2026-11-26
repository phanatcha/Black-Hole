#ifndef SILKYSEAL_KERR_GAS_HLSL
#define SILKYSEAL_KERR_GAS_HLSL

#include "KerrSchild.hlsl"
#include "Swirl.hlsl"

// Float mirror of HotFlow, Synchrotron, RadiativeTransfer, WalkerPenrose and HotFlowRay in Runtime/Core; the power-law
// integrals come from a table GasTables.cs bakes from the Core function. GpuGasParityTests compares the two.
// Frequencies are in GHz, everything else cgs: ν³ at visible frequencies overflows float in Hz.

#define KERR_GAS_OFF 0
#define KERR_GAS_EYE 1
#define KERR_GAS_RADIO 2
#define KERR_GAS_POLARIZATION 3
#define KERR_GAS_TABLE_SIZE 1024
#define KERR_GAS_OUTER_RADIUS 50.0     // HotFlow.OuterRadius
#define KERR_GAS_STEP_FRACTION 0.1     // HotFlowRay.StepFraction
#define KERR_GAS_HORIZON_MARGIN 1e-3   // RayTracer.HorizonMargin

int _GasView;
float _GasSwirl; // SwirlPattern contrast; 0 leaves the flow smooth
// GasTables.Parameters: [0] (n_th⁰, n_nth⁰, nonthermal slope, T_e⁰); [1] (r_isco, E_isco, L_isco, cm per M);
// [2] field mix (radial, toroidal, vertical); [3] frequencies in GHz (blue, green, red, radio);
// [4..6] VisibleBand rows; [7] power-law table (ln x_min, 1 / ln(x_max / x_min), slope below x_min).
float4 _Gas[8];
Texture2D<float2> _GasPowerLaw; // (ln G_I, ln G_Q) over ln x

float KerrDot(float4 u, float4 v, float3 x, float a)
{
    return dot(KerrLower(u, x, a), v);
}

// --- HotFlow ---

// n⁰ ρ^slope e^(−z²/2ρ²); ρ² = r² sin²θ, taken from x² + y² so it does not cancel near the axis.
float KerrGasProfile(float3 x, float r, float a, float scale, float slope)
{
    float rho2 = r * r * (x.x * x.x + x.y * x.y) / (r * r + a * a);
    return rho2 > 1e-12 ? scale * pow(rho2, 0.5 * slope) * exp(-0.5 * x.z * x.z / rho2) : 0.0;
}

float4 KerrGasRadial(float3 x, float r, float a)
{
    float s = r * r + a * a;
    return float4((r * x.x + a * x.y) / s, (r * x.y - a * x.x) / s, x.z / r, 0.0);
}

float4 KerrZamo(float3 x, float a)
{
    float4 v = float4(0.0, 0.0, 0.0, 1.0);
    if (x.x * x.x + x.y * x.y >= 1e-12)
    {
        float4 rotation = float4(-x.y, x.x, 0.0, 0.0);
        v += rotation * (-KerrDot(v, rotation, x, a) / KerrDot(rotation, rotation, x, a));
    }
    return v * rsqrt(-KerrDot(v, v, x, a));
}

// HotFlow.Plunge at the equatorial point (r, a, 0), where f = 2/r and l = (1, 0, 0) put every dot in closed form.
void KerrGasPlunge(float r, float a, out float omega, out float drift)
{
    float f = 2.0 / r;
    float tt = f - 1.0, tp = -f * a, pp = r * r + a * a * (1.0 + f);
    float tr = f, pr = -a * (1.0 + f), rr = 1.0 + f, det = tt * pp - tp * tp;
    float e = _Gas[1].y, l = _Gas[1].z;
    float wA = (-e * pp - l * tp) / det, wB = (l * tt + e * tp) / det;
    float vA = -(tr * pp - pr * tp) / det, vB = -(pr * tt - tr * tp) / det;
    float killingNorm = -e * wA + l * wB, normalNorm = vA * tr + vB * pr + rr;
    float c = -sqrt(max(0.0, (-1.0 - killingNorm) / normalNorm));
    float timeComponent = wA + vA * c;
    omega = (wB + vB * c) / timeComponent;
    drift = c / timeComponent;
}

float4 KerrGasVelocity(float3 x, float r, float a)
{
    float omega, drift = 0.0;
    if (r >= _Gas[1].x) omega = 1.0 / (r * sqrt(r) + a);
    else KerrGasPlunge(r, a, omega, drift);
    float4 v = float4(-omega * x.y, omega * x.x, 0.0, 1.0) + KerrGasRadial(x, r, a) * drift;
    float norm = KerrDot(v, v, x, a);
    return norm < 0.0 ? v * rsqrt(-norm) : KerrZamo(x, a);
}

float4 KerrGasSpatial(float4 v, float4 u, float3 x, float a)
{
    float4 s = v + u * KerrDot(v, u, x, a);
    float n = KerrDot(s, s, x, a);
    return n > 1e-24 ? s * rsqrt(n) : 0.0;
}

float4 KerrGasField(float3 x, float r, float a, float4 u)
{
    float4 b = KerrGasSpatial(KerrGasRadial(x, r, a), u, x, a) * _Gas[2].x
             + KerrGasSpatial(float4(-x.y, x.x, 0.0, 0.0), u, x, a) * _Gas[2].y
             + KerrGasSpatial(float4(0.0, 0.0, 1.0, 0.0), u, x, a) * _Gas[2].z;
    float n = KerrDot(b, b, x, a);
    return n > 1e-24 ? b * rsqrt(n) : 0.0;
}

// --- Synchrotron, ν in GHz ---

float2 KerrGasPowerLawIntegrals(float x)
{
    float lnx = log(x);
    float u = (lnx - _Gas[7].x) * _Gas[7].y * (KERR_GAS_TABLE_SIZE - 1);
    if (u <= 0.0) return exp(_GasPowerLaw.Load(int3(0, 0, 0)) + _Gas[7].z * (lnx - _Gas[7].x));
    u = min(u, KERR_GAS_TABLE_SIZE - 1.0); // also catches x = ∞ before the int cast
    int i = min((int)u, KERR_GAS_TABLE_SIZE - 2);
    return exp(lerp(_GasPowerLaw.Load(int3(i, 0, 0)), _GasPowerLaw.Load(int3(i + 1, 0, 0)), u - i));
}

// Thermal plus power-law j_I and j_Q, and the thermal j_I alone for Kirchhoff absorption.
void KerrGasEmissivity(float thermal, float nonthermal, float theta, float gauss, float sinB, float nu,
                       out float jI, out float jQ, out float thermalI)
{
    float nuB = 2.7992490e-3 * gauss;                 // e B / (2π m c) in GHz
    float x = nu / (1.5 * nuB * sinB * theta * theta);
    float pre = thermal * 2.2215233e-21 * nu / (theta * theta); // e² · 1e9 / (2√3 c)
    float c = pow(x, 1.0 / 3.0), damping = 2.5651 * exp(-1.8899 * c);
    thermalI = pre * (1.0 + 1.92 / c + 0.9977 / (c * c)) * damping;
    jQ = pre * (1.0 + 0.932 / c + 0.4998 / (c * c)) * damping;
    jI = thermalI;
    float nuP = 1.5 * nuB * sinB;
    if (nuP > 0.0)
    {
        float2 g = KerrGasPowerLawIntegrals(nu / (1e4 * nuP)); // γ_min² = 10⁴
        float p = nonthermal * 5.5538082e-16 * nuP * pow(nu / nuP, -1.25); // p = 3.5, γ_min = 100
        jI += p * g.x;
        jQ += p * g.y;
    }
}

float KerrGasPlanck(float nu, float kelvin)
{
    float x = 0.047992431 * nu / kelvin;
    // e^x − 1 loses float digits below x ≈ 0.01; the series is good to 4e-8 there.
    float expm1 = x < 1e-2 ? x * (1.0 + x * (0.5 + x / 6.0)) : exp(x) - 1.0;
    return 1.4744995e-20 * nu * nu * nu / expm1;
}

// --- Walker–Penrose ---

float4 KerrGasPerpendicular(float4 u, float4 k, float4 b, float3 x, float a)
{
    float4 p = KerrLower(u, x, a), q = KerrLower(k, x, a), s = KerrLower(b, x, a);
    return float4(-dot(p.wyz, cross(q.wyz, s.wyz)), dot(p.wxz, cross(q.wxz, s.wxz)),
                  -dot(p.wxy, cross(q.wxy, s.wxy)), dot(p.xyz, cross(q.xyz, s.xyz)));
}

// (κ1, κ2) of a ray with tangent k at x carrying polarization f.
float2 KerrWalkerPenrose(float3 x, float4 k, float4 f, float a)
{
    float r = KerrRadius(x, a), r2a2 = r * r + a * a;
    float rho2 = x.x * x.x + x.y * x.y;
    // On the spin axis φ is undefined; step off it along φ = 0, as Core does (further, for float).
    if (rho2 < 1e-10 * r2a2)
    {
        x = float3(1e-5 * r, 1e-5 * a, x.z);
        rho2 = 1e-10 * r2a2;
    }
    float z = x.z, den = r * r * r * r + a * a * z * z;
    float3 gradR = float3(x.x * r * r * r, x.y * r * r * r, z * r * r2a2) / den;
    float3 sinGradTheta = float3(z * x.x, z * x.y, -r * r * rho2 / r2a2) * (r / den);
    float3 gradPhi = float3(-x.y / rho2, x.x / rho2, 0.0) + gradR * (a / r2a2);
    float kr = dot(gradR, k.xyz), ks = dot(sinGradTheta, k.xyz), kp = dot(gradPhi, k.xyz);
    float fr = dot(gradR, f.xyz), fs = dot(sinGradTheta, f.xyz), fp = dot(gradPhi, f.xyz);
    float cosTheta = z / r, sin2 = rho2 / r2a2;
    float A = (k.w * fr - kr * f.w) + a * sin2 * (kr * fp - kp * fr);
    float B = r2a2 * (kp * fs - ks * fp) - a * (k.w * fs - ks * f.w) - a * (kr * fs - ks * fr);
    return float2(A * r - B * a * cosTheta, -(A * a * cosTheta + B * r));
}

// (cos 2χ, sin 2χ) on the pixel's screen, through the inverse map WalkerPenrose.Screen holds.
float2 KerrGasPolarization(float4 screen, float2 kappa)
{
    float right = screen.x * kappa.x + screen.y * kappa.y, up = screen.z * kappa.x + screen.w * kappa.y;
    float norm = right * right + up * up;
    return norm > 0.0 ? float2(up * up - right * right, -2.0 * right * up) / norm : 0.0;
}

// --- HotFlowRay ---

struct KerrGasSample
{
    float4 radio;            // g² j_I, g² j_Q cos 2χ, g² j_Q sin 2χ, α / g at 230 GHz
    float3 visible;          // g² j_I at the visible frequencies
    float3 visibleAbsorption;
};

struct KerrGasRay
{
    float4 screen;           // polarization view only
    float3 stokes;           // I, Q, U at 230 GHz
    float radioTransmittance;
    float3 visible;          // I at the visible frequencies
    KerrGasSample last;
};

KerrGasSample KerrGasEmit(float4 screen, float3 x, float4 k, float r, float a, float time)
{
    KerrGasSample s = (KerrGasSample)0;
    if (r >= KERR_GAS_OUTER_RADIUS || r <= KerrHorizonRadius(a) + KERR_GAS_HORIZON_MARGIN) return s;
    float thermal = KerrGasProfile(x, r, a, _Gas[0].x, -1.1);
    float nonthermal = KerrGasProfile(x, r, a, _Gas[0].y, _Gas[0].z);
    if (thermal <= 0.0 && nonthermal <= 0.0) return s;

    float4 u = KerrGasVelocity(x, r, a);
    float4 b = KerrGasField(x, r, a, u);
    float shift = KerrDot(k, u, x, a); // ν in the gas over ν at the camera, 1/g
    float cosB = KerrDot(k, b, x, a) / shift, sinB = sqrt(max(0.0, 1.0 - cosB * cosB));
    float kelvin = _Gas[0].w * pow(r, -0.84), theta = kelvin / 5.9298e9;
    float gauss = sqrt(6.2969146e-4 * thermal / r); // B²/8π = n m_p c² / (6 r β), β = 10
    // The streaks thin and thicken the electrons where they were when this light left; the field stays smooth.
    float streaks = KerrSwirl(_GasSwirl, KERR_SWIRL_GAS_COARSENESS, r, atan2(x.y, x.x), x.z, time, a, false);
    thermal *= streaks;
    nonthermal *= streaks;
    float g2 = 1.0 / (shift * shift);
    float jI, jQ, thermalI;

    if (_GasView == KERR_GAS_EYE)
    {
        [unroll]
        for (int c = 0; c < 3; c++)
        {
            float nu = _Gas[3][c] * shift;
            KerrGasEmissivity(thermal, nonthermal, theta, gauss, sinB, nu, jI, jQ, thermalI);
            s.visible[c] = g2 * jI;
            s.visibleAbsorption[c] = thermalI / KerrGasPlanck(nu, kelvin) * shift;
        }
        return s;
    }

    float nuRadio = _Gas[3].w * shift;
    KerrGasEmissivity(thermal, nonthermal, theta, gauss, sinB, nuRadio, jI, jQ, thermalI);
    s.radio = float4(g2 * jI, 0.0, 0.0, thermalI / KerrGasPlanck(nuRadio, kelvin) * shift);
    if (_GasView == KERR_GAS_POLARIZATION && jQ > 0.0)
    {
        float4 f = KerrGasPerpendicular(u, k, b, x, a);
        s.radio.yz = g2 * jQ * KerrGasPolarization(screen, KerrWalkerPenrose(x, k, f, a));
    }
    return s;
}

float KerrSelfShielding(float depth)
{
    // (1 − e^−τ)/τ cancels in float below τ ≈ 0.1; four series terms are good to 1e-6 there.
    return depth > 0.1 ? (1.0 - exp(-depth)) / depth : 1.0 - depth * (0.5 - depth * (1.0 / 6.0 - depth / 24.0));
}

// One point of the ray: caps the next step inside the flow, then adds the trapezoid over the step just taken
// (h = 0 at the camera only samples). transmittance is shared with the disk and sky in the eye view.
void KerrGasStep(inout KerrGasRay g, float3 x, float time, float4 p, float a, float h, inout float next, inout float transmittance)
{
    float r = KerrRadius(x, a);
    float4 k = KerrRaise(p, x, a);
    next = min(next, max(KERR_GAS_STEP_FRACTION * r, r - KERR_GAS_OUTER_RADIUS) / length(k.xyz));
    KerrGasSample s = KerrGasEmit(g.screen, x, k, r, a, time);
    if (h > 0.0)
    {
        float path = h * _Gas[1].w; // cm
        if (_GasView == KERR_GAS_EYE)
        {
            float3 depth = 0.5 * (g.last.visibleAbsorption + s.visibleAbsorption) * path;
            float3 shielding = float3(KerrSelfShielding(depth.x), KerrSelfShielding(depth.y), KerrSelfShielding(depth.z));
            g.visible += transmittance * 0.5 * (g.last.visible + s.visible) * path * shielding;
            // shortcut: one grey transmittance, from the green sample, shared with disk and sky; thermal electrons this
            // cool barely absorb visible light. Keep three if a flow ever turns optically thick in the visible.
            transmittance *= exp(-depth.y);
        }
        else
        {
            float depth = 0.5 * (g.last.radio.w + s.radio.w) * path;
            g.stokes += g.radioTransmittance * 0.5 * (g.last.radio.xyz + s.radio.xyz) * path * KerrSelfShielding(depth);
            g.radioTransmittance *= exp(-depth);
        }
    }
    g.last = s;
}

// Linear sRGB in cd/m² of the visible samples: the VisibleBand matrix.
float3 KerrGasRgb(float3 visible)
{
    return float3(dot(_Gas[4].xyz, visible), dot(_Gas[5].xyz, visible), dot(_Gas[6].xyz, visible));
}

// --- False-colour views ---

// matplotlib's afmhot, the EHT's colour map: black through red and orange to white at x = 1.
float3 KerrAfmhot(float x)
{
    return saturate(float3(2.0 * x, 2.0 * x - 0.5, 2.0 * x - 1.0));
}

// Hue is the EVPA, red for north and once round the wheel per π; brightness is the polarized intensity, white at 1.
float3 KerrEvpaColour(float2 qu)
{
    float hue = frac(atan2(qu.y, qu.x) / 6.2831853 + 1.0); // χ / π with χ = ½ atan2(U, Q)
    float3 rgb = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return rgb * saturate(length(qu));
}

#endif
