using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class GeodesicTests
    {
        // Integrator tolerance is 1e-10 per step; a few thousand steps stay well under 1e-9.
        const double ConservationTolerance = 1e-9;
        // Bisection to 1e-14 in sinδ leaves the integrator error (~1e-8) as the floor; 1e-4 is the plan's bar.
        const double ShadowRelativeTolerance = 1e-4;
        const int BisectionIterations = 40;
        // Second-order deflection (15π/4)/b² and Kerr-Schild coordinate skew at D add ~0.5%.
        const double WeakFieldRelativeTolerance = 0.01;

        /// <summary>
        /// Past-directed null covector at (D, 0, 0) whose spatial part points at angle δ off −x.
        /// side = +1 gives the photon positive L_z (prograde for a > 0).
        /// </summary>
        internal static (Vector3d x0, Vector4d kLower) ShootFromFar(double a, double D, double sinDelta, int side)
        {
            var x0 = new Vector3d(D, 0, 0);
            double cosDelta = Math.Sqrt(1 - sinDelta * sinDelta);
            // The photon arrives travelling along −k, so its L_z = −D·k_y: side +1 needs k_y < 0.
            var n = new Vector4d(0, -cosDelta, -side * sinDelta, 0);
            var et = new Vector4d(1, 0, 0, 0);
            double gtt = KerrSchild.Dot(et, et, x0, a);
            double gtn = KerrSchild.Dot(et, n, x0, a);
            double gnn = KerrSchild.Dot(n, n, x0, a);
            double disc = Math.Sqrt(gtn * gtn - gtt * gnn);
            double kt = Math.Min((-gtn + disc) / gtt, (-gtn - disc) / gtt);
            return (x0, KerrSchild.Lower(new Vector4d(kt, n.Spatial), x0, a));
        }

        static double AngularMomentum(Vector3d x, Vector4d p) => x.X * p.Y - x.Y * p.X;

        // Impact parameter b = L_z / E with E = −p_t. Same ratio for k and −k.
        static double ImpactParameter(Vector3d x, Vector4d p) => AngularMomentum(x, p) / -p.T;

        [Test]
        public void NullCondition_HeldAlongRay()
        {
            var (x0, k) = ShootFromFar(0.9, 50, 0.1, +1);
            var result = RayTracer.Trace(x0, k, TraceSettings.Reference(0.9));
            Assert.AreEqual(RayStatus.Escaped, result.Status);
            Assert.Less(result.MaxAbsHamiltonian, ConservationTolerance);
        }

        [Test]
        public void AngularMomentumLz_IsConserved()
        {
            var (x0, k) = ShootFromFar(0.9, 50, 0.1, +1);
            var result = RayTracer.Trace(x0, k, TraceSettings.Reference(0.9));
            Assert.AreEqual(AngularMomentum(x0, k), AngularMomentum(result.Position, result.Momentum), ConservationTolerance);
        }

        [Test]
        public void WeakField_DeflectionMatchesFinitePathFormula()
        {
            const double D = 20000, targetB = 1000, farRadius = 30000;
            var (x0, k) = ShootFromFar(0, D, targetB / D, +1);
            var settings = TraceSettings.Reference(0);
            settings.FarRadius = farRadius;
            var result = RayTracer.Trace(x0, k, settings);

            double b = Math.Abs(ImpactParameter(x0, k));
            double R = result.Position.Length;
            double expected = 2 / b * (Math.Sqrt(D * D - b * b) / D + Math.Sqrt(R * R - b * b) / R);
            double measured = Math.Acos(Vector3d.Dot(k.Spatial.Normalized, result.SkyDirection));

            Assert.AreEqual(RayStatus.Escaped, result.Status);
            Assert.AreEqual(expected, measured, WeakFieldRelativeTolerance * expected);
        }

        [Test]
        public void ShadowEdge_Schwarzschild()
        {
            double expected = Math.Sqrt(27);
            Assert.AreEqual(expected, ShadowEdgeImpactParameter(0, +1), ShadowRelativeTolerance * expected);
            Assert.AreEqual(-expected, ShadowEdgeImpactParameter(0, -1), ShadowRelativeTolerance * expected);
        }

        [TestCase(0.6)]
        [TestCase(0.999)]
        public void ShadowEdge_KerrEquatorial(double a)
        {
            double prograde = CircularPhotonOrbitImpact(a, 2 * (1 + Math.Cos(2.0 / 3 * Math.Acos(-a))));
            double retrograde = CircularPhotonOrbitImpact(a, 2 * (1 + Math.Cos(2.0 / 3 * Math.Acos(a))));

            Assert.AreEqual(prograde, ShadowEdgeImpactParameter(a, +1), ShadowRelativeTolerance * Math.Abs(prograde));
            Assert.AreEqual(retrograde, ShadowEdgeImpactParameter(a, -1), ShadowRelativeTolerance * Math.Abs(retrograde));
        }

        // Equatorial circular photon orbit at radius r has b = −(r³ − 3r² + a²r + a²) / (a (r − 1)).
        static double CircularPhotonOrbitImpact(double a, double r) =>
            -(r * r * r - 3 * r * r + a * a * r + a * a) / (a * (r - 1));

        // Bisects sinδ between a captured (head-on) and an escaping ray and returns b at the boundary.
        static double ShadowEdgeImpactParameter(double a, int side)
        {
            const double D = 1000;
            var settings = TraceSettings.Reference(a);
            double lo = 0, hi = 0.02;
            for (int i = 0; i < BisectionIterations; i++)
            {
                double mid = 0.5 * (lo + hi);
                var (x0, k) = ShootFromFar(a, D, mid, side);
                var status = RayTracer.Trace(x0, k, settings).Status;
                Assert.AreNotEqual(RayStatus.MaxSteps, status, $"sinδ = {mid}");
                if (status == RayStatus.Captured) lo = mid; else hi = mid;
            }
            var (xb, kb) = ShootFromFar(a, D, 0.5 * (lo + hi), side);
            return ImpactParameter(xb, kb);
        }
    }
}
