using System;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class BlackbodyTests
    {
        // Reference chart: the Planckian locus, Kim et al. (2002) cubic fits, good to ~1e-3 in xy over 1667–25000 K.
        static (double x, double y) PlanckianLocus(double t)
        {
            double x = t < 4000
                ? -0.2661239e9 / (t * t * t) - 0.2343589e6 / (t * t) + 0.8776956e3 / t + 0.179910
                : -3.0258469e9 / (t * t * t) + 2.1070379e6 / (t * t) + 0.2226347e3 / t + 0.240390;
            double y = t < 2222 ? -1.1063814 * x * x * x - 1.34811020 * x * x + 2.18555832 * x - 0.20219683
                     : t < 4000 ? -0.9549476 * x * x * x - 1.37418593 * x * x + 2.09137015 * x - 0.16748867
                     : 3.0817580 * x * x * x - 5.87338670 * x * x + 3.75112997 * x - 0.37001483;
            return (x, y);
        }

        // The analytic CIE fit drifts in the far red; 0.005 is about one just-noticeable difference.
        [TestCase(1700, 0.005)]
        [TestCase(2000, 0.005)]
        [TestCase(2856, 0.002)]
        [TestCase(4000, 0.002)]
        [TestCase(5000, 0.002)]
        [TestCase(6500, 0.002)]
        [TestCase(10000, 0.002)]
        [TestCase(20000, 0.002)]
        public void Chromaticity_MatchesPlanckianLocus(double kelvin, double tolerance)
        {
            var xyz = Blackbody.Xyz(kelvin);
            double sum = xyz.X + xyz.Y + xyz.Z;
            var (x, y) = PlanckianLocus(kelvin);
            Assert.Less(Math.Sqrt(Sq(xyz.X / sum - x) + Sq(xyz.Y / sum - y)), tolerance);
        }

        [Test]
        public void Chromaticity_MatchesIlluminantA()
        {
            // CIE Illuminant A is a 2856 K blackbody: x = 0.44757, y = 0.40745.
            var xyz = Blackbody.Xyz(2856);
            double sum = xyz.X + xyz.Y + xyz.Z;
            Assert.Less(Math.Sqrt(Sq(xyz.X / sum - 0.44757) + Sq(xyz.Y / sum - 0.40745)), 0.002);
        }

        [Test]
        public void Luminance_GivesPeakEfficacyNear95LumensPerWatt()
        {
            // Luminous exitance πY over radiant exitance σT⁴; a blackbody peaks at ~95 lm/W near 6600 K.
            const double t = 6600, sigma = 5.670374419e-8;
            Assert.AreEqual(95, Math.PI * Blackbody.Xyz(t).Y / (sigma * t * t * t * t), 2);
        }

        [Test]
        public void LinearSrgb_IsNearWhite_At6500K()
        {
            var c = Blackbody.LinearSrgb(6500);
            double max = Math.Max(c.X, Math.Max(c.Y, c.Z)), min = Math.Min(c.X, Math.Min(c.Y, c.Z));
            Assert.Less(max / min, 1.1);
        }

        [Test]
        public void LinearSrgb_ClipsOutOfGamutReds([Values(800.0, 1000.0, 1500.0)] double kelvin)
        {
            var c = Blackbody.LinearSrgb(kelvin);
            Assert.That(c.X > 0 && c.Y >= 0 && c.Z >= 0, $"got {c}");
        }

        static double Sq(double v) => v * v;
    }
}
