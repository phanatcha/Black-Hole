using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class WalkerPenroseTests
    {
        [TestCase(0.0)]
        [TestCase(0.9)]
        public void Constant_IsConservedUnderParallelTransport(double a)
        {
            // A ray bent hard by the hole, its polarization parallel-transported with Christoffel symbols taken by finite
            // differences of the metric: the brute-force answer the constant has to match.
            var x = new Vector3d(9, 1, 2.5);
            var u = ObserverFrame.Zamo(x, a);
            var n = Spatial(new Vector4d(0, -1, 0.75, -0.2), u, x, a);
            var k = n - u;
            var p = KerrSchild.Lower(k, x, a);
            var f = WalkerPenrose.Perpendicular(u, k, new Vector4d(0, 0.3, 0.2, 1), x, a);

            WalkerPenrose.Constant(x, k, f, a, out double start1, out double start2);
            double closest = double.MaxValue;
            const double h = 0.005;
            for (int i = 0; i < 3000; i++)
            {
                Rk4(ref x, ref p, ref f, a, h);
                closest = Math.Min(closest, KerrSchild.Radius(x, a));
            }
            k = KerrSchild.Raise(p, x, a);
            WalkerPenrose.Constant(x, k, f, a, out double end1, out double end2);

            Assert.Less(closest, 6, "the ray should pass close to the hole");
            double scale = Math.Sqrt(start1 * start1 + start2 * start2);
            Assert.AreEqual(start1, end1, 1e-6 * scale);
            Assert.AreEqual(start2, end2, 1e-6 * scale);
        }

        [TestCase(5.0, 0.0)]
        [TestCase(0.0, 5.0)]
        [TestCase(-4.0, 3.0)]
        [TestCase(3.0, -4.5)]
        [TestCase(-6.0, -2.0)]
        public void DistantCamera_SeesTheHimwichAngle(double alpha, double beta)
        {
            const double a = 0.9, inclination = 60 * Math.PI / 180, distance = 1e4;
            var position = new Vector3d(distance * Math.Sin(inclination), 0, distance * Math.Cos(inclination));
            var frame = ObserverFrame.Build(position, a, (-position).Normalized, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0), default, 0);
            var direction = new Vector3d(alpha / distance, beta / distance, 1).Normalized;
            var screen = new WalkerPenrose.Screen(frame, direction);

            // Follow the pixel's ray back to r < 6 and polarize the light there in some direction across the ray.
            var k0 = RayTracer.PixelRay(frame, direction.X / direction.Z, direction.Y / direction.Z, 1, 1);
            var recorder = new Recorder();
            var settings = TraceSettings.Reference(a);
            settings.FarRadius = 2 * distance; // the camera sits beyond the reference sky sphere
            RayTracer.Trace(frame.Position, k0, settings, ref recorder);
            Assert.IsTrue(recorder.Done, "the ray should reach the hole's neighbourhood");
            var k = KerrSchild.Raise(recorder.P, recorder.X, a);
            var f = WalkerPenrose.Perpendicular(ObserverFrame.Zamo(recorder.X, a), k, new Vector4d(0, 1, -0.5, 0.7), recorder.X, a);
            WalkerPenrose.Constant(recorder.X, k, f, a, out double k1, out double k2);
            screen.Polarization(k1, k2, out double cos2, out double sin2);

            // Himwich et al. (2020) eqs. 7–10: screen α from the ray's λ, E on a screen at infinity, EVPA from E.
            double lambda = -k0.Y * position.X / k0.T; // λ = L / E = −k_φ / k_t; k_φ = x k_y − y k_x, here x k_y
            Assert.AreEqual(alpha, -lambda / Math.Sin(inclination), 0.01 * Math.Max(1, Math.Abs(alpha)), "α is screen right");
            double nu = -(alpha + a * Math.Sin(inclination));
            double eAlpha = beta * k2 - nu * k1, eBeta = beta * k1 + nu * k2;
            double expected = Math.Atan2(-eAlpha, eBeta), actual = 0.5 * Math.Atan2(sin2, cos2);
            // EVPA is an axis, defined modulo π; 1e-3 covers the camera's finite distance, O(M / r) = 1e-4.
            Assert.Less(Math.Abs(Math.Sin(actual - expected)), 1e-3, $"EVPA {actual} vs {expected}");
        }

        [Test]
        public void Constant_IsContinuousOnTheSpinAxis()
        {
            // A camera straight above a pole sits on the axis, where the azimuth is undefined.
            const double a = 0.8;
            var k = new Vector4d(1, 0.3, -0.5, -0.8);
            var f = new Vector4d(0, 1, 0.4, 0.1);
            WalkerPenrose.Constant(new Vector3d(0, 0, 12), k, f, a, out double axis1, out double axis2);
            WalkerPenrose.Constant(HotFlowTests.PointAt(12, 1e-7, 2.0, a), k, f, a, out double near1, out double near2);
            double scale = Math.Sqrt(near1 * near1 + near2 * near2);
            Assert.AreEqual(near1, axis1, 1e-5 * scale);
            Assert.AreEqual(near2, axis2, 1e-5 * scale);
        }

        [Test]
        public void Perpendicular_IsOrthogonalToAllThree()
        {
            const double a = 0.7;
            var x = new Vector3d(3, -2, 1.5);
            var u = ObserverFrame.Zamo(x, a);
            var k = new Vector4d(1, 0.2, -0.6, 0.4);
            var b = new Vector4d(0.1, 1, 0.3, -0.2);
            var f = WalkerPenrose.Perpendicular(u, k, b, x, a);
            Assert.AreEqual(0, KerrSchild.Dot(f, u, x, a), 1e-12);
            Assert.AreEqual(0, KerrSchild.Dot(f, k, x, a), 1e-12);
            Assert.AreEqual(0, KerrSchild.Dot(f, b, x, a), 1e-12);
            Assert.Greater(KerrSchild.Dot(f, f, x, a), 0);
        }

        struct Recorder : IRayObserver
        {
            public Vector3d X;
            public Vector4d P;
            public bool Done;

            public bool OnEquatorCrossing(Vector3d x, double time, double kt, double kPhi) => true;

            public bool OnStep(Vector3d x, double time, Vector4d p, double h, ref double nextStep)
            {
                if (KerrSchild.Radius(x, 0.9) >= 6) return true;
                X = x;
                P = p;
                Done = true;
                return false;
            }
        }

        // Unit spatial direction in the rest frame of u.
        static Vector4d Spatial(Vector4d v, Vector4d u, Vector3d x, double a)
        {
            var s = v + u * KerrSchild.Dot(v, u, x, a);
            return s * (1 / Math.Sqrt(KerrSchild.Dot(s, s, x, a)));
        }

        // Fixed-step RK4 of the geodesic (x, p_i) and the parallel transport df^μ/dλ = −Γ^μ_αβ k^α f^β.
        static void Rk4(ref Vector3d x, ref Vector4d p, ref Vector4d f, double a, double h)
        {
            void D(Vector3d xs, Vector4d ps, Vector4d fs, out Vector3d dx, out Vector3d dp, out Vector4d df)
            {
                var k = KerrSchild.Raise(ps, xs, a);
                dx = k.Spatial;
                KerrSchild.HamiltonianGradient(xs, ps, a, out var grad);
                dp = -grad;
                df = Transport(xs, k, fs, a) * -1;
            }
            D(x, p, f, out var x1, out var p1, out var f1);
            D(x + x1 * (h / 2), p + new Vector4d(0, p1 * (h / 2)), f + f1 * (h / 2), out var x2, out var p2, out var f2);
            D(x + x2 * (h / 2), p + new Vector4d(0, p2 * (h / 2)), f + f2 * (h / 2), out var x3, out var p3, out var f3);
            D(x + x3 * h, p + new Vector4d(0, p3 * h), f + f3 * h, out var x4, out var p4, out var f4);
            x += (x1 + 2 * x2 + 2 * x3 + x4) * (h / 6);
            p += new Vector4d(0, (p1 + 2 * p2 + 2 * p3 + p4) * (h / 6));
            f += (f1 + f2 * 2 + f3 * 2 + f4) * (h / 6);
        }

        // Γ^μ_αβ k^α f^β = ½ g^μν (k^α f^β ∂_α g_νβ + k^α f^β ∂_β g_να − k^α f^β ∂_ν g_αβ), derivatives by central differences.
        static Vector4d Transport(Vector3d x, Vector4d k, Vector4d f, double a)
        {
            const double h = 1e-5;
            double[] kc = Components(k), fc = Components(f);
            var dg = new double[4][,];
            dg[0] = new double[4, 4]; // the metric is stationary
            for (int i = 1; i < 4; i++)
            {
                var step = new Vector3d(i == 1 ? h : 0, i == 2 ? h : 0, i == 3 ? h : 0);
                double[,] plus = Metric(x + step, a), minus = Metric(x - step, a);
                dg[i] = new double[4, 4];
                for (int m = 0; m < 4; m++)
                for (int n = 0; n < 4; n++)
                    dg[i][m, n] = (plus[m, n] - minus[m, n]) / (2 * h);
            }
            var lowered = new double[4];
            for (int nu = 0; nu < 4; nu++)
            for (int al = 0; al < 4; al++)
            for (int be = 0; be < 4; be++)
                lowered[nu] += 0.5 * kc[al] * fc[be] * (dg[al][nu, be] + dg[be][nu, al] - dg[nu][al, be]);
            return KerrSchild.Raise(new Vector4d(lowered[0], lowered[1], lowered[2], lowered[3]), x, a);
        }

        static double[,] Metric(Vector3d x, double a)
        {
            var g = new double[4, 4];
            for (int m = 0; m < 4; m++)
            {
                var lowered = Components(KerrSchild.Lower(Basis(m), x, a));
                for (int n = 0; n < 4; n++) g[m, n] = lowered[n];
            }
            return g;
        }

        static Vector4d Basis(int m) => new Vector4d(m == 0 ? 1 : 0, m == 1 ? 1 : 0, m == 2 ? 1 : 0, m == 3 ? 1 : 0);
        static double[] Components(Vector4d v) => new[] { v.T, v.X, v.Y, v.Z };
    }
}
