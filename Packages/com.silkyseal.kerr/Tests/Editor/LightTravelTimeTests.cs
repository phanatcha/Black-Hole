using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>The coordinate time each point of a traced ray had, relative to the camera, so moving gas is seen late.</summary>
    public class LightTravelTimeTests
    {
        // In ingoing Kerr-Schild coordinates light falling down the spin axis keeps t + |z| fixed, so light the camera
        // sees from height z left it z − z_camera of coordinate time earlier.
        [Test]
        public void DownTheAxis_LightTakesItsDistance([Values(0.0, 0.9)] double a)
        {
            var camera = new Vector3d(0, 0, 10);
            var frame = ObserverFrame.Build(camera, a, new Vector3d(0, 0, 1), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), default, 0);
            var clock = Clock.Create();
            RayTracer.Trace(camera, RayTracer.PixelRay(frame, 0, 0, 1, 1), TraceSettings.Reference(a), ref clock);

            Assert.AreEqual(0, clock.Times[0]);
            Assert.Greater(clock.Times.Count, 3);
            for (int i = 0; i < clock.Times.Count; i++)
                Assert.AreEqual(-(clock.Points[i].Z - camera.Z), clock.Times[i], 1e-6 * (1 + clock.Points[i].Z));
        }

        // Kerr-Schild t is a time coordinate everywhere outside the horizon and light never covers more than one unit of
        // coordinate distance per unit of it, so going back along any ray the time falls by at least the chord.
        [Test]
        public void PastTheHole_TimeFallsAtLeastAsFastAsTheChord()
        {
            const double a = 0.9;
            var camera = new Vector3d(0, -30, 4);
            var frame = ObserverFrame.Build(camera, a, (-camera).Normalized, new Vector3d(0, 0, 1), new Vector3d(1, 0, 0), default, 0);
            var clock = Clock.Create();
            RayTracer.Trace(camera, RayTracer.PixelRay(frame, 0.14, 0, 1, 1), TraceSettings.Reference(a), ref clock);

            Assert.AreEqual(0, clock.Times[0]);
            Assert.Greater(clock.Crossings.Count, 0, "the ray should cross the equator");
            for (int i = 1; i < clock.Times.Count; i++)
            {
                Assert.Less(clock.Times[i], clock.Times[i - 1]);
                Assert.GreaterOrEqual(-clock.Times[i], (clock.Points[i] - camera).Length - 1e-9);
            }
            foreach (var (step, time) in clock.Crossings)
                Assert.That(time < clock.Times[step - 1] && time > clock.Times[step], $"crossing at t = {time}");
        }

        // The inner disk's streaks move a cell every few M, and the GPU traces at tolerance 1e-5: its rays must still be
        // timed to a hundredth of an M where they cross the disk.
        [Test]
        public void LooseSteps_StillTimeTheLight()
        {
            const double a = 0.9;
            var camera = new Vector3d(0, -15, 3);
            var frame = ObserverFrame.Build(camera, a, (-camera).Normalized, new Vector3d(0, 0, 1), new Vector3d(1, 0, 0), default, 0);
            var tight = TraceSettings.Reference(a);
            var loose = tight;
            loose.Tolerance = 1e-5;
            double worst = 0;
            for (int i = 0; i < 40; i++)
            {
                var k = RayTracer.PixelRay(frame, -0.6 + 0.03 * i, -0.05, 1, 1);
                var exact = Clock.Create();
                var rough = Clock.Create();
                RayTracer.Trace(camera, k, tight, ref exact);
                RayTracer.Trace(camera, k, loose, ref rough);
                if (exact.Crossings.Count == 0 || rough.Crossings.Count == 0) continue;
                worst = Math.Max(worst, Math.Abs(exact.Crossings[0].Item2 - rough.Crossings[0].Item2));
            }
            Assert.Less(worst, 0.01);
        }

        struct Clock : IRayObserver
        {
            public List<Vector3d> Points;
            public List<double> Times;
            // Index of the step that ends past the crossing, and the crossing's time.
            public List<(int, double)> Crossings;

            public static Clock Create() =>
                new Clock { Points = new List<Vector3d>(), Times = new List<double>(), Crossings = new List<(int, double)>() };

            public bool OnEquatorCrossing(Vector3d x, double time, double kt, double kPhi)
            {
                Crossings.Add((Times.Count, time));
                return true;
            }

            public bool OnStep(Vector3d x, double time, Vector4d p, double h, ref double nextStep)
            {
                Points.Add(x);
                Times.Add(time);
                return true;
            }
        }
    }
}
