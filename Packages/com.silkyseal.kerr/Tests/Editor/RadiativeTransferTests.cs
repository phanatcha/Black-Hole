using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class RadiativeTransferTests
    {
        [TestCase(1, 0.3)]
        [TestCase(7, 0.3)]
        [TestCase(100, 4.0)]
        [TestCase(3, 1e-9)]
        public void UniformSlab_GivesTheTextbookIntensityForAnyStepCount(int steps, double opticalDepth)
        {
            // I = S (1 − e^−τ) with S = j / α; e^−τ of whatever lies behind still shows.
            const double length = 2, emission = 5;
            double absorption = opticalDepth / length, intensity = 0, transmittance = 1;
            for (int i = 0; i < steps; i++)
                RadiativeTransfer.Step(ref intensity, ref transmittance, emission, absorption, length / steps);

            double fraction = opticalDepth > 1e-4 ? (1 - Math.Exp(-opticalDepth)) / opticalDepth : 1 - opticalDepth / 2;
            double expected = emission * length * fraction;
            Assert.AreEqual(expected, intensity, 1e-12 * expected);
            Assert.AreEqual(Math.Exp(-opticalDepth), transmittance, 1e-12);
        }

        [TestCase(2000.0)]
        [TestCase(3000.0)]
        [TestCase(6500.0)]
        [TestCase(15000.0)]
        public void VisibleBand_ThreeSamplesOfABlackbodyGiveItsColour(double kelvin)
        {
            var nu = VisibleBand.Frequencies;
            var samples = new Vector3d(Synchrotron.PlanckRadiance(nu.X, kelvin), Synchrotron.PlanckRadiance(nu.Y, kelvin),
                                       Synchrotron.PlanckRadiance(nu.Z, kelvin));
            var actual = VisibleBand.LinearSrgb(samples);
            var expected = Blackbody.LinearSrgb(kelvin); // in gamut from 2000 K up, so nothing is clipped
            double luminance = Blackbody.Xyz(kelvin).Y;
            // Three samples stand in for the whole spectrum; the steep Wien side of 2000 K is the worst case, near 1 %.
            Assert.AreEqual(expected.X, actual.X, 0.015 * luminance);
            Assert.AreEqual(expected.Y, actual.Y, 0.015 * luminance);
            Assert.AreEqual(expected.Z, actual.Z, 0.015 * luminance);
        }
    }
}
