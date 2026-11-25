using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class HotFlowRayTests
    {
        const double Distance = 1000;

        [Test]
        public void Ring_AtZeroSpinIsTheSizeEhtMeasures()
        {
            // The 230 GHz ring of a non-spinning hole sits on its photon ring, diameter 2√27 M = 10.4 M (EHT 2022, Paper I).
            const double inclination = 20 * Math.PI / 180;
            var flow = HotFlow.SgrA(0, new Vector3d(0, 1, 0));
            var position = new Vector3d(Distance * Math.Sin(inclination), 0, Distance * Math.Cos(inclination));
            var frame = ObserverFrame.Build(position, 0, -position.Normalized, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0), default, 0);

            // Seen this close to face-on the ring is round, so every cut from the centre peaks on it.
            const int cuts = 8, samples = 70;
            for (int c = 0; c < cuts; c++)
            {
                double angle = 2 * Math.PI * c / cuts, peak = 0, brightest = -1;
                for (int s = 0; s < samples; s++)
                {
                    double b = 2 + 7.0 * s / (samples - 1);
                    var ray = Trace(flow, frame, b * Math.Cos(angle), b * Math.Sin(angle));
                    if (ray.Radio > brightest) { brightest = ray.Radio; peak = b; }
                }
                Assert.Greater(brightest, 0, "the flow should glow at 230 GHz");
                Assert.AreEqual(10.4, 2 * peak, 1.04, $"ring diameter along cut {c}");
            }
        }

        [Test]
        public void Beta2_TwistReversesSeenFromTheOtherPole()
        {
            // Palumbo, Wong & Prather (2020): β2 = Σ P e^(−2iφ) / Σ I. The flow is mirror-symmetric about its equator, and
            // seen from the south pole the sky is the north view flipped left to right, so β2 turns into its conjugate.
            var flow = HotFlow.SgrA(0.5, new Vector3d(1, 1, 0));
            Beta2(flow, +1, out double northRe, out double northIm);
            Beta2(flow, -1, out double southRe, out double southIm);

            double size = Math.Sqrt(northRe * northRe + northIm * northIm);
            Assert.Greater(size, 0.02, "the image should be polarized in a pattern");
            Assert.Greater(Math.Abs(northIm), 0.1 * size, "a spiral field should twist the pattern");
            Assert.AreEqual(northRe, southRe, 0.02 * size, "β2 real parts");
            Assert.AreEqual(-northIm, southIm, 0.02 * size, "β2 imaginary parts");
        }

        static void Beta2(in HotFlow flow, int pole, out double re, out double im)
        {
            var position = new Vector3d(0, 0, pole * Distance);
            var forward = new Vector3d(0, 0, -pole);
            var up = new Vector3d(0, 1, 0);
            var frame = ObserverFrame.Build(position, flow.Spin, forward, up, Vector3d.Cross(forward, up), default, 0);

            const int pixels = 24;
            const double halfWidth = 8;
            double total = 0;
            re = im = 0;
            for (int i = 0; i < pixels; i++)
            for (int j = 0; j < pixels; j++)
            {
                double alpha = ((i + 0.5) / pixels * 2 - 1) * halfWidth, beta = ((j + 0.5) / pixels * 2 - 1) * halfWidth;
                var ray = Trace(flow, frame, alpha, beta);
                // Image azimuth φ east of north, the convention EVPA uses: from up towards the left.
                double phi = Math.Atan2(-alpha, beta), c = Math.Cos(2 * phi), s = Math.Sin(2 * phi);
                re += ray.StokesQ * c + ray.StokesU * s;
                im += ray.StokesU * c - ray.StokesQ * s;
                total += ray.Radio;
            }
            re /= total;
            im /= total;
        }

        // The ray through screen point (α, β) in M, right and up, at the camera's distance.
        internal static HotFlowRay Trace(in HotFlow flow, in CameraFrame frame, double alpha, double beta)
        {
            var direction = new Vector3d(alpha / Distance, beta / Distance, 1).Normalized;
            var ray = new HotFlowRay(flow, new WalkerPenrose.Screen(frame, direction));
            var k0 = RayTracer.PixelRay(frame, direction.X / direction.Z, direction.Y / direction.Z, 1, 1);
            var settings = new TraceSettings { Spin = flow.Spin, Tolerance = 1e-8, FarRadius = 5000, MaxSteps = 100000 };
            RayTracer.Trace(frame.Position, k0, settings, ref ray);
            return ray;
        }
    }
}
