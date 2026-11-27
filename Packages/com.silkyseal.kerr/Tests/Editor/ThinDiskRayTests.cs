using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class ThinDiskRayTests
    {
        const double CameraHeight = 1000, RelativeTolerance = 1e-9;

        [Test]
        public void OpaqueDisk_AbsorbsTheRay_WithTheShiftedBlackbody([Values] bool shifts)
        {
            var disk = ThinDisk.FromPeakTemperature(0, 30, 6000);
            var observer = new ThinDiskRay(disk, 1, shifts);
            var result = TraceMeridional(10, ref observer);

            Assert.AreEqual(RayStatus.Absorbed, result.Status);
            Assert.AreEqual(0, observer.Transmittance);
            double r = observer.FirstHitRadius;
            Assert.That(r > disk.InnerRadius && r < 10, $"hit at r = {r}");
            // Static camera on the axis, ray in a meridional plane (k_φ = 0): gravitational and transverse Doppler only.
            double g = shifts ? Math.Sqrt(1 - 3 / r) / Math.Sqrt(1 - 2 / CameraHeight) : 1;
            AssertRadiance(Blackbody.LinearSrgb(g * disk.Temperature(r)), observer.Radiance);
        }

        [Test]
        public void SwirlPattern_ScalesTheLight_AsItWasWhenTheLightLeft()
        {
            var disk = ThinDisk.FromPeakTemperature(0, 30, 6000);
            var swirl = new SwirlPattern(0, 1, 2000);
            var smooth = new ThinDiskRay(disk, 1, true);
            var streaky = new FirstCrossingTime { Disk = new ThinDiskRay(disk, 1, true, swirl), Time = double.NaN };
            TraceMeridional(10, ref smooth);
            TraceMeridional(10, ref streaky);

            // The ray lies in the y = 0 plane and meets the disk at x > 0, so φ = 0.
            double r = streaky.Disk.FirstHitRadius, seen = swirl.Value(r, 0, 0, streaky.Time);
            Assert.Greater(Math.Abs(seen - swirl.Value(r, 0, 0, 0)), 0.01, "the delay should matter here");
            AssertRadiance(smooth.Radiance * seen, streaky.Disk.Radiance);
        }

        [Test]
        public void CloudTexture_VariesPartialOpacity_AndPreservesItsEndpoints()
        {
            var disk = ThinDisk.FromPeakTemperature(0, 30, 7000);
            var clouds = new SwirlPattern(0, 0.75, 300, broadStreams: true, cloudTexture: 1);
            double min = 1, max = 0;
            for (int i = 0; i < 64; i++)
            {
                double phi = 2 * Math.PI * i / 64;
                var x = new Vector3d(12 * Math.Cos(phi), 12 * Math.Sin(phi), 0);
                var clear = new ThinDiskRay(disk, 0, false, clouds);
                var veil = new ThinDiskRay(disk, 0.34, false, clouds);
                var opaque = new ThinDiskRay(disk, 1, false, clouds);
                clear.OnEquatorCrossing(x, 0, 1, 0);
                veil.OnEquatorCrossing(x, 0, 1, 0);
                opaque.OnEquatorCrossing(x, 0, 1, 0);
                Assert.AreEqual(1, clear.Transmittance);
                Assert.AreEqual(0, opaque.Transmittance);
                min = Math.Min(min, veil.Transmittance);
                max = Math.Max(max, veil.Transmittance);
            }
            Assert.Less(min, 1 - 0.34);
            Assert.Greater(max, 1 - 0.34);
        }

        struct FirstCrossingTime : IRayObserver
        {
            public ThinDiskRay Disk;
            public double Time;

            public bool OnEquatorCrossing(Vector3d x, double time, double kt, double kPhi)
            {
                if (double.IsNaN(Time)) Time = time;
                return Disk.OnEquatorCrossing(x, time, kt, kPhi);
            }

            public bool OnStep(Vector3d x, double time, Vector4d p, double h, ref double nextStep) => true;
        }

        [Test]
        public void TranslucentDisk_LetsTheRayThroughAtReducedWeight()
        {
            var disk = ThinDisk.FromPeakTemperature(0, 30, 6000);
            var opaque = new ThinDiskRay(disk, 1, true);
            var halfway = new ThinDiskRay(disk, 0.5, true);
            TraceMeridional(10, ref opaque);
            var result = TraceMeridional(10, ref halfway);

            Assert.AreEqual(RayStatus.Escaped, result.Status);
            Assert.AreEqual(1, result.EquatorCrossings);
            Assert.AreEqual(0.5, halfway.Transmittance);
            AssertRadiance(opaque.Radiance * 0.5, halfway.Radiance);
        }

        [Test]
        public void RayPastTheOuterEdge_IsUntouched()
        {
            var observer = new ThinDiskRay(ThinDisk.FromPeakTemperature(0, 20, 6000), 1, true);
            var result = TraceMeridional(40, ref observer);

            Assert.AreEqual(RayStatus.Escaped, result.Status);
            Assert.AreEqual(1, result.EquatorCrossings);
            Assert.AreEqual(1, observer.Transmittance);
            Assert.AreEqual(0, observer.Radiance.X + observer.Radiance.Y + observer.Radiance.Z);
            Assert.IsNaN(observer.FirstHitRadius);
        }

        [Test]
        public void ApproachingSide_IsBluerAndBrighter()
        {
            // a = 0 keeps the scene mirror-symmetric about the camera's vertical plane, so mirrored pixels hit the
            // same radius and differ only by the gas velocity. Gas orbits +z, so the camera's left (KS −y) approaches.
            var disk = ThinDisk.FromPeakTemperature(0, 30, 4500);
            var frame = GpuParityTests.TestFrame(0);
            var settings = TraceSettings.Reference(0);
            double tanHalfFov = Math.Tan(Math.PI / 6);
            var left = new ThinDiskRay(disk, 1, true);
            var right = new ThinDiskRay(disk, 1, true);
            Assert.AreEqual(RayStatus.Absorbed, RayTracer.Trace(frame.Position, RayTracer.PixelRay(frame, -0.4, -0.3, tanHalfFov, 16.0 / 9), settings, ref left).Status);
            Assert.AreEqual(RayStatus.Absorbed, RayTracer.Trace(frame.Position, RayTracer.PixelRay(frame, 0.4, -0.3, tanHalfFov, 16.0 / 9), settings, ref right).Status);

            Assert.AreEqual(left.FirstHitRadius, right.FirstHitRadius, 1e-8);
            Assert.Greater(Luminance(left.Radiance), Luminance(right.Radiance));
            Assert.Greater(left.Radiance.Z / left.Radiance.X, right.Radiance.Z / right.Radiance.X);
        }

        [Test]
        public void HitRadius_AtGpuTolerance_MatchesTheReference()
        {
            // Big steps at the GPU's tolerance must still place the crossing where the tight reference does. Measured:
            // a chord between step ends misses by 1 %, the Hermite crossing by 1e-4 (the loose path itself).
            // 1e-3 sits a decade from both and keeps colour within ~0.5 %.
            var disk = ThinDisk.FromPeakTemperature(0.9, 30, 6000);
            var frame = GpuParityTests.TestFrame(0.9);
            var tight = TraceSettings.Reference(0.9);
            var loose = tight;
            loose.Tolerance = 1e-5;
            double tanHalfFov = Math.Tan(Math.PI / 6), worst = 0;
            for (double ndcX = -0.9; ndcX <= 0.9; ndcX += 0.3)
            for (double ndcY = -0.9; ndcY <= 0.3; ndcY += 0.3)
            {
                var k = RayTracer.PixelRay(frame, ndcX, ndcY, tanHalfFov, 16.0 / 9);
                var reference = new ThinDiskRay(disk, 1, true);
                var coarse = new ThinDiskRay(disk, 1, true);
                RayTracer.Trace(frame.Position, k, tight, ref reference);
                RayTracer.Trace(frame.Position, k, loose, ref coarse);
                if (double.IsNaN(reference.FirstHitRadius)) continue;
                worst = Math.Max(worst, Math.Abs(coarse.FirstHitRadius / reference.FirstHitRadius - 1));
            }
            Assert.Less(worst, 1e-3);
        }

        // A Schwarzschild ray from a static camera on the +z axis, aimed at (aim, 0, 0) on the equator.
        static RayResult TraceMeridional<T>(double aim, ref T observer) where T : struct, IRayObserver
        {
            var pos = new Vector3d(0, 0, CameraHeight);
            var frame = ObserverFrame.Build(pos, 0, (new Vector3d(aim, 0, 0) - pos).Normalized,
                                            new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), default, 0);
            return RayTracer.Trace(pos, RayTracer.PixelRay(frame, 0, 0, 1, 1), TraceSettings.Reference(0), ref observer);
        }

        static void AssertRadiance(Vector3d expected, Vector3d actual)
        {
            Assert.AreEqual(expected.X, actual.X, RelativeTolerance * expected.X);
            Assert.AreEqual(expected.Y, actual.Y, RelativeTolerance * expected.Y);
            Assert.AreEqual(expected.Z, actual.Z, RelativeTolerance * expected.Z);
        }

        static double Luminance(Vector3d linearSrgb) => 0.2126 * linearSrgb.X + 0.7152 * linearSrgb.Y + 0.0722 * linearSrgb.Z;
    }
}
