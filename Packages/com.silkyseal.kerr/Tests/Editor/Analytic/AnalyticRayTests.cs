using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace SilkySeal.Kerr.Tests
{
    public class AnalyticRayTests
    {
        const double Spin = 0.9, FarPoint = 30;
        const double QuadratureTolerance = 1e-10;

        // Pixel rays off the equatorial plane: there η = 0 and the angular motion degenerates.
        static AnalyticRay Ray(double ndcX, double ndcY)
        {
            var frame = GpuParityTests.TestFrame(Spin);
            var k = RayTracer.PixelRay(frame, ndcX, ndcY, Math.Tan(Math.PI / 6), 16.0 / 9.0);
            return AnalyticRay.FromKerrSchild(frame.Position, k, Spin);
        }

        // An escaping ray with a radial turning point, on the prograde (+1) or retrograde (−1) side.
        static AnalyticRay Escaping(int side) => Ray(-side * 0.6, 0.4);

        [Test]
        public void Roots_ZeroTheRadialPotential([Values(-1, 1)] int side)
        {
            var ray = Escaping(side);
            Assert.IsTrue(ray.Escapes);
            Assert.That(ray.R1 < ray.R2 && ray.R2 < ray.R3 && ray.R3 < ray.R4, "roots must be real and ascending");
            foreach (double r in new[] { ray.R1, ray.R2, ray.R3, ray.R4 })
                Assert.AreEqual(0, ray.RadialPotential(r), 1e-9 * (1 + r * r * r * r));
        }

        [Test]
        public void MinoTime_MatchesQuadrature([Values(-1, 1)] int side)
        {
            var ray = Escaping(side);
            // t = r₄ + v² removes the 1/√ end-point singularity.
            double expected = EllipticTests.Integrate(
                v => 2 * v / Math.Sqrt(ray.RadialPotential(ray.R4 + v * v)), 0, Math.Sqrt(FarPoint - ray.R4));
            Assert.AreEqual(expected, ray.MinoTime(FarPoint), QuadratureTolerance);
        }

        [Test]
        public void PoleIntegral_MatchesQuadrature([Values(-1, 1)] int side, [Values(true, false)] bool outerHorizon)
        {
            var ray = Escaping(side);
            double root = Math.Sqrt(1 - Spin * Spin), c = outerHorizon ? 1 + root : 1 - root;
            double expected = EllipticTests.Integrate(
                v => 2 * v / ((ray.R4 + v * v - c) * Math.Sqrt(ray.RadialPotential(ray.R4 + v * v))),
                0, Math.Sqrt(FarPoint - ray.R4));
            Assert.AreEqual(expected, ray.PoleIntegral(c, FarPoint), QuadratureTolerance);
        }

        [Test]
        public void Escapes_IsFalse_InsideShadow()
        {
            Assert.IsFalse(Ray(0.05, 0.05).Escapes);
        }
    }

    /// <summary>
    /// Phase 2 bar: the adaptive integrator lands where the closed-form Kerr solution says it should.
    /// Compared as end positions at the integrator's own final radius, so no asymptotic error enters.
    /// </summary>
    public class AnalyticParityTests
    {
        const int Columns = 32, Rows = 18; // even, so no pixel sits exactly on the λ = 0 column
        const double VerticalFov = Math.PI / 3, Aspect = 16.0 / 9.0;
        // Radians of sky. Measured max is 1e-8 at a = 0.6 (integrator tolerance 1e-10 per step); 10× headroom.
        const double EndPointTolerance = 1e-7;

        [TestCase(0.0)]
        [TestCase(0.6)]
        [TestCase(0.999)]
        public void EndPoints_MatchGrallaLupsasca(double a)
        {
            var frame = GpuParityTests.TestFrame(a);
            var settings = TraceSettings.Reference(a);
            double tanHalf = Math.Tan(VerticalFov / 2);
            var error = new double[Columns * Rows];
            var agrees = new bool[Columns * Rows];
            var escaped = new bool[Columns * Rows];
            var crossings = new int[Columns * Rows];
            var crossingsAgree = new bool[Columns * Rows];

            Parallel.For(0, error.Length, i =>
            {
                double ndcX = (i % Columns + 0.5) / Columns * 2 - 1, ndcY = (i / Columns + 0.5) / Rows * 2 - 1;
                var k = RayTracer.PixelRay(frame, ndcX, ndcY, tanHalf, Aspect);
                var traced = RayTracer.Trace(frame.Position, k, settings);
                var ray = AnalyticRay.FromKerrSchild(frame.Position, k, a);
                escaped[i] = traced.Status == RayStatus.Escaped;
                agrees[i] = ray.Escapes == escaped[i] && traced.Status != RayStatus.MaxSteps;
                if (escaped[i] && ray.Escapes)
                {
                    var expected = ray.PositionAt(KerrSchild.Radius(traced.Position, a));
                    error[i] = (traced.Position - expected).Length / traced.Position.Length;
                    crossings[i] = traced.EquatorCrossings;
                    crossingsAgree[i] = traced.EquatorCrossings == ray.EquatorCrossingsTo(KerrSchild.Radius(traced.Position, a));
                }
            });

            int escapedCount = 0, disagreements = 0, crossingMismatches = 0, mostCrossings = 0;
            double maxError = 0;
            for (int i = 0; i < error.Length; i++)
            {
                if (escaped[i]) escapedCount++;
                if (!agrees[i]) disagreements++;
                if (escaped[i] && agrees[i] && !crossingsAgree[i]) crossingMismatches++;
                mostCrossings = Math.Max(mostCrossings, crossings[i]);
                maxError = Math.Max(maxError, error[i]);
            }
            Debug.Log($"[AnalyticParity a={a}] escaped {escapedCount}/{error.Length}, disagreements {disagreements}, " +
                      $"max end-point error {maxError:E2} rad, crossing mismatches {crossingMismatches}, most crossings {mostCrossings}");

            Assert.Greater(escapedCount, error.Length / 2, "most rays should escape");
            Assert.AreEqual(0, disagreements, "capture/escape must match the root structure");
            Assert.Less(maxError, EndPointTolerance);
            Assert.AreEqual(0, crossingMismatches, "equator crossings must match the analytic θ oscillation");
            Assert.GreaterOrEqual(mostCrossings, 2, "grid should include rays that wrap past the hole");
        }
    }
}
