using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// One adaptive Cash–Karp RK4(5) step of Hamilton's equations for a null geodesic:
    /// dx^i/dλ = g^{iν} p_ν, dp_i/dλ = −∂_i H. p_t is a constant of motion (stationary metric), so the
    /// state is the six numbers (x^i, p_i); the coordinate time t rides along without steering the step size.
    /// O(1), allocation-free; the GPU port mirrors it line for line.
    /// </summary>
    public static class GeodesicIntegrator
    {
        const double MaxGrowth = 5.0, MinShrink = 0.1, Safety = 0.9;

        // Cash–Karp tableau (Numerical Recipes, 3rd ed., §17.2).
        const double B21 = 1.0 / 5;
        const double B31 = 3.0 / 40, B32 = 9.0 / 40;
        const double B41 = 3.0 / 10, B42 = -9.0 / 10, B43 = 6.0 / 5;
        const double B51 = -11.0 / 54, B52 = 5.0 / 2, B53 = -70.0 / 27, B54 = 35.0 / 27;
        const double B61 = 1631.0 / 55296, B62 = 175.0 / 512, B63 = 575.0 / 13824, B64 = 44275.0 / 110592, B65 = 253.0 / 4096;
        const double C1 = 37.0 / 378, C3 = 250.0 / 621, C4 = 125.0 / 594, C6 = 512.0 / 1771;
        const double E1 = C1 - 2825.0 / 27648, E3 = C3 - 18575.0 / 48384, E4 = C4 - 13525.0 / 55296,
                     E5 = -277.0 / 14336, E6 = C6 - 1.0 / 4;

        /// <summary>
        /// Attempts one step of size h. On success advances x, t and p and returns true; either way h becomes
        /// the size to try next. Error per component is |err| / (tolerance · (1 + |y|)).
        /// </summary>
        public static bool Step(ref Vector3d x, ref double t, ref Vector3d p, double pt, double a, ref double h, double tolerance)
        {
            Derivatives(x, p, pt, a, out var kx1, out double kt1, out var kp1);
            Derivatives(x + h * B21 * kx1, p + h * B21 * kp1, pt, a, out var kx2, out _, out var kp2);
            Derivatives(x + h * (B31 * kx1 + B32 * kx2), p + h * (B31 * kp1 + B32 * kp2), pt, a, out var kx3, out double kt3, out var kp3);
            Derivatives(x + h * (B41 * kx1 + B42 * kx2 + B43 * kx3),
                        p + h * (B41 * kp1 + B42 * kp2 + B43 * kp3), pt, a, out var kx4, out double kt4, out var kp4);
            Derivatives(x + h * (B51 * kx1 + B52 * kx2 + B53 * kx3 + B54 * kx4),
                        p + h * (B51 * kp1 + B52 * kp2 + B53 * kp3 + B54 * kp4), pt, a, out var kx5, out _, out var kp5);
            Derivatives(x + h * (B61 * kx1 + B62 * kx2 + B63 * kx3 + B64 * kx4 + B65 * kx5),
                        p + h * (B61 * kp1 + B62 * kp2 + B63 * kp3 + B64 * kp4 + B65 * kp5), pt, a, out var kx6, out double kt6, out var kp6);

            var ex = h * (E1 * kx1 + E3 * kx3 + E4 * kx4 + E5 * kx5 + E6 * kx6);
            var ep = h * (E1 * kp1 + E3 * kp3 + E4 * kp4 + E5 * kp5 + E6 * kp6);
            double err = Math.Max(ErrorRatio(ex, x), ErrorRatio(ep, p)) / tolerance;

            // NaN fails both comparisons and lands in the reject branch, so a bad state never advances.
            if (err <= 1.0)
            {
                x += h * (C1 * kx1 + C3 * kx3 + C4 * kx4 + C6 * kx6);
                t += h * (C1 * kt1 + C3 * kt3 + C4 * kt4 + C6 * kt6);
                p += h * (C1 * kp1 + C3 * kp3 + C4 * kp4 + C6 * kp6);
                h *= err > 0 ? Math.Min(MaxGrowth, Safety * Math.Pow(err, -0.2)) : MaxGrowth;
                return true;
            }
            h *= double.IsNaN(err) ? MinShrink : Math.Max(MinShrink, Safety * Math.Pow(err, -0.25));
            return false;
        }

        static void Derivatives(Vector3d x, Vector3d p, double pt, double a, out Vector3d dx, out double dt, out Vector3d dp)
        {
            var p4 = new Vector4d(pt, p);
            var k = KerrSchild.Raise(p4, x, a);
            dx = k.Spatial;
            dt = k.T;
            KerrSchild.HamiltonianGradient(x, p4, a, out var dHdx);
            dp = -dHdx;
        }

        static double ErrorRatio(Vector3d err, Vector3d y) =>
            Math.Max(Math.Abs(err.X) / (1 + Math.Abs(y.X)),
            Math.Max(Math.Abs(err.Y) / (1 + Math.Abs(y.Y)), Math.Abs(err.Z) / (1 + Math.Abs(y.Z))));
    }
}
