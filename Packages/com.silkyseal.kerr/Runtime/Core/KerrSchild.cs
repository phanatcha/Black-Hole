using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Kerr metric in Cartesian Kerr-Schild coordinates, G = c = M = 1, spin along +z:
    /// g_μν = η_μν + f l_μ l_ν with η = diag(−1, 1, 1, 1).
    /// Kerr-Schild (ingoing) is regular across the future horizon, which the plunge needs.
    /// Every method is O(1) and allocation-free; the 4×4 metric is never built.
    /// </summary>
    public static class KerrSchild
    {
        /// <summary>Boyer-Lindquist r of a point: the positive root of r⁴ − (R² − a²) r² − a² z² = 0.</summary>
        public static double Radius(Vector3d x, double a)
        {
            double w = Vector3d.Dot(x, x) - a * a;
            // shortcut: 0.5w + sqrt cancels badly when w < 0 (only below the inner horizon); use the conjugate form a²z²/(sqrt − 0.5w) for the Phase 6 plunge.
            return Math.Sqrt(0.5 * w + Math.Sqrt(0.25 * w * w + a * a * x.Z * x.Z));
        }

        /// <summary>Outer event horizon r₊ = 1 + √(1 − a²).</summary>
        public static double HorizonRadius(double a) => 1.0 + Math.Sqrt(1.0 - a * a);

        /// <summary>Metric inner product g(u, v) of two vectors (upper indices) at x.</summary>
        public static double Dot(Vector4d u, Vector4d v, Vector3d x, double a)
        {
            Geometry(x, a, out _, out double f, out var l);
            double lu = u.T + Vector3d.Dot(l, u.Spatial);
            double lv = v.T + Vector3d.Dot(l, v.Spatial);
            return -u.T * v.T + Vector3d.Dot(u.Spatial, v.Spatial) + f * lu * lv;
        }

        /// <summary>v_μ = g_μν v^ν.</summary>
        public static Vector4d Lower(Vector4d v, Vector3d x, double a)
        {
            Geometry(x, a, out _, out double f, out var l);
            double flv = f * (v.T + Vector3d.Dot(l, v.Spatial));
            return new Vector4d(-v.T + flv, v.Spatial + l * flv);
        }

        /// <summary>p^μ = g^μν p_ν, using g^μν = η^μν − f l^μ l^ν with l^μ = (−1, l_x, l_y, l_z).</summary>
        public static Vector4d Raise(Vector4d p, Vector3d x, double a)
        {
            Geometry(x, a, out _, out double f, out var l);
            double flp = f * (-p.T + Vector3d.Dot(l, p.Spatial));
            return new Vector4d(-p.T + flp, p.Spatial - l * flp);
        }

        /// <summary>H = ½ g^μν p_μ p_ν. Zero for light; conserved along every geodesic.</summary>
        public static double Hamiltonian(Vector3d x, Vector4d p, double a)
        {
            Geometry(x, a, out _, out double f, out var l);
            double lp = -p.T + Vector3d.Dot(l, p.Spatial);
            return 0.5 * (-p.T * p.T + Vector3d.Dot(p.Spatial, p.Spatial) - f * lp * lp);
        }

        /// <summary>
        /// ∂H/∂x^i at fixed p_μ: the force term dp_i/dλ = −∂_i H.
        /// Analytic, because finite differences would cost 6 extra metric evaluations per stage on the GPU.
        /// </summary>
        public static void HamiltonianGradient(Vector3d x, Vector4d p, double a, out Vector3d dHdx)
        {
            Geometry(x, a, out double r, out double f, out var l);
            double r2 = r * r, a2 = a * a, s = r2 + a2;
            double q = r2 * r2 + a2 * x.Z * x.Z;
            // ∇r by implicit differentiation of the quartic that defines r.
            var dr = new Vector3d(x.X * r2 * r, x.Y * r2 * r, x.Z * r * s) / q;
            double lp = -p.T + Vector3d.Dot(l, p.Spatial);

            // l_x p_x + l_y p_y = A / s; differentiate numerator and denominator separately.
            double A = p.X * (r * x.X + a * x.Y) + p.Y * (r * x.Y - a * x.X);
            var dA = dr * (p.X * x.X + p.Y * x.Y) + new Vector3d(r * p.X - a * p.Y, a * p.X + r * p.Y, 0);
            var dlp = dA / s - dr * (2 * r * A / (s * s))
                    + new Vector3d(0, 0, p.Z / r) - dr * (p.Z * x.Z / r2);

            var df = dr * (6 * r2 / q - 8 * r2 * r2 * r2 / (q * q))
                   - new Vector3d(0, 0, 4 * r2 * r * a2 * x.Z / (q * q));

            dHdx = df * (-0.5 * lp * lp) - dlp * (f * lp);
        }

        // r, f = 2r³/(r⁴ + a²z²) and the spatial part of l_μ (l_t = 1).
        static void Geometry(Vector3d x, double a, out double r, out double f, out Vector3d l)
        {
            r = Radius(x, a);
            double r2 = r * r, a2 = a * a;
            f = 2.0 * r2 * r / (r2 * r2 + a2 * x.Z * x.Z);
            double s = r2 + a2;
            l = new Vector3d((r * x.X + a * x.Y) / s, (r * x.Y - a * x.X) / s, x.Z / r);
        }
    }
}
