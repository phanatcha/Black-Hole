using System;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// Closed-form Kerr null geodesic after Gralla &amp; Lupsasca (2020), arXiv:1910.12881, for a backward-traced ray
    /// that leaves the camera moving inward. The solution lives in Boyer–Lindquist (r, θ, φ) with Mino time s;
    /// the ends convert from and to the ingoing Kerr–Schild Cartesian coordinates of the integrator, where
    /// x + iy = (r + ia) sinθ e^{iφ̃} and φ̃ = φ + Φ(r) with dΦ/dr = a/Δ. G = c = M = 1.
    /// Test-only reference: it has no captured-ray branch and no outward-start branch.
    /// </summary>
    internal sealed class AnalyticRay
    {
        const int ResolventBisections = 200;

        public readonly double Spin;
        /// <summary>Conserved λ = L/E and η = Q/E² of the photon.</summary>
        public readonly double Lambda, Eta;
        /// <summary>Roots of the radial potential R(r), ascending; NaN unless all four are real.</summary>
        public readonly double R1 = double.NaN, R2 = double.NaN, R3 = double.NaN, R4 = double.NaN;
        /// <summary>R(r) turns the ray around outside the horizon, so it goes back out to infinity.</summary>
        public readonly bool Escapes;

        readonly double _rPlus, _rMinus, _r0, _phiTilde0;
        // R(r) = r⁴ + A r² + B r + C.
        readonly double _a, _b, _c;
        // Angular motion: cos θ = √u₊ sn(κ s + w₀ | m).
        readonly double _uPlus, _m, _kappa, _amplitude0, _w0;
        // Radial integrals reduce to Legendre form with this prefactor and parameter.
        readonly double _radialScale, _radialM;

        public static AnalyticRay FromKerrSchild(Vector3d x, Vector4d kLower, double a) => new AnalyticRay(x, kLower, a);

        /// <exception cref="ArgumentException">The ray does not start moving inward.</exception>
        AnalyticRay(Vector3d x, Vector4d k, double a)
        {
            Spin = a;
            double r = KerrSchild.Radius(x, a), u = x.Z / r, sinTheta = Math.Sqrt(1 - u * u);
            double horizonGap = Math.Sqrt(1 - a * a);
            _rPlus = 1 + horizonGap;
            _rMinus = 1 - horizonGap;
            _r0 = r;
            _phiTilde0 = Math.Atan2(x.Y, x.X) - Math.Atan2(a, r);

            // k is past-directed, so k_t = E > 0 and λ = L/E = −k_φ/k_t. BL k_θ follows from ∂x^i/∂θ at fixed r, φ̃.
            double energy = k.T;
            Lambda = -(x.X * k.Y - x.Y * k.X) / energy;
            double kTheta = u / sinTheta * (x.X * k.X + x.Y * k.Y) - r * sinTheta * k.Z;
            Eta = kTheta * kTheta / (energy * energy) - a * a * u * u + Lambda * Lambda * u * u / (sinTheta * sinTheta);

            // Signs of dr/ds and d(cos θ)/ds along the trace, from the contravariant k and ∇r.
            var velocity = KerrSchild.Raise(k, x, a);
            double q = r * r * r * r + a * a * x.Z * x.Z;
            double rDot = (r * r * r * (x.X * velocity.X + x.Y * velocity.Y) + r * (r * r + a * a) * x.Z * velocity.Z) / q;
            if (rDot >= 0) throw new ArgumentException("Only rays that start moving inward are supported.", nameof(k));
            double uDot = velocity.Z - u * rDot;

            _a = a * a - Eta - Lambda * Lambda;
            _b = 2 * (Eta + (Lambda - a) * (Lambda - a));
            _c = -a * a * Eta;
            if (!SolveRadialRoots(out R1, out R2, out R3, out R4)) return;
            Escapes = Eta > 0 && R4 > _rPlus;
            if (!Escapes) return;

            _radialScale = 2 / Math.Sqrt((R3 - R1) * (R4 - R2));
            _radialM = (R3 - R2) * (R4 - R1) / ((R3 - R1) * (R4 - R2));

            // u₊ in the form that stays finite as a → 0; then κ² = −u₋a² = η/u₊ and m = u₊/u₋ = −a²u₊²/η.
            _uPlus = Math.Min(1, 2 * Eta / (Math.Sqrt(_a * _a + 4 * a * a * Eta) - _a));
            _m = -a * a * _uPlus * _uPlus / Eta;
            _kappa = Math.Sqrt(Eta / _uPlus);
            _amplitude0 = Math.Asin(Math.Max(-1, Math.Min(1, u / Math.Sqrt(_uPlus))));
            if (uDot < 0) _amplitude0 = Math.PI - _amplitude0; // cn < 0 on the falling half of the oscillation
            _w0 = Elliptic.F(_amplitude0, _m);
        }

        public double RadialPotential(double r) => r * r * r * r + _a * r * r + _b * r + _c;

        /// <summary>Mino time ∫ dr/√R from the turning point R4 out to r.</summary>
        public double MinoTime(double r) => _radialScale * Elliptic.F(RadialAmplitude(r), _radialM);

        /// <summary>∫ dr / ((r − c) √R) from R4 out to r, for a pole c below R4 and away from R3.</summary>
        public double PoleIntegral(double c, double r)
        {
            double psi = RadialAmplitude(r), r3c = R3 - c, r4c = R4 - c;
            double n = (R4 - R1) * r3c / ((R3 - R1) * r4c);
            return _radialScale * (Elliptic.F(psi, _radialM) / r3c - (R4 - R3) / (r3c * r4c) * Elliptic.Pi(n, psi, _radialM));
        }

        /// <summary>Kerr–Schild position where the ray, after its turning point, is back out at radius r.</summary>
        public Vector3d PositionAt(double r)
        {
            if (!Escapes) throw new InvalidOperationException("Ray is captured; it never comes back out.");

            double a = Spin, amplitude = AmplitudeAt(r);
            double u = Math.Sqrt(_uPlus) * Math.Sin(amplitude);

            // dφ/ds = −[a(2r − aλ)/Δ + λ/sin²θ] along a past-directed trace; split a(2r − aλ)/Δ at r₊ and r₋.
            double angular = (Elliptic.Pi(_uPlus, amplitude, _m) - Elliptic.Pi(_uPlus, _amplitude0, _m)) / _kappa;
            double split = _rPlus - _rMinus;
            double weightPlus = a * (2 * _rPlus - a * Lambda) / split, weightMinus = -a * (2 * _rMinus - a * Lambda) / split;
            double radial = weightPlus * (PoleIntegral(_rPlus, _r0) + PoleIntegral(_rPlus, r))
                          + weightMinus * (PoleIntegral(_rMinus, _r0) + PoleIntegral(_rMinus, r));
            double phiTilde = _phiTilde0 - (radial + Lambda * angular) + AzimuthShift(r) - AzimuthShift(_r0);

            double sinTheta = Math.Sqrt(1 - u * u);
            return new Vector3d(sinTheta * (r * Math.Cos(phiTilde) - a * Math.Sin(phiTilde)),
                                sinTheta * (r * Math.Sin(phiTilde) + a * Math.Cos(phiTilde)),
                                r * u);
        }

        /// <summary>Times the ray has passed z = 0 by the time it is back out at r: cos θ ∝ sin(am) vanishes at am = jπ.</summary>
        public int EquatorCrossingsTo(double r) =>
            (int)(Math.Floor(AmplitudeAt(r) / Math.PI) - Math.Floor(_amplitude0 / Math.PI));

        // Jacobi amplitude of the angular motion after the full Mino time: in to R4, then out to r.
        double AmplitudeAt(double r)
        {
            if (!Escapes) throw new InvalidOperationException("Ray is captured; it never comes back out.");
            return Elliptic.Am(_w0 + _kappa * (MinoTime(_r0) + MinoTime(r)), _m);
        }

        // Φ(r) = ∫ a/Δ dr, zero at infinity.
        double AzimuthShift(double r) => Spin / (_rPlus - _rMinus) * Math.Log((r - _rPlus) / (r - _rMinus));

        // sin²ψ = (r₃ − r₁)(r − r₄) / ((r₄ − r₁)(r − r₃)) maps [R4, ∞) onto Legendre form.
        double RadialAmplitude(double r) =>
            Math.Asin(Math.Sqrt(Math.Min(1, (R3 - R1) * (r - R4) / ((R4 - R1) * (r - R3)))));

        /// <summary>
        /// Ferrari: R = (r² + y)² − (s r − B/2s)² with s² = 2y − A, where y is the resolvent root above A/2
        /// (the resolvent is −B² there and grows without bound, so bisection always brackets it).
        /// </summary>
        bool SolveRadialRoots(out double r1, out double r2, out double r3, out double r4)
        {
            r1 = r2 = r3 = r4 = double.NaN;
            double lo = _a / 2, hi = lo + 1;
            while (Resolvent(hi) < 0) hi = lo + 2 * (hi - lo);
            for (int i = 0; i < ResolventBisections && hi - lo > 0; i++)
            {
                double mid = (lo + hi) / 2;
                if (mid == lo || mid == hi) break;
                if (Resolvent(mid) < 0) lo = mid; else hi = mid;
            }
            double y = (lo + hi) / 2, s = Math.Sqrt(2 * y - _a);
            double upper = s * s / 4 - y - _b / (2 * s), lower = s * s / 4 - y + _b / (2 * s);
            if (!(s > 0) || upper < 0 || lower < 0) return false;

            var roots = new[] { -s / 2 - Math.Sqrt(lower), -s / 2 + Math.Sqrt(lower), s / 2 - Math.Sqrt(upper), s / 2 + Math.Sqrt(upper) };
            Array.Sort(roots);
            (r1, r2, r3, r4) = (roots[0], roots[1], roots[2], roots[3]);
            return true;
        }

        double Resolvent(double y) => 8 * y * y * y - 4 * _a * y * y - 8 * _c * y + 4 * _a * _c - _b * _b;
    }
}
