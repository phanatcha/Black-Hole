using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class SwirlPatternTests
    {
        const double Spin = 0.6, InnerRadius = 6, OuterRadius = 30;

        [Test]
        public void Ring_ComesBackAfterOneKeplerOrbit([Values(0, 20, 30)] int ring, [Values(1, SwirlPattern.GasCoarseness)] int coarseness)
        {
            double r = SwirlPattern.RingRadius(ring, coarseness), period = 2 * Math.PI / CircularOrbit.AngularVelocity(r, Spin);
            const double start = 1234.5;
            double turns = SwirlPattern.Turns(ring, Spin, start, coarseness);

            Assert.AreEqual(0, TurnDifference(SwirlPattern.Turns(ring, Spin, start + period, coarseness), turns), 1e-9);
            Assert.AreEqual(0.5, Math.Abs(TurnDifference(SwirlPattern.Turns(ring, Spin, start + period / 2, coarseness), turns)), 1e-9);
        }

        // A ray samples the hot flow every tenth of r, too sparse for the disk's streaks.
        [Test]
        public void GasPattern_VariesAtLeastFourTimesMoreSlowly()
        {
            Roughness(new SwirlPattern(Spin, 1, 0), out double fineRadial, out double fineAzimuthal);
            Roughness(new SwirlPattern(Spin, 1, 0, SwirlPattern.GasCoarseness), out double coarseRadial, out double coarseAzimuthal);
            Assert.GreaterOrEqual(fineRadial / coarseRadial, SwirlPattern.GasCoarseness);
            Assert.GreaterOrEqual(fineAzimuthal / coarseAzimuthal, SwirlPattern.GasCoarseness);
        }

        [Test]
        public void Pattern_TurnsProgradeWithTheGas()
        {
            const int ring = 40, samples = 2000;
            double r = SwirlPattern.RingRadius(ring), omega = CircularOrbit.AngularVelocity(r, Spin);
            double later = 2 * Math.PI / SwirlPattern.Slots / omega; // the ring turns by one cell
            var now = new SwirlPattern(Spin, 1, 0);
            var then = new SwirlPattern(Spin, 1, later);

            double prograde = 0, retrograde = 0;
            for (int i = 0; i < samples; i++)
            {
                double phi = -Math.PI + 2 * Math.PI * (i + 0.5) / samples, before = now.Value(r, phi, 0, 0);
                prograde += Math.Abs(then.Value(r, phi + omega * later, 0, 0) - before);
                retrograde += Math.Abs(then.Value(r, phi - omega * later, 0, 0) - before);
            }
            Assert.Less(prograde, 0.2 * retrograde);
        }

        [Test]
        public void Pattern_SampledEarlier_IsThePatternAtThatTime()
        {
            var now = new SwirlPattern(Spin, 1, 500);
            var earlier = new SwirlPattern(Spin, 1, 380);
            for (double r = 6.5; r < 30; r += 3.7)
                Assert.AreEqual(earlier.Value(r, 1.1, 0.2, 0), now.Value(r, 1.1, 0.2, -120), 1e-9);
        }

        // The method's point: a texture carried by the shearing gas winds into ever thinner rings, while the swirl
        // pattern's cells only slide past each other and keep their noise, so its texture is the same at any time.
        [Test]
        public void Pattern_KeepsItsTexture_AfterAThousandOrbits()
        {
            Roughness(new SwirlPattern(Spin, 1, 0), out double radialEarly, out _);
            Roughness(new SwirlPattern(Spin, 1, 1e5), out double radialLate, out _);
            Assert.AreEqual(1, radialLate / radialEarly, 0.15);
        }

        [Test]
        public void Pattern_StreaksRunAlongTheOrbit()
        {
            Roughness(new SwirlPattern(Spin, 1, 0), out double radial, out double azimuthal);
            Assert.Greater(radial / azimuthal, 4);
        }

        [Test]
        public void Pattern_AveragesToOne_WithVisibleContrast()
        {
            var pattern = new SwirlPattern(Spin, 1, 777);
            double sum = 0, squares = 0, min = double.MaxValue;
            int count = 0;
            ForDisk((r, phi) =>
            {
                double v = pattern.Value(r, phi, 0, 0);
                sum += v;
                squares += v * v;
                min = Math.Min(min, v);
                count++;
            });
            double mean = sum / count, spread = Math.Sqrt(squares / count - mean * mean);

            Assert.AreEqual(1, mean, 0.02);
            Assert.GreaterOrEqual(min, 0);
            Assert.Greater(spread, 0.3);
        }

        [Test]
        public void Pattern_HasNoSeams()
        {
            var pattern = new SwirlPattern(Spin, 1, 4321);
            // Fine enough that the steepest strands, where the drift bunches them, change by about 0.01 a step; a seam would
            // not shrink with the step.
            const int steps = 80000;
            double worst = 0, previous = pattern.Value(10, -Math.PI, 0, 0);
            for (int i = 1; i <= steps; i++) // once around, across every cell edge and φ = ±π
            {
                double v = pattern.Value(10, -Math.PI + 2 * Math.PI * i / steps, 0, 0);
                worst = Math.Max(worst, Math.Abs(v - previous));
                previous = v;
            }
            double u0 = Math.Log(InnerRadius), u1 = Math.Log(OuterRadius);
            previous = pattern.Value(InnerRadius, 0.3, 0, 0);
            for (int i = 1; i <= steps; i++) // outward across every ring edge
            {
                double v = pattern.Value(Math.Exp(u0 + (u1 - u0) * i / steps), 0.3, 0, 0);
                worst = Math.Max(worst, Math.Abs(v - previous));
                previous = v;
            }
            Assert.Less(worst, 0.02);
        }

        // The demo camera sits 3 M above the disk's outer edge, looking at the hole. Seen from there, lanes in the
        // foreground that run on along the orbit line up side by side across the frame like grooves in a record. Without
        // the drifting, merging strands 16.5 % of the darkest foreground lies in lanes wider than a quarter of the frame.
        [Test]
        public void DemoForeground_HasFewWideGrooves()
        {
            Assert.Less(WideGrooveShare(), 0.12);
        }

        // Neighbouring strands blend into one broad ribbon for part of their orbit, where a short radial profile is smooth.
        [Test]
        public void Strands_MergeIntoRibbons_ForPartOfTheirOrbit()
        {
            RadialProfiles(new SwirlPattern(Spin, 1, 777), out double merged, out _);
            Assert.Greater(merged, 0.04);
        }

        // Not a blur: away from the ribbons the strands keep their detail. Without ribbons the roughness is 0.15.
        [Test]
        public void Ribbons_KeepTheFineStrands()
        {
            RadialProfiles(new SwirlPattern(Spin, 1, 777), out _, out double roughness);
            Assert.Greater(roughness, 0.11);
        }

        // The demo view's lower 40 %, 640 × 360 and 60° high, on a flat disk (lensing there is slight), over six frames:
        // the darkest tenth after a 9-pixel blur along each row, and the share of it in runs wider than a quarter frame.
        // Darkest relative to the frame itself, so lower contrast alone cannot pass.
        static double WideGrooveShare()
        {
            const int width = 640, height = 360, rows = 144, blur = 4, frames = 6;
            double tan = Math.Tan(Math.PI / 6), pitch = Math.Atan2(3, 30), share = 0;
            for (int f = 0; f < frames; f++)
            {
                var pattern = new SwirlPattern(Spin, 1, 0.6 + 173.3 * f);
                var p = new double[width * rows];
                for (int j = 0; j < rows; j++)
                for (int i = 0; i < width; i++)
                {
                    // Camera at Unity (0, 3, −30), pitched down onto the hole; the disk is Unity's y = 0, Kerr-Schild (x, z).
                    double x = (2 * (i + 0.5) / width - 1) * tan * width / height, y = (2 * (j + 0.5) / height - 1) * tan;
                    double down = y * Math.Cos(pitch) - Math.Sin(pitch), ahead = Math.Cos(pitch) + y * Math.Sin(pitch);
                    double t = -3 / down, hx = t * x, hz = -30 + t * ahead;
                    p[j * width + i] = pattern.Value(Math.Sqrt(hx * hx + hz * hz), Math.Atan2(hz, hx), 0, 0);
                }
                var smooth = new double[p.Length];
                for (int j = 0; j < rows; j++)
                for (int i = 0; i < width; i++)
                {
                    double sum = 0;
                    int n = 0;
                    for (int x = Math.Max(0, i - blur); x <= Math.Min(width - 1, i + blur); x++, n++) sum += p[j * width + x];
                    smooth[j * width + i] = sum / n;
                }
                var sorted = (double[])smooth.Clone();
                Array.Sort(sorted);
                double darkest = sorted[sorted.Length / 10], dark = 0, wide = 0;
                for (int j = 0; j < rows; j++)
                {
                    int run = 0;
                    for (int i = 0; i <= width; i++)
                    {
                        if (i < width && smooth[j * width + i] < darkest) { run++; continue; }
                        dark += run;
                        if (run > width / 4) wide += run;
                        run = 0;
                    }
                }
                share += wide / dark / frames;
            }
            return share;
        }

        // Profiles 1.5 M long outward across the foreground: the share smoother than half the median, and the RMS step.
        static void RadialProfiles(SwirlPattern pattern, out double merged, out double roughness)
        {
            const int profiles = 1500, steps = 30;
            var rough = new List<double>();
            double squares = 0;
            for (int k = 0; k < profiles; k++)
            {
                double r = 22 + 5 * (k * 0.618034 % 1), phi = -Math.PI + 2 * Math.PI * (k * 0.414214 % 1), sum = 0;
                double previous = pattern.Value(r, phi, 0, 0);
                for (int q = 1; q <= steps; q++)
                {
                    double v = pattern.Value(r + 0.05 * q, phi, 0, 0);
                    sum += (v - previous) * (v - previous);
                    previous = v;
                }
                rough.Add(Math.Sqrt(sum / steps));
                squares += sum / steps;
            }
            rough.Sort();
            double median = rough[profiles / 2];
            merged = rough.Count(x => x < median / 2) / (double)profiles;
            roughness = Math.Sqrt(squares / profiles);
        }

        // RMS of ∂P/∂ln r and of ∂P/∂φ over the disk; equal values would mean round blobs.
        static void Roughness(SwirlPattern pattern, out double radial, out double azimuthal)
        {
            const double d = 1e-4;
            double sumR = 0, sumPhi = 0;
            int count = 0;
            ForDisk((r, phi) =>
            {
                double dr = (pattern.Value(r * Math.Exp(d), phi, 0, 0) - pattern.Value(r * Math.Exp(-d), phi, 0, 0)) / (2 * d);
                double dPhi = (pattern.Value(r, phi + d, 0, 0) - pattern.Value(r, phi - d, 0, 0)) / (2 * d);
                sumR += dr * dr;
                sumPhi += dPhi * dPhi;
                count++;
            });
            radial = Math.Sqrt(sumR / count);
            azimuthal = Math.Sqrt(sumPhi / count);
        }

        // An even grid in ln r and φ over the disk.
        static void ForDisk(Action<double, double> sample)
        {
            const int radii = 150, angles = 300;
            double u0 = Math.Log(InnerRadius), u1 = Math.Log(OuterRadius);
            for (int i = 0; i < radii; i++)
            for (int j = 0; j < angles; j++)
                sample(Math.Exp(u0 + (u1 - u0) * (i + 0.5) / radii), -Math.PI + 2 * Math.PI * (j + 0.5) / angles);
        }

        static double TurnDifference(double a, double b) => a - b - Math.Round(a - b);
    }
}
