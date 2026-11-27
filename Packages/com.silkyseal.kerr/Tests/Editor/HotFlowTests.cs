using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class HotFlowTests
    {
        static readonly Vector3d Toroidal = new Vector3d(0, 1, 0);

        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(0.9)]
        public void AngularMomentum_AtTheIscoMatchesMummeryBalbus(double a)
        {
            // Mummery & Balbus (2022) eq. 7: J = 2√3 (1 − 2a / (3√r_I)) at the ISCO only.
            double r = CircularOrbit.IscoRadius(a);
            Assert.AreEqual(2 * Math.Sqrt(3) * (1 - 2 * a / (3 * Math.Sqrt(r))), CircularOrbit.SpecificAngularMomentum(r, a), 1e-12);
        }

        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(0.9)]
        public void Plunge_MatchesMummeryBalbusOnTheEquator(double a)
        {
            var flow = HotFlow.SgrA(a, Toroidal);
            double rI = CircularOrbit.IscoRadius(a), rH = KerrSchild.HorizonRadius(a);
            foreach (double fraction in new[] { 0.99, 0.9, 0.75, 0.6 })
            {
                double r = Math.Max(fraction * rI, rH + 0.05);
                var x = new Vector3d(r * 0.6, r * 0.8, 0); // Kerr-Schild x, y at z = 0 lie on radius √(r² + a²)
                x = x * (Math.Sqrt(r * r + a * a) / r);
                Assert.AreEqual(r, KerrSchild.Radius(x, a), 1e-12);

                // Eq. 13: U^r = −√(2 / 3r_I) (r_I / r − 1)^(3/2), with the ISCO's energy and angular momentum.
                double expected = -Math.Sqrt(2 / (3 * rI)) * Math.Pow(rI / r - 1, 1.5);
                Assert.AreEqual(expected, RadialVelocity(flow.Velocity(x), x, a), 1e-9, $"r = {r}");
            }
        }

        [TestCase(0.0)]
        [TestCase(0.6)]
        [TestCase(0.998)]
        public void Velocity_IsAUnitFutureTimelikeVectorEverywhere(double a)
        {
            var flow = HotFlow.SgrA(a, Toroidal);
            double rH = KerrSchild.HorizonRadius(a), rI = CircularOrbit.IscoRadius(a);
            foreach (double r in new[] { rH + 0.01, rH + 0.3, 0.5 * (rH + rI), rI, rI * 1.01, 10, 40 })
            foreach (double polar in new[] { 1.0, 20.0, 60.0, 89.0, 90.0, 120.0, 179.0 })
            {
                var x = PointAt(r, polar * Math.PI / 180, 0.3, a);
                var u = flow.Velocity(x);
                Assert.AreEqual(-1, KerrSchild.Dot(u, u, x, a), 1e-9, $"r = {r}, θ = {polar}");
                Assert.Greater(u.T, 0);
                if (r < rI) Assert.LessOrEqual(RadialVelocity(u, x, a), 1e-12, "gas inside the ISCO falls in");
            }
        }

        [TestCase(0.0)]
        [TestCase(0.9)]
        public void Velocity_ChangesSmoothlyOverAShellInsideTheIsco(double a)
        {
            // Gas off the equator must not outrun the plunge it is modelled on: its speed relative to the ZAMO,
            // as a Lorentz factor, stays modest and changes little from one degree of latitude to the next.
            var flow = HotFlow.SgrA(a, Toroidal);
            double r = 0.5 * (KerrSchild.HorizonRadius(a) + CircularOrbit.IscoRadius(a)), previous = double.NaN;
            for (double polar = 2; polar <= 178; polar += 1)
            {
                var x = PointAt(r, polar * Math.PI / 180, 0.7, a);
                double gamma = -KerrSchild.Dot(flow.Velocity(x), ObserverFrame.Zamo(x, a), x, a);
                Assert.Less(gamma, 5, $"θ = {polar}");
                if (!double.IsNaN(previous)) Assert.AreEqual(previous, gamma, 0.05 * previous, $"θ = {polar}");
                previous = gamma;
            }
        }

        [TestCase(0.0)]
        [TestCase(0.9)]
        public void Velocity_IsTheCircularOrbitOutsideTheIsco(double a)
        {
            var flow = HotFlow.SgrA(a, Toroidal);
            double r = 2 * CircularOrbit.IscoRadius(a);
            var x = new Vector3d(Math.Sqrt(r * r + a * a), 0, 0);
            var u = flow.Velocity(x);
            var expected = CircularOrbit.Velocity(x, a);
            Assert.AreEqual(expected.T, u.T, 1e-12);
            Assert.AreEqual(expected.Y, u.Y, 1e-12);
        }

        [Test]
        public void Field_IsAUnitVectorTheGasSeesAsPurelySpatial()
        {
            const double a = 0.6;
            var flow = HotFlow.SgrA(a, new Vector3d(0.5, 1, 0.3));
            foreach (double r in new[] { 2.0, 5.0, 20.0 })
            {
                var x = PointAt(r, 1.1, 2.0, a);
                var u = flow.Velocity(x);
                var b = flow.FieldDirection(x, u);
                Assert.AreEqual(0, KerrSchild.Dot(b, u, x, a), 1e-12);
                Assert.AreEqual(1, KerrSchild.Dot(b, b, x, a), 1e-12);
            }
        }

        [Test]
        public void SgrA_InterpolatesTheBroderickLoebFits()
        {
            // Broderick & Loeb (2006), Table 1, rows a = 0 and 0.5.
            var row = HotFlow.SgrA(0.5, Toroidal);
            Assert.AreEqual(3e7, row.ThermalDensityScale, 1e-6);
            Assert.AreEqual(1.4e11, row.TemperatureScale, 1);
            Assert.AreEqual(5e4, row.NonthermalDensityScale, 1e-9);
            Assert.AreEqual(-2.8, row.NonthermalSlope, 1e-12);
            var between = HotFlow.SgrA(0.25, Toroidal);
            Assert.AreEqual(1.55e11, between.TemperatureScale, 1);
            Assert.AreEqual(-2.85, between.NonthermalSlope, 1e-12);
        }

        [Test]
        public void Density_VanishesOnTheSpinAxis()
        {
            var flow = HotFlow.SgrA(0.6, Toroidal);
            Assert.AreEqual(0, flow.ThermalDensity(new Vector3d(0, 0, 5), 5));
            Assert.AreEqual(0, flow.NonthermalDensity(new Vector3d(0, 0, 5), 5));
        }

        // Kerr-Schild point with Boyer–Lindquist r, polar angle θ and Kerr-Schild azimuth φ.
        internal static Vector3d PointAt(double r, double theta, double phi, double a)
        {
            double s = Math.Sin(theta);
            return new Vector3d(s * (r * Math.Cos(phi) - a * Math.Sin(phi)), s * (r * Math.Sin(phi) + a * Math.Cos(phi)), r * Math.Cos(theta));
        }

        // u^r = dr/dτ, the same in both charts: the change of r along u, by central differences.
        static double RadialVelocity(Vector4d u, Vector3d x, double a)
        {
            const double h = 1e-6;
            var s = u.Spatial;
            return (KerrSchild.Radius(x + s * h, a) - KerrSchild.Radius(x - s * h, a)) / (2 * h);
        }
    }
}
