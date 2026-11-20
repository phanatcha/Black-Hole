using System;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// Elliptic integrals in double precision for the analytic Kerr checks. Carlson's symmetric forms use the
    /// duplication algorithms of Carlson (1995), Numer. Algorithms 10, 13–26; the Legendre forms are built on them
    /// and accept any real amplitude, which the Kerr solution needs once a ray has orbited more than once.
    /// Parameter convention: m = k², so F(φ|m) = ∫₀^φ dθ / √(1 − m sin²θ). Every m &lt; 1 is valid, negative included.
    /// </summary>
    internal static class Elliptic
    {
        // Carlson's r: truncation error of the final series is about r, so this sits at double rounding level.
        const double R = 1e-16;
        const int MaxNewtonIterations = 60;

        /// <summary>R_F(x, y, z) for x, y, z ≥ 0 with at most one zero.</summary>
        public static double RF(double x, double y, double z)
        {
            double a0 = (x + y + z) / 3, a = a0, x0 = x, y0 = y, scale = 1;
            double q = Math.Pow(3 * R, -1.0 / 6) * Math.Max(Math.Abs(a0 - x), Math.Max(Math.Abs(a0 - y), Math.Abs(a0 - z)));
            while (scale * q >= Math.Abs(a))
            {
                double sx = Math.Sqrt(x), sy = Math.Sqrt(y), sz = Math.Sqrt(z);
                double lambda = sx * sy + sx * sz + sy * sz;
                x = (x + lambda) / 4; y = (y + lambda) / 4; z = (z + lambda) / 4; a = (a + lambda) / 4;
                scale /= 4;
            }
            double X = (a0 - x0) * scale / a, Y = (a0 - y0) * scale / a, Z = -(X + Y);
            double e2 = X * Y - Z * Z, e3 = X * Y * Z;
            return (1 - e2 / 10 + e3 / 14 + e2 * e2 / 24 - 3 * e2 * e3 / 44) / Math.Sqrt(a);
        }

        /// <summary>R_C(x, y) = R_F(x, y, y) for x ≥ 0, y &gt; 0.</summary>
        public static double RC(double x, double y)
        {
            double a0 = (x + 2 * y) / 3, a = a0, y0 = y, scale = 1;
            double q = Math.Pow(3 * R, -1.0 / 8) * Math.Abs(a0 - x);
            while (scale * q >= Math.Abs(a))
            {
                double lambda = 2 * Math.Sqrt(x) * Math.Sqrt(y) + y;
                x = (x + lambda) / 4; y = (y + lambda) / 4; a = (a + lambda) / 4;
                scale /= 4;
            }
            double s = (y0 - a0) * scale / a;
            return (1 + s * s * (3.0 / 10 + s * (1.0 / 7 + s * (3.0 / 8 + s * (9.0 / 22 + s * (159.0 / 208 + s * 9.0 / 8))))))
                   / Math.Sqrt(a);
        }

        /// <summary>R_J(x, y, z, p) for x, y, z ≥ 0 with at most one zero, and p &gt; 0.</summary>
        public static double RJ(double x, double y, double z, double p)
        {
            double a0 = (x + y + z + 2 * p) / 5, a = a0, x0 = x, y0 = y, z0 = z, scale = 1;
            double delta = (p - x) * (p - y) * (p - z);
            double q = Math.Pow(R / 4, -1.0 / 6) * Math.Max(Math.Max(Math.Abs(a0 - x), Math.Abs(a0 - y)),
                                                            Math.Max(Math.Abs(a0 - z), Math.Abs(a0 - p)));
            double sum = 0;
            while (scale * q >= Math.Abs(a))
            {
                double sx = Math.Sqrt(x), sy = Math.Sqrt(y), sz = Math.Sqrt(z), sp = Math.Sqrt(p);
                double lambda = sx * sy + sx * sz + sy * sz;
                double d = (sp + sx) * (sp + sy) * (sp + sz);
                double e = delta * scale * scale * scale / (d * d);
                sum += scale * RC(1, 1 + e) / d;
                x = (x + lambda) / 4; y = (y + lambda) / 4; z = (z + lambda) / 4; p = (p + lambda) / 4; a = (a + lambda) / 4;
                scale /= 4;
            }
            double X = (a0 - x0) * scale / a, Y = (a0 - y0) * scale / a, Z = (a0 - z0) * scale / a, P = -(X + Y + Z) / 2;
            double e2 = X * Y + X * Z + Y * Z - 3 * P * P;
            double e3 = X * Y * Z + 2 * e2 * P + 4 * P * P * P;
            double e4 = (2 * X * Y * Z + e2 * P + 3 * P * P * P) * P;
            double e5 = X * Y * Z * P * P;
            double series = 1 - 3 * e2 / 14 + e3 / 6 + 9 * e2 * e2 / 88 - 3 * e4 / 22 - 9 * e2 * e3 / 52 + 3 * e5 / 26;
            return scale * series / (a * Math.Sqrt(a)) + 6 * sum;
        }

        /// <summary>Complete integral of the first kind, K(m) = F(π/2 | m).</summary>
        public static double K(double m) => RF(0, 1 - m, 1);

        /// <summary>F(φ | m) for any real φ: the integrand is π-periodic, so whole half-turns add 2K.</summary>
        public static double F(double phi, double m)
        {
            double turns = Math.Round(phi / Math.PI), r = phi - turns * Math.PI;
            double s = Math.Sin(r), c = Math.Cos(r);
            return s * RF(c * c, 1 - m * s * s, 1) + 2 * turns * K(m);
        }

        /// <summary>Π(n; φ | m) = ∫₀^φ dθ / ((1 − n sin²θ) √(1 − m sin²θ)) for any real φ and n &lt; 1.</summary>
        public static double Pi(double n, double phi, double m)
        {
            double turns = Math.Round(phi / Math.PI), r = phi - turns * Math.PI;
            double s = Math.Sin(r), c = Math.Cos(r), s2 = s * s;
            double complete = turns == 0 ? 0 : 2 * turns * (K(m) + n / 3 * RJ(0, 1 - m, 1, 1 - n));
            return s * RF(c * c, 1 - m * s2, 1) + n / 3 * s * s2 * RJ(c * c, 1 - m * s2, 1, 1 - n * s2) + complete;
        }

        /// <summary>Jacobi amplitude am(u | m): the φ with F(φ | m) = u. Newton on the reduced half-period.</summary>
        public static double Am(double u, double m)
        {
            double k = K(m), turns = Math.Round(u / (2 * k)), target = u - turns * 2 * k;
            double phi = target / k * (Math.PI / 2);
            for (int i = 0; i < MaxNewtonIterations; i++)
            {
                double s = Math.Sin(phi);
                double step = (F(phi, m) - target) * Math.Sqrt(1 - m * s * s);
                phi = Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, phi - step));
                if (Math.Abs(step) < 1e-15) break;
            }
            return phi + turns * Math.PI;
        }
    }
}
