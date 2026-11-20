using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace SilkySeal.Kerr.Tests
{
    public class PhotonShellTests
    {
        [Test]
        public void Lyapunov_IsPi_ForSchwarzschild() =>
            // Spherical orbits collapse onto r = 3 as a → 0; the polar one stays at r̃ = 3 exactly.
            Assert.AreEqual(Math.PI, PhotonShell.Lyapunov(3, 1e-4), 1e-6);

        // Johnson et al. (2020), Sci. Adv. 6, eaaz1310, eq. 4: γ = 4 r̃ √χ K(u₊/u₋) / √(−u₋a²), χ = 1 − Δ/(r̃(r̃ − 1)²).
        [Test]
        public void Lyapunov_MatchesJohnsonClosedForm([Values(0.2, 0.5, 0.8)] double shellFraction)
        {
            const double a = 0.94;
            double inner = 2 * (1 + Math.Cos(2.0 / 3 * Math.Acos(-a))), outer = 2 * (1 + Math.Cos(2.0 / 3 * Math.Acos(a)));
            double r = inner + shellFraction * (outer - inner);
            double lambda = PhotonShell.Lambda(r, a), eta = PhotonShell.Eta(r, a);
            double delta = r * r - 2 * r + a * a, chi = 1 - delta / (r * (r - 1) * (r - 1));
            double dTheta = 0.5 * (1 - (eta + lambda * lambda) / (a * a));
            double uPlus = dTheta + Math.Sqrt(dTheta * dTheta + eta / (a * a));
            double uMinus = dTheta - Math.Sqrt(dTheta * dTheta + eta / (a * a));
            double expected = 4 * r * Math.Sqrt(chi) * Elliptic.K(uPlus / uMinus) / Math.Sqrt(-uMinus * a * a);
            Assert.AreEqual(expected, PhotonShell.Lyapunov(r, a), 1e-10 * expected);
        }
    }

    /// <summary>
    /// Phase 2 bar: successive photon subrings shrink by e^{−γ}. Subring n of the sky image is the band of screen
    /// where rays cross the equatorial plane n + 1 times. Its edges are found by bisection on real camera rays.
    /// </summary>
    public class SubringTests
    {
        // M87*-like view: spin 0.94, 17° from the spin axis (EHT 2019 model fits).
        const double Spin = 0.94, CameraPolarDegrees = 17, CameraRadius = 100;
        const int Directions = 8;
        const double OuterTan = 0.2;            // well outside the critical curve (≈ 0.05)
        const double EdgeTolerance = 1e-12;     // bisection width in screen tangent
        // n = 1 → 2 is not yet in the asymptotic regime: the outer edge of n = 1 sits where bending is only ~90°.
        // Measured ln(w₂/w₁) + γ runs −0.28 … −0.47 around the ring (Schwarzschild face-on: −0.29); the offset itself
        // shrinks by about e^{−γ} per order, to −0.02 … −0.05 for n = 2 → 3. Bars leave ~30% headroom.
        const double FirstRatioLogTolerance = 0.6, SecondRatioLogTolerance = 0.07;

        [Test]
        public void SubringWidths_ShrinkByLyapunovFactor()
        {
            var frame = Frame();
            var settings = TraceSettings.Reference(Spin);
            var edges = new double[Directions, 4];
            var gamma = new double[Directions];

            Parallel.For(0, Directions, d =>
            {
                double angle = 2 * Math.PI * d / Directions;
                for (int k = 1; k <= 4; k++) edges[d, k - 1] = Edge(frame, settings, angle, k);
                // The innermost edge hugs the critical curve, where R(r) has a near-double root at the orbit radius.
                var ray = AnalyticRay.FromKerrSchild(frame.Position, Ray(frame, angle, edges[d, 3]), Spin);
                gamma[d] = PhotonShell.Lyapunov((ray.R3 + ray.R4) / 2, Spin);
            });

            for (int d = 0; d < Directions; d++)
            {
                double w1 = edges[d, 0] - edges[d, 1], w2 = edges[d, 1] - edges[d, 2], w3 = edges[d, 2] - edges[d, 3];
                double first = Math.Log(w2 / w1) + gamma[d], second = Math.Log(w3 / w2) + gamma[d];
                Debug.Log($"[Subrings {360.0 * d / Directions:F0}°] γ = {gamma[d]:F4}, e^-γ = {Math.Exp(-gamma[d]):F5}, " +
                          $"widths (M) n1 {w1 * CameraRadius:E3}, n2 {w2 * CameraRadius:E3}, n3 {w3 * CameraRadius:E3}, " +
                          $"w2/w1 {w2 / w1:F5}, w3/w2 {w3 / w2:F5}, ln-offsets {first:+0.000;-0.000} {second:+0.000;-0.000}");
                Assert.Less(Math.Abs(first), FirstRatioLogTolerance, $"n = 2 vs n = 1 at {360.0 * d / Directions}°");
                Assert.Less(Math.Abs(second), SecondRatioLogTolerance, $"n = 3 vs n = 2 at {360.0 * d / Directions}°");
            }
        }

        static CameraFrame Frame()
        {
            double theta = CameraPolarDegrees * Math.PI / 180;
            var pos = new Vector3d(CameraRadius * Math.Sin(theta), 0, CameraRadius * Math.Cos(theta));
            return ObserverFrame.Build(pos, Spin, (-pos).Normalized, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0), default, 0);
        }

        // With tanHalfFov = aspect = 1, ndc is the screen tangent itself.
        static Vector4d Ray(CameraFrame f, double angle, double radius) =>
            RayTracer.PixelRay(f, radius * Math.Cos(angle), radius * Math.Sin(angle), 1, 1);

        // Screen radius where the crossing count along this direction drops from above k to k.
        static double Edge(CameraFrame f, TraceSettings s, double angle, int k)
        {
            double inside = 0, outside = OuterTan;
            Assert.IsFalse(PastEdge(f, s, angle, outside, k), "outer start must be in the direct image");
            while (outside - inside > EdgeTolerance)
            {
                double mid = (inside + outside) / 2;
                if (PastEdge(f, s, angle, mid, k)) inside = mid; else outside = mid;
            }
            return (inside + outside) / 2;
        }

        static bool PastEdge(CameraFrame f, TraceSettings s, double angle, double radius, int k)
        {
            var result = RayTracer.Trace(f.Position, Ray(f, angle, radius), s);
            return result.Status != RayStatus.Escaped || result.EquatorCrossings > k;
        }
    }
}
