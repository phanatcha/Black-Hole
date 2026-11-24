using System.IO;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class StarCatalogueTests
    {
        [Test]
        public void Sun_GivesDaylightIlluminance()
        {
            // V = −26.74 is the Sun; about 127 klux reach the top of the atmosphere.
            Assert.AreEqual(127e3, StarCatalogue.Lux(-26.74), 127e3 * 0.02);
        }

        [Test]
        public void SolarColourIndex_GivesSolarTemperature()
        {
            // B − V = 0.65 against the IAU nominal solar effective temperature.
            Assert.AreEqual(5772, StarCatalogue.Kelvin(0.65), 60);
        }

        [Test]
        public void Direction_MapsIcrsOntoUnityAxes()
        {
            AssertClose(new Vector3d(1, 0, 0), StarCatalogue.Direction(0, 0));
            AssertClose(new Vector3d(0, 0, 1), StarCatalogue.Direction(90, 0));
            AssertClose(new Vector3d(0, 1, 0), StarCatalogue.Direction(123, 90));
        }

        [Test]
        public void Pack_KeepsEveryStarWithAPositionAndAMagnitude()
        {
            // The layout of the VizieR I/239 TSV export: comments, column names, units, dashes, then rows.
            const string tsv =
                "#RESOURCE=yCat_1239\n" +
                "\n" +
                "HIP\tRAICRS\tDEICRS\tVmag\tB-V\n" +
                " \tdeg\tdeg\tmag\tmag\n" +
                "------\t------------\t------------\t-----\t------\n" +
                " 32349\t101.28715533\t-16.71611586\t-1.44\t 0.009\n" +
                "   835\t002.55428058\t+25.52675303\t 8.91\t      \n" +
                "   836\t002.55874804\t+60.02604910\t     \t 1.311\n" +
                "   837\t            \t            \t 8.95\t 1.104\n";

            var stars = StarCatalogue.Unpack(StarCatalogue.Pack(new StringReader(tsv)));

            Assert.AreEqual(2, stars.Length);
            AssertClose(StarCatalogue.Direction(101.28715533, -16.71611586), stars[0].Direction);
            Assert.AreEqual(StarCatalogue.Lux(-1.44), stars[0].Lux, StarCatalogue.Lux(-1.44) * 1e-6);
            Assert.AreEqual(StarCatalogue.Kelvin(0.009), stars[0].Kelvin, 1);
            Assert.AreEqual(StarCatalogue.DefaultKelvin, stars[1].Kelvin);
        }

        static void AssertClose(Vector3d expected, Vector3d actual)
        {
            Assert.AreEqual(0, (expected - actual).Length, 1e-6, $"expected {expected}, got {actual}");
        }
    }
}
