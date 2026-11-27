using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class RgbSpectrumTests
    {
        static readonly Vector3d[] Colours =
        {
            new Vector3d(1, 1, 1), new Vector3d(0.2, 0.5, 0.9), new Vector3d(1, 0.6, 0.1), new Vector3d(0.8, 0.1, 0.15),
            new Vector3d(0.1, 0.7, 0.2), new Vector3d(0.1, 0.1, 0.8), new Vector3d(3, 2, 1), new Vector3d(1e-4, 2e-4, 3e-4),
        };

        [Test]
        public void UnshiftedColour_ComesBackUnchanged()
        {
            // The table is interpolated between fitted grid points; 0.5 % of the brightest channel is below what
            // anyone can see in a sky colour.
            foreach (var c in Colours)
            {
                var back = RgbSpectrum.Shift(c, 1);
                double tolerance = 5e-3 * Math.Max(c.X, Math.Max(c.Y, c.Z));
                Assert.AreEqual(c.X, back.X, tolerance, $"{c}");
                Assert.AreEqual(c.Y, back.Y, tolerance, $"{c}");
                Assert.AreEqual(c.Z, back.Z, tolerance, $"{c}");
            }
        }

        [Test]
        public void Black_StaysBlack()
        {
            var shifted = RgbSpectrum.Shift(new Vector3d(0, 0, 0), 2);
            Assert.AreEqual(0, shifted.X + shifted.Y + shifted.Z);
        }

        // A star's colour is upsampled to its own blackbody, and a blackbody at T seen with shift g is one at gT:
        // the shifted colour must be the hotter or cooler blackbody's, brightness change included.
        [TestCase(3000, 0.8)]
        [TestCase(3000, 1.5)]
        [TestCase(5800, 0.5)]
        [TestCase(5800, 2.0)]
        [TestCase(12000, 0.7)]
        [TestCase(12000, 1.25)]
        public void StarColour_ShiftsLikeItsBlackbody(double kelvin, double g)
        {
            // Unit luminance, so the expectation is the blackbody's luminance ratio.
            double y = Blackbody.Xyz(kelvin).Y;
            var shifted = RgbSpectrum.Shift(Blackbody.LinearSrgb(kelvin) / y, g);
            var expected = Blackbody.LinearSrgb(g * kelvin) / y;

            // 1 % of the brightest channel: the CPU blackbody integrates at 1 nm, the spectrum at 10 nm.
            double tolerance = 0.01 * Math.Max(expected.X, Math.Max(expected.Y, expected.Z));
            Assert.AreEqual(expected.X, shifted.X, tolerance);
            Assert.AreEqual(expected.Y, shifted.Y, tolerance);
            Assert.AreEqual(expected.Z, shifted.Z, tolerance);
        }

        [Test]
        public void Blueshift_MakesAnyColourBluer()
        {
            foreach (var c in Colours)
            {
                var red = RgbSpectrum.Shift(c, 0.9);
                var blue = RgbSpectrum.Shift(c, 1.1);
                Assert.Greater(blue.Z / blue.X, red.Z / red.X, $"{c}");
            }
        }
    }
}
