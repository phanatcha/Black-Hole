using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class EllipticTests
    {
        // Reference values are quoted to 14 digits in Carlson (1995), Numer. Algorithms 10, 13–26.
        const double TableTolerance = 1e-13;
        const double QuadratureTolerance = 1e-10;

        [TestCase(1.0, 2.0, 0.0, 1.3110287771461)]
        [TestCase(2.0, 3.0, 4.0, 0.58408284167715)]
        [TestCase(0.0, 0.5, 1.0, 1.8540746773013719)] // K(1/2)
        public void RF_MatchesTable(double x, double y, double z, double expected) =>
            Assert.AreEqual(expected, Elliptic.RF(x, y, z), TableTolerance);

        [TestCase(0.0, 0.25, Math.PI)]
        [TestCase(2.25, 2.0, 0.69314718055995)] // ln 2
        public void RC_MatchesTable(double x, double y, double expected) =>
            Assert.AreEqual(expected, Elliptic.RC(x, y), TableTolerance);

        [TestCase(0.0, 1.0, 2.0, 3.0, 0.77688623778582)]
        [TestCase(2.0, 3.0, 4.0, 5.0, 0.14297579667157)]
        [TestCase(0.0, 2.0, 1.0, 1.0, 1.7972103521034)]   // R_D(0, 2, 1)
        [TestCase(2.0, 3.0, 4.0, 4.0, 0.16510527294261)]  // R_D(2, 3, 4)
        public void RJ_MatchesTable(double x, double y, double z, double p, double expected) =>
            Assert.AreEqual(expected, Elliptic.RJ(x, y, z, p), TableTolerance);

        [TestCase(0.3, -3.0)]
        [TestCase(2.0, 0.7)]
        [TestCase(-7.0, -0.4)]
        public void F_MatchesQuadrature_AnyAmplitude(double phi, double m) =>
            Assert.AreEqual(Integrate(t => 1 / Math.Sqrt(1 - m * Sq(Math.Sin(t))), 0, phi), Elliptic.F(phi, m),
                            QuadratureTolerance);

        [TestCase(-2.0, 0.3, -3.0)]
        [TestCase(0.5, 2.0, 0.7)]
        [TestCase(0.9, 7.0, -0.4)]
        [TestCase(0.97, -4.5, -12.0)]
        public void Pi_MatchesQuadrature_AnyAmplitude(double n, double phi, double m) =>
            Assert.AreEqual(Integrate(t => 1 / ((1 - n * Sq(Math.Sin(t))) * Math.Sqrt(1 - m * Sq(Math.Sin(t)))), 0, phi),
                            Elliptic.Pi(n, phi, m), QuadratureTolerance);

        [Test]
        public void K_IsQuarterPeriodOfF([Values(-5.0, 0.0, 0.6)] double m) =>
            Assert.AreEqual(Elliptic.F(Math.PI / 2, m), Elliptic.K(m), TableTolerance);

        [Test]
        public void Am_InvertsF([Values(-25.0, -0.3, 0.0, 0.9)] double m,
                                [Values(-9.0, -1.0, 0.0, 0.4, 3.1, 17.0)] double u) =>
            Assert.AreEqual(u, Elliptic.F(Elliptic.Am(u, m), m), 1e-12 * Math.Max(1, Math.Abs(u)));

        static double Sq(double v) => v * v;

        /// <summary>
        /// Composite two-point Gauss–Legendre rule: O(h⁴) like Simpson, but it never evaluates the end points,
        /// so integrands that are finite only as a limit there (0/0 after a √ substitution) are fine.
        /// </summary>
        internal static double Integrate(Func<double, double> f, double from, double to, int intervals = 20000)
        {
            double h = (to - from) / intervals, offset = h / (2 * Math.Sqrt(3)), sum = 0;
            for (int i = 0; i < intervals; i++)
            {
                double mid = from + (i + 0.5) * h;
                sum += f(mid - offset) + f(mid + offset);
            }
            return sum * h / 2;
        }
    }
}
