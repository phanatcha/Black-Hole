using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class FreeFallTests
    {
        // Integrator tolerance is 1e-10 per step; a few thousand steps stay well under this.
        const double ConservationTolerance = 1e-8;

        // From rest far away (E = 1) dr/dτ = −√(2/r), so the horizon to radius r takes (√2/3)(2^{3/2} − r^{3/2}): 4M/3 to the
        // singularity.
        [Test]
        public void Schwarzschild_HorizonToSingularity_TakesFourThirdsM()
        {
            var x = new Vector3d(1.2, 1.6, 0); // r = 2
            var fall = new FreeFall(x, Rain(x), 0);
            for (int i = 0; i < 10000 && !fall.Ended; i++) fall.Advance(0.01);

            Assert.IsTrue(fall.Ended);
            double r = fall.Radius;
            Assert.AreEqual(Math.Sqrt(2) / 3 * (Math.Pow(2, 1.5) - Math.Pow(r, 1.5)), fall.ProperTime, 1e-7);
            Assert.AreEqual(4.0 / 3, fall.ProperTime, 1e-3);
        }

        [Test]
        public void KerrFall_KeepsItsConstants_ThroughTheHorizon_DownToTheInnerHorizon()
        {
            const double a = 0.9;
            var x0 = new Vector3d(5, 1, 2);
            var u0 = ObserverFrame.Build(x0, a, (-x0).Normalized, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0),
                                         new Vector3d(-1, 0.2, 0), 0.3).U;
            var fall = new FreeFall(x0, u0, a);
            double l0 = AngularMomentum(x0, KerrSchild.Lower(u0, x0, a));
            Assert.Greater(Math.Abs(l0), 1e-3, "a test of L needs some");

            bool crossed = false;
            for (int i = 0; i < 100000 && !fall.Ended; i++)
            {
                fall.Advance(0.05);
                var x = fall.Position;
                var u = fall.Velocity;
                Assert.AreEqual(-1, KerrSchild.Dot(u, u, x, a), ConservationTolerance, $"u·u at r = {fall.Radius}");
                Assert.AreEqual(l0, AngularMomentum(x, KerrSchild.Lower(u, x, a)), ConservationTolerance, $"L at r = {fall.Radius}");
                crossed |= fall.Radius < KerrSchild.HorizonRadius(a);
            }
            Assert.IsTrue(crossed);
            Assert.IsTrue(fall.Ended);
            Assert.AreEqual(KerrSchild.InnerHorizonRadius(a) + FreeFall.EndMargin, fall.Radius, 0.01);
        }

        [Test]
        public void Advance_StopsAtTheRequestedProperTime()
        {
            const double a = 0.6;
            var x = new Vector3d(20, 0, 3);
            var fall = new FreeFall(x, ObserverFrame.Zamo(x, a), a, 100);
            fall.Advance(0.37);
            fall.Advance(1.3);

            Assert.AreEqual(1.67, fall.ProperTime, 1e-12);
            Assert.Greater(fall.Time, 101.67, "a clock down the well runs slow against Kerr-Schild time");
        }

        // Aimed just short of the line between capture and escape, the body circles the unstable orbit before plunging;
        // just past it, it circles and leaves.
        [TestCase(-1)]
        [TestCase(1)]
        public void Whirl_CirclesTheHole_ThenMeetsItsFate(int side)
        {
            const double a = 0.6;
            var start = new Vector3d(0, -29.6, 4.7); // the demo camera's direction, 30 M out
            var fall = FreeFall.Whirl(start, a, side);
            double r0 = fall.Radius, near = 0, phi = Azimuth(fall.Position);
            bool left = false;
            for (int i = 0; i < 100000 && !fall.Ended && !left; i++)
            {
                fall.Advance(0.5);
                double next = Azimuth(fall.Position), step = Math.IEEERemainder(next - phi, 2 * Math.PI);
                if (fall.Radius < 8) near += step;
                phi = next;
                left = fall.Radius > r0;
            }

            Assert.Greater(near, 3 * Math.PI, "at least one whole turn besides the way in and out");
            Assert.AreEqual(side < 0, fall.Ended, "plunges");
            Assert.AreEqual(side > 0, left, "leaves");
        }

        /// <summary>Kerr-Schild 4-velocity at a = 0 of a body dropped from rest far away: u^r = −√(2/r), u_t = −1.</summary>
        internal static Vector4d Rain(Vector3d x)
        {
            double r = x.Length, s = Math.Sqrt(2 / r);
            return new Vector4d((1 + s + s * s) / (1 + s), x * (-s / r));
        }

        static double AngularMomentum(Vector3d x, Vector4d p) => x.X * p.Y - x.Y * p.X;
        static double Azimuth(Vector3d x) => Math.Atan2(x.Y, x.X);
    }
}
